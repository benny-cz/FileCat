using System.Diagnostics;
using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Tests;

internal static class ArchiveCreationCrashAssertions
{
    internal static async Task<ArchiveTransactionFacts> KillAndRecover(string root, string point, string windowsAssembly)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string sentinel = Path.Combine(root, "untouched-sentinel"); File.WriteAllText(sentinel, "owned unchanged sentinel");
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "FileCat.slnx"))) checkout = checkout.Parent;
        Assert.NotNull(checkout);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string harness = Path.Combine(checkout.FullName, "tests", "FileCat.CoreCrashHarness", "bin", configuration, "net10.0", "FileCat.CoreCrashHarness.dll");
        string probe = Path.Combine(checkout.FullName, "tests", "FileCat.WorkerProbe", "bin", configuration, "net10.0", "FileCat.WorkerProbe.dll");
        string runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
        var start = new ProcessStartInfo(Path.Combine(runtime, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { harness, "--archive-creation-crash", root, point, windowsAssembly, probe }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; Task<string> errors = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            string? checkpoint = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.True(checkpoint == "archive-creation-checkpoint:" + point, "Missing checkpoint: " + checkpoint + (process.HasExited ? " stderr: " + await errors : ""));
            Assert.False(process.HasExited); process.Kill(entireProcessTree: true); await process.WaitForExitAsync(timeout.Token);
            Assert.NotEqual(0, process.ExitCode); Assert.Equal("", await errors.WaitAsync(timeout.Token));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        var before = Snapshot(); string journals = Path.Combine(root, "journals");
        var interrupted = Assert.Single(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        Assert.Null(interrupted.ArchiveTransactions.Problem); Assert.True(interrupted.ArchiveTransactions.RequiresReview);
        Assert.False(JournalRecovery.TryClose(interrupted, "creation crash requires native object review"));
        var facts = Assert.Single(interrupted.ArchiveTransactions.Transactions);
        Assert.Equal(ArchiveTransactionFacts.SchemaVersion, facts.RecordVersion); Assert.NotNull(facts.VerifiedStage); Assert.Null(facts.Original);
        Assert.Equal(point switch
        {
            "ready" => ArchiveTransactionPhase.ReadyToPublish,
            "publishing" or "claimed" => ArchiveTransactionPhase.Publishing,
            "reconciling" => ArchiveTransactionPhase.Reconciling,
            "committed-warning" => ArchiveTransactionPhase.CommittedWithRetainedArtifacts,
            _ => ArchiveTransactionPhase.Verifying,
        }, facts.Phase);
        Assert.Equal(point is "reconciling" or "committed-warning" ? facts.VerifiedStage : null, facts.Published);
        bool claimed = point is "claimed" or "reconciling" or "committed-warning";
        Assert.Equal(!claimed, File.Exists(facts.StagePath)); Assert.Equal(claimed, File.Exists(facts.ArchivePath));
        string stage = claimed ? facts.ArchivePath : facts.StagePath;
        Assert.Equal(facts.VerifiedStage.Length, new FileInfo(stage).Length);
        Assert.Equal(facts.VerifiedStage.Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(stage))));
        using (var stream = File.OpenRead(stage)) { using var reader = new TarReader(stream); Assert.Null(reader.GetNextEntry()); }
        var metadata = Assert.Single(facts.Sidecars, s => s.Role == ArchiveSidecarRole.CreationMetadata);
        Assert.Equal(4, facts.Sidecars.Count);
        Assert.Equal(point != "metadata-reserved", metadata.Identity is not null);
        bool recorded = point is not ("metadata-reserved" or "metadata-identified" or "metadata-partial" or "metadata-sealed");
        Assert.Equal(recorded, metadata.Version is not null); Assert.Equal(point != "metadata-reserved", File.Exists(metadata.Path));
        if (point is "metadata-identified" or "metadata-partial") Assert.Equal(point == "metadata-partial" ? 1 : 0, new FileInfo(metadata.Path).Length);
        if (point is "metadata-sealed" || recorded)
        {
            byte[] data = File.ReadAllBytes(metadata.Path); Assert.Equal("FCCMETA1"u8.ToArray(), data[..8]); Assert.Equal("FCCMEND1"u8.ToArray(), data[^8..]);
            if (recorded) { Assert.Equal(metadata.Version!.Length, data.Length); Assert.Equal(metadata.Version.Sha256, Convert.ToHexString(SHA256.HashData(data))); }
        }
        bool ready = point is "ready" or "publishing" or "claimed" or "reconciling" or "committed-warning";
        Assert.Equal(ready ? metadata.Version!.Sha256 : null, facts.SecuritySnapshotSha256);
        Assert.All(facts.Sidecars.Where(s => s.Role != ArchiveSidecarRole.CreationMetadata), s => Assert.NotNull(s.Version));
        if (point == "committed-warning") Assert.Equal(facts.Sidecars.Select(s => s.Path), facts.UnresolvedArtifacts);
        Assert.Equal("owned unchanged sentinel", File.ReadAllText(sentinel));
        Assert.DoesNotContain(JobJournal.ReadAll(interrupted.JournalPath), r => r.Type == "end");
        // Repeated scans and stale close attempts must not erase, rewrite, publish or prune any surviving object.
        Assert.Single(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        Assert.False(JournalRecovery.TryClose(interrupted, "stale closure remains refused"));
        Assert.Equal(before, Snapshot());
        return facts;

        string[] Snapshot() => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(p => Path.GetRelativePath(root, p) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
    }
}
