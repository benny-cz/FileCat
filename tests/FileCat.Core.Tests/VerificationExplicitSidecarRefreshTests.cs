using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

public sealed class VerificationExplicitSidecarRefreshTests(ITestOutputHelper output)
{
    [Fact]
    public void Automatic_verification_invalidates_a_rewritten_sidecar_after_folder_rescan()
    {
        string root = Directory.CreateTempSubdirectory("filecat-automatic-sidecar-").FullName;
        string target = Path.Combine(root, "content.bin");
        string sidecar = Path.Combine(root, "SHA256SUMS");
        byte[] bytes = "owned verification content"u8.ToArray();
        File.WriteAllBytes(target, bytes);
        File.WriteAllText(sidecar, Convert.ToHexStringLower(SHA256.HashData(bytes)) + "  content.bin\n", new UTF8Encoding(false));
        var service = new VerificationService(new VerificationCache(null), () => long.MaxValue, () => []) { Settle = TimeSpan.Zero };
        int changed = 0;
        service.SidecarsChanged += _ => changed++;
        try
        {
            var first = service.Automatic(target, TestContext.Current.CancellationToken);
            DateTime sidecarTime = File.GetLastWriteTimeUtc(sidecar);
            DateTime folderTime = Directory.GetLastWriteTimeUtc(root);
            File.WriteAllText(sidecar, new string('0', 64) + "  content.bin\n", new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(sidecar, sidecarTime);
            Directory.SetLastWriteTimeUtc(root, folderTime);
            service.FolderChanged(root, TestContext.Current.CancellationToken);
            var second = service.Automatic(target, TestContext.Current.CancellationToken);
            output.WriteLine("AUTOMATIC_SIDECAR_REWRITE " + JsonSerializer.Serialize(new
            {
                first = first?.State.ToString(), second = second?.State.ToString(), changed,
                sidecarTimeUnchanged = File.GetLastWriteTimeUtc(sidecar) == sidecarTime,
                folderTimeUnchanged = Directory.GetLastWriteTimeUtc(root) == folderTime,
                targetUnchanged = File.ReadAllBytes(target).SequenceEqual(bytes),
            }));
            Assert.Equal(VerificationState.Matches, first?.State);
            Assert.Equal(sidecarTime, File.GetLastWriteTimeUtc(sidecar));
            Assert.Equal(folderTime, Directory.GetLastWriteTimeUtc(root));
            Assert.Equal(bytes, File.ReadAllBytes(target));
            Assert.Equal(1, changed);
            Assert.Equal(VerificationState.NotChecked, second?.State);
            Assert.Equal(VerificationState.Differs, service.OnRequest(target, TestContext.Current.CancellationToken)?.State);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }

    [Fact]
    public void Explicit_verification_reads_a_sidecar_rewritten_with_its_metadata_restored()
    {
        string root = Directory.CreateTempSubdirectory("filecat-explicit-sidecar-").FullName;
        string target = Path.Combine(root, "content.bin");
        string sidecar = Path.Combine(root, "SHA256SUMS");
        byte[] bytes = "owned verification content"u8.ToArray();
        string matching = Convert.ToHexStringLower(SHA256.HashData(bytes)) + "  content.bin\n";
        string different = new string('0', 64) + "  content.bin\n";
        File.WriteAllBytes(target, bytes);
        File.WriteAllText(sidecar, matching, new UTF8Encoding(false));
        var service = new VerificationService(new VerificationCache(null), () => long.MaxValue, () => []);
        try
        {
            var first = service.OnRequest(target, TestContext.Current.CancellationToken);
            DateTime sidecarTime = File.GetLastWriteTimeUtc(sidecar);
            DateTime folderTime = Directory.GetLastWriteTimeUtc(root);
            File.WriteAllText(sidecar, different, new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(sidecar, sidecarTime);
            Directory.SetLastWriteTimeUtc(root, folderTime);
            var second = service.OnRequest(target, TestContext.Current.CancellationToken);

            output.WriteLine("EXPLICIT_SIDECAR_REWRITE " + JsonSerializer.Serialize(new
            {
                first = first?.State.ToString(), second = second?.State.ToString(),
                sidecarLength = new FileInfo(sidecar).Length,
                sidecarTimeUnchanged = File.GetLastWriteTimeUtc(sidecar) == sidecarTime,
                folderTimeUnchanged = Directory.GetLastWriteTimeUtc(root) == folderTime,
                targetUnchanged = File.ReadAllBytes(target).SequenceEqual(bytes),
                sidecarChanged = File.ReadAllText(sidecar) == different,
            }));
            Assert.Equal(VerificationState.Matches, first?.State);
            Assert.Equal((long)Encoding.UTF8.GetByteCount(matching), new FileInfo(sidecar).Length);
            Assert.Equal(sidecarTime, File.GetLastWriteTimeUtc(sidecar));
            Assert.Equal(folderTime, Directory.GetLastWriteTimeUtc(root));
            Assert.Equal(bytes, File.ReadAllBytes(target));
            Assert.Equal(different, File.ReadAllText(sidecar));
            Assert.Equal(VerificationState.Differs, second?.State);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }
}
