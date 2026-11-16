using System.Reflection;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

// Remaining-work ledger rows 4 and 8: a native F4 copy in a process that is killed while it copies ("copy": its marker
// published and its first megabyte written), before its creation marker exists ("folders": its folders made and held,
// under the storage gate), right after its folder is made ("session": before it holds that folder), or while it removes
// what its failed copy made ("abandon": its second read failed, its cleanup about to read the partial copy), or while it
// publishes its record ("publish": the copy complete, its record staged, the storage gate held). The parent
// observes the running creation from its own process, then kills this child at the pause and checks that the interrupted
// copy is kept and listed for review.
internal static class EditCreationCrashHarness
{
    internal const int Bytes = 3 * 1024 * 1024;

    internal static int Run(string nativeRoot, string platformAssembly, string marker, string point)
    {
        if (!Path.IsPathFullyQualified(nativeRoot) || !Path.IsPathFullyQualified(marker) || point is not ("copy" or "folders" or "session" or "abandon" or "publish")) return 64;
        Func<string, bool, CancellationToken, EditDirectoryLease> nativeOpen = UnixEditDirectoryLease.OpenRoot;
        if (OperatingSystem.IsWindows())
        {
            if (!Path.IsPathFullyQualified(platformAssembly)) return 64;
            var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(platformAssembly);
            var native = assembly.GetType("FileCat.Platform.Windows.Archives.WindowsEditDirectoryLease", true)!
                .GetMethod("OpenRoot", BindingFlags.Static | BindingFlags.NonPublic)!;
            nativeOpen = (path, priv, ct) => (EditDirectoryLease)native.Invoke(null, [path, priv, ct])!;
        }
        var open = nativeOpen;
        if (point != "copy") open = (path, priv, ct) => new PausingLease(nativeOpen(path, priv, ct), marker, point);
        var store = new EditSessionStore(nativeRoot, new PortableFileOperations(), open);
        using var content = new MemoryContentSource("crash.txt", Pattern());
        using var paused = new PausingSource(content, point == "copy" ? marker : null, point == "abandon");
        store.CreateRemote("owned", "owned", "/crash.txt", paused, paused.GetRevision()!.Value, null);
        return 65; // A normal return means the process-kill boundary was missed.
    }

    internal static byte[] Pattern() => Enumerable.Range(0, Bytes).Select(i => (byte)(i * 7 + 3)).ToArray();

    private static void Pause(string marker)
    {
        // The parent waits for this pathname; publish it only once its content is complete.
        string pending = marker + ".tmp";
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { stream.Write("paused"u8); stream.Flush(true); }
        File.Move(pending, marker);
        Console.WriteLine("PAUSED"); Console.Out.Flush();
        Thread.Sleep(Timeout.Infinite);
    }

    // Pauses (or fails) on its second read: the first megabyte is then in the private working copy.
    private sealed class PausingSource(IContentSource inner, string? marker, bool fail) : IContentSource
    {
        private int reads;
        public string DisplayName => inner.DisplayName; public long Length => inner.Length; public bool CanSeek => inner.CanSeek;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => inner.GetRevision();
        public int Read(long offset, Span<byte> buffer)
        {
            if (++reads == 2 && marker is not null) Pause(marker);
            if (reads == 2 && fail) throw new IOException("Owned source failed during the copy.");
            return inner.Read(offset, buffer);
        }
        public void Dispose() { }
    }

    // The native store as it is, except that it pauses once the new edit's working folder exists ("folders": its session
    // folder made and held, its creation marker not yet), once its session folder exists ("session": not held yet), when
    // the failed copy's cleanup first opens a file to remove ("abandon"), or before its record's name is published ("publish").
    private sealed class PausingLease(EditDirectoryLease owner, string marker, string point) : EditDirectoryLease
    {
        internal override string Path => owner.Path; internal override string Identity => owner.Identity; internal override SafeFileHandle Handle => owner.Handle;
        private EditDirectoryLease Wrap(EditDirectoryLease child) => new PausingLease(child, marker, point);
        internal override EditDirectoryLease CreateDirectory(string leaf, CancellationToken ct, bool removable = false)
        {
            var made = Wrap(owner.CreateDirectory(leaf, ct, removable));
            if (point == "folders" ? leaf == "files" : point == "session" && Guid.TryParseExact(leaf, "N", out _)) Pause(marker);
            return made;
        }
        internal override EditDirectoryLease OpenDirectory(string leaf, string? id, CancellationToken ct) => Wrap(owner.OpenDirectory(leaf, id, ct));
        internal override EditDirectoryLease OpenStorageDirectory(string leaf, CancellationToken ct) => Wrap(owner.OpenStorageDirectory(leaf, ct));
        internal override bool CanDiscardReviewedSession => owner.CanDiscardReviewedSession;
        internal override EditDirectoryLease OpenDiscardDirectory(string leaf, string? id, CancellationToken ct) => Wrap(owner.OpenDiscardDirectory(leaf, id, ct));
        internal override FileStream OpenDiscardFile(string leaf, string? id, CancellationToken ct)
        {
            if (point == "abandon") Pause(marker);
            return owner.OpenDiscardFile(leaf, id, ct);
        }
        internal override FileStream CreateFile(string leaf, CancellationToken ct) => owner.CreateFile(leaf, ct);
        internal override FileStream OpenFile(string leaf, string? id, CancellationToken ct) => owner.OpenFile(leaf, id, ct);
        internal override FileStream OpenSharedRecord(string leaf, CancellationToken ct) => owner.OpenSharedRecord(leaf, ct);
        internal override FileStream OpenWorkingFile(string leaf, CancellationToken ct) => owner.OpenWorkingFile(leaf, ct);
        internal override FileStream OpenWorkingFileForReview(string leaf, CancellationToken ct) => owner.OpenWorkingFileForReview(leaf, ct);
        internal override ContentRevision ReviewWorkingFile(FileStream file, string leaf, CancellationToken ct) => owner.ReviewWorkingFile(file, leaf, ct);
        internal override ArchiveOriginSnapshot ReadOrigin(FileStream file, string leaf, CancellationToken ct) => owner.ReadOrigin(file, leaf, ct);
        internal override void CopyOrigin(FileStream file, string leaf, ArchiveOriginSnapshot origin, CancellationToken ct) => owner.CopyOrigin(file, leaf, origin, ct);
        internal override void ReviewRemoval(FileStream file, string leaf, ArchiveFileObservation expected, CancellationToken ct, bool working = false) => owner.ReviewRemoval(file, leaf, expected, ct, working);
        internal override void DeleteSnapshot(FileStream file, string leaf, ArchiveFileObservation expected, CancellationToken ct, bool working = false) => owner.DeleteSnapshot(file, leaf, expected, ct, working);
        internal override bool CanRemoveOwnedDirectories => owner.CanRemoveOwnedDirectories;
        internal override void RequireRemovable(FileStream file, string leaf) => owner.RequireRemovable(file, leaf);
        internal override void RemoveEmpty(CancellationToken ct) => owner.RemoveEmpty(ct);
        internal override IReadOnlyList<string> Names(CancellationToken ct) => owner.Names(ct);
        internal override IReadOnlyList<string> Names(CancellationToken ct, int max) => owner.Names(ct, max);
        internal override EditStoredFileSize MeasureStoredFile(string leaf, CancellationToken ct) => owner.MeasureStoredFile(leaf, ct);
        internal override EditStoredDirectorySize MeasureStoredDirectory(CancellationToken ct) => owner.MeasureStoredDirectory(ct);
        internal override FileStream OpenStorageGate(CancellationToken ct) => owner.OpenStorageGate(ct);
        internal override bool TryLockStorageGate(FileStream file, CancellationToken ct) => owner.TryLockStorageGate(file, ct);
        internal override FileSystemCapacitySnapshot? StorageCapacity(CancellationToken ct) => owner.StorageCapacity(ct);
        internal override string Identify(SafeFileHandle file) => owner.Identify(file);
        internal override string IdentifyWorking(SafeFileHandle file) => owner.IdentifyWorking(file);
        internal override void PublishNewFile(FileStream file, string source, string target, EditFileVersion expected, CancellationToken ct)
        {
            if (point == "publish" && target == "session.json") Pause(marker);
            owner.PublishNewFile(file, source, target, expected, ct);
        }
        protected override void ReviewCore(CancellationToken ct) => owner.Review(ct);
        protected override void CloseCore() => owner.Dispose();
    }
}
