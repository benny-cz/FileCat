using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal sealed unsafe partial class DarwinPermissionService
{
    // Allow for a parent, a metadata descriptor, a parent watch, and a separate directory pin and namespace watch.
    internal const int ModeTreeTargetLimit = 24;
    private const int ModeTreeListingLimit = 4096;
    private const uint FirmlinkFlag = 0x00800000, RestrictedFlag = 0x00080000;
    private const uint TreeProtection = UserProtection | SystemProtection | RestrictedFlag;

    internal bool CanEditModeTree(PermissionSnapshot snapshot) => qualifyModeTrees && IsTreeFolder(snapshot);

    internal bool CanEditAclTree(PermissionSnapshot snapshot) => qualifyAclTrees && IsTreeFolder(snapshot) && CanEditAcl(snapshot);

    private static bool IsTreeFolder(PermissionSnapshot snapshot) => ModeRefusal(snapshot) is null &&
        snapshot.Binding?.Identity.Kind == PermissionObjectKind.Directory &&
        snapshot.Flags.Value is { } flags && (flags.Bits & (TreeProtection | FirmlinkFlag | MacMaterializationPolicy.DatalessFlag)) == 0;

    internal PermissionTreeReview PrepareModeTree(IEnumerable<(ItemRef Item, PermissionSubject Subject)> selection,
        ChangePermissionMode change, bool exactExecute, bool acceptSetIdClear, CancellationToken ct, int retainedInspections = 0)
    {
        if (change is not { Bits: <= PermissionBits, TouchedBits: > 0 and <= PermissionBits })
            throw new ArgumentException("Review at least one supported set or clear permission bit.", nameof(change));
        return PrepareTree(selection, change, exactExecute, acceptSetIdClear, reset: false, ct, retainedInspections);
    }

    internal PermissionTreeReview PrepareAclTree(ItemRef folder, PermissionSubject source, bool reset, CancellationToken ct,
        int retainedInspections = 0) => PrepareTree([(folder, source)], null, exactExecute: false, acceptSetIdClear: false, reset, ct, retainedInspections);

    private PermissionTreeReview PrepareTree(IEnumerable<(ItemRef Item, PermissionSubject Subject)> selection,
        ChangePermissionMode? change, bool exactExecute, bool acceptSetIdClear, bool reset, CancellationToken ct, int retainedInspections)
    {
        bool inheritance = change is null;
        var scope = reset ? PermissionScope.ResetTree : PermissionScope.ReviewedTree;
        if (!OperatingSystem.IsMacOS() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64 ||
            (inheritance ? !qualifyAclTrees : !qualifyModeTrees))
            throw new NotSupportedException("The Darwin tree writer is unavailable on this system.");
        if (ReviewedPermissionOperation.ProcessRefusal() is { } refusal) throw new UnauthorizedAccessException(refusal);
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
                subject.RawLocator != candidate.Item.FileSystemPath || !IsExactTreePath(subject.RawLocator))
                throw new NotSupportedException("Reviewed trees require exact local paths with no resolved links.");
            if (!roots.Any(root => root.Subject.RawLocator == subject.RawLocator ||
                root.Item.Kind == EntryKind.Directory && subject.RawLocator.StartsWith(root.Subject.RawLocator + "/", StringComparison.Ordinal)))
                roots.Add(candidate);
        }

        var entries = new List<PermissionTreeEntry>();
        var prepared = new Dictionary<string, (ReviewedPermissionTarget Target, PermissionTreeRow Row)>(StringComparer.Ordinal);
        var visitedDirectories = new HashSet<PermissionIdentity>();
        int inaccessible = 0, excluded = 0, protectedEntries = 0, observedNames = 0;
        PermissionIdentity? firstRoot = null;
        ReviewedPermissionTreeSource? template = null;
        PermissionTreeReview? completed = null;
        try
        {
            completed = MacMaterializationPolicy.Run(ct, Capture, (_, reason) => throw new NotSupportedException(reason));
            return completed;
        }
        catch (ReadFailure error) when (error.Status is PermissionReadStatus.Gone or PermissionReadStatus.Stale)
        { throw new PermissionBindingConflictException(error.Message); }
        catch (ReadFailure error) { throw new IOException(error.Message, error); }
        finally
        {
            // The manifest normalizes file aliases. Release their redundant pins, or all pins if capture failed.
            var retained = completed?.Operation.Targets.Select(target => target.Subject.RawLocator).ToHashSet(StringComparer.Ordinal);
            if (retained is not null && template is not null) retained.Add(template.Subject.RawLocator);
            foreach (var entry in prepared)
                if (retained is null || !retained.Contains(entry.Key)) entry.Value.Target.Mutation.Dispose();
        }

        PermissionTreeReview Capture()
        {
            foreach (var root in roots) Visit(root.Item, root.Subject, [], -1);
            if (entries.Count == 0) throw new NotSupportedException("There are no eligible objects in this tree review.");
            var manifest = PermissionTreeManifest.Capture(entries, inaccessible, ct, excluded, protectedEntries);
            var ordered = manifest.Entries.Where(entry => !inheritance || entry.ParentIndex >= 0).OrderByDescending(entry => entry.Depth)
                .Select(entry => prepared[entry.Subject.RawLocator]).ToArray();
            if (ordered.Length == 0) throw new NotSupportedException("There are no eligible captured descendants to review.");
            var operation = new ReviewedPermissionOperation(scope, ordered.Select(entry => entry.Target).ToImmutableArray(), manifest, template);
            return new(operation, ordered.Select(entry => entry.Row).ToImmutableArray())
            { DarwinTemplate = template is null ? null : prepared[template.Subject.RawLocator].Row.DarwinBefore };
        }

        void Visit(ItemRef item, PermissionSubject subject, ImmutableArray<TreeDirectory> ancestry, int parentIndex)
        {
            ct.ThrowIfCancellationRequested();
            if (entries.Count == limit || ancestry.Length >= PermissionSnapshots.MaxPathDepth)
                throw new NotSupportedException($"Review smaller trees with at most {limit} captured names per batch.");
            var snapshot = InspectWithPolicy(subject, WriteSecuritySections, ct, out var pin);
            using var inspection = new PermissionInspection(snapshot, pin);
            var binding = snapshot.Binding;
            if (binding is null || pin is null)
            {
                if (parentIndex < 0) throw new NotSupportedException(snapshot.BindingProblem ?? "The original tree root is unavailable.");
                inaccessible++;
                return;
            }
            if (binding.Identity.Kind == PermissionObjectKind.VolumeRoot ||
                (pin.Entry.Flags & (FirmlinkFlag | MacMaterializationPolicy.DatalessFlag)) != 0)
            {
                if (parentIndex < 0) throw new NotSupportedException("Select an ordinary file or folder to review a Darwin mode tree.");
                excluded++;
                return;
            }
            if ((pin.Entry.Flags & TreeProtection) != 0) { protectedEntries++; return; }
            if (ModeRefusal(snapshot) is { } problem)
            {
                if (parentIndex < 0) throw new NotSupportedException(problem);
                inaccessible++;
                return;
            }
            var originalAcl = snapshot.Access.Value?.Acl as DarwinAcl;
            bool isTemplate = inheritance && parentIndex < 0;
            if (isTemplate && !CanEditAclTree(snapshot)) throw new NotSupportedException("Select a bound ordinary folder with a readable source ACL.");
            if (inheritance && !isTemplate && ((originalAcl?.Flags ?? 0) & (DarwinPermissionAcl.NoInheritance | DarwinPermissionAcl.DeferredInheritance)) != 0)
            { protectedEntries++; return; }
            firstRoot ??= binding.Identity;
            if (binding.Identity.Authority != firstRoot.Authority || binding.Identity.MountInstance != firstRoot.MountInstance)
                throw new NotSupportedException("Review a Darwin mode tree on one mounted filesystem at a time.");
            if (VerifyTreePath(subject, binding, pin.Entry, pin.Mount, ancestry, ct))
                throw new PermissionBindingConflictException("The tree changed while capturing its entries. Capture a new review.");
            bool directory = binding.Identity.Kind == PermissionObjectKind.Directory;
            if (directory && !visitedDirectories.Add(binding.Identity))
            {
                if (parentIndex < 0) return; // The same directory was selected through an overlapping name.
                throw new PermissionBindingConflictException("A directory appeared more than once inside the reviewed tree.");
            }
            ushort before = snapshot.Mode.Value!.Value;
            (ReviewedPermissionTarget Target, PermissionSnapshot Expected) result;
            try
            {
                if (isTemplate)
                    result = PrepareTarget(inspection, item, new(PermissionScope.Objects, [new ChangePermissionMode(before, 0)]), false, ct);
                else if (change is { } mode)
                {
                    ushort after = (ushort)UnixPermissions.Apply((UnixFileMode)before, (UnixFileMode)(mode.Bits & mode.TouchedBits),
                        (UnixFileMode)mode.TouchedBits, directory, inside: parentIndex >= 0 && !exactExecute);
                    result = PrepareTarget(inspection, item, new(PermissionScope.Objects, [new ChangePermissionMode(after, mode.TouchedBits)]), acceptSetIdClear, ct);
                }
                else result = PrepareInheritedAclTarget(inspection, item, ancestry[^1].ProjectedAcl, originalAcl, directory, reset, ct);
            }
            catch (Exception error) when (parentIndex >= 0 && error is (NotSupportedException or UnauthorizedAccessException))
            { inaccessible++; return; }
            var component = result.Target.Components[0];
            TreeDirectory? folder = null;
            var resultingAcl = result.Expected.Access.Value?.Acl as DarwinAcl;
            try
            {
                if (directory)
                    folder = new(subject, binding, pin.Entry, component.Before, OpenTreeDirectory(pin.Handle, pin.Leaf), resultingAcl);
            }
            catch (Exception error) when (error is ReadFailure { Status: PermissionReadStatus.Denied } or IOException { HResult: MacMaterializationPolicy.Required })
            { inaccessible++; }
            catch { result.Target.Mutation.Dispose(); throw; }
            var target = result.Target with { Mutation = new TreeMutation(result.Target.Mutation, subject, binding, pin.Entry, pin.Mount, ancestry, folder) };
            var row = new PermissionTreeRow(subject, before, result.Expected.Mode.Value!.Value, binding.LinkCount, null)
            {
                DarwinBefore = inheritance ? originalAcl : null,
                DarwinAfter = inheritance ? resultingAcl : null
            };
            if (!prepared.TryAdd(subject.RawLocator, (target, row)))
            {
                target.Mutation.Dispose();
                throw new PermissionBindingConflictException("A captured name appeared more than once.");
            }
            int ordinal = entries.Count;
            entries.Add(new(parentIndex, ancestry.Length, subject, binding, component.Before, new(scope, isTemplate ? [] : [component.Change])));
            if (isTemplate) template = new(subject, binding, component.Before, target.Mutation);
            if (folder is not null) VisitChildren(pin, folder, ancestry, ordinal);
        }

        void VisitChildren(InspectionPin pin, TreeDirectory folder, ImmutableArray<TreeDirectory> ancestry, int ordinal)
        {
            var children = new List<(ItemRef Item, PermissionSubject Subject)>();
            try
            {
                var handle = folder.Handle;
                if (ReadEntry(handle, null) != pin.Entry || ReadMount(handle) != pin.Mount || folder.NamespaceChanged)
                    throw new PermissionBindingConflictException("The captured directory changed before listing.");
                UnixArchiveNative.EnumerateSelectionNames(handle, (name, inode) =>
                {
                    if (++observedNames > ModeTreeListingLimit)
                        throw new NotSupportedException("The tree exceeds the directory entry review limit. Review smaller batches.");
                    if (name is null || !IsExactTreePath(Path.Join(folder.Subject.RawLocator, name))) { inaccessible++; return; }
                    var stat = UnixFiles.StatChild(handle, name);
                    if (stat is null) { inaccessible++; return; }
                    if ((!stat.Value.IsRegular && !stat.Value.IsDirectory) || stat.Value.IsLink || stat.Value.Device != pin.Entry.Object.VolumeSerial)
                    { excluded++; return; }
                    Entry entry;
                    try { entry = ReadEntry(handle, name); }
                    catch (Exception error) when (error is ReadFailure or IOException { HResult: MacMaterializationPolicy.Required })
                    { inaccessible++; return; }
                    if (entry.FileSystem != pin.Mount.Id || (entry.Flags & (FirmlinkFlag | MacMaterializationPolicy.DatalessFlag)) != 0)
                    { excluded++; return; }
                    if (entry.Inode != inode || entry.Object != stat.Value.Identity)
                        throw new PermissionBindingConflictException("A directory member changed during capture.");
                    var child = new ItemRef(Location.FileSystem(folder.Subject.RawLocator), name, entry.Kind == PermissionObjectKind.Directory
                        ? EntryKind.Directory : EntryKind.File);
                    string path = child.FileSystemPath!;
                    children.Add((child, new(child.Parent, path, path, folder.Subject.Authority, entry.Kind, PermissionLinkPolicy.NoFollow,
                        ExpectedIdentity: new(entry.Object, folder.Binding.Identity.MountInstance, entry.Kind, folder.Subject.Authority))));
                }, ct);
                if (ReadEntry(handle, null) != pin.Entry || ReadMount(handle) != pin.Mount || folder.NamespaceChanged)
                    throw new PermissionBindingConflictException("The captured directory changed while listing.");
            }
            catch (Exception error) when (error is ReadFailure or ArchiveWriteException { Reason: ArchiveWriteReason.SourceChanged } or
                IOException { HResult: 1 or 13 or MacMaterializationPolicy.Required })
            { inaccessible++; return; }
            var chain = ancestry.Add(folder);
            foreach (var child in children.OrderBy(child => child.Item.Name, StringComparer.Ordinal))
                Visit(child.Item, child.Subject, chain, ordinal);
            if (VerifyTreePath(folder.Subject, folder.Binding, pin.Entry, pin.Mount, ancestry, ct) || folder.NamespaceChanged)
                throw new PermissionBindingConflictException("The captured directory changed during review.");
        }
    }

    private static (ReviewedPermissionTarget Target, PermissionSnapshot Expected) PrepareInheritedAclTarget(PermissionInspection inspection,
        ItemRef item, DarwinAcl? parent, DarwinAcl? child, bool directory, bool reset, CancellationToken ct)
    {
        var snapshot = inspection.Snapshot;
        if (!CanEditAcl(snapshot)) throw new NotSupportedException("The captured child does not have a qualified ACL binding.");
        byte[] payload = DarwinPermissionAcl.DeriveChild(parent, child, directory, reset);
        var projected = ProjectAclPayload(snapshot, payload);
        var before = RequireRevision(snapshot, WriteSecuritySections);
        var after = RequireRevision(projected, WriteSecuritySections);
        ApplyDarwinAcl operation;
        if (projected.Access.Status == PermissionReadStatus.Absent) operation = new(ImmutableArray.Create(payload), Remove: true);
        else
        {
            nint clone = CloneAcl(payload);
            try { operation = VerifyNativeAcl(clone, payload); }
            finally { ReleaseAcl(clone); }
        }
        string call = snapshot.Binding!.DarwinRoute == DarwinBindingRoute.Descriptor
            ? "acl_set_fd_np (retained descriptor)" : "setattrlistat extended security (checked parent and name)";
        return BindTarget(inspection, item, before, projected, [new(operation, before, after, call)], ct);
    }

    private static bool IsExactTreePath(string path) => !PathUtil.HasAmbiguousUnixName(path) && Path.IsPathFullyQualified(path) &&
        Path.GetFullPath(path) == path && path.Count(character => character == '/') <= PermissionSnapshots.MaxPathDepth;

    private sealed class TreeDirectory : IDisposable
    {
        private readonly DirectoryWatch _watch;
        private bool _namespaceChanged;
        internal PermissionSubject Subject { get; }
        internal PermissionBinding Binding { get; }
        internal Entry Entry { get; }
        internal PermissionRevision Baseline { get; }
        internal SafeFileHandle Handle { get; }
        internal DarwinAcl? ProjectedAcl { get; }

        internal TreeDirectory(PermissionSubject subject, PermissionBinding binding, Entry entry, PermissionRevision baseline, SafeFileHandle handle,
            DarwinAcl? projectedAcl)
        {
            Subject = subject;
            Binding = binding;
            Entry = entry;
            Baseline = baseline;
            Handle = handle;
            ProjectedAcl = projectedAcl;
            try { _watch = new(handle, namespaceOnly: true); }
            catch { handle.Dispose(); throw; }
        }

        // Namespace events also report changes that share one HFS+ timestamp tick.
        internal bool NamespaceChanged => _namespaceChanged |= _watch.TakeChanges();

        public void Dispose() { _watch.Dispose(); Handle.Dispose(); }
    }

    private static SafeFileHandle OpenTreeDirectory(SafeFileHandle parent, string leaf)
    {
        int fd = OpenAt(parent, leaf, SearchNoFollow);
        if (fd < 0) throw NativeFailure("Open the captured directory for search", Marshal.GetLastPInvokeError());
        return new(fd, ownsHandle: true);
    }

    private static bool VerifyTreePath(PermissionSubject subject, PermissionBinding binding, Entry baseline, Mount mount,
        ImmutableArray<TreeDirectory> ancestry, CancellationToken ct)
    {
        bool changed = false;
        using var first = OpenDirectory(ancestry.IsEmpty ? Path.GetDirectoryName(subject.RawLocator)! : ancestry[0].Subject.RawLocator);
        SafeFileHandle current = first;
        try
        {
            for (int index = 0; index < ancestry.Length; index++)
            {
                ct.ThrowIfCancellationRequested();
                var directory = ancestry[index];
                if (index != 0)
                {
                    using var previous = current;
                    current = OpenTreeDirectory(previous, Path.GetFileName(directory.Subject.RawLocator));
                }
                var stamp = ReadEntry(current, null);
                if (!SameTreeEntry(stamp, directory.Entry) || ReadMount(current) != mount ||
                    RequireRevision(ReadSnapshot(directory.Subject, directory.Binding, WriteSecuritySections, current, null, stamp, mount),
                        WriteSecuritySections) != directory.Baseline || stamp != ReadEntry(current, null))
                    throw new PermissionBindingConflictException("A captured ancestor's identity or permissions changed.");
                changed |= stamp != directory.Entry | directory.NamespaceChanged;
            }
            ct.ThrowIfCancellationRequested();
            string leaf = Path.GetFileName(subject.RawLocator);
            Entry target;
            try { target = ReadEntry(current, leaf); }
            catch (ReadFailure error) when (error.Status == PermissionReadStatus.Gone)
            { throw new PermissionBindingConflictException("The captured entry no longer exists at its original name."); }
            if (!SameTreeEntry(target, baseline) || target.Object != binding.Identity.Object || ReadMount(current) != mount ||
                (target.Flags & (TreeProtection | FirmlinkFlag | MacMaterializationPolicy.DatalessFlag)) != 0)
                throw new PermissionBindingConflictException("The captured member or its mounted filesystem changed.");
            return changed || target.Kind == PermissionObjectKind.Directory && (target.Modified != baseline.Modified || target.Links != baseline.Links);
        }
        finally { if (!ReferenceEquals(current, first)) current.Dispose(); }
    }

    private static bool SameTreeEntry(Entry current, Entry original) => current.Object == original.Object && current.Kind == original.Kind &&
        current.FileSystem == original.FileSystem && current.Created == original.Created;

    private sealed class TreeMutation(IBoundPermissionMutation original, PermissionSubject subject, PermissionBinding binding,
        Entry baseline, Mount mount, ImmutableArray<TreeDirectory> ancestry, TreeDirectory? folder) : IBoundPermissionMutation
    {
        private bool _folderChanged;

        public PermissionSnapshot CheckCurrent(PermissionSections sections, CancellationToken cancellation)
        {
            if (TreeConflict(cancellation) is { } why) throw new PermissionBindingConflictException(why);
            return original.CheckCurrent(sections, cancellation);
        }

        public PermissionMutationReturn Apply(PermissionDelta change) => TreeConflict(CancellationToken.None) is { } why
            ? new(BindingConflict: true, Detail: why) : original.Apply(change);

        public PermissionReadBack Observe(PermissionSections sections)
        {
            var result = original.Observe(sections);
            if (result.Verification != PermissionVerification.SameObject) return result;
            bool matchesTree = TreeConflict(CancellationToken.None) is null;
            if (!matchesTree && binding.DarwinRoute == DarwinBindingRoute.ParentRelative)
                return result with { Verification = PermissionVerification.Unavailable, Detail = "The captured ancestry could not be verified after the call." };
            return !matchesTree || _folderChanged ? result with { FolderChanged = true,
                Detail = "The reviewed object was verified. Its captured directory or ancestor changed. Later entries were outside the review." } : result;
        }

        private string? TreeConflict(CancellationToken cancellation)
        {
            try
            {
                string detail = MacMaterializationPolicy.Run(cancellation, () =>
                {
                    _folderChanged |= VerifyTreePath(subject, binding, baseline, mount, ancestry, cancellation) | (folder?.NamespaceChanged ?? false);
                    return string.Empty;
                }, static (_, reason) => reason);
                return detail.Length == 0 ? null : detail;
            }
            catch (Exception error) when (error is ReadFailure or IOException or NotSupportedException or UnauthorizedAccessException)
            { return error.Message; }
        }

        public void Dispose() { try { original.Dispose(); } finally { folder?.Dispose(); } }
    }
}
