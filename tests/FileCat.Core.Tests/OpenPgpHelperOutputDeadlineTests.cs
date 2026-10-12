using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

[Collection(nameof(GnuPgToolOverride))]
public sealed class OpenPgpHelperOutputDeadlineTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Exited_helper_with_a_descendant_held_status_pipe_obeys_the_one_minute_deadline()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "This owned shell/pipe control requires Unix.");
        string root = Directory.CreateTempSubdirectory("filecat-owned-gpg-pipe-").FullName;
        string helper = Path.Combine(root, "owned-gpg");
        string release = helper + ".release", done = helper + ".done", parent = helper + ".parent";
        string signature = Path.Combine(root, "owned.sig"), signed = Path.Combine(root, "owned.bin");
        byte[] sigBytes = "synthetic signature only\n"u8.ToArray();
        byte[] dataBytes = Enumerable.Range(0, 8192).Select(n => (byte)(n * 17 + 3)).ToArray();
        File.WriteAllBytes(signature, sigBytes);
        File.WriteAllBytes(signed, dataBytes);
        File.WriteAllText(helper, "#!/bin/sh\n(i=0; while [ \"$i\" -lt 90 ] && [ ! -f \"$0.release\" ]; do " +
            "/bin/sleep 1; i=$((i+1)); done; printf done > \"$0.done\") &\n" +
            "printf '[GNUPG:] GOODSIG 1234567890ABCDEF Owned Synthetic Signer\\n[GNUPG:] TRUST_FULLY\\n'\n" +
            "printf parent > \"$0.parent\"\n");
        File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var tool = typeof(OpenPgp).GetField("_tool", BindingFlags.NonPublic | BindingFlags.Static)!;
        var looked = typeof(OpenPgp).GetField("_looked", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? oldTool = tool.GetValue(null), oldLooked = looked.GetValue(null);
        Task<SignatureResult>? pending = null;
        try
        {
            OpenPgp.UseTool(helper);
            var clock = Stopwatch.StartNew();
            pending = Task.Run(() => OpenPgp.Verify(signature, signed, CancellationToken.None, root));
            Assert.True(SpinWait.SpinUntil(() => File.Exists(parent), TimeSpan.FromSeconds(5)),
                "The owned helper did not publish its parent-exit marker.");
            Task first = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(70), TestContext.Current.CancellationToken));
            output.WriteLine($"completedBefore70Seconds={ReferenceEquals(first, pending)}, elapsedMs={clock.ElapsedMilliseconds}");
            Assert.Same(pending, first);
            SignatureResult result = await pending;
            Assert.Equal(VerificationState.SignatureUnchecked, result.State);
            Assert.Contains("within a minute", result.Text);
            Assert.Equal(sigBytes, File.ReadAllBytes(signature));
            Assert.Equal(dataBytes, File.ReadAllBytes(signed));
            output.WriteLine($"signatureSha256={Convert.ToHexString(SHA256.HashData(sigBytes))}, " +
                $"signedSha256={Convert.ToHexString(SHA256.HashData(dataBytes))}");
        }
        finally
        {
            // An original wrapper can still be reading the inherited pipe. Release only our finite child.
            File.WriteAllText(release, "release");
            Assert.True(SpinWait.SpinUntil(() => File.Exists(done), TimeSpan.FromSeconds(5)));
            if (pending is not null) await pending.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            tool.SetValue(null, oldTool);
            looked.SetValue(null, oldLooked);
            File.Delete(done); File.Delete(parent); File.Delete(release);
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }
}
