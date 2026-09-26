using FileCat.Core.Resources;

namespace FileCat.Core.Search;

/// <summary>
/// Provenance of a result-set entry: the original container and the folder shown to the user.
/// Result sets hold references to originals, never copies (plan §11).
/// </summary>
public sealed record ResultTag(Location Parent, string RelativeFolder);
