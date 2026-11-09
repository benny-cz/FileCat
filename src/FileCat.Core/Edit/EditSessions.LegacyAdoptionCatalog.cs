namespace FileCat.Core.Edit;

/// <summary>
/// Read-only relationship review. A linked native copy remains retained; the old edit is
/// still the active commit route. This snapshot grants no authority to retire either folder.
/// </summary>
internal enum EditLegacyAdoptionCatalogState { LinkedRetainedCopy, NeedsReview }

internal sealed record EditLegacyAdoptionCatalogEntry(EditSessionRecord Native,
    EditSessionRecord? ActiveLegacy,EditLegacyAdoptionCatalogState State,string Reason);

public sealed partial class EditSessionStore
{
    internal IReadOnlyList<EditLegacyAdoptionCatalogEntry> ReviewLegacyAdoptions(
        Func<string,EditSessionRecord,CancellationToken,ILegacyEditSource> openLegacy,
        CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(openLegacy);
        var all=LoadAll();
        var candidates=all.Where(row=>row.IsSupported&&row.Version==EditSessionRecord.NativeStorageVersion&&
            row.LegacyAdoption is not null).ToArray();
        var originalsById=all.Where(row=>row.IsSupported&&row.Version is 1 or EditSessionRecord.CurrentVersion&&
            row.StorageRootIdentity.Length==0).GroupBy(row=>row.Id,StringComparer.Ordinal)
            .ToDictionary(group=>group.Key,group=>group.ToArray(),StringComparer.Ordinal);
        var candidatesBySource=candidates.GroupBy(row=>row.LegacyAdoption!.SourceId,StringComparer.Ordinal)
            .ToDictionary(group=>group.Key,group=>group.Count(),StringComparer.Ordinal);
        var result=new List<EditLegacyAdoptionCatalogEntry>(candidates.Length);
        foreach(var native in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var link=native.LegacyAdoption!;
            originalsById.TryGetValue(link.SourceId,out var originals);
            if(originals is not { Length:1 }||originals[0].Version!=link.SourceVersion||
                candidatesBySource[link.SourceId]!=1)
            {
                result.Add(new(native,null,EditLegacyAdoptionCatalogState.NeedsReview,
                    "The adoption has a missing or ambiguous original. Both copies are kept."));
                continue;
            }
            var old=originals[0];
            if(!MatchesOriginalTarget(old,native))
            {
                result.Add(new(native,old,EditLegacyAdoptionCatalogState.NeedsReview,
                    "The adoption target differs from its old edit. Both copies are kept."));
                continue;
            }
            try
            {
                using var source=openLegacy(Root,old,ct);
                source.Review(ct);
                if(source.Session!=old||source.RootPath!=Root||source.RootIdentity!=link.RootIdentity||
                    source.SessionIdentity!=link.SessionIdentity||source.FilesIdentity!=link.FilesIdentity||
                    source.RecordIdentity!=link.RecordIdentity||source.RecordSha256!=link.RecordSha256||
                    source.Working.Identity!=link.WorkingIdentity||source.Working.Length!=link.WorkingLength||
                    source.Working.Sha256!=link.WorkingSha256||source.Origin.Digest!=link.OriginDigest)
                    throw new IOException("The old edit no longer matches the adoption evidence.");
                using var root=OpenNativeRoot(ct,false);
                using var session=root.OpenDirectory(native.Id,native.SessionDirectoryIdentity,ct);
                RequireStorage(native,root,session);
                using var files=session.OpenDirectory("files",native.WorkingDirectoryIdentity,ct);
                string leaf=Path.GetFileName(native.WorkingPath);
                using var file=files.OpenFile(leaf,native.WorkingIdentity,ct);
                var observed=files.ObserveSnapshot(file,leaf,ct);
                bool intact=observed.Version.Length==link.WorkingLength&&
                    observed.Version.Sha256==link.WorkingSha256&&observed.Origin.Digest==link.OriginDigest;
                files.Review(ct);session.Review(ct);root.Review(ct);source.Review(ct);
                result.Add(!intact
                    ? new(native,old,EditLegacyAdoptionCatalogState.NeedsReview,
                        "The native copy no longer matches its adoption evidence. Both copies are kept.")
                    : new(native,old,EditLegacyAdoptionCatalogState.LinkedRetainedCopy,
                        "The old edit remains active; its linked native copy is retained for review."));
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                result.Add(new(native,old,EditLegacyAdoptionCatalogState.NeedsReview,
                    "The old edit cannot be matched to its native copy. Both copies are kept."));
            }
        }
        return result;
    }

    private static bool MatchesOriginalTarget(EditSessionRecord old,EditSessionRecord native)
    {
        // The copy changes only storage ownership and the working path. While it is retained,
        // no commit may revise its archive/server target or baseline independently of the old edit.
        return old==native with
        {
            Version=old.Version,Id=old.Id,CreatedUtc=old.CreatedUtc,WorkingPath=old.WorkingPath,
            WorkingIdentity=old.WorkingIdentity,WorkingDirectoryIdentity=old.WorkingDirectoryIdentity,
            StorageRootIdentity=old.StorageRootIdentity,SessionDirectoryIdentity=old.SessionDirectoryIdentity,
            LegacyAdoption=old.LegacyAdoption,LegacyAdoptionReady=old.LegacyAdoptionReady,
            StorageRevision=old.StorageRevision,PreviousRecordSha256=old.PreviousRecordSha256,
            StoredRecordSha256=old.StoredRecordSha256
        };
    }
}
