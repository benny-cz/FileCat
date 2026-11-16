using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Tests;

internal static class ArchiveRar5NativeCases
{
    internal const string Password = "Public-RAR-Transform-暗号-42829316";
    public static IEnumerable<object[]> Modes() => from method in Enumerable.Range(0, 6) from mode in Enumerable.Range(0, 3) select new object[] { method, mode };
    internal static ArchiveWriteProfile Profile(int method, int mode) => ArchiveWriteProfile.ForRar5((ArchiveRar5Compression)method,
        mode == 0 ? null : new((ArchiveEncryptionMode)mode));
    internal static async Task Create<T>(int method, int mode, string root, ArchiveCapturedInputs captured,
        Func<Guid, ArchiveArtifactKind, long, ArchiveArtifactReservation> reserve, Func<ArchiveArtifactReservation, T> create,
        ArchiveTransformClient transform, ArchiveVerificationClient verifier, CancellationToken ct, string variant = "unix") where T : class, IArchiveRetainedStage
    {
        var profile = Profile(method, mode); string target = Path.Combine(root, "result.rar");
        var plan = captured.CreatePlan(profile, null, [], [captured.DirectoryAddition("tree/", 2), new("tree/payload.bin", ArchiveMemberType.File, 1)], ct);
        Assert.IsType<ArchiveRar5TransformPlan>(plan); Assert.Equal(captured.Cost, plan.CaptureCost); Assert.Same(captured, plan.CaptureOwner);
        using var password = mode == 0 ? null : new ArchiveJobPassword(Password);
        using var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest
        { Kind = JobKind.ArchiveUpdate, Archive = new(target, null, [], Profile: profile) }, "owned native RAR creation", "owned", [], []));
        using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, target, plan, null, captured.Bindings,
            reserve, create, transform, verifier, ct, password: password);
        Assert.False(File.Exists(target)); Assert.Equal(plan.Expected.Digest, prepared.Verified.Verification.ManifestDigest);
        Assert.Single(prepared.Verified.Directories); Assert.Equal(2, prepared.Facts.OriginEvidenceSources);
        ArchiveJournaledTarAssertions.Complete(prepared, journal, plan, ct);
        await ArchiveJournaledTarAssertions.PublishNew(prepared, journal, captured.Bindings, create, ct);
        byte[] published = ReadShared(target);
        // Publication retains the native DELETE-capable owner. A readback must share that capability while the
        // owner's own deny-write/replace pin continues to protect the published object.
        Assert.Equal(prepared.Verified.Stage.Version.Sha256, Convert.ToHexString(SHA256.HashData(published)));
        Assert.DoesNotContain(Password, System.Text.Encoding.UTF8.GetString(ReadShared(journal.Path)));
        if (password is not null) { using var fresh = password.CreateGrant(); Assert.True(fresh.UseOnce(value => value.SequenceEqual(Password))); }
        string? export = Environment.GetEnvironmentVariable("FILECAT_RAR_NATIVE_EXPORT_ROOT");
        if (export is not null)
        {
            string directory = Path.Combine(export, variant + "-method-" + method + "-mode-" + mode); Directory.CreateDirectory(directory);
            Export(directory, "created", published, plan.Expected);
        }
    }

    private static byte[] ReadShared(string path)
    {
        using var read = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream(); read.CopyTo(copy); return copy.ToArray();
    }

    internal static void Export(string directory, string name, byte[] bytes, ArchiveWriteManifest manifest)
    {
        Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name + ".rar"), bytes);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new { synthetic_public_vector = true, password = Password,
            digest = manifest.Digest, features = Convert.ToHexString(manifest.Features), members = manifest.Members.Select(member => new
            { name = member.Name, type = (int)member.Type, length = member.Length, sha256 = member.Sha256, metadata = Convert.ToHexString(member.Metadata) }) }));
    }
}
