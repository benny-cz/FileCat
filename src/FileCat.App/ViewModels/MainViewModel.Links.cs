using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.ViewModels;

/// <summary>
/// Create link (FAR's Alt+F6; plan §23.3): symbolic links, junctions, and hard links to the selected items, in the
/// target panel's folder by default. Every link is checked against its drives and its target's type before anything is
/// created, and Undo removes the links that are still unchanged.
/// </summary>
public sealed partial class MainViewModel
{
    private const int MaxLinks = 10_000;

    /// <summary>
    /// Where a single link is proposed: under the item's own name, or, where that is taken (always so in the item's own
    /// folder), "name - link", as Explorer proposes "- Shortcut".
    /// </summary>
    internal static string FreeLinkPath(string folder, string name, Func<string, bool>? exists = null)
    {
        exists ??= path => File.Exists(path) || Directory.Exists(path);
        string path = Path.Combine(folder, name);
        if (!exists(path)) return path;
        string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
        if (stem.Length == 0) (stem, extension) = (name, "");
        for (int n = 1; n < 100; n++)
        {
            path = Path.Combine(folder, $"{stem} - link{(n > 1 ? $" ({n})" : "")}{extension}");
            if (!exists(path)) return path;
        }
        return Path.Combine(folder, name);
    }

    private async Task CreateLinkAsync()
    {
        var sel = SourceSelection();
        if (sel is null) return;
        using var initialScope = new PreparationScope(sel.Value.Tab, Services.Io, sel.Value.Items);
        if (sel.Value.Items.Count > MaxLinks) { Notify($"Create at most {MaxLinks:N0} links at a time.", true); return; }
        var items = sel.Value.Items.ToList();
        if (items.Any(i => i.FileSystemPath is null))
        {
            Notify("Links point only to files and folders on disk.", true);
            return;
        }
        var origin = sel.Value.Tab; var context = origin.Location;
        var destination = Workspace.ActiveTarget?.ActiveTab?.Location is { IsFileSystem: true } t ? t.Path : Path.GetDirectoryName(items[0].FileSystemPath!)!;
        var fs = Services.Platform.FileOperations;
        bool? canSymlink; string initial;
        try
        {
            canSymlink = await MutationIoAsync(destination, () => fs.CanCreateSymbolicLinks, initialScope.Check);
            initial = items.Count == 1
                ? await MutationIoAsync(destination, () => FreeLinkPath(destination, items[0].Name, p => { initialScope.Check(); return fs.TryGetInfo(p) is not null; }), initialScope.Check)
                : AppendSeparator(destination);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { if (initialScope.Current) Notify("Cannot prepare links: " + ex.Message, true); return; }
        var pathBox = new TextBox { Text = initial, MinWidth = 560 };
        Avalonia.Automation.AutomationProperties.SetName(pathBox, "Link path");
        bool allFolders = items.All(i => i.IsContainer), allFiles = items.All(i => !i.IsContainer);
        var kinds = new List<(LinkKind Kind, RadioButton Button)>
        {
            (LinkKind.Symbolic, new RadioButton { Content = "Symbolic link", GroupName = "linkKind" }),
        };
        if (OperatingSystem.IsWindows()) kinds.Add((LinkKind.Junction, new RadioButton { Content = "Junction (folders on local drives)", GroupName = "linkKind" }));
        kinds.Add((LinkKind.Hard, new RadioButton { Content = "Hard link (files on the same drive)", GroupName = "linkKind" }));
        // Without the symbolic-link right, start with the kind that works without it.
        var preferred = canSymlink == false && allFolders && OperatingSystem.IsWindows() ? LinkKind.Junction
            : canSymlink == false && allFiles ? LinkKind.Hard : LinkKind.Symbolic;
        foreach (var (kind, button) in kinds) button.IsChecked = kind == preferred;
        var relative = new CheckBox { Content = "Store the target relative to the link's folder" };
        var privilege = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Classes = { "error" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 640,
            Text = "This account cannot create symbolic links: that needs Developer Mode (Settings → System → For developers) or administrator rights. A junction works for folders on local drives, and a hard link for files on the same drive.",
        };
        var preview = new ListBox { Height = 200, MinWidth = 640 };
        Avalonia.Automation.AutomationProperties.SetName(preview, "Links to create");
        var summary = new TextBlock { HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap, MaxWidth = 640 };
        IReadOnlyList<LinkPreview> rows = [];
        string? pathProblem = "Checking…";
        (string, LinkKind, bool)? plannedFor = null;
        int generation = 0; bool open = true;
        Task? previewWork = null; bool requested = false;
        (string, LinkKind, bool) Current() => (pathBox.Text ?? "", Kind(), relative.IsChecked == true);

        LinkKind Kind() => kinds.First(k => k.Button.IsChecked == true).Kind;
        void Refresh()
        {
            if (!open) return;
            ++generation; plannedFor = null; requested = true;
            // One owned preview per dialog. Edits replace the pending request instead of growing the device queue.
            if (previewWork is null || previewWork.IsCompleted) previewWork = RefreshPendingAsync();
        }
        async Task RefreshPendingAsync()
        {
            while (open && requested && !Services.Io.IsStopped) { requested = false; await RefreshAsync(); }
        }
        async Task RefreshAsync()
        {
            int mine = generation;
            void Check() { if (!open || mine != generation || Services.Io.IsStopped) throw new OperationCanceledException(); }
            var kind = Kind();
            relative.IsEnabled = kind == LinkKind.Symbolic;
            privilege.IsVisible = kind == LinkKind.Symbolic && canSymlink == false;
            var options = new LinkOptions(kind, relative.IsChecked == true && kind == LinkKind.Symbolic);
            string text = pathBox.Text ?? ""; var key = Current();
            plannedFor = null; pathProblem = "Checking…"; summary.Text = pathProblem;
            string? problem; IReadOnlyList<LinkPreview> result;
            try
            {
                Check();
                var resolved = ResolveLinkPath(text, items.Count, context);
                problem = resolved.Error; result = [];
                if (problem is null)
                {
                    string folder = resolved.Folder!;
                    var directory = await MutationIoAsync(folder, () => fs.TryGetInfo(folder), Check);
                    if (directory?.IsDirectory != true) problem = $"The folder \"{folder}\" does not exist.";
                    else
                    {
                        var volumes = new Dictionary<string, VolumeInfo>(PathUtil.SafetyComparer);
                        foreach (string path in items.Select(i => Path.GetDirectoryName(i.FileSystemPath!)!).Prepend(folder).Distinct(PathUtil.SafetyComparer))
                        { Check(); volumes[path] = await MutationIoAsync(path, () => fs.GetVolumeInfo(path), Check); }
                        var occupied = new HashSet<string>(PathUtil.SafetyComparer);
                        foreach (var item in items)
                        {
                            Check(); string path = Path.Combine(folder, resolved.Name ?? item.Name);
                            if (await MutationIoAsync(path, () => fs.TryGetInfo(path), Check) is not null) occupied.Add(path);
                        }
                        Check(); result = LinkPlanner.Preview(items, folder, resolved.Name is null ? null : [resolved.Name], options, p => volumes[p], occupied.Contains);
                    }
                }
                Check();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                if (!open || mine != generation || Services.Io.IsStopped) return;
                problem = ex.Message; result = [];
            }
            pathProblem = problem; rows = result; plannedFor = key;
            preview.ItemsSource = rows.Take(1000).Select(r => r.Problem is null
                ? $"{Path.GetFileName(r.LinkPath)}  →  {r.TargetText}"
                : $"⚠ {Path.GetFileName(r.LinkPath)}: {r.Problem}").ToList();
            int problems = rows.Count(r => r.Problem is not null);
            summary.Text = problem ?? (problems > 0
                ? $"{Formatters.Plural(problems, "link has a problem", "links have problems")}; nothing is created until every link can be."
                : $"{Formatters.Plural(rows.Count, LinkPlanner.KindName(kind), LinkPlanner.KindName(kind) + "s")} will be created.");
            summary.Classes.Set("error", problem is not null || problems > 0);
        }
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        debounce.Tick += (_, _) => { debounce.Stop(); Refresh(); };
        pathBox.TextChanged += (_, _) => { ++generation; plannedFor = null; debounce.Stop(); if (open) debounce.Start(); };
        foreach (var (_, button) in kinds) button.IsCheckedChanged += (_, _) => { if (button.IsChecked == true) Refresh(); };
        relative.IsCheckedChanged += (_, _) => Refresh();

        var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (var (_, button) in kinds) kindRow.Children.Add(button);
        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                kindRow,
                privilege,
                new TextBlock { Text = items.Count == 1 ? "Link (folder and name):" : "Create the links in:" },
                pathBox,
                relative,
                preview,
                summary,
            },
        };
        Refresh();
        var answer = await Dialogs.ShowCustomAsync(items.Count == 1 ? $"Create link to \"{Formatters.SafeName(items[0].Name)}\"" : $"Create links to {items.Count:N0} items", body,
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Create", "create", IsDefault: true)], pathBox,
            // Only a preview of exactly what is entered can be created.
            () => plannedFor == Current() && pathProblem is null && rows.Count > 0 && rows.All(r => r.Problem is null));
        open = false; ++generation; debounce.Stop();
        if (previewWork is not null) await previewWork;
        if (Services.Io.IsStopped || answer as string != "create" || plannedFor != Current() || pathProblem is not null || rows.Count == 0 || rows.Any(r => r.Problem is not null)) return;
        string linkFolder = Path.GetDirectoryName(rows[0].LinkPath)!;
        var job = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.CreateLink,
            Sources = items,
            Destination = Location.FileSystem(linkFolder),
            NewNames = rows.Select(r => Path.GetFileName(r.LinkPath)).ToList(),
            Link = new LinkOptions(Kind(), relative.IsChecked == true && Kind() == LinkKind.Symbolic),
        });
        Track(job, origin);
    }

    /// <summary>
    /// The typed link path. For several items, or text ending in a separator, it is the existing folder for links named
    /// like their targets; otherwise it is the full path of the one link (so the preview shows exactly what is created).
    /// </summary>
    private (string? Folder, string? Name, string? Error) ResolveLinkPath(string text, int count, Location? context)
    {
        var t = text.Trim();
        if (t.Length == 0) return (null, null, "Enter where to create the link.");
        // Only the file-system parser is needed here; archive parsers can probe the disk while interpreting input.
        if (!Services.Providers.Get(Schemes.FileSystem).TryParse(t, context, out var loc) || loc is not { IsFileSystem: true })
            return (null, null, "Links are created only in folders on disk.");
        if (count > 1 || t.EndsWith('\\') || t.EndsWith('/')) return (loc.Path, null, null);
        string? folder = Path.GetDirectoryName(loc.Path); string name = Path.GetFileName(loc.Path);
        return folder is null || name.Length == 0 ? (null, null, "Enter a folder and link name.") : (folder, name, null);
    }
}
