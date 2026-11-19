using FileCat.Core.Content;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

public sealed class HexEfsJournalTests
{
    [Fact]
    public void Efs_target_remains_readable_and_editable_in_memory_but_save_creates_no_journal()
    {
        WindowsStorageAbiTests.RequireNativeEfsOutputFixture();
        string directory = Directory.CreateTempSubdirectory("filecat-hex-efs-").FullName;
        string target = Path.Join(directory, "target.bin"), journals = Path.Join(directory, "journals");
        File.WriteAllBytes(target, [1, 2, 3, 4]); File.Encrypt(target);
        try
        {
            using var file = new ProtectedHexFile(target); using var overlay = new HexPatchOverlay(file);
            var before = new byte[4]; Assert.Equal(4, file.Read(0, before)); Assert.Equal([1, 2, 3, 4], before);
            overlay.Write(1, [9]); Assert.True(overlay.Undo()); Assert.True(overlay.Redo());
            Assert.Contains("EFS files cannot be saved in place", Assert.Throws<IOException>(() => HexSaveJournal.Save(file, overlay, journals)).Message);
            Assert.False(Directory.Exists(journals)); Assert.Equal(1, overlay.DirtyBytes);
            Assert.Equal(4, file.Read(0, before)); Assert.Equal([1, 2, 3, 4], before);
            Assert.True(overlay.Undo()); Assert.True(overlay.Redo());
        }
        finally { File.Decrypt(target); Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Efs_applied_after_a_journal_flush_stops_save_and_keeps_the_journal(int writtenRanges)
    {
        WindowsStorageAbiTests.RequireNativeEfsOutputFixture();
        string directory = Directory.CreateTempSubdirectory("filecat-hex-efs-late-save-").FullName;
        string target = Path.Join(directory, "target.bin"), journals = Path.Join(directory, "journals");
        byte[] original = [1, 2, 3, 4];
        File.WriteAllBytes(target, original);
        try
        {
            string journal;
            using (var file = new ProtectedHexFile(target))
            using (var overlay = new HexPatchOverlay(file))
            {
                overlay.Write(1, [9]);
                var error = Assert.Throws<HexSaveInterruptedException>(() => HexSaveJournal.Save(file, overlay, journals,
                    step => { if (step == writtenRanges) File.Encrypt(target); }));
                Assert.Contains("EFS files cannot be saved in place", error.Message);
                Assert.Equal(1, overlay.DirtyBytes);
                journal = error.JournalPath;
            }

            Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(writtenRanges == 0 ? original : [1, 9, 3, 4], File.ReadAllBytes(target));
            var record = HexSaveJournal.Read(journal);
            Assert.Equal(writtenRanges, record.WrittenRanges);
            Assert.Contains("EFS", HexSaveJournal.Inspect(record).Blocker);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Efs_applied_during_recovery_stops_the_next_write_and_keeps_the_journal(bool rollback)
    {
        WindowsStorageAbiTests.RequireNativeEfsOutputFixture();
        string directory = Directory.CreateTempSubdirectory("filecat-hex-efs-late-recovery-").FullName;
        string target = Path.Join(directory, "target.bin"), journals = Path.Join(directory, "journals");
        File.WriteAllBytes(target, [1, 2, 3, 4]);
        try
        {
            string journal;
            using (var file = new ProtectedHexFile(target))
            using (var overlay = new HexPatchOverlay(file))
            {
                overlay.Write(1, [9]);
                overlay.Write(3, [8]);
                int lastWrite = rollback ? 2 : 0;
                journal = Assert.Throws<HexSaveInterruptedException>(() => HexSaveJournal.Save(file, overlay, journals,
                    step => { if (step == lastWrite) throw new IOException("test interruption"); })).JournalPath;
            }

            byte[] journalBefore = File.ReadAllBytes(journal);
            var record = HexSaveJournal.Read(journal);
            Assert.Contains("EFS", Assert.Throws<IOException>(() => HexSaveJournal.Recover(record, rollback,
                afterDurableStep: step => { if (step == 1) File.Encrypt(target); })).Message);
            Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(rollback ? new byte[] { 1, 2, 3, 8 } : [1, 9, 3, 4], File.ReadAllBytes(target));
            Assert.Equal(journalBefore, File.ReadAllBytes(journal));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_target_encrypted_after_a_clear_save_interruption_blocks_recovery_and_retains_the_journal(bool rollback)
    {
        WindowsStorageAbiTests.RequireNativeEfsOutputFixture();
        string directory = Directory.CreateTempSubdirectory("filecat-hex-efs-recovery-").FullName;
        string target = Path.Join(directory, "target.bin"), journals = Path.Join(directory, "journals");
        File.WriteAllBytes(target, [1, 2, 3, 4]); string journal;
        try
        {
            using (var file = new ProtectedHexFile(target))
            using (var overlay = new HexPatchOverlay(file))
            {
                overlay.Write(1, [9]); overlay.Write(3, [8]);
                journal = Assert.Throws<HexSaveInterruptedException>(() => HexSaveJournal.Save(file, overlay, journals,
                    step => { if (step == 1) throw new IOException("test interruption"); })).JournalPath;
            }
            byte[] journalBefore = File.ReadAllBytes(journal), targetBefore = File.ReadAllBytes(target);
            var record = HexSaveJournal.Read(journal); Assert.Null(HexSaveJournal.Inspect(record).Blocker);
            File.Encrypt(target);
            Assert.Contains("EFS", HexSaveJournal.Inspect(record).Blocker);
            Assert.Contains("EFS", Assert.Throws<IOException>(() => HexSaveJournal.Recover(record, rollback)).Message);
            using (var file = new ProtectedHexFile(target))
            {
                Assert.Contains("EFS", HexSaveJournal.Inspect(record, file).Blocker);
                Assert.Contains("EFS", Assert.Throws<IOException>(() => HexSaveJournal.Recover(record, rollback, file)).Message);
            }
            Assert.Equal(journalBefore, File.ReadAllBytes(journal)); Assert.Equal(targetBefore, File.ReadAllBytes(target));
            Assert.Contains(journal, HexSaveJournal.Pending(journals));
            // Explicit decryption restores the existing clear-target recovery route; no inverse is attempted by FileCat.
            File.Decrypt(target); HexSaveJournal.Recover(record, rollback);
            Assert.False(File.Exists(journal));
            Assert.Equal(rollback ? new byte[] { 1, 2, 3, 4 } : [1, 9, 3, 8], File.ReadAllBytes(target));
        }
        finally { File.Decrypt(target); Directory.Delete(directory, true); }
    }
}
