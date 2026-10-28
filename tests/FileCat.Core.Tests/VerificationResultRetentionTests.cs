using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Operations;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

public sealed class VerificationResultRetentionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Many_claims_keep_a_bounded_cached_result_without_hiding_a_late_mismatch(bool lateMismatch)
    {
        using var root = new TempDir();
        const string payload = "owned verification payload";
        string target = root.File("item.bin", payload);
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddMinutes(-2));
        string good = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        string wrong = Convert.ToHexStringLower(SHA256.HashData("other payload"u8));
        var lines = Enumerable.Repeat($"{good} *item.bin", 127).ToList();
        lines.Add($"{(lateMismatch ? wrong : good)} *item.bin");
        root.File("SHA256SUMS", string.Join('\n', lines) + "\n");

        var service = new VerificationService(new VerificationCache(null), () => 1L << 20, () => []) { Settle = TimeSpan.Zero };
        var result = service.Automatic(target, TestContext.Current.CancellationToken)!;
        Assert.Equal(lateMismatch ? VerificationState.Differs : VerificationState.Matches, result.State);
        Assert.InRange(result.Details.Count, 1, 64);
        Assert.Contains(result.Details, d => d.Contains("more claims", StringComparison.Ordinal));
        if (lateMismatch)
            Assert.Contains(result.Details, d => d.Contains("differs", StringComparison.Ordinal) && d.Contains("line 128", StringComparison.Ordinal));

        var cached = service.Automatic(target, TestContext.Current.CancellationToken)!;
        Assert.Equal(VerificationState.NotChecked, cached.State);
        Assert.Contains("previously checked", cached.Text, StringComparison.Ordinal);
        Assert.InRange(cached.Details.Count, 1, 64);
        Assert.Contains(cached.Details, d => d.Contains("more claims", StringComparison.Ordinal));
        if (lateMismatch)
            Assert.Contains(cached.Details, d => d.Contains("differs", StringComparison.Ordinal) && d.Contains("line 128", StringComparison.Ordinal));
        Assert.Equal(result.State, service.OnRequest(target, TestContext.Current.CancellationToken)!.State);
        Assert.Equal(payload, File.ReadAllText(target));
    }

    [Fact]
    public void Many_signature_results_keep_late_failure_visible_without_retaining_all_diagnostics()
    {
        var signatures = Enumerable.Range(1, 128)
            .Select(i => new SignatureClaim(SignatureKind.Minisign, $"sig-{i}.minisig", "item.bin"))
            .ToArray();
        var result = Verifier.Check([], signatures,
            _ => new Dictionary<ChecksumKind, string>(),
            (signature, _) => signature.Signature == "sig-128.minisig"
                ? new SignatureResult(VerificationState.SignatureBad, "bad signature")
                : new SignatureResult(VerificationState.SignatureGood, new string('g', 800), new string('s', 800)),
            _ => [], TestContext.Current.CancellationToken);

        Assert.Equal(VerificationState.SignatureBad, result.State);
        Assert.InRange(result.Details.Count, 1, 64);
        Assert.Contains(result.Details, d => d.Contains("sig-128.minisig", StringComparison.Ordinal) && d.Contains("bad signature", StringComparison.Ordinal));
        Assert.Contains(result.Details, d => d.Contains("more claims", StringComparison.Ordinal));
        Assert.InRange(result.Text.Length, 1, 8_192);
        Assert.Contains("bad signature", result.Text, StringComparison.Ordinal);
        Assert.Contains("more results", result.Text, StringComparison.Ordinal);
    }
}
