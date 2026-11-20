using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>Qualification-stage private evidence authority. Rolling no-follow directory opens fit its three-slot
/// envelope. It never borrows the storage worker allowance, follows a reparse point, or operates on a historical user path.</summary>
internal sealed partial class WindowsStorageReviewEvidence : StorageReviewEvidence
{
    internal static bool IsSupportedHost => WindowsStorageTransforms.IsQualifiedHost && !WindowsTokenAuthority.IsHighIntegrity;
    internal static readonly StorageEvidenceBackend Backend = CreateBackend(qualifyNativeArm64: false);
    internal static readonly StorageEvidenceBackend Arm64QualificationBackend = CreateBackend(qualifyNativeArm64: true);

    private static StorageEvidenceBackend CreateBackend(bool qualifyNativeArm64) => new(
        (directory, leaf, ct) => Open(directory, leaf, ct, qualifyNativeArm64),
        (directory, ct) => OpenRetained(directory, ct, qualifyNativeArm64),
        (directory, leaf, ct) => ObserveJournal(directory, leaf, ct, qualifyNativeArm64),
        (directory, artifacts, ct) => OpenCursor(directory, artifacts, ct, qualifyNativeArm64),
        (directory, leaf, ct) => OpenHistory(directory, leaf, ct, qualifyNativeArm64));
    private const uint ReadControl = 0x20000, NoRecall = 0x400000;
    private readonly string _path, _owner, _binding;
    private readonly ulong _serial;
    private readonly bool _qualifyNativeArm64;
    private readonly StorageEvidenceEnvelope _envelope;
    private readonly NativeHandle? _journal;
    private readonly FileStream? _stream;
    private string? _reviewedIdentity;
    private bool _disposed, _deleted;
    internal override string DirectoryIdentity { get; }
    internal override string JournalLeaf { get; }
    internal override string JournalIdentity
    {
        get { Check(); RequireFile(_journal!.Handle, _owner, _binding, CancellationToken.None); return Identity(_journal.Handle, _binding); }
    }
    internal override Stream Journal => _stream ?? throw new IOException("This namespace owner does not own an inactive journal.");
    internal override StorageEvidenceExport CreateExport(string destination, CancellationToken ct)
    { Check(); return CreateBoundExport(_envelope, DirectoryIdentity, _owner, destination, ct, _qualifyNativeArm64); }
    internal override bool IsOwned => NamespaceOwned && !_deleted && _journal is not null && !_journal.Handle.IsClosed;
    private bool NamespaceOwned => !_disposed;
    internal int MaximumHandles => _envelope.MaximumHandles;

    private WindowsStorageReviewEvidence(string path, string leaf, string owner, string binding, ulong serial,
        string directoryIdentity, NativeHandle? journal, StorageEvidenceEnvelope envelope, bool qualifyNativeArm64)
    {
        _path = path; JournalLeaf = leaf; _owner = owner; _binding = binding; _serial = serial;
        DirectoryIdentity = directoryIdentity; _journal = journal; _envelope = envelope;
        _qualifyNativeArm64 = qualifyNativeArm64;
        _stream = journal is null ? null : new FileStream(journal.Handle, FileAccess.Read);
    }

    internal static StorageRetainedReviewEvidence OpenRetained(string directory, CancellationToken ct, bool qualifyNativeArm64 = false)
    {
        if (StorageNativeAllowance.Current is not null) throw new IOException("Release native observation ownership before opening retained evidence.");
        var envelope = StorageEvidenceLanes.EnterEnvelope(ct);
        try
        {
            string owner = OrdinaryOwner(envelope.Claim, qualifyNativeArm64), full = FullDirectory(directory);
            using var root = Walk(full, envelope.Claim, writable: false, ct);
            RequireNamespace(root.Handle, owner, ct);
            string binding = WindowsStorageTransforms.ActualVolumeBinding(root.Handle, false, ct);
            var id = ProtectedHexFile.Identity(root.Handle);
            return new Retained(new(full, string.Empty, owner, binding, id.VolumeSerial, binding + ":" + id, null, envelope, qualifyNativeArm64));
        }
        catch { envelope.Dispose(); throw; }
    }

    internal static WindowsStorageReviewEvidence Open(string directory, string leaf, CancellationToken ct, bool qualifyNativeArm64 = false)
    {
        if (StorageNativeAllowance.Current is not null) throw new IOException("Release native storage observation ownership before opening independent review evidence.");
        var envelope = StorageEvidenceLanes.EnterEnvelope(ct); NativeHandle? journal = null;
        try
        {
            string owner = OrdinaryOwner(envelope.Claim, qualifyNativeArm64);
            string full = FullDirectory(directory); Leaf(leaf);
            if (!StorageJournalPolicy.IsCandidate(leaf)) throw new IOException("Only storage journals belong to this evidence authority.");
            using var root = Walk(full, envelope.Claim, writable: false, ct);
            RequireNamespace(root.Handle, owner, ct);
            string binding = WindowsStorageTransforms.ActualVolumeBinding(root.Handle, false, ct);
            var rootId = ProtectedHexFile.Identity(root.Handle);
            string rootIdentity = binding + ":" + rootId;
            journal = OpenFile(root.Handle, leaf, create: false, owner, binding, envelope.Claim, exclusive: true, ct);
            var result = new WindowsStorageReviewEvidence(full, leaf, owner, binding, rootId.VolumeSerial, rootIdentity, journal, envelope, qualifyNativeArm64);
            journal = null; return result;
        }
        catch { journal?.Dispose(); envelope.Dispose(); throw; }
    }

    private static StorageEvidenceObservation ObserveJournal(string directory, string leaf, CancellationToken ct, bool qualifyNativeArm64)
    {
        if (StorageNativeAllowance.Current is not null) throw new IOException("Release native observation ownership before independent evidence accounting.");
        using var envelope = StorageEvidenceLanes.EnterEnvelope(ct);
        string owner = OrdinaryOwner(envelope.Claim, qualifyNativeArm64), full = FullDirectory(directory); Leaf(leaf);
        using var root = Walk(full, envelope.Claim, writable: false, ct); RequireNamespace(root.Handle, owner, ct);
        string binding = WindowsStorageTransforms.ActualVolumeBinding(root.Handle, false, ct);
        using var file = OpenMetadata(root.Handle, leaf, envelope.Claim, ct); RequireFile(file.Handle, owner, binding, ct);
        return new(Identity(root.Handle, binding), Identity(file.Handle, binding), RandomAccess.GetLength(file.Handle));
    }

    internal static StorageJournalFile CreateJournal(string directory, string leaf, CancellationToken ct) =>
        CreateJournal(directory, leaf, ct, qualifyNativeArm64: false);

    internal static StorageJournalFile CreateArm64QualificationJournal(string directory, string leaf, CancellationToken ct) =>
        CreateJournal(directory, leaf, ct, qualifyNativeArm64: true);

    /// <summary>The worker creates its journal through its own scratch capacity, never waits for the evidence envelope.</summary>
    private static StorageJournalFile CreateJournal(string directory, string leaf, CancellationToken ct, bool qualifyNativeArm64)
    {
        var native = StorageNativeAllowance.RequireCurrent();
        string owner = OrdinaryOwner(native.ClaimScratch, qualifyNativeArm64);
        string full = FullDirectory(directory); Leaf(leaf);
        if (!StorageJournalPolicy.TryJobId(leaf, out _)) throw new IOException("A new storage journal needs its recognizable exact name.");
        using var root = Walk(full, native.ClaimScratch, writable: true, ct);
        RequireNamespace(root.Handle, owner, ct);
        string binding = WindowsStorageTransforms.ActualVolumeBinding(root.Handle, false, ct);
        NativeHandle? opened = OpenFile(root.Handle, leaf, create: true, owner, binding, native.ClaimScratch, exclusive: false, ct, deleteAccess: false);
        try
        {
            WindowsArchiveNative.Flush(root.Handle); // the recognizable namespace entry precedes any durable intent
            var stream = new FileStream(opened.Handle, FileAccess.ReadWrite);
            var file = new StorageJournalFile(stream, opened); opened = null; return file;
        }
        finally { opened?.Dispose(); }
    }

    internal override IReadOnlyList<StorageReviewArtifactInfo> Artifacts(Guid association, int maximum, CancellationToken ct)
    {
        CheckNamespace(); if (maximum is < 1 or > StorageReviewNames.ArtifactCount) throw new ArgumentOutOfRangeException(nameof(maximum));
        using var directory = Reviewed(create: false, ct);
        if (directory is null) return [];
        var entries = new List<StorageReviewArtifactInfo>();
        using var cursor = new WindowsStorageDirectoryCursor(directory.Handle);
        while (!cursor.IsComplete)
        {
            foreach (var entry in cursor.ReadBatch(ct))
            {
                if (!Associated(entry.Name, association)) continue;
                if (entry.IsDirectory || entry.ReparseTag != 0 || (entry.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || entry.ObjectId is null)
                    throw new IOException("An associated storage artifact is not a known ordinary object; it was retained.");
                using var metadata = OpenMetadata(directory.Handle, entry.Name, _envelope.Claim, ct);
                RequireFile(metadata.Handle, _owner, _binding, ct);
                var identity = ProtectedHexFile.Identity(metadata.Handle);
                if (identity.VolumeSerial != _serial || identity.FileId != entry.ObjectId || RandomAccess.GetLength(metadata.Handle) != entry.ByteLength)
                    throw new IOException("An associated storage artifact changed during its bounded inventory.");
                entries.Add(new(entry.Name, Identity(metadata.Handle, _binding), entry.ByteLength));
                if (entries.Count > maximum) return entries;
            }
        }
        RequireNamespace(directory.Handle, _owner, ct);
        return entries;
    }

    internal override StorageReviewArtifact OpenArtifact(StorageReviewArtifactInfo expected, CancellationToken ct)
    {
        CheckNamespace(); ArtifactLeaf(expected.Leaf);
        var directory = Reviewed(create: false, ct) ?? throw new FileNotFoundException("The storage review directory is missing.");
        NativeHandle? file = null;
        try
        {
            file = OpenFile(directory.Handle, expected.Leaf, false, _owner, _binding, _envelope.Claim, exclusive: false, ct);
            if (Identity(file.Handle, _binding) != expected.Identity || RandomAccess.GetLength(file.Handle) != expected.ByteLength)
                throw new IOException("The named storage artifact changed since its inventory.");
            var artifact = new Artifact(this, directory, file, expected.Leaf, writable: false); file = null; return artifact;
        }
        catch { file?.Dispose(); directory.Dispose(); throw; }
    }
    internal override StorageReviewArtifact CreateArtifact(string temporaryLeaf, CancellationToken ct)
    {
        Check(); ArtifactLeaf(temporaryLeaf);
        if (!StorageReviewNames.TryParse(temporaryLeaf, out _, out _, out _, out var temporary) || !temporary)
            throw new IOException("New storage artifacts need a private temporary name.");
        var directory = Reviewed(create: true, ct)!; NativeHandle? file = null;
        try
        {
            file = OpenFile(directory.Handle, temporaryLeaf, true, _owner, _binding, _envelope.Claim, exclusive: false, ct);
            var artifact = new Artifact(this, directory, file, temporaryLeaf, writable: true); file = null; return artifact;
        }
        catch { file?.Dispose(); directory.Dispose(); throw; }
    }
    internal override void PublishNew(StorageReviewArtifact artifact, string publishedLeaf, CancellationToken ct)
    {
        var owned = Own(artifact, ct); ArtifactLeaf(publishedLeaf);
        if (!StorageReviewNames.TryParse(owned.Leaf, out var association, out var kind, out var nonce, out var temporary) || !temporary ||
            !StorageReviewNames.TryParse(publishedLeaf, out var otherAssociation, out var otherKind, out var otherNonce, out var otherTemporary) ||
            otherTemporary || association != otherAssociation || kind != otherKind || nonce != otherNonce)
            throw new IOException("The storage publication names do not describe the same owned artifact.");
        owned.FlushDurable(ct); ct.ThrowIfCancellationRequested();
        NtFile.Rename(owned.File.Handle, owned.Directory.Handle, publishedLeaf, replace: false);
        owned.Name = publishedLeaf; // visible even if the following durability boundary fails
        WindowsArchiveNative.Flush(owned.Directory.Handle);
        RequireFile(owned.File.Handle, _owner, _binding, CancellationToken.None);
    }
    internal override void DeleteArtifact(StorageReviewArtifact artifact, StorageEvidenceVersion expected, CancellationToken ct)
    {
        var owned = Own(artifact, ct);
        if (JournalRecovery.InspectStorageEvidence(owned.Stream, () => owned.Identity, ct) != expected)
            throw new IOException("The private artifact changed before exact-object retirement.");
        NtFile.DeleteOpen(owned.File.Handle);
        owned.Deleted = true;
        WindowsArchiveNative.Flush(owned.Directory.Handle);
    }
    internal override void DeleteJournal(StorageEvidenceVersion expected, CancellationToken ct)
    {
        Check();
        if (JournalRecovery.InspectStorageEvidence(_stream!, () => JournalIdentity, ct) != expected) throw new IOException("The journal changed before pruning.");
        using var root = Root(writable: true, ct);
        RequireFile(_journal!.Handle, _owner, _binding, ct);
        NtFile.DeleteOpen(_journal.Handle); _deleted = true;
        WindowsArchiveNative.Flush(root.Handle);
    }

    private Artifact Own(StorageReviewArtifact artifact, CancellationToken ct)
    {
        CheckNamespace(); ct.ThrowIfCancellationRequested();
        if (artifact is not Artifact owned || !ReferenceEquals(owned.Owner, this) || owned.Closed || owned.Deleted)
            throw new IOException("This evidence owner does not own that live artifact.");
        RequireNamespace(owned.Directory.Handle, _owner, ct); RequireFile(owned.File.Handle, _owner, _binding, ct); return owned;
    }
    private NativeHandle Root(bool writable, CancellationToken ct, bool listing = false)
    {
        var root = Walk(_path, _envelope.Claim, writable, ct, listing);
        try
        {
            RequireNamespace(root.Handle, _owner, ct);
            if (WindowsStorageTransforms.ActualVolumeBinding(root.Handle, false, ct) != _binding || Identity(root.Handle, _binding) != DirectoryIdentity)
                throw new IOException("The storage journal directory was replaced.");
            return root;
        }
        catch { root.Dispose(); throw; }
    }
    private NativeHandle? Reviewed(bool create, CancellationToken ct)
    {
        using var root = Root(writable: create, ct);
        IDisposable? charge = _envelope.Claim(); SafeFileHandle? file = null;
        try
        {
            int status = NtFile.TryOpen(root.Handle, "reviewed", DirectoryRights(writable: true), NtFile.ShareRead | NtFile.ShareWrite,
                NtFile.Open, NtFile.DirectoryFile | NtFile.OpenReparsePoint, out file, caseSensitive: true);
            if (status is NtFile.NameNotFound or NtFile.PathNotFound)
            {
                file.Dispose(); file = null;
                if (!create) return null;
                status = NtFile.TryOpen(root.Handle, "reviewed", DirectoryRights(writable: true), NtFile.ShareRead | NtFile.ShareWrite,
                    NtFile.Create, NtFile.DirectoryFile | NtFile.OpenReparsePoint, out file, (uint)FileAttributes.Directory,
                    Security(_owner, directory: true), caseSensitive: true);
            }
            if (status < 0) throw NtFile.Error(status, "Open private storage review directory");
            RequireNamespace(file, _owner, ct);
            string identity = Identity(file, _binding);
            if (_reviewedIdentity is not null && _reviewedIdentity != identity) throw new IOException("The storage review directory was replaced.");
            _reviewedIdentity = identity;
            var held = new NativeHandle(file, charge); file = null; charge = null; return held;
        }
        finally { file?.Dispose(); charge?.Dispose(); }
    }

    private static NativeHandle Walk(string directory, Func<IDisposable> claim, bool writable, CancellationToken ct, bool listing = false)
    {
        string raw = directory.StartsWith(@"\\?\", StringComparison.Ordinal) ? directory[4..] : directory;
        int prefix = raw.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase) ? 45 : 3;
        string[] names = raw[prefix..].TrimEnd('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (names.Length is 0 or > 63) throw new IOException("Storage evidence requires a bounded directory below the volume root.");
        NativeHandle? current = OpenDirectory(null, @"\??\" + raw[..prefix], claim, writable: false, ct);
        try
        {
            _ = WindowsArchiveNative.Volume(current.Handle);
            _ = WindowsStorageTransforms.ActualVolumeBinding(current.Handle, rootOnly: true, ct);
            for (int i = 0; i < names.Length; i++)
            {
                RequireDirectory(current.Handle, ct);
                var next = OpenDirectory(current.Handle, names[i], claim, writable && i == names.Length - 1, ct, listing && i == names.Length - 1);
                current.Dispose(); current = next;
            }
            var result = current; current = null; return result;
        }
        finally { current?.Dispose(); }
    }
    private static NativeHandle OpenDirectory(SafeFileHandle? parent, string name, Func<IDisposable> claim, bool writable, CancellationToken ct, bool listing = false)
    {
        ct.ThrowIfCancellationRequested(); IDisposable? charge = claim(); SafeFileHandle? file = null;
        try
        {
            int status = NtFile.TryOpen(parent, name, DirectoryRights(writable, listing), NtFile.ShareRead | NtFile.ShareWrite, NtFile.Open,
                NtFile.DirectoryFile | NtFile.OpenReparsePoint, out file, caseSensitive: true);
            if (status < 0) throw NtFile.Error(status, "Open no-follow storage evidence directory");
            RequireDirectory(file, ct);
            var result = new NativeHandle(file, charge); file = null; charge = null; return result;
        }
        finally { file?.Dispose(); charge?.Dispose(); }
    }
    private static NativeHandle OpenFile(SafeFileHandle parent, string leaf, bool create, string owner, string binding,
        Func<IDisposable> claim, bool exclusive, CancellationToken ct, bool deleteAccess = true)
    {
        Leaf(leaf); ct.ThrowIfCancellationRequested(); IDisposable? charge = claim(); SafeFileHandle? file = null;
        try
        {
            uint access = NtFile.ReadData | NtFile.ReadAttributes | ReadControl | NtFile.Synchronize | (deleteAccess ? NtFile.Delete : 0) | (create ? NtFile.WriteData : 0);
            int status = NtFile.TryOpen(parent, leaf, access, exclusive ? 0 : NtFile.ShareRead, create ? NtFile.Create : NtFile.Open,
                NtFile.NonDirectoryFile | NtFile.OpenReparsePoint | NoRecall, out file, (uint)FileAttributes.Normal,
                create ? Security(owner, directory: false) : [], caseSensitive: true);
            if (status < 0) throw NtFile.Error(status, "Open private storage evidence file");
            RequireFile(file, owner, binding, ct);
            var result = new NativeHandle(file, charge); file = null; charge = null; return result;
        }
        finally { file?.Dispose(); charge?.Dispose(); }
    }
    private static NativeHandle OpenMetadata(SafeFileHandle parent, string leaf, Func<IDisposable> claim, CancellationToken ct)
    {
        Leaf(leaf); ct.ThrowIfCancellationRequested(); IDisposable? charge = claim(); SafeFileHandle? file = null;
        try
        {
            int status = NtFile.TryOpen(parent, leaf, NtFile.ReadAttributes | ReadControl | NtFile.Synchronize, NtFile.ShareAll,
                NtFile.Open, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint | NoRecall, out file, caseSensitive: true);
            if (status < 0) throw NtFile.Error(status, "Observe private storage artifact metadata");
            var result = new NativeHandle(file, charge); file = null; charge = null; return result;
        }
        finally { file?.Dispose(); charge?.Dispose(); }
    }
    private static uint DirectoryRights(bool writable, bool listing = false) => NtFile.ReadAttributes | ReadControl | NtFile.Synchronize |
        (writable ? NtFile.GenericRead | NtFile.GenericWrite : listing ? NtFile.ListDirectory : 0);
    private static void RequireDirectory(SafeFileHandle file, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); WindowsArchiveNative.SourceDirectory(file);
        uint attributes = NtFile.GetBasic(file).FileAttributes;
        if ((attributes & (0x1000u | 0x580000u | (uint)FileAttributes.Encrypted)) != 0 || WindowsStorageTransforms.SyncRootProblem(file, ct) is not null)
            throw new IOException("Storage evidence cannot use an encrypted, cloud-managed or unavailable directory.");
    }
    private static void RequireNamespace(SafeFileHandle file, string owner, CancellationToken ct)
    {
        RequireDirectory(file, ct);
        var security = new RawSecurityDescriptor(WindowsArchiveNative.ReadOwnerDaclSecurity(file), 0);
        if (security.Owner?.Value != owner || security.DiscretionaryAcl is not { Count: > 0 } acl)
            throw new UnauthorizedAccessException("The storage evidence directory is not an invoking-user namespace.");
        foreach (var entry in acl)
        {
            if (entry is not QualifiedAce ace) throw new UnauthorizedAccessException("Storage directory permissions are unqualified.");
            if (ace.AceQualifier != AceQualifier.AccessAllowed || (ace.AceFlags & AceFlags.InheritOnly) != 0) continue;
            if (ace.SecurityIdentifier.Value is not ("S-1-5-18" or "S-1-5-32-544") && ace.SecurityIdentifier.Value != owner &&
                (unchecked((uint)ace.AccessMask) & 0x500D0156u) != 0)
                throw new UnauthorizedAccessException("Other identities can change the storage evidence namespace.");
        }
    }
    private static void RequireFile(SafeFileHandle file, string owner, string binding, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); WindowsArchiveNative.Regular(file, long.MaxValue, artifact: true, mayRename: false);
        WindowsArchiveNative.RequirePrivateSecurity(file, owner);
        if ((NtFile.GetBasic(file).FileAttributes & (0x1000u | 0x580000u | 0x40000u)) != 0 || WindowsStorageTransforms.SyncRootProblem(file, ct) is not null)
            throw new IOException("Managed or unavailable storage evidence is not read or changed.");
        if (WindowsStorageTransforms.ActualVolumeBinding(file, false, ct) != binding) throw new IOException("Storage evidence crossed its held volume binding.");
        WindowsArchiveNative.RequireOnlyDataStream(file, RandomAccess.GetLength(file));
    }
    private static string Identity(SafeFileHandle file, string binding) => binding + ":" + ProtectedHexFile.Identity(file);
    private static string FullDirectory(string directory)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!StoragePaths.IsOrdinaryLocalPath(full)) throw new IOException("Storage evidence needs an ordinary local directory path.");
        return full;
    }
    private static string OrdinaryOwner(Func<IDisposable> claim, bool qualifyNativeArm64)
    {
        using var charge = claim();
        if (!WindowsStorageTransforms.SupportsHost(qualifyNativeArm64) || WindowsTokenAuthority.IsHighIntegrity)
            throw new UnauthorizedAccessException("Storage evidence requires a normal-user Windows 11 process.");
        using var identity = WindowsIdentity.GetCurrent(); return identity.User?.Value ?? throw new UnauthorizedAccessException("The invoking storage evidence identity is unavailable.");
    }
    private static byte[] Security(string owner, bool directory)
    {
        string flags = directory ? "OICI;" : ";";
        var descriptor = new RawSecurityDescriptor($"O:{owner}D:P(A;{flags}FA;;;{owner})(A;{flags}FA;;;SY)");
        byte[] bytes = new byte[descriptor.BinaryLength]; descriptor.GetBinaryForm(bytes, 0); return bytes;
    }
    private static bool Associated(string leaf, Guid association) => leaf.StartsWith($"storage-{association:N}-", StringComparison.OrdinalIgnoreCase) ||
        leaf.StartsWith($".storage-{association:N}-", StringComparison.OrdinalIgnoreCase);
    private static void ArtifactLeaf(string leaf)
    { Leaf(leaf); if (!StorageJournalPolicy.IsReviewArtifact(leaf)) throw new IOException("An artifact must belong to the private storage review namespace."); }
    private static void Leaf(string leaf)
    { if (!StoragePaths.IsChildName(leaf) || leaf.Length > 255) throw new IOException("An evidence name must be one bounded exact component."); }
    private void Check() { if (!IsOwned) throw new ObjectDisposedException(nameof(WindowsStorageReviewEvidence)); }
    private void CheckNamespace() { if (!NamespaceOwned) throw new ObjectDisposedException(nameof(WindowsStorageReviewEvidence)); }
    public override void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try { _stream?.Dispose(); } finally { try { _journal?.Dispose(); } finally { _envelope.Dispose(); } }
    }
    private sealed class NativeHandle(SafeFileHandle handle, IDisposable charge) : IDisposable
    {
        private IDisposable? _charge = charge;
        internal SafeFileHandle Handle => handle;
        internal void TransferCharge(IDisposable replacement) { Interlocked.Exchange(ref _charge, replacement)?.Dispose(); }
        public void Dispose() { try { handle.Dispose(); } finally { Interlocked.Exchange(ref _charge, null)?.Dispose(); } }
    }
    private sealed class Artifact(WindowsStorageReviewEvidence owner, NativeHandle directory, NativeHandle file, string leaf, bool writable) : StorageReviewArtifact
    {
        private NativeHandle? _directory = directory;
        internal WindowsStorageReviewEvidence Owner => owner;
        internal NativeHandle Directory => _directory ?? throw new IOException("The private artifact's directory owner is detached.");
        internal void DetachDirectory() { Interlocked.Exchange(ref _directory, null)?.Dispose(); }
        internal void AdoptDirectory(NativeHandle value) { if (_directory is not null) throw new InvalidOperationException(); _directory = value; }
        internal NativeHandle File => file;
        internal string Name = leaf;
        internal bool Closed, Deleted;
        private readonly FileStream _stream = new(file.Handle, writable ? FileAccess.ReadWrite : FileAccess.Read);
        internal override string Leaf => Name;
        internal override string Identity
        {
            get
            {
                owner.CheckNamespace(); if (Closed || Deleted) throw new ObjectDisposedException(nameof(Artifact));
                RequireFile(file.Handle, owner._owner, owner._binding, CancellationToken.None);
                return WindowsStorageReviewEvidence.Identity(file.Handle, owner._binding);
            }
        }
        internal override Stream Stream => _stream;
        internal override void FlushDurable(CancellationToken ct) { ct.ThrowIfCancellationRequested(); _stream.Flush(true); }
        public override void Dispose()
        {
            if (Closed) return; Closed = true;
            try { _stream.Dispose(); } finally { try { file.Dispose(); } finally { DetachDirectory(); } }
        }
    }

    private bool HasJournal(string historicalLeaf, Guid association, CancellationToken ct)
    {
        CheckNamespace(); Leaf(historicalLeaf);
        if (!StorageJournalPolicy.IsCandidate(historicalLeaf)) throw new IOException("A retained report can refer only to its private storage journal name.");
        using var root = Root(writable: false, ct, listing: true);
        using var cursor = new WindowsStorageDirectoryCursor(root.Handle);
        while (!cursor.IsComplete)
            foreach (var entry in cursor.ReadBatch(ct))
                if (entry.Name.Equals(historicalLeaf, StringComparison.OrdinalIgnoreCase) ||
                    StorageJournalPolicy.TryJobId(entry.Name, out var job) && job == association) return true;
        return false;
    }

    private sealed class Retained(WindowsStorageReviewEvidence store) : StorageRetainedReviewEvidence
    {
        internal override string DirectoryIdentity => store.DirectoryIdentity;
        internal override bool IsOwned => store.NamespaceOwned;
        internal override IReadOnlyList<StorageReviewArtifactInfo> Artifacts(Guid association, int maximum, CancellationToken ct) => store.Artifacts(association, maximum, ct);
        internal override StorageReviewArtifact OpenArtifact(StorageReviewArtifactInfo expected, CancellationToken ct) => store.OpenArtifact(expected, ct);
        internal override bool JournalExists(string historicalLeaf, Guid association, CancellationToken ct) => store.HasJournal(historicalLeaf, association, ct);
        internal override void DeleteArtifact(StorageReviewArtifact artifact, StorageEvidenceVersion expected, CancellationToken ct) =>
            throw new IOException("Retained artifact retirement requires a fresh journal-absence check.");
        internal override void DeleteUnreferenced(StorageReviewArtifact artifact, StorageEvidenceVersion expected, string historicalLeaf, Guid association, CancellationToken ct)
        {
            var owned = store.Own(artifact, ct);
            // Keep the exact file held while temporarily releasing its directory, so the absence walk fits three slots.
            owned.DetachDirectory();
            if (store.HasJournal(historicalLeaf, association, ct)) throw new IOException("A storage journal still depends on this retained review.");
            owned.AdoptDirectory(store.Reviewed(create: false, ct) ?? throw new IOException("The retained review directory disappeared."));
            store.DeleteArtifact(owned, expected, ct);
        }
        public override void Dispose() => store.Dispose();
    }
}
