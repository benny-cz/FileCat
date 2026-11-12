using FileCat.Core.Archives;
using FileCat.Core.Resources;

namespace FileCat.Archives;

public sealed partial class ArchiveProvider
{
    /// <summary>
    /// The SHA-256 of the archive a listing showed, for the native review of a member command: read now, through that
    /// listing's own witness handle, outside the provider's lock and cancellable (see <see cref="ArchiveListingRevision"/>).
    /// </summary>
    internal string ResolveListingRevision(string token, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        ArchiveIndex? index; IDisposable? lease;
        lock (_cacheLock)
        {
            index = _cache.Values.FirstOrDefault(value => value.Revision?.Token == token);
            lease = index?.TryLease();
        }
        if (index?.Revision is not { } revision || lease is null) throw new IOException(ArchiveListingRevision.Closed);
        using (lease) return revision.Resolve(ct);
    }

    // An edit reads the archive's revision (once, if no command read it yet) before taking the provider's lock; a lease
    // keeps the index open meanwhile. Under the lock only the known revision is compared.
    private void ResolveEditRevision(Location root, CancellationToken ct)
    {
        ArchiveIndex index; IDisposable lease;
        lock (_cacheLock)
        {
            index = GetIndex(root, ct);
            lease = index.TryLease() ?? throw new IOException("The archive was closed; open it again.");
        }
        using (lease) index.RequireRevision().Resolve(ct);
    }

    internal string ReviewedMemberPath(ArchiveTargetReview target, ItemRef item, ArchiveJobPassword? password, CancellationToken ct)
    {
        var root = item.Parent.WithPath("");
        if (root.Container is not { IsFileSystem: true } container || Path.GetFullPath(container.Path) != target.Path)
            throw new IOException("An edit needs a member of the reviewed outer archive.");
        using var view = OpenReadView(root, password, ct, requireRevision: true);
        ResolveEditRevision(root, ct);
        lock (_cacheLock)
        {
            var index = GetIndex(root, ct); RequireEditRevision(index, target);
            var node = index.FindFile(item.Parent.Path, item.Name, item.Ordinal);
            if (node?.Tag is not { Kind: MemberKind.File, UnsafeReason: null } tag)
                throw new IOException("This archive entry is not a regular member that can be edited directly.");
            // AW7: the displayed name of a standalone stream's one member is derived from the outer file name. The native
            // assessment and every plan use the synthetic member, so a rename of the outer file never changes an edit.
            if (ArchiveStandalone.IsStandalone(target.Profile.Format))
                return ReferenceEquals(StandaloneSingleFile(index), tag) ? ArchiveStandalone.MemberName
                    : throw new IOException("A compressed file's only member is its whole contents.");
            return tag.FullName;
        }
    }

    internal IContentSource OpenReviewedMember(ArchiveTargetReview target, string rawPath, ArchiveJobPassword? password, CancellationToken ct)
    {
        Location root;
        lock (_cacheLock)
            root = _cache.Values.FirstOrDefault(index => index.ContainerPath == target.Path)?.ContainerLocation ??
                ForFile(target.Path, ReaderKind(target.Profile.Format));
        using var view = OpenReadView(root, password, ct, requireRevision: true);
        ResolveEditRevision(root, ct);
        lock (_cacheLock)
        {
            var index = GetIndex(root, ct); RequireEditRevision(index, target);
            if (ArchiveStandalone.IsStandalone(target.Profile.Format))
            {
                if (rawPath != ArchiveStandalone.MemberName || StandaloneSingleFile(index) is not { } single)
                    throw new IOException("A compressed file's only member is its whole contents.");
                ct.ThrowIfCancellationRequested(); return index.Extract(single, _tempDirectory);
            }
            var matches = index.Children.Values.SelectMany(nodes => nodes).Where(node => node.Tag?.FullName == rawPath).Take(2).ToArray();
            if (matches.Length != 1 || matches[0].Tag is not { Kind: MemberKind.File, UnsafeReason: null } tag)
                throw new IOException("Direct editing needs one exact regular member. Extract the selected occurrence with F5 instead.");
            ct.ThrowIfCancellationRequested(); return index.Extract(tag, _tempDirectory);
        }
    }

    private static ArchiveMemberTag? StandaloneSingleFile(ArchiveIndex index)
    {
        var nodes = index.Children.Values.SelectMany(items => items).Where(node => node.Tag is not null).Take(2).ToArray();
        return nodes is [{ Tag: { Kind: MemberKind.File, UnsafeReason: null } tag }] ? tag : null;
    }

    private static void RequireEditRevision(ArchiveIndex index, ArchiveTargetReview target)
    {
        if (index.SourceIdentity != target.Native.Version.Identity || index.Revision is not { Problem: null } revision ||
            revision.Known != target.Assessment.SourceSha256 || !index.CompleteListing)
            throw new IOException("The archive member reader does not match the complete native edit assessment. Refresh and try again.");
    }

    private static ArchiveKind ReaderKind(ArchiveWriteFormat format) => format switch
    {
        ArchiveWriteFormat.Tar => ArchiveKind.Tar, ArchiveWriteFormat.TarGzip => ArchiveKind.TarGzip,
        ArchiveWriteFormat.TarBzip2 => ArchiveKind.TarBzip2, ArchiveWriteFormat.TarZstd => ArchiveKind.TarZstd,
        ArchiveWriteFormat.Rar5 => ArchiveKind.Rar, ArchiveWriteFormat.SevenZip => ArchiveKind.SevenZip,
        ArchiveWriteFormat.Gzip => ArchiveKind.Gzip, ArchiveWriteFormat.Bzip2 => ArchiveKind.Bzip2, ArchiveWriteFormat.Zstd => ArchiveKind.Zstd,
        _ => throw new NotSupportedException("This edit profile has no native archive member reader."),
    };
}
