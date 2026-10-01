using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>
/// Release plan V24, the process half of I16's gate: showing a folder runs only the programs FileCat's own policy
/// allows — Git where the repository may be read, and the restricted Shell helper for an icon — and nothing a file in
/// the folder names. The folder here is built to tempt it: a repository whose configuration names a program for Git to
/// run, shortcuts and a customized folder naming programs as their icon, and a program of its own. What actually ran
/// is recorded outside this process by a trace of started processes (the harness in artifacts/vm/win-browse-trace.ps1);
/// what this test asserts is the part visible from inside: which badges and icons arrived.
/// </summary>
public sealed class BrowsingProcessTests
{
    [AvaloniaFact]
    public async Task Browsing_a_hostile_folder_reads_only_what_it_may()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("The Shell helper and this trace are Windows'."); return; }
        if (Environment.GetEnvironmentVariable("FILECAT_V24_BROWSE") != "1") { Assert.Skip("Set FILECAT_V24_BROWSE=1 to run this under a process trace."); return; }
        if (GitStatusReader.FindGit(Environment.GetEnvironmentVariable("PATH")) is not { } git) { Assert.Skip("Git is not installed."); return; }
        var log = TestContext.Current.TestOutputHelper;
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-v24-browse", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            // A program of this computer's own, which the fixtures name as their icon and as Git's filter.
            string program = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            void Git(string folder, params string[] arguments)
            {
                var start = new System.Diagnostics.ProcessStartInfo(git) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var a in new[] { "-c", "user.name=FileCat", "-c", "user.email=filecat@example.com" }.Concat(arguments)) start.ArgumentList.Add(a);
                using var p = System.Diagnostics.Process.Start(start)!;
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
            }
            string Repository(string name)
            {
                string folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
                Git(folder, "init", "-q");
                File.WriteAllText(Path.Combine(folder, "a.txt"), "one");
                Git(folder, "add", "a.txt");
                Git(folder, "commit", "-q", "-m", "first");
                return folder;
            }
            Repository("ordinary");
            string hostile = Repository("with-a-filter");
            // "git status" runs a clean filter while it compares files: this repository must not be read at all.
            File.AppendAllText(Path.Combine(hostile, ".git", "config"), $"[filter \"evil\"]\n\tclean = \"{program.Replace("\\", "\\\\")}\"\n\tsmudge = cat\n");
            File.WriteAllText(Path.Combine(hostile, ".gitattributes"), "* filter=evil\n");

            File.WriteAllText(Path.Combine(root, "shortcut.url"), $"[InternetShortcut]\r\nURL=https://example.com/\r\nIconFile={program}\r\nIconIndex=0\r\n");
            var custom = Directory.CreateDirectory(Path.Combine(root, "customized"));
            File.WriteAllText(Path.Combine(custom.FullName, "desktop.ini"), $"[.ShellClassInfo]\r\nIconResource={program},0\r\n");
            custom.Attributes |= FileAttributes.ReadOnly;
            File.Copy(program, Path.Combine(root, "a program.exe"));
            File.WriteAllText(Path.Combine(root, "a&calc.txt"), "a name that means something to a shell");

            // The fixtures are built with Git, so the trace must know when that stopped and the browsing began.
            if (Environment.GetEnvironmentVariable("FILECAT_V24_BROWSE_READY") is { Length: > 0 } ready)
            {
                await Task.Delay(1500, ct);
                File.WriteAllText(ready, DateTime.Now.ToString("o"));
                await Task.Delay(1500, ct);
            }

            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "data")));
            services.Settings.ShellPictures = true;
            var icons = NativeIconSource.TryCreate(services.Shell, () => services.AllowedShellPictures);
            Assert.NotNull(icons);
            services.Icons.Native = icons;
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            workspace.ActivePanel = panel;
            var tab = panel.OpenTab(Location.FileSystem(root));
            try
            {
                for (int i = 0; i < 400 && tab.Listing.State == ListingState.Loading; i++) await Task.Delay(10, ct);
                Assert.Equal(ListingState.Complete, tab.Listing.State);

                var place = Location.FileSystem(root);
                for (int round = 0; round < 60; round++)
                {
                    for (int i = 0; i < tab.Listing.VisibleCount; i++) icons.GetIcon(tab.Listing.GetVisible(i), place);
                    await Task.Delay(100, ct);
                }
                var badges = await GitStatusReader.ReadAsync(root, ct);
                log?.WriteLine($"badges: ordinary {badges?.ForName("ordinary")}, with-a-filter {badges?.ForName("with-a-filter")}");

                // The repository that names a program for Git to run gets no badge; the ordinary one does.
                Assert.Equal(GitStatusKind.Clean, badges?.ForName("ordinary"));
                Assert.Equal(GitStatusKind.None, badges?.ForName("with-a-filter"));
            }
            finally { tab.Dispose(); }
        }
        finally
        {
            // A file trace must not count the clean-up below as browsing: it opens everything in the folder.
            if (Environment.GetEnvironmentVariable("FILECAT_V24_BROWSE_DONE") is { Length: > 0 } done) File.WriteAllText(done, DateTime.Now.ToString("o"));
            foreach (var folder in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)) new DirectoryInfo(folder).Attributes = FileAttributes.Directory;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
