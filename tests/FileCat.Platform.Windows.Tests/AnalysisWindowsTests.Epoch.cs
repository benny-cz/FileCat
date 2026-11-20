using FileCat.Core.Analysis;
using FileCat.Core.Threading;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeWindowsRescanEpochAcceptsNewChildrenButRefusesAReplacedSelectedRoot(bool replaceRoot)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs an ordinary Windows process.");
        using var temp = new EpochTempDir(); string path = Path.Combine(temp.Path, "root"); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "first"), "one");
        using var io = new DeviceIoScheduler(); var coordinator = new AnalysisCoordinator(io, "epoch-profile");
        using var first = coordinator.Start([new WindowsAnalysisBackend(path)]);
        await first.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); var old = first.Snapshot();
        if (replaceRoot)
        {
            string moved = Path.GetFullPath(Path.Combine(temp.Path, "original"));
            Assert.Equal(Path.GetFullPath(temp.Path), Path.GetDirectoryName(Path.GetFullPath(path)));
            Assert.Equal(Path.GetFullPath(temp.Path), Path.GetDirectoryName(moved));
            Directory.Move(path, moved); Directory.CreateDirectory(path);
        }
        File.WriteAllText(Path.Combine(path, "second"), "two"); using var source = new WindowsAnalysisBackend(path);
        if (replaceRoot) Assert.Throws<InvalidOperationException>(() => coordinator.Rescan(first, [source]));
        else
        {
            using var second = coordinator.Rescan(first, [source]);
            await second.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); var next = second.Snapshot();
            Assert.Equal(3, next.Nodes.Length); Assert.Equal(old.Identity.SessionId, next.Identity.SessionId); Assert.Equal(2, next.Identity.Epoch);
            AnalysisValidation.Validate(next, TestContext.Current.CancellationToken);
        }
        Assert.Equal(old, first.Snapshot());
    }

    private sealed class EpochTempDir : IDisposable
    {
        private readonly string _base = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "filecat-epoch-native-tests"));
        public string Path { get; }
        internal EpochTempDir()
        {
            Path = System.IO.Path.Combine(_base, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            Assert.Equal(_base, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)));
            Assert.True(Guid.TryParseExact(System.IO.Path.GetFileName(Path), "N", out _));
            foreach (string folder in new[] { _base, Path })
                Assert.Equal(0, (int)(File.GetAttributes(folder) & FileAttributes.ReparsePoint));
            FileCat.Tests.TestCleanup.DeleteTree(Path);
        }
    }
}
