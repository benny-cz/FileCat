using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Platform;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>
/// Cloud providers' folders (E-CLOUD-1's validation): looking at one never downloads anything. A real provider's folder
/// of files that are only in the cloud is listed with icons, every row's metadata and checksum check is asked for, its
/// content is searched and its size counted — and afterwards every one of those files is still only in the cloud.
/// FILECAT_CLOUD_FOLDER names such a folder (on the owner's computer, a OneDrive folder of five small files).
/// </summary>
public sealed class CloudBrowsingTests
{
    [AvaloniaFact]
    public async Task Looking_at_a_cloud_folder_downloads_nothing()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Cloud placeholders are Windows'."); return; }
        if (Environment.GetEnvironmentVariable("FILECAT_CLOUD_FOLDER") is not { Length: > 0 } folder) { Assert.Skip("Set FILECAT_CLOUD_FOLDER to a cloud provider's folder of online-only files."); return; }
        var log = TestContext.Current.TestOutputHelper;
        var ct = TestContext.Current.CancellationToken;

        // Every file underneath, since the search and the size count go into subfolders too.
        Dictionary<string, FileAttributes> Snapshot() => new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(folder, f.FullName), f => f.Attributes);
        var before = Snapshot();
        var onlineOnly = before.Where(p => CloudFiles.StateOf(p.Value, inSyncRoot: true) == CloudState.OnlineOnly).Select(p => p.Key).ToList();
        log?.WriteLine($"{before.Count} files, {onlineOnly.Count} only in the cloud");
        Assert.NotEmpty(onlineOnly);

        string root = Path.Combine(Path.GetTempPath(), "filecat-cloud-validate", Guid.NewGuid().ToString("N"));
        var platformBefore = PlatformFactory.WindowsFactory;
        PlatformFactory.WindowsFactory = () => new FileCat.Platform.Windows.WindowsPlatform();
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "data")));
            services.Settings.ShellPictures = true;
            var icons = NativeIconSource.TryCreate(services.Shell, () => services.AllowedShellPictures)!;
            services.Icons.Native = icons;
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            workspace.ActivePanel = panel;
            var tab = panel.OpenTab(Location.FileSystem(folder));
            try
            {
                for (int i = 0; i < 400 && tab.Listing.State == ListingState.Loading; i++) await Task.Delay(10, ct);
                Assert.Equal(ListingState.Complete, tab.Listing.State);
                var place = Location.FileSystem(folder);
                int flagged = 0;
                // As drawn rows ask, for six seconds: icons, the metadata a column could show, the checksum check.
                for (int round = 0; round < 60; round++)
                {
                    flagged = 0;
                    for (int i = 0; i < tab.Listing.VisibleCount; i++)
                    {
                        var e = tab.Listing.GetVisible(i);
                        if (e.Kind != EntryKind.File) continue;
                        if (e.Has(EntryFlags.Offline)) flagged++;
                        icons.GetIcon(e, place);
                        string path = Path.Join(folder, e.Name.ToString());
                        foreach (string key in new[] { "dimensions", "duration", "title", "verified" })
                            services.Metadata.Get(key, path, e, "cloud", false, () => true);
                        tab.Verification(e, i);
                    }
                    await Task.Delay(100, ct);
                }
                int topLevel = onlineOnly.Count(n => !n.Contains(Path.DirectorySeparatorChar));
                log?.WriteLine($"{flagged} rows marked as cloud placeholders (quick view shows them a message, not their content)");
                Assert.Equal(topLevel, flagged);
            }
            finally { tab.Dispose(); }

            // Searching their content, and counting the folder's size.
            var set = new ResultSet("cloud", "cloud", "cloud");
            new SearchSession(new SearchQuery { Roots = [folder], Text = "the", IncludeDirectories = false }, set).Run(ct);
            var size = DirectorySizer.Compute(folder, null, ct);
            log?.WriteLine($"content search found {set.Snapshot().Count}; size counted {size.Bytes:N0} bytes in {size.Files} files");
        }
        finally
        {
            PlatformFactory.WindowsFactory = platformBefore;
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }

        // Every file that was only in the cloud still is.
        await Task.Delay(1000, ct);
        var after = Snapshot();
        var downloaded = onlineOnly.Where(n => !after.TryGetValue(n, out var a) || CloudFiles.StateOf(a, inSyncRoot: true) != CloudState.OnlineOnly).ToList();
        Assert.True(downloaded.Count == 0, $"Downloaded by looking: {string.Join(", ", downloaded)}");
        log?.WriteLine($"all {onlineOnly.Count} still only in the cloud");
    }
}
