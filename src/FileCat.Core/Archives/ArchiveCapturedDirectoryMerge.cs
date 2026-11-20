namespace FileCat.Core.Archives;

/// <summary>A captured source directory contributes provenance to a merge; its existing member metadata stays exact.</summary>
internal sealed record ArchiveCapturedDirectoryMerge(int CapturedId, ArchiveMemberKey ExistingDirectory)
{
    internal static HashSet<int> Review(IReadOnlyList<ArchiveCapturedDirectoryMerge>? merges, ArchiveCapturedInputs? owner,
        ArchiveAssessment? source, IReadOnlySet<ArchiveMemberKey> sourceKeys,
        IReadOnlyDictionary<ArchiveMemberKey, ArchiveTransformEdit> edits, ArchiveWriteBudget budget, CancellationToken ct)
    {
        int count = merges?.Count ?? 0;
        if (count > ArchiveWritePolicy.MaxMembers) throw ArchiveWriteBudget.Limit();
        budget.AddMetadata(0, checked(192L * count)); var used = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested(); var merge = merges![i];
            if (merge is null || source is null || owner is null || merge.CapturedId <= 0 ||
                merge.ExistingDirectory is not { Type: ArchiveMemberType.Directory } key || key.CatalogRevision != source.CatalogRevision ||
                !sourceKeys.Contains(key) || edits.ContainsKey(key) || !owner.Directories.ContainsKey(merge.CapturedId) || !used.Add(merge.CapturedId))
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A directory merge must bind one captured directory to an unchanged exact source member.");
        }
        return used;
    }
}
