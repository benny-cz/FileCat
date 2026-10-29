using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>D-56 as administrator: $Extend's $ObjId and $Reparse indexes, and the runs of every non-resident attribute.</summary>
public sealed partial class WindowsFileRecordsTests
{
    /// <summary>The item's MFT reference as Windows reports it (its 64-bit file index): record and sequence.</summary>
    private static (long Record, ushort Sequence) Reference(string path)
    {
        using var handle = CreateFile(path, 0x0010_0080 /* read attributes, synchronize */, 7, 0, 3, 0x0220_0000 /* backup semantics, open reparse point */, 0);
        Assert.False(handle.IsInvalid, $"{path}: {Marshal.GetLastPInvokeError()}");
        var info = new byte[52];
        Assert.True(GetFileInformationByHandle(handle, info), $"GetFileInformationByHandle: {Marshal.GetLastPInvokeError()}");
        ulong index = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(44)) << 32 | BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(48));
        return ((long)(index & 0xFFFF_FFFF_FFFF), (ushort)(index >> 48));
    }

    /// <summary>Gives a file an object ID (FSCTL_CREATE_OR_GET_OBJECT_ID, which needs only write access to it): the 64 bytes Windows returns.</summary>
    private static byte[] CreateObjectId(string file)
    {
        using var handle = CreateFile(file, 0xC000_0000 /* read, write */, 7, 0, 3, 0, 0);
        var buffer = new byte[64];
        Assert.True(DeviceIoControl(handle, 0x000900C0, 0, 0, Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0), 64, out _, 0),
            $"FSCTL_CREATE_OR_GET_OBJECT_ID: {Marshal.GetLastPInvokeError()}");
        return buffer;
    }

    private static string Braced(ReadOnlySpan<byte> id)
    {
        var guid = new Guid(id);
        return guid == Guid.Empty ? "none" : guid.ToString("B");
    }

    [Fact]
    public void As_administrator_the_ObjId_and_Reparse_indexes_hold_the_items_entries_as_Windows_reports_them()
    {
        if (!OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess) Assert.Skip("Needs Windows and administrator rights.");
        // These indexes come from the volume's own structures, so the fixtures need a volume FileCat may read raw.
        string dir = RawReadableVolume.NewFolder("filecat-record-tests");
        string junction = Path.Combine(dir, "junction");
        try
        {
            if (!OnNtfs(dir)) Assert.Skip("The test folder is not on NTFS.");
            // A file given an object ID: its $O entry has the same ID and birth IDs, for its record.
            string file = Path.Combine(dir, "tracked.txt");
            File.WriteAllText(file, "x");
            var ids = CreateObjectId(file);
            var (record, sequence) = Reference(file);
            var report = Read(file);
            var objectIds = Section(report, "Object ID index ($ObjId:$O)");
            string excerpt = Excerpt(report, "Object ID index", "Reparse index");
            Assert.True(objectIds.Fields.Any(f => f.Name == "Its entry"), excerpt);
            Assert.Equal($"{Braced(ids.AsSpan(0, 16))} → record {record:N0}, sequence {sequence}", Field(objectIds, "Its entry"));
            Assert.Equal(Braced(ids.AsSpan(16, 16)), Field(objectIds, "Birth volume"));
            Assert.Equal(Braced(ids.AsSpan(32, 16)), Field(objectIds, "Birth object"));
            Assert.Equal(Braced(ids.AsSpan(48, 16)), Field(objectIds, "Domain"));
            Assert.StartsWith("matches what Windows reports (FSCTL_GET_OBJECT_ID)", Field(objectIds, "Compared"), StringComparison.Ordinal);
            Assert.StartsWith("$ObjId, record ", Field(objectIds, "Index"), StringComparison.Ordinal);
            // The file has no reparse point, and $R has nothing for it.
            Assert.StartsWith("matches: it has no reparse point", Field(Section(report, "Reparse index ($Reparse:$R)"), "Compared"), StringComparison.Ordinal);
            Assert.DoesNotContain(report.Warnings, w => w.Contains("index does not agree", StringComparison.Ordinal) || w.Contains("problem", StringComparison.Ordinal));

            // A junction: its $R entry carries the junction's tag and the folder's record.
            string target = Directory.CreateDirectory(Path.Combine(dir, "target")).FullName;
            Junction.Create(junction, target);
            (record, sequence) = Reference(junction);
            report = Read(junction);
            var reparse = Section(report, "Reparse index ($Reparse:$R)");
            excerpt = Excerpt(report, "Reparse index");
            Assert.True(reparse.Fields.Any(f => f.Name == "Its entry"), excerpt);
            Assert.StartsWith($"tag 0xA0000003 mount point (junction)", Field(reparse, "Its entry"), StringComparison.Ordinal);
            Assert.EndsWith($"→ record {record:N0}, sequence {sequence}", Field(reparse, "Its entry"), StringComparison.Ordinal);
            Assert.Equal("matches what Windows reports (FSCTL_GET_REPARSE_POINT): tag 0xA0000003, for this item's record", Field(reparse, "Compared"));
            Assert.StartsWith("matches: Windows reports no object ID for it", Field(Section(report, "Object ID index ($ObjId:$O)"), "Compared"), StringComparison.Ordinal);
            TestContext.Current.TestOutputHelper?.WriteLine(Excerpt(report, "Object ID index", "Reparse index"));

            // Read with a cap of one block: an index larger than that says plainly that the rest was not searched.
            string index = Field(reparse, "Index");
            var capped = Section(new WindowsFileRecords { ViewIndexReadLimit = 4096 }.Read(junction, TestContext.Current.CancellationToken), "Reparse index ($Reparse:$R)");
            if (System.Text.RegularExpressions.Regex.Match(index, @"· ([\d\s  ,.']+) blocks of 4 KiB") is { Success: true } blocks
                && int.Parse(blocks.Groups[1].Value.Where(char.IsAsciiDigit).ToArray(), CultureInfo.InvariantCulture) > 1)
            {
                Assert.Contains("only the first 4 KiB of its ", Field(capped, "Index"), StringComparison.Ordinal);
                Assert.Contains("the rest was not searched", Field(capped, "Index"), StringComparison.Ordinal);
            }
            else Assert.DoesNotContain("not searched", Field(capped, "Index"), StringComparison.Ordinal);
        }
        finally
        {
            // The junction itself first: a recursive delete stumbles on it.
            if (Directory.Exists(junction)) Directory.Delete(junction);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public unsafe void As_administrator_the_indexes_show_the_entries_an_item_had_left_behind()
    {
        if (!OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess) Assert.Skip("Needs Windows and administrator rights.");
        string dir = RawReadableVolume.NewFolder("filecat-record-tests");
        try
        {
            if (!OnNtfs(dir)) Assert.Skip("The test folder is not on NTFS.");
            // The largest keys there can be, so that the item's entries are the last of $O and of $R: NTFS removes a node's
            // last entry by writing the node's end entry over it, and that leaves the entry's key (and data) behind.
            var top = new Guid(Enumerable.Repeat((byte)0xFF, 16).ToArray());
            const uint Tag = 0xA000_FFFF;
            var ids = new byte[64];
            top.TryWriteBytes(ids);
            var reparse = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(reparse, Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(reparse.AsSpan(4), 8);
            "FileCat!"u8.CopyTo(reparse.AsSpan(8));
            // Another program adding an entry to the same node in the meantime moves the end entry over what was left, so
            // a few items are tried before the test says the index's slack showed nothing.
            string excerpt = "";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string file = Path.Combine(dir, $"was-tracked-{attempt}.txt");
                File.WriteAllText(file, "x");
                Control(file, 0x0009_0098 /* FSCTL_SET_OBJECT_ID */, ids);
                Control(file, 0x0009_00A4 /* FSCTL_SET_REPARSE_POINT */, reparse);
                var before = Read(file);
                Assert.StartsWith($"{top:B} → record ", Field(Section(before, "Object ID index ($ObjId:$O)"), "Its entry"), StringComparison.Ordinal);
                Assert.StartsWith("tag 0xA000FFFF", Field(Section(before, "Reparse index ($Reparse:$R)"), "Its entry"), StringComparison.Ordinal);

                Control(file, 0x0009_00A0 /* FSCTL_DELETE_OBJECT_ID */, []);
                Control(file, 0x0009_00AC /* FSCTL_DELETE_REPARSE_POINT */, reparse[..8].Select((b, i) => i is 4 or 5 ? (byte)0 : b).ToArray());
                var after = Read(file);
                excerpt = Excerpt(after, "Object ID index", "Reparse index");
                var objectIds = Section(after, "Object ID index ($ObjId:$O)");
                Assert.StartsWith("matches: Windows reports no object ID for it", Field(objectIds, "Compared"), StringComparison.Ordinal);
                var reparsePoints = Section(after, "Reparse index ($Reparse:$R)");
                Assert.StartsWith("matches: it has no reparse point", Field(reparsePoints, "Compared"), StringComparison.Ordinal);
                bool idLeft = objectIds.Children.Any(c => c.Title.StartsWith("Left behind in $O for this record (", StringComparison.Ordinal)
                    && c.Table!.Rows.Any(r => r[0] == top.ToString("B") && r[4] == "this item"));
                bool tagLeft = reparsePoints.Children.Any(c => c.Title.StartsWith("Left behind in $R for this record (", StringComparison.Ordinal)
                    && c.Table!.Rows.Any(r => r[0].StartsWith("0xA000FFFF", StringComparison.Ordinal) && r[2] == "this item"));
                if (idLeft && tagLeft) break;
                TestContext.Current.TestOutputHelper?.WriteLine($"Attempt {attempt + 1}: $O's left behind {idLeft}, $R's {tagLeft}.");
                Assert.True(attempt < 2, "The indexes did not both show what the item left behind, in three attempts:\n" + excerpt);
            }
            TestContext.Current.TestOutputHelper?.WriteLine(excerpt);
        }
        finally { Directory.Delete(dir, recursive: true); }

        // Setting an object ID is a restore operation (NTFS asks for a handle opened with the restore privilege): enabled
        // on this thread alone, through a copy of the process's token, so other tests running meanwhile are not affected.
        static void Control(string path, uint code, byte[] input)
        {
            Assert.True(ImpersonateSelf(2 /* SecurityImpersonation */), $"ImpersonateSelf: {Marshal.GetLastPInvokeError()}");
            try
            {
                Assert.True(OpenThreadToken(GetCurrentThread(), 0x0020 | 0x0008 /* adjust privileges, query */, true, out nint token), $"OpenThreadToken: {Marshal.GetLastPInvokeError()}");
                try
                {
                    Assert.True(LookupPrivilegeValue(null, "SeRestorePrivilege", out long luid), $"LookupPrivilegeValue: {Marshal.GetLastPInvokeError()}");
                    var privileges = new byte[16];
                    BinaryPrimitives.WriteUInt32LittleEndian(privileges, 1);
                    BinaryPrimitives.WriteInt64LittleEndian(privileges.AsSpan(4), luid);
                    BinaryPrimitives.WriteUInt32LittleEndian(privileges.AsSpan(12), 2 /* SE_PRIVILEGE_ENABLED */);
                    fixed (byte* p = privileges)
                        Assert.True(AdjustTokenPrivileges(token, false, p, 16, 0, 0) && Marshal.GetLastPInvokeError() == 0, $"AdjustTokenPrivileges: {Marshal.GetLastPInvokeError()}");
                }
                finally { CloseHandle(token); }
                using var handle = CreateFile(path, 0xC000_0000 /* read, write */, 7, 0, 3, 0x0220_0000 /* backup semantics, open reparse point */, 0);
                Assert.False(handle.IsInvalid, $"{path}: {Marshal.GetLastPInvokeError()}");
                fixed (byte* i = input)
                    Assert.True(DeviceIoControl(handle, code, (nint)i, (uint)input.Length, 0, 0, out _, 0), $"FSCTL 0x{code:X8}: {Marshal.GetLastPInvokeError()}");
            }
            finally { RevertToSelf(); }
        }
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ImpersonateSelf(int level);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RevertToSelf();

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentThread();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenThreadToken(nint thread, uint access, [MarshalAs(UnmanagedType.Bool)] bool openAsSelf, out nint token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? system, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, byte* state, uint length, nint previous, nint returned);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [Fact]
    public void As_administrator_the_record_lists_the_runs_of_every_non_resident_attribute()
    {
        if (!OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess) Assert.Skip("Needs Windows and administrator rights.");
        string dir = RawReadableVolume.NewFolder("filecat-record-tests");
        try
        {
            if (!OnNtfs(dir)) Assert.Skip("The test folder is not on NTFS.");
            // Content and a named stream too large to stay in the record.
            string file = Path.Combine(dir, "streams.bin");
            File.WriteAllBytes(file, RandomNumberGenerator.GetBytes(300_000));
            File.WriteAllBytes(file + ":extra", RandomNumberGenerator.GetBytes(70_000));
            var report = Read(file);
            var record = Section(report, "MFT record");
            var runs = record.Children.Single(c => c.Title == "Runs of its non-resident attributes");
            var data = runs.Children.Single(c => c.Title.StartsWith("$DATA (", StringComparison.Ordinal));
            var extra = runs.Children.Single(c => c.Title.StartsWith("$DATA:extra (", StringComparison.Ordinal));
            Assert.Equal(["VCN", "LCN", "Clusters", "Where"], extra.Table!.Columns);
            Assert.NotEmpty(extra.Table.Rows);
            Assert.All(extra.Table.Rows, r => Assert.StartsWith("at byte ", r[3], StringComparison.Ordinal));
            Assert.Contains(record.Table!.Rows, r => r[1] == "$DATA" && r[2] == "extra" && r[3].EndsWith("from VCN 0", StringComparison.Ordinal));
            // The record's runs of its content are the clusters Windows reports for it (FSCTL_GET_RETRIEVAL_POINTERS).
            var layout = Section(report, "Layout on disk").Table!;
            Assert.Equal(layout.Rows.Select(r => (r[0], r[1], r[2])), data.Table!.Rows.Select(r => (r[0], r[1], r[2])));
            Assert.DoesNotContain(report.Warnings, w => w.StartsWith("MFT record:", StringComparison.Ordinal));

            // A folder large enough to need index blocks: their runs, and those of its index's bitmap when it has them.
            string folder = Directory.CreateDirectory(Path.Combine(dir, "many")).FullName;
            for (int i = 0; i < 200; i++) File.WriteAllText(Path.Combine(folder, $"entry-{i:000}-with-a-longer-name.txt"), "x");
            var folderRuns = Section(Read(folder), "MFT record").Children.Single(c => c.Title == "Runs of its non-resident attributes");
            Assert.Contains(folderRuns.Children, c => c.Title.StartsWith("$INDEX_ALLOCATION:$I30 (", StringComparison.Ordinal) && c.Table!.Rows.Count > 0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle handle, byte[] info);
}
