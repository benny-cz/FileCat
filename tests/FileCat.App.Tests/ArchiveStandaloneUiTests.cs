using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Commands;
using FileCat.Core.Edit;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Fixture = FileCat.App.Tests.ArchiveCreationWorkflowTests.Fixture;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>
/// AW7 through the real dialogs: "Compress file" makes one stream named after the whole file, several items get the TAR
/// alternative, F5 of one file explicitly replaces the compressed contents, F4 commits the sole payload, and folder, rename
/// and delete commands inside the compressed file stay unavailable with the reason.
/// </summary>
public sealed class ArchiveStandaloneUiTests
{
    public static TheoryData<ArchiveWriteFormat> Formats => new() { ArchiveWriteFormat.Gzip, ArchiveWriteFormat.Bzip2, ArchiveWriteFormat.Zstd };

    [AvaloniaTheory, MemberData(nameof(Formats))]
    public async Task Compress_file_names_the_stream_after_the_whole_file_and_plans_one_synthetic_member(ArchiveWriteFormat format)
    {
        using var f = await Fixture.Open(); var operation = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready();
        var definition = ArchiveCreationFormat.All.Single(value => value.Format == format);
        f.SelectFormat(format); Assert.EndsWith(Path.DirectorySeparatorChar + "a.txt" + definition.Extension, f.Input("Archive path").Text);
        f.SelectFormat(ArchiveWriteFormat.TarGzip); Assert.EndsWith(Path.DirectorySeparatorChar + "a.tar.gz", f.Input("Archive path").Text);
        f.SelectFormat(ArchiveWriteFormat.Zip); Assert.EndsWith(Path.DirectorySeparatorChar + "a.zip", f.Input("Archive path").Text);
        f.SelectFormat(format); Assert.EndsWith(Path.DirectorySeparatorChar + "a.txt" + definition.Extension, f.Input("Archive path").Text);
        await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == true); f.Click("Pack"); await f.Complete(operation);
        var job = Assert.Single(f.Services.Jobs.Jobs); await Wait(() => job.State.IsFinished());
        var plan = f.Backends.Single(value => value.Format == format).Plan!;
        Assert.Equal(format, plan.Profile!.Format); Assert.Null(plan.Baseline);
        var change = Assert.Single(plan.Changes);
        Assert.Equal((ArchiveChangeKind.AddFile, ArchiveStandalone.MemberName, Path.Combine(f.Files, "a.txt")), (change.Kind, change.MemberPath, change.SourcePath));
        Assert.Contains("Compress “a.txt” to “a.txt" + definition.Extension + "”", job.Title + " " + job.Request.Description);
    }

    [AvaloniaFact]
    public async Task Several_items_get_the_TAR_alternative_and_a_TAR_name_is_refused_for_a_compressed_file()
    {
        using var f = await Fixture.Open(); var listing = f.Vm.ActiveTab!.Listing;
        for (int i = 0; i < listing.VisibleCount; i++) if (listing.GetVisible(i).Name is "a.txt" or "b.txt") listing.SetMark(i, true);
        var operation = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready();
        f.SelectFormat(ArchiveWriteFormat.Gzip);
        await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == false);
        Assert.Contains("A compressed file holds exactly one file. To pack several items or a folder, choose TAR + gzip.", f.Text());
        f.SelectFormat(ArchiveWriteFormat.Zstd); await Wait(() => f.Text().Contains("choose TAR + Zstandard.", StringComparison.Ordinal));
        f.SelectFormat(ArchiveWriteFormat.TarGzip); await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == true);
        f.Click("Cancel"); await f.Complete(operation); Assert.Empty(f.Services.Jobs.Jobs);

        for (int i = 0; i < listing.VisibleCount; i++) listing.SetMark(i, false);
        Assert.True(listing.FocusName("a.txt"));
        var single = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(ArchiveWriteFormat.Gzip);
        f.Input("Archive path").Text = Path.Combine(f.Files, "notes.tar.gz");
        await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == false);
        Assert.Contains("The name ends in .tar.gz, which names a TAR + gzip archive.", f.Text());
        f.Input("Archive path").Text = Path.Combine(f.Files, "notes.gz"); await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == true);
        f.Click("Cancel"); await f.Complete(single); Assert.Empty(f.Services.Jobs.Jobs);
    }

    // The actual Unix job route with real worker processes (the published probe), the real provider and the real dialogs.
    [AvaloniaTheory, MemberData(nameof(Formats))]
    public async Task Actual_Unix_workers_compress_replace_and_commit_one_file(ArchiveWriteFormat format)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Native Unix standalone compression.");
        string probe = Probe();
        await Workflow(format, FileCat.Core.Archives.Unix.UnixArchiveJobBackend.Create(format, (nonce, _) => ProbeWorker.Start(probe, nonce, "archive-assess-v2"),
            (nonce, _) => ProbeWorker.Start(probe, nonce, "archive-transform-v2"), (nonce, _) => ProbeWorker.Start(probe, nonce, "archive-verify-v2")));
    }

    // The Windows lane: AppContainer workers from the published application and TxF publication.
    [AvaloniaTheory, MemberData(nameof(Formats))]
    public async Task Actual_Windows_workers_compress_replace_and_commit_one_file(ArchiveWriteFormat format)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1")
            Assert.Skip("Run the explicit standard-user native standalone UI lane.");
        string assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS") ?? throw new InvalidOperationException("Current native assets required.");
        FileCat.Platform.Windows.Shell.ArchiveAppContainer.ProvisionRuntimeReadAccess(assets);
        await Workflow(format, FileCat.Platform.Windows.Archives.WindowsArchiveJobBackend.Create(format, Path.Combine(assets, "FileCat.exe")));
    }

    private static async Task Workflow(ArchiveWriteFormat format, IArchiveJobBackend backend)
    {
        using var f = await Fixture.Open(customBackends: new([backend]), nativeWindowsOriginMarks: OperatingSystem.IsWindows());
        f.Services.Settings.Editor = new ToolDefinition { Executable = Path.Combine(f.Root, "owned-missing-editor" + (OperatingSystem.IsWindows() ? ".exe" : "")) };
        foreach (string name in new[] { "a.txt", "b.txt" }) File.SetLastWriteTimeUtc(Path.Combine(f.Files, name), DateTime.UnixEpoch.AddSeconds(1700000000));
        var definition = ArchiveCreationFormat.All.Single(value => value.Format == format);

        // Compress file: one stream named after the whole file.
        var create = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(format);
        await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == true); f.Click("Pack"); await f.Complete(create);
        var creation = Assert.Single(f.Services.Jobs.Jobs); await Finished(creation);
        string path = creation.Request.Archive!.ArchivePath;
        Assert.Equal("a.txt" + definition.Extension, Path.GetFileName(path));
        Assert.Equal("alpha", Read(path, format)); Assert.Equal("alpha", File.ReadAllText(Path.Combine(f.Files, "a.txt")));
        Assert.Empty(JournalRecovery.Scan(f.Services.Paths.JournalDirectory));

        // Packing onto the existing compressed file asks to replace its contents after checking it; Enter, still
        // repeating from Pack if held, cancels that question and the contents stay.
        Assert.True(f.Vm.ActiveTab!.Listing.FocusName("a.txt"));
        var again = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(format);
        // The job records the resolved path (macOS: /private/var); the dialog shows the folder as the panel names it.
        Assert.Equal(Path.GetFileName(path), Path.GetFileName(f.Input("Archive path").Text));
        await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == true); f.Click("Pack");
        await HeldEnterCancelsReplacement(f, again);
        Assert.Single(f.Services.Jobs.Jobs); Assert.Equal("alpha", Read(path, format));

        // Inside the compressed file: its one member carries the outer name; nothing else can be created, renamed or removed.
        var source = f.Vm.Workspace.Panels[0].ActiveTab!; var target = f.Vm.Workspace.Panels[1].ActiveTab!;
        var archive = ArchiveProvider.ForFile(path);
        source.Navigate(Location.FileSystem(f.Files)); target.Navigate(archive); f.Vm.Workspace.Activate(target.Panel);
        await Wait(() => source.Listing.State == ListingState.Complete && target.Listing.State == ListingState.Complete && target.Listing.FocusName("a.txt"));
        Assert.Equal(1, target.Listing.VisibleCount - (target.Listing.GetVisible(0).Name == ".." ? 1 : 0));
        foreach (string id in new[] { CommandIds.MakeDirectory, CommandIds.Rename, CommandIds.Delete })
        {
            var refused = f.Vm.GetAvailability(id); Assert.False(refused.Enabled, id);
            Assert.Contains("A compressed file holds exactly one file", refused.Reason);
        }
        var editable = f.Vm.GetAvailability(CommandIds.Edit); Assert.True(editable.Enabled, editable.Reason);

        // F5 of two files never broadens into an addition.
        f.Vm.Workspace.Activate(source.Panel);
        for (int i = 0; i < source.Listing.VisibleCount; i++) if (source.Listing.GetVisible(i).Name is "a.txt" or "b.txt") source.Listing.SetMark(i, true);
        await Transfer(f, null); Assert.Single(f.Services.Jobs.Jobs);
        Assert.Contains("holds exactly one file", f.Vm.Notification);
        for (int i = 0; i < source.Listing.VisibleCount; i++) source.Listing.SetMark(i, false);

        // F5 of one file: an explicit replacement of the compressed contents. Enter cancels the question; the button replaces.
        Assert.True(source.Listing.FocusName("b.txt"));
        var declined = f.Vm.ExecuteAsync(CommandIds.Copy);
        // Copy is enabled once the destination is checked; a click before that does nothing.
        await Wait(() => f.Button("Copy")?.IsEffectivelyEnabled == true || declined.IsCompleted); f.Click("Copy");
        await HeldEnterCancelsReplacement(f, declined);
        Assert.Single(f.Services.Jobs.Jobs); Assert.Equal("alpha", Read(path, format));
        await Transfer(f, "Replace contents");
        var replacement = f.Services.Jobs.Jobs.Last(); Assert.NotSame(creation, replacement); await Finished(replacement);
        var replaced = Assert.Single(replacement.Request.Archive!.Changes);
        Assert.Equal((ArchiveChangeKind.Replace, ArchiveStandalone.MemberName), (replaced.Kind, replaced.MemberPath));
        Assert.Equal("beta", Read(path, format)); Assert.Equal("beta", File.ReadAllText(Path.Combine(f.Files, "b.txt")));
        Assert.Equal(new[] { "a.txt", "a.txt" + definition.Extension, "b.txt" },
            Directory.GetFileSystemEntries(f.Files).Select(Path.GetFileName).Where(name => name!.StartsWith("a.", StringComparison.Ordinal) || name == "b.txt").Order(StringComparer.Ordinal));

        // F4: the sole payload is edited and committed; the session shows the member by its outer-file name. The member keeps
        // its name across the replacement, so "listed" alone cannot tell the refreshed listing from the one before it: list
        // the replaced archive explicitly (v14e Mac: F4 on the earlier listing is refused as stale, as it must be).
        f.Vm.Workspace.Activate(target.Panel); Dispatcher.UIThread.RunJobs(); target.Listing.Refresh();
        await Wait(() => target.Listing.State == ListingState.Complete && !target.Listing.IsRefreshing && target.Listing.FocusName("a.txt"));
        var edit = f.Vm.ExecuteAsync(CommandIds.Edit); await f.Complete(edit);
        var session = Assert.Single(f.Services.EditSessions.LoadAll());
        Assert.True(session.IsNativeArchive); Assert.Equal(ArchiveStandalone.MemberName, session.MemberPath);
        Assert.Equal("a.txt", session.MemberDisplay); Assert.Equal("a.txt", session.DisplayName); Assert.Equal("beta", File.ReadAllText(session.WorkingPath));
        File.WriteAllText(session.WorkingPath, "edited through F4");
        var commit = (Task)typeof(MainViewModel).GetMethod("CommitSessionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(f.Vm, [session])!;
        await f.Complete(commit);
        var committed = f.Services.Jobs.Jobs.Last(); Assert.NotSame(replacement, committed); await Finished(committed);
        await Wait(() => f.Services.EditSessions.LoadAll().Single().LastCommitUtc is not null);
        Assert.Equal("edited through F4", Read(path, format));
        Assert.Empty(JournalRecovery.Scan(f.Services.Paths.JournalDirectory));
        Assert.DoesNotContain(Directory.GetFileSystemEntries(f.Files), entry => Path.GetFileName(entry).StartsWith(".filecat-archive-", StringComparison.Ordinal));
    }

    private static async Task Transfer(Fixture f, string? confirm)
    {
        int before = f.Services.Jobs.Jobs.Count; var command = f.Vm.ExecuteAsync(CommandIds.Copy); var clock = Stopwatch.StartNew(); bool copied = false;
        while (!command.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(90))
        {
            f.Window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var buttons = f.Window.GetVisualDescendants().OfType<Button>().Where(button => button.IsVisible && button.IsEffectivelyEnabled).ToArray();
            var next = (!copied ? buttons.FirstOrDefault(button => Equals(button.Content, "Copy")) : null)
                ?? (confirm is null ? null : buttons.FirstOrDefault(button => Equals(button.Content, confirm)));
            if (next is not null) { copied |= Equals(next.Content, "Copy"); next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.True(command.IsCompleted, "F5 did not finish. UI: " + f.Text() + " Notice: " + f.Vm.Notification);
        await command;
        if (confirm is not null) Assert.Equal(before + 1, f.Services.Jobs.Jobs.Count);
    }

    private static string Read(string path, ArchiveWriteFormat format)
    {
        // The independent read side (not the writer's strict decoders) lists one member named after the outer file.
        var kind = format switch { ArchiveWriteFormat.Gzip => ArchiveKind.Gzip, ArchiveWriteFormat.Bzip2 => ArchiveKind.Bzip2, _ => ArchiveKind.Zstd };
        using var reader = ArchiveFormats.Open(path, kind, Path.GetFileName(path));
        var member = Assert.Single(reader.List(_ => { }, CancellationToken.None));
        Assert.Equal(Path.GetFileName(path)[..^ArchiveStandalone.Suffix(format).Length], member.Path);
        using var content = new StreamReader(reader.Open(member.Index, CancellationToken.None)); return content.ReadToEnd();
    }

    /// <summary>
    /// Plan §11.8: replacing compressed contents keeps no old copy, so its question opens on Cancel and Enter cancels it.
    /// Two Enter presses without a release stand for an Enter held from the command that led here.
    /// </summary>
    private static async Task HeldEnterCancelsReplacement(Fixture f, Task command)
    {
        var questionClock = Stopwatch.StartNew();
        while (f.Button("Replace contents") is null && !command.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs(); f.Window.UpdateLayout();
            if (questionClock.Elapsed > TimeSpan.FromSeconds(90))
                throw new TimeoutException("No replacement question. UI: " + f.Text() + " | Buttons: " +
                    string.Join(", ", f.Window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string ?? "?")) +
                    " | Notice: " + f.Vm.Notification + " | Jobs: " + string.Join(", ", f.Services.Jobs.Jobs.Select(job => job.Title + " " + job.State)));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.False(command.IsCompleted, "No replacement question. UI: " + f.Text() + " Notice: " + f.Vm.Notification);
        var replace = f.Button("Replace contents")!; var cancel = Assert.IsType<Button>(f.Button("Cancel"));
        Assert.False(replace.IsDefault); Assert.Contains("danger", replace.Classes); Assert.True(cancel.IsDefault);
        var focusClock = Stopwatch.StartNew();
        while (!ReferenceEquals(f.Window.FocusManager?.GetFocusedElement(), cancel))
        {
            Dispatcher.UIThread.RunJobs();
            if (focusClock.Elapsed > TimeSpan.FromSeconds(30))
            {
                object? focused = f.Window.FocusManager?.GetFocusedElement();
                string where = focused switch
                {
                    Button button => "button " + button.Content,
                    Control control => control.GetType().Name + " " + Avalonia.Automation.AutomationProperties.GetName(control) +
                        " in " + string.Join("/", control.GetVisualAncestors().OfType<Control>().Take(6).Select(a => a.GetType().Name)),
                    null => "nothing",
                    _ => focused.GetType().Name,
                };
                throw new TimeoutException("Focus did not reach the replacement question's Cancel; it is on " + where + ". UI: " + f.Text());
            }
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        f.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        f.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await f.Complete(command);
    }

    private static async Task Finished(Job job)
    {
        await Wait(() => job.State.IsFinished());
        // Each issue's technical detail too: the user's text names the kind of refusal, not which check made it.
        Assert.True(job.State == JobState.Completed, job.State + ": " + job.Summary + " " + JobIssue.Summarize(job.Issues) + " " +
            string.Join(" | ", job.Issues.Where(issue => issue.Details is not null).Select(issue => issue.Details)));
    }

    private static async Task Wait(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            Dispatcher.UIThread.RunJobs();
            if (clock.Elapsed > TimeSpan.FromSeconds(90)) throw new TimeoutException("The standalone compression UI did not settle.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static string Probe()
    {
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "FileCat.slnx"))) checkout = checkout.Parent;
        Assert.NotNull(checkout);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string probe = Path.Combine(checkout.FullName, "tests", "FileCat.WorkerProbe", "bin", configuration, "net10.0", "FileCat.WorkerProbe.dll");
        Assert.True(File.Exists(probe), probe); return probe;
    }

    /// <summary>A real worker process from the test probe (the same worker code as the application), never a production launch.</summary>
    private sealed class ProbeWorker(Process process, System.IO.Pipes.AnonymousPipeServerStream cancellation) : IArchiveWorkerProcess
    {
        private uint exit; private TimeSpan cpu; private bool disposed;
        private readonly Task<bool> stderr = Drain(process);
        public Stream Input => process.StandardInput.BaseStream;
        public Stream Output => process.StandardOutput.BaseStream;
        public bool HasExited => disposed || process.HasExited;
        public uint ExitCode => disposed ? exit : (uint)process.ExitCode;
        public TimeSpan TotalProcessorTime
        {
            get
            {
                if (!disposed)
                {
                    try { cpu = process.TotalProcessorTime; }
                    catch (Exception ex) when ((ex is System.ComponentModel.Win32Exception or InvalidOperationException) && process.HasExited) { }
                }
                return cpu;
            }
        }
        public bool ErrorLimitExceeded { get; private set; }
        public void RequestCancellation() => cancellation.Dispose();
        public void Kill() { if (!disposed && !process.HasExited) process.Kill(entireProcessTree: true); }
        public async ValueTask DisposeAsync()
        {
            if (disposed) return; Kill(); await process.WaitForExitAsync(); exit = (uint)process.ExitCode;
            ErrorLimitExceeded = await stderr; cancellation.Dispose(); process.Dispose(); disposed = true;
        }
        private static async Task<bool> Drain(Process child)
        {
            char[] buffer = new char[1024]; int total = 0;
            while (await child.StandardError.ReadAsync(buffer) is int count && count != 0)
            {
                total += count;
                if (total >= 64 * 1024) { if (!child.HasExited) child.Kill(entireProcessTree: true); return true; }
            }
            return total != 0;
        }
        internal static ProbeWorker Start(string probe, Guid nonce, string role)
        {
            string root = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
            var start = new ProcessStartInfo(Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string value in new[] { probe, role, nonce.ToString("D") }) start.ArgumentList.Add(value);
            var cancellation = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out, HandleInheritability.Inheritable);
            start.ArgumentList.Add(ArchiveWorkerCancellation.Argument); start.ArgumentList.Add(cancellation.GetClientHandleAsString());
            try { var process = Process.Start(start)!; cancellation.DisposeLocalCopyOfClientHandle(); return new(process, cancellation); }
            catch { cancellation.Dispose(); throw; }
        }
    }
}
