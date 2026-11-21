using System.Collections.Immutable;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Permissions;

internal sealed unsafe partial class WindowsPermissionService
{
    internal sealed record AclResetRow(string Path, ImmutableArray<WindowsAce> Before, ImmutableArray<WindowsAce> After, bool Changed);
    internal sealed record AclResetReview(ReviewedPermissionOperation Operation, ImmutableArray<AclResetRow> Rows);

    internal bool CanReviewAclReset(PermissionSnapshot snapshot) => _qualifyAclResets && IsReviewableNtfsFolder(snapshot) &&
        snapshot.Binding!.Identity.Kind == PermissionObjectKind.Directory &&
        snapshot.Access.Value?.Acl is WindowsDacl { Revision: 2 } source && HasSimpleFolderDacl(source);

    internal AclResetReview PrepareAclReset(PermissionInspection inspection, ItemRef source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var selected = inspection.Snapshot;
        if (!CanReviewAclReset(selected) || source.Kind != EntryKind.Directory || source.FileSystemPath != selected.Subject.RawLocator)
            throw new NotSupportedException("Select an original fixed NTFS folder with a readable access ACL as the reset source.");
        var boundary = CapturePropagationBoundary(selected, ct);
        if (boundary.Members.Length > FileTreeMemberLimit)
            throw new NotSupportedException($"Choose a smaller folder with at most {FileTreeMemberLimit} total entries.");

        const PermissionScope scope = PermissionScope.ResetTree;
        var before = RequireRevision(selected);
        var sourceMutation = BindDiscretionaryReview(inspection, before, WindowsPermissionToken.CurrentStamp(), ct,
            treeAncestry: boundary.Ancestors);
        var template = new ReviewedPermissionTreeSource(selected.Subject, boundary.Root, before, sourceMutation);
        var entries = new List<PermissionTreeEntry> { new(-1, 0, template.Subject, template.Binding, template.Revision, new(scope, [])) };
        var parents = new Dictionary<string, ResetParent>(StringComparer.Ordinal)
        {
            [string.Empty] = new(0, 0, boundary.Ancestors, (WindowsDacl)selected.Access.Value!.Value.Acl, [template])
        };
        var prepared = new List<(ReviewedPermissionTarget Target, AclResetRow Row, int Depth)>();
        int protectedEntries = 0;
        bool transferred = false;
        try
        {
            foreach (var member in boundary.Members.Skip(1))
            {
                ct.ThrowIfCancellationRequested();
                string parentPath = Path.GetDirectoryName(member.RelativePath) ?? string.Empty;
                // Descendants of a protected directory are outside the reset.
                if (!parents.TryGetValue(parentPath, out var parent)) continue;
                bool directory = member.Identity.Kind == PermissionObjectKind.Directory;
                string path = Path.Combine(boundary.Locator, member.RelativePath);
                var item = ItemRef.ForFileSystemPath(path, directory ? EntryKind.Directory : EntryKind.File);
                var subject = new PermissionSubject(item.Parent, path, path, boundary.Root.Identity.Authority, member.Identity.Kind,
                    PermissionLinkPolicy.NoFollow, ExpectedIdentity: member.Identity);
                using var child = Inspect(subject, DiscretionarySections, ct);
                var snapshot = child.Snapshot;
                var binding = snapshot.Binding;
                if (binding?.Identity != member.Identity || !directory && binding.LinkCount != 1)
                    throw new PermissionBindingConflictException(snapshot.BindingProblem ?? "A captured object or its original name changed.");
                if (snapshot.Inheritance.Value?.IsProtected == true) { protectedEntries++; continue; }
                if (prepared.Count == ReviewedPermissionOperation.TargetLimit)
                    throw new NotSupportedException($"Review a smaller tree with at most {ReviewedPermissionOperation.TargetLimit} reset targets.");
                if (!TryGetUnprotectedDacl(snapshot, out var baseline, out _))
                    throw new NotSupportedException($"Could not verify an unprotected discretionary ACL for {path}.");

                var change = new ResetWindowsDacl(MergeParentInheritance(snapshot, [], parent.Projected, directory));
                var chain = parent.Ancestry.Add(member.Identity);
                var (target, expected) = PrepareChangeTarget(child, item, change, null, ct, treeAncestry: chain);
                var component = target.Components[0];
                var projected = (WindowsDacl)expected.Access.Value!.Value.Acl;
                int depth = parent.Depth + 1;
                var row = new AclResetRow(path, baseline.Entries, projected.Entries, component.Before != component.Expected);
                prepared.Add((target with { Mutation = new ResetMutation(target.Mutation, parent.Sources) }, row, depth));
                if (directory)
                    parents.Add(member.RelativePath, new(entries.Count, depth, chain, projected,
                        parent.Sources.Add(new(subject, binding, component.Before, target.Mutation))));
                entries.Add(new(parent.Index, depth, subject, binding, component.Before, new(scope, [change])));
            }
            if (prepared.Count == 0) throw new NotSupportedException("This folder contains no eligible descendants to reset.");
            var manifest = PermissionTreeManifest.Capture(entries, 0, ct, protectedEntries: protectedEntries);
            var ordered = prepared.OrderByDescending(entry => entry.Depth).ToArray();
            var operation = new ReviewedPermissionOperation(scope, ordered.Select(entry => entry.Target).ToImmutableArray(), manifest, template);
            transferred = true;
            return new(operation, ordered.Select(entry => entry.Row).ToImmutableArray());
        }
        finally
        {
            if (!transferred)
            {
                foreach (var entry in prepared) entry.Target.Mutation.Dispose();
                sourceMutation.Dispose();
            }
        }
    }

    private static (PermissionSnapshot Expected, uint Section, ImmutableArray<byte> Bytes) ProjectTreeReset(PermissionSnapshot original,
        ResetWindowsDacl change)
    {
        if (!TryGetUnprotectedDacl(original, out _, out var control))
            throw new NotSupportedException("Windows reset requires an unprotected DACL with verified automatic inheritance.");
        string authority = original.Subject.Authority.Id;
        var projected = WindowsPermissionDescriptor.ProjectDacl(control, change.InheritedAcl, authority);
        if (projected.Form != WindowsDaclForm.Entries || projected.Revision != 2 || !HasSimpleFolderDacl(projected) ||
            projected.Entries.Any(rule => (rule.Flags & InheritedAce) == 0))
            throw new NotSupportedException("Windows did not derive a complete inherited access ACL for this object.");
        var expected = original with { Access = original.Access with { Value = new PermissionAclValue(projected) } };
        return (expected, DaclInformation, ObjectDaclDescriptor(control, change.InheritedAcl, authority));
    }

    private sealed record ResetParent(int Index, int Depth, ImmutableArray<PermissionIdentity> Ancestry, WindowsDacl Projected,
        ImmutableArray<ReviewedPermissionTreeSource> Sources);

    private sealed class ResetMutation(IBoundPermissionMutation target, ImmutableArray<ReviewedPermissionTreeSource> sources) : IBoundPermissionMutation
    {
        public PermissionSnapshot CheckCurrent(PermissionSections sections, CancellationToken cancellation)
        {
            RequireCurrentSources(cancellation);
            return target.CheckCurrent(sections, cancellation);
        }

        public PermissionMutationReturn Apply(PermissionDelta change) => Apply(change, CancellationToken.None);

        public PermissionMutationReturn Apply(PermissionDelta change, CancellationToken cancellation)
        {
            try { RequireCurrentSources(cancellation); }
            catch (PermissionBindingConflictException error)
            { return new(RejectedBeforeChange: true, BindingConflict: true, Detail: error.Message); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return new(CanceledBeforeChange: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { return new(RejectedBeforeChange: true, FailedWithoutDispatch: true, Detail: error.Message); }
            return target.Apply(change, cancellation);
        }

        public PermissionReadBack Observe(PermissionSections sections) => target.Observe(sections);
        public void Dispose() => target.Dispose();

        private void RequireCurrentSources(CancellationToken cancellation)
        {
            foreach (var source in sources)
            {
                var observed = source.Mutation.CheckCurrent(source.Revision.Sections, cancellation);
                if (observed.Binding != source.Binding || PermissionRevision.Read(observed, source.Revision.Sections) != source.Revision)
                    throw new PermissionBindingConflictException("An inheritance source changed after the Windows reset was reviewed.");
            }
        }
    }
}
