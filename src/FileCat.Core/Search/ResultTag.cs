using FileCat.Core.Resources;

namespace FileCat.Core.Search;

/// <summary>
/// Provenance of a result-set entry: the original container and the folder shown to the user.
/// Result sets hold references to originals, never copies (plan §11).
/// </summary>
public sealed record ResultTag(Location Parent, string RelativeFolder, EntryKind Kind) : IDisplayDetails
{
    /// <summary>The folder shown when it differs from <see cref="RelativeFolder"/> (a working set shows where each item is).</summary>
    public string? Folder { get; init; }

    /// <summary>Distinguishes duplicate names in one container (archives), so the entry maps back to its exact member.</summary>
    public int Ordinal { get; init; }

    /// <summary>What the result set says about the item (its group among duplicates), shown in a details column.</summary>
    public string? Note { get; init; }

    public string KindText => Kind switch
    {
        EntryKind.RegistryKey => "Key",
        EntryKind.RegistryValue => "Value",
        EntryKind.Directory => "Folder",
        _ => "File",
    };
    public string DetailsText => Note ?? string.Empty;
}

/// <summary>An entry in the list of working sets: the set it opens and how many items it holds.</summary>
public sealed record WorkingSetTag(string Id, int Count) : IDisplayDetails
{
    public string KindText => "Working set";
    public string DetailsText => Count == 1 ? "1 item" : $"{Count:N0} items";
}
