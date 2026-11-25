using Avalonia.Automation;
using Avalonia.Controls;
using FileCat.App.Services;
using FileCat.Core.Records;

namespace FileCat.App.Views;

/// <summary>
/// DEC-05: the item's full file-system record (D-56) in the details window. The summary and the metadata-only full record
/// stay first; the record is read only through its own action, on the item's device through the details reader (off the UI
/// thread, admitted, one running and one pending request, retired by leaving, refreshing or closing). It is shown as the
/// record writes itself, with a spoken form for every heading, warning, field and table row, and it is copied or saved only
/// while it is bound to one object whose values cannot be decrypted EFS content.
/// </summary>
public sealed partial class ReportWindow
{
    private const string RecordKind = "File-system record";
    private const string ReadRecordLabel = "Read file-system record";
    private const string ShowRecordLabel = "Show file-system record";
    /// <summary>The placeholder notices while no record has been read yet (compared by reference).</summary>
    private const string ReadingRecord = "Reading the file-system record…";
    private const string WaitingRecord = "Waiting for another file-system record read to finish (FileCat reads one at a time)…";
    private const string RecordHelp = "Reads the item's full file-system record, beyond the metadata of Read full record: its IDs, " +
        "every time with timestamp checks, journal entries, links, object ID, reparse and cloud data, clusters, streams and " +
        "attributes with what they say, and security. FileCat run as administrator (Windows) or root (Linux, macOS) adds the raw " +
        "on-disk records. The record can hold file data, so it is copied or saved only while it describes one object and holds " +
        "no values decrypted from EFS.";

    private readonly Button _recordButton = new() { Content = ReadRecordLabel };
    private bool _recordShown;
    private FileRecordObservation? _record;
    private string? _recordNotice;
    /// <summary>What the spoken line says for each line of the text shown (null: as shown).</summary>
    private string?[]? _spokenForms;

    /// <summary>The record view shows a placeholder, not a record: nothing there is an observation to copy or save.</summary>
    private bool RecordPlaceholder => ReferenceEquals(_recordNotice, ReadingRecord) || ReferenceEquals(_recordNotice, WaitingRecord);

    /// <summary>The record's refusal while it is shown and read, said where Copy or Save was asked for; null otherwise.</summary>
    private string? RecordOutputRefusal => _detailsReader is not null && _recordShown && !_closing.IsCancellationRequested &&
        !RecordPlaceholder && _record?.OutputRefusal() is { } refusal
            ? "Copy and Save are unavailable for this record: " + refusal : null;

    private void SetRecordAction()
    {
        string help;
        if (_recordShown)
        {
            _recordButton.Content = _detailsDisplayedTier == FileDetailsTier.Full ? "Show full record" : "Show summary";
            help = (_detailsDisplayedTier == FileDetailsTier.Full ? "Returns to the metadata-only full record" : "Returns to the metadata summary") +
                (_reading is not null ? "; the record being read is stopped." : ".");
            _recordButton.IsEnabled = true;
        }
        else
        {
            _recordButton.Content = _record is null ? ReadRecordLabel : ShowRecordLabel;
            help = RecordHelp;
            _recordButton.IsEnabled = _detailsObservation is not null;
        }
        AutomationProperties.SetHelpText(_recordButton, help);
        ToolTip.SetTip(_recordButton, help);
    }

    /// <summary>The view's name for the reader, the title and Save as: details or the record.</summary>
    private void SetViewNames()
    {
        string kind = _recordShown ? RecordKind : _kind;
        AutomationProperties.SetName(_view, kind);
        string name = Path.GetFileName(_subject.TrimEnd('\\', '/'));
        Title = $"{(name.Length > 0 ? name : _subject)} · {kind} — FileCat";
    }

    private async Task ToggleRecordAsync()
    {
        if (_detailsReader is not { } reader || _closing.IsCancellationRequested) return;
        if (_recordShown)
        {
            ExitRecordView();
            PublishDetails();
            SetDetailsActions();
            return;
        }
        if (_detailsObservation is null) return;
        // The record takes the reader's one running request: a details read or target check in progress is retired.
        _reading?.Cancel();
        _checkingTarget?.Cancel();
        _copying?.Cancel(); _saving?.Cancel();
        reader.Cancel();
        _detailsTier = _detailsDisplayedTier;
        _detailsNotice = null;
        _recordShown = true;
        SetViewNames();
        if (_record is not null)
        {
            _recordNotice = null;
            PublishRecord(keepPlace: false);
            SetDetailsActions();
        }
        else await (Reading = ReadRecordCoreAsync());
    }

    /// <summary>Leaves the record view; a record still being read is retired at once, its native read keeps its permit until it returns.</summary>
    private void ExitRecordView()
    {
        _reading?.Cancel();
        _copying?.Cancel(); _saving?.Cancel();
        _detailsReader?.Cancel();
        _recordShown = false;
        _recordNotice = null;
        SetViewNames();
    }

    private async Task ReadRecordCoreAsync()
    {
        if (_detailsReader is not { } reader || _closing.IsCancellationRequested || !_recordShown) return;
        _reading?.Cancel();
        _checkingTarget?.Cancel();
        _copying?.Cancel(); _saving?.Cancel();
        using var cts = _reading = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        var ct = cts.Token;
        if (_record is null)
        {
            _recordNotice = ReadingRecord;
            Show(ReadingRecord);
            UpdateRecordStatus(announce: false);
        }
        else
        {
            _recordNotice = "Previous record · reading…";
            PublishRecord(announceStatus: false);
        }
        SetDetailsActions();
        // Another window's record holds FileCat's one record reservation: say so instead of an unexplained wait.
        void Waiting() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (ct.IsCancellationRequested || !ReferenceEquals(_reading, cts) || !_recordShown) return;
            if (_record is null)
            {
                _recordNotice = WaitingRecord;
                Show(WaitingRecord);
                UpdateRecordStatus();
            }
            else
            {
                _recordNotice = "Previous record · waiting for another file-system record read to finish";
                PublishRecord();
            }
        });
        try
        {
            FileRecordObservation? record = null;
            string? failure = null;
            try { record = await reader.ReadRecordAsync(ct, Waiting); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || _closing.IsCancellationRequested) { return; }
            // The record parses on-disk structures: any failure is said in the window, never thrown at it.
            catch (Exception ex) { failure = ex.Message; }
            if (ct.IsCancellationRequested || _closing.IsCancellationRequested || !ReferenceEquals(_reading, cts) || !_recordShown) return;
            if (record is not null)
            {
                _record = record;
                _recordNotice = null;
                PublishRecord();
                return;
            }
            _recordNotice = "Record read failed: " + failure;
            if (_record is not null) PublishRecord();
            else
            {
                Show(_recordNotice);
                UpdateRecordStatus();
            }
        }
        finally
        {
            if (ReferenceEquals(_reading, cts)) _reading = null;
            if (!_closing.IsCancellationRequested) SetDetailsActions();
        }
    }

    /// <param name="keepPlace">Keep the scroll position (a refresh of the record); false when the record replaces the details.</param>
    private void PublishRecord(bool announceStatus = true, bool keepPlace = true)
    {
        if (_record is not { } observation || !_recordShown) return;
        var heading = new List<string>();
        if (_recordNotice is { } notice) heading.Add(notice + " · showing the record read at " + observation.Before.ObservedAt.ToString("O"));
        if (_detailsObservation?.Summary.Identity is { State: FactState.Known, Value: { } seen } &&
            observation.Before.Identity is { State: FactState.Known, Value: { } recorded } && seen != recorded)
            heading.Add("Changed object: this record describes another object than these details observed. The path names a new object now.");
        if (observation.OutputRefusal() is { } refusal) heading.Add("Copy and Save are unavailable for this record: " + refusal);
        string report = observation.Record.ToText();
        // One line each (a failure's message can hold line breaks), so the spoken forms stay on their own lines.
        string text = heading.Count == 0 ? report
            : string.Join(Environment.NewLine, heading.Select(line => line.ReplaceLineEndings(" "))) + Environment.NewLine + report;
        string?[]? spoken = SpokenReport.Lines(observation.Record, report) is { } lines
            ? [.. Enumerable.Repeat<string?>(null, heading.Count), .. lines] : null;
        // The same bound as the details text: every native acquisition is bounded first; this bounds the assembled text.
        const int MaxChars = 2 * 1024 * 1024 - 128;
        if (text.Length > MaxChars)
        {
            int whole = text.AsSpan(0, MaxChars).Count('\n'); // lines before the cut; the cut line is read as shown
            if (spoken is not null && spoken.Length > whole) spoken = spoken[..whole];
            text = text[..MaxChars] + "\nFurther detail omitted: the report text limit was reached.";
        }
        long top = keepPlace ? _view.TopOffset : 0;
        Show(text, spoken: spoken);
        if (top > 0 && top < System.Text.Encoding.UTF8.GetByteCount(text)) _view.ScrollToOffset(top);
        _readAt = observation.Before.ObservedAt.LocalDateTime;
        UpdateRecordStatus(announceStatus);
    }

    private void UpdateRecordStatus(bool announce = true)
    {
        // As for the details: the pending read is in the text, and the status announces the outcome.
        if (!announce) AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Off);
        _status.Text = _record is { } record && !RecordPlaceholder
            ? $"{(_recordNotice is { } notice ? notice + " · " : "")}{RecordKind} · read at {record.Before.ObservedAt.LocalDateTime:T} · " +
              $"{_lineCount:N0} lines · {(record.OutputRefusal() is null ? "" : "Copy and Save unavailable · ")}F5 reads it again"
            : _recordNotice ?? RecordKind + " not read";
        if (!announce) AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
    }
}
