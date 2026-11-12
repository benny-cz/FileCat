using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.State;
using FileCat.Platform.Windows.Shell;
using Fixture = FileCat.App.Tests.ArchiveCreationWorkflowTests.Fixture;

namespace FileCat.App.Tests;

// Ledger rows 6 and 13 (route decision of 12 November 2026): with a composed ZIP route, real confined workers carry every
// ZIP change through the real dialogs and commands, on each platform's native lane (Windows TXF, confined Linux,
// sandboxed macOS). A ZIP the strict catalog refuses stays read-only and says why.
public sealed class ArchiveZipWorkerNativeUiTests
{
    [AvaloniaFact]
    public async Task Actual_workers_create_change_and_edit_a_ZIP_where_a_ZIP_route_is_composed()
    {
        string executable = NativeApplication();
        using var f = await Fixture.Open(customBackends: Backends(executable), nativeWindowsOriginMarks: OperatingSystem.IsWindows());
        f.Services.Settings.Editor = new ToolDefinition { Executable = Path.Combine(f.Root, "owned-missing-editor") };

        // Pack the focused a.txt into a new ZIP: the dialog takes the worker profile.
        var pack = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(ArchiveWriteFormat.Zip);
        await Wait(() => f.Button("Pack")?.IsEffectivelyEnabled == true); f.Click("Pack"); await f.Complete(pack);
        var created = Assert.Single(f.Services.Jobs.Jobs); await Finished(created);
        Assert.Equal(ArchiveWriteFormat.Zip, created.Request.Archive!.Profile?.Format);
        string path = created.Request.Archive.ArchivePath;
        Assert.Equal(new Dictionary<string, string> { ["a.txt"] = "alpha" }, Read(path));

        var source = f.Vm.ActiveTab!; var target = f.Vm.Workspace.Panels[1].ActiveTab!; var zip = ZipProvider.ForFile(path);
        await Show(zip);

        // F5 b.txt into it.
        f.Vm.Workspace.Activate(source.Panel); source.Navigate(Core.Resources.Location.FileSystem(f.Files));
        await Wait(() => source.Listing.State == ListingState.Complete && source.Listing.FocusName("b.txt"));
        var copy = f.Vm.ExecuteAsync(CommandIds.Copy);
        await Wait(() => f.Button("Copy")?.IsEffectivelyEnabled == true && f.FindInput("Destination") is not null);
        f.Click("Copy"); await f.Complete(copy); await Finished(f.Services.Jobs.Jobs.Last());
        Assert.Equal(new Dictionary<string, string> { ["a.txt"] = "alpha", ["b.txt"] = "beta" }, Read(path));

        // F7, F2 and F8 inside it.
        f.Vm.Workspace.Activate(target.Panel); await Show(zip);
        var mkdir = f.Vm.ExecuteAsync(CommandIds.MakeDirectory); await Wait(() => f.Button("Create") is not null);
        f.Input("Folder name (use / for nested folders):").Text = "dir/sub"; Dispatcher.UIThread.RunJobs(); f.Click("Create");
        await f.Complete(mkdir); await Finished(f.Services.Jobs.Jobs.Last());
        await Show(zip); Assert.True(target.Listing.FocusName("a.txt"));
        var rename = f.Vm.ExecuteAsync(CommandIds.Rename);
        await Wait(() => f.Window.GetVisualDescendants().OfType<TextBox>().Any(box => AutomationProperties.GetName(box) == "Rename selected item" && box.IsEffectivelyVisible));
        var editor = f.Window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Rename selected item" && box.IsEffectivelyVisible);
        int before = f.Services.Jobs.Jobs.Count;
        editor.Text = "renamed.txt"; editor.Focus(); f.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Wait(() => f.Button("Rename") is not null || f.Services.Jobs.Jobs.Count > before);
        if (f.Button("Rename") is not null) f.Click("Rename");
        await f.Complete(rename); await Finished(f.Services.Jobs.Jobs.Last());
        await Show(zip); Assert.True(target.Listing.FocusName("b.txt"));
        var remove = f.Vm.ExecuteAsync(CommandIds.DeletePermanent); await Wait(() => f.Button("Remove") is not null);
        f.Click("Remove"); await f.Complete(remove); await Finished(f.Services.Jobs.Jobs.Last());
        var names = Read(path);
        Assert.Equal("alpha", names["renamed.txt"]); Assert.DoesNotContain("a.txt", names.Keys); Assert.DoesNotContain("b.txt", names.Keys);
        Assert.Contains(names.Keys, name => name.StartsWith("dir/sub", StringComparison.Ordinal));
        Assert.All(f.Services.Jobs.Jobs, job => Assert.Equal(ArchiveWriteFormat.Zip, job.Request.Archive?.Profile?.Format));

        // F4 and commit: a native edit session with the ZIP worker profile.
        await Show(zip); Assert.True(target.Listing.FocusName("renamed.txt"));
        var edit = f.Vm.ExecuteAsync(CommandIds.Edit); await f.Complete(edit);
        var session = Assert.Single(f.Services.EditSessions.LoadAll());
        Assert.True(session.IsNativeArchive); Assert.False(session.IsLegacyZip); Assert.Equal(ArchiveWriteFormat.Zip, session.ArchiveProfile?.Format);
        Assert.Equal("alpha", File.ReadAllText(session.WorkingPath));
        File.WriteAllText(session.WorkingPath, "edited through the ZIP worker é");
        var commit = (Task)typeof(MainViewModel).GetMethod("CommitSessionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(f.Vm, [session])!;
        await f.Complete(commit); await Finished(f.Services.Jobs.Jobs.Last());
        await Wait(() => f.Services.EditSessions.LoadAll().Single().LastCommitUtc is not null || f.Vm.Notification?.Contains("session could not be updated") == true);
        Assert.True(f.Services.EditSessions.LoadAll().Single().LastCommitUtc is not null, f.Vm.Notification);
        Assert.Equal("edited through the ZIP worker é", Read(path)["renamed.txt"]);
        Assert.Equal(ArchiveWriteFormat.Zip, f.Services.Jobs.Jobs.Last().Request.Archive?.Profile?.Format);

        Assert.Empty(JournalRecovery.Scan(f.Services.Paths.JournalDirectory));
        Assert.DoesNotContain(Directory.GetFileSystemEntries(Path.GetDirectoryName(path)!), entry => Path.GetFileName(entry).StartsWith(".filecat-archive-", StringComparison.Ordinal));
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(f.Files, "a.txt")));

        async Task Show(Core.Resources.Location location)
        {
            target.Navigate(location);
            await Wait(() => target.Location == location && !f.Vm.Dialogs.IsOpen && !target.IsLoading && target.Listing.State == ListingState.Complete);
        }
    }

    // The strict catalog keeps a ZIP with an extra field it does not know read-only, where the existing route would have
    // silently dropped that field.
    [AvaloniaFact]
    public async Task A_ZIP_the_strict_catalog_refuses_stays_read_only_and_says_why()
    {
        string executable = NativeApplication();
        using var f = await Fixture.Open(customBackends: Backends(executable), nativeWindowsOriginMarks: OperatingSystem.IsWindows());
        string path = Path.Combine(f.Files, "unknown-extra.zip"); byte[] bytes = UnknownExtraZip(); File.WriteAllBytes(path, bytes);
        var tab = f.Vm.ActiveTab!; var zip = ZipProvider.ForFile(path); tab.Navigate(zip);
        await Wait(() => tab.Location == zip && tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("x.txt"));
        var remove = f.Vm.ExecuteAsync(CommandIds.DeletePermanent);
        await Wait(() => f.Vm.Notification?.Contains("Cannot update this archive", StringComparison.Ordinal) == true || f.Button("Remove") is not null);
        Assert.Null(f.Button("Remove")); await f.Complete(remove);
        Assert.Contains("This ZIP contains metadata FileCat cannot preserve when saving. It is read-only here.", f.Vm.Notification);
        Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    private static ArchiveJobBackends Backends(string executable) =>
        new(ArchiveCreationFormat.All.Select(value => AppServices.PlatformArchiveBackend(value.Format, executable)));

    private static string NativeApplication()
    {
        string? executable = null;
        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") == "1" &&
            Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS") is { Length: > 0 } assets)
        {
            // A package route lane checks that the installed or portable copy itself lets its workers read it.
            if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_PACKAGE_ROUTE") != "1") ArchiveAppContainer.ProvisionRuntimeReadAccess(assets);
            executable = Path.Combine(assets, "FileCat.exe");
        }
        else if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_NATIVE") == "1")
            executable = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_APP");
        else if (OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_MAC_NATIVE") == "1")
            executable = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_MAC_APP");
        if (executable is null) Assert.Skip("Run an explicit native application-worker UI lane.");
        Assert.True(Path.IsPathFullyQualified(executable) && File.Exists(executable), "A published FileCat application is required: " + executable);
        return executable;
    }

    private static Dictionary<string, string> Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.Entries.Where(entry => !entry.FullName.EndsWith('/') || entry.Length == 0)
            .ToDictionary(entry => entry.FullName, entry => { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); });
    }

    // One stored member "x.txt" whose local and central headers carry an extra field with an unassigned ID (0xCAFE).
    private static byte[] UnknownExtraZip()
    {
        byte[] name = "x.txt"u8.ToArray(), data = "hello"u8.ToArray(), extra = [0xFE, 0xCA, 2, 0, 0, 0];
        uint crc = Crc32(data);
        using var output = new MemoryStream(); var writer = new BinaryWriter(output);
        writer.Write(0x04034b50u); writer.Write((ushort)20); writer.Write((ushort)0); writer.Write((ushort)0);
        writer.Write((ushort)0); writer.Write((ushort)0x21); writer.Write(crc); writer.Write(data.Length); writer.Write(data.Length);
        writer.Write((ushort)name.Length); writer.Write((ushort)extra.Length); writer.Write(name); writer.Write(extra); writer.Write(data);
        long central = output.Position;
        writer.Write(0x02014b50u); writer.Write((ushort)20); writer.Write((ushort)20); writer.Write((ushort)0); writer.Write((ushort)0);
        writer.Write((ushort)0); writer.Write((ushort)0x21); writer.Write(crc); writer.Write(data.Length); writer.Write(data.Length);
        writer.Write((ushort)name.Length); writer.Write((ushort)extra.Length); writer.Write((ushort)0); writer.Write((ushort)0);
        writer.Write((ushort)0); writer.Write(0u); writer.Write(0u); writer.Write(name); writer.Write(extra);
        long end = output.Position;
        writer.Write(0x06054b50u); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write((uint)(end - central)); writer.Write((uint)central); writer.Write((ushort)0);
        writer.Flush(); return output.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    private static async Task Finished(Job job)
    {
        var clock = Stopwatch.StartNew();
        while (!job.State.IsFinished()) { Dispatcher.UIThread.RunJobs(); if (clock.Elapsed > TimeSpan.FromSeconds(120)) throw new TimeoutException("ZIP job did not finish: " + job.Summary); await Task.Delay(10); }
        Assert.True(job.State is JobState.Completed or JobState.CompletedWithIssues, job.State + ": " + job.Summary + " " + JobIssue.Summarize(job.Issues));
    }

    private static async Task Wait(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready()) { Dispatcher.UIThread.RunJobs(); if (clock.Elapsed > TimeSpan.FromSeconds(120)) throw new TimeoutException("The ZIP worker UI did not settle."); await Task.Delay(10); }
    }
}
