using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Tests;

internal static class ArchiveNativeJobCases
{
    internal const string Password = "Public-7z-worker-暗号-42829324";
    public static IEnumerable<object[]> Profiles()
    {
        foreach (var format in new[] { ArchiveWriteFormat.Tar, ArchiveWriteFormat.TarGzip, ArchiveWriteFormat.TarBzip2, ArchiveWriteFormat.TarZstd,
            ArchiveWriteFormat.Rar5, ArchiveWriteFormat.SevenZip })
        for (int mode = 0; mode <= (format is ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip ? 2 : 0); mode++) yield return [format, mode];
    }
    internal static ArchiveWriteProfile Profile(ArchiveWriteFormat format, int mode) => format switch
    {
        ArchiveWriteFormat.Rar5 => ArchiveWriteProfile.ForRar5(encryption: mode == 0 ? null : new((ArchiveEncryptionMode)mode)),
        ArchiveWriteFormat.SevenZip => ArchiveWriteProfile.ForSevenZip(compressionLevel: 1, dictionaryBytes: 1 << 20,
            encryption: mode == 0 ? null : new((ArchiveEncryptionMode)mode)),
        _ => new(format),
    };
    internal static IReadOnlyList<ArchiveChange> Fixture(string root)
    {
        foreach (string name in new[] { "a", "b", "a/tree", "a/tree/sub" }) Directory.CreateDirectory(Path.Combine(root, name));
        File.WriteAllBytes(Path.Combine(root, "a/payload"), "first payload é 暗号"u8.ToArray());
        File.WriteAllBytes(Path.Combine(root, "b/payload"), "second payload with different bytes"u8.ToArray());
        File.WriteAllBytes(Path.Combine(root, "a/tree/sub/file"), "nested captured content"u8.ToArray());
        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, (UnixFileMode)0x180);
            File.SetLastWriteTimeUtc(file, DateTime.UnixEpoch.AddSeconds(1700000000).AddTicks(7));
        }
        foreach (string directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, (UnixFileMode)0x1c0);
            Directory.SetLastWriteTimeUtc(directory, DateTime.UnixEpoch.AddSeconds(1700000001).AddTicks(7));
        }
        return [new(ArchiveChangeKind.AddFile, "Case", Path.Combine(root, "a/payload")),
            new(ArchiveChangeKind.AddFile, "case", Path.Combine(root, "b/payload")),
            new(ArchiveChangeKind.AddFile, "é", Path.Combine(root, "a/payload")),
            new(ArchiveChangeKind.AddFile, "é", Path.Combine(root, "b/payload")),
            new(ArchiveChangeKind.AddFolder, "renamed", Path.Combine(root, "a/tree")),
            new(ArchiveChangeKind.CreateFolderEntry, "empty/")];
    }
    internal static Dictionary<string, byte[]> Payloads(string root) => new(StringComparer.Ordinal)
    {
        ["Case"] = File.ReadAllBytes(Path.Combine(root, "a/payload")), ["case"] = File.ReadAllBytes(Path.Combine(root, "b/payload")),
        ["é"] = File.ReadAllBytes(Path.Combine(root, "a/payload")), ["é"] = File.ReadAllBytes(Path.Combine(root, "b/payload")),
        ["renamed/sub/file"] = File.ReadAllBytes(Path.Combine(root, "a/tree/sub/file")),
    };
    internal static async Task Finished(Job job)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(120));
        while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
        // Native recovery evidence is deliberately retained in this unregistered component.
        Assert.True(job.State == JobState.CompletedWithIssues, job.State + ": " + string.Join(" | ", job.Issues.Select(issue => issue.Message)));
        Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Committed);
        Assert.DoesNotContain(job.Issues, issue => issue.Severity == IssueSeverity.Error);
    }
    internal static ArchiveWriteManifest Expected(string journalPath, ArchiveWriteProfile profile, CancellationToken ct)
    {
        var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(journalPath).Transactions);
        var sidecar = Assert.Single(facts.Sidecars, item => item.Role == ArchiveSidecarRole.ExpectedOutput);
        var version = Assert.IsType<ArchiveObjectVersion>(sidecar.Version);
        using var input = File.OpenRead(sidecar.Path);
        using var reader = ArchiveManifestSidecar.OpenRead(input, new(version.Length, version.Sha256),
            facts.Nonce, ArchiveManifestPurpose.ExpectedOutput, profile, ct);
        var members = new List<ArchiveManifestMember>();
        while (reader.ReadNext() is { } member) members.Add(member);
        var result = new ArchiveWriteManifest(profile, members, reader.Features.ToArray());
        Assert.True(reader.IsComplete); Assert.Equal(reader.VerifiedManifestDigest, result.Digest); return result;
    }
    internal static void Check(ArchiveWriteManifest manifest, IReadOnlyDictionary<string, byte[]> expected)
    {
        var files = manifest.Members.Where(member => member.Type == ArchiveMemberType.File).ToDictionary(member => member.Name, StringComparer.Ordinal);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), files.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, bytes) in expected)
        {
            Assert.Equal(bytes.Length, files[name].Length); Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), files[name].Sha256);
        }
    }
    internal static void Export(ArchiveWriteProfile profile, string name, byte[] bytes, ArchiveWriteManifest expected,
        string exportVariable = "FILECAT_ARCHIVE_JOB_EXPORT_ROOT")
    {
        if (Environment.GetEnvironmentVariable(exportVariable) is not { } export) return;
        string directory = Path.Combine(export, $"format-{profile.Format}-mode-{(int)profile.Encryption.Mode}"); Directory.CreateDirectory(directory);
        string extension = profile.Format switch { ArchiveWriteFormat.Tar => ".tar", ArchiveWriteFormat.TarGzip => ".tar.gz", ArchiveWriteFormat.TarBzip2 => ".tar.bz2",
            ArchiveWriteFormat.TarZstd => ".tar.zst", ArchiveWriteFormat.Rar5 => ".rar", _ => ".7z" };
        File.WriteAllBytes(Path.Combine(directory, name + extension), bytes);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new { synthetic_public_vector = true,
            password = Password, format = (int)profile.Format, mode = (int)profile.Encryption.Mode,
            revision = Convert.ToHexString(SHA256.HashData(bytes)), features = Convert.ToHexString(expected.Features),
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            members = expected.Members.Select(member => new { name = member.Name, type = (int)member.Type, length = member.Length, sha256 = member.Sha256, metadata = Convert.ToHexString(member.Metadata) }) }));
    }
}
