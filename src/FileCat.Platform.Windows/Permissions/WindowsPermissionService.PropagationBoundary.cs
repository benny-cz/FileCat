using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Permissions;

internal sealed unsafe partial class WindowsPermissionService
{
    internal sealed record PropagationMember(string RelativePath, PermissionIdentity Identity, long? DirectoryChangeTime);

    // A complete observation is evidence about the inspected namespace. It cannot seal the tree during a native call.
    internal sealed record PropagationBoundary(string Locator, PermissionBinding Root, ImmutableArray<PermissionIdentity> Ancestors,
        ImmutableArray<PropagationMember> Members);

    internal static PropagationBoundary CapturePropagationBoundary(PermissionSnapshot selected, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var subject = selected.Subject;
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            selected.Model != PermissionModel.WindowsDescriptor ||
            selected.Binding is not { FileSystem: "NTFS", ReadOnly: false } binding ||
            binding.Identity.Kind is not (PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot) ||
            subject.LinkPolicy != PermissionLinkPolicy.NoFollow || !PermissionSnapshots.IsLocal(subject) ||
            !StoragePaths.IsOrdinaryLocalPath(subject.RawLocator))
            throw new NotSupportedException("Propagation needs a bound folder on a writable fixed local NTFS volume.");

        var (root, names) = SplitLocalPath(subject.RawLocator);
        if (names.Length > PermissionSnapshots.MaxPathDepth || subject.RawLocator.Length > ReviewedPermissionOperation.LocatorLimit)
            throw new NotSupportedException("The selected folder exceeds the propagation review path limit.");
        try
        {
            using var held = Walk(root, names, subject.Authority, ct, out var parent, out var chain, out var actual);
            parent?.Dispose();
            if (actual != binding || !PermissionSnapshots.Matches(subject, actual.Identity))
                throw new PermissionBindingConflictException("The selected folder or volume changed before the boundary could be captured.");

            var capture = new PropagationBoundaryCapture(binding, subject.RawLocator.Length, names.Length, ct);
            capture.VisitDirectory(held, chain[^1], "", 0);
            using var current = Walk(root, names, subject.Authority, ct, out var currentParent, out var currentChain, out var currentBinding);
            using (currentParent)
                if (currentBinding != binding || !SameChain(chain, currentChain) || chain[^1] != currentChain[^1])
                    throw new PermissionBindingConflictException("The selected folder or its ancestry changed during boundary inspection.");
            return new(subject.RawLocator, binding, chain.Select(stamp => stamp.Identity).ToImmutableArray(), capture.Members());
        }
        catch (ReadFailure error) when (error.Status == PermissionReadStatus.Unsupported) { throw new NotSupportedException(error.Message, error); }
        catch (ReadFailure error) { throw ReadError(error); }
        catch (Win32Exception error) { throw new IOException("The Windows propagation boundary could not be corroborated.", error); }
    }

    internal static void RequireUnchangedPropagationBoundary(PermissionSnapshot selected, PropagationBoundary reviewed, CancellationToken ct)
    {
        if (selected.Subject.RawLocator != reviewed.Locator || selected.Binding != reviewed.Root)
            throw new PermissionBindingConflictException("The selected folder differs from the reviewed boundary.");
        PropagationBoundary current;
        try { current = CapturePropagationBoundary(selected, ct); }
        catch (NotSupportedException error)
        {
            throw new PermissionBindingConflictException("The reviewed propagation boundary can no longer be established. " + error.Message);
        }
        if (!current.Ancestors.AsSpan().SequenceEqual(reviewed.Ancestors.AsSpan()) ||
            !current.Members.AsSpan().SequenceEqual(reviewed.Members.AsSpan()))
            throw new PermissionBindingConflictException("The propagation boundary changed. Review the current tree.");
    }

    private sealed class PropagationBoundaryCapture(PermissionBinding root, int locatorLength, int ancestorDepth, CancellationToken ct)
    {
        // Each recursive directory owns a metadata and listing handle and a directory cursor.
        private const int ActiveDirectoryLimit = (ReviewedPermissionOperation.TargetLimit - 3) / 2;
        private const long MemberByteLimit = (PermissionTreeManifest.ByteLimit -
            3L * WindowsStorageDirectoryCursor.BufferBytes * ActiveDirectoryLimit) / 2; // Review and live comparison coexist.
        private readonly List<PropagationMember> _members = [];
        private readonly HashSet<FileIdentity> _identities = [];
        private long _memberBytes;

        internal void VisitDirectory(SafeFileHandle held, FileStamp before, string relative, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (depth >= ActiveDirectoryLimit || ancestorDepth + depth > PermissionSnapshots.MaxPathDepth)
                throw new NotSupportedException("The tree exceeds the depth that can be inspected with retained directory handles.");
            AddMember(relative, before);
            int status = NtFile.TryReopenDirectoryForListing(held, out var listing);
            using (listing)
            {
                if (status < 0) throw NativeFailure("List the held folder", StatusToDosError(status));
                CheckDirectory(listing, before);
                using var cursor = new WindowsStorageDirectoryCursor(listing);
                while (!cursor.IsComplete)
                    foreach (var entry in cursor.ReadBatch(ct))
                        VisitChild(listing, entry, relative, depth + 1);
                CheckDirectory(listing, before);
            }
        }

        private void VisitChild(SafeFileHandle parent, WindowsStorageDirectoryCursor.Entry entry, string parentRelative, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if ((entry.Attributes & UnsafeAttributes) != 0 || entry.ReparseTag != 0)
                throw new NotSupportedException("The tree contains a reparse point, mounted folder, offline entry or an entry that requires recall.");
            if (entry.ObjectId is null)
                throw new NotSupportedException("A discovered entry has no stable native identity.");
            string relative = parentRelative.Length == 0 ? entry.Name : parentRelative + "\\" + entry.Name;
            if (locatorLength + relative.Length + 1 > ReviewedPermissionOperation.LocatorLimit ||
                ancestorDepth + depth > PermissionSnapshots.MaxPathDepth)
                throw new NotSupportedException("A discovered path exceeds the propagation review path or depth limit.");
            using var held = Open(parent, entry.Name, security: false, ct);
            var stamp = Screen(held, root.Identity.MountInstance, root: false, root.Identity.Authority, ct);
            if (!string.Equals(stamp.Identity.Object.FileId, entry.ObjectId, StringComparison.Ordinal) ||
                (stamp.Identity.Kind == PermissionObjectKind.Directory) != entry.IsDirectory ||
                stamp.Identity.Object.VolumeSerial != root.Identity.Object.VolumeSerial)
                throw new PermissionBindingConflictException("A discovered entry changed identity, kind or volume during boundary inspection.");
            if (entry.IsDirectory) VisitDirectory(held, stamp, relative, depth);
            else
            {
                if (stamp.Links != 1)
                    throw new NotSupportedException("The tree contains a file with another hard-link name.");
                AddMember(relative, stamp);
            }
        }

        private void CheckDirectory(SafeFileHandle listing, FileStamp before)
        {
            if (Screen(listing, root.Identity.MountInstance, before.Identity.Kind == PermissionObjectKind.VolumeRoot, root.Identity.Authority, ct) != before)
                throw new PermissionBindingConflictException("A directory changed while its membership was being inspected.");
        }

        private void AddMember(string relative, FileStamp stamp)
        {
            _memberBytes += 256L + 2L * relative.Length;
            if (_members.Count == PermissionTreeManifest.EntryLimit || _memberBytes > MemberByteLimit)
                throw new NotSupportedException("The tree exceeds the propagation review entry or memory limit.");
            if (!_identities.Add(stamp.Identity.Object))
                throw new NotSupportedException("The tree contains more than one name for an object.");
            _members.Add(new(relative, stamp.Identity, stamp.Identity.Kind == PermissionObjectKind.File ? null : stamp.ChangeTime));
        }

        internal ImmutableArray<PropagationMember> Members()
        {
            _members.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
            return _members.ToImmutableArray();
        }
    }
}
