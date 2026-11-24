using System.IO.Compression;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

/// <summary>Member-path rules shared by the planner and the executor.</summary>
public static class ArchivePaths
{
    public const int MaxMemberSegments = 256;

    public static bool TooDeep(string member)
    {
        // A folder entry ends with '/', which starts no further segment: FileCat's own empty folder at the deepest level
        // made every later update of its ZIP fail (review finding P6 of f134ba37).
        int segments = 1;
        foreach (char c in member.AsSpan().TrimEnd('/')) if (c == '/') segments++;
        return segments > MaxMemberSegments;
    }

    /// <summary>Why a member path is unacceptable for a new or renamed member, or null.</summary>
    public static string? Problem(string member)
    {
        if (member.Length == 0) return "The name is empty.";
        if (member.Length > 32_000) return "The path is too long.";
        if (TooDeep(member)) return "The path has too many folders.";
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
internal sealed class ZipUpdateExecutor(Job job, IFileSystemOperations fs, JobJournal journal,
    Action<string, string, string>? replaceFile = null) : ExecutorBase(job, fs, journal)
{
    private const int BufferSize = 1024 * 1024;

    /// <param name="Root">The folder the user chose to add, for a file found in it: no link below it is followed.</param>
    private sealed record Addition(string Member, string? Source, bool FolderEntry, DateTime Modified, FileAttributes Attributes, long Length,
        string? Root = null, ArchiveMoveSources.Input? Move = null,ArchiveRetainedBinding? Retained=null);

    public override void Execute()
    {
        var plan = Job.Request.Archive ?? throw new InvalidOperationException("Missing archive plan.");
        if (plan.AddConflictPolicy is not null)
            throw new NotSupportedException("Typed archive conflict review is unavailable for this ZIP backend.");
        plan.EditInput?.RequireRequest(plan);
        string archive = Path.GetFullPath(plan.ArchivePath);
        string folder = Path.GetDirectoryName(archive) ?? throw new IOException("The archive needs a folder.");
        bool updating = plan.Baseline is not null;
        if (updating)
        {
            if (OperatingSystem.IsWindows())
                ArchiveOuterEfs.RequireClear(File.GetAttributes(archive), ArchiveOuterEfs.UpdateUnavailable);
            if (!plan.Baseline!.MatchesForMutation(archive, Job.Checkpoint))
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

        var move = plan.MoveSources ? new ArchiveMoveSources(Job, Fs, Journal, plan, archive) : null;
        var additions = ExpandAdditions(plan, move);
        using var sourceStream = updating ? new ArchiveMutationReadStream(archive) : null;
        void CheckOuterProtection() => sourceStream?.RequireClear();
        using var publication = new LegacyZipPublicationTransaction(Job, Fs, Journal, plan, archive, folder, move, replaceFile);
        ZipArchive? source = null;
        try
        {
            if (sourceStream is not null)
            {
                // The pathname can be replaced while this descriptor remains open on Unix. Retain
                // the bytes we actually opened so the replacement backup can be checked later.
                publication.CaptureOpenedSource(sourceStream);
                source = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: true);
                if (source.Entries.FirstOrDefault(e => e.IsEncrypted) is { } encrypted)
                    throw new NotSupportedException($"The archive has encrypted members (for example \"{encrypted.FullName}\"), which FileCat cannot re-create, so it cannot be changed here.");
            }
            var existing = source?.Entries.ToList() ?? [];
            if(plan.EditInput is not null)
            {
                string member=ArchivePaths.Normalize(plan.Changes[0].MemberPath);
                var matches=existing.Where(entry=>ArchivePaths.Normalize(entry.FullName)==member).Take(2).ToArray();
                if(matches.Length!=1||matches[0].FullName.EndsWith('/'))
                    throw new IOException("This ZIP edit no longer identifies one regular member. Save a copy of the edit and review the archive.");
            }
            var plannedNames = PlanNames(plan, existing, additions, out var replaceWith, out var skipped);
            var replacementAdditions = replaceWith.Values.ToHashSet();
            if (move is not null)
            {
                var files = plannedNames.OfType<string>().Where(n => !n.EndsWith('/')).ToHashSet(StringComparer.Ordinal);
                files.UnionWith(additions.Where(a => !a.FolderEntry && !skipped.Contains(a.Member)).Select(a => a.Member));
                var folders = plannedNames.OfType<string>().Where(n => n.EndsWith('/')).ToHashSet(StringComparer.Ordinal);
                foreach (string name in plannedNames.OfType<string>().Concat(additions.Select(a => a.FolderEntry ? a.Member + "/" : a.Member)))
                    if (Ancestors(name.TrimEnd('/')).Skip(name.EndsWith('/') ? 0 : 1).Any(files.Contains))
                        throw new IOException("An archive file blocks a folder needed for this move. Rename or remove that member first.");
                foreach (var addition in additions.Where(a => skipped.Contains(a.Member)))
                    if (addition.Move is not null)
                    {
                        // Existing directories merge just as for a local move. Their container already exists;
                        // conflict skipping applies to the files inside, not to an otherwise completed folder move.
                        if (addition.FolderEntry && folders.Contains(addition.Member + "/")) addition.Move.Written = true;
                        else addition.Move.Skipped = true;
                    }
            }
            long bytes = move is null ? existing.Sum(e => Math.Max(0, e.Length)) + additions.Sum(a => a.Length) :
                existing.Select((e, i) => plannedNames[i] is null ? 0 : replaceWith.TryGetValue(i, out var r) ? r.Length : Math.Max(0, e.Length)).Sum() +
                additions.Where(a => !skipped.Contains(a.Member) && !replacementAdditions.Contains(a)).Sum(a => a.Length);
            Job.AddTotals(plannedNames.Count(n => n is not null) + additions.Count(a => !skipped.Contains(a.Member) && !replacementAdditions.Contains(a)), bytes);
            var addedFiles = additions.Where(a => !a.FolderEntry).Select(a => a.Member).ToHashSet(StringComparer.Ordinal);
            foreach (var member in skipped.Where(addedFiles.Contains))
            {
                if (move is null) Job.ItemSkipped();
                Issue(IssueSeverity.Warning, member, "Not added: a member with this name exists, and replacing was not chosen.", StepOutcome.Skipped);
            }

            var written = new List<(string Name, Addition? From)>();
            bool packedSparseInput = false;
            CheckOuterProtection();
            using (var output = publication.CreateStage())
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
                        CheckOuterProtection();
                        if (replaceWith.TryGetValue(i, out var replacement))
                        {
                            if (replacement.FolderEntry) WriteFolder(rebuilt, replacement, entry.Comment);
                            else packedSparseInput |= WriteFile(rebuilt, name, replacement, plan.Level, entry, move);
                        }
                        else
                            CopyEntry(entry, rebuilt, name, plan.Level, CheckOuterProtection);
                        written.Add((name, replacement));
                        Job.ItemDone();
                    }
                    foreach (var addition in additions)
                    {
                        Job.Checkpoint();
                        if (skipped.Contains(addition.Member) || replacementAdditions.Contains(addition)) continue;
                        Job.SetCurrent(addition.Member);
                        if (addition.FolderEntry) WriteFolder(rebuilt, addition, null);
                        else packedSparseInput |= WriteFile(rebuilt, addition.Member, addition, plan.Level, null, move);
                        written.Add((addition.FolderEntry ? addition.Member + "/" : addition.Member, addition));
                        Job.ItemDone();
                    }
                }
                output.Flush(flushToDisk: true);
                publication.RequireClearStage(output.SafeFileHandle);
            }
            CheckOuterProtection();
            Verify(publication, written);
            ReviewRetainedInputs(additions);
            CheckOuterProtection();
            publication.Prepare();
            CheckOuterProtection();
            source?.Dispose();
            sourceStream?.Dispose();
            source = null;
            ReviewRetainedInputs(additions);
            publication.Publish(() =>
            {
                // All members of the rebuilt archive commit together.
                if (packedSparseInput) ArchiveWriteMessages.ReportSparseInputs(Job, archive);
                if (move is null)
                    for (int root = 0; root < Math.Max(1, Job.Request.Sources.Count); root++) Job.RootCompleted(root);
            });
            move?.RemoveAfterPublication(publication.PreparedSha256, publication.RequiredOrigin);
        }
        catch (Exception ex) when (!publication.Published)
        {
            publication.RecordFailure(ex);
            throw;
        }
        finally
        {
            source?.Dispose();
            publication.CleanupUnpublishedStage();
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
            if (ArchivePaths.TooDeep(name)) throw new IOException("An existing ZIP member has too many path components to update safely.");
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
        var allByName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < names.Count; i++)
            if (names[i] is { } n)
            {
                firstByName.TryAdd(n.TrimEnd('/'), i);
                if (!allByName.TryGetValue(n, out var positions)) allByName.Add(n, positions = []);
                positions.Add(i);
            }
        replaceWith = [];
        skipped = [];
        foreach (var addition in additions)
        {
            if (!firstByName.TryGetValue(addition.Member, out int at)) continue;
            bool replaceable = !addition.FolderEntry && !names[at]!.EndsWith('/') && (replacements.Contains(addition.Member) || plan.ReplaceExisting) ||
                plan.MoveSources && addition.FolderEntry && names[at]!.EndsWith('/') && plan.ReplaceExisting;
            if (replaceable)
            {
                replaceWith[at] = addition;
                // A move's new data must not be hidden by a later same-name occurrence when the source is removed.
                if (plan.MoveSources)
                    foreach (int i in allByName[addition.FolderEntry ? addition.Member + "/" : addition.Member]) replaceWith[i] = addition;
            }
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

    private List<Addition> ExpandAdditions(ArchivePlan plan, ArchiveMoveSources? move)
    {
        var result = new List<Addition>();
        foreach (var change in plan.Changes)
        {
            string member = ArchivePaths.Normalize(change.MemberPath);
            switch (change.Kind)
            {
                case ArchiveChangeKind.AddFile:
                case ArchiveChangeKind.Replace:
                {
                    if(plan.EditInput is { } owned)
                    {
                        var observed=owned.Observe(Job.Token);
                        result.Add(new Addition(member,owned.SourcePath,false,
                            (observed.MemberModified??throw new IOException("The retained edit has no modification time.")).ToDateTimeOffset().LocalDateTime,
                            FileAttributes.Normal,observed.Version.Length,Retained:new(owned,observed)));
                        break;
                    }
                    var info = new FileInfo(change.SourcePath ?? throw new ArgumentException("A source file is required."));
                    PathUtil.RefuseAmbiguousUnixName(info.FullName);
                    if (!info.Exists) throw new FileNotFoundException("The file to add no longer exists.", info.FullName);
                    if (OperatingSystem.IsWindows()) ArchiveOuterEfs.RequireClear(info.Attributes, ArchiveOuterEfs.PackUnavailable);
                    // A cloud placeholder (a OneDrive file) is a reparse point, but no link.
                    if (ReparseLinks.IsLink(info.FullName))
                        throw new IOException("The source is a link; links are not added to ZIP archives.");
                    result.Add(new Addition(member, info.FullName, false, info.LastWriteTime, info.Attributes, info.Length,
                        Move: move?.Capture(info.FullName, directory: false)));
                    break;
                }
                case ArchiveChangeKind.CreateFolderEntry:
                    result.Add(new Addition(member, null, true, DateTime.Now, FileAttributes.Directory, 0));
                    break;
                case ArchiveChangeKind.AddFolder:
                    AddTree(new DirectoryInfo(change.SourcePath ?? throw new ArgumentException("A source folder is required.")), member, result, move);
                    break;
            }
        }
        return result;
    }

    /// <summary>Adds a folder's files; links are neither followed nor stored (reported), empty folders get entries.</summary>
    private void AddTree(DirectoryInfo root, string member, List<Addition> result, ArchiveMoveSources? move)
    {
        var pending = new Stack<(DirectoryInfo Dir, string Member)>();
        pending.Push((root, member));
        while (pending.Count > 0)
        {
            Job.Checkpoint();
            var (dir, prefix) = pending.Pop();
            PathUtil.RefuseAmbiguousUnixName(dir.FullName);
            if (OperatingSystem.IsWindows() && dir.Exists) ArchiveOuterEfs.RequireClear(dir.Attributes, ArchiveOuterEfs.PackUnavailable);
            var capturedDirectory = move?.Capture(dir.FullName, directory: true);
            bool any = false;
            foreach (var entry in dir.EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                PathUtil.RefuseAmbiguousUnixName(entry.Name);
                any = true;
                string child = prefix + "/" + entry.Name;
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 && ReparseLinks.IsLink(entry.FullName))
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
                else if (entry is FileInfo file)
                {
                    if (OperatingSystem.IsWindows()) ArchiveOuterEfs.RequireClear(file.Attributes, ArchiveOuterEfs.PackUnavailable);
                    result.Add(new Addition(child, file.FullName, false, file.LastWriteTime, file.Attributes, file.Length, root.FullName,
                        move?.Capture(file.FullName, directory: false)));
                }
            }
            if (!any || move is not null)
            {
                if (capturedDirectory is not null) capturedDirectory.ExplicitDirectory = true;
                result.Add(new Addition(prefix, null, true, dir.LastWriteTime, FileAttributes.Directory, 0, Move: capturedDirectory));
            }
        }
    }

    private void WriteFolder(ZipArchive target, Addition addition, string? comment)
    {
        var entry = target.CreateEntry(addition.Member + "/");
        if (addition.Move is null) return; // Keep the existing copy route's directory behavior.
        int attributes = (int)FileAttributes.Directory;
        if (addition.Move.UnixMode is { } mode) attributes |= unchecked((int)((mode | 0x4000) << 16));
        Stamp(entry, addition.Modified, attributes, comment); addition.Move.Written = true;
    }

    private void CopyEntry(ZipArchiveEntry entry, ZipArchive target, string name, CompressionLevel level, Action checkProtection)
    {
        bool folder = name.EndsWith('/');
        var copy = target.CreateEntry(name, folder || entry.Length > 0 && entry.CompressedLength >= entry.Length ? CompressionLevel.NoCompression : level);
        Stamp(copy, entry.LastWriteTime, entry.ExternalAttributes, entry.Comment);
        if (folder) return;
        using var from = entry.Open();
        using var to = copy.Open();
        uint crc = CopyBounded(from, to, Math.Max(0, entry.Length), Math.Max(1, entry.CompressedLength), entry.FullName, checkProtection);
        if (crc != entry.Crc32)
            throw new InvalidDataException($"The member \"{entry.FullName}\" is damaged (its checksum does not match), so the archive was not changed. Test the archive for details.");
    }

    private bool WriteFile(ZipArchive target, string name, Addition addition, CompressionLevel level, ZipArchiveEntry? replaced, ArchiveMoveSources? move)
    {
        using var held = addition.Retained is null?Fs.HoldRegularFile(addition.Source!, addition.Root):null;
        if (addition.Move is not null) move!.RequireCurrent(addition.Move);
        using var from = addition.Retained is { } retained ? retained.File.OpenReadView(retained.Reviewed, Job.Token) :
            new FileStream(addition.Source!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan);
        var rawSource = from as FileStream;
        bool sparse = CheckSourceProtection().HasFlag(FileAttributes.SparseFile) || addition.Retained?.Reviewed.HasSparseMainStream == true;
        using var protection = rawSource is null ? null : Fs.HoldArchivePackingInput(rawSource.SafeFileHandle);
        var entry = target.CreateEntry(name, level);
        int attributes = replaced?.ExternalAttributes ?? (int)(addition.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive));
        if (addition.Move is { } input)
        {
            attributes = (int)(addition.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive));
            if (input.UnixMode is { } mode) attributes |= unchecked((int)((mode | 0x8000) << 16));
        }
        Stamp(entry, addition.Modified, attributes, replaced?.Comment);
        using var to = entry.Open();
        var buffer = new byte[BufferSize];
        using var hash = addition.Move is null&&addition.Retained is null ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0;
        while (true)
        {
            Job.Checkpoint();
            CheckSourceProtection();
            int n = from.Read(buffer, 0, buffer.Length);
            CheckSourceProtection();
            if (n == 0) break;
            length += n;
            if (addition.Move is not null && length > addition.Move.Info.Size) throw new IOException("The source grew while it was being moved into the archive.");
            if(addition.Retained is not null&&length>addition.Length)throw new IOException("The retained edit exceeds its reviewed size.");
            hash?.AppendData(buffer, 0, n);
            to.Write(buffer, 0, n);
            Job.AddBytes(n);
        }
        if (addition.Move is not null)
        {
            if (length != addition.Move.Info.Size) throw new IOException("The source changed length while it was being moved into the archive.");
            move!.Recorded(addition.Move, hash!.GetHashAndReset());
        }
        if(addition.Retained is { } captured)
        {
            if(length!=captured.Reviewed.Version.Length||Convert.ToHexString(hash!.GetHashAndReset())!=captured.Reviewed.Version.Sha256)
                throw new IOException("The retained edit changed while its ZIP member was written.");
            captured.File.Review(captured.Reviewed,Job.Token);
        }

        return sparse && length > 0;

        FileAttributes CheckSourceProtection()
        {
            if (!OperatingSystem.IsWindows() || rawSource is null) return 0;
            var attributes = File.GetAttributes(rawSource.SafeFileHandle);
            ArchiveOuterEfs.RequireClear(attributes, ArchiveOuterEfs.PackUnavailable);
            return attributes;
        }
    }

    private void ReviewRetainedInputs(IEnumerable<Addition> additions)
    {foreach(var addition in additions)if(addition.Retained is { } captured){Job.Checkpoint();captured.File.Review(captured.Reviewed,Job.Token);}}

    private static void Stamp(ZipArchiveEntry entry, DateTimeOffset modified, int attributes, string? comment)
    {
        // ZIP times cover 1980–2107; others fall back to the library's default rather than failing the whole update.
        try { entry.LastWriteTime = modified; }
        catch (ArgumentOutOfRangeException) { }
        entry.ExternalAttributes = attributes;
        if (!string.IsNullOrEmpty(comment)) entry.Comment = comment;
    }

    /// <summary>Copies decompressed data under the extraction limits (declared size and expansion ratio are untrusted).</summary>
    private uint CopyBounded(Stream from, Stream to, long declared, long compressed, string name, Action checkProtection)
    {
        long cap = Math.Min(ZipProvider.MaxSpooledMember, declared + 1024 * 1024);
        var buffer = new byte[BufferSize];
        long total = 0;
        uint crc = 0;
        while (true)
        {
            Job.Checkpoint();
            checkProtection();
            int n = from.Read(buffer, 0, buffer.Length);
            checkProtection();
            if (n == 0) break;
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
    private void Verify(LegacyZipPublicationTransaction publication, List<(string Name, Addition? From)> written)
    {
        Job.SetCurrent("Checking the rebuilt archive…");
        using var stream = publication.OpenStageRead();
        using var check = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entries = check.Entries;
        if (entries.Count != written.Count) throw new IOException("The rebuilt archive does not have the planned members; the original was kept.");
        var buffer = new byte[BufferSize];
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].FullName != written[i].Name) throw new IOException("The rebuilt archive's members are out of order; the original was kept.");
            if (written[i].From is not { FolderEntry: false } addition) continue;
            Job.Checkpoint();
            uint crc = 0;
            using var hash = addition.Move is null&&addition.Retained is null ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var data = entries[i].Open();
            int n;
            while ((n = data.Read(buffer, 0, buffer.Length)) > 0)
            { Job.Checkpoint(); crc = Crc32.Append(crc, buffer.AsSpan(0, n)); hash?.AppendData(buffer, 0, n); }
            if (crc != entries[i].Crc32) throw new IOException($"\"{entries[i].FullName}\" did not read back correctly; the original was kept.");
            if (addition.Move is not null && (entries[i].Length != addition.Move.Info.Size ||
                !hash!.GetHashAndReset().AsSpan().SequenceEqual(addition.Move.Hash)))
                throw new IOException("The archived content differs from the move source; the originals were kept.");
            if(addition.Retained is { } captured&&(entries[i].Length!=captured.Reviewed.Version.Length||
                Convert.ToHexString(hash!.GetHashAndReset())!=captured.Reviewed.Version.Sha256))
                throw new IOException("The rebuilt ZIP member does not match the complete retained edit.");
        }
        publication.CaptureVerifiedStage(stream);
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
                    Issue(IssueSeverity.Error, $"{Path.GetFileName(path)}: {entry.FullName}", $"Damaged: {ErrorText.WithoutFinalPeriod(ex.Message)}.", StepOutcome.Failed);
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
