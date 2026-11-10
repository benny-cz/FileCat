using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
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
/// A real creation process fails its rebuild and is killed before or during the exact discard of its unpublished files. The
/// interrupted journal then goes through the production Unix discard recovery and the real approval dialog. Opening the
/// review writes nothing; approval removes exactly the recorded objects and the private folder and closes the journal.
/// </summary>
public sealed class ArchiveDiscardNativeUiTests
{
    [AvaloniaTheory]
    [InlineData(true, "after:T:Rebuilding")]
    [InlineData(false, "after:T:Rebuilding")]
    [InlineData(true, "after:D:DeleteIntent:1")]
    public async Task Actual_Unix_unpublished_creation_is_discarded_through_the_real_approval_dialog(bool approve, string point)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Native Unix archive discard recovery.");
        var (services, vm, window, appRoot) = AccessibilityTests.OpenMainWindow();
        string root = UnixFiles.RealPath(Directory.CreateTempSubdirectory("filecat-archive-discard-ui-").FullName)
            ?? throw new IOException("Owned fixture path unavailable.");
        Task<bool>? operation = null;
        try
        {
            if (typeof(MainWindow).GetField("_interruptedStartup", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) is Task startup) await startup;
            File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
            string source = Path.Combine(root, "source.txt"); File.WriteAllText(source, "never archived");
            File.SetUnixFileMode(source, (UnixFileMode)0x180); File.SetLastWriteTimeUtc(source, DateTime.UnixEpoch.AddSeconds(1700000000));
            await KillCreationProcess(root, "discard:" + point);
            string originalJournal = Directory.GetFiles(Path.Combine(root, "journals"), "*.fcj").Single();
            string journal = Path.Combine(services.Paths.JournalDirectory, Path.GetFileName(originalJournal)); File.Copy(originalJournal, journal);
            var job = Assert.Single(JournalRecovery.Scan(services.Paths.JournalDirectory));
            Assert.True(ArchiveDiscardReview.CanOffer(job));
            Assert.Equal("An archive change stopped before it was saved, so the archive was not changed by it. FileCat's unfinished files for it are still kept; review removes them exactly.",
                InterruptedRecoveryReport.ArchiveSummary(job));
            string privateDirectory = Assert.Single(Directory.GetDirectories(root, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
            string[] privateBefore = Directory.GetFiles(privateDirectory).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(point == "after:T:Rebuilding" ? 3 : 2, privateBefore.Length); // The stage was already removed at DeleteIntent 1.
            string journalBefore = Hash(journal); var journalInfo = Stat(journal);

            vm.Operations.LoadInterrupted([job]); vm.Operations.IsOpen = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var row = Assert.Single(vm.Operations.Interrupted); Assert.True(row.CanCleanup);
            operation = vm.CleanupInterruptedAsync(job);
            await Wait(() => Button("Remove unfinished files") is not null, vm); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Cancel", Assert.IsType<Button>(window.FocusManager!.GetFocusedElement()).Content);
            // The review only observed: the journal (held under its exclusive flock) and every private object are unchanged.
            Assert.Equal(journalInfo, Stat(journal));
            Assert.Equal(privateBefore, Directory.GetFiles(privateDirectory).Order(StringComparer.Ordinal).ToArray());
            Assert.True(Text("This new archive was never saved."));
            Assert.True(Text("Not created: "));
            Assert.IsType<Button>(Button(approve ? "Remove unfinished files" : "Cancel")).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Equal(approve, await operation);

            Assert.Equal(!approve, Directory.Exists(privateDirectory));
            Assert.Equal(approve, JobJournal.ReadAll(journal).Any(record => record.Type == "end"));
            if (approve)
            {
                var resolved = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal).Discards);
                Assert.True(resolved.IsResolved);
                var record = Assert.Single(RecoveryReviewStore.Load(services.Paths.JournalDirectory));
                Assert.True(record.ClosureConfirmed);
                Assert.Contains("All unfinished-file outcomes are recorded.", record.Report);
                Assert.Contains(vm.RecentNotifications, notification => notification.Message == "Removed the unfinished files of an archive change that was never saved.");
            }
            else
            {
                Assert.Equal(journalBefore, Hash(journal));
                Assert.Equal(privateBefore, Directory.GetFiles(privateDirectory).Order(StringComparer.Ordinal).ToArray());
            }
            Assert.False(File.Exists(Path.Combine(root, "created.tar")));
            Assert.Equal("never archived", File.ReadAllText(source));
            Button? Button(string text) => window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsVisible && button.Content as string == text);
            // "Key: value" review lines are styled inlines, so their text is not in TextBlock.Text.
            bool Text(string text) => window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.IsVisible &&
                (block.Text ?? string.Concat(block.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text) ?? [])).Contains(text, StringComparison.Ordinal));
        }
        finally
        {
            vm.StopEditSessions();
            if (operation is not null) { try { await operation.WaitAsync(TimeSpan.FromSeconds(15)); } catch (OperationCanceledException) { } }
            AccessibilityTests.Close(services, window, appRoot);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    private static (long Length, DateTime Written) Stat(string path) { var info = new FileInfo(path); return (info.Length, info.LastWriteTimeUtc); }

    private static async Task Wait(Func<bool> predicate, FileCat.App.ViewModels.MainViewModel vm)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate())
        {
            Dispatcher.UIThread.RunJobs();
            if (timer.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("The unfinished archive files review did not open. Notifications: " +
                    string.Join(" | ", vm.RecentNotifications.Select(notification => notification.Message)));
            await Task.Delay(10);
        }
    }

    private static async Task KillCreationProcess(string root, string point)
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
        var errors = process.StandardError.ReadToEndAsync();
        string expected = point["discard:".Length..];
        try
        {
            string? actual = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(120));
            Assert.True(actual is not null && actual.StartsWith("archive-unix-creation-checkpoint:" + expected, StringComparison.Ordinal),
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
