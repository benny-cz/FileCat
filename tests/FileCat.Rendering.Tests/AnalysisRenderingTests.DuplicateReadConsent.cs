using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeDuplicateReadConsentChecksBoundScopeAndLeavesSelectionsIntact(bool indexed)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "primary-consent");
        Directory.CreateDirectory(input);
        string before = Path.Combine(input, "copy-a.bin"), after = Path.Combine(input, "copy-b.bin"),
            changed = Path.Combine(input, "changed-after-consent.bin"), excluded = Path.Combine(input, "do-not-read.bin");
        byte[] same = [1, 2, 3, 4];
        byte[] different = [4, 3, 2, 1];
        foreach (string path in new[] { before, after, changed }) File.WriteAllBytes(path, same);
        File.WriteAllBytes(excluded, different);
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(before + ":fixture-extra", "first extra data");
            File.WriteAllText(after + ":fixture-extra", "different extra data");
        }

        var pool = new AnalysisSessionAdmission();
        var window = new DiskSpaceWindow(null, null, pool);
        try
        {
            window.Show();
            var opened = await window.AddOwnedAcquisitionAsync(fixture.Services, async token =>
            {
                var owner = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token, indexedStorage: indexed);
                try { await StartAcquiredAsync(owner, fixture, input); return owner; }
                catch { await owner.DisposeAsync(); throw; }
            });
            Frame(window);
            Click(window, "Mark matching files in this scope");
            await Until(() => indexed ? IndexedSelectionCount(window, 4, 0) : opened.Memory!.Selection.Marks.Count == 4,
                () => ComparisonText(window));
            if (indexed) Click(window, "Add to review basket");
            else opened.Memory!.Selection.StageCaptured(opened.Memory.Selection.Marks);

            await OpenPreviewAsync();
            if (!OperatingSystem.IsWindows())
            {
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Review content reads…"));
                Assert.Contains("A bound stable-content reader has not been qualified", ComparisonText(window));
                Click(window, "Close review");
            }
            else
            {
                var list = await OpenConsentAsync();
                Assert.Contains("Read 16 B from 4 files", ReadSummary());
                var close = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Close read review"));
                var read = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Read selected contents"));
                Assert.True(close.IsDefault && close.IsCancel);
                Assert.False(read.IsDefault);
                Click(window, "Close read review");
                Assert.Null(ComparisonControl<ListBox>(window, "Primary-content evidence rows"));

                await OpenPreviewAsync();
                list = await OpenConsentAsync();
                list.SelectedItem = Assert.Single(list.Items, item => item?.ToString()?.Contains("do-not-read.bin", StringComparison.Ordinal) == true);
                Assert.Contains("do-not-read.bin", ComparisonControl<TextBox>(window, "Exact reviewed content path")!.Text);
                Click(window, "Exclude selected file");
                Assert.Contains("Read 12 B from 3 files", ReadSummary());
                var originalTime = File.GetLastWriteTimeUtc(changed);
                File.WriteAllBytes(changed, different);
                File.SetLastWriteTimeUtc(changed, originalTime);
                Click(window, "Read selected contents");
                await Until(() => ComparisonControl<ListBox>(window, "Primary-content evidence rows")?.ItemCount == 3,
                    () => ComparisonText(window), TimeSpan.FromSeconds(30));
                var result = ComparisonControl<ListBox>(window, "Primary-content evidence rows")!;
                var values = result.Items.Select(item => item?.ToString() ?? "").ToArray();
                Assert.Equal(2, values.Count(value => value.StartsWith("Primary match 1", StringComparison.Ordinal)));
                Assert.Single(values, value => value.Contains("Proposed keeper", StringComparison.Ordinal));
                Assert.Single(values, value => value.Contains("Source changed · read refused", StringComparison.Ordinal) && value.Contains("changed-after-consent.bin", StringComparison.Ordinal));
                Assert.DoesNotContain(values, value => value.Contains("do-not-read.bin", StringComparison.Ordinal));
                Assert.Contains("1 group with matching primary digests", ComparisonText(window));
                result.SelectedIndex = 0;
                Assert.Contains(Convert.ToHexStringLower(SHA256.HashData(same)), ComparisonControl<TextBox>(window, "Primary-content evidence details")!.Text);
                Click(window, "Close content results");
                Assert.Equal(different, File.ReadAllBytes(changed));
                Assert.Equal("first extra data", File.ReadAllText(before + ":fixture-extra"));
                Assert.Equal("different extra data", File.ReadAllText(after + ":fixture-extra"));
            }

            if (indexed) Assert.True(IndexedSelectionCount(window, 4, 4));
            else
            {
                Assert.Equal(4, opened.Memory!.Selection.Marks.Count);
                Assert.Equal(4, opened.Memory.Selection.Basket.Count);
            }
            Assert.Equal(same, File.ReadAllBytes(before));
            Assert.Equal(same, File.ReadAllBytes(after));
            Assert.Equal(different, File.ReadAllBytes(excluded));

            async Task OpenPreviewAsync()
            {
                Click(window, "Review duplicate candidates…");
                await Until(() => ComparisonControl<ListBox>(window, "Captured duplicate candidates and exclusions")?.ItemCount == 4,
                    () => ComparisonText(window), TimeSpan.FromSeconds(20));
            }
            async Task<ListBox> OpenConsentAsync()
            {
                Click(window, "Review content reads…");
                await Until(() => ComparisonControl<ListBox>(window, "Eligible primary-content reads")?.ItemCount == 4,
                    () => ComparisonText(window), TimeSpan.FromSeconds(20));
                return ComparisonControl<ListBox>(window, "Eligible primary-content reads")!;
            }
            string? ReadSummary() => ComparisonControl<TextBlock>(window, "Approved primary-content read scope")?.Text;
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        await AssertAllRuntimePlacesAvailableAsync(pool);
    }

    [AvaloniaFact]
    public async Task NativeDuplicateReadConsentRejectsCapturedDataWithoutItsRuntimeOwner()
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow();
        try
        {
            await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken);
            window.Show();
            Frame(window);
            Click(window, "Mark matching files in this scope");
            await Until(() => IndexedSelectionCount(window, 303, 0), () => ComparisonText(window));
            Click(window, "Review duplicate candidates…");
            await Until(() => ComparisonControl<ListBox>(window, "Captured duplicate candidates and exclusions")?.ItemCount == 128,
                () => ComparisonText(window));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Review content reads…"));
            Click(window, "Close review");
            Assert.True(IndexedSelectionCount(window, 303, 0));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [Fact]
    public async Task DuplicateReadPlanBindsConfirmedMembersCostAndOneRun()
    {
        var identity = DuplicateEstimateIdentity();
        long limit = AnalysisDuplicateReadScope.MaximumBytes;
        var items = new[] { At(1, 4), At(2, 4), At(3, 3), At(4, limit), At(5, limit) }.ToImmutableArray();
        int reads = 0;
        var plan = new AnalysisDuplicateReadPlan(identity, items, 0, (scope, _) =>
        {
            reads++;
            Assert.Equal((BigInteger)8, scope.Bytes);
            Assert.Equal([1UL, 2UL], scope.Items.Select(item => item.Target.PathId));
            return Task.FromResult(new AnalysisDuplicateReadResult(identity, []));
        });
        var choices = ImmutableHashSet.Create(1UL, 2UL, 3UL);
        Assert.Equal(1, plan.Select(choices).WithoutSizePeer);
        Assert.False(plan.Select(ImmutableHashSet.Create(4UL, 5UL)).CanRead);
        Assert.Throws<InvalidOperationException>(() => plan.Select(ImmutableHashSet.Create(1UL, 99UL)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => plan.ReadAsync(ImmutableHashSet.Create(1UL, 3UL), TestContext.Current.CancellationToken));
        Assert.Equal(0, reads);
        await plan.ReadAsync(choices, TestContext.Current.CancellationToken);
        Assert.Equal(1, reads);
        await Assert.ThrowsAsync<InvalidOperationException>(() => plan.ReadAsync(choices, TestContext.Current.CancellationToken));

        static AnalysisPrimaryReadItem At(ulong id, long bytes) => new(DuplicateEstimateRoot,
            new(id, null, [], $"file-{id}", $"id-{id}", "v", DateTimeOffset.UnixEpoch, 1, bytes), $"file-{id}");
    }
}
