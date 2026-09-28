using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Mtp;

/// <summary>
/// Job executors for phones and cameras (P8): uploads from disk (a file or a folder tree), permanent deletes, renames,
/// and new folders. Downloads need nothing here: the general transfer executor reads device content like any other
/// provider's. Every changed folder's remembered object IDs are forgotten, so later steps look items up afresh.
/// </summary>
public static class MtpJobs
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        JobExecutors.RegisterModule((job, fs, providers, journal) =>
        {
            if (!providers.TryGet(Schemes.Mtp, out var p) || p is not MtpProvider mtp) return null;
            var r = job.Request;
            bool toDevice = r.Destination?.Scheme == Schemes.Mtp;
            bool fromDevice = r.Sources.Count > 0 && r.Sources.All(s => s.Parent.Scheme == Schemes.Mtp);
            return r.Kind switch
            {
                JobKind.Copy or JobKind.Move when toDevice && r.Sources.All(s => s.Parent.IsFileSystem) => new MtpUploadExecutor(job, fs, journal, mtp),
                JobKind.Delete or JobKind.Recycle when fromDevice => new MtpDeleteExecutor(job, fs, journal, mtp),
                JobKind.Rename when fromDevice && r.Sources.Count == 1 && r.NewName is not null => new MtpRenameExecutor(job, fs, journal, mtp),
                JobKind.CreateDirectory when toDevice && r.NewName is not null => new MtpCreateDirectoryExecutor(job, fs, journal, mtp),
                _ => null,
            };
        });
    }
}

internal abstract class MtpExecutorBase(Job job, IFileSystemOperations fs, JobJournal journal, MtpProvider mtp) : ExecutorBase(job, fs, journal)
{
    protected readonly MtpProvider Mtp = mtp;

    protected static string Display(Location folder, string name) => folder.Path.Length == 0 ? name : folder.Path + "/" + name;

    /// <summary>Why a wanted name is taken by <paramref name="existing"/>: exactly, or in another letter case.</summary>
    protected static string Taken(string existing, string wanted) => existing == wanted
        ? $"an item named \"{wanted}\" is already there."
        : $"\"{existing}\" is already there, and the device's storage does not tell names apart by letter case.";

    /// <summary>Why a name cannot be used on a device, or null (MTP names are one path part).</summary>
    protected static string? BadName(string name) =>
        name.Length == 0 ? "The name is empty." : name.Contains('/') || name.Contains('\\') ? "The name contains a slash." : null;
}

internal sealed class MtpUploadExecutor(Job job, IFileSystemOperations fs, JobJournal journal, MtpProvider mtp) : MtpExecutorBase(job, fs, journal, mtp)
{
    private const int BufferSize = 1024 * 1024;
    private bool Moving => Job.Request.Kind == JobKind.Move;

    /// <summary>
    /// What each destination folder holds, listed once per job and kept current as this job adds and removes items.
    /// Listing a folder costs about a quarter millisecond per item on a phone, so looking each name up by listing again
    /// made copying many files quadratic (1,000 small files took over four minutes).
    /// </summary>
    private readonly Dictionary<string, List<PortableObject>> _folders = new(StringComparer.Ordinal);

    private List<PortableObject> Contents(Location folder)
    {
        if (!_folders.TryGetValue(folder.Path, out var items))
            _folders[folder.Path] = items = [.. Mtp.Session(folder.Session!).Children(Mtp.Resolve(folder), Job.Token)];
        return items;
    }

    /// <summary><see cref="MtpProvider.FindSameName"/> against the job's index: letter case does not tell names apart.</summary>
    private PortableObject? SameName(Location folder, string name)
    {
        var matches = Contents(folder).Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count <= 1) return matches.FirstOrDefault();
        var exact = matches.Where(m => m.Name == name).ToList();
        return exact.Count == 1 ? exact[0]
            : throw new IOException($"Several items on the device are named \"{name}\" in different letter case; FileCat cannot tell which one is meant.");
    }

    private void Added(Location folder, PortableObject item)
    {
        var items = Contents(folder);
        items.RemoveAll(i => i.Id == item.Id);
        items.Add(item);
    }

    private void Removed(Location folder, string id)
    {
        if (_folders.TryGetValue(folder.Path, out var items)) items.RemoveAll(i => i.Id == id);
    }

    public override void Execute()
    {
        var dest = Job.Request.Destination!;
        var sources = Job.Request.Sources;
        foreach (var s in sources)
        {
            var info = Fs.TryGetInfo(s.FileSystemPath!);
            if (info is null) continue;
            if (!info.IsDirectory) Job.AddTotals(1, info.Size);
            else
                foreach (var f in Directory.EnumerateFiles(info.Path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                    Job.AddTotals(1, new FileInfo(f).Length);
        }
        Job.TotalsFinal = true;
        for (int i = 0; i < sources.Count; i++)
        {
            Job.Checkpoint();
            var s = sources[i];
            string name = sources.Count == 1 && !string.IsNullOrEmpty(Job.Request.NewName) ? Job.Request.NewName! : s.Name;
            bool ok = Upload(s.FileSystemPath!, dest, name);
            if (ok) Job.RootCompleted(i);
            else Job.RootFailed(i);
        }
        Job.SetCurrent(null);
    }

    private bool Upload(string source, Location folder, string name)
    {
        string target = Display(folder, name);
        if (BadName(name) is { } bad)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, target, "Not copied: " + bad, StepOutcome.Failed);
            return false;
        }
        var info = Fs.TryGetInfo(source);
        if (info is null)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, source, "The item no longer exists; nothing was copied.", StepOutcome.Failed);
            return false;
        }
        if (info.IsLink)
        {
            Job.ItemSkipped();
            Issue(IssueSeverity.Warning, source, "Links are not copied to devices.", StepOutcome.Skipped);
            return false;
        }
        PortableObject? existing = null;
        if (!TryIo(target, "read the device folder", () => existing = SameName(folder, name))) return false;
        if (info.IsDirectory)
        {
            if (existing is { IsFolder: false })
            {
                Job.ItemFailed();
                Issue(IssueSeverity.Error, target, "A file on the device has this folder's name; nothing was copied into it.", StepOutcome.Failed, "conflict-type");
                return false;
            }
            if (existing is null)
            {
                string? created = null;
                if (!TryIo(target, "create the folder", () => created = Mtp.Session(folder.Session!).CreateFolder(Mtp.Resolve(folder), name))) return false;
                Added(folder, new PortableObject(created!, name, true, false, -1, DateTime.UtcNow, true, false));
                _folders[Display(folder, name)] = []; // a new folder starts empty
            }
            Mtp.Changed(folder);
            // A folder whose name differs only in letter case is the same folder there: merge into it, under its own name.
            var child = folder.WithPath(Display(folder, existing?.Name ?? name));
            bool all = true;
            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
            {
                Job.Checkpoint();
                all &= Upload(entry, child, Path.GetFileName(entry));
            }
            if (all && Moving) TryIo(source, "remove the moved folder", () => Directory.Delete(source, recursive: false));
            return all;
        }
        PortableObject? replacing = null;
        if (existing is not null)
        {
            switch (Conflict(source, target, info, existing))
            {
                case DecisionAction.Replace:
                    if (existing.IsFolder)
                    {
                        Job.ItemFailed();
                        Issue(IssueSeverity.Error, target, "A folder on the device has this name; it was not replaced.", StepOutcome.Failed, "conflict-type");
                        return false;
                    }
                    replacing = existing;
                    break;
                case DecisionAction.KeepBothRenameIncoming:
                    name = UniqueName(folder, name);
                    target = Display(folder, name);
                    break;
                case DecisionAction.Skip:
                    Job.ItemSkipped();
                    Issue(IssueSeverity.Info, target, "Skipped: an item with this name is on the device.", StepOutcome.Skipped);
                    return false;
                default:
                    throw new OperationCanceledException();
            }
        }
        Job.SetCurrent(target);
        int step = Journal.Intent(Moving ? "device-upload-move" : "device-upload", source, target);
        bool ok = replacing is null ? Send(source, folder, name, target) : Replace(source, folder, name, target, replacing);
        Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.Failed);
        if (!ok)
        {
            Job.ItemFailed();
            return false;
        }
        if (Moving && !TryIo(source, "remove the moved file", () => Fs.DeleteFile(source)))
        {
            Issue(IssueSeverity.Warning, source, "Copied to the device, but the original could not be removed.", StepOutcome.PartiallyApplied);
        }
        Job.ItemDone();
        return true;
    }

    private long _sent;

    /// <summary>
    /// Writes the file under <paramref name="name"/> and checks that the device holds all of it. A cancel or a failure part
    /// way leaves nothing: the device reverts the unfinished file, and whatever it kept anyway is removed.
    /// </summary>
    private bool Send(string source, Location folder, string name, string target)
    {
        bool ok = TryIo(target, "copy the file to the device", () =>
        {
            Job.AddBytes(-_sent);
            _sent = 0;
            try
            {
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
                var session = Mtp.Session(folder.Session!);
                var output = session.CreateFile(Mtp.Resolve(folder), name, input.Length);
                using (output)
                {
                    var buffer = new byte[BufferSize];
                    int n;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        Job.Checkpoint();
                        output.Write(buffer, 0, n);
                        _sent += n;
                        Job.AddBytes(n);
                    }
                }
                Mtp.Changed(folder);
                // The device reports what it stored; a short copy is an error, never a success. The new object is read by
                // its ID where the device tells it, and looked up by name otherwise.
                var stored = (output as ICreatedObject)?.CreatedObjectId is { } id ? session.Get(id) : null;
                if (stored is null || stored.Name != name) stored = Mtp.Find(folder, name);
                if (stored is null || stored.Size != _sent) throw new IOException($"The device holds {(stored?.Size ?? 0):N0} of {_sent:N0} bytes, so the copy is incomplete.");
                Added(folder, stored);
            }
            catch
            {
                RemoveUnfinished(folder, name);
                throw;
            }
        });
        _sent = 0;
        return ok;
    }

    /// <summary>After a cancel or failure: a file the device kept under a name this step created is removed.</summary>
    private void RemoveUnfinished(Location folder, string name)
    {
        try
        {
            Mtp.Changed(folder);
            if (Mtp.Find(folder, name) is { IsFolder: false } leftover)
            {
                Mtp.Session(folder.Session!).Delete(leftover.Id, recursive: false);
                Removed(folder, leftover.Id);
            }
            Mtp.Changed(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Replacing keeps the old file until the new one is complete on the device: it is written under a temporary name,
    /// then the old file is removed and the new one takes the name. A device that cannot rename gets a second copy under
    /// the real name before the temporary one goes, so the data exists on the device at every moment.
    /// </summary>
    private bool Replace(string source, Location folder, string name, string target, PortableObject old)
    {
        string temporary = "~filecat-" + Guid.NewGuid().ToString("N")[..8] + ".part";
        if (!Send(source, folder, temporary, Display(folder, temporary))) return false;
        if (!TryIo(target, "remove the old copy", () => Mtp.Session(folder.Session!).Delete(old.Id, recursive: false)))
        {
            RemoveUnfinished(folder, temporary);
            return false;
        }
        Removed(folder, old.Id);
        Mtp.Changed(folder);
        bool renamed = false;
        try
        {
            var written = Contents(folder).FirstOrDefault(i => i.Name == temporary) ?? Mtp.Find(folder, temporary)
                          ?? throw new IOException("The new copy is no longer on the device.");
            var session = Mtp.Session(folder.Session!);
            session.Rename(written.Id, name);
            Mtp.Changed(folder);
            if (session.Get(written.Id) is { } after && after.Name == name)
            {
                Removed(folder, written.Id);
                Added(folder, after);
                renamed = true;
            }
        }
        catch (IOException) { Mtp.Changed(folder); }
        if (renamed) return true;
        // No rename on this device: write the file again under its own name, then drop the temporary copy.
        if (!Send(source, folder, name, target))
        {
            Issue(IssueSeverity.Error, target, $"The device does not allow renaming, and writing the new copy under its own name failed: it is on the device as \"{temporary}\".", StepOutcome.PartiallyApplied);
            return false;
        }
        RemoveUnfinished(folder, temporary);
        return true;
    }

    private DecisionAction Conflict(string source, string target, FileSystemItemInfo incoming, PortableObject existing)
    {
        bool newer = existing.ModifiedUtc > DateTime.MinValue && incoming.ModifiedUtc - existing.ModifiedUtc > TimeSpan.FromSeconds(2);
        switch (Job.Request.Options.Conflicts)
        {
            case ConflictPolicy.Skip: return DecisionAction.Skip;
            case ConflictPolicy.Replace: return DecisionAction.Replace;
            case ConflictPolicy.ReplaceIfNewer: return newer ? DecisionAction.Replace : DecisionAction.Skip;
            case ConflictPolicy.KeepBothRenameIncoming or ConflictPolicy.KeepBothRenameExisting: return DecisionAction.KeepBothRenameIncoming;
        }
        var existingInfo = new FileSystemItemInfo(target, existing.IsFolder, false, existing.Size, existing.ModifiedUtc, existing.ModifiedUtc, FileAttributes.Normal);
        string title = existing.Name != Path.GetFileName(target)
            ? $"\"{existing.Name}\" is on the device: its name differs only in letter case, which the device's storage ignores"
            : existing.IsFolder ? "A folder on the device has this name" : "An item with this name is on the device";
        var d = Job.Ask(new ConflictRequest(title,
            Path.GetFileName(target), incoming, existingInfo, source, target, CanReplace: !existing.IsFolder, SameItem: false, TypeMismatch: existing.IsFolder,
            IncomingIsNewer: newer, SuggestedIncomingName: null, SuggestedExistingName: null));
        return d.Action switch
        {
            DecisionAction.ReplaceIfNewer => newer ? DecisionAction.Replace : DecisionAction.Skip,
            DecisionAction.KeepBothRenameExisting => DecisionAction.KeepBothRenameIncoming,
            var a => a,
        };
    }

    private string UniqueName(Location folder, string name)
    {
        string stem = NameParts.GetStem(name), ext = NameParts.GetExtension(name);
        for (int i = 2; i < 10_000; i++)
        {
            string candidate = ext.Length == 0 ? $"{stem} ({i})" : $"{stem} ({i}).{ext}";
            if (SameName(folder, candidate) is null) return candidate;
        }
        throw new IOException("No free name was found.");
    }
}

internal sealed class MtpDeleteExecutor(Job job, IFileSystemOperations fs, JobJournal journal, MtpProvider mtp) : MtpExecutorBase(job, fs, journal, mtp)
{
    public override void Execute()
    {
        var sources = Job.Request.Sources;
        Job.AddTotals(sources.Count, 0);
        Job.TotalsFinal = true;
        for (int i = 0; i < sources.Count; i++)
        {
            Job.Checkpoint();
            var item = sources[i];
            string target = Display(item.Parent, item.Name);
            int step = Journal.Intent("device-delete", target);
            PortableObject? obj = null;
            bool found = TryIo(target, "find the item", () => obj = Mtp.Find(item.Parent, item.Name));
            if (found && obj is null)
            {
                Journal.Done(step, StepOutcome.Committed);
                Issue(IssueSeverity.Info, target, "It was already gone.", StepOutcome.Committed);
                Job.ItemDone();
                Job.RootCompleted(i);
                continue;
            }
            bool ok = found && TryIo(target, "delete the item", () => Mtp.Session(item.Parent.Session!).Delete(obj!.Id, recursive: obj.IsFolder));
            Mtp.Changed(item.Parent);
            Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.Failed);
            if (ok)
            {
                Job.ItemDone();
                Job.RootCompleted(i);
            }
            else
            {
                Job.ItemFailed();
                Job.RootFailed(i);
            }
        }
    }
}

internal sealed class MtpRenameExecutor(Job job, IFileSystemOperations fs, JobJournal journal, MtpProvider mtp) : MtpExecutorBase(job, fs, journal, mtp)
{
    public override void Execute()
    {
        var item = Job.Request.Sources[0];
        string newName = Job.Request.NewName!;
        string target = Display(item.Parent, item.Name);
        Job.AddTotals(1, 0);
        Job.TotalsFinal = true;
        if (BadName(newName) is { } bad)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, target, "Not renamed: " + bad, StepOutcome.Failed);
            return;
        }
        PortableObject? obj = null, clash = null;
        bool ok = TryIo(target, "read the device folder", () =>
        {
            obj = Mtp.Find(item.Parent, item.Name);
            clash = Mtp.FindSameName(item.Parent, newName);
        });
        // A taken name is not something a retry fixes: it fails at once, with the reason (renaming to the item's own
        // name in another letter case is fine).
        string? refusal = !ok ? null : obj is null ? "the item is no longer on the device."
            : clash is not null && clash.Id != obj.Id ? Taken(clash.Name, newName) : null;
        if (refusal is not null)
        {
            Issue(IssueSeverity.Error, target, "Not renamed: " + refusal, StepOutcome.Failed, "conflict");
            ok = false;
        }
        else if (ok)
        {
            ok = TryIo(target, "rename the item", () => Mtp.Session(item.Parent.Session!).Rename(obj!.Id, newName));
        }
        Mtp.Changed(item.Parent);
        if (ok)
        {
            Job.ItemDone();
            Job.RootCompleted(0);
        }
        else
        {
            Job.ItemFailed();
            Job.RootFailed(0);
        }
    }
}

internal sealed class MtpCreateDirectoryExecutor(Job job, IFileSystemOperations fs, JobJournal journal, MtpProvider mtp) : MtpExecutorBase(job, fs, journal, mtp)
{
    public override void Execute()
    {
        var folder = Job.Request.Destination!;
        string name = Job.Request.NewName!;
        string target = Display(folder, name);
        Job.AddTotals(1, 0);
        Job.TotalsFinal = true;
        if (BadName(name) is { } bad)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, target, "Not created: " + bad, StepOutcome.Failed);
            return;
        }
        PortableObject? clash = null;
        bool ok = TryIo(target, "read the device folder", () => clash = Mtp.FindSameName(folder, name));
        if (ok && clash is not null)
        {
            Issue(IssueSeverity.Error, target, "Not created: " + Taken(clash.Name, name), StepOutcome.Failed, "conflict");
            ok = false;
        }
        else if (ok)
        {
            ok = TryIo(target, "create the folder", () => Mtp.Session(folder.Session!).CreateFolder(Mtp.Resolve(folder), name));
        }
        Mtp.Changed(folder);
        if (ok) Job.ItemDone();
        else Job.ItemFailed();
    }
}
