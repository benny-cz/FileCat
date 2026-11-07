using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Theory] [MemberData(nameof(ArchiveSevenZipNativeCases.Modes), MemberType = typeof(ArchiveSevenZipNativeCases))]
    public async Task Captured_7z_files_and_directories_publish_only_after_journaled_native_sealing_and_fresh_verification(int method, int mode)
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0); string folder = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
        string input = Path.Combine(root, "payload"); byte[] data = new byte[70001]; new Random(42829321).NextBytes(data); File.WriteAllBytes(input, data);
        File.SetUnixFileMode(input, (UnixFileMode)0x180); File.SetUnixFileMode(folder, (UnixFileMode)0x1c0);
        File.SetLastWriteTimeUtc(input, DateTime.UnixEpoch.AddSeconds(1700000000).AddTicks(1234567));
        Directory.SetLastWriteTimeUtc(folder, DateTime.UnixEpoch.AddSeconds(1700000001).AddTicks(7654321));
        using var directory = UnixArchiveDirectory.Open(root); using var captured = new ArchiveCapturedInputs();
        directory.CaptureSource(captured, 1, "payload", ct); directory.CaptureSourceDirectory(captured, 2, "folder", ct); captured.Seal(ct);
        var workers = new List<Child>(); Child Launch(Guid nonce, string role) { var child = Child.Start(nonce, role); workers.Add(child); return child; }
        await ArchiveSevenZipNativeCases.Create(method, mode, root, captured,
            (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
            new((nonce, _) => Launch(nonce, "archive-transform-v2"), protocolVersion: 2),
            new((nonce, _) => Launch(nonce, "archive-verify-v2"), protocolVersion: 2), ct);
        Assert.Equal(2, workers.Count); Assert.All(workers, worker => { Assert.True(worker.Reaped); Assert.True(worker.Disposed); });
    }
}
