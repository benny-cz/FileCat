using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

// This fixture selects Git's MSYS GnuPG through a process-wide test override. Native-GnuPG fixtures use
// Windows paths/keyrings, so the override must not overlap any other collection's verification calls.
[CollectionDefinition(nameof(GnuPgToolOverride), DisableParallelization = true)]
public sealed class GnuPgToolOverride;

/// <summary>
/// V15 with an independent GnuPG: keys made and files signed by gpg itself, then judged by FileCat's reading of gpg's
/// status lines. A key gpg vouches for is good; an imported key nobody certified, a missing key, and a key under
/// trust-model always (gpg vouches for nothing, release issue I95) never read as good; a changed file is bad; and no key
/// server is asked, even when gpg.conf says to, as a listener in the key server's place witnesses.
/// </summary>
[Collection(nameof(GnuPgToolOverride))]
public sealed class GnuPgVerificationTests : IDisposable
{
    // Short: gpg-agent's socket lives in the home folder, and its path has a length limit.
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fcg" + Guid.NewGuid().ToString("N")[..6]);
    private string? _gpg;

    public void Dispose()
    {
        if (_gpg is not null && Path.GetDirectoryName(_gpg) is { } bin)
        {
            string gpgconf = Path.Combine(bin, OperatingSystem.IsWindows() ? "gpgconf.exe" : "gpgconf");
            foreach (var home in new[] { "A", "B", "C" })
                if (Directory.Exists(Path.Combine(_root, home)) && File.Exists(gpgconf))
                    Run(gpgconf, ["--homedir", For(Path.Combine(_root, home)), "--kill", "all"], out _);
        }
        OpenPgp.UseTool(null);
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string? FindGpg()
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? [@"C:\Program Files\Git\usr\bin\gpg.exe"]
            : ["/usr/bin/gpg", "/usr/local/bin/gpg", "/opt/homebrew/bin/gpg"];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>A path as the gpg used here reads it: Git for Windows' gpg is an MSYS program and wants /c/... paths.</summary>
    private static string For(string path) =>
        OperatingSystem.IsWindows() && path.Length > 2 && path[1] == ':'
            ? "/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/')
            : path;

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

    private void Gpg(string home, params string[] args)
    {
        int code = Run(_gpg!, ["--homedir", For(Path.Combine(_root, home)), "--batch", "--pinentry-mode", "loopback", "--passphrase", "", .. args], out var output);
        if (code != 0) throw new InvalidOperationException($"gpg {string.Join(' ', args)}: {output}");
    }

    [Fact]
    public void Only_a_key_gpg_vouches_for_makes_a_good_signature_and_no_key_server_is_asked()
    {
        _gpg = FindGpg();
        if (_gpg is null)
        {
            Assert.Skip("No GnuPG to make the keys and signatures with.");
            return;
        }
        foreach (var home in new[] { "A", "B", "C" })
        {
            var dir = Directory.CreateDirectory(Path.Combine(_root, home));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(dir.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        string file = Path.Combine(_root, "release.tar"), sig = file + ".sig";
        File.WriteAllText(file, "the release");
        try
        {
            Gpg("A", "--quick-gen-key", "FileCat Test <test@example.invalid>", "ed25519", "sign", "never");
        }
        catch (InvalidOperationException ex)
        {
            Assert.Skip("gpg could not make a key here (its agent): " + ex.Message);
            return;
        }
        Gpg("A", "--detach-sign", "--output", For(sig), For(file));
        string pub = Path.Combine(_root, "pub.asc");
        Gpg("A", "--export", "--armor", "--output", For(pub));
        Gpg("B", "--import", For(pub));
        OpenPgp.UseTool(_gpg);
        var ct = TestContext.Current.CancellationToken;
        SignatureResult Check(string home, string signed) => OpenPgp.Verify(For(sig), For(signed), ct, For(Path.Combine(_root, home)));

        // The signer's own key: ultimately trusted by gpg.
        var own = Check("A", file);
        Assert.Equal(VerificationState.SignatureGood, own.State);
        Assert.Contains("ultimately trusted", own.Text, StringComparison.Ordinal);
        // The same key imported, certified by nobody: the file was signed by it, not shown to be the publisher's.
        Assert.Equal(VerificationState.SignatureUnknownKey, Check("B", file).State);
        // trust-model always: gpg says nothing about the key's validity (I95).
        File.WriteAllText(Path.Combine(_root, "B", "gpg.conf"), "trust-model always\n");
        var always = Check("B", file);
        Assert.Equal(VerificationState.SignatureUnknownKey, always.State);
        Assert.Null(always.Signer);
        File.Delete(Path.Combine(_root, "B", "gpg.conf"));
        // A changed file: bad.
        string changed = Path.Combine(_root, "changed.tar");
        File.WriteAllText(changed, "the release, changed");
        Assert.Equal(VerificationState.SignatureBad, Check("A", changed).State);

        // No key at all, and gpg.conf asking to fetch missing keys from a key server: here, a listener on this machine.
        using var keyserver = new TcpListener(IPAddress.Loopback, 0);
        keyserver.Start();
        int port = ((IPEndPoint)keyserver.LocalEndpoint).Port;
        File.WriteAllText(Path.Combine(_root, "C", "gpg.conf"), $"auto-key-retrieve\nkeyserver hkp://127.0.0.1:{port}\n");
        var missing = Check("C", file);
        Assert.Equal(VerificationState.SignatureUnknownKey, missing.State);
        Assert.Contains("not in your keyring", missing.Text, StringComparison.Ordinal);
        Thread.Sleep(500);
        Assert.False(keyserver.Pending(), "gpg asked the key server for the missing key");
    }
}
