using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeVolumeCapacityUsesTheLiveFolderAndStaysOutOfOfflineViews(bool indexed)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        using var model = new DiskSpaceViewModel(fixture.Services, indexedStorage: indexed);
        string source = Path.Combine(fixture.Root, "volume-source");
        Directory.CreateDirectory(source);
        string file = Path.Combine(source, "sample.txt");
        File.WriteAllText(file, "unchanged");
        var window = new DiskSpaceWindow(null, null, new AnalysisSessionAdmission()) { Width = 380, Height = 430, FontSize = 20 };
        window.AddSession(model, [source]);
        window.Show();
        try
        {
            var started = DateTimeOffset.UtcNow;
            await model.StartAsync([source]);
            await Until(() => model.Phase == AnalysisPhase.Finished && model.Tree is not null, timeout: TimeSpan.FromSeconds(25));
            var observations = model.Tree!.Snapshot;
            var capacity = Assert.Single(model.VolumeCapacities).Value;
            Assert.InRange(capacity.ObservedUtc, started, DateTimeOffset.UtcNow);
            Assert.False(capacity.WholeVolumeVerified);
            Assert.True(capacity.TotalBytes > 0 && capacity.UsedBytes <= capacity.TotalBytes &&
                capacity.AvailableBytes <= capacity.FreeBytes && capacity.FreeBytes <= capacity.TotalBytes);
            Assert.False(string.IsNullOrWhiteSpace(capacity.VolumeIdentity));
            CheckCapacity(capacity);

            if (indexed)
            {
                await Until(() => model.CanOpenRecordedResult);
                Click(window, "Open recorded result");
                await Until(() => window.SessionCount == 2);
                Frame(window);
                CheckCapacity(capacity);
                Assert.Equal(capacity, Assert.Single(model.VolumeCapacities).Value);
            }

            using var offline = new DiskSpaceViewModel(observations with { Origin = AnalysisOrigin.ImportedInventory });
            window.AddSession(offline);
            Frame(window);
            Assert.Null(VisibleCapacity());
            Assert.Empty(offline.VolumeCapacities);
            Assert.Equal("unchanged", File.ReadAllText(file));
        }
        finally
        {
            window.ForceClose();
            await window.WaitForIndexedCloseAsync();
            await model.CloseCompletion;
        }

        Button? VisibleCapacity() => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
            button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == "AnalysisVolumeCapacity");

        void CheckCapacity(AnalysisVolumeCapacity sample)
        {
            Frame(window);
            var ribbon = VisibleCapacity();
            Assert.NotNull(ribbon);
            Assert.Contains(DiskSpaceViewModel.Bytes(sample.FreeBytes) + " free", AutomationProperties.GetName(ribbon));
            Click(ribbon);
            Frame(window);
            var text = window.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToArray();
            Assert.Contains("Volume capacity", text);
            Assert.Contains(text, line => line?.Contains("Volume ID " + sample.VolumeIdentity, StringComparison.Ordinal) == true &&
                line.Contains("Reported used " + DiskSpaceViewModel.Bytes(sample.UsedBytes), StringComparison.Ordinal) &&
                line.StartsWith("File totals describe the scanned scope. Capacity describes the containing filesystem.", StringComparison.Ordinal));
            Assert.DoesNotContain(text, line => line?.Contains("Difference from reported used space", StringComparison.Ordinal) == true);
            if (sample.MayShareCapacity)
                Assert.Contains(text, line => line?.Contains("may share capacity with other volumes", StringComparison.Ordinal) == true);
            Click(window, "Close capacity");
            Frame(window);
        }
    }

    [AvaloniaFact]
    public async Task NativeWholeVolumeCapacityShowsSignedDifferenceForUnfilteredObservedAllocation()
    {
        string? volume = Environment.GetEnvironmentVariable("FILECAT_TEST_NATIVE_CAPACITY_VOLUME");
        Assert.SkipWhen(string.IsNullOrEmpty(volume), "Requires a small, separately mounted native test volume.");
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        using var model = new DiskSpaceViewModel(fixture.Services);
        string root = Path.GetFullPath(volume!);
        string anchor = Path.Combine(root, "filecat-capacity-fixture.txt");
        Assert.Equal("FileCat whole-volume capacity fixture", File.ReadAllText(anchor));
        var window = new DiskSpaceWindow(null, null, new AnalysisSessionAdmission());
        window.AddSession(model, [root]);
        window.Show();
        try
        {
            await model.StartAsync([root]);
            if (model.ResolvedTargets is not null && model.Phase == AnalysisPhase.Ready) await model.StartAsync([root]);
            await Until(() => model.Tree?.Snapshot.Phase == AnalysisPhase.Finished, () => model.Status, TimeSpan.FromSeconds(25));
            var sample = Assert.Single(model.VolumeCapacities).Value;
            Assert.True(sample.WholeVolumeVerified);
            Assert.False(sample.MayShareCapacity);
            Assert.InRange(sample.TotalBytes, (UInt128)1, (UInt128)(512L * 1024 * 1024));
            Assert.Contains(model.Tree!.Snapshot.Nodes, node => node.Name == Path.GetFileName(anchor));
            string difference = DiskSpaceWindow.VolumeDifference(sample.UsedBytes, model.Tree.Projection.Totals.Known);
            Assert.Contains(DialogText(), line => line?.Contains("Difference from reported used space " + difference, StringComparison.Ordinal) == true);
            Click(window, "Close capacity");

            model.Query = "name:filecat-capacity-fixture";
            await model.RebuildAsync(true);
            Assert.DoesNotContain(DialogText(), line => line?.Contains("Difference from reported used space", StringComparison.Ordinal) == true);
            Click(window, "Close capacity");
            Assert.Equal("FileCat whole-volume capacity fixture", File.ReadAllText(anchor));
        }
        finally
        {
            window.ForceClose();
            await window.WaitForIndexedCloseAsync();
            await model.CloseCompletion;
        }

        string?[] DialogText()
        {
            Frame(window);
            Click(window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible &&
                AutomationProperties.GetAutomationId(button) == "AnalysisVolumeCapacity"));
            Frame(window);
            return window.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToArray();
        }
    }

    [AvaloniaFact]
    public async Task NativeSharedApfsCapacityKeepsVolumeSamplesSeparate()
    {
        string? first = Environment.GetEnvironmentVariable("FILECAT_TEST_NATIVE_APFS_FIRST_VOLUME");
        string? second = Environment.GetEnvironmentVariable("FILECAT_TEST_NATIVE_APFS_SECOND_VOLUME");
        Assert.SkipWhen(!OperatingSystem.IsMacOS() || string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second),
            "Requires two separately mounted native APFS test volumes in one owned container.");
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        using var model = new DiskSpaceViewModel(fixture.Services);
        string[] roots = [Path.GetFullPath(first!), Path.GetFullPath(second!)];
        string[] anchors = roots.Select(root => Path.Combine(root, "filecat-apfs-capacity-fixture.txt")).ToArray();
        Assert.All(anchors, anchor => Assert.Equal("FileCat APFS capacity fixture", File.ReadAllText(anchor)));
        var window = new DiskSpaceWindow(null, null, new AnalysisSessionAdmission());
        window.AddSession(model, roots);
        window.Show();
        try
        {
            await model.StartAsync(roots);
            if (model.ResolvedTargets is not null && model.Phase == AnalysisPhase.Ready) await model.StartAsync(roots);
            await Until(() => model.Tree?.Snapshot.Phase == AnalysisPhase.Finished, () => model.Status, TimeSpan.FromSeconds(25));
            var samples = model.VolumeCapacities.Values.ToArray();
            Assert.Equal(2, samples.Length);
            Assert.NotEqual(samples[0].VolumeIdentity, samples[1].VolumeIdentity);
            Assert.All(samples, sample =>
            {
                Assert.True(sample.WholeVolumeVerified);
                Assert.True(sample.MayShareCapacity);
            });
            Assert.Contains(model.Tree!.Snapshot.Nodes, node => node.Name == "filecat-apfs-capacity-clone.txt");
            Frame(window);
            var ribbon = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible &&
                AutomationProperties.GetAutomationId(button) == "AnalysisVolumeCapacity");
            Assert.Equal("Volume capacity for 2 locations", AutomationProperties.GetName(ribbon));
            Click(ribbon);
            Frame(window);
            var text = window.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToArray();
            Assert.All(samples, sample => Assert.Single(text, line => line?.Contains("Volume ID " + sample.VolumeIdentity, StringComparison.Ordinal) == true));
            Assert.Equal(2, text.Count(line => line?.Contains("may share capacity with other volumes", StringComparison.Ordinal) == true));
            Assert.DoesNotContain(text, line => line?.Contains("Difference from reported used space", StringComparison.Ordinal) == true);
            Click(window, "Close capacity");
            Assert.All(anchors, anchor => Assert.Equal("FileCat APFS capacity fixture", File.ReadAllText(anchor)));
        }
        finally
        {
            window.ForceClose();
            await window.WaitForIndexedCloseAsync();
            await model.CloseCompletion;
        }
    }

    [Theory]
    [InlineData(90, 10, "+")]
    [InlineData(10, 90, "−")]
    [InlineData(90, 90, "")]
    public void VolumeDifferencePreservesItsSign(int reportedUsed, int observed, string sign)
    {
        var expected = sign + DiskSpaceViewModel.Bytes((UInt128)Math.Abs(reportedUsed - observed));
        Assert.Equal(expected, DiskSpaceWindow.VolumeDifference((UInt128)reportedUsed, (UInt128)observed));
        Assert.Equal("−" + DiskSpaceViewModel.Bytes(UInt128.MaxValue), DiskSpaceWindow.VolumeDifference(0, UInt128.MaxValue));
    }
}
