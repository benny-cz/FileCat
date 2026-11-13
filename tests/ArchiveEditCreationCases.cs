using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Tests;

/// <summary>
/// Remaining-work ledger rows 4 and 8 (plan risk R21): how native storage makes a new edit, now that it is the default
/// store. A copy that fails or is cancelled removes exactly what it made, and what cannot be removed exactly is kept and
/// named; a creation is out of the session list while it runs; its bytes are copied without the storage gate, so other
/// edits are made meanwhile, and this process charges it at its admitted size until it ends.
/// </summary>
internal static class ArchiveEditCreationCases
{
    internal static readonly string[] Scenarios =
        ["failed-copy", "cancelled-copy", "failed-copy-kept", "in-progress-hidden", "concurrent-copies", "concurrent-accounting",
         "published-record-failure", "cancel-before-marker", "unfinished-before-marker", "replaced-before-removal", "held-briefly",
         "folder-not-held", "published-meanwhile", "abandon-without-gate", "set-aside-leftover", "grown-partial", "pending-free-space",
         "describe-outside-gate", "revision-beside-pending", "desktop-artifact", "complete-copy-kept", "store-artifact",
         "revision-stage-kept", "marker-only-kept", "empty-unfinished"];

    /// <summary>
    /// Qualification runs (FILECAT_EDIT_NATIVE_REQUIRED=1) fail where a native case cannot run in this process; other runs
    /// skip it.
    /// </summary>
    internal static void SkipPrivileged(string reason)
    {
        if (!Environment.IsPrivilegedProcess) return;
        if (Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_REQUIRED") == "1") Assert.Fail(reason);
        Assert.Skip(reason);
    }

    /// <summary>
    /// Where a native fixture is made: FILECAT_EDIT_NATIVE_ROOT, else the temporary folder, as the path it really is (macOS
    /// reaches /var, and /tmp with TMPDIR=/tmp, through a link, which native storage refuses to follow).
    /// </summary>
    internal static string FixtureParent()
    {
        string parent = Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT") ?? Path.GetTempPath());
        if (OperatingSystem.IsMacOS() && parent.StartsWith("/var/", StringComparison.Ordinal) &&
            new DirectoryInfo("/var").ResolveLinkTarget(true)?.FullName == "/private/var") parent = "/private" + parent;
        return parent;
    }

    /// <summary>
    /// In a qualification run, a native case must run on a file system this build lists for this runtime, so its evidence is
    /// a listed route's (FILECAT_EDIT_NATIVE_ROOT points the fixtures at one where the temporary folder is elsewhere).
    /// </summary>
    internal static void RequireListedRoute(string folder)
    {
        if (Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_REQUIRED") != "1") return;
        string full = Path.GetFullPath(folder);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // The mount the folder is on: the longest root that contains it at a separator boundary.
        string? fileSystem = DriveInfo.GetDrives()
            .Where(drive =>
            {
                string driveRoot = drive.RootDirectory.FullName;
                string prefix = driveRoot.EndsWith(Path.DirectorySeparatorChar) ? driveRoot : driveRoot + Path.DirectorySeparatorChar;
                return full.Equals(driveRoot.TrimEnd(Path.DirectorySeparatorChar), comparison) || full.StartsWith(prefix, comparison);
            })
            .OrderByDescending(drive => drive.RootDirectory.FullName.Length)
            .Select(drive => { try { return drive.DriveFormat; } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; } })
            .FirstOrDefault(format => !string.IsNullOrEmpty(format));
        string runtime = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        if (!NativeEditStorage.IsQualified(NativeEditStorage.Qualified, runtime, fileSystem))
            Assert.Fail($"The test folder {full} is on {fileSystem ?? "an unknown file system"}, which is not a listed native edit storage route for {runtime}.");
    }

    internal static bool Handles(string scenario) => Scenarios.Contains(scenario, StringComparer.Ordinal);

    internal static void Run(string scenario, EditSessionStore store, Func<string, bool, CancellationToken, EditDirectoryLease> open,
        string fixture, MemoryContentSource source)
    {
        var ct = TestContext.Current.CancellationToken;
        var revision = source.GetRevision()!.Value;
        switch (scenario)
        {
            case "failed-copy":
            {
                using var failing = new HookedSource(source, _ => throw new IOException("Owned interrupted edit copy"));
                var error = Assert.Throws<IOException>(() => store.CreateRemote("owned", "owned", "/owned.txt", failing, revision, null, ct));
                Assert.Equal("Owned interrupted edit copy", error.Message);
                Assert.False(error.Data.Contains(EditSessionStore.RetainedCreationKey));
                Assert.Empty(store.LoadAll()); AssertOnlyGate(store);
                break;
            }
            case "cancelled-copy":
            {
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
                using var cancelling = new HookedSource(source, _ => stop.Cancel());
                var error = Assert.ThrowsAny<OperationCanceledException>(() =>
                    store.CreateRemote("owned", "owned", "/owned.txt", cancelling, revision, null, stop.Token));
                Assert.False(error.Data.Contains(EditSessionStore.RetainedCreationKey));
                Assert.Empty(store.LoadAll()); AssertOnlyGate(store);
                break;
            }
            case "failed-copy-kept":
            {
                // While the copy runs, something else puts a file into its private folder. The working copy FileCat made is
                // still removed exactly, but the folder is no longer only what FileCat made: it is kept, named in the
                // failure, and listed as an interrupted edit, its creation marker with it.
                string? folder = null;
                using var hooked = new HookedSource(source, _ =>
                {
                    folder = Assert.Single(Directory.GetDirectories(store.NativeRoot));
                    File.WriteAllText(Path.Combine(folder, "files", "unexpected.txt"), "another program's file");
                    throw new IOException("Owned interrupted edit copy");
                });
                var error = Assert.Throws<IOException>(() => store.CreateRemote("owned", "owned", "/owned.txt", hooked, revision, null, ct));
                var kept = Assert.IsType<string>(error.Data[EditSessionStore.RetainedCreationKey]);
                Assert.Contains(folder!, kept, StringComparison.Ordinal);
                var row = Assert.Single(store.LoadAll());
                Assert.False(row.IsSupported); Assert.Equal(EditSessionStore.InterruptedCreationReason, row.UnsupportedReason);
                Assert.Equal(folder, store.SessionDirectory(row));
                Assert.True(File.Exists(Path.Combine(folder!, "creation.json")));
                Assert.Equal(["unexpected.txt"], Directory.GetFileSystemEntries(Path.Combine(folder!, "files")).Select(Path.GetFileName));
                break;
            }
            case "in-progress-hidden":
            {
                bool seen = false;
                using var hooked = new HookedSource(source, _ =>
                {
                    if (seen) return; seen = true;
                    // The creation is on disk (its folder, its marker, a working file being written)...
                    string folder = Assert.Single(Directory.GetDirectories(store.NativeRoot));
                    Assert.True(File.Exists(Path.Combine(folder, "creation.json")));
                    Assert.False(File.Exists(Path.Combine(folder, "session.json")));
                    // ...but it is not an edit yet: no session list of this process shows it, this store's or another's.
                    Assert.Empty(store.LoadAll());
                    Assert.Empty(new EditSessionStore(store.Root, new PortableFileOperations(), open).LoadAll());
                });
                var made = store.CreateRemote("owned", "owned", "/owned.txt", hooked, revision, null, ct);
                Assert.True(seen); Assert.Equal(made, Assert.Single(store.LoadAll()));
                break;
            }
            case "concurrent-copies":
            {
                // The first copy waits, inside its source, until a second edit has been made entirely: the storage gate is
                // not held while bytes are copied (with it held, the second would wait for the first and then fail).
                using var firstReading = new ManualResetEventSlim(); using var secondMade = new ManualResetEventSlim(); int reads = 0;
                using var slow = new HookedSource(source, _ =>
                {
                    if (Interlocked.Increment(ref reads) != 1) return;
                    firstReading.Set();
                    if (!secondMade.Wait(TimeSpan.FromSeconds(60))) throw new IOException("The second edit was not made while the first one copied.");
                });
                var first = Task.Factory.StartNew(() => store.CreateRemote("owned", "owned", "/first.txt", slow, revision, null, ct),
                    ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                try
                {
                    Assert.True(firstReading.Wait(TimeSpan.FromSeconds(60), ct));
                    using var other = new MemoryContentSource("second.txt", "second complete copy"u8.ToArray());
                    var second = store.CreateRemote("owned", "owned", "/second.txt", other, other.GetRevision()!.Value, null, ct);
                    Assert.True(second.IsSupported);
                    // While the first one is still copying, the list shows only the edit that is complete.
                    Assert.Equal(second, Assert.Single(store.LoadAll()));
                }
                finally { secondMade.Set(); }
                Assert.True(first.Wait(TimeSpan.FromSeconds(60), ct));
                Assert.Equal(2, store.LoadAll().Count(row => row.IsSupported));
                break;
            }
            case "concurrent-accounting":
            {
                // A creation of this process is charged at its admitted size while it copies (here 12 MiB of a 20 MiB
                // budget), so a second one that would not fit beside it is refused, even though the first one's files are
                // still small; once it is complete, its measured size counts and another edit fits again.
                var limited = new EditSessionStore(store.Root, new PortableFileOperations(), open, null, new EditStorageReviewLimits(Bytes: 20L * 1024 * 1024));
                using var firstReading = new ManualResetEventSlim(); using var secondTried = new ManualResetEventSlim(); int reads = 0;
                using var slow = new HookedSource(source, _ =>
                {
                    if (Interlocked.Increment(ref reads) != 1) return;
                    firstReading.Set();
                    if (!secondTried.Wait(TimeSpan.FromSeconds(60))) throw new IOException("The second edit was not tried while the first one copied.");
                });
                var first = Task.Factory.StartNew(() => limited.CreateRemote("owned", "owned", "/first.txt", slow, revision, null, ct),
                    ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                try
                {
                    Assert.True(firstReading.Wait(TimeSpan.FromSeconds(60), ct));
                    // Free space must still hold what the copy has yet to write: here all of it.
                    Assert.Equal(source.Length, limited.ReviewNativeStorageUsage(ct).PendingBytes);
                    using var other = new MemoryContentSource("second.txt", "second complete copy"u8.ToArray());
                    var refused = Assert.Throws<IOException>(() => limited.CreateRemote("owned", "owned", "/second.txt", other, other.GetRevision()!.Value, null, ct));
                    Assert.Contains("no room", refused.Message, StringComparison.Ordinal);
                }
                finally { secondTried.Set(); }
                Assert.True(first.Wait(TimeSpan.FromSeconds(60), ct));
                using var third = new MemoryContentSource("third.txt", "third complete copy"u8.ToArray());
                Assert.True(limited.CreateRemote("owned", "owned", "/third.txt", third, third.GetRevision()!.Value, null, ct).IsSupported);
                Assert.Equal(2, limited.LoadAll().Count(row => row.IsSupported));
                AssertNoUnfinished(limited);
                break;
            }
            case "published-record-failure":
            {
                // R21's failed record publication: the edit's record is published, then its review fails. The creation did
                // not complete, so it removes what it made, the published record first, and nothing remains.
                var hooks = new Hooks { AfterPublish = target => { if (target == "session.json") throw new IOException("Owned record review failed"); } };
                var hooked = new EditSessionStore(store.Root, new PortableFileOperations(), Hook(open, hooks));
                var error = Assert.Throws<IOException>(() => hooked.CreateRemote("owned", "owned", "/owned.txt", source, revision, null, ct));
                Assert.Equal("Owned record review failed", error.Message);
                Assert.False(error.Data.Contains(EditSessionStore.RetainedCreationKey));
                Assert.Empty(store.LoadAll()); AssertOnlyGate(store);
                break;
            }
            case "cancel-before-marker":
            {
                // A stop that arrives while the folders are made, before the creation marker exists: the folder is made
                // without the cancelled token, held, and then removed exactly; nothing is left that nothing names.
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var hooks = new Hooks { BeforeCreateDirectory = leaf => { if (leaf == "files") stop.Cancel(); } };
                var hooked = new EditSessionStore(store.Root, new PortableFileOperations(), Hook(open, hooks));
                Assert.ThrowsAny<OperationCanceledException>(() => hooked.CreateRemote("owned", "owned", "/owned.txt", source, revision, null, stop.Token));
                Assert.True(stop.IsCancellationRequested);
                Assert.Empty(store.LoadAll()); AssertOnlyGate(store);
                break;
            }
            case "unfinished-before-marker":
            {
                // What a crash leaves before the creation marker exists: an edit folder with an empty working folder. It is
                // listed as an interrupted copy, not hidden, and its review removes it.
                using (var parent = open(Path.GetDirectoryName(store.NativeRoot)!, false, ct))
                using (var root = parent.CreateDirectory(Path.GetFileName(store.NativeRoot), ct))
                using (var session = root.CreateDirectory(Guid.NewGuid().ToString("N"), ct))
                using (session.CreateDirectory("files", ct)) { }
                string folder = Assert.Single(Directory.GetDirectories(store.NativeRoot));
                var row = Assert.Single(store.LoadAll());
                Assert.False(row.IsSupported); Assert.Equal(EditSessionStore.InterruptedCreationReason, row.UnsupportedReason);
                Assert.Equal(folder, store.SessionDirectory(row));
                var removed = store.RemoveInterruptedCopy(row, ct);
                Assert.True(removed.Complete, removed.Issue);
                Assert.Empty(store.LoadAll()); AssertOnlyGate(store);
                break;
            }
            case "replaced-before-removal":
            {
                // R21's replacement before disposal: while the copy runs another object takes the working copy's name. Only
                // the object the creation made may be removed; the newcomer is kept, and so the folder and its marker.
                string? folder = null; Exception? replacement = null;
                using var hooked = new HookedSource(source, _ =>
                {
                    folder = Assert.Single(Directory.GetDirectories(store.NativeRoot));
                    string working = Path.Combine(folder, "files", "owned.txt");
                    try { File.Move(working, Path.Combine(folder, "files", "renamed.txt")); File.WriteAllText(working, "another program's file"); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { replacement = error; }
                    throw new IOException("Owned interrupted edit copy");
                });
                var error = Assert.Throws<IOException>(() => store.CreateRemote("owned", "owned", "/owned.txt", hooked, revision, null, ct));
                if (OperatingSystem.IsWindows())
                {
                    // Windows refuses the swap while FileCat holds its working copy; nothing else was there to keep.
                    Assert.NotNull(replacement);
                    Assert.Empty(store.LoadAll()); AssertOnlyGate(store);
                    break;
                }
                Assert.Null(replacement);
                Assert.Contains(folder!, Assert.IsType<string>(error.Data[EditSessionStore.RetainedCreationKey]), StringComparison.Ordinal);
                Assert.Equal("another program's file", File.ReadAllText(Path.Combine(folder!, "files", "owned.txt")));
                Assert.True(File.Exists(Path.Combine(folder!, "files", "renamed.txt")));
                Assert.Equal(EditSessionStore.InterruptedCreationReason, Assert.Single(store.LoadAll()).UnsupportedReason);
                break;
            }
            case "held-briefly":
            {
                // Another program (here this test) holds the partly copied working file for a moment when the copy fails,
                // as a scanner or another FileCat window's review may: its removal waits for it (Windows refuses the open,
                // Linux the removal's lease) and then removes everything exactly.
                using var released = new ManualResetEventSlim(); int held = 0;
                var hooks = new Hooks();
                var hooked = new EditSessionStore(store.Root, new PortableFileOperations(), Hook(open, hooks));
                hooks.BeforeOpenDiscardFile = leaf =>
                {
                    if (leaf != "owned.txt" || Interlocked.Exchange(ref held, 1) != 0) return;
                    string working = Path.Combine(Assert.Single(Directory.GetDirectories(store.NativeRoot)), "files", "owned.txt");
                    var reader = new FileStream(working, FileMode.Open, FileAccess.Read, FileShare.Read);
                    new Thread(() => { Thread.Sleep(300); reader.Dispose(); released.Set(); }) { IsBackground = true }.Start();
                };
                using var failing = new HookedSource(source, _ => throw new IOException("Owned interrupted edit copy"));
                var error = Assert.Throws<IOException>(() => hooked.CreateRemote("owned", "owned", "/owned.txt", failing, revision, null, ct));
                Assert.Equal("Owned interrupted edit copy", error.Message);
                Assert.Equal(1, held);
                Assert.True(released.Wait(TimeSpan.FromSeconds(10), ct));
                Assert.False(error.Data.Contains(EditSessionStore.RetainedCreationKey), error.Data[EditSessionStore.RetainedCreationKey] as string);
                Assert.Empty(store.LoadAll()); AssertOnlyGate(store);
                break;
            }
            case "folder-not-held":
            {
                // The creation's folder is made, but no lease can hold it (its private review refuses it, say). Nothing then
                // binds the folder to the creation, so it is not removed by name: it is kept, said in the failure, listed as
                // an interrupted copy, and removed through that copy's review.
                var hooks = new Hooks { AfterCreateDirectory = leaf => { if (Guid.TryParseExact(leaf, "N", out _)) throw new IOException("Owned folder review failed"); } };
                var hooked = new EditSessionStore(store.Root, new PortableFileOperations(), Hook(open, hooks));
                var error = Assert.Throws<IOException>(() => hooked.CreateRemote("owned", "owned", "/owned.txt", source, revision, null, ct));
                Assert.Equal("Owned folder review failed", error.Message);
                string folder = Assert.Single(Directory.GetDirectories(store.NativeRoot));
                Assert.Contains(folder, Assert.IsType<string>(error.Data[EditSessionStore.RetainedCreationKey]), StringComparison.Ordinal);
                var row = Assert.Single(store.LoadAll());
                Assert.False(row.IsSupported); Assert.Equal(EditSessionStore.InterruptedCreationReason, row.UnsupportedReason);
                var removed = store.RemoveInterruptedCopy(row, ct);
                Assert.True(removed.Complete, removed.Issue);
                Assert.Empty(store.LoadAll()); AssertOnlyGate(store);
                break;
            }
            case "published-meanwhile":
            {
                // Another process's list reads a folder just as its creation publishes the record: the record was missing when
                // the list looked, but is there once the list judges the folder under the storage gate, so the folder is read
                // again and listed as the complete edit it now is, never as an interrupted copy.
                var made = store.CreateRemote("owned", "owned", "/owned.txt", source, revision, null, ct);
                string record = Path.Combine(store.SessionDirectory(made), "session.json"), aside = Path.Combine(fixture, "session.json.aside");
                File.Move(record, aside);
                var hooks = new Hooks { BeforeOpenStorageDirectory = leaf => { if (leaf == made.Id && File.Exists(aside)) File.Move(aside, record); } };
                var observer = new EditSessionStore(store.Root, new PortableFileOperations(), Hook(open, hooks));
                Assert.Equal(made, Assert.Single(observer.LoadAll()));
                Assert.False(File.Exists(aside));
                break;
            }
            case "abandon-without-gate":
            {
                // A failed copy whose cleanup cannot take the storage gate (here the gate cannot be opened, once) removes
                // nothing and says what it kept; the copy is listed as interrupted and removed through its review.
                int gates = 0;
                var hooks = new Hooks { BeforeOpenStorageGate = () => { if (Interlocked.Increment(ref gates) == 2) throw new IOException("Owned storage gate unavailable"); } };
                var hooked = new EditSessionStore(store.Root, new PortableFileOperations(), Hook(open, hooks));
                using var failing = new HookedSource(source, _ => throw new IOException("Owned interrupted edit copy"));
                var error = Assert.Throws<IOException>(() => hooked.CreateRemote("owned", "owned", "/owned.txt", failing, revision, null, ct));
                Assert.Equal("Owned interrupted edit copy", error.Message);
                Assert.Contains("Owned storage gate unavailable", Assert.IsType<string>(error.Data[EditSessionStore.RetainedCreationKey]), StringComparison.Ordinal);
                var row = Assert.Single(store.LoadAll());
                Assert.Equal(EditSessionStore.InterruptedCreationReason, row.UnsupportedReason);
                var removed = store.RemoveInterruptedCopy(row, ct);
                Assert.True(removed.Complete, removed.Issue);
                Assert.Empty(store.LoadAll()); AssertOnlyGate(store);
                break;
            }
            case "complete-copy-kept":
            {
                // A copy that is already as long as the file it was made from, whose record went some other way (not by any
                // route of FileCat's): it may be an edit of the user's, so the folder is kept whole, listed as an edit whose
                // record is gone, and no removal is offered for it.
                var made = store.CreateRemote("owned", "owned", "/owned.txt", source, revision, null, ct);
                string folder = store.SessionDirectory(made);
                File.Delete(Path.Combine(folder, "session.json"));
                var row = Assert.Single(store.LoadAll());
                Assert.False(row.IsSupported); Assert.Equal(EditSessionStore.IncompleteEditReason, row.UnsupportedReason);
                Assert.False(store.SupportsInterruptedCopyRemoval && row.UnsupportedReason == EditSessionStore.InterruptedCreationReason);
                Assert.Throws<NotSupportedException>(() => store.RemoveInterruptedCopy(row, ct));
                Assert.Equal("original complete working copy", File.ReadAllText(made.WorkingPath));
                break;
            }
            case "store-artifact":
            {
                // The Finder writes .DS_Store into the store's own folders (here the root and an edit's folder, as 0644, which
                // is nobody's private file): the review passes it by instead of refusing every copy, new edits are still made,
                // and the edit beside it still reads.
                var first = store.CreateRemote("owned", "owned", "/owned.txt", source, revision, null, ct);
                foreach (string folder in new[] { store.NativeRoot, store.SessionDirectory(first) })
                {
                    string artifact = Path.Combine(folder, ".DS_Store");
                    File.WriteAllText(artifact, "finder settings");
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(artifact, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                }
                var usage = store.ReviewNativeStorageUsage(ct);
                Assert.Equal(1, usage.Sessions);
                using var other = new MemoryContentSource("second.txt", "second complete copy"u8.ToArray());
                var second = store.CreateRemote("owned", "owned", "/second.txt", other, other.GetRevision()!.Value, null, ct);
                Assert.True(second.IsSupported);
                var rows = store.LoadAll();
                Assert.Equal(2, rows.Count(row => row.IsSupported));
                Assert.Equal(EditState.Unchanged, store.StateOf(first));
                Assert.True(File.Exists(Path.Combine(store.NativeRoot, ".DS_Store")));
                break;
            }
            case "revision-stage-kept":
            {
                // A stage left behind by a revision's publication (not a creation's) beside a complete working copy whose
                // record is gone: the copy may hold the user's edits, so the folder is kept whole and no removal is offered,
                // although a stage of a creation's own publication would have made it an unfinished copy.
                var made = store.CreateRemote("owned", "owned", "/owned.txt", source, revision, null, ct);
                string folder = store.SessionDirectory(made);
                string stage = Path.Combine(folder, ".record-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(stage, "a revision being published");
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.Delete(Path.Combine(folder, "session.json"));
                var row = Assert.Single(store.LoadAll());
                Assert.Equal(EditSessionStore.IncompleteEditReason, row.UnsupportedReason);
                Assert.Throws<NotSupportedException>(() => store.RemoveInterruptedCopy(row, ct));
                // Even a row that claims it was interrupted removes nothing. (The stage this test writes is a file of its
                // own, not one FileCat made, so on Windows its review refuses before the copy's length is weighed.)
                var refused = store.RemoveInterruptedCopy(row with { UnsupportedReason = EditSessionStore.InterruptedCreationReason }, ct);
                Assert.False(refused.Complete); Assert.False(refused.MayHaveRemovedFiles);
                if (!OperatingSystem.IsWindows()) Assert.Contains("as long as", refused.Issue, StringComparison.Ordinal);
                Assert.Equal("original complete working copy", File.ReadAllText(made.WorkingPath));
                break;
            }
            case "marker-only-kept":
            {
                // What a cleanup leaves when it removed the copy and its working folder but could not remove the marker: a
                // folder with the marker alone. It is an unfinished copy with nothing of the user's in it, so its review
                // removes it, rather than reading as a folder FileCat cannot judge.
                var made = store.CreateRemote("owned", "owned", "/owned.txt", source, revision, null, ct);
                string folder = store.SessionDirectory(made);
                File.Delete(Path.Combine(folder, "session.json"));
                File.Delete(made.WorkingPath);
                Directory.Delete(Path.GetDirectoryName(made.WorkingPath)!);
                Assert.Equal(["creation.json"], Directory.GetFileSystemEntries(folder).Select(Path.GetFileName));
                var row = Assert.Single(store.LoadAll());
                Assert.Equal(EditSessionStore.InterruptedCreationReason, row.UnsupportedReason);
                var removed = store.RemoveInterruptedCopy(row, ct);
                Assert.True(removed.Complete, removed.Issue);
                Assert.Empty(store.LoadAll()); AssertOnlyGate(store);
                break;
            }
            case "empty-unfinished":
            {
                // An empty file is copied in full with no bytes at all, so a folder a crash left with an empty copy holds
                // nothing of the user's: it is an unfinished copy, and its review removes it (the length rule alone would
                // read it as a copy as long as its source).
                using var empty = new MemoryContentSource("empty.txt", []);
                var made = store.CreateRemote("owned", "owned", "/empty.txt", empty, empty.GetRevision()!.Value, null, ct);
                string folder = store.SessionDirectory(made);
                Assert.Equal(0, new FileInfo(made.WorkingPath).Length);
                File.Delete(Path.Combine(folder, "session.json"));
                var row = Assert.Single(store.LoadAll());
                Assert.Equal(EditSessionStore.InterruptedCreationReason, row.UnsupportedReason);
                var removed = store.RemoveInterruptedCopy(row, ct);
                Assert.True(removed.Complete, removed.Issue);
                Assert.Empty(store.LoadAll()); AssertOnlyGate(store);
                break;
            }
            case "set-aside-leftover":
            {
                // FileCat ended after it set a removed file's name aside and before it removed it (Unix's exact removal; the
                // copy's record was already gone): that name is part of the interrupted copy, and its review removes it too.
                var made = store.CreateRemote("owned", "owned", "/owned.txt", source, revision, null, ct);
                string folder = store.SessionDirectory(made);
                File.Delete(Path.Combine(folder, "session.json"));
                File.Move(made.WorkingPath, Path.Combine(folder, "files", ".filecat-removing-" + Guid.NewGuid().ToString("N")));
                var row = Assert.Single(store.LoadAll());
                Assert.Equal(EditSessionStore.InterruptedCreationReason, row.UnsupportedReason);
                var removed = store.RemoveInterruptedCopy(row, ct);
                Assert.True(removed.Complete, removed.Issue);
                Assert.Empty(store.LoadAll()); AssertOnlyGate(store);
                break;
            }
            case "grown-partial":
            {
                // A working file as long as its creation was admitted to make it, or longer, is not an unfinished copy: it may
                // hold edits of the user's (here a later line), so the folder is kept whole and no removal is offered.
                var made = store.CreateRemote("owned", "owned", "/owned.txt", source, revision, null, ct);
                string folder = store.SessionDirectory(made);
                File.Delete(Path.Combine(folder, "session.json"));
                File.AppendAllText(made.WorkingPath, " and a later line");
                var row = Assert.Single(store.LoadAll());
                Assert.Equal(EditSessionStore.IncompleteEditReason, row.UnsupportedReason);
                Assert.Throws<NotSupportedException>(() => store.RemoveInterruptedCopy(row, ct));
                var refused = store.RemoveInterruptedCopy(row with { UnsupportedReason = EditSessionStore.InterruptedCreationReason }, ct);
                Assert.False(refused.Complete); Assert.False(refused.MayHaveRemovedFiles);
                Assert.Contains("as long as", refused.Issue, StringComparison.Ordinal);
                Assert.Equal("original complete working copy and a later line", File.ReadAllText(made.WorkingPath));
                Assert.True(File.Exists(Path.Combine(folder, "creation.json")));
                break;
            }
            case "pending-free-space":
            {
                // Free space must hold what a running copy has yet to write as well as a new copy: a new copy that would fit
                // beside the running one's admitted size, but not beside the bytes it has yet to write, is refused for space,
                // and admitted once that space is there.
                using var firstReading = new ManualResetEventSlim(); using var secondTried = new ManualResetEventSlim(); int reads = 0;
                using var slow = new HookedSource(source, _ =>
                {
                    if (Interlocked.Increment(ref reads) != 1) return;
                    firstReading.Set();
                    if (!secondTried.Wait(TimeSpan.FromSeconds(60))) throw new IOException("The second edit was not tried while the first one copied.");
                });
                var first = Task.Factory.StartNew(() => store.CreateRemote("owned", "owned", "/first.txt", slow, revision, null, ct),
                    ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                try
                {
                    Assert.True(firstReading.Wait(TimeSpan.FromSeconds(60), ct));
                    long pending = store.ReviewNativeStorageUsage(ct).PendingBytes;
                    Assert.Equal(source.Length, pending);
                    using var other = new MemoryContentSource("second.txt", "second complete copy"u8.ToArray());
                    // What the second copy needs from free space beside the pending bytes: its payload, its metadata and the
                    // fixed margin admission keeps.
                    long need = other.Length + 2L * EditSessionStore.MaxRecordBytes + 5 * EditStorageAdmission.MetadataPerEntry + 64L * 1024 * 1024;
                    long available = need + pending - 1;
                    var hooks = new Hooks { Capacity = () => new FileSystemCapacitySnapshot("owned test volume", available) };
                    var tight = new EditSessionStore(store.Root, new PortableFileOperations(), Hook(open, hooks));
                    var refused = Assert.Throws<IOException>(() => tight.CreateRemote("owned", "owned", "/second.txt", other, other.GetRevision()!.Value, null, ct));
                    Assert.Contains("available space", refused.Message, StringComparison.Ordinal);
                    available = need + pending;
                    Assert.True(tight.CreateRemote("owned", "owned", "/second.txt", other, other.GetRevision()!.Value, null, ct).IsSupported);
                }
                finally { secondTried.Set(); }
                Assert.True(first.Wait(TimeSpan.FromSeconds(60), ct));
                Assert.Equal(2, store.LoadAll().Count(row => row.IsSupported));
                break;
            }
            case "describe-outside-gate":
            {
                // A copy's record is described (here its download mark kept; for an archive, the whole source reviewed again)
                // before the storage gate is taken for its publication: another edit is made in the meantime.
                using var describing = new ManualResetEventSlim(); using var secondMade = new ManualResetEventSlim();
                var files = new DescribeHook(() =>
                {
                    describing.Set();
                    if (!secondMade.Wait(TimeSpan.FromSeconds(60))) throw new IOException("The second edit was not made while the first was described.");
                });
                var described = new EditSessionStore(store.Root, files, open);
                var first = Task.Factory.StartNew(() => described.CreateRemote("owned", "owned", "/first.txt", source, revision,
                    "[ZoneTransfer]\r\nZoneId=3\r\n", ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                try
                {
                    Assert.True(describing.Wait(TimeSpan.FromSeconds(60), ct));
                    using var other = new MemoryContentSource("second.txt", "second complete copy"u8.ToArray());
                    Assert.True(store.CreateRemote("owned", "owned", "/second.txt", other, other.GetRevision()!.Value, null, ct).IsSupported);
                }
                finally { secondMade.Set(); }
                Assert.True(first.Wait(TimeSpan.FromSeconds(60), ct));
                Assert.Equal(2, store.LoadAll().Count(row => row.IsSupported));
                break;
            }
            case "revision-beside-pending":
            {
                // A revision records a commit that already happened: it is admitted without the copies still running at all,
                // so neither their charge against the storage budget nor the bytes they have yet to write refuses it, while a
                // new copy is refused by both.
                var edit = store.CreateRemote("owned", "owned", "/owned.txt", source, revision, null, ct);
                using var large = new MemoryContentSource("large.txt", new byte[8 * 1024 * 1024]);
                using var firstReading = new ManualResetEventSlim(); using var done = new ManualResetEventSlim(); int reads = 0;
                using var slow = new HookedSource(large, _ =>
                {
                    if (Interlocked.Increment(ref reads) != 1) return;
                    firstReading.Set();
                    if (!done.Wait(TimeSpan.FromSeconds(60))) throw new IOException("The revision was not saved while the copy ran.");
                });
                var copying = Task.Factory.StartNew(() => store.CreateRemote("owned", "owned", "/large.txt", slow, large.GetRevision()!.Value, null, ct),
                    ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                try
                {
                    Assert.True(firstReading.Wait(TimeSpan.FromSeconds(60), ct));
                    var usage = store.ReviewNativeStorageUsage(ct);
                    Assert.Equal(large.Length, usage.PendingBytes);
                    Assert.True(usage.RunningChargedBytes >= large.Length, $"The running copy is charged {usage.RunningChargedBytes}.");
                    using var other = new MemoryContentSource("second.txt", "second complete copy"u8.ToArray());

                    // A storage budget with room for one more record, none for a copy beside the running one's charge (the
                    // running copy is charged apart from the stored bytes, so the review itself still fits this budget).
                    long room = usage.ChargedBytes + EditSessionStore.MaxRecordBytes + EditStorageAdmission.MetadataPerEntry;
                    var budgeted = new EditSessionStore(store.Root, new PortableFileOperations(), open, null, new EditStorageReviewLimits(Bytes: room));
                    var saved = budgeted.Save(edit with { LastCommitUtc = DateTime.UnixEpoch.AddSeconds(1700000001) });
                    Assert.Equal(1, saved.StorageRevision);
                    var budget = Assert.Throws<IOException>(() => budgeted.CreateRemote("owned", "owned", "/second.txt", other, other.GetRevision()!.Value, null, ct));
                    Assert.Contains("no room for this copy", budget.Message, StringComparison.Ordinal);

                    // 70 MiB free: room for a record (its metadata and the margin), not for a copy beside the 8 MiB the
                    // running copy has yet to write.
                    var hooks = new Hooks { Capacity = () => new FileSystemCapacitySnapshot("owned test volume", 70L * 1024 * 1024) };
                    var tight = new EditSessionStore(store.Root, new PortableFileOperations(), Hook(open, hooks));
                    var again = tight.Save(saved with { LastCommitUtc = DateTime.UnixEpoch.AddSeconds(1700000002) });
                    Assert.Equal(2, again.StorageRevision);
                    var refused = Assert.Throws<IOException>(() => tight.CreateRemote("owned", "owned", "/second.txt", other, other.GetRevision()!.Value, null, ct));
                    Assert.Contains("available space for this copy", refused.Message, StringComparison.Ordinal);
                }
                finally { done.Set(); }
                Assert.True(copying.Wait(TimeSpan.FromSeconds(60), ct));
                Assert.Equal(2, store.LoadAll().Count(row => row.IsSupported));
                break;
            }
            case "desktop-artifact":
            {
                // The Finder writes .DS_Store into a folder a user opens (here beside the unfinished copy): the folder is still
                // listed as an unfinished copy, and its removal names that file for the user to remove first (it is not a
                // private file FileCat made); once it is gone, the unfinished copy is removed.
                var made = store.CreateRemote("owned", "owned", "/owned.txt", source, revision, null, ct);
                string folder = store.SessionDirectory(made), finder = Path.Combine(folder, ".DS_Store");
                File.Delete(Path.Combine(folder, "session.json"));
                File.WriteAllText(made.WorkingPath, "part"); // Shorter than its copy was admitted to make it: unfinished.
                File.WriteAllText(finder, "finder settings");
                var row = Assert.Single(store.LoadAll());
                Assert.Equal(EditSessionStore.InterruptedCreationReason, row.UnsupportedReason);
                var refused = store.RemoveInterruptedCopy(row, ct);
                Assert.False(refused.Complete); Assert.False(refused.MayHaveRemovedFiles);
                Assert.Contains(".DS_Store", refused.Issue, StringComparison.Ordinal);
                Assert.True(File.Exists(made.WorkingPath));
                File.Delete(finder);
                var removed = store.RemoveInterruptedCopy(Assert.Single(store.LoadAll()), ct);
                Assert.True(removed.Complete, removed.Issue);
                Assert.Empty(store.LoadAll()); AssertOnlyGate(store);
                break;
            }
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }

    /// <summary>
    /// Listing review of 13 November 2026: composing the store asks whether a creation can hold its own folder here (on Unix
    /// a folder lock). That question must make and leave nothing in the store: an entry nobody recognizes would refuse every
    /// later review of it, and one left behind by a process that ended would refuse them for good.
    /// </summary>
    internal static void RunComposition(Func<string, bool, CancellationToken, EditDirectoryLease> open)
    {
        var ct = TestContext.Current.CancellationToken;
        string parent = FixtureParent();
        RequireListedRoute(parent);
        string fixture = Directory.CreateDirectory(Path.Combine(parent, "filecat-compose-" + Guid.NewGuid().ToString("N"))).FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fixture, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            string state = Directory.CreateDirectory(Path.Combine(fixture, "state")).FullName;
            string native = Path.Combine(state, "edit-sessions-native"), legacy = Path.Combine(state, "edit-sessions");
            var routes = new NativeEditStorage.Route[] { new(System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, "testfs", "test") };
            var files = new PortableFileOperations();
            string[] Entries(string folder) => Directory.Exists(folder)
                ? Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray() : [];

            // Before the root exists: the state folder is asked, and keeps exactly what it had.
            var before = Entries(state);
            var first = NativeEditStorage.Compose(routes, System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                legacy, native, files, _ => "testfs", open, null);
            Assert.Equal(native, first.NativeRoot);
            Assert.False(Directory.Exists(native)); // Made on first use only.
            Assert.Equal(before, Entries(state));

            // With the root there, and an edit in it: the root keeps exactly that edit, and the store still reviews it.
            using var source = new MemoryContentSource("owned.txt", "composed working copy"u8.ToArray());
            var made = first.CreateRemote("owned", "owned", "/owned.txt", source, source.GetRevision()!.Value, null, ct);
            var held = Entries(native);
            Assert.Equal([".edit-storage.lock", made.Id], held);
            var again = NativeEditStorage.Compose(routes, System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                legacy, native, files, _ => "testfs", open, null);
            Assert.Equal(native, again.NativeRoot);
            Assert.Equal(held, Entries(native));
            Assert.Equal(made, Assert.Single(again.LoadAll()));
            Assert.Equal(before.Concat(["edit-sessions-native"]).Order(StringComparer.Ordinal), Entries(state));
        }
        finally { TestCleanup.DeleteTree(fixture); }
    }

    /// <summary>
    /// A creation whose working copy already carries its download mark when it fails (after the record is published):
    /// the marked file, its mark with it, is removed exactly like any other, and nothing remains.
    /// </summary>
    internal static void RunMarkedFailure(Func<string, bool, CancellationToken, EditDirectoryLease> open, IFileSystemOperations files)
    {
        var ct = TestContext.Current.CancellationToken;
        string parent = FixtureParent();
        RequireListedRoute(parent);
        string fixture = Directory.CreateDirectory(Path.Combine(parent, "filecat-edit-store-" + Guid.NewGuid().ToString("N"))).FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fixture, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            string? marked = null;
            var hooks = new Hooks { AfterPublish = target => { if (target == "session.json") throw new IOException("Owned record review failed"); } };
            var store = new EditSessionStore(Path.Combine(fixture, "sessions"), files, Hook(open, hooks));
            hooks.BeforePublish = target =>
            {
                if (target != "session.json") return;
                string working = Assert.Single(Directory.GetFiles(Path.Combine(Assert.Single(Directory.GetDirectories(store.NativeRoot)), "files")));
                marked = files.ReadOriginMark(working);
            };
            const string mark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/owned\r\nQuarantine=0081;6553f100;FileCat;public-test\r\n";
            using var content = new MemoryContentSource("owned.txt", "marked native edit bytes"u8.ToArray());
            Assert.Throws<IOException>(() => store.CreateRemote("owned", "owned", "/owned.txt", content, content.GetRevision()!.Value, mark, ct));
            Assert.NotNull(marked); // The working copy had its mark when the creation failed.
            Assert.Empty(store.LoadAll()); AssertOnlyGate(store);
        }
        finally { TestCleanup.DeleteTree(fixture); }
    }

    // The portable file operations, except that keeping a download mark (done while a remote copy's record is described)
    // first runs the test's hook and then succeeds.
    private sealed class DescribeHook(Action hook) : PortableFileOperations
    {
        public override bool WriteOriginMark(string path, string mark) { hook(); return true; }
    }

    private sealed class Hooks
    {
        internal Action<string>? BeforeCreateDirectory, AfterCreateDirectory, BeforePublish, AfterPublish, BeforeOpenDiscardFile, BeforeOpenStorageDirectory;
        internal Action? BeforeOpenStorageGate;
        internal Func<FileSystemCapacitySnapshot?>? Capacity;
    }

    private static Func<string, bool, CancellationToken, EditDirectoryLease> Hook(Func<string, bool, CancellationToken, EditDirectoryLease> open, Hooks hooks) =>
        (path, requirePrivate, ct) => new HookLease(open(path, requirePrivate, ct), hooks);

    // Delegates everything; runs the test's hooks around folder creation and record publication.
    private sealed class HookLease(EditDirectoryLease owner, Hooks hooks) : EditDirectoryLease
    {
        internal override string Path => owner.Path; internal override string Identity => owner.Identity; internal override SafeFileHandle Handle => owner.Handle;
        private EditDirectoryLease Wrap(EditDirectoryLease child) => new HookLease(child, hooks);
        internal override EditDirectoryLease CreateDirectory(string leaf, CancellationToken ct, bool removable = false)
        {
            hooks.BeforeCreateDirectory?.Invoke(leaf);
            var made = owner.CreateDirectory(leaf, ct, removable);
            try { hooks.AfterCreateDirectory?.Invoke(leaf); } catch { made.Dispose(); throw; }
            return Wrap(made);
        }
        internal override EditDirectoryLease OpenDirectory(string leaf, string? id, CancellationToken ct) => Wrap(owner.OpenDirectory(leaf, id, ct));
        internal override EditDirectoryLease OpenStorageDirectory(string leaf, CancellationToken ct)
        { hooks.BeforeOpenStorageDirectory?.Invoke(leaf); return Wrap(owner.OpenStorageDirectory(leaf, ct)); }
        internal override bool CanDiscardReviewedSession => owner.CanDiscardReviewedSession;
        internal override EditDirectoryLease OpenDiscardDirectory(string leaf, string? id, CancellationToken ct) => Wrap(owner.OpenDiscardDirectory(leaf, id, ct));
        internal override FileStream OpenDiscardFile(string leaf, string? id, CancellationToken ct)
        { hooks.BeforeOpenDiscardFile?.Invoke(leaf); return owner.OpenDiscardFile(leaf, id, ct); }
        internal override FileStream CreateFile(string leaf, CancellationToken ct) => owner.CreateFile(leaf, ct);
        internal override FileStream OpenFile(string leaf, string? id, CancellationToken ct) => owner.OpenFile(leaf, id, ct);
        internal override FileStream OpenWorkingFile(string leaf, CancellationToken ct) => owner.OpenWorkingFile(leaf, ct);
        internal override FileStream OpenWorkingFileForReview(string leaf, CancellationToken ct) => owner.OpenWorkingFileForReview(leaf, ct);
        internal override FileStream OpenSharedRecord(string leaf, CancellationToken ct) => owner.OpenSharedRecord(leaf, ct);
        internal override ContentRevision ReviewWorkingFile(FileStream file, string leaf, CancellationToken ct) => owner.ReviewWorkingFile(file, leaf, ct);
        internal override ArchiveOriginSnapshot ReadOrigin(FileStream file, string leaf, CancellationToken ct) => owner.ReadOrigin(file, leaf, ct);
        internal override void CopyOrigin(FileStream file, string leaf, ArchiveOriginSnapshot origin, CancellationToken ct) => owner.CopyOrigin(file, leaf, origin, ct);
        internal override void ReviewRemoval(FileStream file, string leaf, ArchiveFileObservation expected, CancellationToken ct) => owner.ReviewRemoval(file, leaf, expected, ct);
        internal override void DeleteSnapshot(FileStream file, string leaf, ArchiveFileObservation expected, CancellationToken ct) => owner.DeleteSnapshot(file, leaf, expected, ct);
        internal override bool CanRemoveOwnedDirectories => owner.CanRemoveOwnedDirectories;
        internal override void RequireRemovable(FileStream file, string leaf) => owner.RequireRemovable(file, leaf);
        internal override void RemoveEmpty(CancellationToken ct) => owner.RemoveEmpty(ct);
        internal override IReadOnlyList<string> Names(CancellationToken ct) => owner.Names(ct);
        internal override IReadOnlyList<string> Names(CancellationToken ct, int max) => owner.Names(ct, max);
        internal override EditStoredFileSize MeasureStoredFile(string leaf, CancellationToken ct) => owner.MeasureStoredFile(leaf, ct);
        internal override EditStoredDirectorySize MeasureStoredDirectory(CancellationToken ct) => owner.MeasureStoredDirectory(ct);
        internal override FileStream OpenStorageGate(CancellationToken ct) { hooks.BeforeOpenStorageGate?.Invoke(); return owner.OpenStorageGate(ct); }
        internal override bool TryLockStorageGate(FileStream file, CancellationToken ct) => owner.TryLockStorageGate(file, ct);
        internal override FileSystemCapacitySnapshot? StorageCapacity(CancellationToken ct) => hooks.Capacity is { } capacity ? capacity() : owner.StorageCapacity(ct);
        internal override string Identify(SafeFileHandle file) => owner.Identify(file);
        internal override void PublishNewFile(FileStream file, string source, string target, EditFileVersion expected, CancellationToken ct)
        { hooks.BeforePublish?.Invoke(target); owner.PublishNewFile(file, source, target, expected, ct); hooks.AfterPublish?.Invoke(target); }
        protected override void ReviewCore(CancellationToken ct) => owner.Review(ct);
        protected override void CloseCore() => owner.Dispose();
    }

    /// <summary>
    /// Save copy of a native edit as the application makes it (<see cref="EditSessionStore.CaptureSaveCopy"/>, then
    /// <see cref="EditSessionStore.PublishSaveCopy"/>): the working copy is read through its held folder, the copy keeps the
    /// download mark that working copy carries, replaces an existing destination through a verified stage, and leaves nothing
    /// in the native store or the temporary folder; the edit stays.
    /// </summary>
    internal static void RunSaveCopy(bool replace, Func<string, bool, CancellationToken, EditDirectoryLease> open, IFileSystemOperations files)
    {
        var ct = TestContext.Current.CancellationToken;
        string parent = FixtureParent();
        RequireListedRoute(parent);
        string fixture = Directory.CreateDirectory(Path.Combine(parent, "filecat-edit-store-" + Guid.NewGuid().ToString("N"))).FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fixture, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var store = new EditSessionStore(Path.Combine(fixture, "sessions"), files, open);
            const string mark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/owned\r\nQuarantine=0081;6553f100;FileCat;public-test\r\n";
            using var content = new MemoryContentSource("owned.txt", "first native edit bytes"u8.ToArray());
            var record = store.CreateRemote("owned", "owned", "/owned.txt", content, content.GetRevision()!.Value, mark, ct);
            // The editor's save, in place (the file, and so its download mark, stays).
            using (var save = new FileStream(record.WorkingPath, FileMode.Open, FileAccess.Write)) { save.SetLength(0); save.Write("saved native edit"u8); }
            string? kept = files.ReadOriginMark(record.WorkingPath);
            Assert.NotNull(kept);
            string temp = Directory.CreateDirectory(Path.Combine(fixture, "temp")).FullName;
            string folder = Directory.CreateDirectory(Path.Combine(fixture, "out")).FullName, destination = Path.Combine(folder, "saved.txt");
            if (replace) File.WriteAllText(destination, "previous destination");
            string? lost;
            using (var copy = store.CaptureSaveCopy(record, temp, new LocalFileSystemProvider(), () => ct.ThrowIfCancellationRequested()))
            {
                Assert.Null(copy.Native); // A temporary copy, not a capture in the native store.
                Assert.StartsWith(temp + Path.DirectorySeparatorChar, copy.Path, StringComparison.Ordinal);
                lost = EditSessionStore.PublishSaveCopy(copy, destination, files, () => ct.ThrowIfCancellationRequested());
            }
            Assert.Null(lost);
            Assert.Equal("saved native edit", File.ReadAllText(destination));
            Assert.Equal(kept, files.ReadOriginMark(destination));
            Assert.Equal(["saved.txt"], Directory.GetFileSystemEntries(folder).Select(entry => Path.GetFileName(entry)));
            Assert.Empty(Directory.GetFileSystemEntries(temp));
            Assert.Empty(Directory.GetDirectories(store.SessionDirectory(record), "commit-*"));
            Assert.Equal(record, Assert.Single(store.LoadAll()));
            Assert.Equal("saved native edit", File.ReadAllText(record.WorkingPath));
        }
        finally { TestCleanup.DeleteTree(fixture); }
    }

    // A native root holds its sessions and its admission gate; a removed creation leaves nothing else.
    private static void AssertOnlyGate(EditSessionStore store) =>
        Assert.Equal([EditStorageAdmission.GateName], Directory.GetFileSystemEntries(store.NativeRoot).Select(Path.GetFileName));

    private static void AssertNoUnfinished(EditSessionStore store) =>
        Assert.All(Directory.GetDirectories(store.NativeRoot), folder => Assert.True(File.Exists(Path.Combine(folder, "session.json")), folder));

    private sealed class HookedSource(IContentSource inner, Action<long> beforeRead) : IContentSource
    {
        public string DisplayName => inner.DisplayName; public long Length => inner.Length; public bool CanSeek => inner.CanSeek;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => inner.GetRevision();
        public int Read(long offset, Span<byte> buffer) { beforeRead(offset); return inner.Read(offset, buffer); }
        public void Dispose() { }
    }
}
