namespace FileCat.Core.Archives;

/// <summary>Preserve complete duplicate groups during relocation; reject newly colliding logical names.</summary>
internal static class ArchiveNamespaceReview
{
    internal static void Validate(IReadOnlyList<ArchiveManifestMember> source,
        IReadOnlyList<(ArchiveManifestMember Member, string? OriginalName)> output, ArchiveWriteBudget budget, CancellationToken ct)
    {
        budget.AddMetadata(0, checked(128L * source.Count + 64L * output.Count));
        var originalCounts = new Dictionary<(string, ArchiveMemberType), int>();
        foreach (var member in source)
        {
            ct.ThrowIfCancellationRequested();
            if (member.Type == ArchiveMemberType.Directory) budget.AddMetadata(0, checked(2L * member.Name.Length));
            var key = (Canonical(member.Name, member.Type), member.Type);
            originalCounts[key] = checked(originalCounts.GetValueOrDefault(key) + 1);
        }
        var names = new Dictionary<string, (ArchiveMemberType Type, string? Original, bool Renamed, bool StyleChanged, int Count)>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (member, originalName) in output)
        {
            ct.ThrowIfCancellationRequested();
            if (member.Type == ArchiveMemberType.Directory) budget.AddMetadata(0, checked(4L * member.Name.Length + 2L * (originalName?.Length ?? 0)));
            string name = Canonical(member.Name, member.Type);
            string? original = originalName is null ? null : Canonical(originalName, member.Type);
            bool renamed = member.Name != originalName;
            bool style = member.Type == ArchiveMemberType.Directory && originalName is not null && member.Name.EndsWith('/') != originalName.EndsWith('/');
            if (names.TryGetValue(name, out var previous))
            {
                if (previous.Type != member.Type || original is null || previous.Original != original) throw ArchiveManifestMember.Invalid();
                names[name] = (member.Type, original, renamed || previous.Renamed, style || previous.StyleChanged, checked(previous.Count + 1));
            }
            else names.Add(name, (member.Type, original, renamed, style, 1));
            if (member.Type == ArchiveMemberType.File) files.Add(name);
        }
        foreach (var group in names.Values)
        {
            ct.ThrowIfCancellationRequested();
            if (group.Count > 1 && (group.StyleChanged || group.Renamed &&
                (group.Original is null || originalCounts.GetValueOrDefault((group.Original, group.Type)) != group.Count)))
                throw ArchiveManifestMember.Invalid();
        }
        string[] ordered = names.Keys.ToArray(); Array.Sort(ordered, StringComparer.Ordinal);
        foreach (string file in files)
        {
            ct.ThrowIfCancellationRequested(); string prefix = file + "/"; int next = Array.BinarySearch(ordered, prefix, StringComparer.Ordinal);
            if (next < 0) next = ~next;
            if (next < ordered.Length && ordered[next].StartsWith(prefix, StringComparison.Ordinal)) throw ArchiveManifestMember.Invalid();
        }
    }
    private static string Canonical(string name, ArchiveMemberType type) => type == ArchiveMemberType.Directory ? name.TrimEnd('/') : name;
}
