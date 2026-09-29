using System.Buffers.Binary;
using System.Text;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

/// <summary>D-56: MFT records, change-journal records, timestamp checks, and permissions, read the same on every system.</summary>
public sealed class FileRecordTests
{
    private static readonly long Created = new DateTime(2024, 3, 1, 10, 0, 0, DateTimeKind.Utc).ToFileTimeUtc() + 1234567;

    /// <summary>A 1 KiB MFT record as NTFS writes it to disk: $STANDARD_INFORMATION, $FILE_NAME, a $DATA with three runs, and fixups.</summary>
    private static byte[] Record(bool applyFixups = true, byte[]? slack = null)
    {
        var r = new byte[1024];
        "FILE"u8.CopyTo(r);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(4), 48);  // update sequence array
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(6), 3);   // the number and two sectors' saved bytes
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(8), 0x1234);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(16), 7);  // sequence
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(18), 1);  // links
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(20), 56); // first attribute
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(22), 1);  // in use
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(28), 1024);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(40), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(44), 42); // its own number
        int at = 56;

        // $STANDARD_INFORMATION, resident, 72 bytes.
        var si = new byte[72];
        BinaryPrimitives.WriteInt64LittleEndian(si, Created);
        BinaryPrimitives.WriteInt64LittleEndian(si.AsSpan(8), Created + 10_000_000);
        BinaryPrimitives.WriteInt64LittleEndian(si.AsSpan(16), Created + 20_000_000);
        BinaryPrimitives.WriteInt64LittleEndian(si.AsSpan(24), Created + 30_000_000);
        BinaryPrimitives.WriteUInt32LittleEndian(si.AsSpan(32), 0x20);
        BinaryPrimitives.WriteUInt32LittleEndian(si.AsSpan(52), 0x100);
        BinaryPrimitives.WriteInt64LittleEndian(si.AsSpan(64), 0x999);
        at = Resident(r, at, 0x10, 0, si);

        // $FILE_NAME "test.txt" (Win32) in the root folder.
        string name = "test.txt";
        var fn = new byte[66 + name.Length * 2];
        BinaryPrimitives.WriteUInt64LittleEndian(fn, 5UL | 5UL << 48);
        for (int i = 0; i < 4; i++) BinaryPrimitives.WriteInt64LittleEndian(fn.AsSpan(8 + i * 8), Created);
        BinaryPrimitives.WriteInt64LittleEndian(fn.AsSpan(48), 100);
        BinaryPrimitives.WriteUInt32LittleEndian(fn.AsSpan(56), 0x20);
        fn[64] = (byte)name.Length;
        fn[65] = 1;
        Encoding.Unicode.GetBytes(name).CopyTo(fn, 66);
        at = Resident(r, at, 0x30, 2, fn, indexed: true);

        // $DATA, non-resident: 16 clusters at 4096, 5 at 4080, then 3 sparse.
        byte[] runs = [0x21, 0x10, 0x00, 0x10, 0x11, 0x05, 0xF0, 0x01, 0x03, 0x00];
        int length = (64 + runs.Length + 7) & ~7;
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(at), 0x80);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(at + 4), (uint)length);
        r[at + 8] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(at + 14), 3);
        BinaryPrimitives.WriteInt64LittleEndian(r.AsSpan(at + 24), 23);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(at + 32), 64);
        BinaryPrimitives.WriteInt64LittleEndian(r.AsSpan(at + 40), 24 * 4096);
        BinaryPrimitives.WriteInt64LittleEndian(r.AsSpan(at + 48), 90_000);
        BinaryPrimitives.WriteInt64LittleEndian(r.AsSpan(at + 56), 90_000);
        runs.CopyTo(r, at + 64);
        at += length;
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(at), 0xFFFF_FFFF);
        at += 8;
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(24), (uint)at);
        slack?.CopyTo(r, at + 16);

        if (applyFixups)
        {
            // What the disk holds: the last two bytes of each 512 replaced by the number, their values kept in the array.
            r[510] = 0xAB;
            r[511] = 0xCD;
            BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(48), 0x0001);
            r[50] = r[510];
            r[51] = r[511];
            r[52] = r[1022];
            r[53] = r[1023];
            BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(510), 0x0001);
            BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(1022), 0x0001);
        }
        return r;
    }

    private static int Resident(byte[] r, int at, uint type, ushort id, byte[] value, bool indexed = false)
    {
        int length = (24 + value.Length + 7) & ~7;
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(at), type);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(at + 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(at + 14), id);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(at + 16), (uint)value.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(at + 20), 24);
        r[at + 22] = (byte)(indexed ? 1 : 0);
        value.CopyTo(r, at + 24);
        return at + length;
    }

    [Fact]
    public void An_MFT_record_reads_as_its_header_times_names_and_runs()
    {
        var record = NtfsRecord.Parse(Record());
        Assert.Empty(record.Problems);
        Assert.Equal(42, record.Number);
        Assert.Equal(7, record.Sequence);
        Assert.True(record.InUse);
        Assert.False(record.IsDirectory);
        Assert.Equal(0x1234UL, record.Lsn);
        Assert.Equal(Created, record.Standard!.Created);
        Assert.Equal(Created + 20_000_000, record.Standard.Changed);
        Assert.Equal(0x100u, record.Standard.SecurityId);
        Assert.Equal(0x999, record.Standard.Usn);
        var name = Assert.Single(record.Names);
        Assert.Equal("test.txt", name.Name);
        Assert.Equal((5L, (ushort)5), (name.ParentRecord, name.ParentSequence));
        Assert.Equal("Win32", name.NamespaceText);
        var data = Assert.Single(record.Attributes, a => a.Type == NtfsAttributeTypes.Data);
        Assert.False(data.Resident);
        Assert.Equal(90_000, data.Size);
        Assert.Equal([new NtfsRun(0, 4096, 16), new NtfsRun(16, 4080, 5), new NtfsRun(21, -1, 3)], data.Runs);
        Assert.True(Assert.Single(record.Attributes, a => a.Type == NtfsAttributeTypes.FileName).Indexed);
    }

    [Fact]
    public void Fixups_catch_a_torn_record_and_are_left_alone_in_one_NTFS_hands_out()
    {
        var torn = Record();
        torn[1022] = 0x77; // the second sector was written by another write
        Assert.Contains(NtfsRecord.Parse(torn).Problems, p => p.Contains("torn write", StringComparison.Ordinal));

        // A record from NTFS's memory has its real bytes in place and whatever the array held when last written.
        var live = Record(applyFixups: false);
        live[48] = 9;
        live[50] = 0x55;
        var parsed = NtfsRecord.Parse(live, live: true);
        Assert.Empty(parsed.Problems);
        Assert.Equal("test.txt", Assert.Single(parsed.Names).Name);
    }

    [Fact]
    public void Record_slack_keeps_what_an_earlier_version_left()
    {
        var record = NtfsRecord.Parse(Record(slack: Encoding.ASCII.GetBytes("old-name.docx")));
        Assert.Contains("old-name.docx", Encoding.ASCII.GetString(record.Slack), StringComparison.Ordinal);
        Assert.Contains(RecordText.HexLines(record.Slack), l => l.Contains("old-name", StringComparison.Ordinal));
    }

    [Fact]
    public void Damaged_records_are_described_not_thrown()
    {
        Assert.Contains("damaged", Assert.Single(NtfsRecord.Parse("BAAD"u8.ToArray().Concat(new byte[1020]).ToArray()).Problems), StringComparison.Ordinal);
        Assert.NotEmpty(NtfsRecord.Parse(new byte[20]).Problems);
        var overflowing = Record();
        BinaryPrimitives.WriteUInt32LittleEndian(overflowing.AsSpan(56 + 4), 5000); // $STANDARD_INFORMATION claims 5,000 bytes
        var parsed = NtfsRecord.Parse(overflowing);
        Assert.Null(parsed.Standard);
        Assert.Contains(parsed.Problems, p => p.Contains("runs past", StringComparison.Ordinal));
    }

    [Fact]
    public void Journal_records_of_both_versions_read_with_their_reasons_in_words()
    {
        var v2 = new byte[64 + 16];
        BinaryPrimitives.WriteUInt32LittleEndian(v2, (uint)v2.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(v2.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt64LittleEndian(v2.AsSpan(8), 42UL | 7UL << 48);
        BinaryPrimitives.WriteUInt64LittleEndian(v2.AsSpan(16), 5UL | 5UL << 48);
        BinaryPrimitives.WriteInt64LittleEndian(v2.AsSpan(24), 1000);
        BinaryPrimitives.WriteInt64LittleEndian(v2.AsSpan(32), Created);
        BinaryPrimitives.WriteUInt32LittleEndian(v2.AsSpan(40), UsnRecord.FileCreate | 0x2 | UsnRecord.Close);
        BinaryPrimitives.WriteUInt16LittleEndian(v2.AsSpan(56), 10); // "a.txt": the record pads after it
        BinaryPrimitives.WriteUInt16LittleEndian(v2.AsSpan(58), 60);
        Encoding.Unicode.GetBytes("a.txt\0\0\0").CopyTo(v2, 60);
        var v3 = new byte[80 + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(v3, (uint)v3.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(v3.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt128LittleEndian(v3.AsSpan(8), (UInt128)1 << 100);
        BinaryPrimitives.WriteInt64LittleEndian(v3.AsSpan(40), 2000);
        BinaryPrimitives.WriteUInt32LittleEndian(v3.AsSpan(56), UsnRecord.BasicInfoChange);
        BinaryPrimitives.WriteUInt16LittleEndian(v3.AsSpan(72), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(v3.AsSpan(74), 76);
        Encoding.Unicode.GetBytes("b").CopyTo(v3, 76);
        // A journal read: the next USN, then the records.
        var buffer = new byte[8].Concat(v2).Concat(v3).ToArray();
        var records = UsnRecord.ParseAll(buffer, 8);
        Assert.Equal(2, records.Count);
        Assert.Equal((42L, (ushort)7), (UsnRecord.RecordOf(records[0].FileId), UsnRecord.SequenceOf(records[0].FileId)));
        Assert.Equal("a.txt", records[0].Name);
        Assert.Equal("created, data extended, closed", UsnRecord.ReasonsText(records[0].Reasons));
        Assert.Equal((UInt128)1 << 100, records[1].FileId);
        Assert.Equal("times or attributes set", UsnRecord.ReasonsText(records[1].Reasons));
        Assert.Equal("replication (DFS Replication)", UsnRecord.SourceText(4));
    }

    private static NtfsFileName Name(long created, byte space = 1) => new(5, 5, created, created, created, created, 0, 0, 0x20, 0, space, "a.txt");

    [Fact]
    public void Timestamp_checks_say_what_shows_times_were_set()
    {
        var now = DateTime.UtcNow;
        long made = now.AddDays(-2).ToFileTimeUtc() + 4_321;
        long stomped = new DateTime(2019, 5, 1, 12, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();

        // Kept times: nothing.
        Assert.Empty(TimestampChecks.Check(made, made, made, made, [Name(made)], [], null, now));

        // Created set back: $FILE_NAME disagrees (strong), whole seconds (a note); the DOS alias is not compared twice.
        var findings = TimestampChecks.Check(stomped, made, made, made, [Name(made), Name(made, space: 2)], [], null, now);
        Assert.Single(findings, f => f.Strong && f.Text.Contains("$FILE_NAME", StringComparison.Ordinal));
        Assert.Contains(findings, f => !f.Strong && f.Text.Contains("whole second", StringComparison.Ordinal));

        // All four set: the change time before the creation time.
        Assert.Contains(TimestampChecks.Check(made, made, made - 50_000_000, made, [], [], null, now), f => f.Strong && f.Text.Contains("MFT change time", StringComparison.Ordinal));

        // The journal saw it made two days ago; its creation time says 2019.
        var journal = new UsnRecord(2, 1, 5, 100, made, UsnRecord.FileCreate, 0, 0, 0x20, "a.txt");
        Assert.Contains(TimestampChecks.Check(stomped + 4_321, made, made, made, [], [journal], null, now), f => f.Strong && f.Text.Contains("change journal records it being created", StringComparison.Ordinal));

        // A time in the future, and one before the volume existed.
        Assert.Contains(TimestampChecks.Check(made, now.AddYears(3).ToFileTimeUtc(), now.AddYears(3).ToFileTimeUtc(), made, [], [], null, now), f => f.Text.Contains("in the future", StringComparison.Ordinal));
        Assert.Contains(TimestampChecks.Check(made, made, made, made, [], [], now, now), f => f.Text.Contains("before this volume was formatted", StringComparison.Ordinal));
    }

    [Fact]
    public void Permissions_read_as_the_Security_tab_says_them_and_weak_ones_stand_out()
    {
        var names = new Dictionary<string, string> { ["SY"] = @"NT AUTHORITY\SYSTEM", ["BU"] = @"BUILTIN\Users", ["WD"] = "Everyone" };
        var file = AccessText.Describe("O:BAG:SYD:PAI(A;;FA;;;SY)(A;;0x1200a9;;;BU)(A;;0x1301bf;;;WD)", folder: false, s => names.GetValueOrDefault(s));
        Assert.Equal(["full control", "read and execute", "modify"], file.Entries.Select(e => e.Rights));
        Assert.Equal(["NT AUTHORITY\\SYSTEM", "BUILTIN\\Users", "Everyone"], file.Entries.Select(e => e.Who));
        Assert.Equal("protected: it inherits nothing from its folder", file.Dacl);
        Assert.Equal("Everyone can change its content and delete it.", Assert.Single(file.Findings, f => f.Strong).Text);

        // A folder: its rights in folder words and where each entry applies.
        var folder = AccessText.Describe("D:AI(A;OICIID;FA;;;SY)(A;CIID;0x100026;;;BU)(A;OICIIOID;GA;;;CO)", folder: true, _ => null);
        Assert.Equal(["This folder, subfolders and files", "This folder and subfolders", "Subfolders and files only"], folder.Entries.Select(e => e.AppliesTo));
        Assert.Equal("add files, add subfolders, traverse", folder.Entries[1].Rights);
        Assert.All(folder.Entries, e => Assert.True(e.Inherited));
        var note = Assert.Single(folder.Findings);
        Assert.False(note.Strong);
        Assert.Equal("BU can add files to it and add subfolders.", note.Text);

        // No list at all, and an empty one.
        Assert.Contains(AccessText.Describe("D:NO_ACCESS_CONTROL", false, _ => null).Findings, f => f.Strong && f.Text.Contains("everyone has full access", StringComparison.Ordinal));
        Assert.StartsWith("empty", AccessText.Describe("O:BAD:", false, _ => null).Dacl);

        // SDDL's letter names run together, and the integrity label.
        Assert.Equal("read and execute", AccessText.Words(AccessText.Mask("FRFX")!.Value, folder: false));
        var labelled = AccessText.Describe("D:(A;;FA;;;SY)S:(ML;;NW;;;LW)", false, _ => null);
        Assert.Contains(labelled.Entries, e => e.Type == "integrity" && e.Who == "low" && e.Rights == "no write up");
        Assert.Equal(["D:", "  (A;;FA;;;SY)", "S:", "  (ML;;NW;;;LW)"], AccessText.SddlLines("D:(A;;FA;;;SY)S:(ML;;NW;;;LW)"));
    }

    [Fact]
    public void Record_text_names_times_attributes_links_and_version_1_IDs()
    {
        Assert.Equal("2024-03-01 10:00:00.1234567 UTC", RecordText.Time(Created));
        Assert.Equal("not set", RecordText.Time(0));
        Assert.True(RecordText.IsWholeSecond(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc()));
        Assert.Equal("0x00000023: read-only, hidden, archive", RecordText.Attributes(0x23));
        Assert.Equal("0x10040020: archive, has extended attributes (EAs), folder (has a file-name index)", RecordText.NtfsAttributes(0x1004_0020));
        Assert.StartsWith("0xA000000C symbolic link", RecordText.ReparseTag(0xA000_000C));

        // A symbolic link's data: substitute and print names, then flags; its print name is shown.
        string target = @"C:\Target";
        var names = Encoding.Unicode.GetBytes(@"\??\C:\Target" + target);
        var data = new byte[12 + names.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), (ushort)(names.Length - target.Length * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), (ushort)(names.Length - target.Length * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), (ushort)(target.Length * 2));
        names.CopyTo(data, 12);
        Assert.Equal(target, RecordText.ReparseTarget(0xA000_000C, data));

        // A version-1 GUID: its time and the network card it was made on.
        var origin = RecordText.GuidOrigin(new Guid("e9b1a2c0-5f3e-11ea-8000-00155d8a1b2c"));
        Assert.NotNull(origin);
        Assert.Contains("2020-03-", origin, StringComparison.Ordinal);
        Assert.Contains("00:15:5D:8A:1B:2C", origin, StringComparison.Ordinal);
        Assert.Null(RecordText.GuidOrigin(Guid.NewGuid()));

        var wrapped = RecordText.Wrap("one two three four five six", 12, "• ");
        Assert.Equal(["• one two", "  three four", "  five six"], wrapped);
    }
}
