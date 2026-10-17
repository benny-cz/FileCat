using System.Security.Cryptography;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;
using FileCat.Core.Resources;
using FileCat.Core.Verification;

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

    private async Task<Job> VerifyAsync(params string[] manifests) =>
        await FinishedAsync(_jobs.Submit(new JobRequest { Kind = JobKind.VerifyChecksums, Sources = manifests.Select(m => ItemRef.ForFileSystemPath(m, EntryKind.File)).ToList() }));

    /// <summary>The job once it has ended; one that does not within a minute fails the test instead of holding up the run.</summary>
    private static async Task<Job> FinishedAsync(Job job)
    {
        var deadline = DateTime.UtcNow.AddMinutes(1);
        while (!job.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline, $"{job.Title} did not end within a minute ({job.State}).");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
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
    public async Task Its_results_list_every_line_with_both_values_problems_first()
    {
        _dir.File("good.txt", "good");
        string bad = _dir.File("bad.txt", "changed");
        string manifest = _dir.File("files.sha256", string.Join("\n",
            $"{Sha256("good")}  good.txt", $"{Sha256("original")}  bad.txt", $"{Sha256("gone")}  gone.txt", $"{Sha256("x")}  ../outside.txt", ""));
        var job = await VerifyAsync(manifest);
        var report = job.VerificationReport!;
        Assert.True(report.IsComplete);
        Assert.Equal(_dir.Path, report.Folder);
        Assert.Equal([("bad.txt", VerifiedOutcome.Failed), ("gone.txt", VerifiedOutcome.Missing), ("good.txt", VerifiedOutcome.Good), ("files.sha256, line 4", VerifiedOutcome.NotChecked)],
            report.Files.Select(f => (f.Name, f.Outcome)));
        var differs = Assert.Single(report.Files[0].Checksums);
        Assert.Equal((Sha256("original"), Sha256("changed"), manifest, 2, bad), (differs.Claim.Expected, differs.Actual, differs.Claim.Source, differs.Claim.Line, report.Files[0].Path));
        Assert.Contains("lists it (line 3), but it is not there", Assert.Single(report.Files[1].Notes), StringComparison.Ordinal);
        // A line naming what may not be read is named by where it is, says why and what it names, and counts as a line.
        Assert.Null(report.Files[3].Path);
        Assert.True(report.Files[3].IsLine);
        Assert.Equal(["Not verified: the path leads out of the manifest's folder (\"..\").", "It names: ../outside.txt"], report.Files[3].Notes);
        Assert.Equal((4, 1), (report.Total, report.Lines));
        // A checksum file nobody signed vouches for no origin: the matching file says so.
        Assert.Contains(report.Files[2].Notes, n => n.Contains("proves nothing against tampering", StringComparison.Ordinal));
        // What it read is counted as read (the operations strip's pace, and the results' "read from disk").
        Assert.Equal(new FileInfo(bad).Length + new FileInfo(Path.Combine(_dir.Path, "good.txt")).Length, job.DataBytesProcessed);
    }

    [Fact]
    public async Task Lines_the_check_beside_the_files_compared_are_left_out_and_no_others()
    {
        _dir.File("good.txt", "good");
        _dir.File("sub/x.txt", "x");
        string manifest = _dir.File("files.sha256", $"{Sha256("good")}  good.txt\n{Sha256("x")}  sub/x.txt\n");
        // As the check of the files against everything beside them read the line: the checksum file, the line, the value.
        ChecksumClaim Line(string list, int line, string expected) => new(ChecksumKind.Sha256, expected, Path.GetFullPath(list), line);
        async Task<Job> VerifyAfterBesideAsync(string list, params ChecksumClaim[] compared) => await FinishedAsync(_jobs.Submit(new JobRequest
        {
            Kind = JobKind.VerifyChecksums,
            Sources = [ItemRef.ForFileSystemPath(list, EntryKind.File)],
            ComparedBeside = new HashSet<ChecksumClaim>(compared),
        }));

        // Compared already: left out. Not compared (a file in another folder): checked here.
        var job = await VerifyAfterBesideAsync(manifest, Line(manifest, 1, Sha256("good")));
        Assert.Equal("sub/x.txt", Assert.Single(job.VerificationReport!.Files).Name);
        Assert.Equal("1 verified, all match, 1 checked beside them", job.Summary);

        // The list changed since that check read it: a line that reads otherwise now is checked, not taken as compared.
        var changed = await VerifyAfterBesideAsync(manifest, Line(manifest, 1, Sha256("an older value")), Line(manifest, 2, Sha256("x")));
        Assert.Equal("good.txt", Assert.Single(changed.VerificationReport!.Files).Name);

        // Every line compared already: it says so, not that it verified nothing.
        string mine = _dir.File("mine.sha256", $"{Sha256("good")}  good.txt\n");
        var left = await VerifyAfterBesideAsync(mine, Line(mine, 1, Sha256("good")));
        Assert.Empty(left.VerificationReport!.Files);
        Assert.Equal("the files it lists were checked beside them", left.Summary);
    }

    [Fact]
    public async Task Checksum_files_found_in_a_folder_being_checked_leave_out_files_that_are_not_there()
    {
        _dir.File("desktop.iso", "desktop");
        string sums = _dir.File("SHA256SUMS", $"{Sha256("desktop")}  desktop.iso\n{Sha256("server")}  server.iso\n{Sha256("arm")}  arm.iso\n");
        var job = await FinishedAsync(_jobs.Submit(new JobRequest
        {
            Kind = JobKind.VerifyChecksums,
            Sources = [ItemRef.ForFileSystemPath(sums, EntryKind.File)],
            IgnoreMissing = true,
        }));
        // As sha256sum --ignore-missing: what is here is checked, what is not is no failure, and the results say so once.
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("1 verified, all match, 2 not here, not looked for", job.Summary);
        var report = job.VerificationReport!;
        Assert.Equal([("desktop.iso", VerifiedOutcome.Good)], report.Files.Select(f => (f.Name, f.Outcome)));
        Assert.Equal(["SHA256SUMS also lists 2 files that are not here: they were not looked for (the folder was checked, not this checksum file)."], report.Notes);
        // Chosen, the same checksum file reports them missing.
        Assert.Equal(2, (await VerifyAsync(sums)).VerificationReport!.Count(VerifiedOutcome.Missing));
    }

    [Fact]
    public async Task A_clearsigned_checksum_file_is_read_without_its_armor_and_says_its_signature_is_not_checked()
    {
        _dir.File("Fedora.iso", "fedora");
        string list = _dir.File("Fedora-CHECKSUM", string.Join("\n",
            "-----BEGIN PGP SIGNED MESSAGE-----", "Hash: SHA256", "",
            "# Fedora.iso: 6 bytes", $"SHA256 (Fedora.iso) = {Sha256("fedora")}",
            "-----BEGIN PGP SIGNATURE-----", "", "iQIzBAEBCAAdFiEEabcdefghijklmnopqrstuvwxyz0123456789", "=AbCd", "-----END PGP SIGNATURE-----", ""));
        var manifest = ChecksumManifests.Parse(list, File.ReadAllText(list));
        Assert.True(manifest.ClearSigned);
        Assert.Equal(("Fedora.iso", 5), (Assert.Single(manifest.Entries).Name, manifest.Entries[0].Line));
        // Its lines are checked; what its own signature would say is not claimed.
        var job = await VerifyAsync(list);
        var file = Assert.Single(job.VerificationReport!.Files);
        Assert.Equal((VerifiedOutcome.Good, false), (file.Outcome, file.Authentic));
        Assert.Contains(job.VerificationReport.Notes, n => n.StartsWith("Fedora-CHECKSUM carries its own OpenPGP signature (it is clearsigned), which FileCat does not check", StringComparison.Ordinal));
        // Armor anywhere but around a clearsigned list is read as lines (and so reported, not hidden).
        var plain = ChecksumManifests.Parse(list, $"{Sha256("fedora")}  Fedora.iso\n-----BEGIN PGP SIGNATURE-----\n{Sha256("x")}  x.iso\n");
        Assert.False(plain.ClearSigned);
        Assert.Equal(3, plain.Entries.Count);
    }

    [Fact]
    public async Task A_check_stopped_by_an_error_never_says_all_match()
    {
        _dir.File("good.txt", "good");
        string trap = Path.Combine(_dir.Path, "trap.txt");
        string manifest = _dir.File("files.sha256", $"{Sha256("gone")}  gone.txt\n{Sha256("trap")}  trap.txt\n{Sha256("good")}  good.txt\n");
        var jobs = new JobManager(new FailingOn(trap), _providers, Path.Combine(_dir.Path, "journal2"));
        var job = await FinishedAsync(jobs.Submit(new JobRequest { Kind = JobKind.VerifyChecksums, Sources = [ItemRef.ForFileSystemPath(manifest, EntryKind.File)] }));
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("stopped by an error before any file was read, 1 missing", job.Summary);
        // What it reached is kept, and known not to be all there was.
        Assert.False(job.VerificationReport!.IsComplete);
        Assert.Equal("gone.txt", Assert.Single(job.VerificationReport.Files).Name);
    }

    /// <summary>Asked about one path, it fails as a defect would (not an I/O error a check expects).</summary>
    private sealed class FailingOn(string path) : PortableFileOperations
    {
        public override FileSystemItemInfo? TryGetInfo(string asked) =>
            string.Equals(asked, path, StringComparison.OrdinalIgnoreCase) ? throw new InvalidOperationException("a defect") : base.TryGetInfo(asked);
    }

    [Fact]
    public async Task A_checksum_file_that_cannot_be_read_or_lists_nothing_is_listed_with_the_files()
    {
        // Chosen and not read (here a folder with a checksum file's name): a problem in the list, not a footnote.
        string unreadable = Directory.CreateDirectory(Path.Combine(_dir.Path, "dir.sha256")).FullName;
        string empty = _dir.File("empty.md5", "# only a comment\n");
        _dir.File("good.txt", "good");
        string fine = _dir.File("fine.sha256", $"{Sha256("good")}  good.txt\n");
        var report = (await VerifyAsync(unreadable, empty, fine)).VerificationReport!;
        Assert.Equal([("dir.sha256", VerifiedOutcome.Unreadable), ("good.txt", VerifiedOutcome.Good), ("empty.md5", VerifiedOutcome.NotChecked)],
            report.Files.Select(f => (f.Name, f.Outcome)));
        Assert.StartsWith("dir.sha256 could not be read", Assert.Single(report.Files[0].Notes), StringComparison.Ordinal);
        Assert.Equal(("lists no checksums", "empty.md5 has no checksum lines: there is nothing in it to check files against."), (report.Files[2].Text, Assert.Single(report.Files[2].Notes)));
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

    [Theory]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", ChecksumKind.Sha256)]
    [InlineData("  sha256:E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855\n", ChecksumKind.Sha256)]
    [InlineData("SHA256 (ubuntu.iso) = e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", ChecksumKind.Sha256)]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 *deadbeef.bin", ChecksumKind.Sha256)]
    [InlineData("e3 b0 c4 42 98 fc 1c 14 9a fb f4 c8 99 6f b9 24 27 ae 41 e4 64 9b 93 4c a4 95 99 1b 78 52 b8 55", ChecksumKind.Sha256)]
    [InlineData("MD5: d41d8cd98f00b204e9800998ecf8427e", ChecksumKind.Md5)]
    [InlineData("da39a3ee5e6b4b0d3255bfef95601890afd80709", ChecksumKind.Sha1)]
    [InlineData("CRC32 00000000", ChecksumKind.Crc32)]
    public void A_pasted_checksum_is_read_in_the_forms_pages_and_tools_show(string text, ChecksumKind kind)
    {
        var parsed = Checksums.ParseExpected(text);
        Assert.NotNull(parsed);
        Assert.Equal(kind, parsed.Value.Kind);
        Assert.Equal(parsed.Value.Hex.ToLowerInvariant(), parsed.Value.Hex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a checksum")]
    [InlineData("SHA3-256: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")] // same length, another algorithm
    [InlineData("b2sum e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 BLAKE2b")]
    [InlineData("d41d8cd98f00b204e9800998ecf8427e d41d8cd98f00b204e9800998ecf8427f")] // two of a kind: which one?
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85")] // 63 digits
    public void Text_that_is_not_one_checksum_FileCat_computes_is_not_taken_for_one(string text) =>
        Assert.Null(Checksums.ParseExpected(text));
}
