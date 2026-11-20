using FileCat.Core.Archives;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    [Theory] [MemberData(nameof(ArchiveRar5NativeCases.Modes), MemberType = typeof(ArchiveRar5NativeCases))]
    public async Task Captured_encrypted_RAR_creation_uses_AppContainer_native_sealing_and_publication(int method, int mode)
        => await CreateRar(method, mode, application: false);

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Actual_application_RAR_protocol_two_workers_enter_confinement_before_parsing(int mode)
        => await CreateRar(3, mode, application: true);

    private static async Task CreateRar(int method, int mode, bool application)
    {
        string executable = Probe(); var ct = TestContext.Current.CancellationToken;
        if (application)
        {
            string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
            Assert.True(assets is not null && Path.IsPathFullyQualified(assets)); ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!);
            executable = Path.Combine(assets!, "FileCat.exe");
        }
        string root = Directory.CreateTempSubdirectory("filecat-rar-native-").FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
            byte[] data = new byte[70001]; new Random(42829321).NextBytes(data); string input = Path.Combine(root, "payload"); File.WriteAllBytes(input, data);
            File.WriteAllText(input + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            File.WriteAllText(folder + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=4\r\n");
            File.SetLastWriteTimeUtc(input, DateTime.UnixEpoch.AddSeconds(1700000000).AddTicks(1234567));
            Directory.SetLastWriteTimeUtc(folder, DateTime.UnixEpoch.AddSeconds(1700000001).AddTicks(7654321));
            using var directory = WindowsArchiveDirectory.Open(root); using var captured = new ArchiveCapturedInputs();
            directory.CaptureSource(captured, 1, "payload", ct); directory.CaptureSourceDirectory(captured, 2, "folder", ct); captured.Seal(ct);
            var workers = new List<ArchiveAppContainer>(); ArchiveAppContainer Launch(Guid nonce, TimeSpan cpu, string role)
            {
                var worker = application ? role == "archive-transform-v2" ? ArchiveAppContainer.StartTransform(executable, nonce, cpu, 2)
                    : ArchiveAppContainer.StartVerification(executable, nonce, cpu, 2) : ArchiveAppContainer.Start(executable, role + " " + nonce, cpu);
                workers.Add(worker); return worker;
            }
            await ArchiveRar5NativeCases.Create(method, mode, root, captured,
                (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
                new((nonce, cpu) => Launch(nonce, cpu, "archive-transform-v2"), protocolVersion: 2),
                new((nonce, cpu) => Launch(nonce, cpu, "archive-verify-v2"), protocolVersion: 2), ct, application ? "windows-app" : "windows-probe");
            Assert.Equal(2, workers.Count); Assert.All(workers, worker => Assert.Equal(0u, worker.ExitCode));
            using var published = directory.OpenSource("result.rar");
            Assert.Equal(4, ArchiveWindowsOriginZone.Parse(((IArchiveRetainedFile)published).Observe(ct).Origin).DeclaredZone);
        }
        finally { Directory.Delete(root, true); }
    }
}
