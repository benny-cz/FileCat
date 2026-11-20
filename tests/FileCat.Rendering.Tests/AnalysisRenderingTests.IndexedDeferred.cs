using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisGroupingDimension.FolderHierarchy)]
    [InlineData(AnalysisGroupingDimension.Extension)]
    public async Task NativeDeferredIndexedReviewKeepsItsCapturedQueryAndPreviewReferencesUntilExplicitNewTabApproval(AnalysisGroupingDimension grouping)
    {
        using var styles = new ProductStyles(); await using var fixture = await DeferredFixture.CreateAsync(future: 130, indexedStorage: true);
        var model = fixture.Model; var window = new DiskSpaceWindow();
        try
        {
            Assert.True(model.IsIndexedRuntimePreview); window.AddSession(model); window.Show();
            ulong current = model.Tree!.Snapshot.Nodes.Single(node => node.Name == "current.txt").Id;
            model.Focus(AnalysisTree.Key(current), toggleMark: true); model.StageSelection();
            var marks = model.Selection.Marks; var basket = model.Selection.Basket;
            model.Query = "name:next-"; model.Grouping = grouping; await model.RebuildAsync(true);
            await Until(() => model.PublishedReportView.Query == "name:next-" && model.PublishedReportView.Grouping == grouping);
            Assert.True(model.RequestSelectionAfterScan());
            model.Query = "name:never-match-this"; await model.RebuildAsync(true);
            await fixture.FinishAsync(); await Until(() => model.HasIndexedDeferredReview);
            Assert.Equal(130, model.DeferredSelectionCount); Assert.False(model.CanOpenRecordedResult);
            Assert.Same(marks, model.Selection.Marks); Assert.Same(basket, model.Selection.Basket);
            Click(window, "Review deferred matches"); await Until(() => window.GetVisualDescendants().OfType<ListBox>().Any(list => AutomationLabel(list) == "Deferred captured matches" && list.ItemCount == 128)); Frame(window);
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationLabel(list) == "Deferred captured matches");
            Assert.Equal(128, list.ItemCount); Click(window, "Next deferred matches"); await Until(() => list.ItemCount == 2);
            Assert.All(list.Items.Cast<string>(), text => Assert.Contains("next-", text));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Frame(window);
            Assert.Equal(1, window.SessionCount); Assert.Same(marks, model.Selection.Marks); Assert.Same(basket, model.Selection.Basket);
            if (window.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Keep preview unchanged / Close review")))
                Click(window, "Keep preview unchanged / Close review");
            await Until(() => !window.GetVisualDescendants().OfType<ListBox>().Any(list => AutomationLabel(list) == "Deferred captured matches") &&
                window.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Review deferred matches") && button.IsEnabled));
            Assert.True(model.CanReviewDeferredSelection);
            Click(window, "Review deferred matches");
            await Until(() => window.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Open reviewed matches in a new tab") && button.IsEnabled));
            var displayed = model.Tree;
            Click(window, "Open reviewed matches in a new tab"); await Until(() => window.SessionCount == 2, () => ComparisonText(window), TimeSpan.FromSeconds(30)); Frame(window);
            Assert.Same(displayed, model.Tree); Assert.Same(marks, model.Selection.Marks); Assert.Same(basket, model.Selection.Basket);
            Assert.True(IndexedSelectionCount(window, 130, 0)); Assert.False(model.HasDeferredSelection); Assert.False(model.CanReviewDeferredSelection);
            Assert.Contains("Captured preview", model.RuntimePreviewNotice);
            Assert.Equal("name:next-", IndexedPersistenceControl<TextBox>(window, "Filter indexed observations").Text);
        }
        finally { fixture.Gate.Release.Set(); window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task NativeDeferredIndexedReviewCanCloseWhileItsMetadataIsQueuedAndRetryWithoutAnUnwantedDialog()
    {
        using var styles = new ProductStyles(); await using var fixture = await DeferredFixture.CreateAsync(indexedStorage: true); var model = fixture.Model;
        var window = new DiskSpaceWindow(); using var gate = new ManualResetEventSlim();
        using var entered = new CountdownEvent(fixture.Services.Io.ThreadsPerDevice); var blocked = new List<Task>();
        try
        {
            window.AddSession(model); window.Show(); Assert.True(model.RequestSelectionAfterScan());
            await fixture.FinishAsync(); await Until(() => model.HasIndexedDeferredReview);
            for (int n = 0; n < fixture.Services.Io.ThreadsPerDevice; n++)
                blocked.Add(fixture.Services.Io.RunOwned(PathUtil.GetDeviceKey(fixture.Input), IoPriority.Background,
                    _ => { entered.Signal(); gate.Wait(TimeSpan.FromSeconds(6)); return true; }));
            await Task.Run(() => Assert.True(entered.Wait(TimeSpan.FromSeconds(3))));
            Click(window, "Review deferred matches");
            await Until(() => window.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Keep preview unchanged / Close review")));
            Frame(window);
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Review deferred matches")).IsEnabled);
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Open reviewed matches in a new tab")).IsEnabled);
            Click(window, "Keep preview unchanged / Close review"); gate.Set(); await Task.WhenAll(blocked);
            await Until(() => window.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Review deferred matches") && button.IsEnabled));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ListBox>(), list => AutomationLabel(list) == "Deferred captured matches");
            Assert.True(model.HasIndexedDeferredReview); Assert.Equal(1, window.SessionCount);
            Click(window, "Review deferred matches");
            await Until(() => window.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Open reviewed matches in a new tab") && button.IsEnabled),
                () =>
                {
                    using var captured = model.CaptureIndexedDeferredSelectionReview();
                    return "Can apply now: " + model.CanApplyIndexedDeferredSelection(captured) + " · " + model.Status + " · " + ComparisonText(window) + " · " +
                        string.Join("; ", window.GetVisualDescendants().OfType<Button>().Where(button => Equals(button.Content, "Open reviewed matches in a new tab"))
                            .Select(button => "enabled=" + button.IsEnabled + " help=" + Avalonia.Automation.AutomationProperties.GetHelpText(button)));
                });
            Click(window, "Open reviewed matches in a new tab"); await Until(() => window.SessionCount == 2, () => ComparisonText(window), TimeSpan.FromSeconds(30)); Frame(window);
            Assert.True(IndexedSelectionCount(window, 3, 0));
        }
        finally { gate.Set(); await Task.WhenAll(blocked); fixture.Gate.Release.Set(); window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeDeferredIndexedCanceledOrStoppedRequestsNeverMarkUnseenPaths(bool stop)
    {
        await using var fixture = await DeferredFixture.CreateAsync(indexedStorage: true); var model = fixture.Model;
        var marks = model.Selection.Marks; var basket = model.Selection.Basket;
        Assert.True(model.RequestSelectionAfterScan());
        if (stop) model.Stop(); else model.CancelDeferredSelection();
        await fixture.FinishAsync();
        if (stop) await Until(() => model.DeferredSelectionState == AnalysisDeferredSelectionState.Unavailable);
        else Assert.Equal(AnalysisDeferredSelectionState.None, model.DeferredSelectionState);
        Assert.False(model.CanReviewDeferredSelection); Assert.False(model.HasIndexedDeferredReview);
        Assert.Same(marks, model.Selection.Marks); Assert.Same(basket, model.Selection.Basket);
    }

    [AvaloniaFact]
    public async Task NativeDeferredIndexedReviewRechecksCapacityAcrossWindowsAndKeepsItsCapturedMarks()
    {
        using var styles = new ProductStyles(); await using var fixture = await DeferredFixture.CreateAsync(indexedStorage: true);
        var model = fixture.Model; var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool);
        var other = new DiskSpaceWindow(null, null, pool);
        try
        {
            window.AddSession(model); window.Show(); Assert.True(model.RequestSelectionAfterScan());
            await fixture.FinishAsync(); await Until(() => model.HasIndexedDeferredReview);
            for (int n = 1; n < AnalysisLimits.Sessions; n++) other.AddSession(new DiskSpaceViewModel(fixture.Services, ImportedFixture()));
            Click(window, "Review deferred matches");
            await Until(() => window.GetVisualDescendants().OfType<ListBox>().Any(list => AutomationLabel(list) == "Deferred captured matches" && list.ItemCount == 3));
            Frame(window);
            var apply = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Open reviewed matches in a new tab"));
            Assert.False(apply.IsEnabled); Assert.Equal(1, window.SessionCount); Assert.Empty(model.Selection.Marks);
            Assert.Contains("eight", Avalonia.Automation.AutomationProperties.GetHelpText(apply), StringComparison.OrdinalIgnoreCase);
            other.ForceClose(); await other.WaitForIndexedCloseAsync(); await Until(() => apply.IsEnabled);
            Click(window, "Open reviewed matches in a new tab"); await Until(() => window.SessionCount == 2, () => ComparisonText(window), TimeSpan.FromSeconds(30));
            Frame(window); Assert.True(IndexedSelectionCount(window, 3, 0)); Assert.Empty(model.Selection.Marks); Assert.Empty(model.Selection.Basket);
        }
        finally { other.ForceClose(); await other.WaitForIndexedCloseAsync(); fixture.Gate.Release.Set(); window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        await AssertAllRuntimePlacesAvailableAsync(pool);
    }

    [AvaloniaFact]
    public async Task NativeDeferredIndexedClosingTheSourceWhileReviewReadsAreQueuedRetiresTheDialogAndAllOwners()
    {
        using var styles = new ProductStyles(); await using var fixture = await DeferredFixture.CreateAsync(indexedStorage: true);
        var model = fixture.Model; var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool);
        using var gate = new ManualResetEventSlim(); using var entered = new CountdownEvent(fixture.Services.Io.ThreadsPerDevice); var blocked = new List<Task>();
        try
        {
            window.AddSession(model); window.Show(); Assert.True(model.RequestSelectionAfterScan());
            await fixture.FinishAsync(); await Until(() => model.HasIndexedDeferredReview);
            for (int n = 0; n < fixture.Services.Io.ThreadsPerDevice; n++)
                blocked.Add(fixture.Services.Io.RunOwned(PathUtil.GetDeviceKey(fixture.Input), IoPriority.Background,
                    _ => { entered.Signal(); gate.Wait(TimeSpan.FromSeconds(6)); return true; }));
            await Task.Run(() => Assert.True(entered.Wait(TimeSpan.FromSeconds(3))));
            Click(window, "Review deferred matches");
            await Until(() => window.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Keep preview unchanged / Close review")));
            window.ForceClose(); Assert.False(window.IsVisible); Assert.False(model.CanReviewDeferredSelection);
            gate.Set(); await Task.WhenAll(blocked); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion;
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ListBox>(), list => AutomationLabel(list) == "Deferred captured matches");
            Assert.False(model.HasDeferredSelection); Assert.True(fixture.SourcesDisposed);
        }
        finally { gate.Set(); await Task.WhenAll(blocked); fixture.Gate.Release.Set(); window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        await AssertAllRuntimePlacesAvailableAsync(pool);
    }

    [AvaloniaFact]
    public async Task NativeDeferredIndexedCancellationBeforePublicationRevokesTheHandoffAndKeepsTheRuntimeOwner()
    {
        using var styles = new ProductStyles(); await using var fixture = await DeferredFixture.CreateAsync(indexedStorage: true); var model = fixture.Model;
        var window = new DiskSpaceWindow(); AnalysisDeferredIndexedReview? review = null;
        try
        {
            window.AddSession(model); window.Show(); Assert.True(model.RequestSelectionAfterScan());
            await fixture.FinishAsync(); await Until(() => model.HasIndexedDeferredReview); review = model.CaptureIndexedDeferredSelectionReview();
            await Assert.ThrowsAsync<InvalidOperationException>(() => window.AddPreparedIndexedSessionAsync(token => review.Dataset.RetainAsync(token),
                review.View.Metric, null, review.View, default, fixture.Services, runtimeHandoff: () =>
                { model.CancelDeferredSelection(); return model.DetachRecordedResultOwner(review.Request); }, reviewedMarks: review.Selection));
            await window.WaitForIndexedCloseAsync(); Assert.Equal(1, window.SessionCount); Assert.True(model.IsIndexedRuntimePreview);
            await Until(() => model.CanOpenRecordedResult); Click(window, "Open recorded result");
            await Until(() => window.SessionCount == 2, () => ComparisonText(window), TimeSpan.FromSeconds(30)); Frame(window); Assert.True(IndexedSelectionCount(window, 0, 0));
        }
        finally { review?.Dispose(); fixture.Gate.Release.Set(); window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeferredSelectionReviewWrapsCapturedPathsAtNarrowWidthWithoutLosingItsPreservingFooter(bool indexed)
    {
        using var styles = new ProductStyles(); await using var fixture = await DeferredFixture.CreateAsync(indexedStorage: indexed);
        var window = new DiskSpaceWindow { Width = 480, Height = 620, FontSize = 20 };
        try
        {
            window.AddSession(fixture.Model); window.Show(); Assert.True(fixture.Model.RequestSelectionAfterScan());
            await fixture.FinishAsync(); await Until(() => fixture.Model.CanReviewDeferredSelection);
            Click(window, "Review deferred matches");
            await Until(() => window.GetVisualDescendants().OfType<ListBox>().Any(list => AutomationLabel(list) == "Deferred captured matches" && list.ItemCount == 3));
            Frame(window);
            var rows = window.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationLabel(list) == "Deferred captured matches");
            var captions = rows.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Text?.Contains(" · ", StringComparison.Ordinal) == true).ToArray();
            Assert.NotEmpty(captions);
            Assert.Contains(captions, text => text.TextLayout.TextLines.Count > 1);
            Assert.All(captions, text =>
            {
                var point = text.TranslatePoint(default, rows)!.Value;
                Assert.True(point.X >= 0 && point.X + text.Bounds.Width <= rows.Bounds.Width + 1, "A captured path overflows its review viewport.");
            });
            var keep = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content,
                indexed ? "Keep preview unchanged / Close review" : "Keep marks unchanged / Close review"));
            var position = keep.TranslatePoint(default, window)!.Value;
            Assert.True(keep.IsDefault && keep.IsCancel); Assert.InRange(position.Y, 0, window.ClientSize.Height - keep.Bounds.Height + 1);
        }
        finally { fixture.Gate.Release.Set(); window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task NativeDeferredIndexedMatchesIncludeTheFullRecordedNamespaceBeyondItsTwoPathPreview()
    {
        OrdinaryPreparation(); await using var source = await IndexedFixture.CreateAsync(); var token = TestContext.Current.CancellationToken;
        var dataset = source.Lease!; Assert.InRange(source.CapturedPreviewPaths, 1, 2);
        var captured = await AnalysisDeferredIndexedMatches.CaptureAsync(dataset, dataset.Descriptor.Identity, AnalysisSavedView.Default with { Metric = AnalysisMetric.FileSize }, token);
        Assert.Equal(303, captured.NodeIds.Length); Assert.True(captured.NodeIds.Length > source.CapturedPreviewPaths);
        Assert.Equal(captured.NodeIds.Length, captured.NodeIds.Distinct().Count());
        foreach (ulong id in captured.NodeIds) Assert.Equal(AnalysisKind.File, (await dataset.ReadPathAsync(id, token)).Kind);
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeDeferredIndexedMatchesKeepTheRequestedFolderAndItsFinalOtherRanking(bool other)
    {
        OrdinaryPreparation(); await using var source = await IndexedFixture.CreateAsync(); var token = TestContext.Current.CancellationToken;
        var dataset = source.Lease!; var identity = dataset.Descriptor.Identity;
        var target = await AnalysisDeferredIndexedMatches.CaptureAsync(dataset, identity,
            AnalysisSavedView.Default with { Metric = AnalysisMetric.FileSize, Query = "name:match-target.bin" }, token);
        var file = await dataset.ReadPathAsync(Assert.Single(target.NodeIds), token);
        Assert.Equal("match-target.bin", file.Name); string parent = AnalysisTree.Key(file.ParentId);
        var captured = await AnalysisDeferredIndexedMatches.CaptureAsync(dataset, identity, AnalysisSavedView.Default with
        { Metric = AnalysisMetric.FileSize, Scope = other ? AnalysisTree.OtherKey(parent, 1) : parent }, token);
        var names = new List<string>();
        foreach (ulong id in captured.NodeIds)
        { var path = await dataset.ReadPathAsync(id, token); Assert.Equal(file.ParentId, path.ParentId); names.Add(path.Name); }
        Assert.Equal(other ? ["second.bin"] : new[] { "match-target.bin", "second.bin" }, names.Order(StringComparer.Ordinal));
    }

    [AvaloniaFact]
    public async Task NativeDeferredIndexedMatchesKeepOnlyTheFinalOtherCategoryWithoutAddingItsHardLinkContext()
    {
        OrdinaryPreparation(); await using var source = await IndexedFixture.CreateAsync(sharedAlias: true); var token = TestContext.Current.CancellationToken;
        var dataset = source.Lease!;
        var captured = await AnalysisDeferredIndexedMatches.CaptureAsync(dataset, dataset.Descriptor.Identity, AnalysisSavedView.Default with
        { Metric = AnalysisMetric.FileSize, Grouping = AnalysisGroupingDimension.Extension, Scope = AnalysisTree.OtherKey(AnalysisTree.RootKey, 1) }, token);
        Assert.Equal("alias.jpg", (await dataset.ReadPathAsync(Assert.Single(captured.NodeIds), token)).Name);
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
    }

    [AvaloniaFact]
    public async Task NativeDeferredIndexedOtherReviewNamesItsCapturedLocationAndUncertainCaseSensitiveCohort()
    {
        await using var fixture = await DeferredFixture.CreateAsync(unknownAllocation: true, indexedStorage: true, mixedExtensions: true);
        var model = fixture.Model; Assert.True(model.RequestSelectionAfterScan()); await fixture.FinishAsync();
        await Until(() => model.HasIndexedDeferredReview); using var review = model.CaptureIndexedDeferredSelectionReview();
        string root = Assert.Single(review.Dataset.Descriptor.Roots).Id;
        var view = AnalysisSavedView.Default with { Metric = AnalysisMetric.FileSize, Query = "allocated>0", ShowUnknown = true, CaseSensitive = true,
            Root = root, Grouping = AnalysisGroupingDimension.Extension, Scope = AnalysisTree.OtherKey(AnalysisTree.RootKey, 1) };
        var selected = await AnalysisDeferredIndexedMatches.CaptureAsync(review.Dataset, review.Dataset.Descriptor.Identity, view, DeferredCancellation);
        Assert.Equal("next-0001.bin", (await review.Dataset.ReadPathAsync(Assert.Single(selected.NodeIds), DeferredCancellation)).Name);
        Assert.Contains("allocated>0", selected.FilterSummary); Assert.Contains("captured location " + root, selected.FilterSummary);
        Assert.Contains("case sensitive", selected.FilterSummary); Assert.Contains("uncertain results", selected.FilterSummary);
        Assert.Empty(model.Selection.Marks); Assert.Empty(model.Selection.Basket);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task NativeDeferredIndexedMatchesRefuseChangedDomainsAndMissingLocationsWithoutBroadening(int change)
    {
        OrdinaryPreparation(); await using var source = await IndexedFixture.CreateAsync(); var token = TestContext.Current.CancellationToken;
        var dataset = source.Lease!; var requested = dataset.Descriptor.Identity; var view = AnalysisSavedView.Default;
        switch (change)
        {
            case 0: requested = requested with { SessionId = Guid.NewGuid() }; break;
            case 1: requested = requested with { Epoch = requested.Epoch + 1 }; break;
            case 2: requested = requested with { Revision = requested.Revision + 1 }; break;
            default: view = view with { Root = "not-a-captured-location" }; break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisDeferredIndexedMatches.CaptureAsync(dataset, requested, view, token));
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
    }

    [AvaloniaFact]
    public async Task NativeDeferredIndexedReviewRetainsItsDataAndActualWorkUntilTheLastReviewReferenceCloses()
    {
        OrdinaryPreparation(); await using var source = await IndexedFixture.CreateAsync(); var token = TestContext.Current.CancellationToken;
        var dataset = source.Lease!; var captured = await AnalysisDeferredIndexedMatches.CaptureAsync(dataset, dataset.Descriptor.Identity, AnalysisSavedView.Default, token);
        var work = new AnalysisViewWork(); using var review = new AnalysisDeferredIndexedReview(Guid.NewGuid(), captured, AnalysisSavedView.Default, "Captured analysis",
            await dataset.RetainAsync(token), work);
        using var modal = review.Retain();
        try
        {
            Task close = work.CloseAsync(); Assert.False(close.IsCompleted);
            review.Dispose(); Assert.False(close.IsCompleted); Assert.Throws<ObjectDisposedException>(() => review.Retain());
            await source.ReleaseInitialLeaseAsync();
            Assert.Equal(AnalysisKind.File, (await modal.Dataset.ReadPathAsync(captured.NodeIds[0], token)).Kind);
            Assert.Same(captured, modal.Selection); modal.Dispose(); await close.WaitAsync(TimeSpan.FromSeconds(20), token);
            Assert.Throws<ObjectDisposedException>(() => modal.Dataset);
        }
        finally { modal.Dispose(); review.Dispose(); await work.CloseAsync(); }
    }
}
