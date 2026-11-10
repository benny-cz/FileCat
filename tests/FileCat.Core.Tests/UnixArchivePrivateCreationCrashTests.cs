using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    // A new TAR has four sealed sidecars (expected, origin evidence, verified, creation metadata): reviewed counts 0..4.
    public static IEnumerable<object[]> CreationCleanupCrashPoints()
    {
        yield return ["after:C:Cataloged"];
        for (int i = 0; i < 4; i++) { yield return [$"after:C:DeleteIntent:{i}"]; yield return [$"before:C:Deleted:{i + 1}"]; }
        yield return ["before:C:Completed"];
        yield return ["before:T:Committed"];
    }

    [Theory, MemberData(nameof(CreationCleanupCrashPoints))]
    public async Task Real_private_creation_process_crashes_after_publication_recover_to_exact_cleanup(string point)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string source = Path.Combine(root, "source.txt"); File.WriteAllText(source, "created by a crashed process");
        File.SetUnixFileMode(source, (UnixFileMode)0x180); File.SetLastWriteTimeUtc(source, DateTime.UnixEpoch.AddSeconds(1700000000));
        string target = Path.Combine(root, "created.tar");

        await KillCreationProcess(root, point, ct);

        var job = Assert.Single(JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
        var interrupted = JournalRecovery.ReadArchiveTransactions(job.JournalPath); Assert.Null(interrupted.Problem);
        var catalog = Assert.Single(interrupted.CreationCleanups);
        byte[] published = File.ReadAllBytes(target);
        Assert.Equal(catalog.Target.Sha256, Hash(published));
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(target));
        // A completed cleanup catalog already resolves the creation; only its journal still lacks an end record.
        Assert.Equal(point != "before:T:Committed", interrupted.RequiresReview);

        using (var lease = UnixArchiveCreationCleanupRecovery.OpenCurrentLease(job.JournalPath, catalog.Nonce, ct))
        using (var cleanup = new ArchiveCreationCleanup(lease))
        {
            Assert.True(cleanup.Execute(ct).IsResolved);
            cleanup.Finish("The interrupted Unix creation cleanup was reconciled exactly.");
        }

        Assert.False(JournalRecovery.ReadArchiveTransactions(job.JournalPath).RequiresReview);
        Assert.Equal(published, File.ReadAllBytes(target));
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(target));
        Assert.Empty(Directory.GetDirectories(root, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
        Assert.Equal(new[] { "created.tar", "source.txt" }, Directory.GetFiles(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("created by a crashed process", File.ReadAllText(source));
    }

    [Theory]
    [InlineData("after:T:ReadyToPublish", false)]
    [InlineData("after:T:Publishing", false)]
    [InlineData("before:T:Reconciling", true)]
    public async Task Real_private_creation_process_crashes_before_cataloged_cleanup_keep_every_object(string point, bool claimed)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string source = Path.Combine(root, "source.txt"); File.WriteAllText(source, "created by a crashed process");
        File.SetUnixFileMode(source, (UnixFileMode)0x180);

        await KillCreationProcess(root, point, ct);

        var job = Assert.Single(JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
        var interrupted = JournalRecovery.ReadArchiveTransactions(job.JournalPath); Assert.Null(interrupted.Problem);
        var facts = Assert.Single(interrupted.Transactions);
        Assert.True(interrupted.RequiresReview); Assert.Empty(interrupted.CreationCleanups);
        string privateFolder = Assert.Single(Directory.GetDirectories(root, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
        Assert.Equal(Path.GetDirectoryName(facts.StagePath), privateFolder);
        // Nothing was lost or replaced: the archive is absent before the move and exactly the verified stage after it.
        Assert.Equal(claimed, File.Exists(Path.Combine(root, "created.tar")));
        if (claimed) Assert.Equal(facts.VerifiedStage!.Sha256, Hash(File.ReadAllBytes(Path.Combine(root, "created.tar"))));
        else Assert.Equal(facts.VerifiedStage!.Sha256, Hash(File.ReadAllBytes(facts.StagePath)));
        Assert.All(facts.Sidecars.Where(sidecar => sidecar.Version is not null), sidecar => Assert.True(File.Exists(sidecar.Path), sidecar.Path));
        var refusal = Assert.Throws<ArchiveWriteException>(() => UnixArchiveCreationCleanupRecovery.OpenCurrentLease(job.JournalPath, facts.Nonce, ct).Dispose());
        Assert.Equal(ArchiveWriteReason.NeedsReview, refusal.Reason);
        Assert.Equal("created by a crashed process", File.ReadAllText(source));
    }

    private static async Task KillCreationProcess(string root, string point, CancellationToken ct)
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
        foreach (string argument in new[] { harness, "--archive-unix-creation-crash", root, point, probe }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            string? actual = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.True(actual is not null && actual.StartsWith("archive-unix-creation-checkpoint:" + point, StringComparison.Ordinal),
                "Missing checkpoint " + point + ": " + actual + (process.HasExited ? " stderr: " + await errors : ""));
            Assert.False(process.HasExited); process.Kill(entireProcessTree: true); await process.WaitForExitAsync(timeout.Token);
            Assert.NotEqual(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
        }
    }
}
