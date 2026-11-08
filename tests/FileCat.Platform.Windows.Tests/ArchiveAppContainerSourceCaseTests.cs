using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    public static IEnumerable<object[]> SourceCaseProfiles()
    {
        foreach (var profile in ArchiveNativeJobCases.Profiles())
        foreach (bool parents in new[] { false, true }) yield return [profile[0], profile[1], parents];
    }

    [Theory, MemberData(nameof(SourceCaseProfiles))]
    public async Task Actual_application_native_case_jobs_keep_distinct_files_and_parents(ArchiveWriteFormat format, int mode, bool parents)
    {
        _ = Probe();
        string? template = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_CASE_ROOT");
        if (template is null) Assert.Skip("Explicit case-sensitive NTFS source fixture.");
        Assert.True(Path.IsPathFullyQualified(template));
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
        Assert.True(assets is not null && Path.IsPathFullyQualified(assets));
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!);
        string root = Directory.CreateTempSubdirectory("filecat-source-case-job-").FullName;
        string source = Path.Combine(template, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source); var ct = TestContext.Current.CancellationToken;
        try
        {
            string first = Path.Combine(source, parents ? "Root/payload" : "Case");
            string second = Path.Combine(source, parents ? "root/payload" : "case");
            Directory.CreateDirectory(Path.GetDirectoryName(first)!); Directory.CreateDirectory(Path.GetDirectoryName(second)!);
            byte[] a = [1, 2, 3], b = [7, 8, 9];
            File.WriteAllBytes(first, a); File.WriteAllBytes(second, b);
            Assert.Equal(a, File.ReadAllBytes(first)); Assert.Equal(b, File.ReadAllBytes(second));
            foreach (string path in new[] { first, second }) File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddSeconds(1700000000).AddTicks(7));
            using (var parent = WindowsArchiveDirectory.Open(Path.GetDirectoryName(first)!))
            using (var file = parent.OpenSource(Path.GetFileName(first)))
            using (var otherParent = WindowsArchiveDirectory.Open(Path.GetDirectoryName(second)!))
            using (var other = otherParent.OpenSource(Path.GetFileName(second))) Assert.NotEqual(file.Identity, other.Identity);
            var profile = ArchiveNativeJobCases.Profile(format, mode);
            string target = Path.Combine(root, "typed-output.zip"), journals = Path.Combine(root, "journals");
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals,
                new([WindowsArchiveJobBackend.Create(format, Path.Combine(assets!, "FileCat.exe"), retainArtifacts: true)]));
            manager.DecisionRequested += pending => Assert.Fail(pending.Request.Message);
            using var password = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
            var job = manager.SubmitArchive(new(target, null,
                [new(ArchiveChangeKind.AddFile, "first", first), new(ArchiveChangeKind.AddFile, "second", second)], Profile: profile), password);
            await ArchiveNativeJobCases.Finished(job);
            var expected = ArchiveNativeJobCases.Expected(Assert.Single(Directory.GetFiles(journals)), profile, ct);
            ArchiveNativeJobCases.Check(expected, new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["first"] = a, ["second"] = b });
            byte[] saved = File.ReadAllBytes(target);
            using var readingPassword = mode == 0 ? null : new ArchiveJobPassword(ArchiveNativeJobCases.Password);
            var observed = await new ArchiveAssessmentClient((nonce, cpu) => ArchiveAppContainer.StartAssessment(Path.Combine(assets!, "FileCat.exe"), nonce, cpu, 2), protocolVersion: 2)
                .AssessAsync(new(profile, saved.Length, Convert.ToHexString(SHA256.HashData(saved))), () => new MemoryStream(saved, false), ct, readingPassword?.CreateGrant());
            Assert.Equal(expected.Digest, observed.Manifest.Digest);
            Assert.Equal(a, File.ReadAllBytes(first)); Assert.Equal(b, File.ReadAllBytes(second));
            ArchiveNativeJobCases.Export(profile, parents ? "source-parents" : "source-leaves", saved, expected, "FILECAT_ARCHIVE_SOURCE_CASE_EXPORT_ROOT");
        }
        finally { TestCleanup.DeleteTree(root); TestCleanup.DeleteTree(source); }
    }
}
