using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Jobs;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

/// <summary>
/// D-57: a signature over a checksum file is checked over the very bytes its lines were read from, which gpg reads from its
/// standard input (no copy is written anywhere), and once for all the files the list names: within one job, and for the
/// rows shown. These tests choose which gpg runs, so they run apart from every other verification test.
/// </summary>
[Collection(nameof(GnuPgToolOverride))]
public sealed class OpenPgpContentTests : IDisposable
{
    // Short: gpg-agent's socket lives in the home folder, and its path has a length limit.
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "fcs" + Guid.NewGuid().ToString("N")[..6])).FullName;
    private string? _gpgconf;

    public void Dispose()
    {
        OpenPgp.UseTool(null);
        if (_gpgconf is not null) Run(_gpgconf, ["--homedir", Path.Combine(_root, "home"), "--kill", "all"], out _);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static int Run(string exe, IEnumerable<string> args, out string output)
    {
        var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(60_000)) { p.Kill(true); output = "timed out"; return -1; }
        output = stdout.Result + stderr.Result;
        return p.ExitCode;
    }

    /// <summary>A gpg that vouches for every signature (a fully trusted key) and counts how often it is asked.</summary>
    private string StubGpg()
    {
        string log = Path.Combine(_root, "calls.log");
        string[] status =
        [
            "[GNUPG:] GOODSIG 0123456789ABCDEF Publisher",
            "[GNUPG:] VALIDSIG 0123456789ABCDEF0123456789ABCDEF01234567 2026-01-01 0 4 0 22 8 00 0123456789ABCDEF0123456789ABCDEF01234567",
            "[GNUPG:] TRUST_FULLY 0 pgp",
        ];
        if (OperatingSystem.IsWindows())
        {
            string stub = Path.Combine(_root, "gpg-stub.cmd");
            File.WriteAllText(stub, "@echo off\r\necho run>>\"" + log + "\"\r\n" + string.Concat(status.Select(s => "echo " + s + "\r\n")));
            return stub;
        }
        string script = Path.Combine(_root, "gpg-stub");
        File.WriteAllText(script, "#!/bin/sh\necho run >> '" + log + "'\n" + string.Concat(status.Select(s => "echo '" + s + "'\n")));
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script;
    }

    private int Calls => File.Exists(Path.Combine(_root, "calls.log")) ? File.ReadAllLines(Path.Combine(_root, "calls.log")).Length : 0;

    private JobManager Jobs()
    {
        var providers = new Resources.ProviderRegistry();
        providers.Register(new FileSystem.LocalFileSystemProvider());
        return new JobManager(new FileSystem.PortableFileOperations(), providers, Path.Combine(_root, "journal"));
    }

    private static async Task<Job> RunAsync(JobManager jobs, IEnumerable<string> files)
    {
        var job = jobs.Submit(new JobRequest { Kind = JobKind.VerifyBeside, Sources = [.. files.Select(f => Resources.ItemRef.ForFileSystemPath(f, Resources.EntryKind.File))] });
        var deadline = DateTime.UtcNow.AddMinutes(1);
        while (!job.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline, $"{job.Title} did not end within a minute ({job.State}).");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    [Fact]
    public async Task A_list_signature_is_checked_once_for_all_the_files_it_names()
    {
        var names = Enumerable.Range(0, 12).Select(i => $"part{i:00}.bin").ToList();
        var files = names.Select(n => Write(n, n)).ToList();
        Write("SHA256SUMS", string.Concat(names.Select(n => $"{Sha256(n)}  {n}\n")));
        Write("SHA256SUMS.asc", "-----BEGIN PGP SIGNATURE-----\nstub\n-----END PGP SIGNATURE-----\n");
        OpenPgp.UseTool(StubGpg());
        var service = new VerificationService(new VerificationCache(null), () => 256L << 20, () => []) { Settle = TimeSpan.Zero };
        var previous = VerificationService.Current;
        VerificationService.Current = service;
        try
        {
            // One job: the list's signature is checked once, and vouches for each file it names.
            var job = await RunAsync(Jobs(), files);
            Assert.Equal((12, 12, "12 verified, all good"), (job.VerificationReport!.Total, job.VerificationReport.Authentic, job.Summary));
            Assert.Equal(1, Calls);

            // The rows shown (here by a service that kept no result yet): one check for all of them, while the list, its
            // signature, and the keyring are as they were.
            var rows = new VerificationService(new VerificationCache(null), () => 256L << 20, () => []) { Settle = TimeSpan.Zero };
            foreach (string file in files)
                Assert.Equal(VerificationState.SignatureGood, rows.Automatic(file, TestContext.Current.CancellationToken)!.State);
            Assert.Equal(2, Calls);

            // A check asked for again checks the signature again: once.
            await RunAsync(Jobs(), files);
            Assert.Equal(3, Calls);
        }
        finally
        {
            VerificationService.Current = previous;
        }
    }

    [Fact]
    public async Task A_checksum_file_left_whole_to_the_check_beside_its_files_is_not_checked_again()
    {
        // Its lines were compared beside their files, and its signature checked there over the same bytes.
        Write("disc.iso", "disc");
        string sums = Write("SHA256SUMS", $"{Sha256("disc")}  disc.iso\n");
        Write("SHA256SUMS.asc", "-----BEGIN PGP SIGNATURE-----\nstub\n-----END PGP SIGNATURE-----\n");
        OpenPgp.UseTool(StubGpg());
        var previous = VerificationService.Current;
        VerificationService.Current = new VerificationService(new VerificationCache(null), () => 256L << 20, () => []) { Settle = TimeSpan.Zero };
        try
        {
            var compared = new ComparedLines();
            compared.Add(new ChecksumClaim(Operations.ChecksumKind.Sha256, Sha256("disc"), Path.GetFullPath(sums), 1), Path.GetFullPath(Path.Combine(_root, "disc.iso")));
            var job = Jobs().Submit(new JobRequest { Kind = JobKind.VerifyChecksums, Sources = [Resources.ItemRef.ForFileSystemPath(sums, Resources.EntryKind.File)], ComparedBeside = compared });
            var deadline = DateTime.UtcNow.AddMinutes(1);
            while (!job.State.IsFinished())
            {
                Assert.True(DateTime.UtcNow < deadline, $"{job.Title} did not end within a minute ({job.State}).");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.Equal("the files it lists were checked beside them", job.Summary);
            Assert.Equal(0, Calls);
        }
        finally
        {
            VerificationService.Current = previous;
        }
    }

    [Fact]
    public async Task Many_signed_checksum_files_are_summed_up_and_their_folder_listed_once()
    {
        // Twelve per-file checksum files, each signed: one summary part for all, not twelve.
        var lists = new List<string>();
        for (int i = 0; i < 12; i++)
        {
            Write($"part{i:00}.bin", $"part {i}");
            lists.Add(Write($"part{i:00}.bin.sha256", $"{Sha256($"part {i}")}  part{i:00}.bin\n"));
            Write($"part{i:00}.bin.sha256.asc", "-----BEGIN PGP SIGNATURE-----\nstub\n-----END PGP SIGNATURE-----\n");
        }
        OpenPgp.UseTool(StubGpg());
        var previous = VerificationService.Current;
        VerificationService.Current = new VerificationService(new VerificationCache(null), () => 256L << 20, () => []) { Settle = TimeSpan.Zero };
        try
        {
            var job = Jobs().Submit(new JobRequest { Kind = JobKind.VerifyChecksums, Sources = [.. lists.Select(l => Resources.ItemRef.ForFileSystemPath(l, Resources.EntryKind.File))] });
            var deadline = DateTime.UtcNow.AddMinutes(1);
            while (!job.State.IsFinished())
            {
                Assert.True(DateTime.UtcNow < deadline, $"{job.Title} did not end within a minute ({job.State}).");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.Equal("12 verified, all match, 12 signatures over checksum files: 12 good", job.Summary);
            Assert.Equal((12, 12), (job.VerificationReport!.Total, job.VerificationReport.Authentic));
            Assert.Equal(12, Calls);
        }
        finally
        {
            VerificationService.Current = previous;
        }
    }

    [Fact]
    public async Task A_file_gpg_reads_for_its_signature_counts_as_read()
    {
        // gpg reads the file itself and says nothing meanwhile: once it has an answer, the results count the file as read.
        string image = Write("image.iso", new string('x', 70_000));
        Write("image.iso.asc", "-----BEGIN PGP SIGNATURE-----\nstub\n-----END PGP SIGNATURE-----\n");
        OpenPgp.UseTool(StubGpg());
        var previous = VerificationService.Current;
        VerificationService.Current = new VerificationService(new VerificationCache(null), () => 256L << 20, () => []) { Settle = TimeSpan.Zero };
        try
        {
            var job = await RunAsync(Jobs(), [image]);
            Assert.Equal((VerifiedOutcome.Good, true), (Assert.Single(job.VerificationReport!.Files).Outcome, job.VerificationReport.Files[0].Authentic));
            Assert.Equal(new FileInfo(image).Length, job.DataBytesProcessed);
        }
        finally
        {
            VerificationService.Current = previous;
        }
    }

    [Fact]
    public void Gpg_checks_a_signature_over_the_bytes_it_is_given_not_a_file()
    {
        if (OpenPgp.Tool is not { } gpg)
        {
            Assert.Skip("GnuPG is not installed here.");
            return;
        }
        string home = Directory.CreateDirectory(Path.Combine(_root, "home")).FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(home, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string gpgconf = Path.Combine(Path.GetDirectoryName(gpg)!, OperatingSystem.IsWindows() ? "gpgconf.exe" : "gpgconf");
        if (File.Exists(gpgconf)) _gpgconf = gpgconf;
        string[] options = ["--homedir", home, "--batch", "--pinentry-mode", "loopback", "--passphrase", ""];
        if (Run(gpg, [.. options, "--quick-gen-key", "FileCat Test <test@example.invalid>", "ed25519", "sign", "never"], out string made) != 0)
        {
            Assert.Skip("gpg could not make a key here (its agent): " + made);
            return;
        }
        // A list with Windows line ends, and one larger than any pipe's buffer: both reach gpg byte for byte.
        byte[] small = Encoding.UTF8.GetBytes($"{Sha256("disc")}  disc.iso\r\n{Sha256("tool")}  tool.exe\r\n");
        byte[] large = new byte[8 << 20];
        new Random(57).NextBytes(large);
        string Sign(string name, byte[] content)
        {
            string path = Path.Combine(_root, name);
            File.WriteAllBytes(path, content);
            if (Run(gpg, [.. options, "--armor", "--detach-sign", "--output", path + ".asc", path], out string output) != 0) throw new InvalidOperationException(output);
            // What is checked is the bytes given, never the file: it goes before the check.
            File.Delete(path);
            return path + ".asc";
        }
        string smallSignature = Sign("SHA256SUMS", small), largeSignature = Sign("big.bin", large);
        var ct = TestContext.Current.CancellationToken;
        OpenPgp.UseTool(gpg);

        var good = OpenPgp.Verify(smallSignature, small, ct, home);
        Assert.Equal(VerificationState.SignatureGood, good.State);
        Assert.Contains("ultimately trusted", good.Text, StringComparison.Ordinal);
        Assert.Equal(VerificationState.SignatureGood, OpenPgp.Verify(largeSignature, large, ct, home).State);
        // A byte changed (here a line end): bad.
        byte[] changed = Encoding.UTF8.GetBytes($"{Sha256("disc")}  disc.iso\n{Sha256("tool")}  tool.exe\r\n");
        Assert.Equal(VerificationState.SignatureBad, OpenPgp.Verify(smallSignature, changed, ct, home).State);
        // Not a signature at all: gpg stops before it reads its input, and the check ends at once without a verdict for it.
        string junk = Path.Combine(_root, "junk.asc");
        File.WriteAllText(junk, "not a signature\n");
        var clock = Stopwatch.StartNew();
        var none = OpenPgp.Verify(junk, large, ct, home);
        Assert.NotEqual(VerificationState.SignatureGood, none.State);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"The check took {clock.Elapsed}.");
    }
}
