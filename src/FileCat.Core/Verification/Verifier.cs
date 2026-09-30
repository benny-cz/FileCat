using System.Security.Cryptography;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;

namespace FileCat.Core.Verification;

/// <summary>
/// A file's verification state, worst first: a mismatch or a bad signature outranks everything else, and among good
/// results a trusted signature (authentic: its publisher made it) outranks a plain match (intact: it arrived whole).
/// </summary>
public enum VerificationState
{
    Differs,
    SignatureBad,
    Unreadable,
    SignatureUnknownKey,
    SignatureUnchecked,
    NotChecked,
    SignatureGood,
    Matches,
    /// <summary>A checksum file's or signature's own row, which nothing else covers: what it covers.</summary>
    Sidecar,
}

/// <summary>
/// What checking a file against its checksums and signatures found (D-57): a state, a short text for its row, and the
/// details for its tooltip (each claim, its source, and what an MD5, SHA-1, or CRC-32 match does and does not show).
/// </summary>
public sealed record VerificationResult(VerificationState State, string Text, IReadOnlyList<string> Details)
{
    public bool IsGood => State is VerificationState.Matches or VerificationState.SignatureGood;
    public bool IsBad => State is VerificationState.Differs or VerificationState.SignatureBad;
}

/// <summary>Hashes files for every algorithm their sidecars name in one pass, and puts the answers together.</summary>
public static class Verifier
{
    public const int BlockSize = 1 << 20;

    /// <summary>Each algorithm's value for the file, read once in 1 MiB blocks; <paramref name="progress"/> gets the bytes of each block.</summary>
    public static Dictionary<ChecksumKind, string> Hash(string path, IReadOnlyCollection<ChecksumKind> kinds, CancellationToken ct, Action<long>? progress = null)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        var hashes = kinds.Where(k => k != ChecksumKind.Crc32).Distinct().ToDictionary(k => k, k => IncrementalHash.CreateHash(k switch
        {
            ChecksumKind.Md5 => HashAlgorithmName.MD5,
            ChecksumKind.Sha1 => HashAlgorithmName.SHA1,
            ChecksumKind.Sha384 => HashAlgorithmName.SHA384,
            ChecksumKind.Sha512 => HashAlgorithmName.SHA512,
            _ => HashAlgorithmName.SHA256,
        }));
        bool crc = kinds.Contains(ChecksumKind.Crc32);
        uint crcValue = 0;
        try
        {
            var buffer = new byte[BlockSize];
            int n;
            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var hash in hashes.Values) hash.AppendData(buffer, 0, n);
                if (crc) crcValue = Crc32.Append(crcValue, buffer.AsSpan(0, n));
                progress?.Invoke(n);
            }
            var result = hashes.ToDictionary(p => p.Key, p => Convert.ToHexStringLower(p.Value.GetHashAndReset()));
            if (crc) result[ChecksumKind.Crc32] = crcValue.ToString("x8");
            return result;
        }
        finally
        {
            foreach (var hash in hashes.Values) hash.Dispose();
        }
    }

    /// <summary>
    /// A file checked against its claims: its checksums compared (<paramref name="hash"/> gives the file's value for each
    /// algorithm, read or remembered), its signatures and those of the manifests that list it checked
    /// (<paramref name="checkSignature"/> checks one; <paramref name="manifestSignatures"/> finds a manifest's own).
    /// </summary>
    public static VerificationResult Check(IReadOnlyList<ChecksumClaim> checksums, IReadOnlyList<SignatureClaim> signatures,
        Func<IReadOnlyCollection<ChecksumKind>, IReadOnlyDictionary<ChecksumKind, string>> hash,
        Func<SignatureClaim, CancellationToken, SignatureResult> checkSignature, Func<string, IReadOnlyList<SignatureClaim>> manifestSignatures,
        CancellationToken ct)
    {
        var details = new List<string>();
        var states = new List<VerificationState>();
        var texts = new List<string>();
        if (checksums.Count > 0)
        {
            IReadOnlyDictionary<ChecksumKind, string> actual;
            try { actual = hash(checksums.Select(c => c.Kind).ToHashSet()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new VerificationResult(VerificationState.Unreadable, "cannot be read", [$"It could not be read: {ex.Message}"]);
            }
            var matched = new List<string>();
            var differed = new List<string>();
            // Strongest first: SHA-512, SHA-384, SHA-256, SHA-1, MD5, CRC-32.
            foreach (var claim in checksums.OrderBy(c => Strength(c.Kind)))
            {
                string name = Checksums.Name(claim.Kind);
                string where = $"{Path.GetFileName(claim.Source)}, line {claim.Line}";
                if (string.Equals(actual[claim.Kind], claim.Expected, StringComparison.OrdinalIgnoreCase))
                {
                    matched.Add(name);
                    details.Add($"{name} matches {where}.");
                }
                else
                {
                    differed.Add(name);
                    details.Add($"{name} differs: {where} says {claim.Expected}, the file's is {actual[claim.Kind]}.");
                }
            }
            if (differed.Count > 0)
            {
                states.Add(VerificationState.Differs);
                texts.Add($"✗ {string.Join(", ", differed.Distinct())} differs");
            }
            else
            {
                states.Add(VerificationState.Matches);
                var kinds = checksums.Select(c => c.Kind).Distinct().ToList();
                texts.Add($"✓ {string.Join(", ", matched.Distinct())}" + (kinds.All(Checksums.IsCompatibilityOnly) ? " (integrity only)" : ""));
            }
            if (checksums.Any(c => Checksums.IsCompatibilityOnly(c.Kind)))
                details.Add("MD5, SHA-1, and CRC-32 show that the file is intact, not that it is authentic: they can be forged.");
            // A signature over a manifest vouches for the files it lists.
            foreach (string manifest in checksums.Select(c => c.Source).Distinct())
                foreach (var signature in manifestSignatures(Path.GetFileName(manifest)))
                {
                    var r = checkSignature(signature, ct);
                    details.Add($"{Path.GetFileName(signature.Signature)} (over {Path.GetFileName(manifest)}): {r.Text}.");
                    states.Add(r.State);
                    if (r.State == VerificationState.SignatureGood && differed.Count == 0) texts.Add($"manifest signed by {r.Signer}");
                    else if (r.State != VerificationState.SignatureGood) texts.Add("manifest " + Short(r));
                }
            if (!details.Any(d => d.Contains(" (over ", StringComparison.Ordinal)) && differed.Count == 0)
                details.Add("A checksum beside its file shows that the file is intact; it proves nothing against tampering unless it is signed, or came from a place you trust.");
        }
        foreach (var signature in signatures)
        {
            var r = checkSignature(signature, ct);
            details.Add($"{Path.GetFileName(signature.Signature)}: {r.Text}.");
            states.Add(r.State);
            texts.Add(r.State == VerificationState.SignatureGood ? $"✓ signed by {r.Signer}" : Short(r));
        }
        if (states.Count == 0) return new VerificationResult(VerificationState.NotChecked, "", []);
        return new VerificationResult(states.Min(), string.Join(" · ", texts), details);
    }

    private static int Strength(ChecksumKind kind) => kind switch
    {
        ChecksumKind.Sha512 => 0,
        ChecksumKind.Sha384 => 1,
        ChecksumKind.Sha256 => 2,
        ChecksumKind.Sha1 => 3,
        ChecksumKind.Md5 => 4,
        _ => 5,
    };

    private static string Short(SignatureResult r) => r.Short ?? r.State switch
    {
        VerificationState.SignatureBad => "✗ bad signature",
        VerificationState.SignatureUnknownKey => "? signed by an unknown key",
        _ => "signature not checked",
    };
}

/// <summary>
/// What checking one signature found; <see cref="Signer"/> names who signed when the key is trusted, and
/// <see cref="Short"/>, when given, is the row's words for it (otherwise they follow from the state).
/// </summary>
public sealed record SignatureResult(VerificationState State, string Text, string? Signer = null, string? Short = null);
