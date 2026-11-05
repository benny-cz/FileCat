using FileCat.Core.Analysis;
using System.Runtime.InteropServices;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisPrivateStorageTests
{
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
