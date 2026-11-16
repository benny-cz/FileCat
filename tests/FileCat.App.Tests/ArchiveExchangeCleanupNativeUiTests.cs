using System.Diagnostics;
using System.Formats.Tar;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

/// <summary>
/// A real update process is killed at one durable exchange record; the interrupted journal then goes through the production
/// Unix recovery, the real approval dialog and the exact cleanup. Inspection must not write anything before approval.
/// </summary>
public sealed class ArchiveExchangeCleanupNativeUiTests
{
    [AvaloniaTheory]
    [InlineData(true, "after:ExchangeIntent")]
    [InlineData(true, "after:Exchanged")]
    [InlineData(false, "after:Exchanged")]
    [InlineData(true, "before:Cleaned:3")]
    public async Task Actual_Unix_exchange_update_cleanup_uses_the_production_recovery_through_the_real_approval_dialog(bool approve, string point)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Native Unix archive exchange recovery.");
        var (services, vm, window, appRoot) = AccessibilityTests.OpenMainWindow();
        string root = UnixFiles.RealPath(Directory.CreateTempSubdirectory("filecat-archive-exchange-ui-").FullName)
            ?? throw new IOException("Owned fixture path unavailable.");
        Task<bool>? operation = null;
        try
        {
            if (typeof(MainWindow).GetField("_interruptedStartup", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) is Task startup) await startup;
            File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
            string archive = Path.Combine(root, "original.tar");
            byte[] original = CreateFixture(root, archive);
            await KillUpdateProcess(root, point);
            bool published = point != "after:ExchangeIntent";
            string originalJournal = Directory.GetFiles(Path.Combine(root, "journals"), "*.fcj").Single();
            string journal = Path.Combine(services.Paths.JournalDirectory, Path.GetFileName(originalJournal)); File.Copy(originalJournal, journal);
            var job = Assert.Single(JournalRecovery.Scan(services.Paths.JournalDirectory));
            var facts = Assert.Single(job.ArchiveTransactions.ExchangeUpdates);
            Assert.True(ArchiveExchangeCleanupReview.CanOffer(job));
            Assert.Equal(point switch
            {
                "after:ExchangeIntent" => "An archive update was interrupted before its outcome was recorded. Review checks which version is current, keeps it and removes only that update's private files.",
                _ => "The archive was updated. The previous version and this update's private files are still kept; review removes them exactly.",
            }, InterruptedRecoveryReport.ArchiveSummary(job));
            string privateDirectory = Assert.Single(Directory.GetDirectories(root, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
            string[] privateBefore = Directory.GetFiles(privateDirectory).Order(StringComparer.Ordinal).ToArray();
            int cleanedBefore = facts.CleanedCount;
            Assert.Equal(point == "before:Cleaned:3" ? 2 : 0, cleanedBefore);
            string before = Hash(archive), journalBefore = Hash(journal); var journalInfo = Stat(journal);
            Assert.Equal(published, before != Convert.ToHexString(SHA256.HashData(original)));

            vm.Operations.LoadInterrupted([job]); vm.Operations.IsOpen = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var row = Assert.Single(vm.Operations.Interrupted); Assert.True(row.CanCleanup);
            operation = vm.CleanupInterruptedAsync(job);
            await Wait(() => Button("Keep archive and clean up") is not null, vm); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Cancel", Assert.IsType<Button>(window.FocusManager!.GetFocusedElement()).Content);
            // The review only observed: archive, journal and every private object are exactly as the crash left them.
            // The open review holds the journal's exclusive flock, so compare its append-only length and times here.
            Assert.Equal(before, Hash(archive)); Assert.Equal(journalInfo, Stat(journal));
            Assert.Equal(privateBefore, Directory.GetFiles(privateDirectory).Order(StringComparer.Ordinal).ToArray());
            Assert.Equal(published, Text("Removal includes the backup from before this operation."));
            Assert.Equal(point == "before:Cleaned:3", Text("1 retained file(s) were already absent"));
            Assert.IsType<Button>(Button(approve ? "Keep archive and clean up" : "Cancel")).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Equal(approve, await operation);

            Assert.Equal(before, Hash(archive));
            Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(archive));
            Assert.Equal(!approve, Directory.Exists(privateDirectory));
            Assert.Equal(approve, JobJournal.ReadAll(journal).Any(record => record.Type == "end"));
            if (approve)
            {
                var resolved = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal).ExchangeUpdates);
                Assert.Equal(ArchiveExchangeUpdatePhase.Completed, resolved.Phase); Assert.Equal(published, resolved.WasPublished);
                Assert.Equal(resolved.CleanupCount, resolved.CleanedCount);
                var record = Assert.Single(RecoveryReviewStore.Load(services.Paths.JournalDirectory));
                Assert.True(record.ClosureConfirmed);
                Assert.Contains("All private files of this update are removed.", record.Report);
                Assert.Contains(published ? "(update published)" : "(update not published)", record.Report);
            }
            else
            {
                Assert.Equal(journalBefore, Hash(journal));
                Assert.Equal(privateBefore, Directory.GetFiles(privateDirectory).Order(StringComparer.Ordinal).ToArray());
            }
            Assert.Equal("owned unchanged exchange sentinel", File.ReadAllText(Path.Combine(root, "sentinel")));
            Button? Button(string text) => window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsVisible && button.Content as string == text);
            bool Text(string text) => window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.IsVisible && block.Text?.Contains(text, StringComparison.Ordinal) == true);
        }
        finally
        {
            vm.StopEditSessions();
            if (operation is not null) { try { await operation.WaitAsync(TimeSpan.FromSeconds(15)); } catch (OperationCanceledException) { } }
            AccessibilityTests.Close(services, window, appRoot);
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] CreateFixture(string root, string archive)
    {
        using (var output = File.Create(archive))
        using (var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: false))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "kept.txt") { DataStream = new MemoryStream("kept member"u8.ToArray()),
                ModificationTime = DateTimeOffset.FromUnixTimeSeconds(1700000000), Mode = (UnixFileMode)0x1a4 };
            writer.WriteEntry(entry);
        }
        File.SetUnixFileMode(archive, (UnixFileMode)0x1a0);
        string added = Path.Combine(root, "added.bin"); File.WriteAllBytes(added, "added by the crashed update"u8.ToArray());
        File.SetUnixFileMode(added, (UnixFileMode)0x180); File.SetLastWriteTimeUtc(added, DateTime.UnixEpoch.AddSeconds(1700000002));
        File.WriteAllText(Path.Combine(root, "sentinel"), "owned unchanged exchange sentinel");
        return File.ReadAllBytes(archive);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static (long Length, DateTime Written) Stat(string path) { var info = new FileInfo(path); return (info.Length, info.LastWriteTimeUtc); }

    private static async Task Wait(Func<bool> predicate, FileCat.App.ViewModels.MainViewModel vm)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate())
        {
            Dispatcher.UIThread.RunJobs();
            if (timer.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("Unix archive exchange cleanup approval did not open. Notifications: " +
                    string.Join(" | ", vm.RecentNotifications.Select(notification => notification.Message)));
            await Task.Delay(10);
        }
    }

    private static async Task KillUpdateProcess(string root, string point)
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
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            string? actual = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(120));
            Assert.True(actual is not null && actual.StartsWith("archive-unix-exchange-checkpoint:" + point, StringComparison.Ordinal),
                "Missing checkpoint " + point + ": " + actual + (process.HasExited ? " stderr: " + await errors : ""));
            Assert.False(process.HasExited); process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.NotEqual(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        }
    }
}
