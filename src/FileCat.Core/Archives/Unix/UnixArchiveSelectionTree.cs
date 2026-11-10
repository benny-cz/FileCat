using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Unix;

/// <summary>One retained pane root, bounded transient traversal, lazy parent/file leases; no per-member FD.</summary>
internal sealed class UnixArchiveSelectionTree : IArchiveSelectionTree
{
    private readonly UnixArchiveDirectory root;
    private UnixArchiveSelectionTree(string path) => root = UnixArchiveDirectory.Open(ArchiveLocalPath.ResolveDirectory(path));
    internal static IArchiveSelectionTree Open(string path) => new UnixArchiveSelectionTree(path);
    internal static ArchiveCapturedSelection Capture(string path, IReadOnlyList<string> roots, CancellationToken ct)
        => ArchiveCapturedSelection.Capture(new UnixArchiveSelectionTree(path), roots, ct);
    public ArchiveSelectionEntry InspectRoot(string leaf, CancellationToken ct)
    {
        try { UnixArchiveNative.Leaf(leaf); }
        catch (ArgumentException) { return new(leaf, ArchiveSelectionKind.UnrepresentableName, ""); }
        lock (root.Gate) { ct.ThrowIfCancellationRequested(); root.RequireSelectionRoot(); var entry = UnixArchiveNative.InspectSelection(root.Handle, leaf); root.RequireSelectionRoot(); return entry; }
    }
    public void Enumerate(ArchiveSelectionNode directory, Action<ArchiveSelectionEntry> visit, CancellationToken ct)
    {
        using var held = OpenDirectory(directory, ct);
        UnixArchiveNative.EnumerateSelection(held, visit, ct);
        root.RequireSelectionRoot();
    }
    public void CaptureFile(ArchiveCapturedInputs inputs, int id, ArchiveSelectionNode file, CancellationToken ct)
        => inputs.Capture(id, () => OpenFile(file, ct), ct);
    public void CaptureDirectory(ArchiveCapturedInputs inputs, int id, ArchiveSelectionNode directory, CancellationToken ct)
        => inputs.CaptureDirectory(id, token =>
        {
            using var parent = OpenParent(directory, token);
            var observation = parent.Owner.ObserveSelectionDirectory(directory.Entry.Leaf, token);
            if (observation.Version.Identity != directory.Entry.Identity) throw ArchiveCapturedSelection.Changed();
            return observation;
        }, ct);
    public void ReviewDestination(string path, string identity, IReadOnlySet<string> selectedDirectories, CancellationToken ct)
    {
        root.RequireSelectionRoot(); var held = UnixArchiveNative.PinDirectory(path);
        try
        {
            var facts = UnixFiles.Stat(held) ?? throw ArchiveCapturedSelection.Changed();
            if (facts.Identity.ToString() != identity) throw ArchiveCapturedSelection.Changed();
            // Inspect native ancestor identities, including excluded subtrees; a textual prefix comparison
            // would miss case/normalization aliases and could incorrectly permit staging inside the input.
            for (int depth = 0; ; depth++)
            {
                ct.ThrowIfCancellationRequested(); if (depth > ArchiveWritePolicy.MaxNameChars) throw ArchiveWriteBudget.Limit();
                if (selectedDirectories.Contains(facts.Identity.ToString()))
                    throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Choose an archive destination outside the selected source folders.");
                var parent = UnixArchiveNative.SelectionAncestor(held);
                var before = facts;
                try { facts = UnixFiles.Stat(parent) ?? throw ArchiveCapturedSelection.Changed(); }
                catch { parent.Dispose(); throw; }
                held.Dispose(); held = parent; if (facts.Identity == before.Identity) break;
            }
            root.RequireSelectionRoot();
        }
        finally { held.Dispose(); }
    }
    private ArchiveOwnedSourceFile<UnixArchiveFile> OpenFile(ArchiveSelectionNode node, CancellationToken ct)
    {
        var parent = OpenParent(node, ct); UnixArchiveFile? file = null;
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
        using var verified = OpenDirectory(node.Parent, ct);
        string path = Path.Combine(root.DisplayPath, node.Parent.NativeRelativePath);
        var parent = UnixArchiveDirectory.Open(path);
        try
        {
            if (parent.Identity.ToString() != node.Parent.Entry.Identity || UnixFiles.Stat(verified)?.Identity != parent.Identity) throw ArchiveCapturedSelection.Changed();
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
                    ct.ThrowIfCancellationRequested(); var child = UnixArchiveNative.ChildDirectory(held ?? root.Handle, current.Entry.Leaf);
                    try { if (UnixFiles.Stat(child)?.Identity.ToString() != current.Entry.Identity) throw ArchiveCapturedSelection.Changed(); }
                    catch { child.Dispose(); throw; }
                    held?.Dispose(); held = child;
                }
                root.RequireSelectionRoot(); return held ?? throw ArchiveCapturedSelection.Changed();
            }
            catch { held?.Dispose(); throw; }
        }
    }
    private sealed class Parent(UnixArchiveDirectory owner, bool owned) : IDisposable
    {
        internal UnixArchiveDirectory Owner => owner;
        public void Dispose() { if (owned) owner.Dispose(); }
    }
    public void Dispose() => root.Dispose();
}
