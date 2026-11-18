using FileCat.Core.Resources;

namespace FileCat.Core.Archives;

internal enum ArchiveOutputKind { DiskCache, Extraction, Drag }

/// <summary>Opens members only after the provider can verify the protection of every actual input.</summary>
internal interface IUnprotectedArchiveOutput
{
    void RequireClearOutputSource(Location location, ArchiveOutputKind kind);
    IContentSource? OpenUnprotectedOutput(ItemRef item, ArchiveOutputKind kind);
}
