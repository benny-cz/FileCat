using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Tests;

/// <summary>
/// Remaining-work ledger rows 4 and 8: a native F4 copy whose process is killed while it copies ("copy"), before its
/// creation marker exists ("folders"), right after its folder is made, before it holds it ("session"), while it removes
/// what a failed copy made ("abandon"), or while it publishes its record under the storage gate ("publish"). While it runs,
/// another FileCat process lists its folder as in use, never as interrupted, cannot remove it, and charges it at the
/// length its marker says it was admitted with. Once the process is
/// gone nothing removes it: a fresh store lists it as an interrupted edit, with the partial copy kept for review, new edits
/// are made beside it, and its review action removes exactly the unfinished copy.
/// </summary>
internal static class ArchiveEditCreationCrashCases
{
    internal static async Task RunAsync(Func<string, bool, CancellationToken, EditDirectoryLease> nativeOpen, string platformAssembly, string point)
    {
        var ct = TestContext.Current.CancellationToken;
        string parent = ArchiveEditCreationCases.FixtureParent();
        ArchiveEditCreationCases.RequireListedRoute(parent);
        string fixture = Directory.CreateDirectory(Path.Combine(parent, "filecat-create-crash-" + Guid.NewGuid().ToString("N"))).FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fixture, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            string root = Path.Combine(fixture, "native"), marker = Path.Combine(fixture, "paused.txt");
            var checkout = new DirectoryInfo(AppContext.BaseDirectory);
            while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "FileCat.slnx"))) checkout = checkout.Parent;
            Assert.NotNull(checkout);
            string config = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            string harness = Path.Combine(checkout.FullName, "tests", "FileCat.CoreCrashHarness", "bin", config, "net10.0", "FileCat.CoreCrashHarness.dll");
            string runtimeRoot = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
            var start = new ProcessStartInfo(Path.Combine(runtimeRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { harness, "--edit-creation-crash", root, platformAssembly, marker, point }) start.ArgumentList.Add(arg);
            using var child = Process.Start(start)!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(120));
            string id;
            EditSessionRecord? beside = null;
            try
            {
                while (!File.Exists(marker) && !child.HasExited) { timeout.Token.ThrowIfCancellationRequested(); await Task.Delay(25, timeout.Token); }
                if (!File.Exists(marker)) Assert.Fail("The child exited before its copy paused: " + await child.StandardError.ReadToEndAsync(timeout.Token));
                // While the other process still copies, its edit is listed as being copied there, never as interrupted,
                // and it cannot be removed from here, even through a row that claims it was interrupted.
                var observer = new EditSessionStore(root, new PortableFileOperations(), nativeOpen);
                var copying = Assert.Single(observer.LoadAll());
                id = copying.Id;
                Assert.False(copying.IsSupported);
                // Held (its lock or open marker): in use. Before it holds anything, the gate it holds leaves it undecided.
                if (point is "folders" or "session")
                    Assert.Contains(copying.UnsupportedReason, new[] { EditSessionStore.CopyingElsewhereReason, EditSessionStore.UncheckedReason });
                else Assert.Equal(EditSessionStore.CopyingElsewhereReason, copying.UnsupportedReason);
                Assert.Throws<NotSupportedException>(() => observer.RemoveInterruptedCopy(copying, ct));
                var claim = copying with { UnsupportedReason = EditSessionStore.InterruptedCreationReason };
                // A removal through a row that claims the copy was interrupted is refused, or, before the marker, waits for
                // the storage gate the creation still holds (given up here after 2 s): nothing is removed either way.
                using (var brief = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    brief.CancelAfter(TimeSpan.FromSeconds(2));
                    EditDiscardResult? claimed = null;
                    try { claimed = observer.RemoveInterruptedCopy(claim, brief.Token); }
                    catch (OperationCanceledException) when (point is "folders" or "session" && !ct.IsCancellationRequested) { }
                    if (claimed is not null) { Assert.False(claimed.Complete); Assert.False(claimed.MayHaveRemovedFiles); }
                }
                Assert.True(Directory.Exists(Path.Combine(root, id)));
                Assert.Equal(point is "copy" or "abandon" or "publish", File.Exists(Path.Combine(root, id, "creation.json")));
                Assert.Equal(point != "session", Directory.Exists(Path.Combine(root, id, "files")));
                if (point == "copy") beside = ChargedAsAdmitted(root, nativeOpen, observer, ct);
            }
            finally
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
            }
            Assert.NotEqual(0, child.ExitCode);

            var store = new EditSessionStore(root, new PortableFileOperations(), nativeOpen);
            // The killed process's objects are released as it ends; on Windows another program (a scanner reading the
            // files it just closed) can still hold one for a moment, which reads as a copy still being made.
            var clock = Stopwatch.StartNew();
            EditSessionRecord Listed() => Assert.Single(store.LoadAll(), row => row.Id == id);
            var interrupted = Listed();
            // Its hold or the storage gate may still be another program's for a moment after the kill (on Windows a scanner
            // reading the files it just closed); the row then says in use or not checked until that passes.
            while (interrupted.UnsupportedReason is { } waiting && (waiting == EditSessionStore.CopyingElsewhereReason ||
                waiting == EditSessionStore.UncheckedReason) && clock.Elapsed < TimeSpan.FromSeconds(30))
            { await Task.Delay(100, ct); interrupted = Listed(); }
            TestContext.Current.TestOutputHelper?.WriteLine($"Listed as interrupted {clock.ElapsedMilliseconds} ms after the kill.");
            Assert.False(interrupted.IsSupported);
            Assert.Equal(EditSessionStore.InterruptedCreationReason, interrupted.UnsupportedReason);
            string folder = Path.Combine(root, id);
            Assert.False(File.Exists(Path.Combine(folder, "session.json")));
            if (point is "copy" or "abandon" or "publish")
            {
                Assert.Equal(folder, store.SessionDirectory(interrupted));
                Assert.True(File.Exists(Path.Combine(folder, "creation.json")));
                // The partial copy is the working file the marker names: the bytes copied before the kill, exactly.
                Assert.StartsWith(Path.Combine(folder, "files") + Path.DirectorySeparatorChar, interrupted.WorkingPath, StringComparison.Ordinal);
                byte[] partial = File.ReadAllBytes(interrupted.WorkingPath);
                byte[] pattern = Pattern();
                // Its publication had all the bytes; otherwise the bytes copied before the end.
                if (point == "publish") Assert.Equal(pattern.Length, partial.Length); else Assert.InRange(partial.Length, 1, pattern.Length - 1);
                Assert.True(partial.AsSpan().SequenceEqual(pattern.AsSpan(0, partial.Length)), "The kept partial copy differs from the source's first bytes.");
            }
            else if (point == "folders")
            {
                // Ended before its marker: its folders, empty.
                Assert.Equal("files", Path.GetFileName(Assert.Single(Directory.GetFileSystemEntries(folder))));
                Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(folder, "files")));
            }
            else Assert.Empty(Directory.GetFileSystemEntries(folder)); // Ended right after its folder was made.

            // A new edit is made beside it, and the interrupted one stays listed for review.
            using var next = new MemoryContentSource("next.txt", "next complete copy"u8.ToArray());
            var made = store.CreateRemote("owned", "owned", "/next.txt", next, next.GetRevision()!.Value, null, ct);
            var rows = store.LoadAll();
            Assert.Equal(beside is null ? 2 : 3, rows.Count);
            Assert.Contains(rows, row => row.Id == made.Id && row.IsSupported);
            if (beside is not null) Assert.Contains(rows, row => row.Id == beside.Id && row.IsSupported);
            Assert.Contains(rows, row => row.Id == id && row.UnsupportedReason == EditSessionStore.InterruptedCreationReason);

            // Its review action removes exactly the unfinished copy; the other edits stay.
            var removed = store.RemoveInterruptedCopy(interrupted, ct);
            Assert.True(removed.Complete, removed.Issue);
            Assert.False(Directory.Exists(folder));
            Assert.Equal(new[] { made.Id, beside?.Id }.OfType<string>().Order(StringComparer.Ordinal),
                store.LoadAll().Select(row => row.Id).Order(StringComparer.Ordinal));
        }
        finally { TestCleanup.DeleteTree(fixture); }
    }

    // While the other process copies, this process charges its copy at the length its marker says it was admitted with
    // (not at the most one copy can be), counts as pending only what that copy has yet to write, and still admits a copy of
    // its own beside it exactly when that charge leaves room.
    private static EditSessionRecord ChargedAsAdmitted(string root, Func<string, bool, CancellationToken, EditDirectoryLease> nativeOpen,
        EditSessionStore observer, CancellationToken ct)
    {
        // The other copy has written its first part (it waits before its second read): free space already counts that.
        long written = new FileInfo(Assert.Single(Directory.GetFiles(Path.Combine(Assert.Single(Directory.GetDirectories(root)), "files")))).Length;
        Assert.InRange(written, 1, Bytes - 1);
        var usage = observer.ReviewNativeStorageUsage(ct);
        TestContext.Current.TestOutputHelper?.WriteLine($"Usage beside the running copy ({written} bytes written): {usage}");
        Assert.Equal(1, usage.Sessions);
        Assert.Equal(Bytes - written, usage.PendingBytes);
        // The running copy is charged apart from the stored bytes, as its marker records it was admitted (its payload and
        // the two records), so a copy of this process is admitted beside that charge, and exactly when it leaves room.
        Assert.Equal(1, usage.RunningSessions);
        Assert.InRange(usage.RunningChargedBytes, Bytes + 2L * EditSessionStore.MaxRecordBytes,
            EditSessionStore.MaxMemberBytes + 2L * EditSessionStore.MaxRecordBytes + 5 * EditStorageAdmission.MetadataPerEntry);
        byte[] bytes = "beside the running copy"u8.ToArray();
        long need = bytes.Length + 2L * EditSessionStore.MaxRecordBytes + 5 * EditStorageAdmission.MetadataPerEntry;
        long held = usage.ChargedBytes + usage.RunningChargedBytes;
        EditSessionStore Limited(long limit) => new(root, new PortableFileOperations(), nativeOpen, null, new EditStorageReviewLimits(Bytes: limit));
        using (var refused = new MemoryContentSource("refused.txt", bytes))
        {
            var error = Assert.Throws<IOException>(() => Limited(held + need - 1)
                .CreateRemote("owned", "owned", "/refused.txt", refused, refused.GetRevision()!.Value, null, ct));
            Assert.Contains("no room", error.Message, StringComparison.Ordinal);
        }
        Assert.Equal(EditSessionStore.CopyingElsewhereReason, Assert.Single(observer.LoadAll()).UnsupportedReason);
        using var fits = new MemoryContentSource("fits.txt", bytes);
        var made = Limited(held + need).CreateRemote("owned", "owned", "/fits.txt", fits, fits.GetRevision()!.Value, null, ct);
        Assert.True(made.IsSupported);
        Assert.Equal(2, observer.LoadAll().Count);
        return made;
    }

    private const int Bytes = 3 * 1024 * 1024;

    // The harness's source bytes (EditCreationCrashHarness.Pattern).
    private static byte[] Pattern() => Enumerable.Range(0, Bytes).Select(i => (byte)(i * 7 + 3)).ToArray();
}
