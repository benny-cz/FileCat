using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Remote.Sftp;

/// <summary>Registers the job executors for SFTP locations with the job engine (idempotent).</summary>
public static class SftpJobs
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        JobExecutors.RegisterModule((job, fs, providers, journal) =>
        {
            if (!providers.TryGet(Schemes.Sftp, out var p) || p is not SftpProvider sftp) return null;
            var r = job.Request;
            bool toSftp = r.Destination?.Scheme == Schemes.Sftp;
            bool fromSftp = r.Sources.Count > 0 && r.Sources.All(s => s.Parent.Scheme == Schemes.Sftp);
            bool sameServer = toSftp && fromSftp && r.Sources.All(s => s.Parent.Session == r.Destination!.Session);
            return r.Kind switch
            {
                JobKind.Move when sameServer => new SftpMoveExecutor(job, fs, journal, sftp),
                JobKind.Copy or JobKind.Move when toSftp && (r.Kind == JobKind.Copy || r.Sources.All(s => s.Parent.IsFileSystem)) =>
                    new SftpUploadExecutor(job, fs, journal, sftp, providers),
                JobKind.Move when fromSftp && r.Destination is { IsFileSystem: true } => new SftpDownloadMoveExecutor(job, fs, journal, sftp, providers),
                JobKind.Delete when fromSftp => new SftpDeleteExecutor(job, fs, journal, sftp),
                JobKind.Rename when fromSftp && r.Sources.Count == 1 && r.NewName is not null => new SftpRenameExecutor(job, fs, journal, sftp),
                JobKind.CreateDirectory when toSftp && r.NewName is not null => new SftpCreateDirectoryExecutor(job, fs, journal, sftp),
                _ => null,
            };
        });
    }
}

/// <summary>
/// Shared plumbing: one leased connection per job (re-established after a break when the user retries), and folder
/// listings that are refreshed after every change so remote items are always changed through current entries. A name
/// the job itself added does not refresh its folder's listing for every other name (copying many files into one folder
/// would list the growing folder once per file).
/// </summary>
internal abstract class SftpExecutorBase(Job job, IFileSystemOperations fs, JobJournal journal, SftpProvider sftp) : ExecutorBase(job, fs, journal)
{
    protected readonly SftpProvider Sftp = sftp;
    private SftpLease? _lease;
    private readonly Dictionary<string, Dictionary<string, IRemoteEntry>> _listings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _added = new(StringComparer.Ordinal);

    protected ISftpChannel Channel => (_lease ??= Sftp.Lease(ConnectionLocation, Job.Token)).Channel;

    /// <summary>The location whose connection the job uses.</summary>
    protected abstract Location ConnectionLocation { get; }

    public sealed override void Execute()
    {
        try { Run(); }
        finally { _lease?.Dispose(); }
    }

    protected abstract void Run();

    /// <summary>After a broken connection, the next use connects again.</summary>
    protected void Reconnect()
    {
        if (_lease is null) return;
        _lease.Broken = true;
        _lease.Dispose();
        _lease = null;
        ForgetListings();
    }

    private void ForgetListings()
    {
        _listings.Clear();
        _added.Clear();
    }

    /// <summary>A folder's entries, listed again when the job added names since it was listed.</summary>
    protected IReadOnlyDictionary<string, IRemoteEntry> Entries(string folder)
    {
        if (_added.ContainsKey(folder)) Changed(folder);
        return Listing(folder);
    }

    /// <summary>One entry: its folder is listed again only when the job itself added this name since.</summary>
    protected IRemoteEntry? Entry(string folder, string name)
    {
        if (_added.TryGetValue(folder, out var names) && names.Contains(name)) Changed(folder);
        return Listing(folder).GetValueOrDefault(name);
    }

    private Dictionary<string, IRemoteEntry> Listing(string folder)
    {
        if (!_listings.TryGetValue(folder, out var map))
        {
            map = new Dictionary<string, IRemoteEntry>(StringComparer.Ordinal);
            foreach (var e in Channel.List(folder, Job.Token)) map[e.Name] = e;
            _listings[folder] = map;
        }
        return map;
    }

    /// <summary>An entry from a listing made now: what a change must act on.</summary>
    protected IRemoteEntry? FreshEntry(string folder, string name)
    {
        Changed(folder);
        return Entry(folder, name);
    }

    protected void Changed(string folder)
    {
        _listings.Remove(folder);
        _added.Remove(folder);
    }

    /// <summary>The job created <paramref name="name"/> in a folder: the folder's listing stays valid for every other name.</summary>
    protected void Added(string folder, string name)
    {
        if (!_listings.ContainsKey(folder)) return;
        if (!_added.TryGetValue(folder, out var names)) _added[folder] = names = new HashSet<string>(StringComparer.Ordinal);
        names.Add(name);
    }

    /// <summary>
    /// Runs a remote step; failures ask Retry/Skip/Cancel like local ones, and a retry after a lost connection
    /// connects again first. Returns false when the user skipped.
    /// </summary>
    protected bool Remote(string path, string what, Action action)
    {
        while (true)
        {
            Job.Checkpoint();
            try
            {
                action();
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                bool lost = ex is RemoteDisconnectedException;
                var d = Job.Ask(new ErrorRequest($"Could not {what}", ErrorText.Describe(ex) + (lost ? " Retry connects again." : ""), path, CanRetry: true,
                    lost ? "disconnected" : ErrorText.Classify(ex)));
                switch (d.Action)
                {
                    case DecisionAction.Retry:
                        if (lost || _lease is { Channel.IsConnected: false }) Reconnect();
                        ForgetListings();
                        continue;
                    case DecisionAction.Skip:
                        Issue(IssueSeverity.Error, path, $"Could not {what}: {ErrorText.Describe(ex)}", StepOutcome.Skipped, lost ? "disconnected" : ErrorText.Classify(ex));
                        return false;
                    default:
                        throw new OperationCanceledException();
                }
            }
        }
    }

    /// <summary>
    /// Deletes an entry; a folder (never a link) is emptied bottom-up first. Returns false when something stayed.
    /// <paramref name="count"/> counts items for progress (moves already counted them while copying).
    /// </summary>
    protected bool DeleteTree(IRemoteEntry entry, bool count)
    {
        Job.SetCurrent(entry.FullPath);
        if (entry.IsDirectory && !entry.IsLink)
        {
            IReadOnlyList<IRemoteEntry> children = [];
            if (!Remote(entry.FullPath, "read the folder", () => children = Channel.List(entry.FullPath, Job.Token)))
            {
                if (count) Job.ItemFailed();
                return false;
            }
            if (count) Job.AddTotals(children.Count, 0);
            bool all = true;
            foreach (var child in children)
            {
                Job.Checkpoint();
                all &= DeleteTree(child, count);
            }
            if (!all)
            {
                if (count) Job.ItemFailed();
                Issue(IssueSeverity.Warning, entry.FullPath, "The folder was kept because some items in it were not deleted.", StepOutcome.PartiallyApplied);
                return false;
            }
        }
        bool ok = Remote(entry.FullPath, "delete the item", entry.Delete);
        if (count)
        {
            if (ok) Job.ItemDone();
            else Job.ItemFailed();
        }
        return ok;
    }

    protected static FileSystemItemInfo Info(IRemoteEntry e) =>
        new(e.FullPath, e.IsDirectory && !e.IsLink, e.IsLink, e.IsDirectory ? -1 : e.Size, e.ModifiedUtc, DateTime.MinValue,
            e.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal);

    protected string UniqueName(string folder, string name, bool isDirectory)
    {
        var names = Entries(folder);
        return PathUtil.MakeUniqueName(name, n => names.ContainsKey(n), isDirectory);
    }
}

/// <summary>
/// Copies into an SFTP folder (plan §14.1): each file is written under a private temporary name, checked for its size,
/// and then published by renaming; replacing uses posix-rename (atomic) where the server has it and otherwise deletes
/// the old file just before. A move deletes a local source only after its copy is published and checked.
/// </summary>
internal sealed class SftpUploadExecutor(Job job, IFileSystemOperations fs, JobJournal journal, SftpProvider sftp, ProviderRegistry providers)
    : SftpExecutorBase(job, fs, journal, sftp), IHonorsTransferFilter, IHonorsReadBackVerification
{
    private const int BufferSize = 256 * 1024;
    private byte[]? _buffer;
    private int _temp;
    private bool _notedNonAtomic;

    private bool Moving => Job.Request.Kind == JobKind.Move;
    private TransferOptions Options => Job.Request.Options;

    protected override Location ConnectionLocation => Job.Request.Destination!;

    private string TempName(string folder) =>
        RemotePath.Combine(folder, $"{JournalRecovery.StagedPrefix}{Path.GetFileNameWithoutExtension(Journal.Path)[^8..]}-{Interlocked.Increment(ref _temp)}");

    protected override void Run()
    {
        var sources = Job.Request.Sources;
        string destFolder = Sftp.Resolve(Job.Request.Destination!, Channel);
        CountLocalTotals(sources);
        for (int i = 0; i < sources.Count; i++)
        {
            Job.Checkpoint();
            var source = sources[i];
            string name = sources.Count == 1 && !string.IsNullOrEmpty(Job.Request.NewName) ? Job.Request.NewName! : source.Name;
            if (RemotePath.ProblemWithName(name) is not null)
            {
                Job.ItemFailed();
                Job.RootFailed(i);
                Issue(IssueSeverity.Error, source.Name, $"\"{name}\" cannot be a name on the server.", StepOutcome.Failed);
                continue;
            }
            bool ok = source.FileSystemPath is { } local
                ? Directory.Exists(local) && !IsLink(local) ? UploadLocalFolder(local, destFolder, name) : UploadLocalFile(local, destFolder, name)
                : UploadProviderItem(source, destFolder, name);
            if (ok) Job.RootCompleted(i);
            else Job.RootFailed(i);
        }
        Job.TotalsFinal = true;
        Job.SetCurrent(null);
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private void CountLocalTotals(IReadOnlyList<ItemRef> sources)
    {
        long files = 0, bytes = 0;
        foreach (var s in sources)
        {
            if (s.FileSystemPath is not { } p)
            {
                files++;
                if (s.Size > 0) bytes += s.Size;
                continue;
            }
            if (File.Exists(p))
            {
                files++;
                bytes += new FileInfo(p).Length;
                continue;
            }
            try
            {
                foreach (var f in new DirectoryInfo(p).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }))
                {
                    if (Options.Filter is { } filter && !filter.IsMatch(f.Name)) continue;
                    files++;
                    bytes += f.Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Job.AddTotals(files, bytes);
    }

    private bool UploadLocalFolder(string local, string destFolder, string name)
    {
        string dst = RemotePath.Combine(destFolder, name);
        Job.SetCurrent(local);
        if (!EnsureFolder(destFolder, name, local)) return false;
        bool all = true;
        IEnumerable<FileSystemInfo> children;
        try { children = new DirectoryInfo(local).EnumerateFileSystemInfos().OrderBy(c => c.Name, StringComparer.Ordinal).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, local, "The folder could not be read: " + ErrorText.Describe(ex), StepOutcome.Failed);
            return false;
        }
        foreach (var child in children)
        {
            Job.Checkpoint();
            if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                Job.ItemSkipped();
                Issue(IssueSeverity.Info, child.FullName, "Links are not followed while copying to a server; it was skipped.", StepOutcome.Skipped);
                all &= !Moving; // a move keeps the folder that still holds it
                continue;
            }
            // "Only files matching": the others stay here, and so does their folder when moving.
            if (child is FileInfo && Options.Filter is { } filter && !filter.IsMatch(child.Name))
            {
                all &= !Moving;
                continue;
            }
            all &= child is DirectoryInfo ? UploadLocalFolder(child.FullName, dst, child.Name) : UploadLocalFile(child.FullName, dst, child.Name);
        }
        if (all && Moving)
        {
            try { Fs.DeleteDirectory(local); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Issue(IssueSeverity.Warning, local, "Everything was moved, but the emptied folder could not be removed: " + ErrorText.Describe(ex), StepOutcome.PartiallyApplied);
            }
        }
        return all;
    }

    /// <summary>Creates the folder on the server, or merges into an existing one.</summary>
    private bool EnsureFolder(string destFolder, string name, string sourceDisplay)
    {
        string dst = RemotePath.Combine(destFolder, name);
        IRemoteEntry? existing = null;
        if (!Remote(dst, "read the destination folder", () => existing = Entry(destFolder, name))) return false;
        if (existing is { IsDirectory: true, IsLink: false }) return true;
        if (existing is not null)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, sourceDisplay, $"\"{name}\" exists on the server and is not a folder. Rename one of them, then copy again.", StepOutcome.Failed);
            return false;
        }
        bool made = Remote(dst, "create a folder", () => Channel.CreateDirectory(dst));
        if (made) Added(destFolder, name);
        else Changed(destFolder);
        return made;
    }

    private bool UploadLocalFile(string local, string destFolder, string name)
    {
        FileSystemItemInfo? info = Fs.TryGetInfo(local);
        if (info is null)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, local, "The file no longer exists.", StepOutcome.Failed);
            return false;
        }
        bool ok = UploadFile(() => new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1, FileOptions.SequentialScan),
            info, local, destFolder, name, unchanged: () => Fs.TryGetInfo(local) is { } now && now.Size == info.Size && now.ModifiedUtc == info.ModifiedUtc);
        if (ok && Moving)
        {
            // The copy is published and its size checked: only now may the source go.
            try { Fs.DeleteFile(local); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Issue(IssueSeverity.Warning, local, "The file was copied to the server, but it could not be deleted here, so it exists in both places: " + ErrorText.Describe(ex), StepOutcome.PartiallyApplied);
            }
        }
        return ok;
    }

    private bool UploadProviderItem(ItemRef item, string destFolder, string name)
    {
        var provider = providers.Get(item.Parent.Scheme);
        if (item.IsContainer)
        {
            var location = provider.GetChildLocation(item.Parent, new EntryData(item.Name, item.Kind));
            if (location is null || !EnsureFolder(destFolder, name, item.Name)) return false;
            var children = new List<EntryData>();
            try { provider.EnumerateAsync(location, new ListSink(children), Job.Token).GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Job.ItemFailed();
                Issue(IssueSeverity.Error, item.Name, "The folder could not be read: " + ErrorText.Describe(ex), StepOutcome.Failed);
                return false;
            }
            bool all = true;
            foreach (var c in children)
            {
                if (c.Kind == EntryKind.Parent) continue;
                if (!c.IsContainer && Options.Filter is { } filter && !filter.IsMatch(c.Name)) continue;
                if (c.Has(EntryFlags.Link) && c.IsContainer)
                {
                    Job.ItemSkipped();
                    Issue(IssueSeverity.Info, c.Name, "Links to folders are not followed while copying; it was skipped.", StepOutcome.Skipped);
                    continue;
                }
                var child = provider.GetItemRef(location, c);
                Job.AddTotals(c.IsContainer ? 0 : 1, c.IsContainer ? 0 : Math.Max(0, c.Size));
                all &= UploadProviderItem(child, RemotePath.Combine(destFolder, name), c.Name);
            }
            return all;
        }
        var incoming = new FileSystemItemInfo(item.Name, false, false, item.Size, item.Modified > 0 ? new DateTime(item.Modified, DateTimeKind.Utc) : DateTime.MinValue,
            DateTime.MinValue, FileAttributes.Normal);
        return UploadFile(() => new ContentStream(Core.Content.ProgressiveContent.Sequential(provider.OpenContent(item)) ?? throw new IOException("This item has no content to copy.")), incoming,
            provider.GetDisplayPath(item.Parent).TrimEnd('/', '\\') + "/" + item.Name, destFolder, name);
    }

    /// <param name="unchanged">Whether the source is still the file the upload started with; without it, uploads restart after a break.</param>
    private bool UploadFile(Func<Stream> openSource, FileSystemItemInfo incoming, string sourceDisplay, string destFolder, string name, Func<bool>? unchanged = null)
    {
        Job.SetCurrent(sourceDisplay);
        string dst = RemotePath.Combine(destFolder, name);
        IRemoteEntry? existing = null;
        if (!Remote(dst, "read the destination folder", () => existing = Entry(destFolder, name)))
        {
            Job.ItemFailed();
            return false;
        }
        bool replace = false;
        if (Job.Request.ExpectedTarget is { } expected)
        {
            // An edit commit: replace exactly the version the edit started from, never someone else's newer one.
            if (existing is null || existing.IsLink || existing.IsDirectory || existing.Size != expected.Length || existing.ModifiedUtc.Ticks != expected.ModifiedTicks)
            {
                Job.ItemFailed();
                Issue(IssueSeverity.Error, dst, existing is null ? "Not written: the file is gone from the server."
                    : existing.IsLink ? "Not written: on the server this name is a link, and FileCat does not write through links; your edit is kept."
                    : "Not written: the file changed on the server after the edit started; your edit is kept.", StepOutcome.Failed, "conflict");
                return false;
            }
            replace = true;
        }
        else if (existing is not null)
        {
            switch (ResolveConflict(sourceDisplay, dst, incoming, existing, destFolder))
            {
                case DecisionAction.Replace:
                    replace = true;
                    break;
                case DecisionAction.KeepBothRenameIncoming:
                    name = UniqueName(destFolder, name, false);
                    dst = RemotePath.Combine(destFolder, name);
                    break;
                case DecisionAction.KeepBothRenameExisting:
                    string aside = RemotePath.Combine(destFolder, UniqueName(destFolder, name, existing.IsDirectory));
                    if (!Remote(dst, "rename the existing item", () => (FreshEntry(destFolder, name) ?? throw new FileNotFoundException("The existing item is gone.")).MoveTo(aside)))
                    {
                        Job.ItemFailed();
                        return false;
                    }
                    Changed(destFolder);
                    Issue(IssueSeverity.Info, dst, $"The existing item was renamed to \"{RemotePath.Name(aside)}\".", StepOutcome.Committed);
                    break;
                case DecisionAction.Skip:
                    Job.ItemSkipped();
                    Issue(IssueSeverity.Info, dst, "Skipped: an item with this name exists on the server.", StepOutcome.Skipped);
                    return false;
                default:
                    throw new OperationCanceledException();
            }
        }
        // A new file is group-committed like local copies (plan §9.3); a move (its source goes) or a replacement is flushed first.
        int step = Journal.Intent(Moving ? "upload-move" : "upload", sourceDisplay, dst, null, durable: Moving || replace);
        string? temp = null;
        long written = 0;
        bool differs = false;
        bool ok;
        try
        {
            ok = Remote(dst, "copy the file to the server", () =>
            {
                // After a break, the upload continues where the server's copy ends, once that copy is checked; else anew.
                long start = temp is not null && written > 0 && unchanged is not null && unchanged() ? ResumePoint(temp, openSource, incoming.Size) : 0;
                if (start == 0)
                {
                    DiscardTemp(destFolder, temp);
                    temp = TempName(destFolder);
                }
                else
                {
                    Issue(IssueSeverity.Info, dst, $"The upload was interrupted and continued at {start:N0} bytes, after the part already on the server was checked.", StepOutcome.Committed);
                }
                Job.AddBytes(start - written);
                written = start;
                using (var input = openSource())
                using (var output = start == 0 ? Channel.CreateNew(temp!) : Channel.OpenWriteAt(temp!, start))
                {
                    if (start > 0) input.Seek(start, SeekOrigin.Begin);
                    var buffer = _buffer ??= new byte[BufferSize];
                    var clock = Stopwatch.StartNew();
                    int n;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        Job.Checkpoint();
                        output.Write(buffer, 0, n);
                        written += n;
                        Job.AddBytes(n);
                        Job.Throttle(written, clock);
                    }
                }
                if (incoming.ModifiedUtc > DateTime.MinValue) Channel.SetModified(temp!, incoming.ModifiedUtc);
                var stat = Channel.Stat(temp!);
                if (stat is not { } s || s.Size != written)
                    throw new IOException($"The server holds {(stat is { } x ? x.Size : 0):N0} bytes of the {written:N0} sent, so the copy was not published.");
                // "Read back and compare content": the server's copy, before it takes the name.
                if (Options.Verify == VerifyMode.ReadBack && !ServerCopyMatches(openSource, temp!, written, sourceDisplay))
                {
                    differs = true;
                    return;
                }
                Publish(destFolder, name, temp!, replace);
                temp = null;
            });
        }
        catch (OperationCanceledException)
        {
            // Cancelled part way: the partial copy goes as well (release issue I33: it stayed on the server under its
            // hidden temporary name); nothing was published under the file's name.
            DiscardTemp(destFolder, temp);
            Journal.Done(step, StepOutcome.CanceledBeforeChange);
            throw;
        }
        if (!ok || differs)
        {
            DiscardTemp(destFolder, temp);
            Journal.Done(step, StepOutcome.Failed);
            Job.ItemFailed();
            if (differs) Issue(IssueSeverity.Error, dst, "Read-back verification found different content on the server; the copy was discarded, and nothing was published under this name.", StepOutcome.Failed);
            return false;
        }
        Journal.Done(step, StepOutcome.Committed);
        Job.ItemDone();
        return true;
    }

    /// <summary>
    /// "Read back and compare content": the server's copy, read back through the connection, has the source's bytes
    /// (SHA-256 of both). Reading all of it also catches what a resume's check of the last 64 KiB cannot: bytes before
    /// that tail that changed on the server during a break.
    /// </summary>
    private bool ServerCopyMatches(Func<Stream> openSource, string temp, long length, string sourceDisplay)
    {
        Job.SetCurrent(sourceDisplay + " (verifying)");
        Job.AddVerifyTotal(2 * length);
        long verified = 0, streamDone = 0;
        void Progress(long done)
        {
            Job.AddVerified(done - streamDone);
            verified += done - streamDone;
            streamDone = done;
        }
        try
        {
            byte[] ours, theirs;
            using (var source = openSource()) ours = PortableFileOperations.HashStream(source, System.Security.Cryptography.HashAlgorithmName.SHA256, Job.Token, Progress);
            streamDone = 0;
            using (var copy = Channel.OpenRead(temp, length)) theirs = PortableFileOperations.HashStream(copy, System.Security.Cryptography.HashAlgorithmName.SHA256, Job.Token, Progress);
            return ours.AsSpan().SequenceEqual(theirs);
        }
        catch
        {
            // A retry reads both again: this attempt's reading does not count.
            Job.AddVerified(-verified);
            Job.AddVerifyTotal(-2 * length);
            throw;
        }
    }

    private void Publish(string folder, string name, string temp, bool replace)
    {
        string dst = RemotePath.Combine(folder, name);
        if (!replace)
        {
            Channel.Rename(temp, dst);
            Added(folder, name);
            return;
        }
        Changed(folder);
        var current = Entry(folder, name);
        if (current is { IsLink: true })
        {
            // A link is replaced itself, never through to its target.
            current.Delete();
            Channel.Rename(temp, dst);
            return;
        }
        if (current is null)
        {
            Channel.Rename(temp, dst);
            return;
        }
        if (Channel.TryReplace(temp, dst)) return;
        current.Delete();
        Channel.Rename(temp, dst);
        if (!_notedNonAtomic)
        {
            _notedNonAtomic = true;
            Issue(IssueSeverity.Info, dst, "This server cannot replace a file in one step, so each old file was deleted just before its new copy took the name.", StepOutcome.Committed);
        }
    }

    private const int ResumeCheckBytes = 64 * 1024;

    /// <summary>
    /// Where an interrupted upload can continue (plan §14: partial content is verified before reuse): the size the server
    /// holds of this job's temporary file, when the last 64 KiB there read the same as the source. The server may hold part
    /// of the write that failed, so the bound is the source's length; the check proves those bytes are the source's own.
    /// 0 means starting again.
    /// </summary>
    private long ResumePoint(string temp, Func<Stream> openSource, long sourceLength)
    {
        try
        {
            if (Channel.Stat(temp) is not { IsDirectory: false } stat || stat.Size <= 0 || stat.Size > sourceLength) return 0;
            int n = (int)Math.Min(ResumeCheckBytes, stat.Size);
            var theirs = new byte[n];
            var ours = new byte[n];
            using (var remote = Channel.OpenRead(temp, stat.Size))
            {
                remote.Position = stat.Size - n;
                remote.ReadExactly(theirs);
            }
            using (var local = openSource())
            {
                if (!local.CanSeek) return 0;
                local.Position = stat.Size - n;
                local.ReadExactly(ours);
            }
            return theirs.AsSpan().SequenceEqual(ours) ? stat.Size : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or EndOfStreamException)
        {
            return 0;
        }
    }

    /// <summary>Removes a temporary file this job wrote (best effort; a leftover is named in the job's issues).</summary>
    private void DiscardTemp(string folder, string? temp)
    {
        if (temp is null) return;
        try
        {
            // Listed without the job's cancellation: a cancelled job still removes what it wrote (release issue I33).
            string name = RemotePath.Name(temp);
            Channel.List(folder, CancellationToken.None).FirstOrDefault(e => e.Name == name && !e.IsDirectory)?.Delete();
            Changed(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Issue(IssueSeverity.Warning, temp, "A partial copy is left on the server under this temporary name; delete it when the server is reachable.", StepOutcome.Failed);
        }
    }

    private DecisionAction ResolveConflict(string src, string dst, FileSystemItemInfo incoming, IRemoteEntry existing, string folder)
    {
        var existingInfo = Info(existing);
        bool typeMismatch = existingInfo.IsDirectory;
        // Remote times have one-second precision (SFTP version 3).
        bool newer = incoming.ModifiedUtc - existing.ModifiedUtc > TimeSpan.FromSeconds(2);
        if (!typeMismatch)
        {
            switch (Options.Conflicts)
            {
                case ConflictPolicy.Skip: return DecisionAction.Skip;
                case ConflictPolicy.Replace: return DecisionAction.Replace;
                case ConflictPolicy.ReplaceIfNewer: return newer ? DecisionAction.Replace : DecisionAction.Skip;
                case ConflictPolicy.KeepBothRenameIncoming: return DecisionAction.KeepBothRenameIncoming;
                case ConflictPolicy.KeepBothRenameExisting: return DecisionAction.KeepBothRenameExisting;
            }
        }
        var d = Job.Ask(new ConflictRequest(typeMismatch ? "A folder on the server has this name" : "An item with this name exists on the server",
            RemotePath.Name(dst), incoming, existingInfo, src, dst, CanReplace: !typeMismatch, SameItem: false, TypeMismatch: typeMismatch, IncomingIsNewer: newer,
            SuggestedIncomingName: UniqueName(folder, RemotePath.Name(dst), false), SuggestedExistingName: UniqueName(folder, RemotePath.Name(dst), existing.IsDirectory)));
        return d.Action == DecisionAction.ReplaceIfNewer ? newer ? DecisionAction.Replace : DecisionAction.Skip : d.Action;
    }

    private sealed class ListSink(List<EntryData> into) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries) => into.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }
}

/// <summary>A sequential stream over provider content (an archive member, a file on another server).</summary>
internal sealed class ContentStream(IContentSource source) : Stream
{
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => source.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = source.Read(_position, buffer.AsSpan(offset, count));
        _position += n;
        return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) source.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Moves within one server by renaming: nothing is copied (so there is no copy to read back), and a folder moves with
/// everything in it.
/// </summary>
internal sealed class SftpMoveExecutor(Job job, IFileSystemOperations fs, JobJournal journal, SftpProvider sftp)
    : SftpExecutorBase(job, fs, journal, sftp), IHonorsReadBackVerification
{
    protected override Location ConnectionLocation => Job.Request.Destination!;

    protected override void Run()
    {
        var sources = Job.Request.Sources;
        string destFolder = Sftp.Resolve(Job.Request.Destination!, Channel);
        Job.AddTotals(sources.Count, 0);
        for (int i = 0; i < sources.Count; i++)
        {
            Job.Checkpoint();
            var item = sources[i];
            string folder = Sftp.Resolve(item.Parent, Channel);
            string name = sources.Count == 1 && !string.IsNullOrEmpty(Job.Request.NewName) ? Job.Request.NewName! : item.Name;
            string dst = RemotePath.Combine(destFolder, name);
            Job.SetCurrent(RemotePath.Combine(folder, item.Name));
            if (folder == destFolder && name == item.Name)
            {
                Job.ItemSkipped();
                Issue(IssueSeverity.Info, dst, "The item is already here.", StepOutcome.Skipped);
                Job.RootFailed(i);
                continue;
            }
            if (item.IsContainer && RemotePath.IsSameOrUnder(dst, RemotePath.Combine(folder, item.Name)))
            {
                Job.ItemFailed();
                Issue(IssueSeverity.Error, item.Name, "A folder cannot be moved into itself.", StepOutcome.Failed);
                Job.RootFailed(i);
                continue;
            }
            int step = Journal.Intent("remote-move", RemotePath.Combine(folder, item.Name), dst);
            bool ok = Remote(dst, "move the item", () =>
            {
                if (FreshEntry(destFolder, name) is not null)
                    throw new IOException($"\"{name}\" exists in the destination folder; nothing was moved. Rename one of them first.");
                var entry = FreshEntry(folder, item.Name) ?? throw new FileNotFoundException($"\"{item.Name}\" no longer exists on the server.");
                entry.MoveTo(dst);
                Changed(folder);
                Changed(destFolder);
            });
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

/// <summary>
/// Deletes on a server (no Recycle Bin, so always permanent and confirmed by the UI). Links are removed themselves;
/// folders are emptied bottom-up through fresh listings.
/// </summary>
internal sealed class SftpDeleteExecutor(Job job, IFileSystemOperations fs, JobJournal journal, SftpProvider sftp) : SftpExecutorBase(job, fs, journal, sftp)
{
    protected override Location ConnectionLocation => Job.Request.Sources[0].Parent;

    protected override void Run()
    {
        var sources = Job.Request.Sources;
        Job.AddTotals(sources.Count, 0);
        for (int i = 0; i < sources.Count; i++)
        {
            Job.Checkpoint();
            var item = sources[i];
            string folder = Sftp.Resolve(item.Parent, Channel);
            string path = RemotePath.Combine(folder, item.Name);
            int step = Journal.Intent("remote-delete", path);
            IRemoteEntry? entry = null;
            if (!Remote(path, "read the folder", () => entry = FreshEntry(folder, item.Name)))
            {
                Journal.Done(step, StepOutcome.Failed);
                Job.ItemFailed();
                Job.RootFailed(i);
                continue;
            }
            if (entry is null)
            {
                Journal.Done(step, StepOutcome.Committed);
                Issue(IssueSeverity.Info, path, "It was already gone.", StepOutcome.Committed);
                Job.ItemDone();
                Job.RootCompleted(i);
                continue;
            }
            bool ok = DeleteTree(entry, count: true);
            Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.PartiallyApplied);
            if (ok) Job.RootCompleted(i);
            else Job.RootFailed(i);
        }
        Job.TotalsFinal = true;
    }
}

/// <summary>
/// Moves from a server to a local folder: the common download runs first, and only items that arrived completely
/// (every file published and checked) are then deleted on the server.
/// </summary>
/// <summary>
/// A move from a server to disk: the general transfer copies (and applies the filter), and only the items that arrived
/// completely, whole folders included, are then deleted on the server.
/// </summary>
internal sealed class SftpDownloadMoveExecutor(Job job, IFileSystemOperations fs, JobJournal journal, SftpProvider sftp, ProviderRegistry providers)
    : SftpExecutorBase(job, fs, journal, sftp), IHonorsTransferFilter, IHonorsReadBackVerification
{
    protected override Location ConnectionLocation => Job.Request.Sources[0].Parent;

    protected override void Run()
    {
        new StreamTransferExecutor(Job, Fs, Journal, providers).Execute();
        foreach (int i in Job.CompletedRootIndices.ToList())
        {
            Job.Checkpoint();
            var item = Job.Request.Sources[i];
            string folder = Sftp.Resolve(item.Parent, Channel);
            string path = RemotePath.Combine(folder, item.Name);
            int step = Journal.Intent("delete-moved-source", path);
            IRemoteEntry? entry = null;
            bool ok = Remote(path, "read the folder", () => entry = FreshEntry(folder, item.Name)) && entry is not null && DeleteTree(entry, count: false);
            Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.PartiallyApplied);
            if (!ok) Issue(IssueSeverity.Warning, path, "It was copied here, but not (completely) deleted on the server, so it exists in both places.", StepOutcome.PartiallyApplied);
        }
    }
}

internal sealed class SftpRenameExecutor(Job job, IFileSystemOperations fs, JobJournal journal, SftpProvider sftp) : SftpExecutorBase(job, fs, journal, sftp)
{
    protected override Location ConnectionLocation => Job.Request.Sources[0].Parent;

    protected override void Run()
    {
        var item = Job.Request.Sources[0];
        string newName = Job.Request.NewName!;
        string folder = Sftp.Resolve(item.Parent, Channel);
        string path = RemotePath.Combine(folder, item.Name), target = RemotePath.Combine(folder, newName);
        Job.AddTotals(1, 0);
        Job.SetCurrent(path);
        if (RemotePath.ProblemWithName(newName) is { } problem)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, path, $"\"{newName}\" cannot be a name on the server: {problem}", StepOutcome.Failed);
            return;
        }
        int step = Journal.Intent("remote-rename", path, target);
        bool ok = Remote(path, "rename the item", () =>
        {
            if (FreshEntry(folder, newName) is not null) throw new IOException($"An item named \"{newName}\" exists; nothing was renamed.");
            (FreshEntry(folder, item.Name) ?? throw new FileNotFoundException($"\"{item.Name}\" no longer exists on the server.")).MoveTo(target);
        });
        Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.Failed);
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

internal sealed class SftpCreateDirectoryExecutor(Job job, IFileSystemOperations fs, JobJournal journal, SftpProvider sftp) : SftpExecutorBase(job, fs, journal, sftp)
{
    protected override Location ConnectionLocation => Job.Request.Destination!;

    protected override void Run()
    {
        string folder = Sftp.Resolve(Job.Request.Destination!, Channel);
        string name = Job.Request.NewName!;
        string path = RemotePath.Combine(folder, name);
        Job.AddTotals(1, 0);
        Job.SetCurrent(path);
        if (RemotePath.ProblemWithName(name) is { } problem)
        {
            Job.ItemFailed();
            Issue(IssueSeverity.Error, path, $"\"{name}\" cannot be a name on the server: {problem}", StepOutcome.Failed);
            return;
        }
        int step = Journal.Intent("remote-mkdir", path);
        bool ok = Remote(path, "create the folder", () => Channel.CreateDirectory(path));
        Journal.Done(step, ok ? StepOutcome.Committed : StepOutcome.Failed);
        if (ok) Job.ItemDone();
        else Job.ItemFailed();
    }
}
