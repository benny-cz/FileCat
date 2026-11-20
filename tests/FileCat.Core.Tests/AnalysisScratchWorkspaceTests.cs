using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisScratchWorkspaceTests
{
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void RunAndDataArenasShareOneSessionLimitAndRetainedReadersKeepTheirQuotaAndDirectory()
    {
        Ordinary(); var directory = new MemoryDirectory(); var profile = new AnalysisScratchBudget(200);
        using var workspace = new AnalysisScratchWorkspace(directory, 150, profile);
        using var data = workspace.CreateArena(); using var run = workspace.CreateArena();
        var address = data.Append(new byte[10], Cancellation); run.Append(new byte[10], Cancellation); using var reader = data.Capture();
        Assert.Equal(116, workspace.ReservedBytes); Assert.Equal(116, profile.ReservedBytes);
        Assert.Throws<AnalysisScratchLimitException>(() => data.Append([1], Cancellation)); Assert.Equal(116, profile.ReservedBytes);
        workspace.Dispose(); Assert.False(directory.Closed); Assert.Throws<ObjectDisposedException>(() => workspace.CreateArena());
        run.Dispose(); data.Dispose(); Assert.Equal(58, workspace.ReservedBytes); Assert.False(directory.Closed);
        Assert.Equal(new byte[10], reader.Read(address, Cancellation)); reader.Dispose();
        Assert.Equal(0, profile.ReservedBytes); Assert.Equal(0, workspace.ReservedBytes); Assert.True(directory.Closed); Assert.Equal(1, directory.Closes);
    }

    [Fact]
    public void ASeparateWorkspaceCannotBypassTheSharedProfileCeiling()
    {
        Ordinary(); var profile = new AnalysisScratchBudget(250);
        using var first = new AnalysisScratchWorkspace(new MemoryDirectory(), 200, profile);
        using var second = new AnalysisScratchWorkspace(new MemoryDirectory(), 200, profile);
        using var a = first.CreateArena(); using var b = second.CreateArena();
        a.Append(new byte[100], Cancellation); Assert.Equal(148, profile.ReservedBytes);
        Assert.Throws<AnalysisScratchLimitException>(() => b.Append(new byte[100], Cancellation)); Assert.Equal(0, second.ReservedBytes);
        a.Dispose(); Assert.Equal(0, profile.ReservedBytes);
        using var retry = second.CreateArena(); retry.Append(new byte[100], Cancellation); Assert.Equal(148, second.ReservedBytes);
    }

    [Fact]
    public async Task ConcurrentChildReservationsNeverOverdrawAndReleaseBothLevelsExactlyOnce()
    {
        var profile = new AnalysisScratchBudget(1000); var children = Enumerable.Range(0, 16).Select(_ => new AnalysisScratchBudget(100, profile)).ToArray();
        await Task.WhenAll(children.Select(child => Task.Run(() => { for (int n = 0; n < 100; n++) child.TryReserve(1); }, Cancellation)));
        Assert.Equal(1000, profile.ReservedBytes); Assert.Equal(1000, children.Sum(child => child.ReservedBytes));
        await Task.WhenAll(children.Select(child => Task.Run(() => child.Release(child.ReservedBytes), Cancellation)));
        Assert.Equal(0, profile.ReservedBytes); Assert.All(children, child => Assert.Equal(0, child.ReservedBytes));
    }

    [Fact]
    public void FailedArenaCreationDoesNotRetireTheWorkspaceOrLeaveAFileLease()
    {
        Ordinary(); var directory = new MemoryDirectory { FailNext = true }; using var workspace = new AnalysisScratchWorkspace(directory);
        Assert.Throws<IOException>(() => workspace.CreateArena()); Assert.False(directory.Closed);
        using var arena = workspace.CreateArena(); arena.Append([1], Cancellation); Assert.True(workspace.ReservedBytes > 0);
        arena.Dispose(); workspace.Dispose(); Assert.True(directory.Closed); Assert.Equal(1, directory.Closes);
    }

    private sealed class MemoryDirectory : AnalysisPrivateDirectory
    {
        internal bool Closed, FailNext; internal int Closes;
        internal override void Verify() => ObjectDisposedException.ThrowIf(Closed, this);
        internal override (UInt128 Capacity, UInt128 Available) Space() => ((UInt128)1024 * 1024 * 1024, (UInt128)1024 * 1024 * 1024);
        internal override AnalysisPrivateScratchFile CreateScratch() { if (FailNext) { FailNext = false; throw new IOException("Injected private file creation failure."); } return new MemoryFile(); }
        internal override AnalysisPrivateFile CreateStaging() => throw new NotSupportedException();
        internal override AnalysisPrivateFile OpenRead(string name) => throw new NotSupportedException();
        public override void Dispose() { Closed = true; Closes++; }
    }
    private sealed class MemoryFile : AnalysisPrivateScratchFile
    {
        private readonly MemoryStream _bytes = new();
        public override long Length => _bytes.Length;
        public override void Verify() { }
        public override int ReadAt(Span<byte> buffer, long offset) { _bytes.Position = offset; return _bytes.Read(buffer); }
        public override void WriteAt(ReadOnlySpan<byte> buffer, long offset) { _bytes.Position = offset; _bytes.Write(buffer); }
        public override void Dispose() => _bytes.Dispose();
    }
}
