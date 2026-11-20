using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisScratchArenaTests
{
    [Fact]
    public void UnconfirmedFileCloseKeepsItsBytesAndDirectoryWhileOtherConfirmedFilesReleaseTheirOwnBytes()
    {
        Ordinary(); var quarantine = new AnalysisResourceQuarantine(); var parent = new AnalysisScratchBudget(4096);
        var budget = new AnalysisScratchBudget(2048, parent, quarantine: quarantine);
        var failed = new FakeDirectory(); var healthy = new FakeDirectory();
        var first = new AnalysisScratchArena(failed, budget, 2048); var second = new AnalysisScratchArena(healthy, budget, 2048);
        first.Append([1], Cancellation); second.Append([2, 3], Cancellation); long retained = first.ReservedBytes;
        var checkpoint = first.Capture(); first.Dispose(); failed.File.FailClose = true;
        Assert.Throws<AnalysisCloseUnconfirmedException>(() => checkpoint.Dispose());
        Assert.True(failed.File.Disposed); Assert.False(failed.Disposed); Assert.True(first.Failed); Assert.Equal(1, quarantine.Count);
        Assert.Equal(retained, first.ReservedBytes); Assert.Throws<AnalysisCloseUnconfirmedException>(() => budget.TryReserve(1));
        second.Dispose(); Assert.True(healthy.Disposed); Assert.Equal(retained, budget.ReservedBytes); Assert.Equal(retained, parent.ReservedBytes);
        first.Dispose(); checkpoint.Dispose(); Assert.Equal(retained, budget.ReservedBytes); Assert.Equal(1, quarantine.Count);
    }

    [Fact]
    public void WorkspaceRefusesNewNativeFilesBeforeOpeningThemAfterAnUnconfirmedFileClose()
    {
        Ordinary(); var quarantine = new AnalysisResourceQuarantine(); var parent = new AnalysisScratchBudget(4096); var directory = new FakeDirectory();
        var workspace = new AnalysisScratchWorkspace(directory, 2048, parent, quarantine: quarantine);
        var arena = workspace.CreateArena(); arena.Append([1], Cancellation); long retained = arena.ReservedBytes; directory.File.FailClose = true;
        Assert.Throws<AnalysisCloseUnconfirmedException>(() => arena.Dispose());
        Assert.Throws<AnalysisCloseUnconfirmedException>(() => workspace.CreateArena()); Assert.Throws<AnalysisCloseUnconfirmedException>(() => workspace.Borrow());
        Assert.Equal(1, directory.Creates);
        workspace.Dispose(); Assert.False(directory.Disposed); Assert.Equal(retained, workspace.ReservedBytes); Assert.Equal(retained, parent.ReservedBytes);
        Assert.Equal(1, quarantine.Count);
    }

    [Fact]
    public void FailedConstructorRollbackCannotDisposeTheDirectoryLeaseThatQuarantineStillOwns()
    {
        Ordinary(); var quarantine = new AnalysisResourceQuarantine(); var parent = new AnalysisScratchBudget(4096); var directory = new FakeDirectory();
        directory.File.Data.WriteByte(1); directory.File.FailClose = true;
        var workspace = new AnalysisScratchWorkspace(directory, 2048, parent, quarantine: quarantine);
        Assert.Throws<AnalysisCloseUnconfirmedException>(() => workspace.CreateArena()); workspace.Dispose();
        Assert.False(directory.Disposed); Assert.Equal(1, quarantine.Count); Assert.Equal(1, directory.Creates);
    }

    [Fact]
    public void UnconfirmedDirectoryCloseCannotReleaseItsRetainedCapacityOwner()
    {
        var quarantine = new AnalysisResourceQuarantine(); var directory = new FakeDirectory { FailClose = true }; var capacity = new CountedOwner();
        var shared = new AnalysisSharedPrivateDirectory(directory, capacity, quarantine);
        Assert.Throws<AnalysisCloseUnconfirmedException>(() => shared.Dispose()); Assert.True(directory.Disposed);
        Assert.Equal(0, capacity.Calls); Assert.Equal(1, quarantine.Count); shared.Dispose(); Assert.Equal(0, capacity.Calls);
    }
    private sealed class CountedOwner : IDisposable
    { internal int Calls; public void Dispose() => Calls++; }
}
