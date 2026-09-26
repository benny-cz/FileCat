using FileCat.Core.Listing;
using FileCat.Core.Resources;

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

/// <summary>Named column profiles switchable with Alt+0–9 (plan §10).</summary>
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

    public static ColumnSpec[] Get(int profile, string scheme)
    {
        if (scheme == Schemes.ResultSet) return ResultSet;
        if (scheme == Schemes.Registry) return Registry;
        var p = Defaults[Math.Clamp(profile, 0, Defaults.Count - 1)];
        return p.Columns;
    }
}
