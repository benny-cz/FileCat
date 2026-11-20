namespace FileCat.Core.Archives;

/// <summary>In-process parent capability, never deserialized or granted by a pathname in a worker request.</summary>
internal interface IArchiveOwnedEditInput:IArchiveRetainedFile
{
    string SourcePath { get; }
    void RequireRequest(ArchivePlan request);
    IArchiveSelectionTree OpenTree(string parent,CancellationToken ct);
}
