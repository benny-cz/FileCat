using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Tests;

internal static class ArchiveSevenZipNativeCases
{
    internal const string Password = "Public-7z-Native-暗号-42829324";
    public static IEnumerable<object[]> Modes() => from method in Enumerable.Range(0, 7) from mode in Enumerable.Range(0, 3) select new object[] { method, mode };
    internal static ArchiveWriteProfile Profile(int method, int mode) => ArchiveWriteProfile.ForSevenZip(
        new((ArchiveSevenZipCodec)method, modelOrder: method == 6 ? 4 : 0, modelMemoryBytes: method == 6 ? 1 << 20 : 0),
        method == 0 ? 0 : 1, method is 1 or 2 ? 1 << 20 : 8 << 20,
        mode == 0 ? null : new((ArchiveEncryptionMode)mode));
    internal static async Task Create<T>(int method, int mode, string root, ArchiveCapturedInputs captured,
        Func<Guid, ArchiveArtifactKind, long, ArchiveArtifactReservation> reserve, Func<ArchiveArtifactReservation, T> create,
        ArchiveTransformClient transform, ArchiveVerificationClient verifier, CancellationToken ct, string variant = "unix") where T : class, IArchiveRetainedStage
    {
        var profile = Profile(method, mode); string target = Path.Combine(root, "result.7z");
        var plan = captured.CreatePlan(profile, null, [], [captured.DirectoryAddition("tree/", 2), new("tree/payload.bin", ArchiveMemberType.File, 1)], ct);
        Assert.IsType<ArchiveSevenZipTransformPlan>(plan); Assert.Equal(captured.Cost, plan.CaptureCost); Assert.Same(captured, plan.CaptureOwner);
        using var password = mode == 0 ? null : new ArchiveJobPassword(Password);
        using var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest
        { Kind = JobKind.ArchiveUpdate, Archive = new(target, null, [], Profile: profile) }, "owned native 7z creation", "owned", [], []));
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
        string? export = Environment.GetEnvironmentVariable("FILECAT_7Z_NATIVE_EXPORT_ROOT");
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
        Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name + ".7z"), bytes);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new { synthetic_public_vector = true, password = Password,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            digest = manifest.Digest, features = Convert.ToHexString(manifest.Features), members = manifest.Members.Select(member => new
            { name = member.Name, type = (int)member.Type, length = member.Length, sha256 = member.Sha256, metadata = Convert.ToHexString(member.Metadata) }) }));
    }
}
