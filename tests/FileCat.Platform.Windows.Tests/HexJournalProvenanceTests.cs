using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Content;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

public sealed class HexJournalProvenanceTests
{
    [Fact]
    public void An_actual_Efs_journal_is_a_protected_input_even_when_its_original_sources_and_current_target_are_clear()
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1", "Disposable normal-user Windows EFS journal-input qualification.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
        string root = Directory.CreateTempSubdirectory("filecat-hex-journal-efs-input-").FullName;
        try
        {
            var original = Stopped(root); File.Encrypt(original.JournalPath);
            var record = HexSaveJournal.Read(original.JournalPath);
            Assert.All(record.Contributors, item => Assert.Equal(HexEfsOriginState.NotEfs, item.Protection));
            Assert.Contains(record.JournalInputs, item => item.Protection == HexEfsOriginState.Efs);
            byte[] journal = File.ReadAllBytes(record.JournalPath);
            Assert.NotNull(HexSaveJournal.Inspect(record).Blocker); Assert.Throws<IOException>(() => HexSaveJournal.Recover(record, false));
            Assert.Equal(journal, File.ReadAllBytes(record.JournalPath)); Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(record.TargetPath));
            File.Decrypt(record.JournalPath); HexSaveJournal.Recover(HexSaveJournal.Read(record.JournalPath), false);
            Assert.False(File.Exists(record.JournalPath)); Assert.Equal([1, 9, 3, 4], File.ReadAllBytes(record.TargetPath));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void New_journals_round_trip_the_frozen_actual_input_and_recover_normally()
    {
        string root = Directory.CreateTempSubdirectory("filecat-hex-journal-origin-").FullName;
        try
        {
            var record = Stopped(root);
            var contributor = Assert.Single(record.Contributors);
            Assert.Equal(record.TargetPath, contributor.InputPath); Assert.Equal(record.Identity.ToString(), contributor.Identity);
            Assert.Equal(HexEfsOriginState.NotEfs, contributor.Protection); Assert.Null(HexSaveJournal.Inspect(record).Blocker);
            Assert.Equal("FCHXJ003", Encoding.ASCII.GetString(File.ReadAllBytes(record.JournalPath), 4, 8));
            HexSaveJournal.Recover(record, false); Assert.False(File.Exists(record.JournalPath));
            Assert.Equal([1, 9, 3, 4], File.ReadAllBytes(record.TargetPath));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_journals_are_inspectable_but_unknown_origin_blocks_application_without_deleting_them(bool rollback)
    {
        string root = Directory.CreateTempSubdirectory("filecat-hex-journal-legacy-").FullName;
        try
        {
            var record = Stopped(root); WriteRecord(record, legacy: true);
            var legacy = HexSaveJournal.Read(record.JournalPath); Assert.Equal(HexEfsOriginState.Unknown, Assert.Single(legacy.Contributors).Protection);
            byte[] journal = File.ReadAllBytes(record.JournalPath), target = File.ReadAllBytes(record.TargetPath);
            Assert.Contains("unknown protection", HexSaveJournal.Inspect(legacy).Blocker);
            Assert.Contains("unknown protection", Assert.Throws<IOException>(() => HexSaveJournal.Recover(legacy, rollback)).Message);
            Assert.Equal(journal, File.ReadAllBytes(record.JournalPath)); Assert.Equal(target, File.ReadAllBytes(record.TargetPath));
            Assert.Contains(record.JournalPath, HexSaveJournal.Pending(Path.GetDirectoryName(record.JournalPath)!));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Efs_or_unknown_journal_contributors_block_recovery_even_when_the_target_is_clear(byte state)
    {
        string root = Directory.CreateTempSubdirectory("filecat-hex-journal-tainted-").FullName;
        try
        {
            var record = Stopped(root);
            WriteRecord(record, false, writer => { writer.Write(1); WriteName(writer, "actual input"); WriteName(writer, "input identity"); writer.Write(state); });
            var tainted = HexSaveJournal.Read(record.JournalPath);
            Assert.NotNull(HexSaveJournal.Inspect(tainted).Blocker); byte[] before = File.ReadAllBytes(record.JournalPath);
            Assert.Throws<IOException>(() => HexSaveJournal.Recover(tainted, false));
            Assert.Equal(before, File.ReadAllBytes(record.JournalPath)); Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(record.TargetPath));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("count")]
    [InlineData("state")]
    [InlineData("name")]
    [InlineData("utf8")]
    public void Malformed_but_checksummed_provenance_is_refused_before_recovery(string malformed)
    {
        string root = Directory.CreateTempSubdirectory("filecat-hex-journal-bad-origin-").FullName;
        try
        {
            var record = Stopped(root);
            WriteRecord(record, false, writer =>
            {
                writer.Write(malformed == "count" ? HexPatchOverlay.MaxContributors + 1 : 1);
                if (malformed == "name") { writer.Write(int.MaxValue); return; }
                if (malformed == "utf8") { writer.Write(1); writer.Write((byte)0xFF); return; }
                WriteName(writer, "input"); WriteName(writer, "identity"); writer.Write((byte)255);
            });
            Assert.Throws<InvalidDataException>(() => HexSaveJournal.Read(record.JournalPath));
            Assert.True(File.Exists(record.JournalPath)); Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(record.TargetPath));
        }
        finally { Directory.Delete(root, true); }
    }

    private static HexRecoveryRecord Stopped(string root)
    {
        string target = Path.Join(root, "target"), journals = Path.Join(root, "journals"); File.WriteAllBytes(target, [1, 2, 3, 4]);
        string journal;
        using (var file = new ProtectedHexFile(target))
        using (var overlay = new HexPatchOverlay(file))
        {
            overlay.Write(1, [9]);
            journal = Assert.Throws<HexSaveInterruptedException>(() => HexSaveJournal.Save(file, overlay, journals,
                step => { if (step == 0) throw new IOException("stop after the admitted journal"); })).JournalPath;
        }
        return HexSaveJournal.Read(journal);
    }

    private static void WriteRecord(HexRecoveryRecord record, bool legacy, Action<BinaryWriter>? provenance = null)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, true))
        {
            writer.Write(Encoding.ASCII.GetBytes(legacy ? "FCHXJ002" : "FCHXJ003")); WriteName(writer, record.TargetPath);
            writer.Write(record.Identity.VolumeSerial); writer.Write(Convert.FromHexString(record.Identity.FileId));
            writer.Write(record.Length); writer.Write(record.Ranges.Count);
            foreach (var range in record.Ranges) { writer.Write(range.Offset); writer.Write(range.Original.Length); writer.Write(range.Original); writer.Write(range.Replacement); }
            if (!legacy) provenance!(writer);
        }
        byte[] bytes = payload.ToArray();
        using var output = new BinaryWriter(File.Create(record.JournalPath)); output.Write(bytes.Length); output.Write(bytes); output.Write(SHA256.HashData(bytes));
    }

    private static void WriteName(BinaryWriter writer, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
}
