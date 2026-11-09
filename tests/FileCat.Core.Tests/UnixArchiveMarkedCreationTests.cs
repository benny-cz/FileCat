using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task New_archive_publication_preserves_the_exact_native_source_mark(bool empty)
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string source = Path.Combine(root, "payload"); Payload(source, [7, 0, 255, 9]);
        byte[] bytes = empty ? [] : [0xff, 0, 0x80, 1]; SetCreationSourceMark(source, bytes);
        var format = OperatingSystem.IsMacOS() ? ArchiveOriginFormat.MacQuarantine : ArchiveOriginFormat.LinuxOriginUrl;
        var wanted = ArchiveOriginSnapshot.Present(format, bytes);
        using var directory = UnixArchiveDirectory.Open(root); using var captured = new ArchiveCapturedInputs();
        directory.CaptureSource(captured, 1, "payload", ct); captured.Seal(ct);
        Assert.Equal(wanted.Digest, captured.Bindings[1].Reviewed.Origin.Digest);
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        var plan = captured.CreatePlan(profile, null, [], [new("payload", ArchiveMemberType.File, 1)], ct);
        using var journal = ReservationJournal(root, profile);
        using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, Path.Combine(root, "result.tar"), plan, null, captured.Bindings,
            (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
            new((nonce, _) => Child.Start(nonce, "archive-transform")), new((nonce, _) => Child.Start(nonce, "archive-verify")), ct);
        Assert.Equal(wanted.Digest, prepared.Verified.Stage.Origin.Digest);
        Assert.Equal(bytes, prepared.Verified.Stage.Origin.Bytes.ToArray());
        var published = await ArchiveNewFilePublication.PublishAsync(journal, prepared, captured.Bindings, directory.CreateArtifact, ct);
        Assert.Equal(ArchiveTransactionPhase.CommittedWithRetainedArtifacts, published.Facts.Phase);
        Assert.Equal(wanted.Digest, published.Published.Origin.Digest);
        using (var result = File.OpenHandle(Path.Combine(root, "result.tar")))
            Assert.Equal(bytes, ArchiveUnixOrigin.Read(result, ct).Bytes.ToArray());
        Assert.Equal(new byte[] { 7, 0, 255, 9 }, File.ReadAllBytes(source));
        var evidence = published.Facts.Sidecars.Single(s => s.Role == ArchiveSidecarRole.CreationMetadata);
        Assert.Equal((ushort)3, BitConverter.ToUInt16(File.ReadAllBytes(evidence.Path), 8));
    }

    private static void SetCreationSourceMark(string path, byte[] bytes)
    {
        using var held = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        int fd = checked((int)held.DangerousGetHandle());
        int result = OperatingSystem.IsMacOS()
            ? MacSetCreationMark(fd, "com.apple.quarantine", bytes, bytes.Length, 0, 2)
            : LinuxSetCreationMark(fd, "user.xdg.origin.url", bytes, bytes.Length, 1);
        Assert.Equal(0, result);
    }
    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxSetCreationMark(int fd, string name, byte[] value, nint size, int flags);
    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacSetCreationMark(int fd, string name, byte[] value, nint size, uint position, int options);
}
