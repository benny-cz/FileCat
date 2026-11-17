using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.Controls;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private readonly TextBlock _previewNotice = new() { TextWrapping = TextWrapping.Wrap };
        private readonly Button _recordedResult = new() { Content = "Open recorded result" }, _cancelRecorded = new() { Content = "Stop opening", IsVisible = false };
        private readonly Border _recordedHelp = new() { Focusable = true };
        private CancellationTokenSource? _recordedOpenCancel;
        private bool _openingRecorded;
        private string? _recordedReason;

        private void AddRecordedResultControls(StackPanel toolbar)
        {
            _recordedHelp.Child = _recordedResult;
            var bar = new WrapPanel { ItemSpacing = 8, Children = { _recordedHelp, _cancelRecorded } };
            toolbar.Children.Add(_previewNotice); toolbar.Children.Add(bar);
            AutomationProperties.SetName(_previewNotice, "Recorded scan preview notice");
            AutomationProperties.SetName(_recordedHelp, "Open all recorded observations");
            _recordedResult.Click += async (_, _) => await OpenRecordedResultAsync();
            _cancelRecorded.Click += (_, _) =>
            {
                _recordedOpenCancel?.Cancel();
                Model.SetStatus("Stopping result opening. Waiting for its outstanding request to return; the recorded scan is kept.");
                RefreshRecordedResultAvailability();
            };
            RefreshRecordedResultAvailability();
        }

        private void RefreshRecordedResultAvailability()
        {
            if (_disposed) return;
            _previewNotice.Text = Model.RuntimePreviewNotice; _previewNotice.IsVisible = Model.RuntimePreviewNotice is not null;
            _recordedHelp.IsVisible = Model.IsIndexedRuntimePreview;
            string? refusal = _openingRecorded ? "The recorded result is being prepared." : Model.RecordedResultUnavailableReason ??
                (!_window.CanAddSession ? _window._admissionPool.Refusal : null);
            _recordedResult.IsEnabled = refusal is null;
            string detail = refusal ?? "Open every recorded entry in another tab. Keep this preview's displayed revision, marks and review basket.";
            if (_recordedReason != detail)
            {
                _recordedReason = detail;
                AutomationProperties.SetHelpText(_recordedHelp, detail);
                ToolTip.SetTip(_recordedHelp, new RichTip("All recorded observations", detail: detail));
            }
            _cancelRecorded.IsVisible = _openingRecorded; _cancelRecorded.IsEnabled = _recordedOpenCancel?.IsCancellationRequested == false;
        }

        private async Task OpenRecordedResultAsync()
        {
            if (_disposed || _openingRecorded || !Model.CanOpenRecordedResult || Model.HistoryServices is not { } services) return;
            using var actual = _actualWork.Enter(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Model.HistoryLifetime);
            using var captured = Model.CaptureDisplayedRevision(); var before = captured.Snapshot;
            var saved = Model.PublishedReportView; string scope = Model.Selection.Scope; string? focus = Model.Selection.Focus;
            ulong? restoredFocus = null; string? scopeNotice = null;
            _openingRecorded = true; _recordedOpenCancel = cancel;
            try
            {
                RefreshRecordedResultAvailability();
                Model.SetStatus("Opening every recorded entry. This preview and its review references remain available.");
                await _window.AddPreparedIndexedSessionAsync(Model.AcquireRecordedDatasetAsync, saved.Metric, null, saved, cancel.Token, services,
                    historyNotice: Model.HistoryNotice, prepareView: async (current, token) =>
                    {
                        if (before.Identity.SessionId != current.Descriptor.Identity.SessionId || before.Identity.Epoch != current.Descriptor.Identity.Epoch ||
                            before.Identity.HostProfile != current.Descriptor.Identity.HostProfile)
                            throw new InvalidOperationException("The runtime capture changed before its recorded result was opened.");
                        var aggregate = AnalysisSavedCatalog.SplitAggregationScope(scope);
                        string nextScope = aggregate.Scope;
                        if (aggregate.Skipped != 0)
                            scopeNotice = "The preview's Other subset was cleared because the complete recorded ranking can differ. Showing its captured parent.";
                        if (saved.Grouping == AnalysisGroupingDimension.FolderHierarchy)
                        {
                            var mappedScope = await MatchAsync(aggregate.Scope, directory: true);
                            nextScope = mappedScope is { } id ? AnalysisTree.Key(id) : AnalysisTree.RootKey;
                            if (aggregate.Scope != AnalysisTree.RootKey && mappedScope is null)
                                scopeNotice = "The preview's folder could not be verified in the recorded result. Showing all captured locations; no selection was transferred.";
                        }
                        restoredFocus = saved.Grouping == AnalysisGroupingDimension.FolderHierarchy ? await MatchAsync(focus, directory: false) : null;
                        return saved with { Scope = nextScope };

                        async Task<ulong?> MatchAsync(string? key, bool directory)
                        {
                            if (key is null || !key.StartsWith("n:", StringComparison.Ordinal) ||
                                !ulong.TryParse(key.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out ulong id)) return null;
                            var previous = before.Nodes.FirstOrDefault(node => node.Id == id);
                            if (previous is null || directory && previous.Kind != AnalysisKind.Directory) return null;
                            AnalysisPathMetadata next;
                            try { next = await current.ReadPathAsync(id, token); } catch (ArgumentOutOfRangeException) { return null; }
                            return previous.RootId == next.RootId && previous.Kind == next.Kind && previous.Name == next.Name &&
                                previous.NameEncoding == next.NameEncoding && previous.ObservedIdentity == next.ObservedIdentity &&
                                (previous.RawName.IsDefaultOrEmpty ? AnalysisRawNames.Encode(previous.NameEncoding, previous.Name).AsSpan() : previous.RawName.AsSpan())
                                    .SequenceEqual(next.RawName.IsDefaultOrEmpty ? AnalysisRawNames.Encode(next.NameEncoding, next.Name) : next.RawName.AsSpan()) ? id : null;
                        }
                    }, restoredFocus: () => restoredFocus, runtimeHandoff: Model.DetachRecordedResultOwner, preparedViewNotice: () => scopeNotice);
                Model.RecordedResultPublished();
            }
            catch (Exception error) when (IndexedExpectedError(error))
            {
                if (AnalysisCloseUnconfirmedException.IsPresent(error)) _window.RecordIndexedCloseFailure(error);
                if (!_disposed) Model.SetStatus(error is OperationCanceledException ? "Opening stopped. The preview and recorded scan were kept." :
                    "Recorded result unavailable: " + AnalysisNames.Display(error.Message, 400) + " The preview and recorded scan were kept.");
            }
            finally { _recordedOpenCancel = null; _openingRecorded = false; if (!_disposed) RefreshRecordedResultAvailability(); }
        }
    }
}
