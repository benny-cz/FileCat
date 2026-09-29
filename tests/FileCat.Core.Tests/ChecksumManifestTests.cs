using System.Security.Cryptography;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ChecksumManifestTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly JobManager _jobs;

    public ChecksumManifestTests()
    {
        _providers.Register(new LocalFileSystemProvider());
        _jobs = new JobManager(new PortableFileOperations(), _providers, Path.Combine(_dir.Path, "journal"));
    }

    public void Dispose() => _dir.Dispose();

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private async Task<Job> VerifyAsync(params string[] manifests)
    {
        var job = _jobs.Submit(new JobRequest { Kind = JobKind.VerifyChecksums, Sources = manifests.Select(m => ItemRef.ForFileSystemPath(m, EntryKind.File)).ToList() });
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        return job;
    }

    [Fact]
    public void Names_announce_the_algorithm()
    {
        Assert.Equal(ChecksumKind.Sha256, ChecksumManifests.KindFromName("release.sha256"));
        Assert.Equal(ChecksumKind.Sha256, ChecksumManifests.KindFromName("SHA256SUMS"));
        Assert.Equal(ChecksumKind.Sha512, ChecksumManifests.KindFromName("sha512sums.txt"));
        Assert.Equal(ChecksumKind.Md5, ChecksumManifests.KindFromName("disk.MD5"));
        Assert.Equal(ChecksumKind.Crc32, ChecksumManifests.KindFromName("album.sfv"));
        Assert.Null(ChecksumManifests.KindFromName("notes.txt"));
    }

    [Fact]
    public void GNU_BSD_and_SFV_lines_parse_and_problems_are_explained()
    {
        string sha = Sha256("a");
        string md5 = Convert.ToHexStringLower(MD5.HashData("a"u8));
        var text = string.Join("\n",
            "# comment",
            $"{sha}  a.txt",
            $"{sha.ToUpperInvariant()} *sub/b.txt",
            $"MD5 (c (1).txt) = {md5}",
            $"SHA256 (d.txt) = {sha[..10]}",
            "WHIRLPOOL (e.txt) = 00",
            "not a checksum line",
            "",
            @"\" + sha + @"  back\\slash\nname");
        var m = ChecksumManifests.Parse(Path.Combine(_dir.Path, "list.txt"), text);
        Assert.Equal(7, m.Entries.Count);
        Assert.Equal(("a.txt", ChecksumKind.Sha256, sha), (m.Entries[0].Name, m.Entries[0].Kind, m.Entries[0].Expected));
        Assert.Equal(Path.Combine(_dir.Path, "sub", "b.txt"), m.Entries[1].Path);
        Assert.Equal(sha, m.Entries[1].Expected);
        Assert.Equal(("c (1).txt", ChecksumKind.Md5), (m.Entries[2].Name, m.Entries[2].Kind));
        Assert.Contains("digits", m.Entries[3].Problem);
        Assert.Contains("WHIRLPOOL", m.Entries[4].Problem);
        Assert.Contains("not a checksum line", m.Entries[5].Problem);
        Assert.Equal("back\\slash\nname", m.Entries[6].Name);
        Assert.Equal(9, m.Entries[6].Line);

        var sfv = ChecksumManifests.Parse(Path.Combine(_dir.Path, "album.sfv"), "; made by a tool\ntrack 01.flac 0a1b2c3d\n");
        Assert.Equal(("track 01.flac", ChecksumKind.Crc32, "0a1b2c3d"), (sfv.Entries[0].Name, sfv.Entries[0].Kind, sfv.Entries[0].Expected));
        Assert.Equal($"{sha}  sub/x y.txt", Checksums.ManifestLine(ChecksumKind.Sha256, sha, Path.Combine("sub", "x y.txt")));
    }

    [Fact]
    public void Names_outside_the_manifest_folder_are_refused()
    {
        string sha = Sha256("x");
        var m = ChecksumManifests.Parse(Path.Combine(_dir.Path, "list.sha256"), string.Join("\n",
            $"{sha}  /etc/passwd", $"{sha}  C:\\Windows\\win.ini", $"{sha}  \\\\server\\share\\f", $"{sha}  ../up.txt", $"{sha}  a/../../b", $"{sha}  ./ok/fine.txt"));
        Assert.All(m.Entries.Take(5), e => Assert.StartsWith("Not verified", e.Problem));
        Assert.All(m.Entries.Take(5), e => Assert.Null(e.Path));
        Assert.Null(m.Entries[5].Problem);
        Assert.Equal(Path.Combine(_dir.Path, "ok", "fine.txt"), m.Entries[5].Path);
    }

    [Fact]
    public async Task Verification_reports_matches_mismatches_missing_files_and_skipped_lines()
    {
        _dir.File("good.txt", "good");
        _dir.File("sub/also good.txt", "fine");
        string bad = _dir.File("bad.txt", "changed");
        string manifest = _dir.File("files.sha256", string.Join("\n",
            $"{Sha256("good")}  good.txt", $"{Sha256("fine")} *sub\\also good.txt", $"{Sha256("original")}  bad.txt",
            $"{Sha256("gone")}  gone.txt", $"{Sha256("x")}  ../outside.txt", ""));
        var job = await VerifyAsync(manifest);
        Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Equal("2 match, 1 do not match, 1 missing, 1 lines not verified", job.Summary);
        var mismatch = Assert.Single(job.Issues, i => i.Cause == VerifyChecksumsExecutor.MismatchCause);
        Assert.Equal(bad, mismatch.Path);
        Assert.Single(job.Issues, i => i.Cause == VerifyChecksumsExecutor.MissingCause);
        Assert.Equal(new FileInfo(Path.Combine(_dir.Path, "good.txt")).Length + new FileInfo(Path.Combine(_dir.Path, "sub", "also good.txt")).Length
            + new FileInfo(bad).Length, job.BytesDone);

        string clean = _dir.File("clean.md5", Convert.ToHexStringLower(MD5.HashData("good"u8)) + "  good.txt\n");
        var ok = await VerifyAsync(clean);
        Assert.Equal(JobState.Completed, ok.State);
        Assert.Equal("1 verified, all match", ok.Summary);
        Assert.Contains(ok.Issues, i => i.Severity == IssueSeverity.Info && i.Message.Contains("MD5", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_manifest_without_a_checksum_line_verifies_nothing_and_says_so()
    {
        string notes = _dir.File("notes.sha256", "just\nsome words\nhere\n");
        var job = await VerifyAsync(notes);
        Assert.Equal("nothing verified: none of its 3 lines is a checksum line", job.Summary);
        Assert.DoesNotContain("all match", job.Summary, StringComparison.Ordinal);
        var empty = await VerifyAsync(_dir.File("empty.md5", ""));
        Assert.Equal("nothing verified: the manifest lists no files", empty.Summary);
    }
}
