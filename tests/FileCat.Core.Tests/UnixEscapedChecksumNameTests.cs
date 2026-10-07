using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class UnixEscapedChecksumNameTests
{
    [Theory]
    [InlineData("escaped", ChecksumKind.Sha256)]
    [InlineData("escaped", ChecksumKind.Md5)]
    [InlineData("generated", ChecksumKind.Sha256)]
    [InlineData("generated", ChecksumKind.Md5)]
    [InlineData("ordinary", ChecksumKind.Sha256)]
    [InlineData("ordinary", ChecksumKind.Md5)]
    [InlineData("legacy", ChecksumKind.Sha256)]
    [InlineData("legacy", ChecksumKind.Md5)]
    public async Task Escaped_GNU_literal_names_do_not_select_a_directory_lookalike(string mode, ChecksumKind kind)
    {
        if (OperatingSystem.IsWindows() && mode is "escaped" or "generated")
        {
            Assert.Skip("An actual Unix filename containing a literal backslash cannot be materialized on Windows.");
            return;
        }
        using var owned = new TempDir();
        string literal = Path.Join(owned.Path, @"owned\child.bin");
        string nested = Path.Join(owned.Path, "owned", "child.bin");
        string ordinary = Path.Join(owned.Path, "ordinary.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        byte[] literalBytes = "changed owned literal file"u8.ToArray(), nestedBytes = "expected directory lookalike"u8.ToArray();
        if (!OperatingSystem.IsWindows()) File.WriteAllBytes(literal, literalBytes);
        File.WriteAllBytes(nested, nestedBytes);
        File.WriteAllBytes(ordinary, literalBytes);
        string Hash(byte[] b) => Convert.ToHexStringLower(kind == ChecksumKind.Md5 ? MD5.HashData(b) : SHA256.HashData(b));
        string intended = mode is "escaped" or "generated" ? literal : mode == "legacy" ? nested : ordinary;
        string expected = Hash(mode is "escaped" or "legacy" ? nestedBytes : literalBytes);
        string line = mode switch
        {
            "escaped" => @"\" + expected + @"  owned\\child.bin",
            "generated" => Checksums.ManifestLine(kind, expected, @"owned\child.bin"),
            "legacy" => expected + @"  owned\child.bin",
            _ => Checksums.ManifestLine(kind, expected, "ordinary.bin")
        };
        string manifest = Path.Join(owned.Path, "owned" + Checksums.Extension(kind));
        File.WriteAllText(manifest, line + "\n", new UTF8Encoding(false));
        var parsed = Assert.Single(ChecksumManifests.Load(manifest).Entries);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Join(owned.Path, "journal"));
        var job = jobs.Submit(new JobRequest { Kind = JobKind.VerifyChecksums, Sources = [ItemRef.ForFileSystemPath(manifest, EntryKind.File)] });
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
            bool unchanged = File.ReadAllBytes(nested).SequenceEqual(nestedBytes) &&
                File.ReadAllBytes(ordinary).SequenceEqual(literalBytes) &&
                (OperatingSystem.IsWindows() || File.ReadAllBytes(literal).SequenceEqual(literalBytes));
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new
            {
                mode, Kind = kind.ToString(), Line = line, ParsedName = parsed.Name, ParsedPath = parsed.Path,
                IntendedPath = intended, ParsedSelectsIntendedPath = parsed.Path == intended, parsed.Problem,
                State = job.State.ToString(), job.Summary, job.BytesDone,
                Issues = job.Issues.Select(i => new { i.Path, i.Cause, i.Message }).ToArray(),
                OwnedContentUnchanged = unchanged,
                LiteralHash = Hash(literalBytes), NestedHash = Hash(nestedBytes), ExpectedHash = expected,
                LiteralBytes = literalBytes.Length, NestedBytes = nestedBytes.Length,
                ActualDistinctOwnedFilesAndParserAndVerificationJob = true, NativeDesktopOrPhysicalSource = false
            }));
            Assert.True(unchanged);
            Assert.Null(parsed.Problem);
            Assert.Equal(intended, parsed.Path);
            if (mode == "escaped")
            {
                Assert.Equal(JobState.Failed, job.State);
                var mismatch = Assert.Single(job.Issues, i => i.Cause == VerifyChecksumsExecutor.MismatchCause);
                Assert.Equal(literal, mismatch.Path);
                Assert.Equal(literalBytes.Length, job.BytesDone);
                Assert.DoesNotContain("all match", job.Summary, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(JobState.Completed, job.State);
                Assert.Equal("1 verified, all match", job.Summary);
                Assert.DoesNotContain(job.Issues, i => i.Cause == VerifyChecksumsExecutor.MismatchCause);
                Assert.Equal(mode == "legacy" ? nestedBytes.Length : literalBytes.Length, job.BytesDone);
            }
            if (mode == "generated") Assert.Equal(@"\" + expected + @"  owned\\child.bin", line);
        }
        finally
        {
            job.Cancel();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
        }
    }
}

