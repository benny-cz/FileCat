using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class GitBadgeUiTests
{
    [AvaloniaFact]
    public async Task Completed_folder_reread_updates_git_badge_description_and_icon_tip()
    {
        if (GitStatusReader.FindAutomaticGit() is not { } git)
        {
            Assert.Skip("Automatic Git is unavailable on this platform.");
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "filecat-git-ui-tests", Guid.NewGuid().ToString("N"));
        string settings = Path.Combine(Path.GetTempPath(), "filecat-git-ui-settings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            void Git(params string[] arguments)
            {
                var start = new ProcessStartInfo(git)
                {
                    WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                };
                foreach (string argument in new[] { "-c", "user.name=FileCat", "-c", "user.email=filecat@example.com", "-c", "core.autocrlf=false" }.Concat(arguments))
                    start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.True(process.ExitCode == 0, error);
            }

            Git("init", "-q");
            string tracked = Path.Combine(root, "tracked.txt");
            File.WriteAllText(tracked, "first\n");
            Git("add", "tracked.txt");
            Git("commit", "-q", "-m", "first");

            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: settings));
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            workspace.ActivePanel = panel;
            var tab = panel.OpenTab(Location.FileSystem(root));
            var list = new FileListControl { Tab = tab };
            var window = new Window { Width = 640, Height = 420, Content = list };
            window.Show();
            try
            {
                var ct = TestContext.Current.CancellationToken;
                await Wait(() => tab.Listing.State == ListingState.Complete, ct);
                int storeIndex = -1;
                await Wait(() => (storeIndex = Enumerable.Range(0, tab.Listing.Store.Count)
                    .FirstOrDefault(i => tab.Listing.Store[i].Name == "tracked.txt", -1)) >= 0, ct);
                await Wait(() => list.DescribeItem(storeIndex).Contains("Git clean", StringComparison.Ordinal), ct);
                int row = tab.Listing.GetVisibleIndex(storeIndex);
                var iconPoint = list.TranslatePoint(new Point(12, list.GetRowBounds(row).Center.Y), window)!.Value;
                window.MouseMove(iconPoint);
                Assert.Equal("Git clean", ToolTip.GetTip(list));

                File.AppendAllText(tracked, "second\n");
                int completed = 0;
                tab.Listing.Changed += (_, change) => { if ((change & ListingChange.RefreshComplete) != 0) completed++; };
                tab.Refresh();
                await Wait(() => completed > 0, ct);
                await Wait(() => list.DescribeItem(storeIndex).Contains("Git changed", StringComparison.Ordinal), ct);
                window.MouseMove(new Point(120, 10));
                window.MouseMove(iconPoint);
                Assert.Equal("Git changed", ToolTip.GetTip(list));
            }
            finally
            {
                window.Close();
                tab.Dispose();
            }
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
            try { Directory.Delete(settings, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task Badges_update_while_the_folder_keeps_being_reread_and_an_unchanged_one_keeps_the_tip()
    {
        // A Low of the review of 47f09505: every completed reread canceled the Git read under way, so in a busy repository
        // the badges never updated; and each snapshot closed every tip, also where nothing had changed.
        if (GitStatusReader.FindAutomaticGit() is not { } git)
        {
            Assert.Skip("Automatic Git is unavailable on this platform.");
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "filecat-git-ui-tests", Guid.NewGuid().ToString("N"));
        string settings = Path.Combine(Path.GetTempPath(), "filecat-git-ui-settings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            void Git(params string[] arguments)
            {
                var start = new ProcessStartInfo(git)
                {
                    WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                };
                foreach (string argument in new[] { "-c", "user.name=FileCat", "-c", "user.email=filecat@example.com", "-c", "core.autocrlf=false" }.Concat(arguments))
                    start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.True(process.ExitCode == 0, error);
            }

            Git("init", "-q");
            string tracked = Path.Combine(root, "tracked.txt");
            File.WriteAllText(tracked, "first\n");
            Git("add", "tracked.txt");
            Git("commit", "-q", "-m", "first");

            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: settings));
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            workspace.ActivePanel = panel;
            var tab = panel.OpenTab(Location.FileSystem(root));
            var list = new FileListControl { Tab = tab };
            var window = new Window { Width = 640, Height = 420, Content = list };
            window.Show();
            try
            {
                var ct = TestContext.Current.CancellationToken;
                await Wait(() => tab.Listing.State == ListingState.Complete, ct);
                int storeIndex = -1;
                await Wait(() => (storeIndex = Enumerable.Range(0, tab.Listing.Store.Count)
                    .FirstOrDefault(i => tab.Listing.Store[i].Name == "tracked.txt", -1)) >= 0, ct);
                await Wait(() => list.DescribeItem(storeIndex).Contains("Git clean", StringComparison.Ordinal), ct);
                int row = tab.Listing.GetVisibleIndex(storeIndex);
                var iconPoint = list.TranslatePoint(new Point(12, list.GetRowBounds(row).Center.Y), window)!.Value;
                window.MouseMove(iconPoint);
                Assert.Equal("Git clean", ToolTip.GetTip(list));

                int taken = list.GitSnapshotsTaken;
                list.RefreshGitStatuses();
                await Wait(() => list.GitSnapshotsTaken > taken, ct);
                Assert.Equal("Git clean", ToolTip.GetTip(list));

                // Rereads every 50 ms, faster than a read can finish: the change still shows.
                File.AppendAllText(tracked, "second\n");
                var watch = Stopwatch.StartNew();
                while (!list.DescribeItem(storeIndex).Contains("Git changed", StringComparison.Ordinal) && watch.Elapsed < TimeSpan.FromSeconds(15))
                {
                    list.RefreshGitStatuses();
                    await Task.Delay(50, ct);
                }
                Assert.Contains("Git changed", list.DescribeItem(storeIndex));
            }
            finally
            {
                window.Close();
                tab.Dispose();
            }
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
            try { Directory.Delete(settings, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task Wait(Func<bool> condition, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 500 && !condition(); attempt++)
            await Task.Delay(20, cancellationToken);
        Assert.True(condition());
    }

}
