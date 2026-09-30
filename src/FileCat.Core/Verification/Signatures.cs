using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace FileCat.Core.Verification;

/// <summary>
/// minisign signatures (D-57), checked natively: Ed25519 over the file ("Ed", the legacy form) or over its BLAKE2b-512
/// hash ("ED", the default since minisign 0.8), and the global signature over the signature and its trusted comment.
/// The public key is found by its key ID among the <c>.pub</c> files beside the file and in FileCat's key folders.
/// </summary>
public static class Minisign
{
    /// <summary>The largest file a legacy ("Ed") signature is checked over: it signs the whole file, read into memory.</summary>
    public const long MaxLegacyBytes = 256L << 20;

    public sealed record PublicKey(byte[] KeyId, byte[] Key, string Source)
    {
        public string Id => BinaryPrimitives.ReadUInt64LittleEndian(KeyId).ToString("X16");
    }

    public sealed record Signature(bool Prehashed, byte[] KeyId, byte[] Value, string TrustedComment, byte[] Global)
    {
        public string Id => BinaryPrimitives.ReadUInt64LittleEndian(KeyId).ToString("X16");
    }

    /// <summary>A .minisig file: an untrusted comment, the signature, the trusted comment, and the global signature.</summary>
    public static Signature? ParseSignature(string text)
    {
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 4 || !lines[2].StartsWith("trusted comment: ", StringComparison.Ordinal)) return null;
        var sig = Base64(lines[1]);
        var global = Base64(lines[3]);
        if (sig is not { Length: 74 } || global is not { Length: 64 } || sig[0] != 'E' || sig[1] is not ((byte)'d' or (byte)'D')) return null;
        return new Signature(sig[1] == 'D', sig[2..10], sig[10..], lines[2]["trusted comment: ".Length..], global);
    }

    /// <summary>A minisign public key: its file (an untrusted comment, then the key) or the key's line alone.</summary>
    public static PublicKey? ParsePublicKey(string text, string source)
    {
        foreach (string line in text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("untrusted comment:", StringComparison.Ordinal)) continue;
            var key = Base64(line.Trim());
            return key is { Length: 42 } && key[0] == 'E' && key[1] == 'd' ? new PublicKey(key[2..10], key[10..], source) : null;
        }
        return null;
    }

    /// <summary>The minisign keys among the <c>.pub</c> files of these folders (other kinds of .pub, such as SSH keys, are left out).</summary>
    public static List<PublicKey> KeysIn(IEnumerable<string> folders)
    {
        var keys = new List<PublicKey>();
        foreach (string folder in folders.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder, "*.pub").ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (string file in files)
            {
                try
                {
                    if (new FileInfo(file).Length <= 4096 && ParsePublicKey(File.ReadAllText(file), file) is { } key) keys.Add(key);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return keys;
    }

    public static SignatureResult Verify(string file, string signaturePath, IReadOnlyList<PublicKey> keys, CancellationToken ct, Action<long>? progress = null)
    {
        Signature? signature;
        try { signature = new FileInfo(signaturePath).Length <= 4096 ? ParseSignature(File.ReadAllText(signaturePath)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new SignatureResult(VerificationState.SignatureUnchecked, "it could not be read: " + ex.Message); }
        if (signature is null) return new SignatureResult(VerificationState.SignatureBad, "it is not a minisign signature");
        var key = keys.FirstOrDefault(k => k.KeyId.AsSpan().SequenceEqual(signature.KeyId));
        if (key is null)
            return new SignatureResult(VerificationState.SignatureUnknownKey,
                $"made with minisign key {signature.Id}, whose public key is not here: put its .pub beside the file or in FileCat's key folder");
        byte[] message;
        try
        {
            if (signature.Prehashed) message = Blake2b(file, ct, progress);
            else
            {
                if (new FileInfo(file).Length > MaxLegacyBytes)
                    return new SignatureResult(VerificationState.SignatureUnchecked, $"its legacy form signs the whole file, which is larger than {MaxLegacyBytes >> 20} MiB");
                message = File.ReadAllBytes(file);
                progress?.Invoke(message.Length);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SignatureResult(VerificationState.Unreadable, "the file could not be read: " + ex.Message);
        }
        if (!Ed25519.Verify(signature.Value, 0, key.Key, 0, message, 0, message.Length))
            return new SignatureResult(VerificationState.SignatureBad, $"the signature does not match the file (key {key.Id})");
        byte[] comment = Encoding.UTF8.GetBytes(signature.TrustedComment);
        byte[] signed = [.. signature.Value, .. comment];
        if (!Ed25519.Verify(signature.Global, 0, key.Key, 0, signed, 0, signed.Length))
            return new SignatureResult(VerificationState.SignatureBad, "the file matches, but its trusted comment was changed after signing");
        string signer = Path.GetFileNameWithoutExtension(key.Source);
        return new SignatureResult(VerificationState.SignatureGood,
            $"good minisign signature, key {key.Id} from {Path.GetFileName(key.Source)}; trusted comment: {signature.TrustedComment}", signer);
    }

    /// <summary>BLAKE2b-512 of a file, read in 1 MiB blocks.</summary>
    public static byte[] Blake2b(string file, CancellationToken ct, Action<long>? progress = null)
    {
        var digest = new Blake2bDigest(512);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        var buffer = new byte[Verifier.BlockSize];
        int n;
        while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            digest.BlockUpdate(buffer, 0, n);
            progress?.Invoke(n);
        }
        var hash = new byte[64];
        digest.DoFinal(hash, 0);
        return hash;
    }

    private static byte[]? Base64(string text)
    {
        try { return Convert.FromBase64String(text.Trim()); }
        catch (FormatException) { return null; }
    }
}

/// <summary>
/// OpenPGP signatures (D-57), checked by the system's GnuPG against the user's own keyring and trust: FileCat reads
/// gpg's machine-readable status lines, never its human text. Without GnuPG the signature is listed as not checked.
/// </summary>
public static class OpenPgp
{
    private static string? _tool;
    private static bool _looked;

    /// <summary>gpg as the user runs it: on the PATH, or where Gpg4win and GnuPG install it on Windows.</summary>
    public static string? Tool
    {
        get
        {
            if (_looked) return _tool;
            _looked = true;
            string exe = OperatingSystem.IsWindows() ? "gpg.exe" : "gpg";
            var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(d => Path.Combine(d, exe)).ToList();
            if (OperatingSystem.IsWindows())
            {
                foreach (string? root in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles") })
                    if (!string.IsNullOrEmpty(root)) candidates.Add(Path.Combine(root, "GnuPG", "bin", exe));
            }
            else candidates.AddRange(["/opt/homebrew/bin/gpg", "/usr/local/bin/gpg", "/usr/bin/gpg"]);
            // Git for Windows' own gpg keeps a keyring of its own, not the user's: left out.
            _tool = candidates.FirstOrDefault(c => !c.Contains(Path.Combine("Git", "usr"), StringComparison.OrdinalIgnoreCase) && File.Exists(c));
            return _tool;
        }
    }

    /// <summary>Tests: which gpg to use (null: look again).</summary>
    internal static void UseTool(string? tool)
    {
        _tool = tool;
        _looked = tool is not null;
    }

    public static SignatureResult Verify(string signature, string signed, CancellationToken ct, string? home = null)
    {
        if (Tool is not { } gpg) return new SignatureResult(VerificationState.SignatureUnchecked, "OpenPGP signatures are checked with GnuPG, which is not installed here");
        var start = new ProcessStartInfo(gpg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (home is not null) start.ArgumentList.Add("--homedir=" + home);
        foreach (string arg in new[] { "--batch", "--no-tty", "--status-fd", "1", "--verify", signature, signed }) start.ArgumentList.Add(arg);
        string status;
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("gpg did not start");
            var output = process.StandardOutput.ReadToEndAsync(ct);
            _ = process.StandardError.ReadToEndAsync(ct);
            if (!process.WaitForExit(TimeSpan.FromSeconds(60)) || ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                ct.ThrowIfCancellationRequested();
                return new SignatureResult(VerificationState.SignatureUnchecked, "gpg did not answer within a minute");
            }
            status = output.GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new SignatureResult(VerificationState.SignatureUnchecked, "gpg could not be run: " + ex.Message);
        }
        return Interpret(status);
    }

    /// <summary>gpg's status lines ("[GNUPG:] GOODSIG …", "TRUST_FULLY", "NO_PUBKEY …") as a result.</summary>
    public static SignatureResult Interpret(string status)
    {
        string? good = null, bad = null, missing = null, expired = null, revoked = null, fingerprint = null, trust = null;
        foreach (string raw in status.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (!line.StartsWith("[GNUPG:] ", StringComparison.Ordinal)) continue;
            var parts = line["[GNUPG:] ".Length..].Split(' ', 3);
            string rest = parts.Length > 2 ? parts[2] : "";
            switch (parts[0])
            {
                case "GOODSIG": good = rest; break;
                case "BADSIG": bad = rest.Length > 0 ? rest : parts.ElementAtOrDefault(1) ?? ""; break;
                case "EXPKEYSIG": expired = rest; break;
                case "REVKEYSIG": revoked = rest; break;
                case "NO_PUBKEY": missing = parts.ElementAtOrDefault(1); break;
                case "ERRSIG": missing ??= parts.ElementAtOrDefault(1); break;
                case "VALIDSIG": fingerprint = parts.ElementAtOrDefault(1); break;
                case "TRUST_ULTIMATE": trust = "ultimately trusted"; break;
                case "TRUST_FULLY": trust = "fully trusted"; break;
                case "TRUST_MARGINAL": trust = "marginally trusted"; break;
                case "TRUST_UNDEFINED": trust = "not certified as trusted"; break;
                case "TRUST_NEVER": trust = "marked never to be trusted"; break;
            }
        }
        if (bad is not null) return new SignatureResult(VerificationState.SignatureBad, $"BAD OpenPGP signature by {bad}: the file or the signature was changed");
        if (revoked is not null) return new SignatureResult(VerificationState.SignatureBad, $"signed by {revoked}, whose key is revoked");
        if (good is not null || expired is not null)
        {
            string who = good ?? expired!;
            string text = $"good OpenPGP signature by {who}" + (fingerprint is not null ? $" (key {fingerprint})" : "") +
                          (expired is not null ? "; the key has expired since" : "") + (trust is not null ? $"; the key is {trust}" : "");
            bool trusted = trust is "ultimately trusted" or "fully trusted";
            return new SignatureResult(trusted || trust is null ? VerificationState.SignatureGood : VerificationState.SignatureUnknownKey, text,
                trusted || trust is null ? who : null);
        }
        if (missing is not null) return new SignatureResult(VerificationState.SignatureUnknownKey, $"made with OpenPGP key {missing}, which is not in your keyring");
        return new SignatureResult(VerificationState.SignatureUnchecked, "gpg gave no answer about it");
    }
}
