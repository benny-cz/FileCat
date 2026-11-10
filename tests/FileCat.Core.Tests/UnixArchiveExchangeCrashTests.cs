using System.Diagnostics;
using System.Formats.Tar;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    // TAR update preparation has four sealed sidecars, so cleanup covers five objects (sidecars, then the stage slot).
    public static IEnumerable<object[]> ExchangeCrashPoints()
    {
        foreach (string point in new[] { "after:Reserved", "after:MetadataApplied", "after:ExchangeIntent", "before:Exchanged:0", "after:Exchanged" })
            yield return [point];
        for (int i = 0; i < 5; i++) { yield return [$"after:CleanupIntent:{i}"]; yield return [$"before:Cleaned:{i + 1}"]; }
        yield return ["before:Completed"];
    }

    [Theory, MemberData(nameof(ExchangeCrashPoints))]
    public async Task Real_exchange_update_process_crashes_recover_to_exactly_one_version_and_exact_cleanup(string point)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string target = Path.Combine(root, "original.tar");
        using (var output = File.Create(target))
        using (var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: false))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "kept.txt") { DataStream = new MemoryStream("kept member"u8.ToArray()),
                ModificationTime = DateTimeOffset.FromUnixTimeSeconds(1700000000), Mode = (UnixFileMode)0x1a4 };
            writer.WriteEntry(entry);
        }
        File.SetUnixFileMode(target, (UnixFileMode)0x1a0);
        string added = Path.Combine(root, "added.bin"); File.WriteAllBytes(added, "added by the crashed update"u8.ToArray());
        File.SetUnixFileMode(added, (UnixFileMode)0x180); File.SetLastWriteTimeUtc(added, DateTime.UnixEpoch.AddSeconds(1700000002));
        byte[] original = File.ReadAllBytes(target);
        string sentinel = Path.Combine(root, "sentinel"); File.WriteAllText(sentinel, "owned unchanged exchange sentinel");

        await KillExchangeProcess(root, point, ct);

        var job = Assert.Single(JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
        var interrupted = JournalRecovery.ReadArchiveTransactions(job.JournalPath); Assert.Null(interrupted.Problem);
        var facts = Assert.Single(interrupted.ExchangeUpdates);
        byte[] visible = File.ReadAllBytes(target);
        bool swapped = point is not ("after:Reserved" or "after:MetadataApplied" or "after:ExchangeIntent");
        Assert.Equal(swapped, !visible.AsSpan().SequenceEqual(original));
        Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(target));
        if (swapped) Assert.Equal(facts.Stage.Sha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(visible)));
        Assert.True(interrupted.RequiresReview);

        // The review inspection only observes: it classifies the names and lists the remaining objects, writing nothing.
        // It runs in its own recovery session; the journal (exclusively flocked while a session is open) is compared after.
        string journalBefore = Hash(File.ReadAllBytes(job.JournalPath));
        using (var inspecting = UnixArchiveExchangeRecovery.Open(job.JournalPath, facts.Nonce, ct))
        {
            var inspection = inspecting.Inspect(ct);
            Assert.Equal(swapped, inspection.Published);
            Assert.Equal(facts.CleanupCount - facts.CleanedCount, inspection.Present.Count + inspection.Absent.Count);
            Assert.Equal(point.StartsWith("before:Cleaned:", StringComparison.Ordinal) ? 1 : 0, inspection.Absent.Count);
            Assert.All(inspection.Present, path => Assert.True(File.Exists(path), path));
        }
        Assert.Equal(journalBefore, Hash(File.ReadAllBytes(job.JournalPath)));

        using (var recovery = UnixArchiveExchangeRecovery.Open(job.JournalPath, facts.Nonce, ct))
        {
            var resolved = recovery.Resume(ct);
            Assert.Equal(ArchiveExchangeUpdatePhase.Completed, resolved.Phase);
            Assert.Equal(swapped, resolved.WasPublished);
            recovery.Finish("The interrupted Unix exchange update was reconciled and cleaned exactly.");
        }

        Assert.False(JournalRecovery.ReadArchiveTransactions(job.JournalPath).RequiresReview);
        Assert.Equal(visible, File.ReadAllBytes(target));
        Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(target));
        Assert.Empty(Directory.GetDirectories(root, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
        Assert.Equal("owned unchanged exchange sentinel", File.ReadAllText(sentinel));
        Assert.Equal(new[] { "added.bin", "original.tar", "sentinel" }, Directory.GetFiles(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    private static async Task KillExchangeProcess(string root, string point, CancellationToken ct)
    {
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "FileCat.slnx"))) checkout = checkout.Parent;
        Assert.NotNull(checkout);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string harness = Path.Combine(checkout.FullName, "tests", "FileCat.CoreCrashHarness", "bin", configuration, "net10.0", "FileCat.CoreCrashHarness.dll");
        string probe = Path.Combine(checkout.FullName, "tests", "FileCat.WorkerProbe", "bin", configuration, "net10.0", "FileCat.WorkerProbe.dll");
        Assert.True(File.Exists(harness), harness); Assert.True(File.Exists(probe), probe);
        string runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
        var start = new ProcessStartInfo(Path.Combine(runtime, "dotnet"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { harness, "--archive-unix-exchange-crash", root, point, probe }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            string? actual = await process.StandardOutput.ReadLineAsync(timeout.Token);
            string expected = "archive-unix-exchange-checkpoint:" + (point.Split(':').Length == 2 && point.StartsWith("before", StringComparison.Ordinal) ? point + ":" : point);
            Assert.True(actual is not null && actual.StartsWith("archive-unix-exchange-checkpoint:" + point, StringComparison.Ordinal),
                "Missing checkpoint " + expected + ": " + actual + (process.HasExited ? " stderr: " + await errors : ""));
            Assert.False(process.HasExited); process.Kill(entireProcessTree: true); await process.WaitForExitAsync(timeout.Token);
            Assert.NotEqual(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
        }
    }
}
