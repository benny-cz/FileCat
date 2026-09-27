using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Controls;

public enum ColumnField
{
    Name,
    Extension,
    Size,
    Modified,
    Created,
    Attributes,
    /// <summary>Containing folder of a result-set item.</summary>
    Folder,
    /// <summary>Registry value type or a provider-specific kind.</summary>
    Kind,
    /// <summary>Registry data preview or archive member details.</summary>
    Details,
    /// <summary>A lazily computed metadata field (<see cref="ColumnSpec.MetadataId"/>).</summary>
    Metadata,
}

public sealed record ColumnSpec(ColumnField Field, string Header, double Width, bool RightAlign = false, bool Star = false)
{
    public string? MetadataId { get; init; }

    public SortField? SortField => Field switch
    {
        ColumnField.Name => Core.Listing.SortField.Name,
        ColumnField.Extension => Core.Listing.SortField.Extension,
        ColumnField.Size => Core.Listing.SortField.Size,
        ColumnField.Modified => Core.Listing.SortField.Modified,
        ColumnField.Created => Core.Listing.SortField.Created,
        ColumnField.Attributes => Core.Listing.SortField.Attributes,
        ColumnField.Metadata => Core.Listing.SortField.Metadata,
        _ => null,
    };
}

/// <summary>Built-in column profiles (the defaults of <see cref="ColumnProfileSet"/>) and dedicated layouts.</summary>
public static class ColumnProfiles
{
    public static IReadOnlyList<(string Name, ColumnSpec[] Columns)> Defaults { get; } =
    [
        ("Details", [
            new(ColumnField.Name, "Name", 200, Star: true),
            new(ColumnField.Extension, "Ext", 56),
            new(ColumnField.Size, "Size", 86, RightAlign: true),
            new(ColumnField.Modified, "Modified", 128),
            new(ColumnField.Attributes, "Attr", 44),
        ]),
        ("Brief", [
            new(ColumnField.Name, "Name", 200, Star: true),
            new(ColumnField.Size, "Size", 86, RightAlign: true),
        ]),
        ("Full", [
            new(ColumnField.Name, "Name", 200, Star: true),
            new(ColumnField.Extension, "Ext", 56),
            new(ColumnField.Size, "Size", 96, RightAlign: true),
            new(ColumnField.Modified, "Modified", 128),
            new(ColumnField.Created, "Created", 128),
            new(ColumnField.Metadata, "Version", 110) { MetadataId = "version" },
            new(ColumnField.Attributes, "Attr", 50),
        ]),
        ("Media", [
            new(ColumnField.Name, "Name", 200, Star: true),
            new(ColumnField.Size, "Size", 86, RightAlign: true),
            new(ColumnField.Metadata, "Dimensions", 110, RightAlign: true) { MetadataId = "dimensions" },
            new(ColumnField.Metadata, "Origin", 90) { MetadataId = "zone" },
            new(ColumnField.Modified, "Modified", 128),
        ]),
        ("Links", [
            new(ColumnField.Name, "Name", 200, Star: true),
            new(ColumnField.Metadata, "Link target", 260, Star: true) { MetadataId = "linkTarget" },
            new(ColumnField.Modified, "Modified", 128),
        ]),
    ];

    public static ColumnSpec[] ResultSet { get; } =
    [
        new(ColumnField.Name, "Name", 180, Star: true),
        new(ColumnField.Folder, "Folder", 220, Star: true),
        new(ColumnField.Size, "Size", 86, RightAlign: true),
        new(ColumnField.Modified, "Modified", 128),
    ];

    public static ColumnSpec[] Registry { get; } =
    [
        new(ColumnField.Name, "Name", 200, Star: true),
        new(ColumnField.Kind, "Type", 120),
        new(ColumnField.Details, "Data", 240, Star: true),
        new(ColumnField.Size, "Size", 70, RightAlign: true),
    ];
}

/// <summary>A column the profile editor offers: its stable settings key and how to build it.</summary>
public sealed record ColumnChoice(string Key, string Title, bool RightAlign, double DefaultWidth);

/// <summary>
/// The user's column profiles (Alt+0–9), shared by all tabs and backed by settings. Until customized the built-in
/// profiles apply. A width of 0 in settings means "fill the remaining space". Result sets and the registry keep
/// their dedicated layouts. Every profile keeps a Name column.
/// </summary>
public sealed class ColumnProfileSet
{
    public const int MaxProfiles = 10;
    private List<(string Name, ColumnSpec[] Columns)> _profiles;

    public ColumnProfileSet(IReadOnlyList<ColumnProfile>? saved)
    {
        _profiles = Parse(saved) ?? [.. ColumnProfiles.Defaults];
    }

    /// <summary>Raised on the UI thread after a width or profile change.</summary>
    public event Action? Changed;

    public int Count => _profiles.Count;

    public IReadOnlyList<(string Name, ColumnSpec[] Columns)> Profiles => _profiles;

    public string NameOf(int profile) => _profiles[Math.Clamp(profile, 0, _profiles.Count - 1)].Name;

    /// <summary>True when the scheme has a dedicated layout that profiles do not change.</summary>
    public static bool HasFixedLayout(string scheme) => scheme is Schemes.ResultSet or Schemes.Registry;

    public ColumnSpec[] Get(int profile, string scheme)
    {
        if (scheme == Schemes.ResultSet) return ColumnProfiles.ResultSet;
        if (scheme == Schemes.Registry) return ColumnProfiles.Registry;
        return _profiles[Math.Clamp(profile, 0, _profiles.Count - 1)].Columns;
    }

    public static IReadOnlyList<ColumnChoice> Choices { get; } =
    [
        new("name", "Name", false, 0),
        new("ext", "Ext", false, 56),
        new("size", "Size", true, 86),
        new("modified", "Modified", false, 128),
        new("created", "Created", false, 128),
        new("attr", "Attr", false, 44),
        .. Core.Metadata.BuiltInFields.All.Select(f => new ColumnChoice("meta:" + f.Id, f.Title, f.RightAlign, 110)),
    ];

    /// <summary>Stores a column width chosen by dragging its border (never turns a fill column fixed).</summary>
    public void SetWidth(int profile, int column, double width)
    {
        if ((uint)profile >= (uint)_profiles.Count) return;
        var (name, columns) = _profiles[profile];
        if ((uint)column >= (uint)columns.Length || columns[column].Star) return;
        var copy = (ColumnSpec[])columns.Clone();
        copy[column] = copy[column] with { Width = Math.Round(Math.Clamp(width, 24, 2000)) };
        _profiles[profile] = (name, copy);
        Changed?.Invoke();
    }

    /// <summary>Replaces all profiles (the Settings editor); keeps at least one and at most ten.</summary>
    public void Replace(IEnumerable<(string Name, ColumnSpec[] Columns)> profiles)
    {
        var list = profiles.Where(p => p.Columns.Length > 0).Take(MaxProfiles).Select(p => (p.Name, EnsureName(p.Columns))).ToList();
        _profiles = list.Count > 0 ? list : [.. ColumnProfiles.Defaults];
        Changed?.Invoke();
    }

    public List<ColumnProfile> ToSettings() =>
        _profiles.Select(p => new ColumnProfile
        {
            Name = p.Name,
            Columns = p.Columns.Select(c => new ColumnSetting { Field = KeyOf(c), Width = c.Star ? 0 : c.Width, Visible = true }).ToList(),
        }).ToList();

    public static string KeyOf(ColumnSpec c) => c.Field switch
    {
        ColumnField.Name => "name",
        ColumnField.Extension => "ext",
        ColumnField.Size => "size",
        ColumnField.Modified => "modified",
        ColumnField.Created => "created",
        ColumnField.Attributes => "attr",
        ColumnField.Metadata => "meta:" + c.MetadataId,
        _ => c.Field.ToString().ToLowerInvariant(),
    };

    /// <summary>A column for a settings key; width 0 fills the remaining space. Null for unknown keys.</summary>
    public static ColumnSpec? Create(string key, double width)
    {
        var choice = Choices.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
        if (choice is null) return null;
        bool star = width <= 0;
        double w = star ? 200 : Math.Clamp(width, 24, 2000);
        var field = choice.Key switch
        {
            "name" => ColumnField.Name,
            "ext" => ColumnField.Extension,
            "size" => ColumnField.Size,
            "modified" => ColumnField.Modified,
            "created" => ColumnField.Created,
            "attr" => ColumnField.Attributes,
            _ => ColumnField.Metadata,
        };
        var spec = new ColumnSpec(field, choice.Title, w, choice.RightAlign, star);
        return field == ColumnField.Metadata ? spec with { MetadataId = choice.Key["meta:".Length..] } : spec;
    }

    private static List<(string Name, ColumnSpec[] Columns)>? Parse(IReadOnlyList<ColumnProfile>? saved)
    {
        if (saved is null || saved.Count == 0) return null;
        var list = new List<(string, ColumnSpec[])>();
        foreach (var p in saved)
        {
            if (list.Count == MaxProfiles) break;
            var columns = p.Columns.Where(c => c.Visible).Select(c => Create(c.Field, c.Width)).OfType<ColumnSpec>().ToArray();
            if (columns.Length == 0) continue;
            list.Add((string.IsNullOrWhiteSpace(p.Name) ? $"Profile {list.Count}" : p.Name.Trim(), EnsureName(columns)));
        }
        return list.Count > 0 ? list : null;
    }

    private static ColumnSpec[] EnsureName(ColumnSpec[] columns) =>
        columns.Any(c => c.Field == ColumnField.Name) ? columns : [Create("name", 0)!, .. columns];
}
