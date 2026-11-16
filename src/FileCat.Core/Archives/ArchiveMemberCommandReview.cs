namespace FileCat.Core.Archives;

/// <summary>Exact member/folder command scope from a complete catalog, never from a tolerant listing.</summary>
internal sealed class ArchiveMemberCommandReview
{
    private readonly ArchiveManifestMember[] ordered;
    private readonly Dictionary<string, int> files = new(StringComparer.Ordinal), directories = new(StringComparer.Ordinal);
    private readonly int[] fileCounts, directoryCounts;
    private readonly long[] lengths;
    internal long RetainedHeapBytes { get; }

    internal ArchiveMemberCommandReview(ArchiveAssessment assessment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var members = assessment.Manifest.Members;
        // Charge the catalog that remains live as well as these maps, references and prefix arrays.
        var budget = new ArchiveWriteBudget();
        budget.AddMetadata(0, checked(4096L + assessment.Manifest.Features.Length + members.Count * 160L));
        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();
            budget.AddMetadata(0, checked(512L + member.Metadata.Length + member.Name.Length * 2L));
        }
        ordered = members.ToArray();
        Array.Sort(ordered, (a, b) =>
        {
            int name = StringComparer.Ordinal.Compare(a.Name, b.Name);
            if (name != 0) return name;
            if (a.Type != b.Type) return a.Type == ArchiveMemberType.File ? -1 : 1;
            return a.Occurrence.CompareTo(b.Occurrence);
        });
        fileCounts = new int[ordered.Length + 1]; directoryCounts = new int[ordered.Length + 1]; lengths = new long[ordered.Length + 1];
        for (int i = 0; i < ordered.Length; i++)
        {
            ct.ThrowIfCancellationRequested(); var member = ordered[i];
            var counts = member.Type == ArchiveMemberType.Directory ? directories : files;
            counts[member.Name] = checked(counts.GetValueOrDefault(member.Name) + 1);
            fileCounts[i + 1] = fileCounts[i] + (member.Type == ArchiveMemberType.File ? 1 : 0);
            directoryCounts[i + 1] = directoryCounts[i] + (member.Type == ArchiveMemberType.Directory ? 1 : 0);
            lengths[i + 1] = checked(lengths[i] + member.Length);
        }
        RetainedHeapBytes = budget.RetainedHeapBytes;
    }

    internal ArchiveMemberCommandScope Select(string name, bool directory, int occurrence = 0)
    {
        ArchiveWritePolicy.ValidateName(name, directory ? ArchiveMemberType.Directory : ArchiveMemberType.File);
        if (directory)
        {
            string path = name.TrimEnd('/'), prefix = path + "/";
            var (start, end) = Range(prefix);
            int directoryRecords = directoryCounts[end] - directoryCounts[start] + directories.GetValueOrDefault(path);
            int fileRecords = fileCounts[end] - fileCounts[start];
            if (directoryRecords + fileRecords == 0) throw Changed();
            return new(prefix, true, 0, 0, fileRecords, directoryRecords, lengths[end] - lengths[start]);
        }
        int copies = files.GetValueOrDefault(name);
        if (occurrence < 0 || occurrence >= copies) throw Changed();
        return new(name, false, occurrence, copies, 1, 0, ordered[LowerBound(name) + occurrence].Length);
    }

    internal string? RenameProblem(ArchiveMemberCommandScope source, string destination)
    {
        try { ArchiveWritePolicy.ValidateName(destination, source.Directory ? ArchiveMemberType.Directory : ArchiveMemberType.File); }
        catch (ArchiveWriteException error) { return error.Message; }
        string canonical = destination.TrimEnd('/');
        if (canonical == source.Path.TrimEnd('/')) return "The name is unchanged.";
        var (start, end) = Range(canonical + "/");
        return files.ContainsKey(canonical) || directories.ContainsKey(canonical) || start != end
            ? "A member or folder already has that name. Choose another name." : null;
    }

    internal string? FolderProblem(string path)
    {
        try { ArchiveWritePolicy.ValidateName(path, ArchiveMemberType.Directory); }
        catch (ArchiveWriteException error) { return error.Message; }
        string canonical = path.TrimEnd('/'); var (start, end) = Range(canonical + "/");
        if (files.ContainsKey(canonical) || directories.ContainsKey(canonical) || start != end)
            return "A member or folder already has that name.";
        for (int slash = canonical.IndexOf('/'); slash >= 0; slash = canonical.IndexOf('/', slash + 1))
            if (files.ContainsKey(canonical[..slash])) return "A file blocks this folder path.";
        return null;
    }

    internal bool DirectoryExists(string path)
    {
        if (path.Length == 0) return true;
        string canonical = path.TrimEnd('/'); var (start, end) = Range(canonical + "/");
        return directories.ContainsKey(canonical) || start != end;
    }

    internal string? DestinationFolderProblem(string path)
    {
        if (path.Length == 0) return null;
        try { ArchiveWritePolicy.ValidateName(path, ArchiveMemberType.Directory); }
        catch (ArchiveWriteException error) { return error.Message; }
        string canonical = path.TrimEnd('/');
        if (files.ContainsKey(canonical)) return "An archive file already uses this folder name.";
        for (int slash = canonical.IndexOf('/'); slash >= 0; slash = canonical.IndexOf('/', slash + 1))
            if (files.ContainsKey(canonical[..slash])) return "An archive file blocks this folder path.";
        return null;
    }

    private (int Start, int End) Range(string prefix)
    {
        int start = LowerBound(prefix), low = start, high = ordered.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (ordered[middle].Name.StartsWith(prefix, StringComparison.Ordinal)) low = middle + 1; else high = middle;
        }
        return (start, low);
    }

    private int LowerBound(string name)
    {
        int low = 0, high = ordered.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (StringComparer.Ordinal.Compare(ordered[middle].Name, name) < 0) low = middle + 1; else high = middle;
        }
        return low;
    }

    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged,
        "The selected archive member no longer matches the complete catalog. Refresh the archive and select it again.");
}

internal sealed record ArchiveMemberCommandScope(string Path, bool Directory, int Occurrence, int Copies,
    int Files, int DirectoryRecords, long LogicalBytes)
{
    internal int PhysicalEntries => checked(Files + DirectoryRecords);
    internal ArchiveChange Change(ArchiveChangeKind kind, string? destination = null) =>
        new(kind, Path, NewMemberPath: destination, Ordinal: Directory ? null : Occurrence);
}
