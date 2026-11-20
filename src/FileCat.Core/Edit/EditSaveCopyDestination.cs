using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

/// <summary>Explicit parent-owned destination. Deserialized names never manufacture this native lease.</summary>
internal abstract class EditSaveCopyDestination:IDisposable
{
    internal abstract string Path { get; }
    internal abstract string Identity { get; }
    internal abstract IArchiveDestinationSpace Space { get; }
    internal abstract ArchiveArtifactReservation Reserve(Guid nonce,ArchiveArtifactKind kind,long maximum,CancellationToken ct);
    internal abstract IArchiveRetainedStage Create(ArchiveArtifactReservation reservation,CancellationToken ct);
    internal abstract IArchiveRetainedArtifact? OpenReadOrAbsent(string leaf,long maximum,CancellationToken ct);
    internal abstract void Review(CancellationToken ct);
    public abstract void Dispose();
}

internal interface IEditSaveCopyInspection:IArchiveRetainedFile
{
    ArchiveFileObservation InspectSavedCopy(CancellationToken ct);
}

public enum EditSavedCopyRecoveryState { Interrupted, NeedsReview, MatchesRecordedCopy }

/// <summary>Current read-only observations. These fields authorize no replacement or cleanup.</summary>
public sealed record EditSavedCopyRecovery(string Destination,EditSavedCopyRecoveryState State,string Reason);
