using System.Runtime.InteropServices;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisScanningTests
{
    [Fact]
    public void NativeDeepSiblingEnumerationHasBoundedDescriptorChurn()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix descriptor-relative native fixture."); Ordinary();
        using var temp = new TempDir(); string root = Path.Combine(temp.Path, "source"), bottom = root;
        Directory.CreateDirectory(root);
        const int depth = 32, files = 64;
        for (int i = 0; i < depth; i++) { bottom = Path.Combine(bottom, "d"); Directory.CreateDirectory(bottom); }
        for (int i = 0; i < files; i++) File.WriteAllBytes(Path.Combine(bottom, $"f-{i:D4}.bin"), [7]);
        int closes = 0;
        using var backend = new UnixAnalysisBackend(root, fd => { closes++; return CloseRelativeProbe((int)fd) == 0; });
        var directory = backend.RootEntry;
        for (int i = 0; i < depth; i++) directory = Assert.Single(backend.Children(directory, TestContext.Current.CancellationToken));
        backend.ValidateReadScope(directory); closes = 0;
        var children = backend.Children(directory, TestContext.Current.CancellationToken).ToArray();
        Assert.Equal(files, children.Length); Assert.All(children, child => Assert.Equal(AnalysisKind.File, child.Kind));
        // Count real confirmed file-descriptor closes, not a stopwatch threshold. Siblings should
        // use their held directory; reopening every ancestor for every entry is unbounded by depth.
        Assert.InRange(closes, files, files + depth * 2 + 8);
        backend.ValidateReadScope(directory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeDirectoryReplacementDuringEnumerationCannotPublishEitherOccupant(bool restore)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix descriptor-relative native fixture."); Ordinary();
        using var temp = new TempDir(); string root = Path.Combine(temp.Path, "source"), directory = Path.Combine(root, "parent");
        Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "same.bin"), [7]);
        string moved = Path.Combine(temp.Path, "moved"); bool replaced = false, restored = false;
        var backend = new UnixAnalysisBackend(root, null, (entry, _) =>
        {
            if (entry.Name != "parent") return;
            Thread.Sleep(20); Directory.Move(directory, moved); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "same.bin"), new byte[99]); replaced = true;
        }, entry =>
        {
            if (restore && replaced && !restored && entry.Kind == AnalysisKind.File)
            {
                Directory.Delete(directory, true); Thread.Sleep(20); Directory.Move(moved, directory); restored = true;
            }
            return entry;
        });
        using var io = new DeviceIoScheduler(); using var session = new AnalysisCoordinator(io).Start([backend]);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        var snapshot = session.Snapshot(); Assert.True(replaced); Assert.Equal(restore, restored);
        Assert.Equal(NamespaceCoverage.Partial, snapshot.Coverage); Assert.Equal(AnalysisFreshness.PossiblyStale, snapshot.Freshness);
        Assert.DoesNotContain(snapshot.Nodes, node => node.Kind == AnalysisKind.File);
        Assert.Contains(snapshot.Issues, issue => issue.Code == "source-changed-or-boundary");
        Assert.Equal(0UL, session.SourceCloseFailures);
    }

    [Fact]
    public void NativeRelativeChildrenObserveLinksAndFifosWithoutFollowingOrReadingThem()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix descriptor-relative native fixture."); Ordinary();
        using var temp = new TempDir(); string root = Path.Combine(temp.Path, "source"), bottom = Path.Combine(root, "p", "d");
        Directory.CreateDirectory(bottom); string outside = Path.Combine(temp.Path, "outside"); Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "must-not-appear"), "outside canary");
        Directory.CreateSymbolicLink(Path.Combine(bottom, "link"), outside);
        Assert.Equal(0, MakeRelativeFifo(Path.Combine(bottom, "fifo"), 0x180));
        using var backend = new UnixAnalysisBackend(root);
        var parent = Assert.Single(backend.Children(backend.RootEntry, TestContext.Current.CancellationToken));
        var child = Assert.Single(backend.Children(parent, TestContext.Current.CancellationToken));
        backend.ValidateReadScope(child);
        var entries = backend.Children(child, TestContext.Current.CancellationToken).ToArray();
        backend.ValidateReadScope(child); Assert.Equal(2, entries.Length);
        Assert.Equal(AnalysisKind.Link, entries.Single(entry => entry.Name == "link").Kind);
        Assert.Equal(AnalysisKind.Special, entries.Single(entry => entry.Name == "fifo").Kind);
        Assert.DoesNotContain(entries, entry => entry.Name == "must-not-appear");
    }

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int CloseRelativeProbe(int descriptor);
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeRelativeFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
}
