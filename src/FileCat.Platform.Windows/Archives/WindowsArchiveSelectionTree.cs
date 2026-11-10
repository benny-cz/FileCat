using System.Buffers.Binary;
using System.ComponentModel;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Archives;

internal sealed class WindowsArchiveSelectionTree : IArchiveSelectionTree, IArchiveSelectionRemoval
{
    private readonly WindowsArchiveDirectory root;
    private readonly Dictionary<ArchiveSelectionNode, string?> removalDirectories = [];
    private WindowsArchiveSelectionTree(string path) => root = WindowsArchiveDirectory.Open(path);
    internal static IArchiveSelectionTree Open(string path) => new WindowsArchiveSelectionTree(path);
    public bool CanRemoveSources => true;
    internal static ArchiveCapturedSelection Capture(string path, IReadOnlyList<string> roots, CancellationToken ct)
        => ArchiveCapturedSelection.Capture(new WindowsArchiveSelectionTree(path), roots, ct);
    public ArchiveSelectionEntry InspectRoot(string leaf, CancellationToken ct)
    {
        try { WindowsArchiveDirectory.ValidateSelectionLeaf(leaf); }
        catch (ArgumentException) { return new(leaf, ArchiveSelectionKind.UnrepresentableName, ""); }
        lock (root.Gate) { ct.ThrowIfCancellationRequested(); root.RequireSelectionRoot(); var entry = Inspect(root.Handle, leaf); root.RequireSelectionRoot(); return entry; }
    }
    private static ArchiveSelectionEntry Inspect(SafeFileHandle parent, string leaf)
    {
        WindowsArchiveDirectory.ValidateSelectionLeaf(leaf);
        using var child = OpenOrThrow(parent, leaf, ReadAttributes | Synchronize, ShareAll, NtFile.Open, OpenReparsePoint, "Inspect selected archive input");
        uint attributes = GetBasic(child).FileAttributes;
        return new(leaf, IsReparsePoint(attributes) ? ArchiveSelectionKind.Link : IsDirectory(attributes) ? ArchiveSelectionKind.Directory : ArchiveSelectionKind.File,
            ProtectedHexFile.Identity(child).ToString());
    }
    public void Enumerate(ArchiveSelectionNode directory, Action<ArchiveSelectionEntry> visit, CancellationToken ct)
    {
        using var held = OpenDirectory(directory, ct);
        WindowsArchiveNative.SourceDirectoryNamespace(held, ct, (leaf, id, attributes) =>
        {
            ct.ThrowIfCancellationRequested();
            if (leaf is null) { visit(new("", ArchiveSelectionKind.UnrepresentableName, "")); return; }
            try { ArchiveWritePolicy.ValidateName(leaf); WindowsArchiveDirectory.ValidateSelectionLeaf(leaf); }
            catch (Exception ex) when (ex is ArgumentException || ex is ArchiveWriteException { Reason: ArchiveWriteReason.InvalidRequest })
            { visit(new(leaf, ArchiveSelectionKind.UnrepresentableName, "")); return; }
            var entry = Inspect(held, leaf);
            // NTFS FileIdBothDirectoryInformation carries the low 64 bits of FILE_ID_128, in native byte order.
            var identity = entry.Identity.AsSpan(17, 16);
            if (BinaryPrimitives.ReadUInt64LittleEndian(Convert.FromHexString(identity.ToString())) != id ||
                entry.Kind != (IsReparsePoint(attributes) ? ArchiveSelectionKind.Link : IsDirectory(attributes) ? ArchiveSelectionKind.Directory : ArchiveSelectionKind.File))
                throw ArchiveCapturedSelection.Changed();
            visit(entry);
        });
        root.RequireSelectionRoot();
    }
    public void CaptureFile(ArchiveCapturedInputs inputs, int id, ArchiveSelectionNode file, CancellationToken ct)
        => inputs.Capture(id, () => OpenFile(file, ct), ct);
    public void CaptureDirectory(ArchiveCapturedInputs inputs, int id, ArchiveSelectionNode directory, CancellationToken ct)
    {
        ArchiveDirectoryObservation? initial = null;
        inputs.CaptureDirectory(id, token =>
        {
            using var parent = OpenParent(directory, token);
            var observation = parent.Owner.ObserveSelectionDirectory(directory.Entry.Leaf, token);
            if (observation.Version.Identity != directory.Entry.Identity) throw ArchiveCapturedSelection.Changed();
            initial ??= observation;
            return observation;
        }, ct);
        inputs.RetainSelection(64, 384); // Dictionary entry and the current digest after our own child deletions.
        removalDirectories.Add(directory, initial?.RemovalMetadataDigest ?? throw ArchiveCapturedSelection.Changed());
    }
    // Handle-bound deletion: no aside name is used, so none is ever journaled for Windows.
    public void RemoveFile(ArchiveSelectionNode node, ArchiveFileObservation expected, CancellationToken ct, string? asideLeaf = null)
    {
        if (node.Entry.Kind != ArchiveSelectionKind.File || node.Entry.Identity != expected.Version.Identity) throw ArchiveCapturedSelection.Changed();
        using var parent = OpenParent(node, ct);
        string? stableParent = ReviewRemovalParent(node, ct);
        using var file = parent.Owner.OpenReviewedTarget(node.Entry.Leaf);
        file.DeleteCapturedSource(expected, ct);
        RecordOwnChildRemoval(node, stableParent);
    }
    public void RemoveDirectory(ArchiveSelectionNode node, ArchiveDirectoryObservation expected, CancellationToken ct)
    {
        if (node.Entry.Kind != ArchiveSelectionKind.Directory || node.Entry.Identity != expected.Version.Identity) throw ArchiveCapturedSelection.Changed();
        using var parent = OpenParent(node, ct);
        string? stableParent = ReviewRemovalParent(node, ct);
        if (!removalDirectories.TryGetValue(node, out string? current) || current is null) throw ArchiveCapturedSelection.Changed();
        parent.Owner.RemoveCapturedDirectory(node.Entry.Leaf, expected, current, ct);
        RecordOwnChildRemoval(node, stableParent);
    }
    private string? ReviewRemovalParent(ArchiveSelectionNode node, CancellationToken ct)
    {
        if (node.Parent is not { } folder) return null;
        if (!removalDirectories.TryGetValue(folder, out string? current) || current is null) throw ArchiveCapturedSelection.Changed();
        var observed = RemovalMetadata(folder, ct);
        if (observed.Digest != current) throw ArchiveCapturedSelection.Changed();
        return observed.StableDigest;
    }
    private (string Digest, string StableDigest) RemovalMetadata(ArchiveSelectionNode folder, CancellationToken ct)
    {
        using var parent = OpenParent(folder, ct);
        return parent.Owner.ObserveRemovalDirectory(folder.Entry.Leaf, folder.Entry.Identity, ct);
    }
    private void RecordOwnChildRemoval(ArchiveSelectionNode node, string? stableParent)
    {
        if (node.Parent is not { } folder) return;
        // The deletion already committed. Failure of this bounded follow-up poisons the parent's guard;
        // it must not turn a removed file into a claimed retained file or permit another deletion attempt.
        removalDirectories[folder] = null;
        try
        {
            var after = RemovalMetadata(folder, CancellationToken.None);
            if (after.StableDigest == stableParent) removalDirectories[folder] = after.Digest;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or Win32Exception) { }
    }
    public void ReviewDestination(string path, string identity, IReadOnlySet<string> selectedDirectories, CancellationToken ct)
    {
        root.RequireSelectionRoot();
        using var held = LocalIconFileLease.TryOpenDirectory(path, LeaseLinks.Refuse, removable: true)
            ?? throw ArchiveCapturedSelection.Changed();
        if (ProtectedHexFile.Identity(held.LastHandle).ToString() != identity) throw ArchiveCapturedSelection.Changed();
        if (held.ContainsAnyIdentity(selectedDirectories, ct))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Choose an archive destination outside the selected source folders.");
        ct.ThrowIfCancellationRequested(); root.RequireSelectionRoot();
    }
    private ArchiveOwnedSourceFile<WindowsArchiveFile> OpenFile(ArchiveSelectionNode node, CancellationToken ct)
    {
        var parent = OpenParent(node, ct); WindowsArchiveFile? file = null;
        try
        {
            ct.ThrowIfCancellationRequested(); file = parent.Owner.OpenSource(node.Entry.Leaf, ArchiveWritePolicy.MaxPayloadBytes);
            if (((IArchiveRetainedFile)file).ObjectIdentity != node.Entry.Identity) throw ArchiveCapturedSelection.Changed();
            return new(file, parent);
        }
        catch { try { file?.Dispose(); } finally { parent.Dispose(); } throw; }
    }
    private Parent OpenParent(ArchiveSelectionNode node, CancellationToken ct)
    {
        if (node.Parent is null) { ct.ThrowIfCancellationRequested(); root.RequireSelectionRoot(); return new(root, false); }
        // Pin the parent path against rename/delete, then check each selected ancestor before granting reads.
        string path = Path.Combine(root.DisplayPath, node.Parent.NativeRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var parent = WindowsArchiveDirectory.Open(path);
        try
        {
            using var verified = OpenDirectory(node.Parent, ct);
            if (parent.Identity.ToString() != node.Parent.Entry.Identity || ProtectedHexFile.Identity(verified) != parent.Identity) throw ArchiveCapturedSelection.Changed();
            ct.ThrowIfCancellationRequested(); return new(parent, true);
        }
        catch { parent.Dispose(); throw; }
    }
    private SafeFileHandle OpenDirectory(ArchiveSelectionNode node, CancellationToken ct)
    {
        lock (root.Gate)
        {
            ct.ThrowIfCancellationRequested(); root.RequireSelectionRoot();
            var ancestors = new Stack<ArchiveSelectionNode>();
            for (var current = node; current is not null; current = current.Parent) ancestors.Push(current);
            SafeFileHandle? held = null;
            try
            {
                while (ancestors.TryPop(out var current))
                {
                    ct.ThrowIfCancellationRequested(); var child = OpenOrThrow(held ?? root.Handle, current.Entry.Leaf,
                        ListDirectory | ReadAttributes | Synchronize, ShareAll, NtFile.Open, DirectoryFile | OpenReparsePoint, "Open captured source folder");
                    try { WindowsArchiveNative.SourceDirectory(child); if (ProtectedHexFile.Identity(child).ToString() != current.Entry.Identity) throw ArchiveCapturedSelection.Changed(); }
                    catch { child.Dispose(); throw; }
                    held?.Dispose(); held = child;
                }
                root.RequireSelectionRoot(); return held ?? throw ArchiveCapturedSelection.Changed();
            }
            catch { held?.Dispose(); throw; }
        }
    }
    private sealed class Parent(WindowsArchiveDirectory owner, bool owned) : IDisposable
    {
        internal WindowsArchiveDirectory Owner => owner;
        public void Dispose() { if (owned) owner.Dispose(); }
    }
    public void Dispose() => root.Dispose();
}
