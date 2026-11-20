using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Theory]
    [InlineData(ArchiveWriteFormat.Tar, false, 0)] [InlineData(ArchiveWriteFormat.Tar, true, 0)]
    [InlineData(ArchiveWriteFormat.Rar5, false, 0)] [InlineData(ArchiveWriteFormat.Rar5, true, 0)]
    [InlineData(ArchiveWriteFormat.Rar5, false, 1)] [InlineData(ArchiveWriteFormat.Rar5, true, 1)]
    [InlineData(ArchiveWriteFormat.Rar5, false, 2)] [InlineData(ArchiveWriteFormat.Rar5, true, 2)]
    [InlineData(ArchiveWriteFormat.SevenZip, false, 0)] [InlineData(ArchiveWriteFormat.SevenZip, true, 0)]
    [InlineData(ArchiveWriteFormat.SevenZip, false, 1)] [InlineData(ArchiveWriteFormat.SevenZip, true, 1)]
    [InlineData(ArchiveWriteFormat.SevenZip, false, 2)] [InlineData(ArchiveWriteFormat.SevenZip, true, 2)]
    public async Task New_archive_publication_preserves_the_exact_native_source_mark(ArchiveWriteFormat archiveFormat, bool empty, int encryption)
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
        var profile = archiveFormat switch
        {
            ArchiveWriteFormat.Tar => new ArchiveWriteProfile(ArchiveWriteFormat.Tar),
            ArchiveWriteFormat.Rar5 => ArchiveRar5NativeCases.Profile(1, encryption),
            ArchiveWriteFormat.SevenZip => ArchiveSevenZipNativeCases.Profile(1, encryption),
            _ => throw new ArgumentOutOfRangeException(nameof(archiveFormat))
        };
        string target = Path.Combine(root, archiveFormat switch
        {
            ArchiveWriteFormat.Rar5 => "result.rar", ArchiveWriteFormat.SevenZip => "result.7z", _ => "result.tar"
        });
        using var password = encryption == 0 ? null : new ArchiveJobPassword(archiveFormat == ArchiveWriteFormat.Rar5
            ? ArchiveRar5NativeCases.Password : ArchiveSevenZipNativeCases.Password);
        var plan = captured.CreatePlan(profile, null, [], [new("payload", ArchiveMemberType.File, 1)], ct);
        using var journal = ReservationJournal(root, profile);
        bool versioned = archiveFormat is ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip;
        ArchiveTransformClient transformer = versioned
            ? new((nonce, _) => Child.Start(nonce, "archive-transform-v2"), protocolVersion: 2)
            : new((nonce, _) => Child.Start(nonce, "archive-transform"));
        ArchiveVerificationClient verifier = versioned
            ? new((nonce, _) => Child.Start(nonce, "archive-verify-v2"), protocolVersion: 2)
            : new((nonce, _) => Child.Start(nonce, "archive-verify"));
        using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, target, plan, null, captured.Bindings,
            (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
            transformer, verifier, ct, password: password);
        Assert.Equal(wanted.Digest, prepared.Verified.Stage.Origin.Digest);
        Assert.Equal(bytes, prepared.Verified.Stage.Origin.Bytes.ToArray());
        var published = await ArchiveNewFilePublication.PublishAsync(journal, prepared, captured.Bindings, directory.CreateArtifact, ct);
        Assert.Equal(ArchiveTransactionPhase.CommittedWithRetainedArtifacts, published.Facts.Phase);
        Assert.Equal(wanted.Digest, published.Published.Origin.Digest);
        using (var result = File.OpenHandle(target))
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
