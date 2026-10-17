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

    /// <summary>Files these tests write are read at once (the product waits until a file has been still: see the settling test).</summary>
    private VerificationService Service(long threshold = 256L << 20, string? cacheFile = null, IReadOnlyList<string>? keys = null) =>
        new(new VerificationCache(cacheFile), () => threshold, () => keys ?? []) { Settle = TimeSpan.Zero };

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
    public void Repeated_manifest_claims_keep_each_line_but_cover_each_file_once()
    {
        Write("a.txt", "a");
        Write("b.txt", "b");
        string repeated = Sha256("a") + "  a.txt\n";
        string[] otherNames = Enumerable.Range(0, 24).Select(i => $"other-{i:00}.txt").ToArray();
        foreach (string name in otherNames) Write(name, name);
        string otherLines = string.Concat(otherNames.Select(name => Sha256(name) + "  " + name + "\n"));
        Write("SHA256SUMS", string.Concat(Enumerable.Repeat(repeated, 512)) + Sha256("b") + "  b.txt\n" + otherLines);

        var sidecars = FolderSidecars.Scan(_dir, TestContext.Current.CancellationToken);
        var claims = sidecars.Checksums("a.txt");
        Assert.Equal(512, claims.Count);
        Assert.Equal(Enumerable.Range(1, 512), claims.Select(c => c.Line));
        Assert.Single(sidecars.Checksums("b.txt"));
        Assert.Equal(["a.txt", "b.txt", .. otherNames], sidecars.Covered("SHA256SUMS"));
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
    public void Minisign_ignores_oversized_link_target_for_trusted_key()
    {
        var signer = new MinisignKey();
        string keys = Directory.CreateDirectory(Path.Combine(_dir, "linked-keys")).FullName;
        string oversizedKey = Path.Combine(_dir, "oversized.pub");
        string linkedKey = Path.Combine(keys, "release.pub");
        WriteSparseText(oversizedKey, signer.PublicFile, 8 * 1024 * 1024);
        try { File.CreateSymbolicLink(linkedKey, oversizedKey); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Assert.Skip("This account cannot create a file symbolic link: " + ex.Message);
        }
        if (new FileInfo(linkedKey).Length > 4096)
            Assert.Skip("This filesystem reports the opened target length for a file symbolic link.");

        Assert.Empty(Minisign.KeysIn([keys], trusted: true));
    }

    [Fact]
    public void Minisign_ignores_oversized_link_target_for_signature_sidecar()
    {
        var signer = new MinisignKey();
        byte[] content = "signed bytes"u8.ToArray();
        string source = Path.Combine(_dir, "linked-signed.bin");
        File.WriteAllBytes(source, content);
        string oversizedSignature = Path.Combine(_dir, "oversized.minisig");
        string linkedSignature = source + ".minisig";
        WriteSparseText(oversizedSignature, signer.Sign(content, "release") + "\n", 8 * 1024 * 1024);
        try { File.CreateSymbolicLink(linkedSignature, oversizedSignature); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Assert.Skip("This account cannot create a file symbolic link: " + ex.Message);
        }
        if (new FileInfo(linkedSignature).Length > 4096)
            Assert.Skip("This filesystem reports the opened target length for a file symbolic link.");

        var publicKey = Assert.IsType<Minisign.PublicKey>(Minisign.ParsePublicKey(signer.PublicFile, "release.pub"));
        Assert.Equal(VerificationState.SignatureBad,
            Minisign.Verify(source, linkedSignature, [publicKey with { Trusted = true }], TestContext.Current.CancellationToken).State);
    }

    [Fact]
    public void Minisign_refuses_oversized_link_target_for_legacy_signed_content()
    {
        var signer = new MinisignKey();
        byte[] smallContent = "signed bytes"u8.ToArray();
        string oversizedSource = Path.Combine(_dir, "oversized-legacy.bin");
        string linkedSource = Path.Combine(_dir, "linked-legacy.bin");
        using (var stream = new FileStream(oversizedSource, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.SetLength(Minisign.MaxLegacyBytes + 1);
        try { File.CreateSymbolicLink(linkedSource, oversizedSource); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Assert.Skip("This account cannot create a file symbolic link: " + ex.Message);
        }
        if (new FileInfo(linkedSource).Length > Minisign.MaxLegacyBytes)
            Assert.Skip("This filesystem reports the opened target length for a file symbolic link.");

        string signature = Path.Combine(_dir, "legacy.minisig");
        File.WriteAllText(signature, signer.Sign(smallContent, "legacy", prehashed: false));
        var publicKey = Assert.IsType<Minisign.PublicKey>(Minisign.ParsePublicKey(signer.PublicFile, "release.pub"));
        Assert.Equal(VerificationState.SignatureUnchecked,
            Minisign.Verify(linkedSource, signature, [publicKey with { Trusted = true }], TestContext.Current.CancellationToken).State);
    }

    private static void WriteSparseText(string path, string text, int length)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(Encoding.UTF8.GetBytes(text));
        stream.SetLength(length);
    }

    [Fact]
    public void Automatic_signature_result_rechecks_a_trusted_key_rewritten_with_its_metadata_restored()
    {
        var signer = new MinisignKey();
        var replacement = new MinisignKey();
        string keys = Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName;
        string trustedKey = Path.Combine(keys, "release.pub");
        File.WriteAllText(trustedKey, signer.PublicFile);
        byte[] content = "signed archive"u8.ToArray();
        string archive = Path.Combine(_dir, "archive.zip");
        File.WriteAllBytes(archive, content);
        Write("archive.zip.minisig", signer.Sign(content, "release"));
        var service = Service(keys: [keys]);
        Assert.Equal(VerificationState.SignatureGood, service.Automatic(archive, TestContext.Current.CancellationToken)!.State);

        long length = new FileInfo(trustedKey).Length;
        DateTime modified = File.GetLastWriteTimeUtc(trustedKey);
        File.WriteAllText(trustedKey, replacement.PublicFile);
        File.SetLastWriteTimeUtc(trustedKey, modified);
        Assert.Equal(length, new FileInfo(trustedKey).Length);
        Assert.Equal(modified, File.GetLastWriteTimeUtc(trustedKey));
        Assert.NotEqual(signer.PublicFile, File.ReadAllText(trustedKey));

        var unknown = service.Automatic(archive, TestContext.Current.CancellationToken)!;
        Assert.Equal(VerificationState.SignatureUnknownKey, unknown.State);

        // Minisign ignores public-key files above 4 KiB. Their presence must not make every
        // automatic signed row reread the target or retain a fingerprint of unrelated bytes.
        File.WriteAllBytes(Path.Combine(keys, "oversized.pub"), new byte[4097]);
        Thread.Sleep(25);
        Assert.Equal(unknown.CheckedUtc, service.Automatic(archive, TestContext.Current.CancellationToken)!.CheckedUtc);
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
        // I95: no trust line (gpg.conf's trust-model always): gpg vouched for nothing, so neither does FileCat.
        var unjudged = OpenPgp.Interpret("[GNUPG:] GOODSIG 0123 Someone\n[GNUPG:] VALIDSIG 1111222233334444555566667777888899990000 2024-01-01\n");
        Assert.Equal(VerificationState.SignatureUnknownKey, unjudged.State);
        Assert.Null(unjudged.Signer);
        Assert.Contains("did not say whether the key is valid", unjudged.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Gpg_is_found_by_full_path_never_through_a_relative_PATH_entry()
    {
        // Release plan I16: a relative PATH entry is resolved against the current directory, so it is not searched. The
        // folder lies under the current directory to make a relative entry that names it.
        string exe = OperatingSystem.IsWindows() ? "gpg.exe" : "gpg";
        string folder = Path.Combine(Environment.CurrentDirectory, "filecat-gpg-" + Guid.NewGuid().ToString("N")[..8]);
        string bin = Directory.CreateDirectory(Path.Combine(folder, "bin")).FullName;
        try
        {
            File.WriteAllText(Path.Combine(bin, exe), "");
            string relative = Path.GetRelativePath(Environment.CurrentDirectory, bin);
            Assert.False(Path.IsPathFullyQualified(relative));
            Assert.Null(OpenPgp.FindTool(relative, []));
            Assert.Null(OpenPgp.FindTool(Path.Combine(".", relative), ["installed-relative" + Path.DirectorySeparatorChar + exe]));
            string sep = Path.PathSeparator.ToString();
            Assert.Equal(Path.Combine(bin, exe), OpenPgp.FindTool(relative + sep + "\"" + bin + "\"", []));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void Automatic_Gpg_ignores_an_absolute_writable_PATH_entry()
    {
        string root = Directory.CreateTempSubdirectory("filecat-auto-gpg-").FullName;
        string exe = OperatingSystem.IsWindows() ? "gpg.exe" : "gpg";
        try
        {
            string pathEntry = Directory.CreateDirectory(Path.Combine(root, "writable-path")).FullName;
            string installedDir = Directory.CreateDirectory(Path.Combine(root, "installed-candidate")).FullName;
            string fake = Path.Combine(pathEntry, exe);
            string installed = Path.Combine(installedDir, exe);
            File.WriteAllText(fake, "owned replacement");
            File.WriteAllText(installed, "installed fixture");

            Assert.Equal(fake, OpenPgp.FindTool(pathEntry, [])); // explicit lookup still has its own behavior
            Assert.Null(OpenPgp.SelectAutomaticTool(pathEntry, []));
            Assert.Equal(installed, OpenPgp.SelectAutomaticTool(pathEntry, [installed]));
        }
        finally { Directory.Delete(root, recursive: true); }
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
        // Keep the owned home short: gpg-agent socket paths are limited on Windows and macOS.
        string home = OperatingSystem.IsWindows()
            ? Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "fcg-" + Guid.NewGuid().ToString("N")[..8])).FullName
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
            try { Directory.Delete(home, recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Release plan V24, with a packet capture on the named host as the oracle: FILECAT_V24_SHARE is a host FileCat
    /// has no reason to contact. A detached signature that names it as its key server, made by a key this keyring does
    /// not have, in a home whose gpg.conf asks for missing keys to be fetched from it and for the address a signature
    /// names to be honoured — the charter's "malicious local gpg configuration/keyserver hints". Checking it must say
    /// the key is unknown and contact nothing. FILECAT_V24_FETCH=1 checks the same files the way a caller that does
    /// not forbid fetching would, to show on the same capture what that configuration asks for.
    /// </summary>
    [Fact]
    public void A_signature_naming_a_key_server_is_checked_without_contacting_it()
    {
        string? host = Environment.GetEnvironmentVariable("FILECAT_V24_SHARE");
        if (host is null) { Assert.Skip("Set FILECAT_V24_SHARE to a host under capture (and FILECAT_V24_FETCH=1 to show the contact)."); return; }
        string? gpg = FindGpg();
        if (gpg is null) { Assert.Skip("No gpg here."); return; }
        bool fetch = Environment.GetEnvironmentVariable("FILECAT_V24_FETCH") == "1";
        var log = TestContext.Current.TestOutputHelper;
        // gpg-agent's socket lives in the home, and socket paths are short: the test's own folder is too long for it.
        string home = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "fcg-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(home, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            string server = $"hkp://{host}:11371";
            File.WriteAllText(Path.Combine(home, "gpg.conf"), $"keyserver {server}\nauto-key-retrieve\nkeyserver-options honor-keyserver-url\n");
            string file = Write("download.bin", "the file the user downloaded");
            string[] quiet = ["--batch", "--pinentry-mode", "loopback", "--passphrase", ""];
            Run(gpg, home, [.. quiet, "--quick-gen-key", "V24 Fixture <v24@example.invalid>", "ed25519", "sign", "never"]);
            Run(gpg, home, [.. quiet, "--sig-keyserver-url", $"http://{host}:11371", "--detach-sign", "--output", file + ".sig", file]);
            // Nothing is left to check it with: only a fetch from that server could supply the key. (In batch mode gpg
            // deletes a key by its fingerprint, never by the name it carries.)
            var listing = new ProcessStartInfo(gpg) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (string a in new[] { "--homedir=" + home, "--batch", "--with-colons", "--list-keys" }) listing.ArgumentList.Add(a);
            using (var list = Process.Start(listing)!)
            {
                string keys = list.StandardOutput.ReadToEnd();
                list.WaitForExit(60_000);
                string fingerprint = keys.Split('\n').First(l => l.StartsWith("fpr:", StringComparison.Ordinal)).Split(':')[9];
                Run(gpg, home, [.. quiet, "--yes", "--delete-secret-and-public-key", fingerprint]);
            }
            log?.WriteLine($"the signature names {server}; the keyring is empty");

            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (fetch)
            {
                // What the configuration asks for, run as a caller that does not forbid it: it may well fail, and
                // whether it does is not the point — the capture is.
                var start = new ProcessStartInfo(gpg) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (string a in new[] { "--homedir=" + home, "--batch", "--no-tty", "--verify", "--", file + ".sig", file }) start.ArgumentList.Add(a);
                using var p = Process.Start(start)!;
                p.StandardOutput.ReadToEnd();
                string errors = p.StandardError.ReadToEnd();
                p.WaitForExit(120_000);
                log?.WriteLine($"gpg without --no-auto-key-retrieve: exit {p.ExitCode} in {clock.Elapsed.TotalSeconds:N1} s; {errors.Trim().Replace('\n', ' ')}");
                return;
            }

            var result = OpenPgp.Verify(file + ".sig", file, TestContext.Current.CancellationToken, home);
            log?.WriteLine($"FileCat: {result.State} in {clock.Elapsed.TotalSeconds:N1} s; {result.Text}");
            Assert.Equal(VerificationState.SignatureUnknownKey, result.State);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"Took {clock.Elapsed}: the key server was tried.");
        }
        finally
        {
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
            try { Directory.Delete(home, recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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
    public void A_file_being_written_is_not_read_until_it_has_been_still()
    {
        // A copy or a download to the final name, read half-done, would "differ": it waits instead, and says so.
        string path = Write("download.iso", "partial");
        Write("download.iso.sha256", Sha256("complete") + "\n");
        var service = new VerificationService(new VerificationCache(null), () => 256L << 20, () => []) { Settle = TimeSpan.FromMinutes(1) };
        var waiting = service.Automatic(path, TestContext.Current.CancellationToken)!;
        Assert.Equal((VerificationState.NotChecked, "being written", true), (waiting.State, waiting.Text, waiting.Settling));
        // Still for long enough: read.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
        Assert.Equal(VerificationState.Differs, service.Automatic(path, TestContext.Current.CancellationToken)!.State);
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
    public void While_a_disk_holding_GnuPGs_folder_is_recovered_gpg_is_not_run_and_nothing_is_kept()
    {
        // Release plan V09 (I09): gpg may write into its folder while it checks (trust database, locks), so while FileCat
        // recovers deleted files from the disk that holds that folder, OpenPGP signatures stay unchecked, saying why, and
        // that result is not kept to be shown later. Minisign is FileCat's own and goes on.
        string cacheFile = Path.Combine(_dir, "cache", "verification.jsonl");
        string signed = Write("release.tar", "release");
        Write("release.tar.asc", "-----BEGIN PGP SIGNATURE-----\nnot really\n-----END PGP SIGNATURE-----\n");
        var service = Service(cacheFile: cacheFile);
        service.SignatureToolsPaused = true;
        var held = service.Automatic(signed, TestContext.Current.CancellationToken)!;
        Assert.Contains("recovers deleted files", string.Join("\n", [held.Text, .. held.Details]), StringComparison.Ordinal);
        service.SignatureToolsPaused = false;
        var later = service.Automatic(signed, TestContext.Current.CancellationToken)!;
        Assert.DoesNotContain("recovers deleted files", string.Join("\n", [later.Text, .. later.Details]), StringComparison.Ordinal);
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

    private JobManager Jobs()
    {
        var providers = new Resources.ProviderRegistry();
        providers.Register(new FileSystem.LocalFileSystemProvider());
        return new JobManager(new FileSystem.PortableFileOperations(), providers, Path.Combine(_dir, "journal"));
    }

    private static async Task<Job> RunAsync(JobManager jobs, JobKind kind, params string[] sources)
    {
        var job = jobs.Submit(new JobRequest { Kind = kind, Sources = [.. sources.Select(s => Resources.ItemRef.ForFileSystemPath(s, Resources.EntryKind.File))] });
        // A job that does not end fails the test instead of holding up the run.
        var deadline = DateTime.UtcNow.AddMinutes(1);
        while (!job.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline, $"{job.Title} did not end within a minute ({job.State}).");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    [Fact]
    public void A_check_asked_for_keeps_each_claim_with_both_values_and_says_what_a_result_means()
    {
        Write("a.txt", "a");
        Write("b.txt", "b");
        string sums = Write("SHA256SUMS", $"{Sha256("a")}  a.txt\n{Sha256("not b")}  b.txt\n");
        var a = Check("a.txt");
        var match = Assert.Single(a.Checksums);
        Assert.Equal((ChecksumKind.Sha256, Sha256("a"), sums, 1, Sha256("a"), true),
            (match.Claim.Kind, match.Claim.Expected, match.Claim.Source, match.Claim.Line, match.Actual, match.Matches));
        Assert.Contains(a.Notes, n => n.Contains("proves nothing against tampering", StringComparison.Ordinal));
        var b = Check("b.txt");
        var differs = Assert.Single(b.Checksums);
        Assert.Equal((Sha256("not b"), Sha256("b"), 2, false), (differs.Claim.Expected, differs.Actual, differs.Claim.Line, differs.Matches));
        Assert.Contains(b.Notes, n => n.StartsWith("It is not the file the checksum describes", StringComparison.Ordinal));
        Assert.DoesNotContain(b.Notes, n => n.Contains("proves nothing against tampering", StringComparison.Ordinal));
        // The row's tooltip says the same (the notes are among the details).
        Assert.Contains(b.Details, d => d.StartsWith("It is not the file the checksum describes", StringComparison.Ordinal));
        var entry = VerificationReport.Entry(Path.Combine(_dir, "b.txt"), "b.txt", b);
        Assert.Equal((VerifiedOutcome.Failed, "✗ SHA-256 differs", false), (entry.Outcome, entry.Text, entry.Authentic));
        Assert.Same(b.Checksums, entry.Checksums);
    }

    [Fact]
    public async Task Verifying_files_on_request_reports_each_one_problems_first()
    {
        string good = Write("good.bin", "good"), bad = Write("bad.bin", "bad"), lonely = Write("lonely.txt", "alone");
        Write("SHA256SUMS", $"{Sha256("good")}  good.bin\n{Sha256("not bad")}  bad.bin\n");
        var previous = VerificationService.Current;
        VerificationService.Current = Service();
        try
        {
            var job = await RunAsync(Jobs(), JobKind.VerifyBeside, good, lonely, bad);
            var report = job.VerificationReport!;
            Assert.Equal(_dir, report.Folder);
            // Problems first, then matches, then the files nothing was compared for.
            Assert.Equal([("bad.bin", VerifiedOutcome.Failed), ("good.bin", VerifiedOutcome.Good), ("lonely.txt", VerifiedOutcome.NotChecked)],
                report.Files.Select(f => (f.Name, f.Outcome)));
            Assert.Equal((1, 1, 1, 3, 0), (report.Count(VerifiedOutcome.Failed), report.Count(VerifiedOutcome.Good), report.Count(VerifiedOutcome.NotChecked), report.Total, report.Unlisted));
            var failed = Assert.Single(report.Files[0].Checksums);
            Assert.Equal((Sha256("not bad"), Sha256("bad")), (failed.Claim.Expected, failed.Actual));
            Assert.Equal(bad, report.Files[0].Path);
            Assert.StartsWith("Nothing beside it says what it should be", Assert.Single(report.Files[2].Notes), StringComparison.Ordinal);
            // The job's own counts agree with what is listed.
            Assert.Equal("1 verified, 1 failed, 1 with nothing to check against", job.Summary);

            // A file gone since it was chosen is reported as such.
            File.Delete(lonely);
            var gone = await RunAsync(Jobs(), JobKind.VerifyBeside, lonely);
            Assert.Equal(VerifiedOutcome.Missing, Assert.Single(gone.VerificationReport!.Files).Outcome);
        }
        finally
        {
            VerificationService.Current = previous;
        }
    }

    [Fact]
    public async Task A_checksum_file_chosen_for_verification_has_its_own_signature_checked()
    {
        var key = new MinisignKey();
        string keys = Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName;
        File.WriteAllText(Path.Combine(keys, "publisher.pub"), key.PublicFile);
        string image = Write("image.iso", "disc image");
        string sums = Write("SHA256SUMS", Sha256("disc image") + "  image.iso\n");
        Write("SHA256SUMS.minisig", key.Sign(File.ReadAllBytes(sums), "release list"));
        var previous = VerificationService.Current;
        try
        {
            var jobs = Jobs();
            // Signed by a key the user trusts: the files it lists are authentic.
            VerificationService.Current = Service(keys: [keys]);
            var signed = await RunAsync(jobs, JobKind.VerifyChecksums, sums);
            Assert.Equal((JobState.Completed, "1 verified, all match, SHA256SUMS signed by publisher"), (signed.State, signed.Summary));
            var file = Assert.Single(signed.VerificationReport!.Files);
            Assert.Equal((VerifiedOutcome.Good, true, "image.iso", "✓ SHA-256 · manifest signed by publisher"), (file.Outcome, file.Authentic, file.Name, file.Text));
            Assert.Equal(("SHA256SUMS", VerificationState.SignatureGood), (Assert.Single(file.Signatures).Manifest, file.Signatures[0].Result.State));
            Assert.Contains(signed.VerificationReport.Notes, n => n.StartsWith("SHA256SUMS.minisig (over SHA256SUMS): good minisign signature", StringComparison.Ordinal));

            // Without the key among the trusted ones, the list vouches for no more than its checksums: unsure, said once.
            VerificationService.Current = Service();
            var unsure = await RunAsync(jobs, JobKind.VerifyChecksums, sums);
            Assert.Equal(JobState.CompletedWithIssues, unsure.State);
            Assert.Equal(VerifiedOutcome.Unsure, Assert.Single(unsure.VerificationReport!.Files).Outcome);
            Assert.Equal("1 verified, all match, SHA256SUMS: ? signed by an unknown key", unsure.Summary);
            Assert.Single(unsure.Issues, i => i.Severity == IssueSeverity.Warning);

            // A list changed after it was signed fails every file it lists, although their checksums match it.
            VerificationService.Current = Service(keys: [keys]);
            File.AppendAllText(sums, "# added later\n");
            var bad = await RunAsync(jobs, JobKind.VerifyChecksums, sums);
            Assert.Equal(JobState.Failed, bad.State);
            Assert.Equal("1 match, SHA256SUMS: ✗ bad signature", bad.Summary);
            var failed = Assert.Single(bad.VerificationReport!.Files);
            Assert.Equal((VerifiedOutcome.Failed, true), (failed.Outcome, Assert.Single(failed.Checksums).Matches));
            Assert.Contains(bad.Issues, i => i.Path == image && i.Cause == VerifyChecksumsExecutor.SignatureCause);
        }
        finally
        {
            VerificationService.Current = previous;
        }
    }

    [Fact]
    public void A_full_list_still_shows_a_mismatch_and_an_unsure_file_before_any_missing_one()
    {
        // A list of hundreds of thousands of files, most of them missing (planned first) and a few corrupt (read last).
        var report = new VerificationReport(_dir);
        static VerifiedFile Entry(string name, VerifiedOutcome outcome) => new(null, name, outcome, "");
        for (int i = 0; i < VerificationReport.MaxListed; i++) report.Add(Entry($"missing{i}", VerifiedOutcome.Missing));
        report.Add(Entry("nothing", VerifiedOutcome.NotChecked)); // worth less than everything listed: counted, not listed
        report.Add(Entry("unsure", VerifiedOutcome.Unsure));      // worth more: takes the place of the last missing one
        report.Add(Entry("corrupt", VerifiedOutcome.Failed));     // so does a mismatch, and it is shown first
        Assert.Equal(["corrupt", "unsure"], report.Files.Take(2).Select(f => f.Name));
        Assert.Equal(VerificationReport.MaxListed, report.Files.Count);
        Assert.DoesNotContain(report.Files, f => f.Name == "nothing");
        Assert.Equal((1, 1, VerificationReport.MaxListed, 3),
            (report.Count(VerifiedOutcome.Failed), report.Count(VerifiedOutcome.Unsure), report.Count(VerifiedOutcome.Missing), report.Unlisted));
    }

    [Fact]
    public void Every_outcome_has_its_place_in_the_list()
    {
        // An outcome missing from the order would be worth less than nothing, and crowd out failures.
        Assert.Equal(Enum.GetValues<VerifiedOutcome>().Order(), VerificationReport.Shown.Order());
        Assert.Equal(VerifiedOutcome.Failed, VerificationReport.Shown[0]);
    }

    [Fact]
    public async Task A_signed_list_makes_files_authentic_only_through_a_hash_that_cannot_be_forged()
    {
        var key = new MinisignKey();
        string keys = Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName;
        File.WriteAllText(Path.Combine(keys, "publisher.pub"), key.PublicFile);
        // CRC-32 is forged by appending four bytes; MD5 by chosen-prefix collisions: a good signature over such a list
        // shows the list is the publisher's, not that a file with a matching value is.
        Write("track.flac", "music");
        string sfv = Write("album.sfv", $"track.flac {Crc32.Append(0, Encoding.UTF8.GetBytes("music")):x8}\n");
        Write("album.sfv.minisig", key.Sign(File.ReadAllBytes(sfv), "album"));
        Write("tool.exe", "tool");
        string md5 = Write("MD5SUMS", Convert.ToHexStringLower(MD5.HashData("tool"u8)) + "  tool.exe\n");
        Write("MD5SUMS.minisig", key.Sign(File.ReadAllBytes(md5), "tools"));
        var service = Service(keys: [keys]);
        foreach (string file in new[] { "track.flac", "tool.exe" })
        {
            var result = Check(file, service);
            Assert.Equal(VerificationState.Matches, result.State);
            Assert.Contains("can be forged", result.Text, StringComparison.Ordinal);
            Assert.Contains(result.Notes, n => n.EndsWith("the file is intact, not proven authentic.", StringComparison.Ordinal));
            Assert.Equal(VerificationState.SignatureGood, Assert.Single(result.Signatures).Result.State); // the list itself is the publisher's
            Assert.False(VerificationReport.Entry(Path.Combine(_dir, file), file, result).Authentic);
        }
        // The same through the checksum file chosen on its own.
        var previous = VerificationService.Current;
        VerificationService.Current = service;
        try
        {
            var entry = Assert.Single((await RunAsync(Jobs(), JobKind.VerifyChecksums, sfv)).VerificationReport!.Files);
            Assert.Equal((VerifiedOutcome.Good, false), (entry.Outcome, entry.Authentic));
        }
        finally
        {
            VerificationService.Current = previous;
        }
    }

    [Fact]
    public void A_list_changed_after_its_lines_were_read_is_not_vouched_for_by_its_signature()
    {
        var key = new MinisignKey();
        string keys = Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName;
        File.WriteAllText(Path.Combine(keys, "publisher.pub"), key.PublicFile);
        Write("image.iso", "disc image");
        string sums = Write("SHA256SUMS", Sha256("disc image") + "  image.iso\n");
        Write("SHA256SUMS.minisig", key.Sign(File.ReadAllBytes(sums), "release"));
        // (Another service: one that has checked the signature already answers from what it found.)
        Assert.Equal(VerificationState.SignatureGood, Service(keys: [keys]).Automatic(Path.Combine(_dir, "image.iso"), TestContext.Current.CancellationToken)!.State);
        var service = Service(keys: [keys]);

        // Its lines are read (the scan is kept a few seconds); then the list is rewritten in place, which leaves the folder's
        // own time alone. Its signature is checked over the bytes the lines came from, which are gone: not vouched for.
        service.SidecarsOf(_dir, TestContext.Current.CancellationToken, forceRefresh: true);
        File.WriteAllText(sums, Sha256("disc image") + "  image.iso\n# appended later\n");
        File.WriteAllText(Path.Combine(_dir, "image.iso"), "disc image"); // a new time: the kept result does not answer
        var result = service.Automatic(Path.Combine(_dir, "image.iso"), TestContext.Current.CancellationToken)!;
        Assert.Equal(VerificationState.SignatureUnchecked, result.State);
        Assert.Contains("changed after its lines were read", Assert.Single(result.Signatures).Result.Text, StringComparison.Ordinal);

        // Read again, the list's new bytes are what its signature is checked over: it no longer matches them.
        Assert.Equal(VerificationState.SignatureBad, Check("image.iso", service).State);
    }

    [Fact]
    public async Task Each_checksum_file_in_one_job_is_vouched_for_by_its_own_signature()
    {
        var key = new MinisignKey();
        var stranger = new MinisignKey();
        string keys = Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName;
        File.WriteAllText(Path.Combine(keys, "publisher.pub"), key.PublicFile);
        Write("one.bin", "one");
        Write("two.bin", "two");
        Write("three.bin", "three");
        string signed = Write("SHA256SUMS", Sha256("one") + "  one.bin\n");
        Write("SHA256SUMS.minisig", key.Sign(File.ReadAllBytes(signed), "release"));
        string other = Write("other.sha256", Sha256("two") + "  two.bin\n");
        Write("other.sha256.minisig", stranger.Sign(File.ReadAllBytes(other), "someone"));
        string plain = Write("plain.sha256", Sha256("three") + "  three.bin\n");
        var previous = VerificationService.Current;
        VerificationService.Current = Service(keys: [keys]);
        try
        {
            var job = await RunAsync(Jobs(), JobKind.VerifyChecksums, signed, other, plain);
            var files = job.VerificationReport!.Files.ToDictionary(f => f.Name);
            Assert.Equal((VerifiedOutcome.Good, true), (files["one.bin"].Outcome, files["one.bin"].Authentic));
            Assert.Equal(VerifiedOutcome.Unsure, files["two.bin"].Outcome);
            Assert.Equal((VerifiedOutcome.Good, false), (files["three.bin"].Outcome, files["three.bin"].Authentic));
            Assert.Contains("SHA256SUMS signed by publisher", job.Summary, StringComparison.Ordinal);
            Assert.Contains("other.sha256: ? signed by an unknown key", job.Summary, StringComparison.Ordinal);
        }
        finally
        {
            VerificationService.Current = previous;
        }
    }

    [Fact]
    public async Task While_gpg_is_held_off_a_list_it_signs_vouches_for_nothing()
    {
        Write("release.tar", "release");
        string sums = Write("SHA256SUMS", Sha256("release") + "  release.tar\n");
        Write("SHA256SUMS.asc", "-----BEGIN PGP SIGNATURE-----\nnot really\n-----END PGP SIGNATURE-----\n");
        var service = Service();
        service.SignatureToolsPaused = true;
        var previous = VerificationService.Current;
        VerificationService.Current = service;
        try
        {
            var job = await RunAsync(Jobs(), JobKind.VerifyChecksums, sums);
            var entry = Assert.Single(job.VerificationReport!.Files);
            Assert.Equal(VerifiedOutcome.Unsure, entry.Outcome);
            Assert.Contains("recovers deleted files", Assert.Single(entry.Signatures).Result.Text, StringComparison.Ordinal);
            Assert.Equal(JobState.CompletedWithIssues, job.State);
        }
        finally
        {
            VerificationService.Current = previous;
        }
    }

    [Fact]
    public void A_checksum_files_signatures_are_found_by_name_and_names_match_by_the_folders_rule()
    {
        string sums = Write("SUMS", "x");
        Write("SUMS.ASC", "signature");         // any letter case of the extension
        Write("SUMS.minisig", "signature");
        Write("SUMSX.asc", "signature");        // signs another file
        Write("SUMS.txt", "not a signature");
        var claims = FolderSidecars.SignaturesOver(sums)!;
        Assert.Equal([("SUMS.ASC", SignatureKind.OpenPgp), ("SUMS.minisig", SignatureKind.Minisign)],
            claims.Select(c => (Path.GetFileName(c.Signature), c.Kind)).Order());
        Assert.All(claims, c => Assert.Equal(sums, c.Signed));

        // A bare checksum is known as such from the scan; a list is not.
        Write("disc.iso", "disc");
        Write("disc.iso.sha256", Sha256("disc") + "\n");
        Write("SHA256SUMS", Sha256("disc") + "  disc.iso\n");
        var sidecars = FolderSidecars.Scan(_dir, TestContext.Current.CancellationToken);
        Assert.True(sidecars.IsBare("disc.iso.sha256"));
        Assert.False(sidecars.IsBare("SHA256SUMS"));

        // Ubuntu signs SHA256SUMS as SHA256SUMS.gpg; any other .gpg is more likely an encrypted file than a signature.
        Assert.True(SidecarNames.IsSignatureFile("SHA256SUMS.gpg"));
        Assert.False(SidecarNames.IsSignatureFile("backup.tar.gpg"));
        Write("SHA256SUMS.gpg", "signature");
        Write("disc.iso.gpg", "encrypted");
        var gpg = Assert.Single(FolderSidecars.SignaturesOver(Path.Combine(_dir, "SHA256SUMS"))!);
        Assert.Equal(("SHA256SUMS.gpg", SignatureKind.OpenPgp), (Path.GetFileName(gpg.Signature), gpg.Kind));
        Assert.Empty(FolderSidecars.Scan(_dir, TestContext.Current.CancellationToken).Signatures("disc.iso"));
    }

    [Fact]
    public void A_checksum_files_signatures_are_found_whatever_its_name_holds()
    {
        // A name is compared, not matched as a pattern: wildcards and (on Unix) a backslash are only characters in it.
        string name = OperatingSystem.IsWindows() ? "rel[1] (x).sha256" : "rel" + (char)92 + "b*?[1].sha256";
        string sums = Write(name, Sha256("x") + "  x\n");
        Write(name + ".asc", "signature");
        Write("relXb.sha256.asc", "another file's");
        Assert.Equal(name + ".asc", Path.GetFileName(Assert.Single(FolderSidecars.SignaturesOver(sums)!).Signature));
    }

    [Fact]
    public void A_one_line_list_naming_no_file_here_is_no_bare_checksum_and_breaks_nothing()
    {
        // SHA256SUMS of one line for a file not downloaded yet (or in a subfolder): once, the scan failed on such a name.
        Write("other.txt", "other");
        Write("SHA256SUMS", Sha256("disc") + "  disc.iso\n");
        Write("MD5SUMS", Convert.ToHexStringLower(MD5.HashData("x"u8)) + "  sub/x\n");
        var sidecars = FolderSidecars.Scan(_dir, TestContext.Current.CancellationToken);
        Assert.False(sidecars.Covers("other.txt"));
        Assert.Empty(sidecars.Covered("SHA256SUMS"));
        Assert.Empty(sidecars.Covered("MD5SUMS"));

        // "X.iso.sha256.txt" holding a bare value checks "X.iso", as "X.iso.sha256" does.
        Write("image.iso", "image");
        Write("image.iso.sha256.txt", Sha256("image") + "\n");
        Assert.Equal(Sha256("image"), Assert.Single(FolderSidecars.Scan(_dir, TestContext.Current.CancellationToken).Checksums("image.iso")).Expected);
    }

    [Fact]
    public void A_minisign_signature_is_checked_over_bytes_in_memory()
    {
        // The bytes a list's lines were read from: no copy is written anywhere, and the file read again plays no part.
        var key = new MinisignKey();
        byte[] list = Encoding.UTF8.GetBytes(Sha256("disc") + "  disc.iso\r\n");
        string signature = Write("SHA256SUMS.minisig", key.Sign(list, "release"));
        string folder = Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName;
        File.WriteAllText(Path.Combine(folder, "publisher.pub"), key.PublicFile);
        var keys = Minisign.KeysIn([folder], trusted: true);
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(VerificationState.SignatureGood, Minisign.Verify(list, signature, keys, ct).State);
        Assert.Equal(VerificationState.SignatureBad, Minisign.Verify(list.Append((byte)'x').ToArray(), signature, keys, ct).State);
        // The legacy form signs the bytes themselves.
        string legacy = Write("legacy.minisig", key.Sign(list, "old", prehashed: false));
        Assert.Equal(VerificationState.SignatureGood, Minisign.Verify(list, legacy, keys, ct).State);
    }

    [Fact]
    public async Task Files_one_signed_list_names_are_each_authentic_in_one_job()
    {
        // In one job the list is read once and its signature checked once: every file it names still gets that result.
        var key = new MinisignKey();
        string keys = Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName;
        File.WriteAllText(Path.Combine(keys, "publisher.pub"), key.PublicFile);
        var names = Enumerable.Range(0, 12).Select(i => $"part{i:00}.bin").ToList();
        foreach (string name in names) Write(name, name);
        string sums = Write("SHA256SUMS", string.Concat(names.Select(n => $"{Sha256(n)}  {n}\n")));
        Write("SHA256SUMS.minisig", key.Sign(File.ReadAllBytes(sums), "release"));
        var previous = VerificationService.Current;
        VerificationService.Current = Service(keys: [keys]);
        try
        {
            var job = await RunAsync(Jobs(), JobKind.VerifyBeside, [.. names.Select(n => Path.Combine(_dir, n))]);
            Assert.Equal((12, 12), (job.VerificationReport!.Total, job.VerificationReport.Authentic));
            Assert.Equal("12 verified, all good", job.Summary);
        }
        finally
        {
            VerificationService.Current = previous;
        }
    }

    [Fact]
    public void Results_list_problems_before_matches_and_count_what_they_cannot_list()
    {
        var report = new VerificationReport(_dir);
        static VerifiedFile Entry(string name, VerifiedOutcome outcome) => new(null, name, outcome, "");
        for (int i = 0; i < VerificationReport.MaxListed; i++) report.Add(Entry($"f{i}", i % 2 == 0 ? VerifiedOutcome.Good : VerifiedOutcome.NotChecked));
        report.Add(Entry("failed", VerifiedOutcome.Failed));        // takes the place of a file nothing was compared for
        report.Add(Entry("good", VerifiedOutcome.Good));            // so does a match
        report.Add(Entry("unchecked", VerifiedOutcome.NotChecked)); // not listed: everything listed is worth more
        Assert.Equal((VerificationReport.MaxListed + 3, 3, VerificationReport.MaxListed), (report.Total, report.Unlisted, report.Files.Count));
        Assert.Equal("failed", report.Files[0].Name);
        Assert.Contains(report.Files, f => f.Name == "good");
        Assert.DoesNotContain(report.Files, f => f.Name == "unchecked");
        Assert.Equal((VerificationReport.MaxListed / 2 + 1, VerificationReport.MaxListed / 2 + 1), (report.Count(VerifiedOutcome.Good), report.Count(VerifiedOutcome.NotChecked)));

        // The results of checks one command started, as one: counts add up, and problems still come first.
        var other = new VerificationReport(_dir);
        other.Add(Entry("gone", VerifiedOutcome.Missing));
        other.Note("a note");
        var all = VerificationReport.Combine([report, other]);
        Assert.Equal((report.Total + 1, 1), (all.Total, all.Count(VerifiedOutcome.Missing)));
        Assert.Equal(["failed", "gone"], all.Files.Take(2).Select(f => f.Name));
        Assert.Equal(["a note"], all.Notes);
        // Always a report of its own: what the dialog adds to it is not added to the job's.
        var alone = VerificationReport.Combine([other]);
        Assert.NotSame(other, alone);
        Assert.Equal((1, 0, 0), (alone.Total, alone.Unlisted, alone.Authentic));
    }

    [Fact]
    public void Checks_started_together_show_each_file_once_with_every_claim_on_it()
    {
        string iso = Path.Combine(_dir, "disc.iso"), other = Path.Combine(_dir, "other.bin");
        var claimHere = new ChecksumComparison(new ChecksumClaim(ChecksumKind.Sha256, Sha256("disc"), Path.Combine(_dir, "disc.iso.sha256"), 1), Sha256("disc"));
        var claimThere = new ChecksumComparison(new ChecksumClaim(ChecksumKind.Sha256, Sha256("old disc"), Path.Combine(_dir, "up", "SHA256SUMS"), 3), Sha256("disc"));
        // The check beside the files found it intact (and found nothing for another file); a list elsewhere says it differs.
        var beside = new VerificationReport(_dir);
        beside.Add(new VerifiedFile(iso, "disc.iso", VerifiedOutcome.Good, "intact") { Checksums = [claimHere], Notes = ["unsigned"] });
        beside.Add(new VerifiedFile(other, "other.bin", VerifiedOutcome.NotChecked, "nothing to check against") { Notes = ["Nothing beside it says what it should be."] });
        beside.IsComplete = true;
        var lists = new VerificationReport(_dir);
        // Listed in another spelling: on Windows the same file (an ordinary folder ignores letter case), elsewhere exactly as named.
        lists.Add(new VerifiedFile(OperatingSystem.IsWindows() ? Path.Combine(_dir, "DISC.ISO") : iso, "DISC.ISO", VerifiedOutcome.Failed, "differs")
        {
            Checksums = [claimThere],
        });
        lists.Add(new VerifiedFile(other, "other.bin", VerifiedOutcome.Good, "intact") { Authentic = true });
        lists.IsComplete = true;

        var all = VerificationReport.Combine([beside, lists]);
        // Each file once: the worse result wins, and keeps every claim; why nothing was compared is moot once something was.
        Assert.Equal([("disc.iso", VerifiedOutcome.Failed), ("other.bin", VerifiedOutcome.Good)], all.Files.Select(f => (f.Name, f.Outcome)));
        Assert.Equal([claimThere, claimHere], all.Files[0].Checksums);
        Assert.Equal(["unsigned"], all.Files[0].Notes);
        Assert.Empty(all.Files[1].Notes);
        Assert.Equal((2, 1, 1, 0, 1), (all.Total, all.Count(VerifiedOutcome.Failed), all.Count(VerifiedOutcome.Good), all.Count(VerifiedOutcome.NotChecked), all.Authentic));
        Assert.True(all.IsComplete);

        // A file elsewhere with the same name is another file; one check that did not end leaves the whole incomplete.
        var elsewhere = new VerificationReport(_dir);
        elsewhere.Add(new VerifiedFile(Path.Combine(_dir, "sub", "disc.iso"), "sub/disc.iso", VerifiedOutcome.Good, "intact"));
        var three = VerificationReport.Combine([beside, lists, elsewhere]);
        Assert.Equal((3, false), (three.Total, three.IsComplete));
    }
}
