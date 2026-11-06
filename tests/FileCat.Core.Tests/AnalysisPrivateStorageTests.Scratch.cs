using FileCat.Core.Analysis;
using System.Runtime.InteropServices;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisPrivateStorageTests
{
    [Fact]
    public async Task UnixNativeMetadataCaptureCoalescesActualHardLinksWithoutReadingFileContent()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native capture fixture."); Ordinary(); using var fixture = new NativeFixture();
        await FileCat.Tests.AnalysisObservationCaptureFixture.VerifyNative(fixture.Path, path => new UnixAnalysisBackend(path),
            () => new UnixAnalysisPrivateDirectory(fixture.Path, "scratch"), Cancellation);
    }

    [Fact]
    public async Task UnixNativeTypedGraphKeepsItsExactRelatedIndexesAfterOwnerClosure()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native indexed graph fixture."); Ordinary(); using var fixture = new NativeFixture();
        await FileCat.Tests.AnalysisIndexedGraphFixture.VerifyNative(() => new UnixAnalysisPrivateDirectory(fixture.Path, "scratch"), fixture.Path, Cancellation);
    }

    [Fact]
    public async Task UnixNativeExternalIndexKeepsExactRangesAndReleasesRunArtifacts()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native external index fixture."); Ordinary(); using var fixture = new NativeFixture();
        using var io = new DeviceIoScheduler(); string device = "native-sort-" + Guid.NewGuid().ToString("N");
        var workspace = await AnalysisIo.Run(io, device, _ => new AnalysisScratchWorkspace(new UnixAnalysisPrivateDirectory(fixture.Path, "scratch")), Cancellation);
        AnalysisSortedIndex? index = null; AnalysisSortedIndexReader? reader = null;
        try
        {
            using var source = Enumerable.Range(1, 1000).Reverse().Select(n => new AnalysisSortEntry([(byte)(n % 31), 0xff], (ulong)n)).GetEnumerator();
            index = await AnalysisExternalSort.BuildAsync(workspace, io, device, source, Cancellation, 4096);
            reader = await AnalysisIo.Run(io, device, _ => index.Acquire(), Cancellation);
            await AnalysisIo.Run(io, device, token =>
            {
                Assert.Equal(1000UL, reader.Count); Assert.Equal(33UL, reader.FindRange([7, 0xff], token).Count);
                Assert.Equal(reader.FindRange([7, 0xff], token), reader.PrefixRange([7], token)); return true;
            }, Cancellation);
        }
        finally
        {
            if (reader is not null) await AnalysisIo.ReleaseScratchAsync(io, device, reader);
            if (index is not null) await AnalysisIo.ReleaseScratchAsync(io, device, index);
            await AnalysisIo.ReleaseScratchAsync(io, device, workspace);
        }
        Assert.Equal(0, workspace.ReservedBytes); Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(fixture.Path, "scratch")));
    }

    [Fact]
    public void UnixNativeWorkspaceSharesDirectoryAndQuotaUntilAllArenaReadersClose()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native scratch workspace fixture."); Ordinary(); using var fixture = new NativeFixture();
        using var native = new UnixAnalysisPrivateDirectory(fixture.Path, "scratch");
        var profile = new AnalysisScratchBudget(1024 * 1024); using var workspace = new AnalysisScratchWorkspace(native, 512 * 1024, profile);
        using var data = workspace.CreateArena(); using var runs = workspace.CreateArena();
        var address = data.Append([1, 2, 3], Cancellation); runs.Append([4, 5], Cancellation); using var reader = data.Capture();
        workspace.Dispose(); runs.Dispose(); data.Dispose(); native.Verify(); Assert.Equal(51, profile.ReservedBytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, reader.Read(address, Cancellation)); reader.Dispose();
        Assert.Equal(0, profile.ReservedBytes); Assert.Throws<ObjectDisposedException>(() => native.Verify());
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(fixture.Path, "scratch")));
    }

    [Fact]
    public void UnixNativeRelatedTablesRetainOneCoherentRevisionAfterOwnerClosure()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native composite scratch fixture."); Ordinary(); using var fixture = new NativeFixture();
        var budget = new AnalysisScratchBudget(1024 * 1024);
        using var arena = new AnalysisScratchArena(new UnixAnalysisPrivateDirectory(fixture.Path, "scratch"), budget, 1024 * 1024);
        using var store = new AnalysisScratchComposite(arena, 10, 10, 10);
        for (int table = 0; table < 3; table++) store.Append(table, [(byte)(table + 1)], Cancellation);
        store.Publish(Cancellation); using var first = store.AcquireCommitted(); store.Dispose();
        using var retained = first.Retain(); first.Dispose();
        string foreign = Path.Combine(fixture.Path, "scratch", "user-file.txt"); File.WriteAllText(foreign, "preserve me");
        for (int table = 0; table < 3; table++) Assert.Equal(new byte[] { (byte)(table + 1) }, retained.Read(table, 1, Cancellation));
        Assert.True(budget.ReservedBytes > 0); retained.Dispose(); Assert.Equal(0, budget.ReservedBytes);
        Assert.Equal(new[] { foreign }, Directory.GetFiles(Path.GetDirectoryName(foreign)!)); Assert.Equal("preserve me", File.ReadAllText(foreign));
    }

    [Fact]
    public void LinuxScratchCannotBePublishedThroughItsKernelDescriptorButInventoryStagingCan()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "Linux O_TMPFILE publication fixture."); Ordinary(); using var fixture = new NativeFixture();
        using var directory = new UnixAnalysisPrivateDirectory(fixture.Path, "scratch");
        using var staging = directory.CreateStaging(); string prefix = Path.Combine(fixture.Path, "scratch") + "/#";
        string[] Descriptors() => Directory.GetFiles("/proc/self/fd").Where(path =>
        {
            string? target = new FileInfo(path).LinkTarget;
            return target is not null && target.StartsWith(prefix, StringComparison.Ordinal) && target.EndsWith(" (deleted)", StringComparison.Ordinal);
        }).ToArray();
        string heldStaging = Assert.Single(Descriptors()), positive = Path.Combine(fixture.Path, "scratch", "publication-control");
        Assert.Equal(0, LinkScratchDescriptor(-100, heldStaging, -100, positive, 0x400)); File.Delete(positive);
        using var scratch = directory.CreateScratch(); string descriptor = Assert.Single(Descriptors().Except([heldStaging]));
        string forbidden = Path.Combine(fixture.Path, "scratch", "must-not-be-published");
        try
        {
            int result = LinkScratchDescriptor(-100, descriptor, -100, forbidden, 0x400), error = Marshal.GetLastPInvokeError();
            Assert.Equal(-1, result); Assert.Equal(2, error); Assert.False(File.Exists(forbidden)); scratch.Verify();
        }
        finally { File.Delete(forbidden); }
    }

    [LibraryImport("libc", EntryPoint = "linkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinkScratchDescriptor(int oldDirectory, string source, int newDirectory, string target, int flags);

    [Fact]
    public async Task UnixNativeScratchHasNoPayloadNameAfterAbruptProcessDeath()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native scratch crash fixture."); Ordinary(); using var fixture = new NativeFixture();
        await FileCat.Tests.AnalysisScratchCrashFixture.KillOwnedChild(fixture.Path, Cancellation);
    }

    [Fact]
    public void UnixNativeScratchIsAnonymousAndItsReaderSurvivesWriterClosure()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native scratch fixture."); Ordinary(); using var fixture = new NativeFixture();
        var directory = new UnixAnalysisPrivateDirectory(fixture.Path, "scratch");
        var budget = new AnalysisScratchBudget(1024 * 1024); using var arena = new AnalysisScratchArena(directory, budget, 1024 * 1024);
        byte[] payload = Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray();
        var record = arena.Append(payload, Cancellation); using var checkpoint = arena.Capture();
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(fixture.Path, "scratch")));
        string foreign = Path.Combine(fixture.Path, "scratch", "user-file.txt"); File.WriteAllText(foreign, "preserve me");
        arena.Dispose(); Assert.True(budget.ReservedBytes > payload.Length); Assert.Equal(payload, checkpoint.Read(record, Cancellation));
        checkpoint.Dispose(); Assert.Equal(0, budget.ReservedBytes);
        Assert.Equal("preserve me", File.ReadAllText(foreign)); Assert.Single(Directory.GetFileSystemEntries(Path.GetDirectoryName(foreign)!));
    }

    [Fact]
    public void UnixScratchPrivacyOrAncestryLossRefusesFurtherIoAndNeverTouchesTheReplacement()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native scratch fixture."); Ordinary(); using var fixture = new NativeFixture();
        using var arena = new AnalysisScratchArena(new UnixAnalysisPrivateDirectory(fixture.Path, "scratch"));
        var record = arena.Append([1, 2, 3], Cancellation); using var checkpoint = arena.Capture(); string path = Path.Combine(fixture.Path, "scratch");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead);
        Assert.Throws<IOException>(() => checkpoint.Read(record, Cancellation));
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Assert.Equal(new byte[] { 1, 2, 3 }, checkpoint.Read(record, Cancellation));
        string moved = path + "-moved"; Directory.Move(path, moved); Directory.CreateDirectory(path);
        string foreign = Path.Combine(path, "user-file.txt"); File.WriteAllText(foreign, "preserve me");
        Assert.Throws<IOException>(() => arena.Append([4], Cancellation));
        Assert.Throws<IOException>(() => checkpoint.Read(record, Cancellation));
        checkpoint.Dispose(); arena.Dispose(); Assert.Equal("preserve me", File.ReadAllText(foreign)); Assert.Empty(Directory.GetFileSystemEntries(moved));
    }
}
