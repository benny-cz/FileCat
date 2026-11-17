using FileCat.Core.Content;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

public sealed class HexEfsJournalTests
{
    private static void NativeFixture()
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Explicit EFS output qualification in the disposable normal-user Windows VM.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
    }

    [Fact]
    public void Efs_target_remains_readable_and_editable_in_memory_but_save_creates_no_journal()
    {
        NativeFixture(); string directory = Directory.CreateTempSubdirectory("filecat-hex-efs-").FullName;
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
    [InlineData(false)]
    [InlineData(true)]
    public void A_target_encrypted_after_a_clear_save_interruption_blocks_recovery_and_retains_the_journal(bool rollback)
    {
        NativeFixture(); string directory = Directory.CreateTempSubdirectory("filecat-hex-efs-recovery-").FullName;
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
