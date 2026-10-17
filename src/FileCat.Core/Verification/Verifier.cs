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
    /// <summary>When the file was read for this result (kept results are shown without reading it again).</summary>
    public DateTime? CheckedUtc { get; init; }

    /// <summary>The file changed a moment ago and was not read: ask again once it has been still (it may be being written).</summary>
    public bool Settling { get; init; }

    /// <summary>Each checksum compared with the file's own value, the strongest first: from a check just made (a kept result has none).</summary>
    public IReadOnlyList<ChecksumComparison> Checksums { get; init; } = [];

    /// <summary>Each signature checked, over the file or over a checksum file that lists it (as <see cref="Checksums"/>).</summary>
    public IReadOnlyList<SignatureCheck> Signatures { get; init; } = [];

    /// <summary>What a match does not show (MD5 is forgeable; an unsigned checksum proves no origin), also among <see cref="Details"/>.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

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
        using var stream = VerificationFileReader.OpenSequential(path);
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
        // A bounded manifest can still repeat one file's claim thousands of times. Evaluate every claim,
        // but do not retain an unbounded tooltip/status in the result cache. Prefer adverse details when
        // the display limit is reached so a late mismatch remains identifiable by source and line.
        const int maxDetails = 64, maxTextParts = 16, maxMessageChars = 512;
        var details = new List<string>();
        var importantDetails = new List<bool>();
        var texts = new List<string>();
        var importantTexts = new List<bool>();
        // The same claims, kept whole for the results of a check asked for (the comparisons, the signatures).
        var comparisons = new List<ChecksumComparison>();
        var importantComparisons = new List<bool>();
        var signatureChecks = new List<SignatureCheck>();
        var importantSignatures = new List<bool>();
        var notes = new List<string>();
        long hiddenDetails = 0, hiddenTexts = 0, hiddenClaims = 0;
        VerificationState? state = null;
        bool hasManifestSignature = false, signedByTrustedKey = false;
        int differing = 0;
        static void Keep<T>(List<T> items, List<bool> priorities, ref long hidden, T item, bool important, int maximum)
        {
            if (items.Count < maximum - 1)
            {
                items.Add(item);
                priorities.Add(important);
                return;
            }
            if (important)
            {
                int replace = priorities.FindLastIndex(priority => !priority);
                if (replace >= 0)
                {
                    items.RemoveAt(replace);
                    priorities.RemoveAt(replace);
                    items.Add(item);
                    priorities.Add(true);
                }
            }
            hidden++;
        }
        static void Show(List<string> messages, List<bool> priorities, ref long hidden, string message, bool important, int maximum) =>
            Keep(messages, priorities, ref hidden, message.Length > maxMessageChars ? message[..maxMessageChars] + "…" : message, important, maximum);
        void Detail(string message, bool important = false) => Show(details, importantDetails, ref hiddenDetails, message, important, maxDetails);
        void Text(string message, bool important = false) => Show(texts, importantTexts, ref hiddenTexts, message, important, maxTextParts);
        void Note(string message)
        {
            Detail(message, important: true);
            if (!notes.Contains(message)) notes.Add(message);
        }
        void Include(VerificationState next) => state = state is { } current && current < next ? current : next;
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
                var comparison = new ChecksumComparison(claim, actual[claim.Kind]);
                Keep(comparisons, importantComparisons, ref hiddenClaims, comparison, !comparison.Matches, maxDetails);
                if (comparison.Matches)
                {
                    if (!matched.Contains(name)) matched.Add(name);
                    Detail($"{name} matches {where}.");
                }
                else
                {
                    if (!differed.Contains(name)) differed.Add(name);
                    Detail($"{name} differs: {where} says {claim.Expected}, the file's is {actual[claim.Kind]}.", important: true);
                }
            }
            differing = differed.Count;
            if (differed.Count > 0)
            {
                Include(VerificationState.Differs);
                Text($"✗ {string.Join(", ", differed)} differs", important: true);
                Note("It is not the file the checksum describes: it was damaged, cut short, or changed, or the checksum itself is wrong.");
            }
            else
            {
                Include(VerificationState.Matches);
                var kinds = checksums.Select(c => c.Kind).Distinct().ToList();
                Text($"✓ {string.Join(", ", matched)}" + (kinds.All(Checksums.IsCompatibilityOnly) ? " (integrity only)" : ""));
            }
            if (checksums.Any(c => Checksums.IsCompatibilityOnly(c.Kind)))
                Note("MD5, SHA-1, and CRC-32 show that the file is intact, not that it is authentic: they can be forged.");
            // A signature over a manifest vouches for the files it lists, as far as the hash that ties each file to it can:
            // MD5, SHA-1, and CRC-32 can be forged, so through them a file is intact, never proven authentic.
            foreach (string manifest in checksums.Select(c => c.Source).Distinct())
                foreach (var signature in manifestSignatures(Path.GetFileName(manifest)))
                {
                    var r = checkSignature(signature, ct);
                    hasManifestSignature = true;
                    Keep(signatureChecks, importantSignatures, ref hiddenClaims, new SignatureCheck(signature, r, Path.GetFileName(manifest)), r.State != VerificationState.SignatureGood, maxDetails);
                    Detail($"{Path.GetFileName(signature.Signature)} (over {Path.GetFileName(manifest)}): {r.Text}.", important: r.State != VerificationState.SignatureGood);
                    var tied = checksums.Where(c => c.Source == manifest).Select(c => c.Kind).Distinct().ToList();
                    if (r.State == VerificationState.SignatureGood && tied.All(Checksums.IsCompatibilityOnly))
                    {
                        string weak = string.Join(" and ", tied.Select(Checksums.Name));
                        Include(VerificationState.Matches);
                        // Said of a file that matches: one that differs is neither intact nor authentic.
                        if (differed.Count == 0)
                        {
                            Text($"manifest signed by {r.Signer}, but {weak} can be forged", important: true);
                            Note($"{Path.GetFileName(signature.Signature)} is a good signature by {r.Signer} over {Path.GetFileName(manifest)}, but that list names this file "
                                 + $"by {weak} only, which can be forged: the file is intact, not proven authentic.");
                        }
                        continue;
                    }
                    Include(r.State);
                    if (r.State == VerificationState.SignatureGood && differed.Count == 0) Text($"manifest signed by {r.Signer}");
                    else if (r.State != VerificationState.SignatureGood) Text("manifest " + Short(r), important: true);
                }
        }
        foreach (var signature in signatures)
        {
            var r = checkSignature(signature, ct);
            signedByTrustedKey |= r.State == VerificationState.SignatureGood;
            Keep(signatureChecks, importantSignatures, ref hiddenClaims, new SignatureCheck(signature, r), r.State != VerificationState.SignatureGood, maxDetails);
            Detail($"{Path.GetFileName(signature.Signature)}: {r.Text}.", important: r.State != VerificationState.SignatureGood);
            Include(r.State);
            Text(r.State == VerificationState.SignatureGood ? $"✓ signed by {r.Signer}" : Short(r), important: r.State != VerificationState.SignatureGood);
        }
        // An unsigned checksum that matches proves no origin; a file a trusted key signed itself needs no such warning.
        if (checksums.Count > 0 && !hasManifestSignature && differing == 0 && !signedByTrustedKey)
            Note("A checksum beside its file shows that the file is intact; it proves nothing against tampering unless it is signed, or came from a place you trust.");
        if (state is null) return new VerificationResult(VerificationState.NotChecked, "", []);
        if (hiddenDetails > 0) details.Add($"{hiddenDetails:N0} more claims or signatures were checked; details not shown.");
        if (hiddenTexts > 0) texts.Add($"{hiddenTexts:N0} more results");
        if (hiddenClaims > 0) notes.Add($"{hiddenClaims:N0} more claims or signatures were checked and are not listed; those listed include every one that failed, up to {maxDetails - 1}.");
        return new VerificationResult(state.Value, string.Join(" · ", texts), details)
        {
            Checksums = comparisons,
            Signatures = signatureChecks,
            Notes = notes,
        };
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

    /// <summary>A signature's result in a few words for a row ("✗ bad signature", "? signed by an unknown key").</summary>
    internal static string Short(SignatureResult r) => r.Short ?? r.State switch
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
