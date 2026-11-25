using System.Collections.Immutable;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Core.Permissions;

internal interface IBoundPermissionMutation : IDisposable
{
    // Namespace admission and dependency reads must use this operation's retained native authority.
    PermissionSnapshot CheckCurrent(PermissionSections sections, CancellationToken cancellation);
    PermissionMutationReturn Apply(PermissionDelta change);
    PermissionMutationReturn Apply(PermissionDelta change, CancellationToken cancellation) => Apply(change);
    PermissionReadBack Observe(PermissionSections sections);
}

internal sealed record PermissionMutationReturn(int? NativeError = null, bool RejectedBeforeChange = false, bool BindingConflict = false,
    string? Detail = null, bool CanceledBeforeChange = false, bool FailedWithoutDispatch = false);
internal sealed class PermissionBindingConflictException(string message) : IOException(message);
internal sealed record PermissionReadBack(PermissionSnapshot Snapshot, PermissionVerification Verification, bool FolderChanged = false,
    string? Detail = null, bool Reconnected = false, PermissionRevision? AttestedRevision = null);
internal sealed record ReviewedPermissionComponent(PermissionDelta Change, PermissionRevision Before, PermissionRevision Expected,
    string NativeCall);
internal sealed record ReviewedPermissionTarget(ItemRef Source, PermissionSubject Subject, PermissionBinding? Binding,
    ImmutableArray<ReviewedPermissionComponent> Components, PermissionRevision Final, IBoundPermissionMutation Mutation,
    PermissionServerPath? ServerPath = null);
internal sealed record ReviewedPermissionTreeSource(PermissionSubject Subject, PermissionBinding Binding, PermissionRevision Revision,
    IBoundPermissionMutation Mutation);

/// <summary>The adapter supplies reviewed before/after facts and transfers each mutation binding exactly once.</summary>
internal sealed class ReviewedPermissionOperation : IDisposable
{
    internal const int TargetLimit = 128, ComponentLimit = 16, LocatorLimit = 32767;
    private const PermissionSections WindowsDiscretionary = PermissionSections.Owner | PermissionSections.Group |
        PermissionSections.Access | PermissionSections.Inheritance;
    private int _state; // review, submitted, released
    internal PermissionScope Scope { get; }
    internal ImmutableArray<ReviewedPermissionTarget> Targets { get; }
    internal PermissionTreeManifest? Tree { get; }
    internal ReviewedPermissionTreeSource? TreeSource { get; }
    internal bool UsesServerPath => Targets[0].ServerPath is not null;

    internal ReviewedPermissionOperation(PermissionScope scope, ImmutableArray<ReviewedPermissionTarget> targets, PermissionTreeManifest? tree = null,
        ReviewedPermissionTreeSource? treeSource = null)
    {
        if ((scope is PermissionScope.ReviewedTree or PermissionScope.ResetTree) != (tree is not null) ||
            scope == PermissionScope.ResetTree && treeSource is null || treeSource is not null && tree is null || scope is not (PermissionScope.Objects or
                PermissionScope.DefaultsForNewChildren or PermissionScope.NativePropagation or PermissionScope.ReviewedTree or PermissionScope.ResetTree))
            throw new NotSupportedException("This permissions executor does not support the requested scope.");
        if (targets.IsDefaultOrEmpty || targets.Length > TargetLimit ||
            targets.Select(t => (t.Binding?.Identity.Authority, t.Binding?.Identity.Object, t.ServerPath?.Device, t.ServerPath?.Path))
                .Distinct().Count() != targets.Length ||
            targets.Select(t => t.Mutation).Distinct(ReferenceEqualityComparer.Instance).Count() != targets.Length)
            throw new ArgumentException("A finite set of distinct objects and independent retained bindings is required.", nameof(targets));
        bool usesServerPath = targets[0].ServerPath is not null;
        if (targets.Any(target => (target.ServerPath is not null) != usesServerPath) || usesServerPath && scope != PermissionScope.Objects)
            throw new ArgumentException("A server-path review needs one operation scope for its selected server paths.", nameof(targets));
        if (scope == PermissionScope.NativePropagation)
            ValidatePropagation(targets);
        if (tree is not null)
        {
            bool windowsAccessTree = tree.Entries[0].VisitOnly;
            if (windowsAccessTree) ValidateWindowsAccessTree(tree, scope, treeSource);
            else if (tree.Entries.Any(entry => entry.VisitOnly))
                throw new ArgumentException("This tree scope requires a reviewed change for each captured descendant.", nameof(tree));
            if (treeSource is not null) ValidateTreeSource(tree, treeSource, targets, scope);
            var entries = tree.Entries.Where(entry => !entry.VisitOnly && (treeSource is null || entry.ParentIndex >= 0))
                .OrderByDescending(entry => entry.Depth).ToArray();
            if (entries.Length != targets.Length || entries.Where((entry, index) => !MatchesTreeEntry(entry, targets[index], scope, treeSource, windowsAccessTree)).Any())
                throw new ArgumentException("A tree operation must use its captured objects and changes in children-first order.", nameof(targets));
        }
        long footprint = 0;
        foreach (var target in targets)
        {
            if (target.Subject.RawLocator is not { Length: > 0 and <= LocatorLimit } locator || locator.Contains('\0') ||
                target.Components.IsDefaultOrEmpty || target.Components.Length > ComponentLimit || !Valid(target.Final))
                throw new ArgumentException("The operation lacks usable reviewed binding or final verification evidence.", nameof(targets));
            if (target.ServerPath is { } server)
            {
                const PermissionSections serverSections = PermissionSections.Owner | PermissionSections.Group | PermissionSections.Mode;
                if (target.Binding is not null || target.Subject.Authority != server.Authority ||
                    server.Authority.Kind != PermissionAuthorityKind.RemoteSession || server.Authority.Id != target.Source.Parent.Session ||
                    target.Source.Parent != target.Subject.OriginalLocation || target.Source.Parent.Scheme != Schemes.Sftp ||
                    target.Subject.LinkPolicy != PermissionLinkPolicy.NoFollow ||
                    target.Subject.Kind is not (PermissionObjectKind.File or PermissionObjectKind.Directory) || server.Path != locator ||
                    target.Source.Kind != (target.Subject.Kind == PermissionObjectKind.File ? EntryKind.File : EntryKind.Directory) ||
                    target.Source.Flags.HasFlag(EntryFlags.Link) ||
                    locator[0] != '/' || locator[(locator.LastIndexOf('/') + 1)..] != target.Source.Name ||
                    server.Digest is not { Length: 64 } || !server.Digest.All(char.IsAsciiHexDigitUpper) ||
                    string.IsNullOrEmpty(server.Device) || target.Final.Sections != serverSections ||
                    target.Components.Any(component => component.Before.Sections != serverSections || component.Expected.Sections != serverSections ||
                        component.Change is not (ChangePermissionMode or AssignPermissionPrincipal { IsOwner: false })))
                    throw new ArgumentException("The operation lacks an exact reviewed SFTP source and complete server-path evidence.", nameof(targets));
            }
            else if (target.Binding is not { ReadOnly: false } binding || target.Subject.Authority != binding.Identity.Authority ||
                target.Subject.Authority.Kind != PermissionAuthorityKind.Local || target.Source.FileSystemPath != locator)
                throw new ArgumentException("This executor requires a captured local source and matching locator.", nameof(targets));
            if (scope == PermissionScope.DefaultsForNewChildren &&
                (target.Binding?.Identity.Kind != PermissionObjectKind.Directory ||
                 target.Components.Count(component => component.Change is ChangePosixAcl { IsDefault: true }) != 1 ||
                 target.Components.Any(component => component.Change is not (ChangePosixAcl { IsDefault: true } or ChangePermissionFlags))))
                throw new ArgumentException("The directory default scope requires a default ACL change on each selected directory.", nameof(targets));
            footprint += sizeof(char) * (long)locator.Length;
            foreach (var component in target.Components)
            {
                if (component.Change is ResetWindowsDacl &&
                    (scope != PermissionScope.ResetTree || treeSource is null || !IsWindowsTreeSource(treeSource)))
                    throw new ArgumentException("A Windows reset requires its captured tree and retained ACL source.", nameof(targets));
                if (!Valid(component.Before) || !Valid(component.Expected) || component.NativeCall is not { Length: > 0 and <= 120 } ||
                    (component.Before.Sections & component.Change.Section) == 0 || (component.Expected.Sections & component.Change.Section) == 0 ||
                    (target.Final.Sections & component.Expected.Sections) != component.Expected.Sections)
                    throw new ArgumentException("Every component and preservation dependency must have before, expected and final evidence.", nameof(targets));
                footprint += ChangeBytes(component.Change);
            }
            if (footprint > 64L * 1024 * 1024) throw new ArgumentException("Split the permissions review into smaller batches.", nameof(targets));
        }
        Scope = scope;
        Targets = targets;
        Tree = tree;
        TreeSource = treeSource;
    }

    private static void ValidateWindowsAccessTree(PermissionTreeManifest tree, PermissionScope scope, ReviewedPermissionTreeSource? source)
    {
        if (scope != PermissionScope.ReviewedTree || source is not null || tree.RootCount != 1 || tree.Entries.Any(entry =>
                entry.Binding is not { FileSystem: "NTFS", ReadOnly: false } || entry.Subject.LinkPolicy != PermissionLinkPolicy.NoFollow ||
                entry.Draft.Scope != scope || !entry.VisitOnly &&
                (entry.Binding.Identity.Kind != PermissionObjectKind.Directory &&
                    entry.Binding is not { Identity.Kind: PermissionObjectKind.File, LinkCount: 1 } ||
                    entry.Draft.Changes is not [AddWindowsAce { Entry.Flags: 0 }])))
            throw new ArgumentException("A Windows access tree needs one captured directory root and independently bound NTFS targets.", nameof(tree));
    }

    private static bool MatchesTreeEntry(PermissionTreeEntry entry, ReviewedPermissionTarget target, PermissionScope scope,
        ReviewedPermissionTreeSource? source, bool windowsAccessTree) =>
        entry.Subject == target.Subject && entry.Binding == target.Binding && entry.Subject.LinkPolicy == PermissionLinkPolicy.NoFollow &&
        entry.Draft.Scope == scope && target.Components is [var component] &&
        (windowsAccessTree ? component.Change is AddWindowsAce { Entry.Flags: 0 } &&
            component.Before.Sections == WindowsDiscretionary && component.Expected.Sections == WindowsDiscretionary
        : source is not null && IsWindowsTreeSource(source) ? component.Change is ResetWindowsDacl &&
            entry.Binding.FileSystem == "NTFS" && (entry.Binding.Identity.Kind == PermissionObjectKind.Directory ||
                entry.Binding is { Identity.Kind: PermissionObjectKind.File, LinkCount: 1 }) &&
            component.Before.Sections == WindowsDiscretionary && component.Expected.Sections == WindowsDiscretionary
        : source is null ? component.Change is ChangePermissionMode : source.Binding.DarwinRoute != DarwinBindingRoute.None
            ? entry.Binding.DarwinRoute != DarwinBindingRoute.None && component.Change is ApplyDarwinAcl &&
                component.Before.Sections == DarwinPermissionService.AccessChangeSections && component.Expected.Sections == component.Before.Sections
            : component.Change is ChangePosixAcl { IsDefault: false, Entries.IsDefaultOrEmpty: false }) && component.Before == entry.Before &&
        component.Expected == target.Final && entry.Draft.Changes.SequenceEqual([component.Change]);

    private static bool IsWindowsTreeSource(ReviewedPermissionTreeSource source) =>
        source.Binding.FileSystem == "NTFS" && source.Revision.Sections == WindowsDiscretionary;

    private static void ValidateTreeSource(PermissionTreeManifest tree, ReviewedPermissionTreeSource source,
        ImmutableArray<ReviewedPermissionTarget> targets, PermissionScope scope)
    {
        var root = tree.Entries[0];
        bool darwin = source.Binding.DarwinRoute != DarwinBindingRoute.None;
        bool windows = IsWindowsTreeSource(source);
        if (tree.RootCount != 1 || root.ParentIndex != -1 || root.Draft.Scope != scope || !root.Draft.Changes.IsEmpty ||
            root.Subject != source.Subject || root.Binding != source.Binding || root.Before != source.Revision ||
            source.Subject.LinkPolicy != PermissionLinkPolicy.NoFollow || source.Binding.Identity.Kind != PermissionObjectKind.Directory ||
            (!darwin && scope != PermissionScope.ResetTree) || source.Binding.ReadOnly != false ||
            source.Revision.Sections != (windows ? WindowsDiscretionary : darwin ? DarwinPermissionService.AccessChangeSections : PermissionSections.Defaults) ||
            !Valid(source.Revision) ||
            targets.Any(target => ReferenceEquals(target.Mutation, source.Mutation)))
            throw new ArgumentException("An inherited ACL review requires the retained source and distinct descendant bindings.", nameof(source));
    }

    private static void ValidatePropagation(ImmutableArray<ReviewedPermissionTarget> targets)
    {
        if (targets is not [var root] || root.Binding?.Identity.Kind is not (PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot) ||
            root.Subject.LinkPolicy != PermissionLinkPolicy.NoFollow || !PermissionSnapshots.Matches(root.Subject, root.Binding.Identity) ||
            root.Components is not [{ Change: AddWindowsAce or ChangeWindowsAce or ChangeWindowsInheritance } component] ||
            component.Before.Sections != WindowsDiscretionary || component.Expected.Sections != WindowsDiscretionary || component.Expected != root.Final)
            throw new ArgumentException("Native propagation requires one reviewed Windows folder DACL change with root verification.", nameof(targets));
        if (component.Before == component.Expected)
            throw new NotSupportedException("Repairing inheritance when the root already matches requires a qualified repair operation.");
    }

    internal static long ChangeBytes(PermissionDelta change) => change switch
    {
        ChangePosixAcl acl when !acl.Entries.IsDefault && acl.Entries.Length <= PermissionSnapshots.MaxAclEntries => 256 + acl.Entries.Length * 256L,
        ApplyDarwinAcl acl => 256L + acl.FileSecurity.Length,
        ChangePosixAcl => throw new ArgumentException("The ACL exceeds the reviewed row budget.", nameof(change)),
        ChangeWindowsAce ace => 256L + ace.Expected.Bytes.Length + (ace.Replacement?.Bytes.Length ?? 0),
        AddWindowsAce ace => 256L + ace.Entry.Bytes.Length,
        ResetWindowsDacl reset when !reset.InheritedAcl.IsDefault && reset.InheritedAcl.Length is >= 8 and <= ushort.MaxValue =>
            256L + reset.InheritedAcl.Length,
        ResetWindowsDacl => throw new ArgumentException("The inherited Windows ACL exceeds the reviewed byte budget.", nameof(change)),
        ChangeWindowsAuditAce ace => 256L + (ace.Expected?.Bytes.Length ?? 0) + (ace.Replacement?.Bytes.Length ?? 0),
        _ => 256
    };

    private static bool Valid(PermissionRevision revision) => revision.Sections != PermissionSections.None &&
        revision.Digest is { Length: 64 } && revision.Digest.All(char.IsAsciiHexDigitUpper);

    internal static string? ProcessRefusal() => ProcessAuthority.IsElevatedOrRoot
        ? "Open an ordinary FileCat window to apply reviewed permissions." : null;

    internal void TransferToJob()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("This permissions review has already been released or submitted.");
    }

    internal void ReleaseFromJob() { if (Interlocked.CompareExchange(ref _state, 2, 1) == 1) CloseBindings(); }
    public void Dispose() { if (Interlocked.CompareExchange(ref _state, 2, 0) == 0) CloseBindings(); }

    private void CloseBindings()
    {
        List<Exception>? failures = null;
        foreach (var target in Targets)
        {
            try { target.Mutation.Dispose(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (TreeSource is { } source)
        {
            try { source.Mutation.Dispose(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is not null) throw new AggregateException("Could not confirm release of all permission bindings.", failures);
    }
}
