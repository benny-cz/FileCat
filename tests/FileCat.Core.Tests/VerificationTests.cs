using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;
using FileCat.Core.Verification;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace FileCat.Core.Tests;

/// <summary>D-57: checksums and signatures beside files, found, checked, cached, and signed with minisign and OpenPGP.</summary>
public sealed class VerificationTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-verify-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private VerificationResult Check(string name, VerificationService? service = null) =>
        (service ?? Service()).OnRequest(Path.Combine(_dir, name), TestContext.Current.CancellationToken)!;

    private VerificationService Service(long threshold = 256L << 20, string? cacheFile = null, IReadOnlyList<string>? keys = null) =>
        new(new VerificationCache(cacheFile), () => threshold, () => keys ?? []);

    [Fact]
    public void Checksum_files_in_every_form_are_found_for_the_files_they_name()
    {
        Write("tool.exe", "tool");
        Write("tool.exe.sha256", Sha256("tool") + "  tool.exe\n");       // GNU line in a sidecar named after the file
        Write("image.iso", "image");
        Write("image.iso.sha256", Sha256("image") + "\n");              // a bare checksum
        Write("setup.msi", "setup");
        Write("setup.sha256", Sha256("setup") + " *setup.msi\n");       // named without the file's extension, binary mode
        Write("a.txt", "a");
        Write("b.txt", "b");
        Write("SHA256SUMS", $"{Sha256("a")}  a.txt\n{Sha256("not b")}  b.txt\n{Sha256("x")}  ../outside.txt\n");
        Write("disk.img", "disk");
        Write("disk.sfv", $"disk.img {Crc32.Append(0, Encoding.UTF8.GetBytes("disk")):x8}\n");
        Write("app.zip", "app");
        Write("app.zip.minisig", "not really");
        Write("SHA256SUMS.asc", "not really");

        var sidecars = FolderSidecars.Scan(_dir, TestContext.Current.CancellationToken);
        Assert.Equal(ChecksumKind.Sha256, Assert.Single(sidecars.Checksums("tool.exe")).Kind);
        Assert.Equal(Sha256("image"), Assert.Single(sidecars.Checksums("image.iso")).Expected);
        Assert.Single(sidecars.Checksums("setup.msi"));
        Assert.Single(sidecars.Checksums("a.txt"));
        Assert.Single(sidecars.Checksums("b.txt"));
        Assert.Equal(ChecksumKind.Crc32, Assert.Single(sidecars.Checksums("disk.img")).Kind);
        Assert.Equal(SignatureKind.Minisign, Assert.Single(sidecars.Signatures("app.zip")).Kind);
        Assert.Equal(SignatureKind.OpenPgp, Assert.Single(sidecars.Signatures("SHA256SUMS")).Kind);
        Assert.Equal(["a.txt", "b.txt"], sidecars.Covered("SHA256SUMS"));
        Assert.False(sidecars.Covers("outside.txt"));
    }

    [Fact]
    public void A_file_matches_or_differs_and_weak_algorithms_say_what_they_show()
    {
        Write("a.txt", "a");
        Write("b.txt", "b");
        Write("SHA256SUMS", $"{Sha256("a")}  a.txt\n{Sha256("not b")}  b.txt\n");
        Write("c.txt", "c");
        Write("c.txt.md5", Convert.ToHexStringLower(MD5.HashData("c"u8)) + "  c.txt\n");
        var a = Check("a.txt");
        Assert.Equal(VerificationState.Matches, a.State);
        Assert.Equal("✓ SHA-256", a.Text);
        Assert.Contains(a.Details, d => d.Contains("proves nothing against tampering", StringComparison.Ordinal));
        var b = Check("b.txt");
        Assert.Equal(VerificationState.Differs, b.State);
        Assert.Equal("✗ SHA-256 differs", b.Text);
        Assert.Contains(b.Details, d => d.Contains($"the file's is {Sha256("b")}", StringComparison.Ordinal));
        var c = Check("c.txt");
        Assert.Equal("✓ MD5 (integrity only)", c.Text);
        Assert.Contains(c.Details, d => d.StartsWith("MD5, SHA-1, and CRC-32 show", StringComparison.Ordinal));
        // Not covered, and a sidecar's own row.
        Write("plain.txt", "p");
        Assert.Null(Service().Automatic(Path.Combine(_dir, "plain.txt"), TestContext.Current.CancellationToken));
        Assert.Equal("checks 2 files", Service().Automatic(Path.Combine(_dir, "SHA256SUMS"), TestContext.Current.CancellationToken)!.Text);
    }

    [Fact]
    public void Every_algorithm_a_file_needs_is_computed_in_one_pass()
    {
        string path = Write("data.bin", new string('x', 3_000_000));
        long read = 0;
        var hashes = Verifier.Hash(path, [ChecksumKind.Sha256, ChecksumKind.Md5, ChecksumKind.Crc32], TestContext.Current.CancellationToken, n => read += n);
        Assert.Equal(3_000_000, read);
        Assert.Equal(Checksums.Compute(path, ChecksumKind.Sha256, TestContext.Current.CancellationToken), hashes[ChecksumKind.Sha256]);
        Assert.Equal(Checksums.Compute(path, ChecksumKind.Md5, TestContext.Current.CancellationToken), hashes[ChecksumKind.Md5]);
        Assert.Equal(Checksums.Compute(path, ChecksumKind.Crc32, TestContext.Current.CancellationToken), hashes[ChecksumKind.Crc32]);
    }

    /// <summary>A minisign key pair and signature files as minisign writes them.</summary>
    private sealed class MinisignKey
    {
        private readonly byte[] _secret = new byte[32];
        public readonly byte[] Public = new byte[32];
        public readonly byte[] Id = RandomNumberGenerator.GetBytes(8);

        public MinisignKey()
        {
            RandomNumberGenerator.Fill(_secret);
            Ed25519.GeneratePublicKey(_secret, 0, Public, 0);
        }

        public string PublicFile => $"untrusted comment: minisign public key {BinaryPrimitives.ReadUInt64LittleEndian(Id):X16}\n" +
                                    Convert.ToBase64String([(byte)'E', (byte)'d', .. Id, .. Public]) + "\n";

        public string Sign(byte[] content, string comment, bool prehashed = true)
        {
            byte[] message = prehashed ? Blake2b(content) : content;
            var signature = new byte[64];
            Ed25519.Sign(_secret, 0, message, 0, message.Length, signature, 0);
            byte[] global = new byte[64];
            byte[] signed = [.. signature, .. Encoding.UTF8.GetBytes(comment)];
            Ed25519.Sign(_secret, 0, signed, 0, signed.Length, global, 0);
            return "untrusted comment: signature from minisign secret key\n" +
                   Convert.ToBase64String([(byte)'E', prehashed ? (byte)'D' : (byte)'d', .. Id, .. signature]) + "\n" +
                   $"trusted comment: {comment}\n" + Convert.ToBase64String(global) + "\n";
        }

        private static byte[] Blake2b(byte[] content)
        {
            var digest = new Org.BouncyCastle.Crypto.Digests.Blake2bDigest(512);
            digest.BlockUpdate(content, 0, content.Length);
            var hash = new byte[64];
            digest.DoFinal(hash, 0);
            return hash;
        }
    }

    [Fact]
    public void Minisign_signatures_are_checked_in_both_forms_against_the_key_by_its_id()
    {
        var key = new MinisignKey();
        string keys = Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName;
        File.WriteAllText(Path.Combine(keys, "release.pub"), key.PublicFile);
        byte[] content = Encoding.UTF8.GetBytes("release archive");
        File.WriteAllBytes(Path.Combine(_dir, "app.zip"), content);
        Write("app.zip.minisig", key.Sign(content, "timestamp:1700000000\tfile:app.zip"));
        var service = Service(keys: [keys]);
        var good = Check("app.zip", service);
        Assert.Equal(VerificationState.SignatureGood, good.State);
        Assert.Equal("✓ signed by release", good.Text);
        Assert.Contains(good.Details, d => d.Contains("trusted comment: timestamp:1700000000", StringComparison.Ordinal));

        // The legacy form (the whole file signed).
        File.WriteAllBytes(Path.Combine(_dir, "old.zip"), content);
        Write("old.zip.minisig", key.Sign(content, "legacy", prehashed: false));
        Assert.Equal(VerificationState.SignatureGood, Check("old.zip", Service(keys: [keys])).State);

        // A key beside the file came from the same place as the file: its good signature is unsure, never a tick,
        // unless the same key is among the trusted ones.
        File.WriteAllText(Path.Combine(_dir, "release.pub"), key.PublicFile);
        var beside = Check("old.zip");
        Assert.Equal((VerificationState.SignatureUnknownKey, "? signed by a key found beside it"), (beside.State, beside.Text));
        Assert.Contains(beside.Details, d => d.Contains("not among your trusted keys", StringComparison.Ordinal));
        Assert.Equal(VerificationState.SignatureGood, Check("old.zip", Service(keys: [keys])).State);
        File.Delete(Path.Combine(_dir, "release.pub"));

        // A changed file, a changed trusted comment, and a key that is not here.
        File.WriteAllBytes(Path.Combine(_dir, "app.zip"), [.. content, (byte)'!']);
        Assert.Equal(VerificationState.SignatureBad, Check("app.zip", Service(keys: [keys])).State);
        File.WriteAllBytes(Path.Combine(_dir, "app.zip"), content);
        Write("app.zip.minisig", key.Sign(content, "original").Replace("trusted comment: original", "trusted comment: forged"));
        var forged = Check("app.zip", Service(keys: [keys]));
        Assert.Equal(VerificationState.SignatureBad, forged.State);
        Assert.Contains(forged.Details, d => d.Contains("trusted comment was changed", StringComparison.Ordinal));
        var stranger = new MinisignKey();
        Write("app.zip.minisig", stranger.Sign(content, "someone else"));
        var unknown = Check("app.zip", Service(keys: [keys]));
        Assert.Equal(VerificationState.SignatureUnknownKey, unknown.State);
        Assert.Contains(unknown.Details, d => d.Contains($"minisign key {BinaryPrimitives.ReadUInt64LittleEndian(stranger.Id):X16}", StringComparison.Ordinal));

        // Trusting the key later (copying it into the keys folder) is noticed: the kept result is worked out again.
        string cacheFile = Path.Combine(_dir, "cache", "trust.jsonl");
        string trusted = Directory.CreateDirectory(Path.Combine(_dir, "trusted")).FullName;
        File.WriteAllText(Path.Combine(_dir, "release.pub"), key.PublicFile);
        Assert.Equal(VerificationState.SignatureUnknownKey, Check("old.zip", Service(cacheFile: cacheFile, keys: [trusted])).State);
        File.WriteAllText(Path.Combine(trusted, "release.pub"), key.PublicFile);
        Assert.Equal(VerificationState.SignatureGood, Check("old.zip", Service(cacheFile: cacheFile, keys: [trusted])).State);
    }

    [Fact]
    public void A_manifest_signed_by_a_trusted_key_makes_the_files_it_lists_authentic()
    {
        // Intact (the checksum matches) and authentic (a trusted key signed the list): the stronger mark wins.
        var key = new MinisignKey();
        string keys = Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName;
        File.WriteAllText(Path.Combine(keys, "publisher.pub"), key.PublicFile);
        Write("image.iso", "disc image");
        string sums = Write("SHA256SUMS", Sha256("disc image") + "  image.iso\n");
        Write("SHA256SUMS.minisig", key.Sign(File.ReadAllBytes(sums), "release list"));
        var signed = Check("image.iso", Service(keys: [keys]));
        Assert.Equal((VerificationState.SignatureGood, "✓ SHA-256 · manifest signed by publisher"), (signed.State, signed.Text));

        // Without the key among the trusted ones, the list vouches for nothing more than a checksum beside the file.
        var unsure = Check("image.iso", Service());
        Assert.Equal(VerificationState.SignatureUnknownKey, unsure.State);
        Assert.Equal("✓ SHA-256 · manifest ? signed by an unknown key", unsure.Text);
    }

    [Fact]
    public void GnuPG_status_lines_read_as_good_bad_untrusted_or_unknown()
    {
        var good = OpenPgp.Interpret("[GNUPG:] NEWSIG\n[GNUPG:] GOODSIG 0123456789ABCDEF Release Team <release@example.org>\n[GNUPG:] VALIDSIG 1111222233334444555566667777888899990000 2024-01-01\n[GNUPG:] TRUST_FULLY 0 pgp\n");
        Assert.Equal(VerificationState.SignatureGood, good.State);
        Assert.Equal("Release Team <release@example.org>", good.Signer);
        Assert.Contains("fully trusted", good.Text, StringComparison.Ordinal);
        Assert.Equal(VerificationState.SignatureUnknownKey, OpenPgp.Interpret("[GNUPG:] GOODSIG 0123 Someone\n[GNUPG:] TRUST_UNDEFINED 0 pgp\n").State);
        Assert.Equal(VerificationState.SignatureBad, OpenPgp.Interpret("[GNUPG:] BADSIG 0123 Someone\n").State);
        Assert.Equal(VerificationState.SignatureUnknownKey, OpenPgp.Interpret("[GNUPG:] ERRSIG 0123456789ABCDEF 1 10 00 1700000000 9 -\n[GNUPG:] NO_PUBKEY 0123456789ABCDEF\n").State);
        Assert.Equal(VerificationState.SignatureBad, OpenPgp.Interpret("[GNUPG:] REVKEYSIG 0123 Someone\n").State);
    }

    [Fact]
    public void A_real_OpenPGP_signature_is_checked_with_the_systems_gpg()
    {
        string? gpg = FindGpg();
        if (gpg is null)
        {
            Assert.Skip("No gpg here.");
            return;
        }
        // gpg-agent's socket lives in the home on macOS, whose socket paths hold 104 bytes: a home under $TMPDIR is too long.
        string home = OperatingSystem.IsWindows()
            ? Directory.CreateDirectory(Path.Combine(_dir, "gnupg")).FullName
            : Directory.CreateDirectory("/tmp/fcg-" + Guid.NewGuid().ToString("N")[..8], UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute).FullName;
        try
        {
            string file = Write("release.tar", "release contents");
            Run(gpg, home, "--batch", "--pinentry-mode", "loopback", "--passphrase", "", "--quick-gen-key", "FileCat Test <test@example.org>", "ed25519", "sign", "never");
            Run(gpg, home, "--batch", "--pinentry-mode", "loopback", "--passphrase", "", "--detach-sign", "--output", file + ".sig", file);
            var good = OpenPgp.Verify(file + ".sig", file, TestContext.Current.CancellationToken, home);
            Assert.Equal(VerificationState.SignatureGood, good.State);
            Assert.Contains("FileCat Test", good.Text, StringComparison.Ordinal);
            File.AppendAllText(file, "tampered");
            Assert.Equal(VerificationState.SignatureBad, OpenPgp.Verify(file + ".sig", file, TestContext.Current.CancellationToken, home).State);
        }
        finally
        {
            // The agent gpg started for this home goes with it.
            string gpgconf = Path.Combine(Path.GetDirectoryName(gpg)!, OperatingSystem.IsWindows() ? "gpgconf.exe" : "gpgconf");
            if (File.Exists(gpgconf))
            {
                try
                {
                    using var stop = Process.Start(new ProcessStartInfo(gpgconf, ["--homedir", home, "--kill", "all"]) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true });
                    stop?.WaitForExit(10_000);
                }
                catch (System.ComponentModel.Win32Exception) { }
            }
            if (!OperatingSystem.IsWindows()) try { Directory.Delete(home, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The gpg FileCat itself would use (Git for Windows' own, which wants its own paths, is not one).</summary>
    private static string? FindGpg()
    {
        OpenPgp.UseTool(null);
        return OpenPgp.Tool;
    }

    private static void Run(string gpg, string home, params string[] args)
    {
        var start = new ProcessStartInfo(gpg) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--homedir=" + home);
        foreach (string a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        p.StandardOutput.ReadToEnd();
        string error = p.StandardError.ReadToEnd();
        Assert.True(p.WaitForExit(60_000), "gpg did not finish");
        Assert.True(p.ExitCode == 0, $"gpg {string.Join(' ', args)}: {error}");
    }

    [Fact]
    public void Results_are_kept_across_runs_and_checked_again_when_the_file_or_its_sidecar_changes()
    {
        string cacheFile = Path.Combine(_dir, "cache", "verification.jsonl");
        string path = Write("big.bin", "contents");
        Write("big.bin.sha256", Sha256("contents") + "\n");
        var first = Service(cacheFile: cacheFile);
        Assert.Equal(VerificationState.Matches, first.OnRequest(path, TestContext.Current.CancellationToken)!.State);
        // A later run with a tiny threshold still shows the kept result instead of "not checked".
        var later = Service(threshold: 1, cacheFile: cacheFile);
        Assert.Equal(VerificationState.Matches, later.Automatic(path, TestContext.Current.CancellationToken)!.State);
        // A changed sidecar is a new question, answered from the hash kept from the first read: the file is not read again.
        Write("big.bin.sha256", Sha256("something else") + "\n");
        File.SetLastWriteTimeUtc(Path.Combine(_dir, "big.bin.sha256"), DateTime.UtcNow.AddMinutes(1));
        var again = Service(threshold: 1, cacheFile: cacheFile);
        Assert.Equal(VerificationState.Differs, again.Automatic(path, TestContext.Current.CancellationToken)!.State);
        // One that needs an algorithm never read waits for a request, the file being large.
        Write("big.bin.md5", Convert.ToHexStringLower(MD5.HashData("contents"u8)) + "\n");
        var md5 = Service(threshold: 1, cacheFile: cacheFile);
        var waiting = md5.Automatic(path, TestContext.Current.CancellationToken)!;
        Assert.Equal(VerificationState.NotChecked, waiting.State);
        Assert.StartsWith("not checked: ", waiting.Text, StringComparison.Ordinal);
        var checkedNow = md5.OnRequest(path, TestContext.Current.CancellationToken)!;
        Assert.Equal((VerificationState.Differs, "✗ SHA-256 differs"), (checkedNow.State, checkedNow.Text));
        Assert.Contains("MD5 matches big.bin.md5, line 1.", checkedNow.Details);
    }

    [Fact]
    public void A_checksum_file_that_is_a_cloud_placeholder_is_not_read()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Placeholder attributes are Windows'.");
            return;
        }
        // Reading a placeholder downloads it: its claims wait until it is on this computer.
        string path = Write("x.bin", "x");
        string sums = Write("x.bin.sha256", Sha256("x") + "\n");
        File.SetAttributes(sums, FileAttributes.Offline);
        try
        {
            Assert.Null(Service().Automatic(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.SetAttributes(sums, FileAttributes.Normal);
        }
        Assert.Equal(VerificationState.Matches, Service().Automatic(path, TestContext.Current.CancellationToken)!.State);
    }

    [Fact]
    public void A_check_asked_for_reads_the_file_even_when_a_kept_result_says_it_matched()
    {
        // A file changed with its size and time set back looks unchanged to a kept result; an explicit check reads it.
        string path = Write("setup.exe", "genuine!");
        Write("setup.exe.sha256", Sha256("genuine!") + "\n");
        var service = Service();
        Assert.Equal(VerificationState.Matches, service.OnRequest(path, TestContext.Current.CancellationToken)!.State);
        var time = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "tampered");
        File.SetLastWriteTimeUtc(path, time);
        Assert.Equal(VerificationState.Matches, service.Automatic(path, TestContext.Current.CancellationToken)!.State); // the row: kept
        Assert.Equal(VerificationState.Differs, service.OnRequest(path, TestContext.Current.CancellationToken)!.State);
        Assert.Equal(VerificationState.Differs, service.Automatic(path, TestContext.Current.CancellationToken)!.State); // and kept anew
    }

    [Fact]
    public void A_changed_sidecar_is_noticed_when_the_folder_changes()
    {
        string path = Write("x.bin", "x");
        Write("x.bin.sha256", Sha256("x") + "\n");
        var service = Service();
        string? changed = null;
        service.SidecarsChanged += folder => changed = folder;
        Assert.Equal(VerificationState.Matches, service.Automatic(path, TestContext.Current.CancellationToken)!.State);
        Write("x.bin.md5", Convert.ToHexStringLower(MD5.HashData("x"u8)) + "\n");
        service.FolderChanged(_dir, TestContext.Current.CancellationToken);
        Assert.Equal(_dir, changed);
        Assert.Equal("✓ SHA-256, MD5", service.Automatic(path, TestContext.Current.CancellationToken)!.Text);
    }
    [Fact]
    public void Hashes_a_job_read_answer_for_a_large_file_until_it_changes()
    {
        string path = Write("large.iso", "contents");
        // The checksum file says what a remembered value says, not what the contents are: a match proves the file was not read.
        Write("large.iso.sha256", Sha256("remembered") + "\n");
        var service = Service(threshold: 1);
        Assert.Equal(VerificationState.NotChecked, service.Automatic(path, TestContext.Current.CancellationToken)!.State);
        var stamp = VerificationService.Stamp(path)!.Value;
        service.Remember(path, stamp, new Dictionary<ChecksumKind, string> { [ChecksumKind.Sha256] = Sha256("remembered") });
        Assert.Equal(VerificationState.Matches, service.Automatic(path, TestContext.Current.CancellationToken)!.State);

        // Changed: the remembered value is for the old file, so the large file waits for a request again.
        File.AppendAllText(path, " and more");
        Assert.Equal(VerificationState.NotChecked, service.Automatic(path, TestContext.Current.CancellationToken)!.State);
        // A value read while the file changed is not remembered at all.
        var before = VerificationService.Stamp(path)!.Value;
        File.AppendAllText(path, " again");
        service.Remember(path, before, new Dictionary<ChecksumKind, string> { [ChecksumKind.Sha256] = Sha256("remembered") });
        Assert.Empty(service.Cache.HashesOf(path, before.Size, before.Modified));
        var now = VerificationService.Stamp(path)!.Value;
        Assert.Empty(service.Cache.HashesOf(path, now.Size, now.Modified));
    }

    [Fact]
    public async Task A_gigabyte_file_is_checked_with_progress_can_be_canceled_and_is_read_once()
    {
        // A file extended without writing (sparse on NTFS, ext4, and APFS): 1 GiB of zeros that cost no disk space.
        string path = Path.Combine(_dir, "disk.img");
        using (var stream = new FileStream(path, FileMode.CreateNew)) stream.SetLength(1L << 30);
        string expected;
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var zeros = new byte[1 << 20];
            for (int i = 0; i < 1024; i++) hash.AppendData(zeros);
            expected = Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        Write("disk.img.sha256", expected + "  disk.img\n");
        var service = Service(cacheFile: Path.Combine(_dir, "cache", "verification.jsonl"));
        var previous = VerificationService.Current;
        VerificationService.Current = service;
        try
        {
            var providers = new Resources.ProviderRegistry();
            providers.Register(new FileSystem.LocalFileSystemProvider());
            var jobs = new JobManager(new FileSystem.PortableFileOperations(), providers, Path.Combine(_dir, "journal"));
            JobRequest Request() => new() { Kind = JobKind.VerifyBeside, Sources = [Resources.ItemRef.ForFileSystemPath(path, Resources.EntryKind.File)] };

            // Canceled part way: nothing is kept, and the row still waits for a request.
            var canceled = jobs.Submit(Request());
            while (canceled.BytesDone == 0 && !canceled.State.IsFinished()) await Task.Delay(5, TestContext.Current.CancellationToken);
            canceled.Cancel();
            while (!canceled.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal(JobState.Canceled, canceled.State);
            Assert.StartsWith("canceled after 0 of 1 files", canceled.Summary, StringComparison.Ordinal);
            Assert.Equal(VerificationState.NotChecked, service.Automatic(path, TestContext.Current.CancellationToken)!.State);

            // To the end: progress reaches the size, and the result is kept.
            var watch = Stopwatch.StartNew();
            var job = jobs.Submit(Request());
            long seen = 0;
            int steps = 0;
            while (!job.State.IsFinished())
            {
                if (job.BytesDone != seen)
                {
                    seen = job.BytesDone;
                    steps++;
                }
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }
            watch.Stop();
            Assert.Equal((JobState.Completed, "1 verified, all good"), (job.State, job.Summary));
            Assert.Equal((1L << 30, 1L << 30), (job.BytesTotal, job.BytesDone));
            Assert.True(steps > 1, "progress moved only once");
            TestContext.Current.TestOutputHelper?.WriteLine($"1 GiB checked in {watch.Elapsed.TotalSeconds:0.00} s ({1024 / watch.Elapsed.TotalSeconds:0} MiB/s)");

            // Kept: shown at once in this run and the next.
            Assert.Equal(VerificationState.Matches, Service(threshold: 1, cacheFile: Path.Combine(_dir, "cache", "verification.jsonl")).Automatic(path, TestContext.Current.CancellationToken)!.State);
        }
        finally
        {
            VerificationService.Current = previous;
        }
    }

    [Fact]
    public async Task Verifying_on_request_checks_large_files_says_what_it_found_and_keeps_it()
    {
        string cacheFile = Path.Combine(_dir, "cache", "verification.jsonl");
        string good = Write("good.bin", "good"), bad = Write("bad.bin", "bad"), lonely = Write("lonely.txt", "alone");
        Write("SHA256SUMS", $"{Sha256("good")}  good.bin\n{Sha256("not bad")}  bad.bin\n");
        var service = Service(threshold: 1, cacheFile: cacheFile);
        var previous = VerificationService.Current;
        VerificationService.Current = service;
        try
        {
            var providers = new Resources.ProviderRegistry();
            providers.Register(new FileSystem.LocalFileSystemProvider());
            var jobs = new JobManager(new FileSystem.PortableFileOperations(), providers, Path.Combine(_dir, "journal"));
            var job = jobs.Submit(new JobRequest { Kind = JobKind.VerifyBeside, Sources = [.. new[] { good, bad, lonely }.Select(f => Resources.ItemRef.ForFileSystemPath(f, Resources.EntryKind.File))] });
            while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal(JobState.CompletedWithIssues, job.State);
            Assert.Equal("1 verified, 1 failed, 1 with nothing to check against", job.Summary);
            var failed = Assert.Single(job.Issues, i => i.Severity == IssueSeverity.Error);
            Assert.Equal((bad, VerifyBesideExecutor.FailedCause), (failed.Path, failed.Cause));
            Assert.StartsWith("✗ SHA-256 differs. SHA-256 differs: SHA256SUMS, line 2 says", failed.Message, StringComparison.Ordinal);
            Assert.Contains(job.Issues, i => i.Path == lonely && i.Message.StartsWith("Nothing beside it says what it should be", StringComparison.Ordinal));
            Assert.Equal(job.BytesTotal, job.BytesDone);

            // Kept: the rows show it without reading, in this run and the next.
            Assert.Equal(VerificationState.Matches, service.Automatic(good, TestContext.Current.CancellationToken)!.State);
            Assert.Equal(VerificationState.Differs, Service(threshold: 1, cacheFile: cacheFile).Automatic(bad, TestContext.Current.CancellationToken)!.State);

            // A manifest verified as such leaves the hashes it read: a new checksum file's row needs no reading.
            string later = Write("later.bin", "later");
            Write("later.sha256", $"{Sha256("later")}  later.bin\n");
            var manifestJob = jobs.Submit(new JobRequest { Kind = JobKind.VerifyChecksums, Sources = [Resources.ItemRef.ForFileSystemPath(Path.Combine(_dir, "later.sha256"), Resources.EntryKind.File)] });
            while (!manifestJob.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.Equal(JobState.Completed, manifestJob.State);
            Write("later.bin.md5", "0123456789abcdef0123456789abcdef\n"); // a second claim needs MD5, which no job read: not checked
            Assert.Equal(VerificationState.NotChecked, service.Automatic(later, TestContext.Current.CancellationToken)!.State);
            File.Delete(Path.Combine(_dir, "later.bin.md5"));
            service.FolderChanged(_dir, TestContext.Current.CancellationToken);
            Assert.Equal(VerificationState.Matches, service.Automatic(later, TestContext.Current.CancellationToken)!.State);
        }
        finally
        {
            VerificationService.Current = previous;
        }
    }
}
