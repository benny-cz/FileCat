using System.Text;
using FileCat.Core.Archives;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsArchiveOriginEvidenceTests
{
    [Fact]
    public void Native_private_evidence_reopens_by_identity_and_preserves_all_urls_while_the_stage_gets_the_strongest_declaration()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows native archive provenance storage required.");
        Assert.SkipWhen(WindowsTokenAuthority.IsHighIntegrity, "Run native provenance storage at medium integrity.");
        string directory = Directory.CreateTempSubdirectory("filecat-archive-origin-evidence-").FullName;
        try
        {
            string originalPath = Path.Combine(directory, "original.bin"), inputPath = Path.Combine(directory, "input.bin");
            File.WriteAllBytes(originalPath, [1, 2, 3]); File.WriteAllBytes(inputPath, [4, 5, 6]);
            void Mark(string path, int zone, string url)
            {
                using var mark = new FileStream(path + ":Zone.Identifier", FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete);
                mark.Write(Encoding.UTF8.GetBytes($"[ZoneTransfer]\r\nZoneId={zone}\r\nHostUrl={url}\r\n"));
            }
            Mark(originalPath, 3, "https://a.test/original"); Mark(inputPath, 4, "https://b.test/input");
            using var root = WindowsArchiveDirectory.Open(directory);
            using var original = root.OpenSource("original.bin"); using var input = root.OpenSource("input.bin");
            var originalRecord = new ArchiveOriginEvidenceRecord(ArchiveOriginSourceKind.OriginalArchive,
                original.CaptureVersion(TestContext.Current.CancellationToken), original.ReadOrigin(TestContext.Current.CancellationToken));
            var inputRecord = new ArchiveOriginEvidenceRecord(ArchiveOriginSourceKind.File,
                input.CaptureVersion(TestContext.Current.CancellationToken), input.ReadOrigin(TestContext.Current.CancellationToken));
            Guid nonce = Guid.NewGuid(); var scope = new ArchiveOriginEvidenceScope(nonce, new(ArchiveWriteFormat.Tar), ArchiveOriginOperation.Update, 2);
            ArchiveWindowsOriginAggregate result; ArchiveObjectVersion version; string name; FileCat.Core.FileSystem.FileIdentity identity;
            using (var evidence = root.CreateArtifact(nonce, WindowsArchiveArtifactKind.Provenance, ArchiveWritePolicy.MaxMetadataBytes))
            {
                name = evidence.KnownName; identity = evidence.Identity;
                using (var aggregation = new ArchiveWindowsOriginAggregation(evidence, scope, TestContext.Current.CancellationToken))
                { aggregation.Add(originalRecord); aggregation.Add(inputRecord); result = aggregation.Complete(); }
                version = evidence.Seal(TestContext.Current.CancellationToken);
                Assert.Equal(result.Evidence.Length, version.Length); Assert.Equal(result.Evidence.Sha256, version.Sha256);
            }
            using var reopened = root.OpenRecoveryArtifact(name, identity, WindowsArchiveArtifactKind.Provenance, ArchiveWritePolicy.MaxMetadataBytes);
            Assert.Equal(version, reopened.CaptureVersion(TestContext.Current.CancellationToken));
            var reviewed = ArchiveWindowsOriginAggregation.Review(reopened, result.Evidence, scope, TestContext.Current.CancellationToken);
            Assert.Equal(result.RequiredOrigin.Digest, reviewed.RequiredOrigin.Digest);
            reopened.Position = 0;
            using (var evidence = new ArchiveOriginEvidenceReader(reopened, result.Evidence, scope, TestContext.Current.CancellationToken))
            {
                var first = Assert.IsType<ArchiveOriginEvidenceRecord>(evidence.ReadNext());
                var second = Assert.IsType<ArchiveOriginEvidenceRecord>(evidence.ReadNext());
                Assert.Equal(originalRecord.Source, first.Source); Assert.Equal(originalRecord.Origin.Digest, first.Origin.Digest);
                Assert.Equal(inputRecord.Source, second.Source); Assert.Equal(inputRecord.Origin.Digest, second.Origin.Digest);
                Assert.Contains("https://a.test/original", Encoding.UTF8.GetString(first.Origin.Bytes));
                Assert.Contains("https://b.test/input", Encoding.UTF8.GetString(second.Origin.Bytes));
                Assert.Null(evidence.ReadNext()); Assert.True(evidence.IsComplete);
            }
            using var stage = root.CreateArtifact(nonce, WindowsArchiveArtifactKind.Stage, 4096);
            stage.Write([7, 8, 9]); stage.Seal(TestContext.Current.CancellationToken);
            stage.CopyOriginNoReplace(reviewed.RequiredOrigin, TestContext.Current.CancellationToken);
            var actual = stage.ReadOrigin(TestContext.Current.CancellationToken);
            Assert.Equal(4, ArchiveWindowsOriginZone.Parse(actual).DeclaredZone);
            Assert.Contains("https://a.test/original", Encoding.UTF8.GetString(actual.Bytes));
            Assert.DoesNotContain("https://b.test/input", Encoding.UTF8.GetString(actual.Bytes)); // Preserved in evidence, never invented as the outer URL.
            Assert.Equal(3, ArchiveWindowsOriginZone.Parse(original.ReadOrigin(TestContext.Current.CancellationToken)).DeclaredZone);
            reopened.DeleteReviewed(version, reopened.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
            Assert.False(File.Exists(Path.Combine(directory, name)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
