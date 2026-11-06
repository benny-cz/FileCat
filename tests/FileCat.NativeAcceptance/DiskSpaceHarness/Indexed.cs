using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Analysis;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private AnalysisIndexedAcquisition? _indexedAcquisition;
    private bool _indexedProbeFinished;
    private string? _indexedProbeError;
    private object? _indexedProbe;
    private readonly CancellationTokenSource _indexedLifetime = new();
    private Task _indexedProbeTask = Task.CompletedTask;
    private async Task IndexedProbeAsync()
    {
        IAnalysisBackend? source = null; AnalysisDatasetLease? lease = null;
        try
        {
            var services = _services!; var window = _window!;
            if (OperatingSystem.IsWindows()) WindowsGuestInput.DesktopGuard();
            _indexedAcquisition = await AnalysisIndexedAcquisition.PrepareAsync(services, _indexedLifetime.Token);
            var profile = _indexedAcquisition.Profile ?? throw new IOException("The native indexed fixture did not retain a private profile identity.");
            source = await AnalysisIo.Run(services.Io, PathUtil.GetDeviceKey(_fixture), _ => OperatingSystem.IsWindows()
                ? (IAnalysisBackend)new WindowsAnalysisBackend(_fixture) : new UnixAnalysisBackend(_fixture), _indexedLifetime.Token);
            _indexedLifetime.Token.ThrowIfCancellationRequested();
            var session = _indexedAcquisition.Start([source]); source = null;
            using var stop = _indexedLifetime.Token.Register(session.Stop);
            await session.Completion.WaitAsync(TimeSpan.FromMinutes(3));
            if (session.Snapshot().Phase != AnalysisPhase.Finished || session.SourceCloseFailures != 0) throw new IOException("The native indexed acquisition did not finish with source closure.");
            lease = await session.AcquireDatasetAsync() ?? throw new IOException("The native indexed dataset was not published.");
            var descriptor = lease.Descriptor;
            var explorer = await window.AddIndexedSessionAsync(lease, AnalysisMetric.OnDisk);
            await _indexedAcquisition.DisposeAsync(); _indexedAcquisition = null;
            if (Program.IndexedInventoryProbe) await IndexedInventoryProbeAsync(lease);
            await lease.DisposeAsync(); lease = null;
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(control => AutomationProperties.GetName(control) == "Indexed analysis entries");
            var root = list.Items.OfType<AnalysisHierarchyRow>().Single(); list.SelectedItem = root;
            window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Explore selected folder")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ReadyAsync(() => explorer.Page?.ParentId == root.Path.Id && explorer.PendingRequests == 0);
            var sort = window.GetVisualDescendants().OfType<ComboBox>().Single(control => AutomationProperties.GetName(control) == "Indexed table order");
            sort.SelectedIndex = (int)AnalysisHierarchySort.Name;
            var descending = window.GetVisualDescendants().OfType<CheckBox>().Single(control => AutomationProperties.GetName(control) == "Indexed descending table order");
            descending.IsChecked = false;
            await ReadyAsync(() => explorer.Page is { Sort: AnalysisHierarchySort.Name, Descending: false } && explorer.PendingRequests == 0);
            if (explorer.LogicalTotals.Files != (ulong)_originals.Count) throw new InvalidDataException("Indexed native file-path totals differ from the generated fixture.");
            var rows = list.Items.OfType<AnalysisHierarchyRow>().ToArray();
            if (!rows.Select(row => row.Path.Name).SequenceEqual(rows.Select(row => row.Path.Name).Order(StringComparer.OrdinalIgnoreCase))) throw new InvalidDataException("The displayed indexed native name order is inconsistent.");
            window.GetVisualDescendants().OfType<ComboBox>().Single(control => AutomationProperties.GetName(control) == "Indexed chart mode").SelectedIndex = 2;
            var navigationLink = rows.FirstOrDefault(row => row.Path.Kind == AnalysisKind.Link);
            string? namespaceEvidence = null;
            if (navigationLink is not null)
            {
                list.SelectedItem = navigationLink; list.ScrollIntoView(navigationLink);
                await ReadyAsync(() => window.GetVisualDescendants().OfType<TextBox>().Any(control => AutomationProperties.GetName(control) == "Captured metadata inspector" && control.Text?.Contains("target was not followed or measured", StringComparison.Ordinal) == true));
                var row = window.GetVisualDescendants().OfType<Grid>().Single(control => AutomationProperties.GetName(control)?.StartsWith(navigationLink.Path.Name + ";", StringComparison.Ordinal) == true);
                namespaceEvidence = AutomationProperties.GetName(row);
                if (!namespaceEvidence!.Contains("Not counted", StringComparison.Ordinal) || !namespaceEvidence.Contains("Link not followed", StringComparison.Ordinal)) throw new InvalidDataException("The native indexed namespace row presents a measured-file state.");
            }
            _indexedProbe = new
            {
                paths = descriptor.Paths, objects = descriptor.Objects, matchingPaths = explorer.LogicalTotals.Files,
                known = explorer.Totals.Known.ToString(), unverified = explorer.Totals.IdentityUnverified.ToString(),
                scope = explorer.Page!.ParentId, order = explorer.Page.Sort.ToString(), explorer.Page.Descending,
                displayedIds = rows.Select(row => row.Path.Id).ToArray(), displayedNames = rows.Select(row => row.Path.Name).ToArray(),
                view = "Table", namespaceInspected = navigationLink is not null, namespaceEvidence,
                sourceCloseFailures = session.SourceCloseFailures, acquisitionOwnerClosed = true,
                privateStateSha256 = Hash(services.Paths.AnalysisPrivateStateFile), profile = profile.ProfileId,
                scratch = services.Paths.AnalysisScratchDirectory, nativeDesktopInputQualified = false
            };
            if (Program.IndexedInventoryProbe) await RenderInventoryWindowAsync();
            else if (OperatingSystem.IsWindows())
            {
                WindowsGuestInput.ActivateFromDesktop(window.TryGetPlatformHandle()?.Handle ?? 0);
                await Task.Delay(600); WindowsCapture("indexed-probe");
            }
            _indexedProbeFinished = true; WriteReport("indexed-probe-finished");
        }
        catch (Exception error) { _indexedProbeError = error.GetType().Name + ": " + error.Message; WriteReport("indexed-probe-failed"); }
        finally
        {
            if (lease is not null) await lease.DisposeAsync();
            if (source is not null) await AnalysisIo.ReleaseAsync(_services!.Io, source);
        }
    }
    private async Task ReadyAsync(Func<bool> condition)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(45);
        while (_window?.IsVisible == true && DateTimeOffset.UtcNow < until)
        {
            if (condition()) { await Task.Delay(100); if (condition()) return; }
            await Task.Delay(25);
        }
        throw new TimeoutException("The native indexed window did not publish the requested captured view.");
    }
}
