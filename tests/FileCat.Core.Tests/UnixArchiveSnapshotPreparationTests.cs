using System.Formats.Tar;
using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Theory]
    [InlineData(ArchiveWriteFormat.Tar, false, false)] [InlineData(ArchiveWriteFormat.TarGzip, false, false)]
    [InlineData(ArchiveWriteFormat.TarBzip2, false, false)] [InlineData(ArchiveWriteFormat.TarZstd, false, false)]
    [InlineData(ArchiveWriteFormat.Tar, true, false)] [InlineData(ArchiveWriteFormat.Tar, false, true)]
    [InlineData(ArchiveWriteFormat.Tar, true, true)] [InlineData(ArchiveWriteFormat.TarGzip, true, true)]
    public async Task Snapshot_preparation_clones_exact_bytes_from_a_separate_native_source_and_keeps_the_target_unchanged(
        ArchiveWriteFormat format, bool targetMarked, bool snapshotMarked)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        string targetPath = Path.Combine(root, "target.tar"), snapshotPath = Path.Combine(root, "snapshot");
        Payload(targetPath, "current target baseline, not the retained archive"u8.ToArray());
        using (var data = File.Create(snapshotPath))
        using (Stream wrapper = format switch
        {
            ArchiveWriteFormat.Tar => data,
            ArchiveWriteFormat.TarGzip => new GZipStream(data, CompressionLevel.Optimal, true),
            ArchiveWriteFormat.TarBzip2 => SharpCompress.Compressors.BZip2.BZip2Stream.Create(data, SharpCompress.Compressors.CompressionMode.Compress, false, leaveOpen: true),
            ArchiveWriteFormat.TarZstd => new SharpCompress.Compressors.ZStandard.CompressionStream(data, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        })
        using (var writer = new TarWriter(wrapper, TarEntryFormat.Pax, leaveOpen: true))
        using (var bytes = new MemoryStream("old retained payload"u8.ToArray()))
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "old") { DataStream = bytes, ModificationTime = DateTimeOffset.UnixEpoch });
        byte[] mark = [0, 255, 9];
        if (targetMarked) SetCreationSourceMark(targetPath, mark);
        if (snapshotMarked) SetCreationSourceMark(snapshotPath, mark);
        byte[] expected = File.ReadAllBytes(snapshotPath), before = File.ReadAllBytes(targetPath);
        using var directory = UnixArchiveDirectory.Open(root); using var target = directory.OpenSource("target.tar"); using var snapshot = directory.OpenSource("snapshot");
        var current = Observe(target); var retained = Observe(snapshot); var ct = TestContext.Current.CancellationToken;
        using var journal = ReservationJournal(root); Child? assessor = null, verifier = null;
        using var prepared = await ArchiveJournaledSnapshotPreparation.PrepareAsync(journal, targetPath, new(format), new(target, current), new(snapshot, retained),
            (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
            new((nonce, _) => assessor = Child.Start(nonce)), new((nonce, _) => verifier = Child.Start(nonce, "archive-verify")), ct);
        Assert.Equal(expected, File.ReadAllBytes(prepared.Facts.StagePath)); Assert.Equal(before, File.ReadAllBytes(targetPath));
        Assert.Equal(expected, File.ReadAllBytes(snapshotPath)); Assert.Equal(current.Version, prepared.Facts.Original);
        Assert.True(prepared.Verified.Original!.Matches(current)); Assert.True(prepared.Verified.Inputs[0].Matches(retained));
        Assert.Equal(retained.Version.Length, prepared.Facts.StageMaximumLength); Assert.Equal(retained.Version.Sha256, prepared.Verified.Stage.Version.Sha256);
        var wanted = targetMarked || snapshotMarked
            ? ArchiveOriginSnapshot.Present(OperatingSystem.IsMacOS() ? ArchiveOriginFormat.MacQuarantine : ArchiveOriginFormat.LinuxOriginUrl, mark)
            : ArchiveOriginSnapshot.Absent(OperatingSystem.IsMacOS() ? ArchiveOriginFormat.MacQuarantine : ArchiveOriginFormat.LinuxOriginUrl);
        Assert.Equal(wanted.Digest, prepared.Verified.Stage.Origin.Digest); Assert.NotNull(prepared.Facts.OriginEvidence);
        Assert.Equal(2, prepared.Facts.OriginEvidenceSources); Assert.Equal(4, prepared.Sidecars.Count); Assert.True(journal.NeedsRecovery);
        Assert.NotNull(assessor); Assert.NotNull(verifier); Assert.NotSame(assessor, verifier);
        Assert.True(assessor.HasExited); Assert.True(verifier.HasExited); Assert.Equal(0u, assessor.ExitCode); Assert.Equal(0u, verifier.ExitCode);
    }
}
