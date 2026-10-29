using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Mtp;
using FileCat.Remote.Sftp;
using FileCat.Remote.Tests;

namespace FileCat.App.Tests;

/// <summary>
/// Review finding R-M2: FAT and exFAT derive a file's identity from where its directory entry lies, and a rename to a
/// longer name can move that entry, so a Move that renames its source to a longer hold name before deleting it must not
/// refuse for a changed identity there. Runs where FILECAT_TEST_FAT_DIRS names writable folders on FAT32 or exFAT volumes,
/// separated by ';'.
/// </summary>
public sealed class FatMoveIdentityTests(ITestOutputHelper output)
{
    private static IReadOnlyList<string> Folders() =>
        Environment.GetEnvironmentVariable("FILECAT_TEST_FAT_DIRS")?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    [Fact]
    public async Task A_move_from_a_FAT_or_exFAT_folder_removes_its_source_after_the_hold_rename()
    {
        if (!OperatingSystem.IsWindows() || Folders().Count == 0)
            Assert.Skip("Set FILECAT_TEST_FAT_DIRS to writable folders on FAT32 or exFAT volumes (Windows).");
        var ops = new WindowsFileOperations();
        foreach (string folder in Folders())
        {
            var volume = ops.GetVolumeInfo(folder);
            Assert.False(volume.IdentitySurvivesRename, $"{folder} is on {volume.FileSystem}, not FAT or exFAT.");
            string work = Directory.CreateDirectory(Path.Combine(folder, "fc-rm2-" + Guid.NewGuid().ToString("N")[..8])).FullName;
            try
            {
                // What the volume does: a short name renamed to a hold name as long as the Moves use.
                string probe = Path.Combine(work, "a.txt");
                File.WriteAllText(probe, "probe");
                string? before = ops.GetFileIdentity(probe), legacyBefore = LegacyIndex(probe);
                string renamed = probe + ".filecat-move-" + Guid.NewGuid().ToString("N") + ".hold";
                File.Move(probe, renamed);
                string? after = ops.GetFileIdentity(renamed), legacyAfter = LegacyIndex(renamed);
                output.WriteLine($"FAT_IDENTITY fs={volume.FileSystem} id128 before={before ?? "none"} after={after ?? "none"} " +
                                 $"index before={legacyBefore} after={legacyAfter} changed={legacyBefore != legacyAfter}");

                MtpRetire(work, volume);
                if (WindowsTokenAuthority.IsHighIntegrity) output.WriteLine("SFTP part not run: an elevated FileCat reads no host-key trust.");
                else await SftpMove(work, ops);
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch (IOException) { }
            }
        }
    }

    private static async Task SftpMove(string work, WindowsFileOperations ops)
    {
        var server = new FakeSftpServer();
        server.Dir("/up");
        var profile = new RemoteProfile { Name = "Test", Host = "files.example", User = "user" };
        var ui = new ScriptedInteraction { HostKeyAnswer = HostKeyDecision.AcceptOnce };
        for (int i = 0; i < 4; i++) ui.Secrets.Enqueue("secret");
        using var connections = new SftpConnections(id => id == profile.Id ? profile : null, new FakeConnector(server),
            new HostKeyTrust(Path.Combine(work, "known_hosts"), []), new SessionSecretStore(), ui);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(new SftpProvider(connections, () => [profile], p => p));
        SftpJobs.Register();
        var jobs = new JobManager(ops, providers, Path.Combine(work, "journal"));
        string source = Path.Combine(work, "b.txt");
        File.WriteAllText(source, "moved over SFTP");
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Move, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)], Destination = SftpProvider.At(profile, "/up"),
        });
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!job.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline, "The SFTP move did not finish.");
            job.Decision?.Resolve(new Decision(DecisionAction.Skip));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.True(job.State == JobState.Completed, string.Join("; ", job.Issues.Select(i => i.Message)));
        Assert.False(File.Exists(source));
        Assert.Equal("moved over SFTP", server.Read("/up/b.txt"));
    }

    private static void MtpRetire(string work, VolumeInfo volume)
    {
        string source = Path.Combine(work, "c.txt");
        File.WriteAllText(source, "moved to a device");
        byte[] digest = SHA256.HashData(File.ReadAllBytes(source));
        FileIdentity? identity = null;
        using (var handle = File.OpenHandle(source))
        {
            try { identity = ProtectedHexFile.Identity(handle); }
            catch (System.ComponentModel.Win32Exception) { } // FAT32 has no 128-bit file ID
        }
        MtpMoveSafety.RetireVerifiedSource(source, MtpMoveSafety.HoldName(source), digest, identity, volume.IdentitySurvivesRename);
        Assert.False(File.Exists(source));
        Assert.Empty(Directory.GetFiles(work, "c.txt*"));
    }

    /// <summary>The 64-bit file index Windows gives every file system, FAT's included (from its directory entry).</summary>
    private static string LegacyIndex(string path)
    {
        using var handle = File.OpenHandle(path);
        if (!GetFileInformationByHandle(handle, out var info)) return "unavailable";
        return $"{info.IndexHigh:X8}{info.IndexLow:X8}";
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleInfo
    {
        public uint Attributes;
        public long Created, Accessed, Written;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, out ByHandleInfo info);
}
