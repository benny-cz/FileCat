using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Analysis;
using FileCat.Core.State;

namespace FileCat.Core.Tests;

[Collection("Analysis native source device")]
public sealed partial class AnalysisPrivateStorageTests
{
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    private static readonly Guid InventoryId = Guid.Parse("ffffffff-1111-1111-1111-111111111111");
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("../bad.fcdsa")]
    [InlineData("bad\\name.fcdsa")]
    [InlineData("file.fcdsa:stream")]
    [InlineData("NUL.fcdsa")]
    [InlineData("COM1.fcdsa")]
    [InlineData("bad.fcdsa ")]
    [InlineData("bad.FCDSA")]
    [InlineData("file.json")]
    public void DestinationNamesCannotSelectPathsStreamsDevicesOrOtherFormats(string name) =>
        Assert.Throws<ArgumentException>(() => AnalysisPrivateInventoryStore.ValidateName(name));

    [Fact]
    public void PortableUnicodeNamesIncludeSupplementaryScalarsButRefuseUnpairedUnits()
    {
        AnalysisPrivateInventoryStore.ValidateName("地图😀.fcdsa");
        Assert.Throws<ArgumentException>(() => AnalysisPrivateInventoryStore.ValidateName("bad\ud800.fcdsa"));
    }

    [Theory]
    [InlineData("write")]
    [InlineData("flush")]
    [InlineData("space")]
    [InlineData("privacy")]
    [InlineData("cancel")]
    public void PrecommitFailuresDoNotPublishAndCloseTheirFileLease(string fault)
    {
        Ordinary(); using var cancel = new CancellationTokenSource();
        using var directory = new FaultDirectory(fault, cancel);
        using var store = new AnalysisPrivateInventoryStore(directory);
        var exception = Record.Exception(() => store.SaveNew("test.fcdsa", AnalysisAccountingTests.Golden(), InventoryId, cancel.Token));
        Assert.NotNull(exception); Assert.IsNotType<AnalysisPublicationUncertainException>(exception);
        Assert.False(directory.Published); Assert.True(directory.FileDisposed);
    }

    [Fact]
    public void PostcommitFailureIsExplicitlyUncertainAndNeverInvokesAnotherPublication()
    {
        Ordinary(); using var directory = new FaultDirectory("postcommit", new());
        using var store = new AnalysisPrivateInventoryStore(directory);
        var exception = Assert.Throws<AnalysisPublicationUncertainException>(() => store.SaveNew("test.fcdsa", AnalysisAccountingTests.Golden(), InventoryId, Cancellation));
        Assert.Equal("test.fcdsa", exception.InventoryName); Assert.True(directory.Published); Assert.Equal(1, directory.Publications);
        Assert.True(directory.FileDisposed);
    }

    [Fact]
    public void ReserveHasTheSpecifiedMinimumPercentageAndCap()
    {
        Assert.Equal((UInt128)(256UL * 1024 * 1024), AnalysisPrivateInventoryStore.RequiredReserve(1024UL * 1024 * 1024));
        Assert.Equal((UInt128)1_000_000_000, AnalysisPrivateInventoryStore.RequiredReserve(100_000_000_000));
        Assert.Equal((UInt128)1_000_000_001, AnalysisPrivateInventoryStore.RequiredReserve(100_000_000_001));
        Assert.Equal((UInt128)(2UL * 1024 * 1024 * 1024), AnalysisPrivateInventoryStore.RequiredReserve(UInt128.MaxValue));
    }

    [Fact]
    public void PrivatePathsStayInSelectedDataAndProfileWithoutEagerOrdinaryCreation()
    {
        using var temp = new TempDir();
        var a = AppPaths.Resolve(dataRoot: temp.Path); var b = AppPaths.Resolve("other", dataRoot: temp.Path);
        Assert.StartsWith(a.ListingScratchDirectory + Path.DirectorySeparatorChar, a.AnalysisScratchDirectory);
        Assert.StartsWith(a.LocalDirectory + Path.DirectorySeparatorChar, a.AnalysisSnapshotsDirectory);
        Assert.StartsWith(a.AnalysisSnapshotsDirectory + Path.DirectorySeparatorChar, a.AnalysisCatalogFile);
        Assert.StartsWith(a.AnalysisSnapshotsDirectory + Path.DirectorySeparatorChar, a.AnalysisPrivateStateFile);
        Assert.NotEqual(a.AnalysisSnapshotsDirectory, b.AnalysisSnapshotsDirectory);
        Assert.NotEqual(a.AnalysisScratchDirectory, b.AnalysisScratchDirectory);
        Assert.Contains(a.WriteFolders, f => f.Folder == a.AnalysisScratchDirectory);
        Assert.Contains(a.WriteFolders, f => f.Folder == a.AnalysisSnapshotsDirectory);
        Assert.False(Directory.Exists(a.AnalysisScratchDirectory)); Assert.False(Directory.Exists(a.AnalysisSnapshotsDirectory));
    }

    [Fact]
    public void UnixNativeCreateNewRoundTripConflictsAndInterruptedWritesKeepExistingBytes()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native storage fixture."); Ordinary();
        using var fixture = new NativeFixture(); string saved = Path.Combine(fixture.Path, "saved"), target = Path.Combine(saved, "first.fcdsa");
        using var directory = new UnixAnalysisPrivateDirectory(fixture.Path, "saved");
        var space = directory.Space();
        Assert.Equal((UInt128)(ulong)new DriveInfo(fixture.Path).TotalSize, space.Capacity);
        Assert.InRange(space.Available, (UInt128)0, space.Capacity);
        using (var store = new AnalysisPrivateInventoryStore(directory))
        {
            var receipt = store.SaveNew("first.fcdsa", AnalysisAccountingTests.Golden(), InventoryId, Cancellation);
            Assert.Equal(InventoryId, receipt.InventoryId); Assert.Equal(new FileInfo(target).Length, receipt.Bytes);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(saved));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(target));
            byte[] before = File.ReadAllBytes(target);
            using (var loaded = store.Load("first.fcdsa", Cancellation))
            { Assert.Equal(InventoryId, loaded.InventoryId); Assert.Equal(AnalysisOrigin.ImportedInventory, loaded.Snapshot.Origin); }
            Assert.Throws<IOException>(() => store.SaveNew("first.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), Cancellation));
            Assert.Equal(before, File.ReadAllBytes(target));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => store.SaveNew("cancel.fcdsa", AnalysisAccountingTests.Golden(), InventoryId, cancel.Token));
            var invalid = AnalysisAccountingTests.Golden() with { Roots = [] };
            Assert.Throws<InvalidDataException>(() => store.SaveNew("invalid.fcdsa", invalid, InventoryId, Cancellation));
            Assert.Equal(new[] { "first.fcdsa" }, Directory.GetFiles(saved).Select(Path.GetFileName));
        }
        using var reopened = new AnalysisPrivateInventoryStore(new UnixAnalysisPrivateDirectory(fixture.Path, "saved"));
        using var reloaded = reopened.Load("first.fcdsa", Cancellation); Assert.Equal(InventoryId, reloaded.InventoryId);
        var unicode = reopened.SaveNew("地图😀.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), Cancellation);
        using var unicodeReloaded = reopened.Load(unicode.Name, Cancellation); Assert.Equal(unicode.InventoryId, unicodeReloaded.InventoryId);
    }

    [Fact]
    public void UnixNativeRefusesLinksBroadPermissionsAndMovedAncestry()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native storage fixture."); Ordinary(); using var fixture = new NativeFixture();
        string saved = Path.Combine(fixture.Path, "saved"), outside = Path.Combine(fixture.Path, "outside"); Directory.CreateDirectory(outside);
        using var store = new AnalysisPrivateInventoryStore(new UnixAnalysisPrivateDirectory(fixture.Path, "saved"));
        store.SaveNew("one.fcdsa", AnalysisAccountingTests.Golden(), InventoryId, Cancellation);
        string target = Path.Combine(saved, "one.fcdsa"), alias = Path.Combine(outside, "alias");
        Assert.Equal(0, Link(target, alias));
        Assert.Throws<IOException>(() => store.Load("one.fcdsa", Cancellation)); File.Delete(alias);
        File.CreateSymbolicLink(Path.Combine(saved, "link.fcdsa"), target);
        Assert.Throws<IOException>(() => store.Load("link.fcdsa", Cancellation));
        Assert.Throws<IOException>(() => store.SaveNew("link.fcdsa", AnalysisAccountingTests.Golden(), InventoryId, Cancellation));
        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
        Assert.Throws<IOException>(() => store.Load("one.fcdsa", Cancellation));
        File.SetUnixFileMode(saved, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead);
        Assert.Throws<IOException>(() => store.SaveNew("refused.fcdsa", AnalysisAccountingTests.Golden(), InventoryId, Cancellation));
        File.SetUnixFileMode(saved, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string moved = saved + "-moved"; Directory.Move(saved, moved); Directory.CreateSymbolicLink(saved, outside);
        Assert.Throws<IOException>(() => store.SaveNew("redirected.fcdsa", AnalysisAccountingTests.Golden(), InventoryId, Cancellation));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
        Assert.Throws<IOException>(() => new UnixAnalysisPrivateDirectory(saved, "child"));
        Directory.Delete(saved); // The link itself, never its target.
    }

    [Fact]
    public void MacNativeExtendedAclGrantIsRefusedBeforePayloadCreation()
    {
        Assert.SkipWhen(!OperatingSystem.IsMacOS(), "Darwin ACL fixture."); Ordinary(); using var fixture = new NativeFixture();
        string saved = Path.Combine(fixture.Path, "saved");
        using (var directory = new UnixAnalysisPrivateDirectory(fixture.Path, "saved")) { }
        var start = new ProcessStartInfo("/bin/chmod") { RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("+a"); start.ArgumentList.Add("everyone allow read,search"); start.ArgumentList.Add(saved);
        using var process = Process.Start(start)!; Assert.True(process.WaitForExit(5000)); Assert.Equal(0, process.ExitCode);
        Assert.Throws<IOException>(() => new UnixAnalysisPrivateDirectory(fixture.Path, "saved"));
        Assert.Empty(Directory.GetFileSystemEntries(saved));
    }

    private sealed class NativeFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(Environment.CurrentDirectory, ".tmp", "analysis-private-" + Guid.NewGuid().ToString("N"));
        public NativeFixture() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8)] private static partial int Link(string existing, string alias);

    private sealed class FaultDirectory(string fault, CancellationTokenSource cancel) : AnalysisPrivateDirectory
    {
        public bool Published, FileDisposed;
        public int Publications;
        private int _checks;
        internal override void Verify()
        {
            if (fault == "privacy" && ++_checks > 1) throw new IOException("Private ownership was lost.");
        }
        internal override (UInt128 Capacity, UInt128 Available) Space() => ((UInt128)1024 * 1024 * 1024, fault == "space" ? 0 : (UInt128)1024 * 1024 * 1024);
        internal override AnalysisPrivateFile CreateStaging() => new FaultFile(this, fault, cancel);
        internal override AnalysisPrivateFile OpenRead(string name) => throw new NotSupportedException();
        public override void Dispose() { }
        private sealed class FaultFile(FaultDirectory owner, string fault, CancellationTokenSource cancel) : AnalysisPrivateFile
        {
            private readonly FaultStream _stream = new(fault);
            public override Stream Stream => _stream;
            public override void Verify() { if (fault == "cancel") cancel.Cancel(); }
            public override void Flush() { if (fault == "flush") throw new IOException("Flush failed."); }
            public override void PublishNew(string name)
            {
                owner.Publications++; owner.Published = true;
                if (fault == "postcommit") throw new AnalysisPublicationUncertainException(name, new IOException("Postcommit sync failed."));
            }
            public override void Dispose() { owner.FileDisposed = true; _stream.Dispose(); }
        }
        private sealed class FaultStream(string fault) : MemoryStream
        {
            public override void Write(ReadOnlySpan<byte> buffer)
            { if (fault == "write") throw new IOException("Write failed."); base.Write(buffer); }
        }
    }
}
