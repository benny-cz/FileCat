using System.Collections.Immutable;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Permissions;

internal sealed unsafe partial class WindowsPermissionService
{
    internal sealed record AccessTreeRow(string Path, int RulePosition, bool AlreadyPresent);
    internal sealed record AccessTreeReview(ReviewedPermissionOperation Operation, ImmutableArray<AccessTreeRow> Rows, int Directories);

    internal bool CanReviewAccessTree(PermissionSnapshot snapshot, PermissionObjectKind targetKind) =>
        (targetKind switch
        {
            PermissionObjectKind.File => _qualifyFileTrees,
            PermissionObjectKind.Directory => _qualifyFolderTrees,
            _ => false
        }) &&
        ReviewedPermissionOperation.ProcessRefusal() is null && PermissionSnapshots.IsLocal(snapshot.Subject) &&
        snapshot.Subject.LinkPolicy == PermissionLinkPolicy.NoFollow && snapshot is
        {
            Model: PermissionModel.WindowsDescriptor, BindingProblem: null, Mount: null,
            Binding: { FileSystem: "NTFS", ReadOnly: false, Identity.Kind: PermissionObjectKind.Directory }
        };

    internal AccessTreeReview PrepareAccessTree(PermissionSnapshot selected, ItemRef folder, AddWindowsAce change,
        PermissionObjectKind targetKind, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!CanReviewAccessTree(selected, targetKind) || folder.Kind != EntryKind.Directory || folder.FileSystemPath != selected.Subject.RawLocator ||
            change.Entry.Flags != 0 || change.Entry.Kind is not (WindowsAceKind.Allow or WindowsAceKind.Deny))
            throw new NotSupportedException("Review a non-inheriting allow or deny rule in one bound fixed NTFS folder.");
        bool folders = targetKind == PermissionObjectKind.Directory;
        var boundary = CapturePropagationBoundary(selected, ct);
        int count = boundary.Members.Skip(1).Count(member => member.Identity.Kind == targetKind);
        if (count == 0)
            throw new NotSupportedException(folders ? "This folder contains no descendant folders to review." : "This folder contains no files to review.");
        if (count > ReviewedPermissionOperation.TargetLimit || boundary.Members.Length > ReviewedTreeMemberLimit)
            throw new NotSupportedException($"Choose a smaller folder with at most {ReviewedPermissionOperation.TargetLimit} targets and {ReviewedTreeMemberLimit} total entries.");

        const PermissionScope scope = PermissionScope.ReviewedTree;
        var root = boundary.Root;
        var entries = new List<PermissionTreeEntry>(boundary.Members.Length)
        {
            new(-1, 0, selected.Subject, root, default, new(scope, [])) { VisitOnly = true }
        };
        var directories = new Dictionary<string, (int Index, int Depth, ImmutableArray<PermissionIdentity> Ancestry)>(StringComparer.Ordinal)
        {
            [string.Empty] = (0, 0, boundary.Ancestors)
        };
        var prepared = new List<(ReviewedPermissionTarget Target, AccessTreeRow Row, int Depth)>(count);
        bool transferred = false;
        try
        {
            foreach (var member in boundary.Members.Skip(1))
            {
                ct.ThrowIfCancellationRequested();
                bool file = member.Identity.Kind == PermissionObjectKind.File;
                if (file && folders) continue;
                string path = Path.Combine(boundary.Locator, member.RelativePath);
                string parent = Path.GetDirectoryName(member.RelativePath) ?? string.Empty;
                var ancestry = directories[parent];
                var chain = ancestry.Ancestry.Add(member.Identity);
                int depth = ancestry.Depth + 1;
                var item = ItemRef.ForFileSystemPath(path, file ? EntryKind.File : EntryKind.Directory);
                var subject = new PermissionSubject(item.Parent, path, path, root.Identity.Authority, member.Identity.Kind,
                    PermissionLinkPolicy.NoFollow, ExpectedIdentity: member.Identity);
                if (!file)
                {
                    directories.Add(member.RelativePath, (entries.Count, depth, chain));
                    if (!folders)
                    {
                        entries.Add(new(ancestry.Index, depth, subject, root with { Identity = member.Identity, LinkCount = null }, default,
                            new(scope, [])) { VisitOnly = true });
                        continue;
                    }
                }

                using var inspection = Inspect(subject, DiscretionarySections, ct);
                var snapshot = inspection.Snapshot;
                if (snapshot.Binding?.Identity != member.Identity || file && snapshot.Binding.LinkCount != 1)
                    throw new PermissionBindingConflictException(snapshot.BindingProblem ?? "A captured object changed identity or a file acquired another name.");
                if (folders)
                {
                    if (!IsReviewableNtfsFolder(snapshot) || !TryGetUnprotectedDacl(snapshot, out _, out _))
                        throw new NotSupportedException($"{path}: Captured folder changes require a qualified unprotected DACL.");
                }
                else if (EditRefusal(snapshot) is { } refusal) throw new NotSupportedException($"{path}: {refusal}");

                var (target, expected) = PrepareChangeTarget(inspection, item, change, null, ct, treeAncestry: chain, folderOnly: folders);
                var component = target.Components[0];
                var acl = (WindowsDacl)expected.Access.Value!.Value.Acl;
                int position = 0;
                foreach (var rule in acl.Entries)
                {
                    position++;
                    if (rule.Bytes.AsSpan().SequenceEqual(change.Entry.Bytes.AsSpan())) break;
                }
                prepared.Add((target, new(path, position, component.Before == component.Expected), depth));
                entries.Add(new(ancestry.Index, depth, subject, snapshot.Binding, component.Before, new(scope, [change])));
            }

            var manifest = PermissionTreeManifest.Capture(entries, 0, ct);
            var ordered = prepared.OrderByDescending(entry => entry.Depth).ToArray();
            var operation = new ReviewedPermissionOperation(scope, ordered.Select(entry => entry.Target).ToImmutableArray(), manifest);
            transferred = true;
            return new(operation, ordered.Select(entry => entry.Row).ToImmutableArray(), directories.Count);
        }
        finally
        {
            if (!transferred)
                foreach (var entry in prepared) entry.Target.Mutation.Dispose();
        }
    }
}
