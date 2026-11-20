using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

internal enum ArchiveArtifactKind { Stage, Manifest, Provenance }

/// <summary>A single-attempt name chosen before creation, bound to one retained directory instance.</summary>
internal sealed class ArchiveArtifactReservation
{
    private readonly object issuer;
    private int claimed;
    internal Guid Nonce { get; }
    internal ArchiveArtifactKind Kind { get; }
    internal string ParentIdentity { get; }
    /// <summary>Set only by a creator-private stage folder: the identity of the reviewed archive folder that contains it.</summary>
    internal string? PublicParentIdentity { get; }
    internal string Path { get; }
    internal string LeafName { get; }
    internal long MaximumLength { get; }

    internal ArchiveArtifactReservation(object issuer, Guid nonce, ArchiveArtifactKind kind, string directory, string parentIdentity, long maximumLength,
        string? publicParentIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        if (nonce == Guid.Empty || !Enum.IsDefined(kind) || maximumLength < 0 ||
            maximumLength > (kind == ArchiveArtifactKind.Stage ? ArchiveWritePolicy.MaxPhysicalBytes : ArchiveWritePolicy.MaxMetadataBytes))
            throw new ArgumentException("Invalid archive artifact reservation.");
        if (!System.IO.Path.IsPathFullyQualified(directory)) throw new ArgumentException("An absolute artifact directory is required.");
        ArchiveTransactionFacts.ValidateText(parentIdentity, 4096);
        if (publicParentIdentity is not null)
        {
            ArchiveTransactionFacts.ValidateText(publicParentIdentity, 4096);
            if (publicParentIdentity == parentIdentity ||
                !(System.IO.Path.GetFileName(directory)?.StartsWith(ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix, StringComparison.Ordinal) ?? false))
                throw new ArgumentException("A private stage reservation requires its own folder below the reviewed archive folder.");
        }
        PublicParentIdentity = publicParentIdentity;
        string suffix = kind switch { ArchiveArtifactKind.Stage => "stage", ArchiveArtifactKind.Manifest => "manifest", _ => "origin" };
        this.issuer = issuer; Nonce = nonce; Kind = kind; ParentIdentity = parentIdentity; MaximumLength = maximumLength;
        LeafName = $".filecat-archive-{nonce:N}-{Guid.NewGuid():N}.{suffix}";
        Path = System.IO.Path.Combine(directory, LeafName);
    }

    internal void Claim(object expectedIssuer)
    {
        if (!ReferenceEquals(issuer, expectedIssuer)) throw new ArgumentException("The reservation belongs to another retained directory.");
        if (Interlocked.Exchange(ref claimed, 1) != 0) throw new InvalidOperationException("An archive artifact reservation can be attempted only once.");
    }

    internal ArchiveSpaceReview ReviewSpace(ArchiveSpaceEstimate estimate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(estimate); ct.ThrowIfCancellationRequested();
        var reviewed = issuer is IArchiveDestinationSpace directory ? directory.ReviewSpace(ParentIdentity, estimate, ct)
            : new ArchiveSpaceReview(ParentIdentity, estimate, null);
        if (reviewed.ParentIdentity != ParentIdentity || reviewed.Estimate != estimate)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The space review differs from its reserved destination or byte estimate.");
        reviewed.RequireAvailable(); ct.ThrowIfCancellationRequested(); return reviewed;
    }
}

/// <summary>Durable intent precedes exclusive creation. Failure retains evidence and never performs pathname cleanup.</summary>
internal static class ArchiveJournaledStage
{
    internal static T Create<T>(JobJournal journal, ArchiveTransactionFacts reserved, ArchiveArtifactReservation reservation,
        Func<ArchiveArtifactReservation, T> create, CancellationToken ct) where T : class, IArchiveRetainedStage
    {
        ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(reserved);
        ArgumentNullException.ThrowIfNull(reservation); ArgumentNullException.ThrowIfNull(create);
        ct.ThrowIfCancellationRequested();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (reserved.RecordVersion != ArchiveTransactionFacts.SchemaVersion || reserved.Sequence != 1 ||
            reserved.Phase != ArchiveTransactionPhase.Reserved || reserved.StageIdentity is not null ||
            reservation.Kind != ArchiveArtifactKind.Stage || reserved.Nonce != reservation.Nonce ||
            reserved.StagePath != reservation.Path || reserved.ParentIdentity != reservation.ParentIdentity ||
            reserved.StageMaximumLength != reservation.MaximumLength ||
            !string.Equals(System.IO.Path.GetDirectoryName(reserved.ArchivePath),
                System.IO.Path.GetDirectoryName(reservation.PublicParentIdentity is null ? reservation.Path : System.IO.Path.GetDirectoryName(reservation.Path)), comparison))
            throw new ArgumentException("The durable stage reservation differs from its native creation authority.");
        journal.ArchiveTransaction(reserved);
        T? stage = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            stage = create(reservation);
            string identity = stage.ReviewCreation(reservation, ct);
            if (identity != stage.ObjectIdentity)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The created stage differs from its durable reservation; retain it for review.");
            journal.ArchiveTransaction(reserved.BindCreatedStage(identity));
            return stage;
        }
        catch
        {
            // The stage may exist under its reserved name without a recorded identity: a discard then refuses.
            journal.RequirePreparationReview();
            if (stage is not null) { try { stage.StopWrites(); } finally { stage.Dispose(); } }
            throw;
        }
    }
}
