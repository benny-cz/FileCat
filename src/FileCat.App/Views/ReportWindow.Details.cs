using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using FileCat.Core.Records;

namespace FileCat.App.Views;

/// <summary>
/// The details window's optional "NTFS compression for this item…" link (I-DETAILS-C). <see cref="Unavailable"/> says why the
/// main window cannot host a review now, or null when it can. <see cref="Open"/> runs one review there for the item the report
/// was opened for, given whether it is a folder and this report's lifetime, and returns once that review has closed.
/// </summary>
public sealed record DetailsCompressionLink(Func<string?> Unavailable, Func<bool, CancellationToken, Task> Open);

public sealed partial class ReportWindow
{
    private FileDetailsReader? _detailsReader;
    private FileDetailsObservation? _detailsObservation;
    private FileDetailsTargetCheck? _targetCheck;
    private CancellationTokenSource? _checkingTarget;
    private FileDetailsTier _detailsTier;
    private FileDetailsTier _detailsDisplayedTier;
    private readonly Button _tier = new() { Content = "Read full record" };
    private string? _detailsNotice;
    private Action? _openExtraData;
    private readonly Button _extraData = new() { Content = OperatingSystem.IsWindows() ? "Open streams and attributes" : "Open attributes" };
    private readonly Button _copyTarget = new() { Content = "Copy stored target", IsVisible = false };
    private readonly Button _navigateTarget = new() { Content = "Open target location", IsVisible = false };
    private Func<string, CancellationToken, Task<FileDetailsTargetCheck>>? _openTarget;
    private DetailsCompressionLink? _compression;
    private readonly Button _compress = new() { Content = "NTFS compression for this item…", IsVisible = false };
    private Task? _compressing;
    private const string CompressHelp = "Opens a review of this item's NTFS compression in the main window: its current state, " +
        "the change to make and the job that makes it. The item is read again there, and nothing changes until you approve it.";

    public ReportWindow(string subject, FileDetailsReader reader, Action? openExtraData = null,
        Func<string, CancellationToken, Task<FileDetailsTargetCheck>>? openTarget = null, DetailsCompressionLink? compression = null)
        : this("File-system details", subject, _ => throw new InvalidOperationException("Details require the owned metadata reader."))
    {
        _detailsReader = reader;
        _openExtraData = openExtraData;
        _openTarget = openTarget;
        _compression = compression;
        Width = 840; Height = 640; MinWidth = 240; MinHeight = 280;
        // The inherited toolbar and spoken-line controls can exceed a short report's height
        // after wrapping or system text scaling. Keep a report viewport and let focus reveal
        // controls inside their own scroll area.
        void FitChrome()
        {
            double height = ClientSize.Height > 0 ? ClientSize.Height : Height;
            double limit = height * 0.58;
            if (_copyNotice.IsVisible) limit = Math.Min(limit, height - 250);
            _chromeScroll.MaxHeight = Math.Max(50, limit);
        }
        FitChrome();
        SizeChanged += (_, _) => FitChrome();
        _copyNotice.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) FitChrome(); };
        _view.Wrap = true; _wrap.IsChecked = true;
        _search.MinWidth = 100;
        _actions.Children.Add(_tier);
        if (openExtraData is not null) _actions.Children.Add(_extraData);
        _actions.Children.Add(_copyTarget);
        _actions.Children.Add(_navigateTarget);
        _actions.Children.Add(_compress);
        _copyTarget.Click += async (_, _) => await CopyStoredTargetAsync();
        _navigateTarget.Click += async (_, _) => await NavigateTargetAsync();
        _compress.Click += async (_, _) => await OpenCompressionReviewAsync();
        _tier.Click += async (_, _) => await SwitchDetailsTierAsync();
        _extraData.Click += (_, _) => _openExtraData?.Invoke();
        AutomationProperties.SetHelpText(_extraData, "Opens the captured item's names, kinds and lengths in a new workspace tab. Values are read only through an explicit action; Windows extended attributes require separate admission.");
        ToolTip.SetShowOnDisabled(_compress, true);
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        SetDetailsActions();
        // The main window's state (a dialog open there, closing) decides whether the link can open a review: look again
        // whenever the user comes back to this window.
        Activated += (_, _) => { if (!_closing.IsCancellationRequested) SetDetailsActions(); };
        Closed += (_, _) => { _openExtraData = null; _openTarget = null; _compression = null; };
    }

    private bool CanPublishDetails => _detailsReader is null || _detailsObservation is not null;

    private void SetDetailsActions()
    {
        bool ready = CanPublishDetails;
        _copyAll.IsEnabled = _copyVisible.IsEnabled = _copyDisplayed.IsEnabled = _save.IsEnabled = ready;
        _extraData.IsEnabled = _detailsObservation is not null;
        _copyTarget.IsVisible = _detailsObservation?.Summary.Target.State == FactState.Known;
        bool network = false;
        bool qualified = _detailsObservation is { Summary.Identity.State: FactState.Known } observation &&
            FileDetailsTargets.TryResolve(observation.Summary, OperatingSystem.IsWindows(), out _, out network);
        _navigateTarget.IsVisible = qualified && _openTarget is not null;
        _navigateTarget.IsEnabled = _checkingTarget is null;
        _navigateTarget.Content = network ? "Open target location (network)" : "Open target location";
        _tier.Content = _detailsTier == FileDetailsTier.Full ? "Show summary"
            : _detailsObservation?.Full is not null ? "Show full record" : "Read full record";
        // Shown wherever the build offers compression; a reason says why it cannot open a review now.
        _compress.IsVisible = _compression is not null && _detailsObservation is not null;
        string? blocked = CompressionBlocked();
        _compress.IsEnabled = blocked is null;
        ToolTip.SetTip(_compress, blocked ?? CompressHelp);
        AutomationProperties.SetHelpText(_compress, blocked ?? CompressHelp);
    }

    /// <summary>
    /// Why the compression link cannot open a review now, or null. The observation only says what it is plainly not for (not
    /// a file or folder, not on NTFS); the review reads the item afresh and decides, as it does for a panel's selection.
    /// </summary>
    private string? CompressionBlocked()
    {
        if (_compression is not { } link || _detailsObservation is not { } observation) return "The item has not been read yet.";
        if (_compressing is not null) return "A compression review for this item is open in the main window.";
        var summary = observation.Summary;
        if (summary.Kind is not (FileObjectKind.File or FileObjectKind.Directory)) return "NTFS compression applies to files and folders.";
        if (summary.FileSystem is { State: FactState.Known, Value: { } format } && !format.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
            return $"NTFS compression needs an NTFS drive; this item is on {format}.";
        return link.Unavailable();
    }

    /// <summary>
    /// One review at a time, for the item this report was opened for. It belongs to this report until it is actionable (closing
    /// the report before then closes it); from then on it belongs to the main window, and closing the report changes nothing.
    /// </summary>
    private async Task OpenCompressionReviewAsync()
    {
        if (_closing.IsCancellationRequested || _compression is not { } link || _detailsObservation is not { } observation ||
            CompressionBlocked() is not null) return;
        var review = link.Open(observation.Summary.Kind == FileObjectKind.Directory, _closing.Token);
        _compressing = review;
        SetDetailsActions();
        try { await review; }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_compressing, review)) _compressing = null;
            if (!_closing.IsCancellationRequested) SetDetailsActions();
        }
    }

    internal Task OpenCompressionReviewForTestAsync() => OpenCompressionReviewAsync();
    internal Button CompressionLinkForTest => _compress;

    private async Task SwitchDetailsTierAsync()
    {
        if (_detailsReader is null || _closing.IsCancellationRequested) return;
        _reading?.Cancel();
        _checkingTarget?.Cancel();
        _copying?.Cancel(); _saving?.Cancel();
        _detailsReader.Cancel();
        _detailsTier = _detailsTier == FileDetailsTier.Summary ? FileDetailsTier.Full : FileDetailsTier.Summary;
        if (_detailsObservation is not null && (_detailsTier == FileDetailsTier.Summary || _detailsObservation.Full is not null))
        {
            _detailsDisplayedTier = _detailsTier;
            _detailsNotice = null;
            PublishDetails();
        }
        else await (Reading = ReadDetailsCoreAsync(_detailsTier));
        SetDetailsActions();
    }

    private async Task ReadDetailsCoreAsync(FileDetailsTier? requestedTier = null)
    {
        if (_detailsReader is not { } reader || _closing.IsCancellationRequested) return;
        _reading?.Cancel();
        _checkingTarget?.Cancel();
        _copying?.Cancel(); _saving?.Cancel();
        using var cts = _reading = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        var ct = cts.Token;
        var requested = requestedTier ?? (_detailsObservation is null ? _detailsTier : _detailsDisplayedTier);
        _detailsTier = requested;
        _detailsNotice = _detailsObservation is null ? "Reading…" : "Previous observation · reading…";
        if (_detailsObservation is null)
        {
            Show("Reading file-system metadata…");
            UpdateDetailsStatus(announce: false);
        }
        else PublishDetails(announceStatus: false);
        SetDetailsActions();
        try
        {
            var observation = await reader.ReadAsync(requested, ct);
            if (ct.IsCancellationRequested || _closing.IsCancellationRequested || !ReferenceEquals(_reading, cts) || _detailsTier != requested) return;
            if (observation.Full is { ContentKind: not ReportContentKind.MetadataOnly }) throw new NotSupportedException("Unqualified report content cannot be displayed or exported through this route.");
            if (_detailsObservation?.Summary.Identity is { State: FactState.Known } previous &&
                observation.Summary.Identity is { State: FactState.Known } current && previous.Value != current.Value)
            {
                var changed = observation.Summary with { Notes = observation.Summary.Notes.Add("Changed object: identity differs from the previous observation. Closed handles do not establish historical continuity.") };
                observation = observation with { Summary = changed, Full = observation.Full is { } paired ? paired with { Summary = changed } : null };
            }
            _detailsObservation = observation;
            _targetCheck = null;
            _detailsDisplayedTier = requested;
            _detailsNotice = null;
            PublishDetails();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || _closing.IsCancellationRequested)
        {
            if (!ct.IsCancellationRequested && !_closing.IsCancellationRequested) _detailsNotice = "Read canceled";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            if (!ct.IsCancellationRequested && ReferenceEquals(_reading, cts))
            {
                _detailsTier = _detailsDisplayedTier;
                _detailsNotice = $"Read failed: {ex.Message}";
                if (_detailsObservation is null) { Show(_detailsNotice); _status.Text = _detailsNotice; }
                else PublishDetails();
            }
        }
        finally
        {
            if (ReferenceEquals(_reading, cts)) _reading = null;
            if (!_closing.IsCancellationRequested) SetDetailsActions();
        }
    }

    private void PublishDetails(bool announceStatus = true)
    {
        if (_detailsObservation is not { } observation) return;
        string text = _detailsDisplayedTier == FileDetailsTier.Full && observation.Full is { } full
            ? full.ToText(_targetCheck) : observation.Summary.ToReport(_targetCheck).ToText();
        if (_detailsNotice is { } notice) text = notice + " · showing observation " + observation.Summary.ObservedAt.ToString("O") + Environment.NewLine + text;
        // All native acquisition is bounded first. This also bounds newly assembled viewer text.
        const int MaxChars = 2 * 1024 * 1024 - 128;
        if (text.Length > MaxChars) text = text[..MaxChars] + "\nFurther detail omitted: the report text limit was reached.";
        long top = _view.TopOffset;
        Show(text);
        if (top > 0 && top < System.Text.Encoding.UTF8.GetByteCount(text)) _view.ScrollToOffset(top);
        _readAt = observation.Summary.ObservedAt.LocalDateTime;
        UpdateDetailsStatus(announceStatus);
    }

    private void UpdateDetailsStatus(bool announce = true)
    {
        // The report itself exposes the pending read. Keep the status live event for the outcome,
        // since a fast read can otherwise lose its completion announcement behind the pending event.
        if (!announce) AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Off);
        string? notice = _detailsNotice;
        if (notice is null && _targetCheck is { Kind.State: not FactState.Known } target)
            notice = "Target could not be opened: " + target.Kind.Describe();
        _status.Text = _detailsObservation is { } observation
            ? $"{notice}{(notice is null ? "" : " · ")}{_detailsDisplayedTier} · observation {observation.Summary.ObservedAt.LocalDateTime:T} · F5 reads this tier again"
            : notice ?? "No observation";
        if (!announce) AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
    }

    internal Task SwitchDetailsTierForTestAsync() => SwitchDetailsTierAsync();
    internal Task RefreshDetailsForTestAsync() => ReadAsync();

    private async Task CopyStoredTargetAsync()
    {
        if (_closing.IsCancellationRequested || _detailsObservation?.Summary.Target.Value is not { } target || Clipboard is not { } clipboard) return;
        _copying?.Cancel();
        using var cts = _copying = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        try
        {
            await _copyWrites.WaitAsync(cts.Token);
            try
            {
                cts.Token.ThrowIfCancellationRequested();
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, target.SubstituteName);
            }
            finally { _copyWrites.Release(); }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        { if (!_closing.IsCancellationRequested) _copyNotice.Show("Copy could not finish: " + ex.Message); }
        finally { if (ReferenceEquals(_copying, cts)) _copying = null; }
    }

    private async Task NavigateTargetAsync()
    {
        if (_checkingTarget is not null || _closing.IsCancellationRequested || _detailsReader is not { } reader ||
            _detailsObservation is not { Summary.Identity.State: FactState.Known } captured || _openTarget is not { } navigate ||
            !FileDetailsTargets.TryResolve(captured.Summary, OperatingSystem.IsWindows(), out string path, out _)) return;
        _reading?.Cancel();
        using var check = _checkingTarget = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        var ct = check.Token;
        SetDetailsActions();
        try
        {
            var fresh = await reader.ReadAsync(FileDetailsTier.Summary, ct);
            if (ct.IsCancellationRequested || !ReferenceEquals(_detailsObservation, captured)) return;
            if (fresh.Summary.Target != captured.Summary.Target || fresh.Summary.Identity != captured.Summary.Identity)
            {
                _copying?.Cancel();
                _saving?.Cancel();
                _detailsObservation = fresh;
                _targetCheck = null;
                _detailsTier = _detailsDisplayedTier = FileDetailsTier.Summary;
                _detailsNotice = "Stored target or source identity changed; review the refreshed details before navigating";
                PublishDetails();
                return;
            }
            var target = await navigate(path, ct);
            if (ct.IsCancellationRequested || !ReferenceEquals(_detailsObservation, captured)) return;
            _copying?.Cancel();
            _saving?.Cancel();
            _targetCheck = target;
            _detailsNotice = null;
            PublishDetails();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (!ct.IsCancellationRequested && ReferenceEquals(_detailsObservation, captured))
            {
                _detailsNotice = "Target could not be opened: " + ex.Message;
                UpdateDetailsStatus();
            }
        }
        finally
        {
            _checkingTarget = null;
            if (!_closing.IsCancellationRequested) SetDetailsActions();
        }
    }
}
