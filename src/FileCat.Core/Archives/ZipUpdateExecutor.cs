using System.IO.Compression;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

/// <summary>Member-path rules shared by the planner and the executor.</summary>
public static class ArchivePaths
{
    /// <summary>Why a member path is unacceptable for a new or renamed member, or null.</summary>
    public static string? Problem(string member)
    {
        if (member.Length == 0) return "The name is empty.";
        if (member.Length > 32_000) return "The path is too long.";
        if (member.StartsWith('/') || member.EndsWith('/') || member.Contains('\\') || member.Length > 1 && member[1] == ':')
            return "Use a relative path with '/' between folders.";
        foreach (var part in member.Split('/'))
        {
            if (part.Length == 0 || part is "." or "..") return "Empty, \".\", and \"..\" path parts are not allowed.";
            if (part.Any(c => c < ' ' || c is '<' or '>' or ':' or '"' or '|' or '?' or '*'))
                return $"\"{part}\" contains characters that are not valid in file names.";
        }
        return null;
    }

    public static string Normalize(string name) => name.Replace('\\', '/');
}

/// <summary>
/// Creates or updates a ZIP by rebuilding it beside the original (plan §15): untouched members are copied with their
/// names, times, attributes, comments, and duplicates; changed ones are written from disk. The result is checked, the
/// original must still be the version the plan saw, and only then does the rebuilt file replace it (keeping the
/// original's permissions and download mark). A failed or stopped rebuild leaves the original untouched.
/// </summary>
internal sealed class ZipUpdateExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    private const int BufferSize = 1024 * 1024;

    private sealed record Addition(string Member, string? Source, bool FolderEntry, DateTime Modified, FileAttributes Attributes, long Length);

    public override void Execute()
    {
        var plan = Job.Request.Archive ?? throw new InvalidOperationException("Missing archive plan.");
        string archive = Path.GetFullPath(plan.ArchivePath);
        string folder = Path.GetDirectoryName(archive) ?? throw new IOException("The archive needs a folder.");
        bool updating = plan.Baseline is not null;
        if (updating)
        {
            if (!plan.Baseline!.Matches(archive))
                throw new IOException("The archive changed after it was opened, so this change was not applied. Open it again and repeat the change.");
            if ((File.GetAttributes(archive) & FileAttributes.ReadOnly) != 0)
                throw new UnauthorizedAccessException("The archive is read-only; clear its read-only attribute first.");
        }
        else if (File.Exists(archive) || Directory.Exists(archive))
            throw new IOException("An item with this name already exists; choose another name, or add to the existing archive.");
        foreach (var change in plan.Changes)
        {
            var member = ArchivePaths.Normalize(change.MemberPath);
            if (ArchivePaths.Problem(member) is { } problem && change.Kind is not (ArchiveChangeKind.Delete or ArchiveChangeKind.Replace))
                throw new ArgumentException($"\"{change.MemberPath}\": {problem}");
            if (change.Kind == ArchiveChangeKind.Rename && ArchivePaths.Problem(change.NewMemberPath ?? "") is { } renameProblem)
                throw new ArgumentException($"\"{change.NewMemberPath}\": {renameProblem}");
        }

        var additions = ExpandAdditions(plan.Changes);
        string temp = Path.Combine(folder, ".filecat-zip-" + Guid.NewGuid().ToString("N") + ".tmp");
        int step = Journal.Intent("zip-update", archive, null, temp);
        FileStream? sourceStream = null;
        ZipArchive? source = null;
        bool published = false;
        try
        {
            if (updating)
            {
                // Writers are excluded while the copy is made, so the members read are the members replaced.
                sourceStream = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
                source = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: true);
                if (source.Entries.FirstOrDefault(e => e.IsEncrypted) is { } encrypted)
                    throw new NotSupportedException($"The archive has encrypted members (for example \"{encrypted.FullName}\"), which FileCat cannot re-create, so it cannot be changed here.");
            }
            var existing = source?.Entries.ToList() ?? [];
            var plannedNames = PlanNames(plan, existing, additions, out var replaceWith, out var skipped);
            Job.AddTotals(plannedNames.Count(n => n is not null) + additions.Count(a => !skipped.Contains(a.Member) && !replaceWith.ContainsValue(a)),
                existing.Sum(e => Math.Max(0, e.Length)) + additions.Sum(a => a.Length));
            foreach (var member in skipped.Where(m => additions.Any(a => a.Member == m && !a.FolderEntry)))
            {
                Job.ItemSkipped();
                Issue(IssueSeverity.Warning, member, "Not added: a member with this name exists, and replacing was not chosen.", StepOutcome.Skipped);
            }

            var written = new List<(string Name, Addition? From)>();
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize))
            {
                using (var rebuilt = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    if (source?.Comment is { Length: > 0 } comment) rebuilt.Comment = comment;
                    for (int i = 0; i < existing.Count; i++)
                    {
                        Job.Checkpoint();
                        if (plannedNames[i] is not { } name) continue;
                        var entry = existing[i];
                        Job.SetCurrent(name);
                        if (replaceWith.TryGetValue(i, out var replacement))
                            WriteFile(rebuilt, name, replacement, plan.Level, entry);
                        else
                            CopyEntry(entry, rebuilt, name, plan.Level);
                        written.Add((name, replacement));
                        Job.ItemDone();
                    }
                    foreach (var addition in additions)
                    {
                        Job.Checkpoint();
                        if (skipped.Contains(addition.Member) || replaceWith.ContainsValue(addition)) continue;
                        Job.SetCurrent(addition.Member);
                        if (addition.FolderEntry) rebuilt.CreateEntry(addition.Member + "/");
                        else WriteFile(rebuilt, addition.Member, addition, plan.Level, null);
                        written.Add((addition.FolderEntry ? addition.Member + "/" : addition.Member, addition));
                        Job.ItemDone();
                    }
                }
                output.Flush(flushToDisk: true);
            }
            Verify(temp, written);
            if (updating && !plan.Baseline!.Matches(archive))
                throw new IOException("The archive changed while it was being rebuilt; the original was kept.");
            string? mark = updating ? Fs.ReadOriginMark(archive) : null;
            source?.Dispose();
            sourceStream?.Dispose();
            source = null;
            sourceStream = null;
            if (updating)
            {
                // ReplaceFile keeps the original's security, attributes, and creation time on the rebuilt file.
                File.Replace(temp, archive, null, ignoreMetadataErrors: true);
                if (mark is not null && Fs.ReadOriginMark(archive) is null && !Fs.WriteOriginMark(archive, mark))
                    Issue(IssueSeverity.Warning, archive, "The archive's downloaded-from-the-internet mark could not be kept.", StepOutcome.Committed, "security");
            }
            else File.Move(temp, archive, overwrite: false);
            published = true;
            Journal.Done(step, StepOutcome.Committed);
            // The rebuilt archive replaced the original as a whole, so every item of the plan completed together.
            for (int root = 0; root < Math.Max(1, Job.Request.Sources.Count); root++) Job.RootCompleted(root);
        }
        catch (Exception ex) when (!published)
        {
            Journal.Done(step, StepOutcome.Failed, ex.Message);
            throw;
        }
        finally
        {
            source?.Dispose();
            sourceStream?.Dispose();
            if (!published && File.Exists(temp))
            {
                try { File.Delete(temp); }
                catch (IOException) { }
            }
        }
    }

    /// <summary>
    /// Final name of every existing member (null: left out), which additions replace an existing member in place, and
    /// which additions are skipped because their name exists. New name collisions stop the plan before anything is written.
    /// </summary>
    private static List<string?> PlanNames(ArchivePlan plan, List<ZipArchiveEntry> existing, List<Addition> additions,
        out Dictionary<int, Addition> replaceWith, out HashSet<string> skipped)
    {
        // Lookups are by name and by ancestor folder, so a plan over a huge archive stays linear in its members.
        var deleteAll = new HashSet<string>(StringComparer.Ordinal);
        var deleteCopy = new HashSet<(string, int)>();
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        var replacements = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in plan.Changes)
        {
            string path = ArchivePaths.Normalize(c.MemberPath);
            switch (c.Kind)
            {
                case ArchiveChangeKind.Delete when c.Ordinal is { } which: deleteCopy.Add((path, which)); break;
                case ArchiveChangeKind.Delete: deleteAll.Add(path); break;
                case ArchiveChangeKind.Rename: renames[path] = ArchivePaths.Normalize(c.NewMemberPath!); break;
                case ArchiveChangeKind.Replace: replacements.Add(path); break;
            }
        }
        var names = new List<string?>(existing.Count);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in existing)
        {
            string name = ArchivePaths.Normalize(entry.FullName);
            string trimmed = name.TrimEnd('/');
            int occurrence = occurrences.TryGetValue(name, out var seen) ? seen + 1 : 0;
            occurrences[name] = occurrence;
            if (deleteCopy.Contains((name, occurrence)) || Ancestors(trimmed).Any(deleteAll.Contains))
            {
                names.Add(null);
                continue;
            }
            // The innermost renamed folder (or the member itself) decides the new name.
            if (Ancestors(trimmed).FirstOrDefault(renames.ContainsKey) is { } from)
                name = renames[from] + name[from.Length..];
            names.Add(name);
        }
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in names.OfType<string>()) counts[n] = counts.GetValueOrDefault(n) + 1;
        for (int i = 0; i < names.Count; i++)
            if (names[i] is { } n && counts[n] > 1 && n != ArchivePaths.Normalize(existing[i].FullName))
                throw new IOException($"Renaming would create two members named \"{n}\"; nothing was changed.");
        var firstByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < names.Count; i++)
            if (names[i] is { } n) firstByName.TryAdd(n.TrimEnd('/'), i);
        replaceWith = [];
        skipped = [];
        foreach (var addition in additions)
        {
            if (!firstByName.TryGetValue(addition.Member, out int at)) continue;
            bool replaceable = !addition.FolderEntry && !names[at]!.EndsWith('/') && (replacements.Contains(addition.Member) || plan.ReplaceExisting);
            if (replaceable) replaceWith[at] = addition;
            else skipped.Add(addition.Member);
        }
        return names;
    }

    /// <summary>The member itself, then each folder above it ("a/b/c", "a/b", "a").</summary>
    private static IEnumerable<string> Ancestors(string member)
    {
        for (string path = member; path.Length > 0;)
        {
            yield return path;
            int slash = path.LastIndexOf('/');
            if (slash < 0) yield break;
            path = path[..slash];
        }
    }

    private List<Addition> ExpandAdditions(IReadOnlyList<ArchiveChange> changes)
    {
        var result = new List<Addition>();
        foreach (var change in changes)
        {
            string member = ArchivePaths.Normalize(change.MemberPath);
            switch (change.Kind)
            {
                case ArchiveChangeKind.AddFile:
                case ArchiveChangeKind.Replace:
                {
                    var info = new FileInfo(change.SourcePath ?? throw new ArgumentException("A source file is required."));
                    if (!info.Exists) throw new FileNotFoundException("The file to add no longer exists.", info.FullName);
                    result.Add(new Addition(member, info.FullName, false, info.LastWriteTime, info.Attributes, info.Length));
                    break;
                }
                case ArchiveChangeKind.CreateFolderEntry:
                    result.Add(new Addition(member, null, true, DateTime.Now, FileAttributes.Directory, 0));
                    break;
                case ArchiveChangeKind.AddFolder:
                    AddTree(new DirectoryInfo(change.SourcePath ?? throw new ArgumentException("A source folder is required.")), member, result);
                    break;
            }
        }
        return result;
    }

    /// <summary>Adds a folder's files; links are neither followed nor stored (reported), empty folders get entries.</summary>
    private void AddTree(DirectoryInfo root, string member, List<Addition> result)
    {
        var pending = new Stack<(DirectoryInfo Dir, string Member)>();
        pending.Push((root, member));
        while (pending.Count > 0)
        {
            Job.Checkpoint();
            var (dir, prefix) = pending.Pop();
            bool any = false;
            foreach (var entry in dir.EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                any = true;
                string child = prefix + "/" + entry.Name;
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Issue(IssueSeverity.Warning, entry.FullName, "Not added: links are not stored in ZIP archives and are not followed.", StepOutcome.Skipped);
                    continue;
                }
                if (ArchivePaths.Problem(child) is { } problem)
                {
                    Issue(IssueSeverity.Warning, entry.FullName, "Not added: " + problem, StepOutcome.Skipped);
                    continue;
                }
                if (entry is DirectoryInfo sub) pending.Push((sub, child));
                else if (entry is FileInfo file) result.Add(new Addition(child, file.FullName, false, file.LastWriteTime, file.Attributes, file.Length));
            }
            if (!any) result.Add(new Addition(prefix, null, true, dir.LastWriteTime, FileAttributes.Directory, 0));
        }
    }

    private void CopyEntry(ZipArchiveEntry entry, ZipArchive target, string name, CompressionLevel level)
    {
        bool folder = name.EndsWith('/');
        var copy = target.CreateEntry(name, folder || entry.Length > 0 && entry.CompressedLength >= entry.Length ? CompressionLevel.NoCompression : level);
        Stamp(copy, entry.LastWriteTime, entry.ExternalAttributes, entry.Comment);
        if (folder) return;
        using var from = entry.Open();
        using var to = copy.Open();
        uint crc = CopyBounded(from, to, Math.Max(0, entry.Length), Math.Max(1, entry.CompressedLength), entry.FullName);
        if (crc != entry.Crc32)
            throw new InvalidDataException($"The member \"{entry.FullName}\" is damaged (its checksum does not match), so the archive was not changed. Test the archive for details.");
    }

    private void WriteFile(ZipArchive target, string name, Addition addition, CompressionLevel level, ZipArchiveEntry? replaced)
    {
        var entry = target.CreateEntry(name, level);
        int attributes = replaced?.ExternalAttributes ?? (int)(addition.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive));
        Stamp(entry, addition.Modified, attributes, replaced?.Comment);
        using var from = new FileStream(addition.Source!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan);
        using var to = entry.Open();
        var buffer = new byte[BufferSize];
        int n;
        while ((n = from.Read(buffer, 0, buffer.Length)) > 0)
        {
            Job.Checkpoint();
            to.Write(buffer, 0, n);
            Job.AddBytes(n);
        }
    }

    private static void Stamp(ZipArchiveEntry entry, DateTimeOffset modified, int attributes, string? comment)
    {
        // ZIP times cover 1980–2107; others fall back to the library's default rather than failing the whole update.
        try { entry.LastWriteTime = modified; }
        catch (ArgumentOutOfRangeException) { }
        entry.ExternalAttributes = attributes;
        if (!string.IsNullOrEmpty(comment)) entry.Comment = comment;
    }

    /// <summary>Copies decompressed data under the extraction limits (declared size and expansion ratio are untrusted).</summary>
    private uint CopyBounded(Stream from, Stream to, long declared, long compressed, string name)
    {
        long cap = Math.Min(ZipProvider.MaxSpooledMember, declared + 1024 * 1024);
        var buffer = new byte[BufferSize];
        long total = 0;
        uint crc = 0;
        int n;
        while ((n = from.Read(buffer, 0, buffer.Length)) > 0)
        {
            Job.Checkpoint();
            total += n;
            if (total > cap) throw new InvalidDataException($"The member \"{name}\" expands beyond its declared size; the archive was not changed.");
            if (total > 64L * 1024 * 1024 && total / compressed > ZipProvider.MaxExpansionRatio)
                throw new InvalidDataException($"The member \"{name}\" exceeds the expansion-ratio limit (possible decompression bomb); the archive was not changed.");
            crc = Crc32.Append(crc, buffer.AsSpan(0, n));
            to.Write(buffer, 0, n);
            Job.AddBytes(n);
        }
        return crc;
    }

    /// <summary>The rebuilt file opens, has exactly the planned members in order, and every written member reads back intact.</summary>
    private void Verify(string temp, List<(string Name, Addition? From)> written)
    {
        Job.SetCurrent("Checking the rebuilt archive…");
        using var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
        using var check = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = check.Entries;
        if (entries.Count != written.Count) throw new IOException("The rebuilt archive does not have the planned members; the original was kept.");
        var buffer = new byte[BufferSize];
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].FullName != written[i].Name) throw new IOException("The rebuilt archive's members are out of order; the original was kept.");
            if (written[i].From is not { FolderEntry: false }) continue;
            Job.Checkpoint();
            uint crc = 0;
            using var data = entries[i].Open();
            int n;
            while ((n = data.Read(buffer, 0, buffer.Length)) > 0) crc = Crc32.Append(crc, buffer.AsSpan(0, n));
            if (crc != entries[i].Crc32) throw new IOException($"\"{entries[i].FullName}\" did not read back correctly; the original was kept.");
        }
    }
}

/// <summary>The archive "test" command: decompress every member and compare it with its stored checksum.</summary>
internal sealed class ZipTestExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    public override void Execute()
    {
        var sources = Job.Request.Sources;
        var buffer = new byte[1024 * 1024];
        int allOk = 0, allDamaged = 0, allEncrypted = 0, unreadable = 0;
        try
        {
            for (int index = 0; index < sources.Count; index++)
                TestOne(index, buffer, ref allOk, ref allDamaged, ref allEncrypted, ref unreadable);
        }
        finally
        {
            // What the operation says when it ends: every member intact, or how many are not.
            var parts = new List<string> { allDamaged == 0 && unreadable == 0 ? $"all {allOk:N0} members intact" : $"{allOk:N0} members intact" };
            if (allDamaged > 0) parts.Add($"{allDamaged:N0} damaged");
            if (allEncrypted > 0) parts.Add($"{allEncrypted:N0} encrypted (not tested)");
            if (unreadable > 0) parts.Add($"{unreadable:N0} {(unreadable == 1 ? "archive" : "archives")} not readable");
            Job.SetSummary(string.Join(", ", parts));
        }
    }

    private void TestOne(int index, byte[] buffer, ref int allOk, ref int allDamaged, ref int allEncrypted, ref int unreadable)
    {
        var path = Job.Request.Sources[index].FileSystemPath ?? throw new NotSupportedException("Only archives in folders can be tested.");
        Job.SetCurrent(path);
        int ok = 0, damaged = 0, encrypted = 0;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            Job.AddTotals(archive.Entries.Count, archive.Entries.Sum(e => Math.Max(0, e.Length)));
            foreach (var entry in archive.Entries)
            {
                Job.Checkpoint();
                if (entry.FullName.EndsWith('/')) { Job.ItemDone(); ok++; continue; }
                if (entry.IsEncrypted)
                {
                    encrypted++;
                    Job.ItemSkipped();
                    continue;
                }
                try
                {
                    using var data = entry.Open();
                    long total = 0, cap = Math.Min(ZipProvider.MaxSpooledMember, Math.Max(0, entry.Length) + 1024 * 1024);
                    uint crc = 0;
                    int n;
                    while ((n = data.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += n;
                        if (total > cap) throw new InvalidDataException("it expands beyond its declared size");
                        crc = Crc32.Append(crc, buffer.AsSpan(0, n));
                        Job.AddBytes(n);
                    }
                    if (crc != entry.Crc32 || total != entry.Length) throw new InvalidDataException("its checksum or size does not match");
                    ok++;
                    Job.ItemDone();
                }
                catch (InvalidDataException ex)
                {
                    damaged++;
                    Job.ItemFailed();
                    Issue(IssueSeverity.Error, $"{Path.GetFileName(path)}: {entry.FullName}", $"Damaged: {ex.Message}.", StepOutcome.Failed);
                }
            }
        }
        catch (InvalidDataException ex)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, path, "Not a readable ZIP archive: " + ex.Message, StepOutcome.Failed);
            Job.RootFailed(index);
            unreadable++;
            return;
        }
        allOk += ok;
        allDamaged += damaged;
        allEncrypted += encrypted;
        Issue(damaged > 0 ? IssueSeverity.Error : encrypted > 0 ? IssueSeverity.Warning : IssueSeverity.Info, path,
            $"{ok:N0} members intact" + (damaged > 0 ? $", {damaged:N0} damaged" : "") + (encrypted > 0 ? $", {encrypted:N0} encrypted (not tested)" : "") + ".",
            damaged > 0 ? StepOutcome.Failed : StepOutcome.Committed);
        if (damaged > 0) Job.RootFailed(index);
        else Job.RootCompleted(index);
    }
}
