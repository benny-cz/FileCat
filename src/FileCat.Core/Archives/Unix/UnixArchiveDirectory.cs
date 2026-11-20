using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Unix;

internal enum UnixArchiveArtifactKind { Stage, Manifest, Provenance }
internal sealed class UnixArchiveArtifactCreationException(string retainedPath, Exception inner)
    : IOException("The created private archive artifact requires review: " + retainedPath, inner)
{ internal string RetainedPath { get; } = retainedPath; } // Evidence, never pathname cleanup authority.

/// <summary>Unregistered parent storage. Held directory-relative children, private-at-create artifacts, no path deletion.</summary>
internal sealed partial class UnixArchiveDirectory : IDisposable, IArchiveDestinationSpace
{
    internal readonly object Gate = new();
    private readonly SafeFileHandle directory;
    private readonly UnixArchiveDirectory? privateStageParent;
    private readonly uint user;
    private int children;
    private bool disposed;
    internal string DisplayPath { get; }
    internal FileIdentity Identity { get; }
    internal UnixArchiveVolume Volume { get; }
    internal SafeFileHandle Handle => directory; // Only under Gate while a live owner/child exists.

    private UnixArchiveDirectory(string path, UnixArchiveVolume? sameMountAs = null)
    {
        user = UnixArchiveNative.RequireOrdinaryIdentity(); DisplayPath = path; directory = UnixArchiveNative.PinDirectory(path);
        try
        {
            Identity = UnixFiles.Stat(directory)?.Identity ?? throw UnixArchiveNative.Unavailable();
            Volume = sameMountAs is { } known && known.Matches(directory) ? known : UnixArchiveVolume.Observe(directory);
        }
        catch { directory.Dispose(); throw; }
    }
    private UnixArchiveDirectory(string path, SafeFileHandle pinned, uint owner, UnixArchiveDirectory creatorParent)
    {
        user = owner; DisplayPath = path; directory = pinned; privateStageParent = creatorParent;
        try
        {
            Identity = UnixFiles.Stat(directory)?.Identity ?? throw UnixArchiveNative.Unavailable();
            Volume = UnixArchiveVolume.Observe(directory);
            VerifyNamespace();
            UnixArchiveNative.RequireEditDirectory(directory, user);
        }
        catch { directory.Dispose(); throw; }
    }
    internal static UnixArchiveDirectory Open(string path) => new(path);
    /// <summary>
    /// Opens a folder below <paramref name="pinnedOwner"/>, which stays open meanwhile. While the owner's descriptor pins
    /// its mount, that mount ID names no other filesystem, so a folder on the same mount shares its observed volume without
    /// reading mountinfo again (AT18: one mount-table read per input reopen); any other folder is observed in full.
    /// </summary>
    internal static UnixArchiveDirectory OpenBelow(string path, UnixArchiveDirectory pinnedOwner)
    {
        ArgumentNullException.ThrowIfNull(pinnedOwner);
        // The owner's descriptor must stay open across the comparison, so the shared mount cannot be released meanwhile.
        lock (pinnedOwner.Gate)
        {
            if (pinnedOwner.disposed) throw new ObjectDisposedException(nameof(UnixArchiveDirectory));
            return new(path, pinnedOwner.Volume);
        }
    }
    /// <summary>
    /// Creates a separate pinned, creator-private stage namespace on this volume. The directory and all later
    /// artifacts are retained if a review or durability step fails; this does not grant exchange or cleanup authority.
    /// </summary>
    internal UnixArchiveDirectory CreatePrivateStageDirectory(Guid nonce, CancellationToken ct = default)
    {
        if (nonce == Guid.Empty) throw new ArgumentException("A fresh stage-directory nonce is required.", nameof(nonce));
        lock (Gate)
        {
            RequireCapacity(); ct.ThrowIfCancellationRequested();
            if (UnixArchiveNative.RequireOrdinaryIdentity() != user) throw UnixArchiveNative.Unavailable();
            VerifyNamespace(); CheckCreationNamespace();
            string leaf = ".filecat-archive-stage-" + nonce.ToString("N");
            string path = Path.Combine(DisplayPath, leaf);
            UnixArchiveDirectory? stage = null;
            try
            {
                // CreateEditDirectory uses mkdirat on the pinned parent and removes inherited Mac ACL entries
                // before any child bytes can be written. The pinned child, not a later path reopen, is adopted.
                var pinned = UnixArchiveNative.CreateEditDirectory(directory, leaf, user, ct, DisplayPath);
                stage = new UnixArchiveDirectory(path, pinned, user, this);
                if (stage.Volume != Volume) throw UnixArchiveNative.Unavailable();
                UnixArchiveNative.SyncDirectory(stage.Handle);
                UnixArchiveNative.SyncDirectory(directory);
                return stage;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException or ArchiveWriteException or OperationCanceledException) &&
                !ex.Data.Contains(ArchiveWriteMessages.NothingCreatedKey))
            {
                stage?.Dispose(); // The directory name is retained for reconciliation, never unlinked here.
                throw new UnixArchiveArtifactCreationException(path, ex);
            }
        }
    }
    ArchiveSpaceReview IArchiveDestinationSpace.ReviewSpace(string expectedParentIdentity, ArchiveSpaceEstimate estimate, CancellationToken ct)
    {
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested(); VerifyNamespace();
            if (expectedParentIdentity != Identity.ToString()) throw UnixArchiveNative.Unavailable();
            var capacity = FileSystemCapacity.QueryHeldDirectory(directory, Identity.VolumeSerial);
            ct.ThrowIfCancellationRequested(); VerifyNamespace(); return new(expectedParentIdentity, estimate, capacity);
        }
    }
    internal UnixArchiveFile CreateArtifact(Guid nonce, UnixArchiveArtifactKind kind, long maximumLength)
        => CreateArtifact(ReserveArtifact(nonce, kind, maximumLength));
    internal ArchiveArtifactReservation ReserveArtifact(Guid nonce, UnixArchiveArtifactKind kind, long maximumLength)
    {
        if (nonce == Guid.Empty || !Enum.IsDefined(kind)) throw new ArgumentException("Invalid Unix archive artifact scope.");
        Limit(kind, maximumLength);
        lock (Gate)
        {
            RequireCapacity(); VerifyNamespace();
            return new(this, nonce, (ArchiveArtifactKind)kind, DisplayPath, Identity.ToString(), maximumLength, privateStageParent?.Identity.ToString());
        }
    }
    internal UnixArchiveFile CreateArtifact(ArchiveArtifactReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        lock (Gate)
        {
            RequireCapacity(); VerifyNamespace(); reservation.Claim(this);
            string leaf = reservation.LeafName;
            var held = UnixArchiveNative.Create(directory, Path.Combine(DisplayPath, leaf), leaf);
            try { return Adopt(held, leaf, reservation.MaximumLength, artifact: true, writable: true, createdKind: (UnixArchiveArtifactKind)reservation.Kind); }
            catch (Exception ex) { throw new UnixArchiveArtifactCreationException(Path.Combine(DisplayPath, leaf), ex); }
        }
    }
    internal UnixArchiveFile OpenSource(string leaf, long maximumLength = ArchiveWritePolicy.MaxPhysicalBytes) => OpenExisting(leaf, maximumLength, null);
    internal void CaptureSource(ArchiveCapturedInputs inputs, int id, string leaf, CancellationToken ct)
    { ArgumentNullException.ThrowIfNull(inputs); UnixArchiveNative.Leaf(leaf); inputs.Capture(id, () => OpenSource(leaf, ArchiveWritePolicy.MaxPayloadBytes), ct); }
    internal UnixArchiveFile OpenRecoveryArtifact(string leaf, FileIdentity expected, UnixArchiveArtifactKind kind, long maximumLength)
    { if (!Enum.IsDefined(kind)) throw new ArgumentException("Invalid artifact kind."); Limit(kind, maximumLength); return OpenExisting(leaf, maximumLength, expected); }
    private UnixArchiveFile OpenExisting(string leaf, long maximumLength, FileIdentity? expected)
    {
        UnixArchiveNative.Leaf(leaf); Limit(UnixArchiveArtifactKind.Stage, maximumLength);
        lock (Gate)
        {
            RequireCapacity(); VerifyNamespace(); var held = UnixArchiveNative.Child(directory, leaf, write: expected.HasValue);
            return Adopt(held, leaf, maximumLength, artifact: expected.HasValue, writable: false, expected);
        }
    }
    private UnixArchiveFile Adopt(SafeFileHandle held, string leaf, long maximumLength, bool artifact, bool writable, FileIdentity? expected = null, UnixArchiveArtifactKind? createdKind = null)
    {
        try
        {
            var facts = UnixFiles.Stat(held) ?? throw UnixArchiveNative.Unavailable();
            if (!facts.IsRegular) throw UnixArchiveNative.Unavailable();
            if (facts.Size < 0 || facts.Size > maximumLength)
                throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The Unix archive file exceeds its physical bound.");
            if (expected is { } identity && facts.Identity != identity) throw UnixArchiveNative.Unavailable();
            if (artifact) UnixArchiveNative.RequirePrivate(held, user);
            VerifyNamespace(); RequireName(leaf, facts.Identity);
            var file = new UnixArchiveFile(this, held, leaf, facts.Identity, maximumLength, artifact, writable, createdKind); children++; return file;
        }
        catch { held.Dispose(); throw; }
    }
    internal void VerifyNamespace()
    {
        using var fresh = UnixArchiveNative.PinDirectory(DisplayPath);
        if (UnixFiles.Stat(fresh)?.Identity != Identity || UnixFiles.Stat(directory)?.Identity != Identity)
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The archive destination directory changed; retain its artifacts.");
        if (!Volume.Matches(directory)) throw UnixArchiveNative.Unavailable();
    }
    internal void RequireName(string leaf, FileIdentity identity)
    {
        using var named = UnixArchiveNative.Child(directory, leaf);
        if (UnixFiles.Stat(named)?.Identity != identity)
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The archive name identifies a different object; retain both objects.");
    }
    internal void CheckPrivate(SafeFileHandle held) => UnixArchiveNative.RequirePrivate(held, user);
    /// <summary>The reviewed archive folder of a creator-private stage folder; null for any other folder.</summary>
    internal UnixArchiveDirectory? PrivateStageParent => privateStageParent;
    /// <summary>
    /// Bind a held stage that was just moved into this folder under <paramref name="leaf"/>. The binding shares the stage's
    /// open file description (a duplicate descriptor), so no pathname reopen can substitute another object.
    /// </summary>
    internal UnixArchiveFile AdoptMovedStage(SafeFileHandle stage, string leaf, FileIdentity identity, long maximumLength)
    {
        UnixArchiveNative.Leaf(leaf);
        lock (Gate)
        {
            RequireCapacity();
            var held = UnixArchiveNative.Duplicate(stage);
            try
            {
                if (UnixFiles.Stat(held) is not { IsRegular: true } facts || facts.Identity != identity) throw UnixArchiveNative.Unavailable();
                RequireName(leaf, identity);
                var file = new UnixArchiveFile(this, held, leaf, identity, maximumLength, artifact: false, writable: false); children++; return file;
            }
            catch { held.Dispose(); throw; }
        }
    }
    internal void CheckCreationNamespace() => UnixArchiveNative.RequireCreationDirectory(directory, user);

    /// <summary>
    /// For a target probe: why the job would refuse to update the file named <paramref name="leaf"/> here, by the same
    /// update admission it applies before any stage exists (owner, special bits, flags, extended attributes, other hard
    /// links, a macOS entry that denies access) and the size it changes; null when it would not, or when nothing is there.
    /// Never follows a link; nothing is held afterwards.
    /// </summary>
    internal string? ProbeExisting(string leaf)
    {
        UnixArchiveNative.Leaf(leaf);
        lock (Gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(UnixArchiveDirectory));
            if (UnixFiles.StatChild(directory, leaf) is not { } facts) return null;
            if (facts.IsLink || !facts.IsRegular) return UnixArchiveJobBackend.LinkedArchive;
            if (facts.Size > ArchiveWritePolicy.MaxPhysicalBytes) return ArchiveListingRevision.TooLarge;
            using var held = UnixArchiveNative.Child(directory, leaf);
            var metadata = ArchiveUnixOuterMetadata.Capture(held);
            try { ArchiveUnixUpdateMetadata.RequireAdmissible(metadata, UnixArchiveNative.GetEuid()); }
            catch (ArchiveWriteException error) when (error.Reason == ArchiveWriteReason.UnsupportedFeature) { return error.Message; }
            if (!KeepsGroup(metadata.Native.Group)) return UnixArchiveFile.GroupNotKept;
            return UnixArchiveNative.Mac && UnixArchiveFile.MacAclHasDeny(UnixArchiveNative.Fd(held)) ? UnixArchiveFile.MacDenyAcl : null;
        }
    }

    /// <summary>
    /// Whether a replacement made here can have <paramref name="group"/>, as the update promises: the group a new file
    /// gets in the private stage folder made here (on macOS this folder's own, which the stage folder inherits; on Linux
    /// the process's, since the stage folder's own permissions clear a set-group-ID bit), or one this process may give it.
    /// </summary>
    internal bool KeepsGroup(uint group)
    {
        uint created = UnixArchiveNative.Mac ? ArchiveUnixOuterMetadata.CaptureDirectory(directory).Native.Group : UnixArchiveNative.GetEgid();
        return group == created || UnixArchiveNative.MayGiveGroup(group);
    }
    internal void RequirePrivateStageOf(UnixArchiveDirectory publicParent)
    {
        if (!ReferenceEquals(privateStageParent, publicParent) || Volume != publicParent.Volume || user != publicParent.user)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest,
                "The private stage directory is not a child of this reviewed archive destination.");
        VerifyNamespace(); publicParent.VerifyNamespace();
        string leaf = Path.GetFileName(DisplayPath);
        if (Path.GetDirectoryName(DisplayPath) != publicParent.DisplayPath ||
            !leaf.StartsWith(".filecat-archive-stage-", StringComparison.Ordinal))
            throw UnixArchiveNative.Unavailable();
        using var named = UnixArchiveNative.ChildDirectory(publicParent.Handle, leaf);
        if (UnixFiles.Stat(named)?.Identity != Identity)
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview,
                "The private archive stage directory changed; retain both namespaces.");
        UnixArchiveNative.RequireEditDirectory(directory, user);
    }
    private void RequireCapacity()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (children >= 16) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "Too many Unix archive file leases are open in this directory.");
    }
    private static void Limit(UnixArchiveArtifactKind kind, long maximum)
    {
        if (maximum < 0 || maximum > (kind == UnixArchiveArtifactKind.Stage ? ArchiveWritePolicy.MaxPhysicalBytes : ArchiveWritePolicy.MaxMetadataBytes))
            throw new ArgumentOutOfRangeException(nameof(maximum));
    }
    internal void ReleaseChild() { children--; if (disposed && children == 0) directory.Dispose(); }
    public void Dispose() { lock (Gate) { if (disposed) return; disposed = true; if (children == 0) directory.Dispose(); } }
}
