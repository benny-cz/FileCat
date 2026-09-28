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

    private async Task CreateLinkAsync()
    {
        var sel = SourceSelection();
        if (sel is null) return;
        var items = sel.Value.Items.ToList();
        if (items.Any(i => i.FileSystemPath is null))
        {
            Notify("Links point only to files and folders on disk.", true);
            return;
        }
        if (items.Count > MaxLinks) { Notify($"Create at most {MaxLinks:N0} links at a time.", true); return; }
        var fs = Services.Platform.FileOperations;
        bool? canSymlink = await Task.Run(() => fs.CanCreateSymbolicLinks);

        var destination = Workspace.ActiveTarget?.ActiveTab?.Location is { IsFileSystem: true } t ? t.Path : Path.GetDirectoryName(items[0].FileSystemPath!)!;
        string initial = items.Count == 1 ? Path.Combine(destination, items[0].Name) : AppendSeparator(destination);
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
            Classes = { "error" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 640,
            Text = "This account cannot create symbolic links: that needs Developer Mode (Settings → System → For developers) or administrator rights. A junction works for folders on local drives, and a hard link for files on the same drive.",
        };
        var preview = new ListBox { Height = 200, MinWidth = 640 };
        Avalonia.Automation.AutomationProperties.SetName(preview, "Links to create");
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 640 };
        IReadOnlyList<LinkPreview> rows = [];
        string? pathProblem = "Checking…";
        int generation = 0;

        LinkKind Kind() => kinds.First(k => k.Button.IsChecked == true).Kind;
        async void Refresh()
        {
            int mine = ++generation;
            var kind = Kind();
            relative.IsEnabled = kind == LinkKind.Symbolic;
            privilege.IsVisible = kind == LinkKind.Symbolic && canSymlink == false;
            var options = new LinkOptions(kind, relative.IsChecked == true && kind == LinkKind.Symbolic);
            string text = pathBox.Text ?? "";
            var (problem, result) = await Task.Run(() =>
            {
                try
                {
                    if (!ResolveLinkPath(text, items.Count, out var folder, out var name, out var error)) return (error, (IReadOnlyList<LinkPreview>)[]);
                    return ((string?)null, LinkPlanner.Preview(items, folder!, name is null ? null : [name], options, fs.GetVolumeInfo, p => fs.TryGetInfo(p) is not null));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    return (ex.Message, []);
                }
            });
            if (mine != generation) return;
            pathProblem = problem;
            rows = result;
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
        pathBox.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
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
            () => pathProblem is null && rows.Count > 0 && rows.All(r => r.Problem is null));
        debounce.Stop();
        if (answer as string != "create" || pathProblem is not null || rows.Count == 0 || rows.Any(r => r.Problem is not null)) return;
        string linkFolder = Path.GetDirectoryName(rows[0].LinkPath)!;
        var job = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.CreateLink,
            Sources = items,
            Destination = Location.FileSystem(linkFolder),
            NewNames = rows.Select(r => Path.GetFileName(r.LinkPath)).ToList(),
            Link = new LinkOptions(Kind(), relative.IsChecked == true && Kind() == LinkKind.Symbolic),
        });
        if (ActiveTab is { } tab) Track(job, tab);
    }

    /// <summary>
    /// The typed link path. For several items, or text ending in a separator, it is the existing folder for links named
    /// like their targets; otherwise it is the full path of the one link (so the preview shows exactly what is created).
    /// </summary>
    private bool ResolveLinkPath(string text, int count, out string? folder, out string? name, out string? error)
    {
        folder = name = error = null;
        var t = text.Trim();
        if (t.Length == 0) { error = "Enter where to create the link."; return false; }
        if (!Services.Providers.TryParse(t, ActiveTab?.Location, out var loc) || loc is not { IsFileSystem: true })
        {
            error = "Links are created only in folders on disk.";
            return false;
        }
        if (count > 1 || t.EndsWith('\\') || t.EndsWith('/'))
        {
            if (!Directory.Exists(loc.Path))
            {
                error = $"The folder \"{loc.Path}\" does not exist.";
                return false;
            }
            folder = loc.Path;
            return true;
        }
        folder = Path.GetDirectoryName(loc.Path);
        name = Path.GetFileName(loc.Path);
        if (folder is null || name.Length == 0 || !Directory.Exists(folder))
        {
            error = $"The folder \"{folder ?? loc.Path}\" does not exist.";
            return false;
        }
        return true;
    }
}
