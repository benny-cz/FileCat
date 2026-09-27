using FileCat.Core.Content;
using System.Text.Json;

namespace FileCat.Platform.Windows.Tests;

public sealed class HexEditingTests
{
    [Fact]
    public void Save_as_creates_exact_new_file_without_touching_original()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "filecat-hexcopy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
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
            HexSaveAs.CreateNew(file, overlay, copyPath, TestContext.Current.CancellationToken);
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
    public void Recovery_rejects_unrelated_changes_and_preserves_journal()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "filecat-hexguard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "target.bin");
        string journals = Path.Combine(directory, "journals");
        File.WriteAllBytes(target, [1, 2, 3, 4]);
        try
        {
            using (var file = new ProtectedHexFile(target))
            using (var overlay = new HexPatchOverlay(file))
            {
                overlay.Write(1, [9]);
                Assert.Throws<IOException>(() => HexSaveJournal.Save(file, overlay, journals,
                    step => { if (step == 0) throw new IOException("interrupted"); }));
            }
            var journal = Assert.Single(HexSaveJournal.Pending(journals));
            var record = HexSaveJournal.Read(journal);
            File.WriteAllBytes(target, [1, 7, 3, 4]);
            Assert.Throws<IOException>(() => HexSaveJournal.Recover(record, rollback: false));
            Assert.True(File.Exists(journal));
            Assert.Equal(new byte[] { 1, 7, 3, 4 }, File.ReadAllBytes(target));
            var corrupt = File.ReadAllBytes(journal);
            corrupt[10] ^= 0xFF;
            File.WriteAllBytes(journal, corrupt);
            Assert.Throws<InvalidDataException>(() => HexSaveJournal.Read(journal));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
