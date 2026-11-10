using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    public static IEnumerable<object[]> CodecUpdateModes()
    {
        foreach (var mode in ArchiveRar5NativeCases.Modes()) yield return [ArchiveWriteFormat.Rar5, mode[0], mode[1]];
        foreach (var mode in ArchiveSevenZipNativeCases.Modes()) yield return [ArchiveWriteFormat.SevenZip, mode[0], mode[1]];
    }

    [Theory] [MemberData(nameof(CodecUpdateModes))]
    public async Task Rar5_and_7z_update_stages_preserve_exact_native_origin_across_store_compression_and_password_modes(
        ArchiveWriteFormat format, int method, int mode)
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temporary = new TempDir(); string root = Canonical(temporary.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string folder = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
        File.SetUnixFileMode(folder, (UnixFileMode)0x1c0);
        string payload = Path.Combine(root, "payload"), next = Path.Combine(root, "next");
        Payload(payload, [7, 0, 255, 9]); Payload(next, [0, 128, 1, 255, 0]);
        byte[] mark = [0, 255, 9]; SetCreationSourceMark(payload, mark); SetCreationSourceMark(next, mark);
        var profile = format == ArchiveWriteFormat.Rar5 ? ArchiveRar5NativeCases.Profile(method, mode) :
            ArchiveSevenZipNativeCases.Profile(method, mode);
        string target = Path.Combine(root, format == ArchiveWriteFormat.Rar5 ? "result.rar" : "result.7z");
        using var directory = UnixArchiveDirectory.Open(root);
        using var first = new ArchiveCapturedInputs();
        directory.CaptureSource(first, 1, "payload", ct); directory.CaptureSourceDirectory(first, 2, "folder", ct); first.Seal(ct);
        var workers = new List<Child>();
        Child Launch(Guid nonce, string role) { var child = Child.Start(nonce, role); workers.Add(child); return child; }
        var assessor = new ArchiveAssessmentClient((nonce, _) => Launch(nonce, "archive-assess-v2"), protocolVersion: 2);
        var transform = new ArchiveTransformClient((nonce, _) => Launch(nonce, "archive-transform-v2"), protocolVersion: 2);
        var verifier = new ArchiveVerificationClient((nonce, _) => Launch(nonce, "archive-verify-v2"), protocolVersion: 2);
        if (format == ArchiveWriteFormat.Rar5)
            await ArchiveRar5NativeCases.Create(method, mode, root, first,
                (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum),
                directory.CreateArtifact, transform, verifier, ct);
        else
            await ArchiveSevenZipNativeCases.Create(method, mode, root, first,
                (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum),
                directory.CreateArtifact, transform, verifier, ct);
        byte[] before = File.ReadAllBytes(target);
        using var original = directory.OpenSource(Path.GetFileName(target));
        var originalState = Observe(original);
        Assert.Equal(mark, originalState.Origin.Bytes.ToArray());
        using var password = mode == 0 ? null : new ArchiveJobPassword(format == ArchiveWriteFormat.Rar5
            ? ArchiveRar5NativeCases.Password : ArchiveSevenZipNativeCases.Password);
        var assessment = await assessor.AssessAsync(new(profile, originalState.Version.Length, originalState.Version.Sha256),
            () => ((IArchiveRetainedFile)original).OpenReadView(originalState, ct), ct, password?.CreateGrant());
        using var second = new ArchiveCapturedInputs(); directory.CaptureSource(second, 3, "next", ct); second.Seal(ct);
        var plan = second.CreatePlan(profile, assessment, [], [new("added", ArchiveMemberType.File, 3)], ct);
        using var journal = ReservationJournal(root, profile);
        using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, target, plan,
            new(original, originalState), second.Bindings,
            (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum),
            directory.CreateArtifact, transform, verifier, ct, password: password);
        ArchiveJournaledTarAssertions.Complete(prepared, journal, plan, ct);
        Assert.Equal(mark, prepared.Verified.Stage.Origin.Bytes.ToArray());
        using (var held = File.OpenHandle(prepared.Facts.StagePath))
            Assert.Equal(mark, ArchiveUnixOrigin.Read(held, ct).Bytes.ToArray());
        Assert.Equal(before, File.ReadAllBytes(target));
        Assert.Equal(new byte[] { 0, 128, 1, 255, 0 }, File.ReadAllBytes(next));
        Assert.Equal(5, workers.Count);
        Assert.All(workers, worker => { Assert.True(worker.Reaped); Assert.True(worker.Disposed); });
    }
}
