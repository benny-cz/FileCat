using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

/// <summary>Read-only private-store inspection with selection and actual-work ownership.</summary>
internal sealed class SavedAnalysisInspectionPanel : StackPanel
{
    private readonly Button _inspect = new() { Content = "Inspect selected analysis", IsEnabled = false };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Func<AnalysisSavedEntry?> _selection;
    private readonly Func<Guid, CancellationToken, Task<OwnedAnalysisInventoryDetails>> _read;
    private readonly Action<Exception> _recordCloseFailure;
    private readonly AnalysisViewWork _work = new();
    private readonly CancellationToken _lifetime;
    private readonly string _directory;
    private CancellationTokenSource? _request;
    private AnalysisSavedEntry? _lastSelection;
    private bool _busy, _closed;
    private int _generation;

    internal SavedAnalysisInspectionPanel(string directory, Func<AnalysisSavedEntry?> selection,
        Func<Guid, CancellationToken, Task<OwnedAnalysisInventoryDetails>> read, CancellationToken lifetime, Action<Exception> recordCloseFailure)
    {
        _directory = directory; _selection = selection; _read = read; _lifetime = lifetime; _recordCloseFailure = recordCloseFailure;
        Spacing = 6; Children.Add(_inspect); Children.Add(_details);
        AutomationProperties.SetAutomationId(_details, "Saved analysis details"); AutomationProperties.SetLiveSetting(_details, AutomationLiveSetting.Polite);
        _inspect.Click += async (_, _) => await InspectAsync();
    }

    internal void SelectionChanged()
    {
        if (_closed) return;
        var selection = _selection();
        if (Equals(selection, _lastSelection)) { _inspect.IsEnabled = !_busy && selection is not null; return; }
        _lastSelection = selection;
        ++_generation; _request?.Cancel(); _details.Text = "";
        _inspect.IsEnabled = !_busy && selection is not null;
    }
    private async Task InspectAsync()
    {
        if (_closed || _busy || _selection() is not { } entry) return;
        using var actual = _work.Enter(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _request = cancel; _busy = true; _inspect.IsEnabled = false; int generation = _generation;
        _details.Text = "Checking the saved file's format and checksum…";
        try
        {
            var value = await _read(entry.InventoryId, cancel.Token);
            if (_closed || generation != _generation) return;
            _details.Text = "Name: " + AnalysisNames.Display(value.Entry.Name, 240) +
                "\nLocation: " + AnalysisNames.Display(Path.Combine(_directory, value.Entry.Name), 4096) +
                "\nSize: " + DiskSpaceViewModel.Bytes((UInt128)value.Bytes) + " (" + value.Bytes.ToString("N0") + " bytes)" +
                "\nObserved: " + value.StartedUtc.ToString("u") + " – " + value.ObservedUtc.ToString("u") +
                "\n" + (value.Entry.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "Complete under captured policy." : "Partial captured observations.") +
                "\nFile checksum and record format verified. These observations have not been refreshed." +
                (value.Taint.HasFlag(AnalysisTaint.PrivilegedDerived) ? "\nContains sensitive observations gathered with administrator access." : "");
        }
        catch (OperationCanceledException) { if (!_closed && generation == _generation) _details.Text = "Inspection canceled."; }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException or OverflowException)
        {
            _recordCloseFailure(error);
            if (!_closed && generation == _generation) _details.Text = "Inspection unavailable: " + AnalysisNames.Display(error.Message, 400);
        }
        finally
        {
            _request = null; _busy = false;
            if (!_closed) _inspect.IsEnabled = _selection() is not null;
        }
    }
    internal Task CloseAsync()
    {
        _closed = true; ++_generation; _request?.Cancel(); _inspect.IsEnabled = false;
        return _work.CloseAsync();
    }
}
