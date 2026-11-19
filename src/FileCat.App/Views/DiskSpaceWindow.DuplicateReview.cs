using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private static Button DuplicateReviewButton()
    {
        const string description = "Estimate primary-content read cost for the marked entries, including marks hidden by filters.";
        var button = new Button { Content = "Review duplicate candidates…" };
        AutomationProperties.SetHelpText(button, description);
        ToolTip.SetTip(button, new RichTip("Review duplicate candidates", detail: description));
        return button;
    }

    private static AnalysisSelection CaptureDuplicateMarks(SessionIdentity identity, ImmutableHashSet<ulong> marks)
    {
        if (marks.Count > AnalysisDuplicateEstimates.MaximumMarks)
            throw new InvalidOperationException($"Narrow the marked selection to at most {AnalysisDuplicateEstimates.MaximumMarks:N0} entries for duplicate review.");
        return new(identity.SessionId, identity.Epoch, identity.Revision, [.. marks]);
    }

    private async Task ShowDuplicateReviewAsync(AnalysisDuplicateEstimate estimate, ImmutableArray<RootBinding> roots,
        Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> readPath, AnalysisIndexedAcquisition? runtime, AnalysisSelection marks,
        CancellationToken lifetime)
    {
        const int pageSize = 128;
        var locations = roots.ToDictionary(root => root.Id, root => AnalysisNames.Display(root.Label, 120) + " [" + root.Id + "]", StringComparer.Ordinal);
        var captured = estimate.Rows.OrderBy(row => row.State != AnalysisDuplicateCandidateState.Candidate).ToArray();
        var header = Text($"Marked entries · {captured.Length:N0} across {captured.Where(row => row.RootId is not null).Select(row => row.RootId).Distinct().Count():N0} captured locations");
        var observation = Text($"Observation {estimate.Identity.ObservedUtc.ToLocalTime():g} · revision {estimate.Identity.Revision:N0} · rule version 1");
        var reads = Text($"Estimated primary-content reads · {estimate.KnownBytes:N0} B for {estimate.CandidateObjects:N0} distinct candidate objects.");
        var unknown = Text($"{estimate.UnknownBytePaths:N0} marked file paths have unknown or changed size and are excluded from the estimate.");
        var rule = Text("Only separate recorded objects with the same known size are candidates. Extra hard-link names and cloud or offline entries are excluded. Hashing would read primary file contents; streams, forks and other metadata would remain unexamined.");
        var capability = new AnalysisCapabilities().For(AnalysisVerb.StableHash);
        bool canReview = runtime?.CanReviewContentReads == true && estimate.Origin == AnalysisOrigin.LiveSession;
        var availability = Text(estimate.Origin != AnalysisOrigin.LiveSession ? "Saved and imported observations carry recorded metadata." :
            canReview ? "Review the exact eligible locations, file paths and bytes before confirming a content read." :
            OperatingSystem.IsWindows() ? "Content reads require a current, finished live NTFS analysis." : capability.Reason);
        var page = Text("");
        AutomationProperties.SetLiveSetting(page, AutomationLiveSetting.Polite);
        var list = new ListBox { MinHeight = 120, MaxHeight = 280, ItemTemplate = DialogLayout.WrappingListText };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(list, "Captured duplicate candidates and exclusions");
        var previous = new Button { Content = "Previous entries" };
        var next = new Button { Content = "Next entries" };
        var navigation = new WrapPanel { IsVisible = captured.Length > pageSize, ItemSpacing = 8, Children = { previous, next } };
        var inspect = new Button { Content = "Inspect recorded path", IsEnabled = false };
        var detail = Text("");
        AutomationProperties.SetAutomationId(detail, "Duplicate candidate recorded path");
        AutomationProperties.SetLiveSetting(detail, AutomationLiveSetting.Polite);
        var body = new StackPanel { Spacing = 8, Children = { header, observation, reads, unknown, rule, availability, page, list, navigation, inspect, detail } };
        if (estimate.Coverage != NamespaceCoverage.CompleteUnderPolicy)
            body.Children.Insert(2, Text("Partial observations. This estimate covers captured marks only."));

        int offset = 0;
        Task? inspection = null;
        bool openReadReview = false;
        using var dialogLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        var closer = new DialogCloser();
        previous.Click += (_, _) => Load(Math.Max(0, offset - pageSize));
        next.Click += (_, _) => { if (offset + pageSize < captured.Length) Load(offset + pageSize); };
        list.SelectionChanged += (_, _) => { detail.Text = ""; UpdateInspect(); };
        inspect.Click += (_, _) =>
        {
            if (inspect.IsEnabled && list.SelectedIndex >= 0)
            {
                inspect.IsEnabled = false;
                inspection = InspectAsync(offset + list.SelectedIndex);
            }
        };
        if (canReview && estimate.CandidateObjects >= 2)
        {
            var readReview = new Button { Content = "Review content reads…" };
            readReview.Click += (_, _) => { openReadReview = true; closer.Close(null); };
            body.Children.Add(readReview);
        }
        Load(0);

        using var close = lifetime.Register(() => Dispatcher.UIThread.Post(() => closer.Close(null)));
        lifetime.ThrowIfCancellationRequested();
        try
        {
            await _dialogs.ShowCustomAsync("Duplicate review", body,
                [new DialogButton("Close review", false, IsDefault: true, IsCancel: true)], closer: closer, resizable: true);
        }
        finally
        {
            dialogLifetime.Cancel();
            if (inspection is not null) await inspection;
        }
        if (openReadReview)
        {
            var plan = await IndexedComparisonProgressAsync("Preparing content read review",
                token => runtime!.PrepareDuplicateReadPlanAsync(marks, token), lifetime);
            await ShowDuplicateReadConsentAsync(plan, estimate.UnknownBytePaths, lifetime);
        }

        void UpdateInspect() => inspect.IsEnabled = list.SelectedIndex >= 0 && offset + list.SelectedIndex < captured.Length &&
            captured[offset + list.SelectedIndex].RootId is not null &&
            (inspection is null || inspection.IsCompleted);

        async Task InspectAsync(int index)
        {
            var row = captured[index];
            var token = dialogLifetime.Token;
            try
            {
                var path = await AnalysisRecipeDialog.RecordedPathAsync(row.PathId, row.RootId!, readPath, token);
                if (!token.IsCancellationRequested && list.SelectedIndex >= 0 && index == offset + list.SelectedIndex)
                {
                    var root = roots.Single(location => location.Id == row.RootId);
                    detail.Text = AnalysisRecipeDialog.LocationLabel(root) + "\n" + path;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) when (IndexedExpectedError(error) && !AnalysisCloseUnconfirmedException.IsPresent(error))
            {
                if (!token.IsCancellationRequested && list.SelectedIndex >= 0 && index == offset + list.SelectedIndex)
                    detail.Text = "Recorded path unavailable. " + AnalysisNames.Display(error.Message, 240);
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    inspect.IsEnabled = list.SelectedIndex >= 0 && offset + list.SelectedIndex < captured.Length &&
                        captured[offset + list.SelectedIndex].RootId is not null;
            }
        }

        void Load(int start)
        {
            offset = start;
            list.ItemsSource = captured.Skip(offset).Take(pageSize).Select(row => $"{row.Name} · {Describe(row.State)}\n" +
                (row.RootId is { } id ? locations.GetValueOrDefault(id, "Location unavailable") + " · " : "") +
                $"captured entry {row.PathId:N0}" + (row.Bytes is { } size ? " · " + DiskSpaceViewModel.Bytes(size) : "")).ToArray();
            previous.IsEnabled = offset > 0;
            next.IsEnabled = offset + pageSize < captured.Length;
            page.Text = $"Entries {offset + 1:N0}–{offset + list.ItemCount:N0} of {captured.Length:N0}";
        }

        static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
        static string Describe(AnalysisDuplicateCandidateState state) => state switch
        {
            AnalysisDuplicateCandidateState.Candidate => "Same-size candidate",
            AnalysisDuplicateCandidateState.Absent => "Absent from this revision",
            AnalysisDuplicateCandidateState.NotRegularFile => "Outside regular-file review",
            AnalysisDuplicateCandidateState.ExcludedByPolicy => "Excluded by scan policy",
            AnalysisDuplicateCandidateState.Cloud => "Cloud or offline entry excluded",
            AnalysisDuplicateCandidateState.UnknownSize => "Size unavailable",
            AnalysisDuplicateCandidateState.UnverifiedObject => "Object identity or version unverified",
            AnalysisDuplicateCandidateState.HardLinkAlias => "Additional name for the same recorded file",
            AnalysisDuplicateCandidateState.NoSizePeer => "No separate marked object with this size",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
    }

    private sealed partial class SessionView
    {
        private readonly Button _reviewDuplicates = DuplicateReviewButton();

        private async Task ReviewDuplicatesAsync()
        {
            if (_disposed || _window._dialogs.IsOpen || Model.Tree is null || Model.Selection.Marks.Count == 0) return;
            using var actual = _actualWork.Enter();
            Model.Interaction(true);
            try
            {
                using var source = Model.CaptureDisplayedRevision();
                var snapshot = source.Snapshot;
                var marks = CaptureDuplicateMarks(snapshot.Identity, Model.Selection.Marks);
                var lifetime = Model.HistoryLifetime;
                var estimate = await _window.IndexedComparisonProgressAsync("Estimating duplicate content reads",
                    token => AnalysisDuplicateEstimates.FromSnapshotAsync(source, marks, token), lifetime);
                await _window.ShowDuplicateReviewAsync(estimate, snapshot.Roots, (id, token) => ReadRecipeMemoryPathAsync(snapshot, id, token),
                    snapshot.Phase == AnalysisPhase.Finished ? Model.BranchRuntime : null, marks, lifetime);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (IndexedExpectedError(error))
            {
                if (AnalysisCloseUnconfirmedException.IsPresent(error)) _window.RecordIndexedCloseFailure(error);
                if (!_disposed) Model.SetStatus("Duplicate review unavailable. " + AnalysisNames.Display(error.Message, 400));
            }
            finally { Model.Interaction(false); }
        }
    }

    private sealed partial class IndexedSessionView
    {
        private readonly Button _reviewDuplicates = DuplicateReviewButton();

        private async Task ReviewDuplicatesAsync()
        {
            if (_disposed || _window._dialogs.IsOpen || _selection.Marks.Count == 0) return;
            using var actual = _actualWork.Enter();
            Interaction(true);
            try
            {
                var descriptor = _source.Descriptor;
                var marks = CaptureDuplicateMarks(descriptor.Identity, _selection.Marks);
                var lifetime = _selectionLifetime.Token;
                var estimate = await _window.IndexedComparisonProgressAsync("Estimating duplicate content reads",
                    token => _source.EstimateDuplicatesAsync(marks, token), lifetime);
                await _window.ShowDuplicateReviewAsync(estimate, descriptor.Roots, _source.ReadPathAsync,
                    descriptor.Phase == AnalysisPhase.Finished && !_actualWork.HasUnconfirmedClose ? _runtimeAcquisition : null, marks, lifetime);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (Expected(error))
            {
                RememberViewCloseFailure(error);
                if (!_disposed) _status.Text = "Duplicate review unavailable. " + AnalysisNames.Display(error.Message, 400);
            }
            finally { Interaction(false); }
        }
    }
}
