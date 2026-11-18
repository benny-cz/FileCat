using FileCat.Core.FileSystem;

namespace FileCat.Core.Resources;

internal enum ContentOutputKind { DiskCache, Extraction, Drag, RemoteUpload }

/// <summary>Admits derived output through a provider that can verify the protection of every actual input.</summary>
internal interface IUnprotectedContentOutput
{
    void RequireClearOutputSource(Location location, ContentOutputKind kind);
    IContentSource? OpenUnprotectedOutput(ItemRef item, ContentOutputKind kind);
    IContentSource? OpenUnprotectedOutput(ItemRef item, ContentOutputKind kind, out OutputEfsObservation? provenance)
    {
        provenance = null;
        return OpenUnprotectedOutput(item, kind);
    }
}
