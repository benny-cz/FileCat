using System.IO.Compression;
using FileCat.App.Services;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Verification;
using FileCat.Platform.Windows;
using FileCat.Remote.Ftp;
using FileCat.Remote.Sftp;

namespace FileCat.App.Tests;

public sealed class ElevatedSecurityBoundaryTests
{
    private sealed class ArchiveRows : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var entry in entries) Entries.Add(entry);
        }
        public void ReportIssue(string message) { }
    }

    [Fact]
    public async Task Native_high_token_refuses_user_profile_authority_and_persistence()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_SECURITY_VM_LINK_FIXTURE");
        if (root is null) Assert.Skip("Run in the owned medium/high Windows fixture.");
        Assert.True(OperatingSystem.IsWindows() && WindowsTokenAuthority.IsHighIntegrity);
        string marker = Path.Combine(root, "high-only-marker.txt");
        string original = File.ReadAllText(marker);
        string trust = Path.Combine(root, "trust");
        string cacheFile = Path.Combine(root, "verification-cache.jsonl");
        string journal = Path.Combine(root, "job-journals");
        string sessions = Path.Combine(root, "edit-sessions");
        Directory.CreateDirectory(trust);
        Directory.CreateDirectory(journal);
        Directory.CreateDirectory(sessions);
        foreach (string linked in new[] { Path.Combine(trust, "known_hosts.tmp"), Path.Combine(trust, "ftps.txt.tmp"),
                     cacheFile + ".tmp", Path.Combine(root, "hex-forged.fcj") })
            Assert.True((File.GetAttributes(linked) & FileAttributes.ReparsePoint) != 0, linked);
        Assert.Equal("OWNED-CACHE-SENTINEL", File.ReadAllText(cacheFile));
        string stateFile = Path.Combine(root, "forged-state.json");
        var defaults = JsonFileStore.Load(stateFile, StateJsonContext.Default.WorkspaceState,
            WorkspaceState.CurrentSchema, () => new WorkspaceState(), out var loadStatus);
        Assert.Equal(StateLoadStatus.UnavailableReadOnly, loadStatus);
        Assert.Equal("Default", defaults.Name);
        Assert.Equal("{not-json", File.ReadAllText(stateFile));
        Assert.False(File.Exists(stateFile + ".corrupt"));

        byte[] key = [0, 0, 0, 1, (byte)'A'];
        var hosts = new HostKeyTrust(Path.Combine(trust, "known_hosts"), []);
        Assert.Equal(HostKeyStatus.Unavailable, hosts.Check("owned.invalid", 22, key).Status);
        Assert.Throws<IOException>(() => hosts.Remember("owned.invalid", 22, key));
        var cert = new CertificateInfo("owned", "owned", DateTime.UtcNow, DateTime.UtcNow.AddDays(1), new string('a', 64), ["self-signed"]);
        var certificates = new CertificateTrust(Path.Combine(trust, "ftps.txt"));
        Assert.Equal(CertificateStatus.Unknown, certificates.Check("owned.invalid", 21, cert).Status);
        Assert.Throws<IOException>(() => certificates.Remember("owned.invalid", 21, cert.Sha256));

        var cache = new VerificationCache(cacheFile);
        cache.Put("owned", new VerificationResult(VerificationState.NotChecked, "owned", []));
        Assert.Equal("OWNED-CACHE-SENTINEL", File.ReadAllText(cacheFile));
        Assert.Null(cache.Get("forged"));
        string verificationLink = Path.Combine(root, "verification-linked.txt");
        Assert.True((File.GetAttributes(verificationLink) & FileAttributes.ReparsePoint) != 0);
        var verifier = new VerificationService(cache, () => 1024 * 1024, () => Array.Empty<string>());
        Assert.Null(verifier.Automatic(verificationLink, TestContext.Current.CancellationToken));
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            // An implicit listing refresh must not enter the sidecar scanner.
            verifier.FolderChanged(root, canceled.Token);
        }
        Assert.Single(verifier.SidecarsOf(root, TestContext.Current.CancellationToken).Checksums("verification-linked.txt"));
        Assert.Equal(default, verifier.Summary(root, ["verification-linked.txt"]));
        Assert.Equal("OWNED-CACHE-SENTINEL", File.ReadAllText(cacheFile));
        Assert.Empty(JournalRecovery.Scan(journal));
        var edits = new EditSessionStore(sessions, new PortableFileOperations());
        Assert.Empty(edits.LoadAll());
        Assert.Throws<IOException>(() => edits.Save(new EditSessionRecord()));
        Assert.Empty(HexSaveJournal.Pending(root));
        Assert.Throws<IOException>(() => HexSaveJournal.Read(Path.Combine(root, "forged.fcj")));
        Assert.Throws<IOException>(() => HexSaveJournal.Save(null!, null!, root));

        Assert.Null(await GitStatusReader.ReadAsync(root, TestContext.Current.CancellationToken, "attacker-git.exe"));
        Assert.Equal(VerificationState.SignatureUnchecked,
            OpenPgp.Verify(Path.Combine(root, "forged.sig"), marker, TestContext.Current.CancellationToken).State);
        var shell = new WindowsShellServices();
        Assert.Throws<IOException>(() => shell.Open(marker));
        Assert.Throws<IOException>(() => shell.OpenTerminal(root, "cmd"));
        Assert.Throws<IOException>(() => shell.RunInTerminal(root, "echo owned", "cmd"));

        string spoolDirectory = Path.Combine(root, "member-spools");
        const int nearLimit = 63 * 1024 * 1024;
        var payload = new byte[65 * 1024 * 1024];
        payload.AsSpan().Fill((byte)'A');
        var limits = new MemberLimits(nearLimit, nearLimit, nearLimit + 1L, 100,
            Crc32.HashToUInt32(payload.AsSpan(0, nearLimit)));
        using (var member = new ProgressiveContent("owned", new object(), () => new MemoryStream(payload, 0, nearLimit, writable: false),
                   limits, 64L * 1024 * 1024, spoolDirectory))
        {
            var buffer = new byte[256 * 1024];
            for (long at = 0; at < nearLimit; at += buffer.Length)
                Assert.Equal((int)Math.Min(buffer.Length, nearLimit - at), member.Read(at, buffer));
            Assert.Equal(0, member.Read(nearLimit, buffer));
            Assert.Equal((byte)'A', buffer[0]);
        }
        var oversized = new MemberLimits(payload.Length, payload.Length, payload.Length + 1L, 100,
            Crc32.HashToUInt32(payload));
        using (var member = new ProgressiveContent("owned-over-limit", new object(),
                   () => new MemoryStream(payload, writable: false), oversized,
                   64L * 1024 * 1024, spoolDirectory))
        {
            var buffer = new byte[256 * 1024];
            for (long at = 0; at < 64L * 1024 * 1024; at += buffer.Length)
                Assert.Equal(buffer.Length, member.Read(at, buffer));
            var error = Assert.Throws<InvalidDataException>(() => member.Read(64L * 1024 * 1024, buffer));
            Assert.Contains("64 MiB", error.Message);
        }
        Assert.False(Directory.Exists(spoolDirectory));
        string innerPath = Path.Combine(root, $"ac37-outer-{Guid.NewGuid():N}.zip");
        using (var innerBytes = new MemoryStream())
        {
            using (var innerArchive = new ZipArchive(innerBytes, ZipArchiveMode.Create, leaveOpen: true))
            using (var innerOutput = innerArchive.CreateEntry("inside.txt").Open()) innerOutput.WriteByte(42);
            using var outerArchive = ZipFile.Open(innerPath, ZipArchiveMode.Create);
            using var outerOutput = outerArchive.CreateEntry("inner.zip").Open();
            outerOutput.Write(innerBytes.ToArray());
        }
        string nestedSpool = Path.Combine(root, "nested-spools");
        var zip = new ZipProvider(nestedSpool);
        var outerLocation = zip.GetContainerLocation(innerPath)!;
        var rows = new ArchiveRows();
        await zip.EnumerateAsync(outerLocation, rows, TestContext.Current.CancellationToken);
        var innerLocation = zip.GetChildLocation(outerLocation, Assert.Single(rows.Entries))!;
        var nestedError = await Assert.ThrowsAnyAsync<IOException>(async () =>
            await zip.EnumerateAsync(innerLocation, new ArchiveRows(), TestContext.Current.CancellationToken));
        Assert.Contains("administrator", nestedError.Message);
        Assert.False(Directory.Exists(nestedSpool));
        Assert.Equal(original, File.ReadAllText(marker));
        File.WriteAllText(Path.Combine(root, "ac37-result.txt"),
            $"HighIntegrity={WindowsTokenAuthority.IsHighIntegrity}\nNearBytes={nearLimit}\nOverLimitRefused=True\n" +
            $"NestedRefused=True\nMemberSpoolExists={Directory.Exists(spoolDirectory)}\n" +
            $"NestedSpoolExists={Directory.Exists(nestedSpool)}\nMarker={File.ReadAllText(marker)}\n");
        Assert.False(File.Exists(Path.Combine(trust, "known_hosts")));
        Assert.False(File.Exists(Path.Combine(trust, "ftps.txt")));
    }
}
