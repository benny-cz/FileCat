using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

public sealed class ArchiveRetainedCleanupNativeUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_NTFS_archive_cleanup_uses_the_production_adapter_through_the_real_approval_dialog(bool approve)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1")
            Assert.Skip("Run the explicit Windows native archive recovery UI lane.");
        var (services, vm, window, appRoot) = AccessibilityTests.OpenMainWindow();
        string root = Directory.CreateTempSubdirectory("filecat-archive-cleanup-ui-").FullName;
        Task<bool>? operation = null;
        try
        {
            if (typeof(MainWindow).GetField("_interruptedStartup", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) is Task startup) await startup;
            await CreateInterruptedNativeUpdate(root);
            string originalJournal = Directory.GetFiles(Path.Combine(root, "journals"), "*.fcj").Single();
            string journal = Path.Combine(services.Paths.JournalDirectory, Path.GetFileName(originalJournal)); File.Copy(originalJournal, journal);
            var job = Assert.Single(JournalRecovery.Scan(services.Paths.JournalDirectory));
            var content = Assert.Single(job.ArchiveTransactions.ContentUpdates); var catalog = Assert.Single(job.ArchiveTransactions.ContentCleanups);
            string archive = Path.Combine(root, "original.tar"), before = Hash(archive), journalBefore = Hash(journal);
            byte[] origin = File.ReadAllBytes(archive + ":Zone.Identifier");
            vm.Operations.LoadInterrupted([job]); vm.Operations.IsOpen = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var row = Assert.Single(vm.Operations.Interrupted); Assert.True(row.CanCleanup);
            operation = vm.CleanupInterruptedAsync(job);
            await Wait(() => Button("Keep archive and clean up") is not null); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Cancel", Assert.IsType<Button>(window.FocusManager!.GetFocusedElement()).Content);
            Assert.Equal(before, Hash(archive)); Assert.Equal(journalBefore, Hash(journal));
            Assert.All(catalog.Artifacts, file => Assert.True(File.Exists(file.Path)));
            Assert.IsType<Button>(Button(approve ? "Keep archive and clean up" : "Cancel")).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Equal(approve, await operation);
            Assert.Equal(before, Hash(archive)); Assert.Equal(origin, File.ReadAllBytes(archive + ":Zone.Identifier"));
            Assert.All(catalog.Artifacts, file => Assert.Equal(!approve, File.Exists(file.Path)));
            Assert.Equal(approve, JobJournal.ReadAll(journal).Any(record => record.Type == "end"));
            if (approve)
            {
                var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal).ContentRecoveries);
                Assert.True(facts.IsResolved); Assert.Equal(0u, facts.AbsentMask); Assert.Equal(63u, facts.DeletedMask);
                Assert.True(Assert.Single(RecoveryReviewStore.Load(services.Paths.JournalDirectory)).ClosureConfirmed);
            }
            else Assert.Equal(journalBefore, Hash(journal));
            Assert.Equal(content.PrivateOutput!.Sha256, before);
            Button? Button(string text) => window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsVisible && button.Content as string == text);
        }
        finally
        {
            vm.StopEditSessions();
            if (operation is not null) { try { await operation.WaitAsync(TimeSpan.FromSeconds(15)); } catch (OperationCanceledException) { } }
            AccessibilityTests.Close(services, window, appRoot);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Hash(string path)
    { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static async Task Wait(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate())
        { Dispatcher.UIThread.RunJobs(); if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Native archive cleanup approval did not open."); await Task.Delay(10); }
    }
    private static async Task CreateInterruptedNativeUpdate(string root)
    {
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "FileCat.slnx"))) checkout = checkout.Parent;
        Assert.NotNull(checkout);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string harness = Path.Combine(checkout.FullName, "tests", "FileCat.CoreCrashHarness", "bin", configuration, "net10.0", "FileCat.CoreCrashHarness.dll");
        string probe = Path.Combine(checkout.FullName, "tests", "FileCat.WorkerProbe", "bin", configuration, "net10.0", "FileCat.WorkerProbe.dll");
        string runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
        var start = new ProcessStartInfo(Path.Combine(runtime, "dotnet.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { harness, "--archive-content-crash", root, "committed-recorded", typeof(FileCat.Platform.Windows.Archives.WindowsArchiveDirectory).Assembly.Location, probe }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!; var errors = process.StandardError.ReadToEndAsync();
        try
        {
            string? checkpoint = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(45));
            Assert.True(checkpoint == "archive-content-checkpoint:committed-recorded", "Missing native update checkpoint: " + checkpoint);
            process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); Assert.Equal("", await errors);
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); } }
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var held = new List<FileStream>();
            try
            { foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) held.Add(new(path, FileMode.Open, FileAccess.Read, FileShare.None)); break; }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 && elapsed.Elapsed < TimeSpan.FromSeconds(5)) { await Task.Delay(25); }
            finally { foreach (var file in held) file.Dispose(); }
        }
    }
}
