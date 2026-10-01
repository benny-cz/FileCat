using System.Buffers.Binary;
using System.Text;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.State;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Shell;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>
/// Release plan I16: an icon that a user's file names (desktop.ini, a shortcut) is read by the restricted helper only
/// from this computer, and a path elsewhere is not touched at all, not even for its time.
/// </summary>
public sealed class IconResourceTests
{
    [Fact]
    public void An_icon_named_on_a_network_path_is_refused_before_it_is_touched()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Icon resources are read on Windows.");
        static string? Policy(IconLocation location) => ShellPreviewPolicy.IconResourceRefusal(location, allowNetworkAndRemovable: false);
        // 203.0.113.9 is a documentation address: reading a time there takes seconds to fail; refusing takes none.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(NativeIconSource.ResourceTime(new IconLocation(@"\\203.0.113.9\share\folder.ico", 0), Policy));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Took {clock.Elapsed}: the network path was tried.");
        string local = Path.Combine(Environment.SystemDirectory, "imageres.dll");
        Assert.Equal(File.GetLastWriteTimeUtc(local).Ticks, NativeIconSource.ResourceTime(new IconLocation(local, 0), Policy));
    }

    /// <summary>
    /// A shell link (MS-SHLLINK) that names an icon and nothing else: the 0x4C header, then the icon location as
    /// string data, then the terminal block. Enough for the reader FileCat uses, and built here so no Shell code of
    /// Windows' own touches the path while the fixture is made.
    /// </summary>
    private static byte[] Shortcut(string? iconFile, int iconIndex = 0)
    {
        var link = new byte[0x4C];
        BinaryPrimitives.WriteUInt32LittleEndian(link, 0x4C);
        new Guid("00021401-0000-0000-c000-000000000046").TryWriteBytes(link.AsSpan(4, 16));
        BinaryPrimitives.WriteUInt32LittleEndian(link.AsSpan(0x14), iconFile is null ? 0x80u : 0x40 | 0x80); // HasIconLocation | IsUnicode
        BinaryPrimitives.WriteUInt32LittleEndian(link.AsSpan(0x18), 0x80); // FILE_ATTRIBUTE_NORMAL
        BinaryPrimitives.WriteInt32LittleEndian(link.AsSpan(0x38), iconIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(link.AsSpan(0x3C), 1); // SW_SHOWNORMAL
        if (iconFile is null) return [.. link, 0, 0, 0, 0];
        var icon = Encoding.Unicode.GetBytes(iconFile);
        var bytes = new byte[link.Length + 2 + icon.Length + 4];
        link.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(link.Length), (ushort)(icon.Length / 2));
        icon.CopyTo(bytes, link.Length + 2);
        return bytes; // the last four bytes stay zero: the terminal block
    }

    /// <summary>
    /// A shell link whose target is <paramref name="target"/>, carried the way shortcuts to environment-dependent
    /// paths carry it (HasExpString and an EnvironmentVariableDataBlock), with no icon of its own: what it shows is its
    /// target's icon, which for a program is the program's own.
    /// </summary>
    private static byte[] ShortcutTo(string target)
    {
        var link = new byte[0x4C];
        BinaryPrimitives.WriteUInt32LittleEndian(link, 0x4C);
        new Guid("00021401-0000-0000-c000-000000000046").TryWriteBytes(link.AsSpan(4, 16));
        BinaryPrimitives.WriteUInt32LittleEndian(link.AsSpan(0x14), 0x80 | 0x200); // IsUnicode | HasExpString
        BinaryPrimitives.WriteUInt32LittleEndian(link.AsSpan(0x18), 0x80); // FILE_ATTRIBUTE_NORMAL: a file, not a folder
        BinaryPrimitives.WriteUInt32LittleEndian(link.AsSpan(0x3C), 1);
        var block = new byte[0x314];
        BinaryPrimitives.WriteUInt32LittleEndian(block, 0x314);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), 0xA0000001);
        Encoding.Unicode.GetBytes(target).CopyTo(block, 8 + 260);
        return [.. link, .. block, 0, 0, 0, 0];
    }

    /// <summary>
    /// Release plan V24, with a packet capture on the named host as the oracle: a folder of files that name their icon
    /// on a share — an Internet shortcut, a shell link and a customized folder — is listed and every icon asked for.
    /// Beside each is the same file naming an icon on this computer, which must get its icon: that control is what
    /// makes a missing icon the policy instead of a broken pipeline. FILECAT_V24_SHARE names the host under capture.
    /// </summary>
    [AvaloniaFact]
    public async Task Icons_named_on_a_share_are_never_contacted_while_a_folder_is_listed()
    {
        string? host = Environment.GetEnvironmentVariable("FILECAT_V24_SHARE");
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Icon resources are read on Windows."); return; }
        if (host is null) { Assert.Skip("Set FILECAT_V24_SHARE to a host under capture."); return; }
        var log = TestContext.Current.TestOutputHelper;
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-v24-icons", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string share = $@"\\{host}\evidence-{Guid.NewGuid():N}\folder.ico";
            string local = Path.Combine(Environment.SystemDirectory, "imageres.dll");
            log?.WriteLine($"the share named by the fixture: {share}");

            void Url(string name, string? iconFile) =>
                File.WriteAllText(Path.Combine(root, name), "[InternetShortcut]\r\nURL=https://example.com/\r\n" + (iconFile is null ? "" : $"IconFile={iconFile}\r\nIconIndex=0\r\n"));
            void Custom(string name, string? resource)
            {
                var folder = Directory.CreateDirectory(Path.Combine(root, name));
                if (resource is not null) File.WriteAllText(Path.Combine(folder.FullName, "desktop.ini"), $"[.ShellClassInfo]\r\nIconResource={resource},0\r\n");
                folder.Attributes |= FileAttributes.ReadOnly; // Explorer reads desktop.ini only for such folders
            }
            // Three files that name an icon on the share, three that name one here, and three that name none: the last
            // are what the type icon looks like, which is what a refused one falls back to.
            Url("shared.url", share);
            Url("local.url", local);
            Url("plain.url", null);
            File.WriteAllBytes(Path.Combine(root, "shared.lnk"), Shortcut(share));
            File.WriteAllBytes(Path.Combine(root, "local.lnk"), Shortcut(local));
            File.WriteAllBytes(Path.Combine(root, "plain.lnk"), Shortcut(null));
            Custom("shared-folder", share);
            Custom("local-folder", local);
            Custom("plain-folder", null);
            // Shortcuts that name no icon but a program as their target, whose own icon they then show: on the share,
            // on this computer, and on this computer but missing — the last is what the program type looks like, which
            // is all a target on the share may show.
            string shareTarget = $@"\\{host}\evidence-{Guid.NewGuid():N}\program.exe";
            File.WriteAllBytes(Path.Combine(root, "target-shared.lnk"), ShortcutTo(shareTarget));
            File.WriteAllBytes(Path.Combine(root, "target-local.lnk"), ShortcutTo(Path.Combine(Environment.SystemDirectory, "notepad.exe")));
            File.WriteAllBytes(Path.Combine(root, "target-missing.lnk"), ShortcutTo(Path.Combine(root, "no-such-folder", "program.exe")));
            log?.WriteLine($"the share named as a target: {shareTarget}");

            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "data")));
            services.Settings.ShellPictures = true;
            services.Settings.ShellPicturesOnNetworkAndRemovable = false;
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

                // Every icon asked for as a drawn row asks, until each named one that can arrive has. A refused icon
                // and one still being read both show the type icon, so "the named icon arrived" means: the row's icon
                // is not the one the file naming no icon shows. Once true it stays true.
                var place = Location.FileSystem(root);
                string[] named = ["local.url", "local.lnk", "local-folder", "shared.url", "shared.lnk", "shared-folder"];
                string[] plain = ["plain.url", "plain.lnk", "plain-folder"];
                var arrived = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.Elapsed < TimeSpan.FromSeconds(30))
                {
                    var current = new Dictionary<string, Avalonia.Media.IImage?>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < tab.Listing.VisibleCount; i++)
                    {
                        var entry = tab.Listing.GetVisible(i);
                        string name = entry.Name.ToString();
                        if (Array.IndexOf(named, name) >= 0 || Array.IndexOf(plain, name) >= 0) current[name] = icons.GetIcon(entry, place);
                    }
                    foreach (string name in named)
                    {
                        string type = plain[name.EndsWith(".url", StringComparison.OrdinalIgnoreCase) ? 0 : name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? 1 : 2];
                        if (current.GetValueOrDefault(name) is { } icon && !ReferenceEquals(icon, current.GetValueOrDefault(type))) arrived[name] = true;
                    }
                    if (named.Take(3).All(n => arrived.GetValueOrDefault(n))) break;
                    await Task.Delay(100, ct);
                }
                foreach (string name in named) log?.WriteLine($"{name}: {(arrived.GetValueOrDefault(name) ? "the icon it names" : "the type icon only")}");
                log?.WriteLine($"asked for {clock.Elapsed.TotalSeconds:N1} s");

                // The controls prove the helper runs and reads named icons here; the three on the share get none.
                Assert.True(arrived.GetValueOrDefault("local.url"), "The control Internet shortcut kept the type icon: no named icon was read at all.");
                Assert.True(arrived.GetValueOrDefault("local.lnk"), "The control shell link kept the type icon.");
                Assert.True(arrived.GetValueOrDefault("local-folder"), "The control customized folder kept the type icon.");
                Assert.False(arrived.GetValueOrDefault("shared.url"), "An icon came from the share.");
                Assert.False(arrived.GetValueOrDefault("shared.lnk"), "An icon came from the share.");
                Assert.False(arrived.GetValueOrDefault("shared-folder"), "An icon came from the share.");

                // Targets: the local program's own icon arrives (it is not the program type's icon, which the missing
                // target shows), and the shortcut to a program on the share shows the program type's icon and no other.
                Avalonia.Media.IImage? Icon(string name)
                {
                    for (int i = 0; i < tab.Listing.VisibleCount; i++)
                        if (tab.Listing.GetVisible(i) is var entry && entry.Name.ToString() == name) return icons.GetIcon(entry, place);
                    return null;
                }
                bool ownArrived = false;
                var typeIcon = (Avalonia.Media.IImage?)null;
                clock.Restart();
                while (clock.Elapsed < TimeSpan.FromSeconds(30))
                {
                    typeIcon = Icon("target-missing.lnk");
                    var own = Icon("target-local.lnk");
                    Icon("target-shared.lnk");
                    if (typeIcon is not null && own is not null && !ReferenceEquals(own, typeIcon)) { ownArrived = true; break; }
                    await Task.Delay(100, ct);
                }
                var sharedTarget = Icon("target-shared.lnk");
                log?.WriteLine($"targets: the local program's own icon {(ownArrived ? "arrived" : "did not arrive")}; the program on the share shows {(sharedTarget is not null && ReferenceEquals(sharedTarget, typeIcon) ? "the program type's icon" : "something else")}");
                Assert.True(ownArrived, "The control shortcut to a local program never showed that program's own icon.");
                Assert.NotNull(sharedTarget);
                Assert.Same(typeIcon, sharedTarget);
            }
            finally
            {
                tab.Dispose();
            }
        }
        finally
        {
            foreach (var folder in Directory.EnumerateDirectories(root)) new DirectoryInfo(folder).Attributes = FileAttributes.Directory;
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
