using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
    [Theory]
    [InlineData(false, AnalysisLimits.BatchBytes * 2 + 73)]
    [InlineData(true, 593)]
    [InlineData(true, 0)]
    public async Task NativeWindowsPrimaryContentReadsOnlyTheHeldPrimaryStream(bool fileRoot, int length)
    {
        byte[] extra = [51, 52, 53];
        using var fixture = new ContentFixture(length, fileRoot, extra);
        string stream = fixture.File + ":fixture-extra";
        var proof = await fixture.HashAsync(fixture.Capture());
        Assert.Equal(AnalysisContentEvidenceKind.StableContentProof, proof.Kind);
        Assert.Equal(7UL, proof.PathId);
        Assert.Equal(length, proof.BytesRead);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(fixture.Payload)), proof.Sha256);
        Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.File));
        Assert.Equal(extra, File.ReadAllBytes(stream));
        Assert.Equal(fixture.InitialHandles, AnalysisNativeHandles.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeWindowsPrimaryContentRefusesAnOutstandingWriterOrWritableMapping(bool mapped)
    {
        using var fixture = new ContentFixture(4096);
        var target = fixture.Capture();
        using (var writer = new FileStream(fixture.File, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
        {
            using var mapping = mapped
                ? MemoryMappedFile.CreateFromFile(writer, null, 0, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: true)
                : null;
            using var view = mapping?.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
            if (mapped) writer.Dispose();
            var error = await Assert.ThrowsAsync<IOException>(() => fixture.HashAsync(target));
            Assert.Contains(error.HResult & 0xffff, new[] { 32, 1224 }); // sharing violation or user-mapped file
        }
        Assert.Equal(fixture.InitialHandles, AnalysisNativeHandles.Count);
        Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.File));
    }

    [Fact]
    public async Task NativeWindowsPrimaryContentDeniesNewWritersThroughItsHardLinkDuringTheRead()
    {
        using var fixture = new ContentFixture(AnalysisLimits.BatchBytes * 2);
        string alias = Path.Combine(fixture.Parent, "alias.bin");
        Assert.True(HardLink(alias, fixture.File, 0));
        var held = await fixture.OpenAsync(fixture.Capture());
        try
        {
            Assert.True(await fixture.AdvanceAsync(held, TestContext.Current.CancellationToken));
            Assert.Equal(AnalysisLimits.BatchBytes, held.BytesRead);
            var denied = await Task.Run(() => Assert.Throws<IOException>(() =>
            {
                using var write = new FileStream(alias, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            }), TestContext.Current.CancellationToken);
            Assert.Equal(32, denied.HResult & 0xffff);
            Assert.ThrowsAny<IOException>(() => File.Move(fixture.File, fixture.File + ".rename"));
            Assert.False(await fixture.AdvanceAsync(held, TestContext.Current.CancellationToken));
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(fixture.Payload)), (await fixture.FinishAsync(held)).Sha256);
        }
        finally { await fixture.CloseAsync(held); }
        using (new FileStream(alias, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
        Assert.Equal(fixture.Payload, File.ReadAllBytes(alias));
        Assert.Equal(fixture.InitialHandles, AnalysisNativeHandles.Count);
    }

    [Fact]
    public async Task NativeWindowsPrimaryContentRefusesAnInPlaceSameSizeWriteWithLastWriteTimeRestored()
    {
        using var fixture = new ContentFixture(31);
        var previous = fixture.Capture();
        var modified = File.GetLastWriteTimeUtc(fixture.File);
        byte[] changed = [.. fixture.Payload.Select(value => (byte)~value)];
        File.WriteAllBytes(fixture.File, changed);
        File.SetLastWriteTimeUtc(fixture.File, modified);
        Assert.Equal(modified, File.GetLastWriteTimeUtc(fixture.File));
        var current = fixture.Capture();
        Assert.Equal(previous.Identity, current.Identity);
        Assert.Equal(previous.Bytes, current.Bytes);
        Assert.NotEqual(previous.Version, current.Version);
        await Assert.ThrowsAsync<AnalysisSourceChangedException>(() => fixture.HashAsync(previous));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(changed)), (await fixture.HashAsync(current)).Sha256);
        Assert.Equal(fixture.InitialHandles, AnalysisNativeHandles.Count);
    }

    [Fact]
    public async Task NativeWindowsPrimaryContentRefusesAReplacementAtTheRecordedName()
    {
        using var fixture = new ContentFixture(31);
        var first = fixture.Capture();
        var lastWrite = File.GetLastWriteTimeUtc(fixture.File);
        var sibling = fixture.File + ".prior";
        File.Move(fixture.File, sibling);
        var replacement = new byte[fixture.Payload.Length];
        File.WriteAllBytes(fixture.File, replacement);
        File.SetLastWriteTimeUtc(fixture.File, lastWrite);
        await Assert.ThrowsAsync<AnalysisSourceChangedException>(() => fixture.HashAsync(first));
        Assert.Equal(fixture.Payload, File.ReadAllBytes(sibling));
        Assert.Equal(replacement, File.ReadAllBytes(fixture.File));
        Assert.Equal(fixture.InitialHandles, AnalysisNativeHandles.Count);
    }

    [Fact]
    public async Task NativeWindowsPrimaryContentRefusesAClosedMappedWrite()
    {
        using var fixture = new ContentFixture(4096);
        var previous = fixture.Capture();
        byte changed = (byte)~fixture.Payload[0];
        using (var mapping = MemoryMappedFile.CreateFromFile(fixture.File, FileMode.Open, null, 0, MemoryMappedFileAccess.ReadWrite))
        using (var view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite))
        {
            view.Write(0, changed);
            view.Flush();
        }
        byte[] currentBytes = File.ReadAllBytes(fixture.File);
        Assert.Equal(changed, currentBytes[0]);
        var current = fixture.Capture();
        Assert.Equal(previous.Identity, current.Identity);
        Assert.Equal(previous.Bytes, current.Bytes);
        await Assert.ThrowsAsync<AnalysisSourceChangedException>(() => fixture.HashAsync(previous));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(currentBytes)), (await fixture.HashAsync(current)).Sha256);
    }

    [Fact]
    public async Task NativeWindowsPrimaryContentRefusesAReplacementDirectoryJunction()
    {
        using var fixture = new ContentFixture(31);
        using var elsewhere = new EpochTempDir();
        string otherAlias = Path.Combine(elsewhere.Path, Path.GetFileName(fixture.File));
        Assert.True(HardLink(otherAlias, fixture.File, 0));
        var target = fixture.Capture();
        string original = fixture.Parent + ".prior";
        Directory.Move(fixture.Parent, original);
        Junction.Create(fixture.Parent, elsewhere.Path);
        try
        {
            Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.File));
            await Assert.ThrowsAnyAsync<IOException>(() => fixture.HashAsync(target));
            Assert.Equal(fixture.Payload, File.ReadAllBytes(otherAlias));
            Assert.Equal(fixture.InitialHandles, AnalysisNativeHandles.Count);
        }
        finally { Directory.Delete(fixture.Parent); }
    }

    [Fact]
    public async Task NativeWindowsPrimaryContentRefusesARevisionWithoutTheJournalWitness()
    {
        using var fixture = new ContentFixture(32);
        var target = fixture.Capture();
        Assert.True(WindowsAnalysisBackend.HasPrimaryVersion(target.Version));
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.HashAsync(target with { Version = target.Version[..^21] }));
        Assert.Equal(fixture.InitialHandles, AnalysisNativeHandles.Count);
    }

    [Fact]
    public async Task NativeWindowsPrimaryContentRefusesReplacedAncestryEvenWithTheSameLeafObject()
    {
        using var fixture = new ContentFixture(41);
        string alternative = Path.Combine(fixture.Root, "alternative");
        Directory.CreateDirectory(alternative);
        Assert.True(HardLink(Path.Combine(alternative, Path.GetFileName(fixture.File)), fixture.File, 0));
        var previous = fixture.Capture();
        Directory.Move(fixture.Parent, fixture.Parent + ".prior");
        Directory.Move(alternative, fixture.Parent);
        var current = fixture.Capture();
        Assert.Equal(previous.Identity, current.Identity);
        Assert.Equal(previous.Version, current.Version);
        Assert.NotEqual(previous.Parents[0].Identity, current.Parents[0].Identity);
        await Assert.ThrowsAsync<AnalysisSourceChangedException>(() => fixture.HashAsync(previous));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(fixture.Payload)), (await fixture.HashAsync(current)).Sha256);
        Assert.Equal(fixture.InitialHandles, AnalysisNativeHandles.Count);
    }

    [Fact]
    public async Task NativeWindowsPrimaryContentCancellationRetiresTheReadAndReleasesItsDeviceAndHandles()
    {
        using var fixture = new ContentFixture(AnalysisLimits.BatchBytes + 7);
        var target = fixture.Capture();
        using var cancel = new CancellationTokenSource();
        var held = await fixture.OpenAsync(target);
        try
        {
            Assert.True(await fixture.AdvanceAsync(held, cancel.Token));
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.AdvanceAsync(held, cancel.Token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.AdvanceAsync(held, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.FinishAsync(held));
            Assert.InRange(held.BytesRead, 1, target.Bytes - 1);
            Assert.True(AnalysisNativeHandles.Count > fixture.InitialHandles);
        }
        finally { await fixture.CloseAsync(held); }
        Assert.Equal(fixture.InitialHandles, AnalysisNativeHandles.Count);
        Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.File));
        Assert.Equal(target.Bytes, (await fixture.HashAsync(target)).BytesRead);
    }

    [Fact]
    public async Task NativeWindowsPrimaryContentRejectsNamesThatCouldSelectOutsideThePrimaryEntry()
    {
        using var fixture = new ContentFixture(73);
        var target = fixture.Capture();
        foreach (var name in new[] { target.Name + ":fixture-extra", @"..\directory\data.bin", @"C:\fixture.bin", "." })
            await Assert.ThrowsAsync<NotSupportedException>(() => fixture.HashAsync(target with { Name = name }));
        Assert.Equal(fixture.InitialHandles, AnalysisNativeHandles.Count);
        Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.File));
    }

    private sealed class ContentFixture : IDisposable
    {
        private readonly EpochTempDir _temp;
        private readonly DeviceIoScheduler _io = new();
        private readonly WindowsAnalysisBackend _source;
        private readonly AnalysisIoDevices _device;
        private readonly bool _fileRoot;
        internal string Root => _temp.Path;
        internal string Parent { get; }
        internal string File { get; }
        internal byte[] Payload { get; }
        internal int InitialHandles { get; }

        internal ContentFixture(int length, bool fileRoot = false, byte[]? extra = null)
        {
            Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs an ordinary Windows process.");
            _temp = new EpochTempDir();
            Assert.SkipWhen(new DriveInfo(Path.GetPathRoot(Root)!).DriveFormat != "NTFS", "Needs a local NTFS fixture.");
            _fileRoot = fileRoot;
            Parent = Path.Combine(Root, "selected");
            Directory.CreateDirectory(Parent);
            File = Path.Combine(Parent, "data.bin");
            Payload = new byte[length];
            RandomNumberGenerator.Fill(Payload);
            System.IO.File.WriteAllBytes(File, Payload);
            if (extra is not null) System.IO.File.WriteAllBytes(File + ":fixture-extra", extra);
            _source = new(fileRoot ? File : Root);
            _device = ((IAnalysisBackend)_source).IoDeviceKey;
            InitialHandles = AnalysisNativeHandles.Count;
        }

        internal AnalysisPrimaryContentTarget Capture()
        {
            using var observed = new WindowsAnalysisBackend(_fileRoot ? File : Root);
            var root = observed.RootEntry;
            var token = TestContext.Current.CancellationToken;
            if (_fileRoot) return Target(root, null, []);
            var folder = observed.Children(root, token).Single(entry => entry.Name == "selected");
            var file = observed.Children(folder, token).Single(entry => entry.Name == "data.bin");
            return Target(file, DirectoryEvidence(root), [DirectoryEvidence(folder)]);

            static AnalysisPrimaryContentTarget Target(AnalysisEntry file, AnalysisBranchComponent? directory,
                System.Collections.Immutable.ImmutableArray<AnalysisBranchComponent> parents) =>
                new(7, directory, parents, file.Name, file.Identity!, file.Version, file.CreatedUtc!.Value,
                    file.CreatedPrecisionTicks!.Value, (long)file.LogicalLength.Value!.Value);
            static AnalysisBranchComponent DirectoryEvidence(AnalysisEntry entry) =>
                new(entry.Name, entry.Identity!, entry.CreatedUtc!.Value, entry.CreatedPrecisionTicks!.Value);
        }

        internal Task<IAnalysisBoundContent> OpenAsync(AnalysisPrimaryContentTarget target) =>
            AnalysisIo.Run(_io, _device, token => _source.OpenPrimaryContent(target, token), TestContext.Current.CancellationToken);
        internal Task<bool> AdvanceAsync(IAnalysisBoundContent content, CancellationToken cancellation) =>
            AnalysisIo.Run(_io, _device, _ => content.ReadNext(cancellation), TestContext.Current.CancellationToken);
        internal Task<AnalysisPrimaryContentProof> FinishAsync(IAnalysisBoundContent content) =>
            AnalysisIo.Run(_io, _device, content.Complete, TestContext.Current.CancellationToken);
        internal Task CloseAsync(IAnalysisBoundContent content) => AnalysisIo.ReleaseContentAsync(_io, _device, content);

        internal async Task<AnalysisPrimaryContentProof> HashAsync(AnalysisPrimaryContentTarget target)
        {
            var content = await OpenAsync(target);
            try
            {
                while (await AdvanceAsync(content, TestContext.Current.CancellationToken)) { }
                return await FinishAsync(content);
            }
            finally { await CloseAsync(content); }
        }

        public void Dispose()
        {
            try { _source.Dispose(); _temp.Dispose(); }
            finally { _io.Dispose(); }
        }
    }
}
