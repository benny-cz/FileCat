using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeRuntimeRescanFactoryReopensOnlyItsCapturedRootsAndKeepsTheSuccessorIndependent(bool indexed)
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"); Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "first.bin"), "first");
        await using var original = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, indexedStorage: indexed);
        var first = await StartAcquiredAsync(original, fixture, input);
        File.WriteAllText(Path.Combine(input, "second.bin"), "second");
        await using var next = await original.BeginRescanAsync(cancellation: TestContext.Current.CancellationToken);
        var second = next.RuntimeSession; await second.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Equal(first.Snapshot().Identity.SessionId, second.Snapshot().Identity.SessionId);
        Assert.Equal(first.Snapshot().Identity.Epoch + 1, second.Snapshot().Identity.Epoch);
        Assert.DoesNotContain(first.Snapshot().Nodes, n => n.Name == "second.bin");
        Assert.Contains(second.Snapshot().Nodes, n => n.Name == "second.bin");
        await using var oldData = await first.AcquireDatasetAsync(); await using var newData = await second.AcquireDatasetAsync();
        if (indexed)
        {
            Assert.NotNull(oldData); Assert.NotNull(newData);
            var oldRows = await oldData.ReadPathsAsync(0); var oldFile = oldRows.Rows.Single(n => n.Name == "first.bin");
            var mapped = await AnalysisRefreshNavigation.MapAsync(oldData, newData, oldFile.Id);
            Assert.NotNull(mapped); Assert.Equal("first.bin", (await newData.ReadPathAsync(mapped.Value)).Name);
        }
        else { Assert.Null(oldData); Assert.Null(newData); }
        // Release borrowed data before closing producer owners; retained datasets have separate
        // coverage elsewhere. This assertion is about successor runtime authority, not leases.
        if (oldData is not null) await oldData.DisposeAsync();
        await original.DisposeAsync();
        await using var third = await next.BeginRescanAsync(cancellation: TestContext.Current.CancellationToken);
        await third.RuntimeSession.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Equal(second.Snapshot().Identity.Epoch + 1, third.RuntimeSession.Snapshot().Identity.Epoch);
        Assert.Equal("first", File.ReadAllText(Path.Combine(input, "first.bin")));
    }

    [AvaloniaFact]
    public async Task NativeRuntimeRescanFactoryRefusesChangedPhysicalRootsWithoutConsumingThePredecessor()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input"), moved = Path.Combine(fixture.Root, "original"); Directory.CreateDirectory(input);
        await using var original = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services);
        await StartAcquiredAsync(original, fixture, input);
        Directory.Move(input, moved); Directory.CreateDirectory(input);
        await Assert.ThrowsAsync<InvalidOperationException>(() => original.BeginRescanAsync());
        Directory.Delete(input); Directory.Move(moved, input);
        await using var next = await original.BeginRescanAsync();
        await next.RuntimeSession.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Equal(2, next.RuntimeSession.Snapshot().Identity.Epoch);
    }
}
