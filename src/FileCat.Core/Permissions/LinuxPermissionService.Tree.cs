using System.Collections.Immutable;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Permissions;

internal sealed unsafe partial class LinuxPermissionService
{
    // Each admitted object retains one descriptor. Leave room under the handle limit for observation and journal calls.
    internal const int ModeTreeTargetLimit = 96;
    private const int ModeTreeListingLimit = 4096;
    private const long ModeTreeAclPreviewBytes = 24 * 1024 * 1024;

    internal PermissionTreeReview PrepareModeTree(IEnumerable<(ItemRef Item, PermissionSubject Subject)> selection,
        ChangePermissionMode change, bool exactExecute, bool acceptSetIdClear, CancellationToken ct, int retainedInspections = 0)
    {
        if (change is not { Bits: <= PermissionBits, TouchedBits: > 0 and <= PermissionBits })
            throw new ArgumentException("Review at least one supported set or clear permission bit.", nameof(change));
        return PrepareTree(selection, change, exactExecute, acceptSetIdClear, ct, retainedInspections);
    }

    internal PermissionTreeReview PrepareAclResetTree(ItemRef folder, PermissionSubject subject, bool exactExecute,
        bool acceptSetIdClear, CancellationToken ct, int retainedInspections = 0) =>
        PrepareTree([(folder, subject)], null, exactExecute, acceptSetIdClear, ct, retainedInspections);

    private PermissionTreeReview PrepareTree(IEnumerable<(ItemRef Item, PermissionSubject Subject)> selection,
        ChangePermissionMode? change, bool exactExecute, bool acceptSetIdClear, CancellationToken ct, int retainedInspections)
    {
        if (ReviewedPermissionOperation.ProcessRefusal() is { } refusal) throw new UnauthorizedAccessException(refusal);
        bool reset = change is null;
        var scope = reset ? PermissionScope.ResetTree : PermissionScope.ReviewedTree;
        ArgumentOutOfRangeException.ThrowIfNegative(retainedInspections);
        int limit = Math.Max(0, ModeTreeTargetLimit - retainedInspections);
        var candidates = selection.Take(limit + 1).ToArray();
        if (candidates.Length == 0 || candidates.Length > limit)
            throw new NotSupportedException("Review a smaller selection of files and directories.");
        var roots = new List<(ItemRef Item, PermissionSubject Subject)>();
        foreach (var candidate in candidates.OrderBy(candidate => candidate.Subject.RawLocator.Length))
        {
            var subject = candidate.Subject;
            if (!PermissionSnapshots.IsLocal(subject) || subject.LinkPolicy != PermissionLinkPolicy.NoFollow ||
                subject.RawLocator != candidate.Item.FileSystemPath || !IsExactPath(subject.RawLocator))
                throw new NotSupportedException("Reviewed trees require exact local paths with no resolved links.");
            if (!roots.Any(root => root.Subject.RawLocator == subject.RawLocator ||
                root.Item.Kind == EntryKind.Directory && subject.RawLocator.StartsWith(root.Subject.RawLocator + "/", StringComparison.Ordinal)))
                roots.Add(candidate);
        }

        var entries = new List<PermissionTreeEntry>();
        var prepared = new Dictionary<string, (ReviewedPermissionTarget Target, PermissionTreeRow Row)>(StringComparer.Ordinal);
        ReviewedPermissionTreeSource? templateSource = null;
        PosixAcl? template = null;
        ChangePosixAcl? resetExact = null, resetWithoutExecute = null;
        int inaccessible = 0, excluded = 0, protectedEntries = 0, observedNames = 0;
        long aclPreviewBytes = 0;
        try
        {
            foreach (var root in roots)
            {
                using var inspection = Inspect(root.Subject, PermissionSections.Ordinary, ct);
                if (ModeReviewRefusal(inspection.Snapshot) is { } problem) throw new NotSupportedException(problem);
                if (reset) CaptureTemplate(inspection);
                else Visit(root.Item, inspection, -1, 0);
            }
            if (prepared.Count == 0)
                throw reset ? new NotSupportedException("There are no eligible captured descendants to reset.")
                    : new UnauthorizedAccessException(ClearProtectionFirst);
            var manifest = PermissionTreeManifest.Capture(entries, inaccessible, ct, excluded, protectedEntries);
            var admitted = manifest.Entries.Where(entry => !reset || entry.ParentIndex >= 0)
                .OrderByDescending(entry => entry.Depth).Select(entry => prepared[entry.Subject.RawLocator]).ToArray();
            var rows = admitted.Select(entry => entry.Row).ToImmutableArray();
            var operation = new ReviewedPermissionOperation(scope,
                admitted.Select(entry => entry.Target).ToImmutableArray(), manifest, templateSource);
            templateSource = null;
            foreach (var row in rows) prepared.Remove(row.Subject.RawLocator);
            return new(operation, rows, template?.Entries ?? default);
        }
        finally
        {
            foreach (var entry in prepared.Values) entry.Target.Mutation.Dispose();
            templateSource?.Mutation.Dispose();
        }

        void CaptureTemplate(PermissionInspection inspection)
        {
            var snapshot = inspection.Snapshot;
            var binding = snapshot.Binding!;
            if (binding.Identity.Kind != PermissionObjectKind.Directory || snapshot.Defaults is not
                { Status: PermissionReadStatus.Present, Value.Acl: PosixAcl acl } || acl.Entries.IsEmpty)
                throw new NotSupportedException("Select an ordinary folder with a readable POSIX default ACL to use as the reset template.");
            var revision = RequireRevision(snapshot, PermissionSections.Defaults);
            template = acl;
            resetExact = new(false, acl.Entries);
            ushort withoutExecute = (ushort)(LinuxPermissionAcl.ProjectMode(acl, 0) & ~(ushort)UnixPermissions.AnyExecute);
            resetWithoutExecute = new(false, LinuxPermissionAcl.WithMode(acl, withoutExecute, snapshot.Subject.Authority.Id).Entries);
            var pin = inspection.TransferLease<LinuxBoundPermission>() ?? throw new InvalidOperationException("The reset source binding was released.");
            templateSource = new(snapshot.Subject, binding, revision, pin);
            entries.Add(new(-1, 0, snapshot.Subject, binding, revision, new(scope, [])));
            VisitChildren(snapshot, pin, 0, 0);
        }

        void Visit(ItemRef item, PermissionInspection inspection, int parentIndex, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (entries.Count == limit)
                throw new NotSupportedException($"Review at most {limit} captured names in this batch.");
            var snapshot = inspection.Snapshot;
            var binding = snapshot.Binding!;
            bool directory = binding.Identity.Kind == PermissionObjectKind.Directory;
            ushort before = snapshot.Mode.Value!.Value;
            PermissionDelta delta;
            if (change is { } modeChange)
            {
                ushort touched = modeChange.TouchedBits;
                var mode = UnixPermissions.Apply((UnixFileMode)before, (UnixFileMode)(modeChange.Bits & touched), (UnixFileMode)touched,
                    directory, inside: parentIndex != -1 && !exactExecute);
                delta = new ChangePermissionMode((ushort)mode, touched);
            }
            else
                delta = directory || exactExecute || ((UnixFileMode)before & UnixPermissions.AnyExecute) != 0 ? resetExact! : resetWithoutExecute!;
            var access = snapshot.Access.Value?.Acl as PosixAcl;
            aclPreviewBytes += ((access?.Entries.Length ?? 0) + (delta is ChangePosixAcl aclChange ? aclChange.Entries.Length : 0)) * 256L;
            if (aclPreviewBytes > ModeTreeAclPreviewBytes)
                throw new NotSupportedException("The ACL previews exceed the tree review limit. Review smaller batches.");
            var (target, expected) = PrepareTarget(inspection, item, new(PermissionScope.Objects, [delta]), acceptSetIdClear, ct);
            var row = new PermissionTreeRow(snapshot.Subject, before, expected.Mode.Value!.Value, binding.LinkCount, access);
            var mutation = (LinuxBoundPermission)target.Mutation;
            if (parentIndex < 0 && mutation.TreeAncestry[^1].ResetUnprotected == false)
            {
                // A protected selection is counted and left out with its branch, like a protected descendant.
                target.Mutation.Dispose();
                protectedEntries++;
                return;
            }
            if (!prepared.TryAdd(snapshot.Subject.RawLocator, (target, row)))
            {
                target.Mutation.Dispose();
                throw new PermissionBindingConflictException("A directory entry appeared more than once during review.");
            }
            if (templateSource?.Mutation is LinuxBoundPermission templatePin)
            {
                mutation.PreserveTreeResetProtection(templatePin.TreeAncestry.Length);
                prepared[snapshot.Subject.RawLocator] = (target, row with
                {
                    ResetBefore = LinuxPermissionAcl.AccessRows(snapshot), ResetAfter = LinuxPermissionAcl.AccessRows(expected)
                });
            }
            var component = target.Components[0];
            int ordinal = entries.Count;
            entries.Add(new(parentIndex, depth, snapshot.Subject, binding, component.Before,
                new(scope, [component.Change])));
            if (directory) VisitChildren(snapshot, mutation, ordinal, depth);
        }

        void VisitChildren(PermissionSnapshot snapshot, LinuxBoundPermission parent, int ordinal, int depth)
        {
            var children = new List<(string? Name, ulong Inode)>();
            Stamp listing;
            try
            {
                listing = parent.EnumerateTree((name, inode) =>
                {
                    if (++observedNames > ModeTreeListingLimit)
                        throw new NotSupportedException("The tree exceeds the directory entry review limit. Review smaller batches.");
                    children.Add((name, inode));
                }, ct);
            }
            catch (Exception error) when (error is ReadFailure || error is IOException and not ArchiveWriteException ||
                error is ArchiveWriteException { Reason: ArchiveWriteReason.SourceChanged })
            {
                inaccessible++;
                return;
            }
            foreach (var (name, inode) in children.OrderBy(child => child.Name, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (name is null || !IsExactPath(Path.Join(snapshot.Subject.RawLocator, name))) { inaccessible++; continue; }
                Stamp facts;
                try { facts = parent.ReadTreeChild(name, inode); }
                catch (ReadFailure error) when (error.Error is 18 or 40) { excluded++; continue; } // EXDEV or ELOOP
                catch (ReadFailure) { inaccessible++; continue; }
                if (facts.FileKind is not (FileType or DirectoryType)) { excluded++; continue; }
                // Immutable and append-only entries, with the branch beneath a protected directory, stay out of every
                // captured tree. A reset also needs proof that an entry is unprotected.
                if (facts.ResetUnprotected == false) { protectedEntries++; continue; }
                if (reset && facts.ResetUnprotected != true) { inaccessible++; continue; }
                var child = new ItemRef(Location.FileSystem(snapshot.Subject.RawLocator), name,
                    facts.FileKind == FileType ? EntryKind.File : EntryKind.Directory);
                string path = child.FileSystemPath!;
                var subject = new PermissionSubject(child.Parent, path, path, snapshot.Subject.Authority,
                    facts.FileKind == FileType ? PermissionObjectKind.File : PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
                using var childInspection = InspectCore(subject, PermissionSections.Ordinary, ct, parent.TreeAncestry, facts);
                if (ModeReviewRefusal(childInspection.Snapshot) is not null) { inaccessible++; continue; }
                Visit(child, childInspection, ordinal, depth + 1);
            }
            parent.ConfirmTreeListing(listing);
        }
    }
}
