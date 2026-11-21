using System.Collections.Immutable;

namespace FileCat.Core.Permissions;

internal sealed record PermissionTreeEntry(int ParentIndex, int Depth, PermissionSubject Subject, PermissionBinding Binding,
    PermissionRevision Before, PermissionDraft Draft);

internal readonly record struct PermissionTreeSummary(int Roots, int InaccessibleBranches, int ExcludedEntries, int Aliases, int ProtectedEntries = 0);

internal sealed record PermissionTreeRow(PermissionSubject Subject, ushort Before, ushort After, uint? LinkCount, PosixAcl? Access)
{
    internal ImmutableArray<PosixAclEntry> ResetBefore { get; init; }
    internal ImmutableArray<PosixAclEntry> ResetAfter { get; init; }
    internal DarwinAcl? DarwinBefore { get; init; }
    internal DarwinAcl? DarwinAfter { get; init; }
    internal bool Changed => Before != After || !ResetBefore.IsDefault && !ResetBefore.SequenceEqual(ResetAfter) ||
        (DarwinBefore is null) != (DarwinAfter is null) || DarwinBefore is not null && DarwinAfter is not null &&
        !DarwinBefore.FileSecurity.AsSpan().SequenceEqual(DarwinAfter.FileSecurity.AsSpan());
}

internal sealed record PermissionTreeReview(ReviewedPermissionOperation Operation, ImmutableArray<PermissionTreeRow> Rows,
    ImmutableArray<PosixAclEntry> Template = default)
{
    internal DarwinAcl? DarwinTemplate { get; init; }
}

/// <summary>Finite discovery evidence for reviewing and applying an explicit tree change.</summary>
internal sealed class PermissionTreeManifest
{
    internal const int EntryLimit = 100_000;
    internal const long ByteLimit = 64 * 1024 * 1024;

    internal ImmutableArray<PermissionTreeEntry> Entries { get; }
    internal int InaccessibleBranches { get; }
    internal int ExcludedEntries { get; }
    internal int AliasCount { get; }
    internal int RootCount { get; }
    internal int ProtectedEntries { get; }
    internal PermissionTreeSummary Summary => new(RootCount, InaccessibleBranches, ExcludedEntries, AliasCount, ProtectedEntries);

    private PermissionTreeManifest(ImmutableArray<PermissionTreeEntry> entries, int inaccessible, int aliases, int excluded, int protectedEntries)
    {
        Entries = entries;
        InaccessibleBranches = inaccessible;
        ExcludedEntries = excluded;
        AliasCount = aliases;
        ProtectedEntries = protectedEntries;
        RootCount = entries.Count(entry => entry.ParentIndex == -1);
    }

    internal static PermissionTreeManifest Capture(IEnumerable<PermissionTreeEntry> discovery, int inaccessibleBranches,
        CancellationToken cancellation, int excludedEntries = 0, int protectedEntries = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inaccessibleBranches);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(inaccessibleBranches, EntryLimit);
        ArgumentOutOfRangeException.ThrowIfNegative(excludedEntries);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(excludedEntries, EntryLimit);
        ArgumentOutOfRangeException.ThrowIfNegative(protectedEntries);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(protectedEntries, EntryLimit);
        var entries = ImmutableArray.CreateBuilder<PermissionTreeEntry>();
        var ordinals = new List<int>();
        var identities = new Dictionary<(PermissionAuthority Authority, FileSystem.FileIdentity Object), int>();
        int aliases = 0;
        long bytes = 0;
        foreach (var entry in discovery)
        {
            cancellation.ThrowIfCancellationRequested();
            if (ordinals.Count >= EntryLimit)
                throw new NotSupportedException("The tree exceeds the review entry limit. Review the scope in smaller batches.");
            if (entry.ParentIndex < -1 || entry.ParentIndex >= ordinals.Count || entry.Depth < 0 || entry.Depth >= PermissionSnapshots.MaxPathDepth ||
                entry.Subject.RawLocator is not { Length: > 0 and <= ReviewedPermissionOperation.LocatorLimit } || !Path.IsPathFullyQualified(entry.Subject.RawLocator) ||
                entry.Binding.Identity.Kind is not (PermissionObjectKind.File or PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot) ||
                !PermissionSnapshots.Matches(entry.Subject, entry.Binding.Identity) || entry.Before.Sections == PermissionSections.None ||
                entry.Draft.Scope is not (PermissionScope.ReviewedTree or PermissionScope.ResetTree) || entry.Draft.Changes.IsDefault)
                throw new ArgumentException("The tree contains an entry without bounded ancestry, identity or reviewed section evidence.", nameof(discovery));

            int parentIndex = entry.ParentIndex < 0 ? -1 : ordinals[entry.ParentIndex];
            if (entry.ParentIndex >= 0 && parentIndex < 0)
                throw new ArgumentException("A file alias cannot be the parent of another entry.", nameof(discovery));
            if (parentIndex < 0 ? entry.Depth != 0 : !BelongsTo(entries[parentIndex], entry))
                throw new ArgumentException("The tree crosses a mount, authority or invalid parent boundary.", nameof(discovery));

            bytes += 512 + 2L * (entry.Subject.RawLocator.Length + entry.Subject.DisplayPath.Length + entry.Binding.MountRoot.Length +
                entry.Binding.Identity.MountInstance.Length + entry.Binding.Identity.Authority.Id.Length);
            foreach (var change in entry.Draft.Changes) bytes += ReviewedPermissionOperation.ChangeBytes(change);
            if (bytes > ByteLimit) throw new NotSupportedException("The tree exceeds the review byte limit. Review the scope in smaller batches.");

            var key = (entry.Binding.Identity.Authority, entry.Binding.Identity.Object);
            if (!identities.TryAdd(key, entries.Count))
            {
                var first = entries[identities[key]];
                bool sameChanges = SameChanges(entry.Draft.Changes, first.Draft.Changes);
                if (!sameChanges && entry.Draft.Changes is [ApplyDarwinAcl] && first.Draft.Changes is [ApplyDarwinAcl])
                    throw new ArgumentException("The captured hard-link names require different inherited ACLs.", nameof(discovery));
                if (entry.Binding.Identity.Kind != PermissionObjectKind.File || entry.Binding != first.Binding ||
                    entry.Before != first.Before || entry.Draft.Scope != first.Draft.Scope || !sameChanges)
                    throw new ArgumentException("Normalize overlapping or conflicting roots before reviewing the tree.", nameof(discovery));
                ordinals.Add(-1);
                aliases++;
                continue;
            }
            ordinals.Add(entries.Count);
            entries.Add(entry with { ParentIndex = parentIndex });
        }

        if (entries.Count == 0) throw new ArgumentException("The tree has no reviewed objects.", nameof(discovery));
        return new(entries.ToImmutable(), inaccessibleBranches, aliases, excludedEntries, protectedEntries);
    }

    private static bool SameChanges(ImmutableArray<PermissionDelta> first, ImmutableArray<PermissionDelta> second) =>
        first.Length == second.Length && first.Zip(second).All(pair => pair.First is ApplyDarwinAcl left && pair.Second is ApplyDarwinAcl right
            ? left.Remove == right.Remove && left.FileSecurity.AsSpan().SequenceEqual(right.FileSecurity.AsSpan())
            : pair.First == pair.Second);

    private static bool BelongsTo(PermissionTreeEntry parent, PermissionTreeEntry child)
    {
        var p = parent.Binding.Identity;
        var c = child.Binding.Identity;
        return p.Kind is PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot && child.Depth == parent.Depth + 1 &&
            c.Authority == p.Authority && c.MountInstance == p.MountInstance && c.Object.VolumeSerial == p.Object.VolumeSerial &&
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(child.Subject.RawLocator)) == Path.TrimEndingDirectorySeparator(parent.Subject.RawLocator);
    }
}
