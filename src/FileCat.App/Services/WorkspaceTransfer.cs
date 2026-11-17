using System.Text.Json;
using System.Text.Json.Serialization;
using FileCat.App.ViewModels;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows.Shell;

namespace FileCat.App.Services;

/// <summary>The forward-only clipboard format for open local folders and the panel layout.</summary>
internal sealed record WorkspaceTransfer(WorkspaceTransfer.Panel[] Panels, WorkspaceTransfer.Node Layout, int ActivePanel)
{
    internal const int MaxCharacters = 64 * 1024;
    private const int MaxTabs = 512;
    private const string Header = "FileCat-Workspace-";
    private const string Prefix = Header + "1 ";
    private static readonly WorkspaceTransferJsonContext Json = new(new JsonSerializerOptions
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 24,
    });

    internal sealed record Panel(string[] Folders, int ActiveTab, int? Target);
    internal sealed record Node(int? Panel, bool Stacked, double Size, Node[] Children);

    internal static bool HasHeader(string? text) => text.AsSpan().TrimStart().StartsWith(Header, StringComparison.Ordinal);

    internal static WorkspaceTransfer Capture(WorkspaceViewModel workspace)
    {
        var panels = workspace.Panels;
        if (panels.Count == 0) throw new IOException("There are no panels to copy.");
        var ids = panels.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        var layout = PanelLayout.Matches(workspace.LayoutTree, ids.Keys)
            ? workspace.LayoutTree : PanelLayout.Default(panels.Select(p => p.Id).ToArray());
        return new(panels.Select(p =>
        {
            var folders = p.Tabs.Select(t => t.Location is { IsFileSystem: true, Container: null, Session: null } loc
                && IsFolderPath(loc.Path) && IsLocalDrive(loc.Path) ? loc.Path : null).ToArray();
            int active = folders.Take(Math.Max(0, p.ActiveTab is { } tab ? p.Tabs.IndexOf(tab) : 0)).Count(path => path is not null);
            string[] local = folders.OfType<string>().ToArray();
            int? target = p.TargetPanelId is { } id && ids.TryGetValue(id, out int index) ? index : null;
            return new Panel(local, Math.Min(active, Math.Max(0, local.Length - 1)), target);
        }).ToArray(), CaptureLayout(layout, ids), workspace.ActivePanel is { } active ? ids[active.Id] : 0);
    }

    private static Node CaptureLayout(PanelLayoutNode node, Dictionary<string, int> ids) => node.PanelId is { } id
        ? new(ids[id], false, node.Size, [])
        : new(null, node.Stacked, node.Size, node.Children.Select(n => CaptureLayout(n, ids)).ToArray());

    internal string Encode()
    {
        if (!IsValid()) throw new IOException($"Copying a workspace supports up to {MaxTabs} local-folder tabs.");
        string text = Prefix + JsonSerializer.Serialize(this, Json.WorkspaceTransfer);
        if (text.Length > MaxCharacters) throw new IOException("This workspace is too large to copy. Close some tabs and try again.");
        return text;
    }

    internal static WorkspaceTransfer? Decode(string? text)
    {
        if (text is null || text.Length > MaxCharacters) return null;
        text = text.Trim();
        if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        try
        {
            var transfer = JsonSerializer.Deserialize(text[Prefix.Length..], Json.WorkspaceTransfer);
            return transfer?.IsValid() == true ? transfer : null;
        }
        catch (JsonException) { return null; }
    }

    private bool IsValid()
    {
        if (Panels is not { Length: > 0 and <= WorkspaceViewModel.MaxPanels } || ActivePanel < 0 || ActivePanel >= Panels.Length)
            return false;
        int total = 0;
        for (int i = 0; i < Panels.Length; i++)
        {
            var panel = Panels[i];
            if (panel?.Folders is not { } folders || panel.ActiveTab < 0 || panel.ActiveTab >= Math.Max(1, folders.Length) ||
                panel.Target is { } target && (target < 0 || target >= Panels.Length || target == i))
                return false;
            total += folders.Length;
            if (total > MaxTabs || folders.Any(path => !IsFolderPath(path))) return false;
        }
        var seen = new HashSet<int>();
        return ValidateLayout(Layout, seen, 1) && seen.Count == Panels.Length;
    }

    private bool ValidateLayout(Node? node, HashSet<int> seen, int depth)
    {
        if (node?.Children is not { } children || depth > Panels.Length || !(node.Size > 0 && node.Size <= 1_000_000))
            return false;
        if (node.Panel is { } index) return children.Length == 0 && index >= 0 && index < Panels.Length && seen.Add(index);
        return children.Length >= 2 && children.Length <= Panels.Length &&
            children.All(child => ValidateLayout(child, seen, depth + 1));
    }

    private static bool IsFolderPath(string? path)
    {
        if (path is not { Length: >= 3 and <= 2048 } || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\')
            return false;
        return path[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries).All(part =>
            !part.EndsWith('.') && !part.EndsWith(' ') &&
            !part.Any(c => char.IsControl(c) || "<>:\"/|?*".Contains(c)) &&
            Formatters.DescribeHiddenNameCharacters(part) is null && !IsReservedDevice(part));
    }

    private static bool IsReservedDevice(string component)
    {
        string stem = component.Split('.', 2)[0].TrimEnd().ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            stem.Length == 4 && stem[..3] is "COM" or "LPT" && "123456789¹²³".Contains(stem[3]);
    }

    private static bool IsLocalDrive(string path)
    {
        try { return new DriveInfo(path[..3]).DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Ram; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    internal sealed class Prepared : IDisposable
    {
        internal WorkspaceState State { get; } = new();
        internal int Skipped { get; set; }
        // Repeated tabs can share one read lease, including when they are in different panels.
        internal Dictionary<string, LocalIconFileLease?> Paths { get; } = new(StringComparer.Ordinal);
        public void Dispose()
        {
            foreach (var lease in Paths.Values) lease?.Dispose();
            Paths.Clear();
        }
    }

    internal Prepared Prepare(Location fallback)
    {
        var prepared = new Prepared();
        try
        {
            // Generate new panel IDs so existing jobs cannot be retargeted by the clipboard.
            string[] ids = Panels.Select(_ => Guid.NewGuid().ToString("N")).ToArray();
            bool CanOpen(string path)
            {
                if (!prepared.Paths.TryGetValue(path, out var lease))
                {
                    lease = IsLocalDrive(path) ? LocalIconFileLease.TryOpenPlainDirectory(path) : null;
                    prepared.Paths.Add(path, lease);
                }
                return lease is not null;
            }
            for (int i = 0; i < Panels.Length; i++)
            {
                var source = Panels[i];
                var panel = new PanelState { Id = ids[i], TargetPanelId = source.Target is { } target ? ids[target] : null };
                for (int tab = 0; tab < source.Folders.Length; tab++)
                {
                    string path = source.Folders[tab];
                    if (!CanOpen(path)) { prepared.Skipped++; continue; }
                    if (tab < source.ActiveTab) panel.ActiveTab++;
                    panel.Tabs.Add(new TabState { Location = Location.FileSystem(path) });
                }
                if (panel.Tabs.Count == 0)
                {
                    var home = IsFolderPath(fallback.Path) && CanOpen(fallback.Path) ? fallback : new Location(Schemes.Computer, "");
                    panel.Tabs.Add(new TabState { Location = home });
                }
                prepared.State.Panels.Add(panel);
            }
            prepared.State.ActivePanelId = ids[ActivePanel];
            prepared.State.Tree = RestoreLayout(Layout, ids);
            prepared.State.Layout = Layout.Stacked ? "Rows" : "Columns";
            return prepared;
        }
        catch { prepared.Dispose(); throw; }
    }

    private static PanelLayoutNode RestoreLayout(Node node, string[] ids) => node.Panel is { } panel
        ? PanelLayoutNode.Panel(ids[panel], node.Size)
        : PanelLayoutNode.Split(node.Stacked, node.Children.Select(child => RestoreLayout(child, ids)), node.Size);
}

[JsonSerializable(typeof(WorkspaceTransfer))]
internal sealed partial class WorkspaceTransferJsonContext : JsonSerializerContext;
