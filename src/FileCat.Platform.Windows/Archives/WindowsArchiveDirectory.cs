using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.State;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Archives;

internal enum WindowsArchiveArtifactKind { Stage, Manifest, Provenance }

internal sealed class WindowsArchiveArtifactCreationException(string retainedPath, Exception inner)
    : IOException($"The created archive artifact could not be admitted; retain it for review: {retainedPath}", inner)
{
    public string RetainedPath { get; } = retainedPath; // Evidence only; never cleanup authority.
}

/// <summary>
/// Parent-owned native storage primitives, deliberately unregistered. All ancestors and the chosen directory are
/// pinned without following links. Children use one-component relative opens. Observed NTFS support is not release
/// qualification: provenance, metadata, directory-entry durability and the journaled publication protocol are separate
/// gates. In particular, a successful file flush does not claim power-loss durability of a rename.
/// </summary>
internal sealed partial class WindowsArchiveDirectory : IDisposable, IArchiveDestinationSpace
{
    internal readonly object Gate = new();
    private readonly LocalIconFileLease directory;
    private readonly byte[] privateSecurity;
    private readonly string owner;
    private int children;
    private bool disposed;
    internal SafeFileHandle Handle => directory.LastHandle; // Only while Gate is held and a live owner/child exists.
    public string DisplayPath { get; }
    public FileIdentity Identity { get; }
    public WindowsArchiveVolume Volume { get; }

    private WindowsArchiveDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows archive handles are required.");
        if (WindowsTokenAuthority.IsHighIntegrity)
            throw new UnauthorizedAccessException("New archive writes require an unelevated FileCat process.");
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute archive directory is required.", nameof(path));
        DisplayPath = Path.GetFullPath(path);
        directory = LocalIconFileLease.TryOpenDirectory(DisplayPath, LeaseLinks.Refuse, removable: true)
            ?? throw new IOException("The archive directory could not be pinned without following links.");
        try
        {
            Volume = WindowsArchiveNative.Volume(Handle);
            Identity = ProtectedHexFile.Identity(Handle);
            using var identity = WindowsIdentity.GetCurrent();
            owner = identity.User?.Value ?? throw new UnauthorizedAccessException("The archive owner is unavailable.");
            // Applied in FILE_CREATE itself, never after a window of inherited access. No package/AAP/Everyone ACE.
            var descriptor = new RawSecurityDescriptor($"O:{owner}D:P(A;;FA;;;{owner})(A;;FA;;;SY)");
            privateSecurity = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(privateSecurity, 0);
        }
        catch { directory.Dispose(); throw; }
    }

    public static WindowsArchiveDirectory Open(string path) => new(path);

    ArchiveSpaceReview IArchiveDestinationSpace.ReviewSpace(string expectedParentIdentity, ArchiveSpaceEstimate estimate, CancellationToken ct)
    {
        lock (Gate)
        {
            EnsureOpen(); ct.ThrowIfCancellationRequested(); VerifyNamespace();
            if (expectedParentIdentity != Identity.ToString()) throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The space-review destination is a different retained directory.");
            var capacity = FileSystemCapacity.QueryHeldDirectory(Handle, Identity.VolumeSerial);
            ct.ThrowIfCancellationRequested(); VerifyNamespace(); return new(expectedParentIdentity, estimate, capacity);
        }
    }

    public WindowsArchiveFile CreateArtifact(Guid nonce, WindowsArchiveArtifactKind kind, long maximumLength)
        => CreateArtifact(ReserveArtifact(nonce, kind, maximumLength));

    internal ArchiveArtifactReservation ReserveArtifact(Guid nonce, WindowsArchiveArtifactKind kind, long maximumLength)
    {
        if (nonce == Guid.Empty || !Enum.IsDefined(kind)) throw new ArgumentException("Invalid archive artifact scope.");
        ValidateLimit(kind, maximumLength);
        lock (Gate)
        {
            EnsureOpen(); RequireCapacity(); VerifyNamespace();
            return new(this, nonce, (ArchiveArtifactKind)kind, DisplayPath, Identity.ToString(), maximumLength);
        }
    }

    internal WindowsArchiveFile CreateArtifact(ArchiveArtifactReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        lock (Gate)
        {
            EnsureOpen(); RequireCapacity(); VerifyNamespace(); reservation.Claim(this);
            string leaf = reservation.LeafName;
            int status = TryOpen(Handle, leaf, GenericRead | GenericWrite | Delete | Synchronize | 0x20000 /* READ_CONTROL */,
                ShareRead, Create, NonDirectoryFile | OpenReparsePoint, out var handle,
                (uint)FileAttributes.Normal, privateSecurity);
            if (status < 0) { handle.Dispose(); throw Error(status, "Create the exact exclusive private archive artifact reservation"); }
            // Never retry with an unrecorded name or clean up an uncertain post-create object.
            try { return Adopt(handle, leaf, reservation.MaximumLength, artifact: true, writable: true, mayRename: true, createdKind: (WindowsArchiveArtifactKind)reservation.Kind); }
            catch (Exception ex) { throw new WindowsArchiveArtifactCreationException(Path.Combine(DisplayPath, leaf), ex); }
        }
    }

    public WindowsArchiveFile OpenSource(string leaf, long maximumLength = ArchiveWritePolicy.MaxPhysicalBytes) =>
        OpenExisting(leaf, maximumLength, artifact: false, mayRename: false, expected: null);

    internal void CaptureSource(ArchiveCapturedInputs inputs, int id, string leaf, CancellationToken ct)
    { ArgumentNullException.ThrowIfNull(inputs); ValidateLeaf(leaf); inputs.Capture(id, () => OpenSource(leaf, ArchiveWritePolicy.MaxPayloadBytes), ct); }

    public WindowsArchiveFile OpenReviewedTarget(string leaf) =>
        OpenExisting(leaf, ArchiveWritePolicy.MaxPhysicalBytes, artifact: false, mayRename: true, expected: null);

    public WindowsArchiveFile OpenRecoveryArtifact(string leaf, FileIdentity expected, WindowsArchiveArtifactKind kind,
        long maximumLength)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Invalid archive artifact kind.");
        ValidateLimit(kind, maximumLength);
        return OpenExisting(leaf, maximumLength, artifact: true, mayRename: true, expected);
    }

    internal WindowsArchiveFile? OpenRecoveryArtifactOrAbsent(string leaf, FileIdentity expected, WindowsArchiveArtifactKind kind, long maximumLength)
    {
        ValidateLeaf(leaf); ValidateLimit(kind, maximumLength);
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Invalid recovery artifact kind.");
        lock (Gate)
        {
            EnsureOpen(); RequireCapacity(); VerifyNamespace();
            int status = TryOpen(Handle, leaf, GenericRead | Delete | Synchronize | 0x20000, ShareRead, NtFile.Open,
                NonDirectoryFile | OpenReparsePoint, out var handle);
            if (status == NameNotFound) { handle.Dispose(); return null; }
            if (status < 0) { handle.Dispose(); throw Error(status, "Open the exact recovery artifact or confirm name absence"); }
            return Adopt(handle, leaf, maximumLength, true, false, true, expected, kind);
        }
    }
    internal void RequireRecoveryNameAbsent(string leaf)
    {
        ValidateLeaf(leaf);
        lock (Gate)
        {
            EnsureOpen(); VerifyNamespace();
            int status = TryOpen(Handle, leaf, ReadAttributes | Synchronize, ShareAll, NtFile.Open, OpenReparsePoint, out var handle);
            handle.Dispose();
            if (status == NameNotFound) return;
            if (status < 0) throw Error(status, "Confirm recovery artifact name absence");
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "A processed archive artifact name is occupied; keep that object for review.");
        }
    }

    private WindowsArchiveFile OpenExisting(string leaf, long maximumLength, bool artifact, bool mayRename, FileIdentity? expected)
    {
        ValidateLeaf(leaf); ValidateLimit(WindowsArchiveArtifactKind.Stage, maximumLength);
        lock (Gate)
        {
            EnsureOpen();
            RequireCapacity();
            // Deny existing/future data writers and namespace mutation. Unlike FileShare on Unix, this is a Windows
            // kernel sharing check, including already-open incompatible handles. ADS/provenance still needs rereads.
            var handle = OpenOrThrow(Handle, leaf, GenericRead | Synchronize | 0x20000 | (mayRename ? Delete : 0),
                ShareRead, NtFile.Open, NonDirectoryFile | OpenReparsePoint, "Open held archive file");
            return Adopt(handle, leaf, maximumLength, artifact, writable: false, mayRename, expected);
        }
    }

    private WindowsArchiveFile Adopt(SafeFileHandle handle, string leaf, long maximumLength, bool artifact, bool writable,
        bool mayRename, FileIdentity? expected = null, WindowsArchiveArtifactKind? createdKind = null)
    {
        try
        {
            WindowsArchiveNative.Regular(handle, maximumLength, artifact, mayRename);
            var identity = ProtectedHexFile.Identity(handle);
            if (expected is { } wanted && identity != wanted) throw new IOException("The archive artifact is a different object; retain it for review.");
            if (artifact) WindowsArchiveNative.RequirePrivateSecurity(handle, owner);
            var result = new WindowsArchiveFile(this, handle, leaf, identity, maximumLength, artifact, writable, mayRename, createdKind);
            children++;
            return result;
        }
        catch { handle.Dispose(); throw; }
    }

    internal void CheckPrivate(SafeFileHandle handle) => WindowsArchiveNative.RequirePrivateSecurity(handle, owner);

    internal void VerifyNamespace()
    {
        using var fresh = LocalIconFileLease.TryOpenDirectory(DisplayPath, LeaseLinks.Refuse, removable: true);
        if (fresh is null || ProtectedHexFile.Identity(fresh.LastHandle) != Identity || ProtectedHexFile.Identity(Handle) != Identity || WindowsArchiveNative.Volume(Handle) != Volume)
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The retained archive directory changed; preserve its objects for review.");
    }
    internal void RequireName(string leaf, FileIdentity expected)
    {
        // Attribute-only observation permits our existing data/delete access without granting a new data writer.
        using var named = OpenOrThrow(Handle, leaf, 0x80 /* FILE_READ_ATTRIBUTES */ | Synchronize, ShareRead | ShareWrite | ShareDelete,
            NtFile.Open, NonDirectoryFile | OpenReparsePoint, "Review retained archive name");
        if (ProtectedHexFile.Identity(named) != expected)
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The retained archive name now identifies another object.");
    }

    internal void ReleaseChild()
    {
        // Called with Gate held. Disposing the directory owner cannot invalidate its outstanding child leases.
        children--;
        if (disposed && children == 0) directory.Dispose();
    }

    private static void ValidateLimit(WindowsArchiveArtifactKind kind, long maximumLength)
    {
        if (maximumLength < 0 || maximumLength > (kind == WindowsArchiveArtifactKind.Stage
                ? ArchiveWritePolicy.MaxPhysicalBytes : ArchiveWritePolicy.MaxMetadataBytes))
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
    }

    internal static void ValidateLeaf(string leaf)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        if (leaf.Length is < 1 or > 255 || leaf is "." or ".." || leaf[^1] is '.' or ' ' ||
            leaf.Any(c => c < 32 || c == 127 || "<>:\"/\\|?*".Contains(c)))
            throw new ArgumentException("A single unambiguous Windows filename is required.", nameof(leaf));
        for (int i = 0; i < leaf.Length; i++)
            if (char.IsSurrogate(leaf[i]) && (!char.IsHighSurrogate(leaf[i]) || ++i == leaf.Length || !char.IsLowSurrogate(leaf[i])))
                throw new ArgumentException("The Windows filename has invalid Unicode.", nameof(leaf));
        string stem = leaf.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
            "123456789¹²³".Contains(stem[3]))
            throw new ArgumentException("A Windows device name cannot be an archive filename.", nameof(leaf));
    }

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(disposed, this);
    private void RequireCapacity()
    {
        // This local bound is additional to (not a substitute for) the worker/job-wide input lease reservation.
        if (children >= 16) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "Too many archive file leases are open in this directory.");
    }
    public void Dispose()
    {
        lock (Gate)
        {
            if (disposed) return;
            disposed = true;
            if (children == 0) directory.Dispose();
        }
    }
}
