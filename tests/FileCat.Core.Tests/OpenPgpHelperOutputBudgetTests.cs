using System.Reflection;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

[Collection(nameof(GnuPgToolOverride))]
public sealed class OpenPgpHelperOutputBudgetTests
{
    [Fact]
    public void Oversized_status_output_is_refused_without_retaining_the_whole_stream()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "This owned shell control requires Unix.");
        string root = Directory.CreateTempSubdirectory("filecat-owned-gpg-output-").FullName;
        string helper = Path.Combine(root, "owned-gpg");
        string signature = Path.Combine(root, "owned.sig"), signed = Path.Combine(root, "owned.bin");
        byte[] sigBytes = "synthetic signature only\n"u8.ToArray();
        byte[] dataBytes = Enumerable.Range(0, 8192).Select(n => (byte)(n * 17 + 3)).ToArray();
        byte[] bulk = Enumerable.Repeat((byte)'x', (1 << 20) + 4096).ToArray();
        File.WriteAllBytes(signature, sigBytes);
        File.WriteAllBytes(signed, dataBytes);
        File.WriteAllBytes(helper + ".bulk", bulk);
        File.WriteAllText(helper, "#!/bin/sh\n/bin/cat \"$0.bulk\"\n" +
            "printf '\\n[GNUPG:] GOODSIG 1234567890ABCDEF Owned Synthetic Signer\\n[GNUPG:] TRUST_FULLY\\n'\n");
        File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var tool = typeof(OpenPgp).GetField("_tool", BindingFlags.NonPublic | BindingFlags.Static)!;
        var looked = typeof(OpenPgp).GetField("_looked", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? oldTool = tool.GetValue(null), oldLooked = looked.GetValue(null);
        try
        {
            OpenPgp.UseTool(helper);
            var result = OpenPgp.Verify(signature, signed, TestContext.Current.CancellationToken, root);
            Assert.Equal(VerificationState.SignatureUnchecked, result.State);
            Assert.Contains("status output exceeded", result.Text);
            Assert.Equal(sigBytes, File.ReadAllBytes(signature));
            Assert.Equal(dataBytes, File.ReadAllBytes(signed));
            Assert.Equal(bulk, File.ReadAllBytes(helper + ".bulk"));
        }
        finally
        {
            tool.SetValue(null, oldTool);
            looked.SetValue(null, oldLooked);
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }
}
