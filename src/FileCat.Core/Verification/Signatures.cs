using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace FileCat.Core.Verification;

/// <summary>
/// minisign signatures (D-57), checked natively: Ed25519 over the file ("Ed", the legacy form) or over its BLAKE2b-512
/// hash ("ED", the default since minisign 0.8), and the global signature over the signature and its trusted comment.
/// The public key is found by its key ID among the <c>.pub</c> files in FileCat's keys folder (trusted: the user put
/// them there) and beside the file (not trusted: a key from the same place as the file shows only that whoever
/// published the file signed it, so a good signature by it is "unsure", never a tick).
/// </summary>
public static class Minisign
{
    /// <summary>The largest file a legacy ("Ed") signature is checked over: it signs the whole file, read into memory.</summary>
    public const long MaxLegacyBytes = 256L << 20;

    public sealed record PublicKey(byte[] KeyId, byte[] Key, string Source)
    {
        public string Id => BinaryPrimitives.ReadUInt64LittleEndian(KeyId).ToString("X16");

        /// <summary>From a folder of keys the user trusts (FileCat's keys folder), not from beside the file.</summary>
        public bool Trusted { get; init; }
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
    public static List<PublicKey> KeysIn(IEnumerable<string> folders, bool trusted = false)
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
                    if (ReadBoundedText(file) is { } text && ParsePublicKey(text, file) is { } key)
                        keys.Add(key with { Trusted = trusted });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return keys;
    }

    /// <param name="trustedFolder">Where trusted keys go (FileCat's keys folder), named in the advice a result gives.</param>
    public static SignatureResult Verify(string file, string signaturePath, IReadOnlyList<PublicKey> keys, CancellationToken ct, Action<long>? progress = null,
        string? trustedFolder = null) =>
        Verify(signaturePath, keys, trustedFolder, prehashed =>
        {
            if (prehashed) return Blake2b(file, ct, progress);
            byte[]? legacy = ReadLegacyMessage(file, ct);
            if (legacy is not null) progress?.Invoke(legacy.Length);
            return legacy;
        });

    /// <summary>
    /// As <see cref="Verify(string, string, IReadOnlyList{PublicKey}, CancellationToken, Action{long}?, string?)"/>, over
    /// <paramref name="content"/> in memory (the bytes a checksum file's lines were read from), never over a file read again.
    /// </summary>
    internal static SignatureResult Verify(ReadOnlyMemory<byte> content, string signaturePath, IReadOnlyList<PublicKey> keys, CancellationToken ct,
        string? trustedFolder = null) =>
        Verify(signaturePath, keys, trustedFolder, prehashed =>
        {
            ct.ThrowIfCancellationRequested();
            if (prehashed) return Blake2b(content.Span);
            return content.Length > MaxLegacyBytes ? null : content.ToArray();
        });

    /// <param name="message">What the signature signs: the file's BLAKE2b-512 hash (prehashed) or the whole file; null when it is too large.</param>
    private static SignatureResult Verify(string signaturePath, IReadOnlyList<PublicKey> keys, string? trustedFolder, Func<bool, byte[]?> message)
    {
        string keysFolder = trustedFolder ?? "FileCat's keys folder";
        Signature? signature;
        try { signature = ReadBoundedText(signaturePath) is { } text ? ParseSignature(text) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new SignatureResult(VerificationState.SignatureUnchecked, "it could not be read: " + ex.Message); }
        if (signature is null) return new SignatureResult(VerificationState.SignatureBad, "it is not a minisign signature");
        // A key the user trusts wins over the same key found beside the file.
        var key = keys.Where(k => k.KeyId.AsSpan().SequenceEqual(signature.KeyId)).OrderByDescending(k => k.Trusted).FirstOrDefault();
        if (key is null)
            return new SignatureResult(VerificationState.SignatureUnknownKey,
                $"made with minisign key {signature.Id}, which is not among your trusted keys: once you have the publisher's .pub from a place you trust (their website, say), copy it into {keysFolder}");
        byte[] signed;
        try
        {
            if (message(signature.Prehashed) is not { } read)
                return new SignatureResult(VerificationState.SignatureUnchecked, $"its legacy form signs the whole file, which is larger than {MaxLegacyBytes >> 20} MiB");
            signed = read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SignatureResult(VerificationState.Unreadable, "the file could not be read: " + ex.Message);
        }
        if (!Ed25519.Verify(signature.Value, 0, key.Key, 0, signed, 0, signed.Length))
            return new SignatureResult(VerificationState.SignatureBad, $"the signature does not match the file (key {key.Id})");
        byte[] comment = Encoding.UTF8.GetBytes(signature.TrustedComment);
        byte[] global = [.. signature.Value, .. comment];
        if (!Ed25519.Verify(signature.Global, 0, key.Key, 0, global, 0, global.Length))
            return new SignatureResult(VerificationState.SignatureBad, "the file matches, but its trusted comment was changed after signing");
        string signer = Path.GetFileNameWithoutExtension(key.Source);
        if (!key.Trusted)
            return new SignatureResult(VerificationState.SignatureUnknownKey,
                $"good minisign signature, but by key {key.Id} from {Path.GetFileName(key.Source)} beside the file, which is not among your trusted keys: " +
                $"a key from the same place as the file shows only that whoever published it signed it. Once you know the key is the publisher's " +
                $"(from their website, say), copy it into {keysFolder}; trusted comment: {signature.TrustedComment}",
                Short: "? signed by a key found beside it");
        return new SignatureResult(VerificationState.SignatureGood,
            $"good minisign signature, key {key.Id} from {Path.GetFileName(key.Source)} (a trusted key); trusted comment: {signature.TrustedComment}", signer);
    }

    private static string? ReadBoundedText(string path)
    {
        using var stream = VerificationFileReader.OpenRead(path, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 4096) return null;
        byte[] bytes = new byte[4097];
        int length = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (length > 4096) return null;
        using var reader = new StreamReader(new MemoryStream(bytes, 0, length, writable: false), Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static byte[]? ReadLegacyMessage(string path, CancellationToken ct)
    {
        using var stream = VerificationFileReader.OpenRead(path, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxLegacyBytes) return null;
        byte[] bytes = new byte[checked((int)stream.Length)];
        int read = 0;
        while (read < bytes.Length)
        {
            ct.ThrowIfCancellationRequested();
            int count = stream.Read(bytes, read, bytes.Length - read);
            if (count == 0) throw new EndOfStreamException("The legacy signed file changed while it was read.");
            read += count;
        }
        ct.ThrowIfCancellationRequested();
        return stream.ReadByte() == -1 ? bytes : null;
    }

    /// <summary>BLAKE2b-512 of a file, read in 1 MiB blocks.</summary>
    public static byte[] Blake2b(string file, CancellationToken ct, Action<long>? progress = null)
    {
        var digest = new Blake2bDigest(512);
        using var stream = VerificationFileReader.OpenSequential(file);
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

    private static byte[] Blake2b(ReadOnlySpan<byte> content)
    {
        var digest = new Blake2bDigest(512);
        digest.BlockUpdate(content);
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
    private const int MaxStatusCharacters = 1 << 20;
    private static string? _tool;
    private static bool _looked;

    /// <summary>
    /// The GnuPG installed at a conventional OS location. Automatic signature checks must not execute a program from
    /// an arbitrary PATH entry, even when that entry is absolute: it may be writable by a downloaded folder's owner.
    /// </summary>
    public static string? Tool
    {
        get
        {
            if (_looked) return _tool;
            _looked = true;
            var installed = new List<string>();
            if (OperatingSystem.IsWindows())
            {
                foreach (string? root in new[]
                         {
                             Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                             Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         })
                    if (!string.IsNullOrEmpty(root)) installed.Add(Path.Combine(root, "GnuPG", "bin", "gpg.exe"));
            }
            else if (OperatingSystem.IsMacOS())
                installed.AddRange(["/opt/homebrew/bin/gpg", "/usr/local/bin/gpg", "/usr/local/MacGPG2/bin/gpg", "/usr/bin/gpg", "/usr/bin/gpg2"]);
            else installed.AddRange(["/usr/bin/gpg", "/usr/bin/gpg2", "/bin/gpg", "/bin/gpg2"]);
            _tool = SelectAutomaticTool(Environment.GetEnvironmentVariable("PATH"), installed);
            return _tool;
        }
    }

    /// <summary>Automatic checks deliberately disregard PATH; installed candidates are OS-specific full paths.</summary>
    internal static string? SelectAutomaticTool(string? environmentPath, IEnumerable<string> installed)
    {
        _ = environmentPath;
        return FindTool(null, installed);
    }

    /// <summary>
    /// Explicit gpg lookup by full path: from the absolute entries of <paramref name="path"/> (PATH), then where it is installed. A
    /// relative entry ("." and the like) would follow FileCat's current directory, where a gpg could have been planted
    /// (release plan I16); as with Git, such entries are not searched.
    /// </summary>
    internal static string? FindTool(string? path, IEnumerable<string> installed)
    {
        string exe = OperatingSystem.IsWindows() ? "gpg.exe" : "gpg";
        var dirs = (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Trim('"')).Where(Path.IsPathFullyQualified).ToList();
        var candidates = dirs.Select(d => Path.Combine(d, exe)).ToList();
        if (!OperatingSystem.IsWindows()) candidates.AddRange(dirs.Select(d => Path.Combine(d, "gpg2")));
        candidates.AddRange(installed.Where(Path.IsPathFullyQualified));
        // Git for Windows' own gpg keeps a keyring of its own, not the user's: left out.
        return candidates.FirstOrDefault(c => !c.Contains(Path.Combine("Git", "usr"), StringComparison.OrdinalIgnoreCase) && File.Exists(c));
    }

    /// <summary>GnuPG's folder: GNUPGHOME, or gpg's default home. gpg may write there while it checks (its trust database, locks).</summary>
    public static string Home() =>
        Environment.GetEnvironmentVariable("GNUPGHOME") is { Length: > 0 } set ? set
        : OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "gnupg")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gnupg");

    /// <summary>
    /// The user's keyring, trust database and gpg.conf (GNUPGHOME, or gpg's default home), which a result depends on: the
    /// configuration chooses the trust model, which decides whether gpg vouches for a key at all.
    /// </summary>
    public static IEnumerable<FileInfo> KeyringFiles()
    {
        string home = Home();
        foreach (string name in new[] { "pubring.kbx", "pubring.gpg", "trustdb.gpg", "gpg.conf" })
        {
            var file = new FileInfo(Path.Combine(home, name));
            if (file.Exists) yield return file;
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
        if (Tool is null) return NotInstalled;
        ct.ThrowIfCancellationRequested();
        // gpg would wait on a pipe planted under the signed file's name until its minute runs out.
        if (VerificationFileReader.IsSpecial(signed)) return new SignatureResult(VerificationState.Unreadable, "the file could not be read: it is not a regular file (a pipe or a device)");
        return Run(signature, signed, null, ct, home);
    }

    /// <summary>
    /// Checks a signature over <paramref name="content"/> in memory (the bytes a checksum file's lines were read from), which
    /// gpg reads from its standard input: never a file read again, and no copy of it written anywhere.
    /// </summary>
    internal static SignatureResult Verify(string signature, ReadOnlyMemory<byte> content, CancellationToken ct, string? home = null)
    {
        if (Tool is null) return NotInstalled;
        ct.ThrowIfCancellationRequested();
        return Run(signature, "-", content, ct, home);
    }

    private static SignatureResult NotInstalled => new(VerificationState.SignatureUnchecked, "OpenPGP signatures are checked with GnuPG, which is not installed here");

    /// <param name="signed">The signed file, or "-" for <paramref name="content"/> on gpg's standard input.</param>
    private static SignatureResult Run(string signature, string signed, ReadOnlyMemory<byte>? content, CancellationToken ct, string? home)
    {
        if (Tool is not { } gpg) return NotInstalled;
        if (VerificationFileReader.IsSpecial(signature)) return new SignatureResult(VerificationState.SignatureUnchecked, "it could not be read: it is not a regular file (a pipe or a device)");
        var start = new ProcessStartInfo(gpg)
        {
            RedirectStandardInput = content is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (home is not null) start.ArgumentList.Add("--homedir=" + home);
        // Never a key server behind the user's back (gpg.conf may ask for it), and the files after "--", never read as options.
        foreach (string arg in new[] { "--batch", "--no-tty", "--no-auto-key-retrieve", "--status-fd", "1", "--verify", "--", signature, signed }) start.ArgumentList.Add(arg);
        string status;
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("gpg did not start");
            // The process may exit while a descendant still holds a redirected pipe. Bound exit and
            // both drains together; stderr is diagnostic text and must not grow an unused string.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromMinutes(1));
            var output = ReadStatusAsync(process.StandardOutput, deadline.Token);
            var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, deadline.Token);
            var exited = process.WaitForExitAsync(deadline.Token);
            var pending = new List<Task> { exited, output, errors };
            if (content is { } data) pending.Add(FeedAsync(process, data, deadline.Token));
            try
            {
                // A full status budget must fail promptly even if the helper then blocks on its pipe.
                while (pending.Count != 0)
                {
                    Task completed = Task.WhenAny(pending).GetAwaiter().GetResult();
                    completed.GetAwaiter().GetResult();
                    pending.Remove(completed);
                }
                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                return new SignatureResult(VerificationState.SignatureUnchecked, "gpg did not answer within a minute");
            }
            finally
            {
                deadline.Cancel();
                if (!process.HasExited)
                    try { process.Kill(entireProcessTree: true); }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            status = output.GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new SignatureResult(VerificationState.SignatureUnchecked, "gpg could not be run: " + ex.Message);
        }
        return Interpret(status);
    }

    /// <summary>Writes the signed bytes to gpg and closes its input; gpg may stop reading early (a signature it cannot use), which is its answer.</summary>
    private static async Task FeedAsync(Process process, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        try
        {
            var input = process.StandardInput.BaseStream;
            await input.WriteAsync(content, ct).ConfigureAwait(false);
            await input.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { }
        finally
        {
            try { process.StandardInput.Close(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        }
    }

    private static async Task<string> ReadStatusAsync(StreamReader reader, CancellationToken ct)
    {
        var status = new StringBuilder();
        char[] buffer = new char[4096];
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) return status.ToString();
            if (read > MaxStatusCharacters - status.Length)
                throw new IOException("gpg status output exceeded 1 MiB");
            status.Append(buffer, 0, read);
        }
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
            // No trust line at all: gpg did not judge the key (with trust-model "always" in gpg.conf it never does), so the
            // signature proves the key signed the file, not that the key is the publisher's: never a shield (V15, I95).
            string text = $"good OpenPGP signature by {who}" + (fingerprint is not null ? $" (key {fingerprint})" : "") +
                          (expired is not null ? "; the key has expired since" : "") +
                          (trust is not null ? $"; the key is {trust}" : "; gpg did not say whether the key is valid (gpg.conf may set trust-model always)");
            bool trusted = trust is "ultimately trusted" or "fully trusted";
            return new SignatureResult(trusted ? VerificationState.SignatureGood : VerificationState.SignatureUnknownKey, text,
                trusted ? who : null, trusted ? null : trust is null ? "? signed by a key gpg did not vouch for" : "? signed by a key you have not certified");
        }
        if (missing is not null) return new SignatureResult(VerificationState.SignatureUnknownKey, $"made with OpenPGP key {missing}, which is not in your keyring");
        return new SignatureResult(VerificationState.SignatureUnchecked, "gpg gave no answer about it");
    }
}
