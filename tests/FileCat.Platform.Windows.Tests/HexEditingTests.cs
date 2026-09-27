using FileCat.Core.Content;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class HexEditingTests
{
    private static string NewDirectory(string prefix)
    {
        string directory = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void Save_as_creates_exact_new_file_without_touching_original()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = NewDirectory("filecat-hexcopy-");
        string originalPath = Path.Combine(directory, "original.bin");
        string copyPath = Path.Combine(directory, "copy.bin");
        string patchPath = Path.Combine(directory, "changes.json");
        var original = Enumerable.Range(0, 100_000).Select(i => (byte)i).ToArray();
        File.WriteAllBytes(originalPath, original);
        try
        {
            using var file = new ProtectedHexFile(originalPath);
            using var overlay = new HexPatchOverlay(file);
            overlay.Write(0, [10, 11]);
            overlay.Write(99_999, [12]);
            var result = HexSaveAs.CreateNew(file, overlay, copyPath, TestContext.Current.CancellationToken);
            Assert.Equal(original.Length, result.BytesWritten);
            Assert.Equal(HexOriginMark.None, result.OriginMark);
            var expected = original.ToArray();
            expected[0] = 10; expected[1] = 11; expected[^1] = 12;
            Assert.Equal(expected, File.ReadAllBytes(copyPath));
            var originalNow = new byte[original.Length];
            Assert.Equal(original.Length, file.Read(0, originalNow));
            Assert.Equal(original, originalNow);
            Assert.Equal(3, overlay.DirtyBytes);
            HexPatchExport.Export(file, overlay, patchPath);
            using var patch = JsonDocument.Parse(File.ReadAllText(patchPath));
            Assert.Equal(original.Length, patch.RootElement.GetProperty("sourceLength").GetInt64());
            Assert.Equal(2, patch.RootElement.GetProperty("ranges").GetArrayLength());
            Assert.Equal(3, overlay.DirtyBytes);
            Assert.Throws<IOException>(() => HexSaveAs.CreateNew(file, overlay, copyPath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Exported_patch_applies_only_where_every_original_byte_matches()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = NewDirectory("filecat-hexpatch-");
        string target = Path.Combine(directory, "target.bin");
        string other = Path.Combine(directory, "other.bin");
        string patchPath = Path.Combine(directory, "p.json");
        var bytes = Enumerable.Range(0, 3000).Select(i => (byte)(i * 7)).ToArray();
        File.WriteAllBytes(target, bytes);
        var different = bytes.ToArray();
        different[2000] ^= 0xFF;
        File.WriteAllBytes(other, different);
        try
        {
            using (var file = new ProtectedHexFile(target))
            using (var overlay = new HexPatchOverlay(file))
            {
                overlay.Write(5, [1, 2, 3]);
                overlay.Write(2000, [9]);
                HexPatchExport.Export(file, overlay, patchPath);
            }
            var patch = HexPatchExport.Read(patchPath);
            Assert.Equal(2, patch.Ranges.Count);
            using (var file = new ProtectedHexFile(target))
            using (var overlay = new HexPatchOverlay(file))
            {
                Assert.Equal((2, 0), HexPatchExport.Apply(patch, overlay));
                Assert.Equal(4, overlay.DirtyBytes);
                Assert.Equal((0, 2), HexPatchExport.Apply(patch, overlay)); // already applied
            }
            using (var file = new ProtectedHexFile(other))
            using (var overlay = new HexPatchOverlay(file))
            {
                Assert.Throws<InvalidDataException>(() => HexPatchExport.Apply(patch, overlay));
                Assert.Equal(0, overlay.DirtyBytes); // nothing staged when any range differs
            }
            File.WriteAllText(patchPath, "{\"format\":\"something else\"}");
            Assert.Throws<InvalidDataException>(() => HexPatchExport.Read(patchPath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Sparse_save_as_keeps_holes_and_the_download_mark()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = NewDirectory("filecat-hexsparse-");
        string source = Path.Combine(directory, "sparse.bin");
        string copy = Path.Combine(directory, "copy.bin");
        const long length = 1L << 30;
        try
        {
            using (var handle = File.OpenHandle(source, FileMode.CreateNew, FileAccess.ReadWrite))
            {
                bool ntfs = new DriveInfo(Path.GetPathRoot(directory)!).DriveFormat is "NTFS" or "ReFS";
                if (!SetSparse(handle)) { Assert.False(ntfs, "NTFS and ReFS support sparse files"); return; }
                RandomAccess.SetLength(handle, length);
                RandomAccess.Write(handle, "head"u8, 0);
                RandomAccess.Write(handle, "tail"u8, length - 4);
            }
            File.WriteAllText(source + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            HexSaveAsResult result;
            using (var file = new ProtectedHexFile(source))
            using (var overlay = new HexPatchOverlay(file))
            {
                Assert.True(file.IsSparse);
                overlay.Write(length / 2, [0x42]); // an edit inside a hole
                result = HexSaveAs.CreateNew(file, overlay, copy, TestContext.Current.CancellationToken);
            }
            Assert.True(result.Sparse);
            Assert.True(result.BytesWritten < length / 8, $"wrote {result.BytesWritten:N0} bytes");
            Assert.Equal(HexOriginMark.Copied, result.OriginMark);
            Assert.Contains("ZoneId=3", File.ReadAllText(copy + ":Zone.Identifier"));
            Assert.True((File.GetAttributes(copy) & FileAttributes.SparseFile) != 0);
            using var output = File.OpenHandle(copy);
            Assert.Equal(length, RandomAccess.GetLength(output));
            var probe = new byte[4];
            RandomAccess.Read(output, probe, 0);
            Assert.Equal("head"u8.ToArray(), probe);
            RandomAccess.Read(output, probe, length - 4);
            Assert.Equal("tail"u8.ToArray(), probe);
            RandomAccess.Read(output, probe.AsSpan(0, 1), length / 2);
            Assert.Equal(0x42, probe[0]);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Protected_open_refuses_empty_read_only_shared_and_linked_files_with_reasons()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = NewDirectory("filecat-hexopen-");
        try
        {
            string empty = Path.Combine(directory, "empty.bin");
            File.WriteAllBytes(empty, []);
            Assert.Contains("empty", Assert.Throws<NotSupportedException>(() => new ProtectedHexFile(empty)).Message);

            string readOnly = Path.Combine(directory, "ro.bin");
            File.WriteAllBytes(readOnly, [1]);
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            Assert.Contains("read-only", Assert.Throws<UnauthorizedAccessException>(() => new ProtectedHexFile(readOnly)).Message);
            File.SetAttributes(readOnly, FileAttributes.Normal);

            string busy = Path.Combine(directory, "busy.bin");
            File.WriteAllBytes(busy, [1]);
            using (new FileStream(busy, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                Assert.Contains("Another program", Assert.Throws<IOException>(() => new ProtectedHexFile(busy)).Message);

            string link = Path.Combine(directory, "link.bin");
            try { File.CreateSymbolicLink(link, busy); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // no symlink privilege
            Assert.Contains("link", Assert.Throws<NotSupportedException>(() => new ProtectedHexFile(link)).Message);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Interrupted_save_reports_its_state_and_recovers_through_the_open_editor_handle()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = NewDirectory("filecat-hexinterrupt-");
        string target = Path.Combine(directory, "target.bin");
        string journals = Path.Combine(directory, "journals");
        var original = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        File.WriteAllBytes(target, original);
        try
        {
            using var file = new ProtectedHexFile(target);
            using var overlay = new HexPatchOverlay(file);
            overlay.Write(1, [0xA1]);
            overlay.Write(10, [0xA2, 0xA3]);
            overlay.Write(40, [0xA4]);
            var failure = Assert.Throws<HexSaveInterruptedException>(() => HexSaveJournal.Save(file, overlay, journals,
                step => { if (step == 2) throw new IOException("device removed"); }));
            Assert.Contains("device removed", failure.Message);
            Assert.Equal(4, overlay.DirtyBytes); // the overlay keeps every edit
            var record = HexSaveJournal.Read(failure.JournalPath);
            Assert.Equal(2, record.WrittenRanges);
            var state = HexSaveJournal.Inspect(record); // works while the editor holds the protected handle
            Assert.Null(state.Blocker);
            Assert.Equal((1, 2, 0), (state.OriginalRanges, state.ReplacedRanges, state.MixedRanges));
            Assert.False(state.AlreadyFinished);

            HexSaveJournal.Recover(record, rollback: true, file);
            Assert.False(File.Exists(failure.JournalPath));
            var now = new byte[64];
            file.Read(0, now);
            Assert.Equal(original, now);
            // Rolled back, the edits are still unsaved and a new save succeeds.
            HexSaveJournal.Save(file, overlay, journals);
            Assert.Equal(0, overlay.DirtyBytes);
            file.Read(0, now);
            Assert.Equal(0xA4, now[40]);
            Assert.Empty(HexSaveJournal.Pending(journals));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Recovery_rejects_unrelated_changes_and_preserves_journal()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = NewDirectory("filecat-hexguard-");
        string target = Path.Combine(directory, "target.bin");
        string journals = Path.Combine(directory, "journals");
        File.WriteAllBytes(target, [1, 2, 3, 4]);
        try
        {
            using (var file = new ProtectedHexFile(target))
            using (var overlay = new HexPatchOverlay(file))
            {
                overlay.Write(1, [9]);
                Assert.Throws<HexSaveInterruptedException>(() => HexSaveJournal.Save(file, overlay, journals,
                    step => { if (step == 0) throw new IOException("interrupted"); }));
            }
            var journal = Assert.Single(HexSaveJournal.Pending(journals));
            var record = HexSaveJournal.Read(journal);
            File.WriteAllBytes(target, [1, 7, 3, 4]);
            Assert.NotNull(HexSaveJournal.Inspect(record).Blocker);
            Assert.Throws<IOException>(() => HexSaveJournal.Recover(record, rollback: false));
            Assert.True(File.Exists(journal));
            Assert.Equal(new byte[] { 1, 7, 3, 4 }, File.ReadAllBytes(target));

            File.Delete(target);
            Assert.Contains("no longer exists", HexSaveJournal.Inspect(record).Blocker);
            HexSaveJournal.Discard(record);
            Assert.Empty(HexSaveJournal.Pending(journals));

            File.WriteAllBytes(journal, [1, 2, 3]);
            Assert.Throws<InvalidDataException>(() => HexSaveJournal.Read(journal));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static unsafe bool SetSparse(SafeFileHandle handle) => DeviceIoControl(handle, 0x000900C4, null, 0, null, 0, out _, IntPtr.Zero);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, uint inputSize,
        void* output, uint outputSize, out uint returned, IntPtr overlapped);
}
