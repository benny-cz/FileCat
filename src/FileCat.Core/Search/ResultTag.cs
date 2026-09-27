using FileCat.Core.Resources;

namespace FileCat.Core.Search;

/// <summary>
/// Provenance of a result-set entry: the original container and the folder shown to the user.
/// Result sets hold references to originals, never copies (plan §11).
/// </summary>
public sealed record ResultTag(Location Parent, string RelativeFolder, EntryKind Kind) : IDisplayDetails
{
    public string KindText => Kind switch
    {
        EntryKind.RegistryKey => "Key",
        EntryKind.RegistryValue => "Value",
        EntryKind.Directory => "Folder",
        _ => "File",
    };
    public string DetailsText => string.Empty;
}
