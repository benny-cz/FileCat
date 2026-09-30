using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

/// <summary>D-56 on Linux and macOS: what the file system records about a file, read without changing it.</summary>
public sealed partial class UnixFileRecordTests
{
    private static string NewFolder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "filecat-record-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static InspectionReport Read(string path) => new UnixFileRecords().Read(path, TestContext.Current.CancellationToken);

    private static InspectionSection Section(InspectionReport report, string title) =>
        report.Sections.Single(s => s.Title == title || s.Title.StartsWith(title + " (", StringComparison.Ordinal));

    private static string Field(InspectionSection section, string name) => section.Fields.Single(f => f.Name == name).Value;

    [Fact]
    public void Flags_read_as_lsattr_and_chflags_print_them()
    {
        Assert.Equal("----i---------e------- · immutable, extents", UnixFileRecords.LsattrText(0x10 | 0x80000));
        Assert.Equal("---------------------- · none", UnixFileRecords.LsattrText(0));
        Assert.Equal("uchg,hidden · immutable, hidden in the Finder", UnixFileRecords.ChflagsText(0x2 | 0x8000));
        Assert.Equal("none", UnixFileRecords.ChflagsText(0));
    }

    [Fact]
    public void Unix_timestamp_checks_say_what_shows_times_were_set()
    {
        var now = DateTime.UtcNow;
        var born = now.AddDays(-1);
        Assert.Empty(TimestampChecks.CheckUnix(born, born, born, born, false, false, now));
        // Set into the future of the moment it was set at: later than the status change.
        Assert.Contains(TimestampChecks.CheckUnix(born, now.AddHours(5), now, now, false, false, now), f => f.Strong && f.Text.Contains("status change time", StringComparison.Ordinal));
        // Copied with its times (cp -p): earlier than its birth, and a whole second; a note, not a warning.
        var copied = TimestampChecks.CheckUnix(born, new DateTime(2019, 5, 1, 12, 0, 0, DateTimeKind.Utc), born, born, true, false, now);
        Assert.All(copied, f => Assert.False(f.Strong));
        Assert.Contains(copied, f => f.Text.Contains("earlier than its birth", StringComparison.Ordinal));
        Assert.Contains(copied, f => f.Text.Contains("whole second", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_reads_with_its_inode_times_permissions_and_layout()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "data.bin");
            File.WriteAllBytes(file, new byte[200_000].Select((_, i) => (byte)i).ToArray());
            var modified = new DateTime(2019, 5, 1, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(file, modified);
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            var report = Read(file);
            var item = Section(report, "Item");
            Assert.Equal("file", Field(item, "Kind"));
            Assert.Contains(item.Fields, f => f.Name is "Inode" or "File ID (inode)");
            var times = Section(report, "Times");
            Assert.Equal("2019-05-01 12:00:00.000000000 UTC", Field(times, "Modified"));
            Assert.Contains(times.Fields, f => f.Name == "Status changed");
            // Copied-in times read as such: earlier than its birth (where the system keeps births), and a whole second.
            Assert.Contains(Section(report, "Timestamp checks").Lines, l => l.Contains("whole second", StringComparison.Ordinal) || l.Contains("earlier than its birth", StringComparison.Ordinal));
            var security = Section(report, "Security");
            Assert.Equal("-rw-r--r-- (644)", Field(security, "Mode"));
            Assert.StartsWith(UnixPermissions.UserName(UnixPermissions.CurrentUserId), Field(security, "Owner"), StringComparison.Ordinal);
            Assert.Empty(report.Warnings);
            Assert.Equal("200,000 bytes (195 KiB)".Replace(",", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator, StringComparison.Ordinal)
                .Replace("195", (200_000 / 1024.0).ToString("0", System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal), Field(Section(report, "Layout on disk"), "Size"));
            if (OperatingSystem.IsLinux())
            {
                Assert.Contains(item.Fields, f => f.Name == "Inode flags");
                Assert.Contains(Section(report, "Volume").Fields, f => f.Name == "File system");
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void World_writable_items_are_flagged_and_a_sticky_folder_is_only_noted()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "shared.txt");
            File.WriteAllText(file, "x");
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
            Assert.Contains(Read(file).Warnings, w => w == "Permissions: Anyone on this computer can change its content: it is world-writable.");

            string open = Directory.CreateDirectory(Path.Combine(dir, "open")).FullName;
            File.SetUnixFileMode(open, (UnixFileMode)0x1FF);
            Assert.Contains(Read(open).Warnings, w => w.Contains("without the sticky bit", StringComparison.Ordinal));
            File.SetUnixFileMode(open, (UnixFileMode)0x3FF);
            var sticky = Read(open);
            Assert.Empty(sticky.Warnings);
            Assert.Contains(Section(sticky, "Security").Lines, l => l.Contains("sticky bit", StringComparison.Ordinal));
            Assert.Equal("drwxrwxrwt (1777)", Field(Section(sticky, "Security"), "Mode"));

            // Character devices are world-writable by design (/dev/null, /dev/tty): nothing to warn about.
            var devNull = Read("/dev/null");
            Assert.StartsWith("character device", Field(Section(devNull, "Item"), "Kind"), StringComparison.Ordinal);
            Assert.Empty(devNull.Warnings);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_POSIX_ACL_reads_as_a_table_of_who_may_do_what()
    {
        if (!OperatingSystem.IsLinux()) return;
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "acl.txt");
            File.WriteAllText(file, "x");
            // user::rw- user:<me>:r-- group::r-- mask::r-- other::---
            uint me = UnixPermissions.CurrentUserId;
            var acl = new byte[4 + 5 * 8];
            BinaryPrimitives.WriteUInt32LittleEndian(acl, 2);
            void Entry(int index, ushort tag, ushort perm, uint id)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(4 + index * 8), tag);
                BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(6 + index * 8), perm);
                BinaryPrimitives.WriteUInt32LittleEndian(acl.AsSpan(8 + index * 8), id);
            }
            Entry(0, 0x01, 6, uint.MaxValue);
            Entry(1, 0x02, 4, me);
            Entry(2, 0x04, 4, uint.MaxValue);
            Entry(3, 0x10, 4, uint.MaxValue);
            Entry(4, 0x20, 0, uint.MaxValue);
            if (SetAttribute(file, "system.posix_acl_access", acl, acl.Length, 0) != 0)
            {
                Assert.Skip("This file system keeps no POSIX ACLs here.");
                return;
            }
            var table = Section(Read(file), "Security").Children.Single(c => c.Title == "ACL (POSIX)").Table!;
            Assert.Equal(["Entry", "Who", "Rights", "For"], table.Columns);
            Assert.Contains(table.Rows, r => r[0] == "user" && r[1] == UnixPermissions.UserName(me) && r[2] == "read");
            Assert.Contains(table.Rows, r => r[0] == "other" && r[2] == "none");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_macOS_file_shows_its_BSD_flags_and_added_time()
    {
        if (!OperatingSystem.IsMacOS()) return;
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "flagged.txt");
            File.WriteAllText(file, "x");
            Assert.Equal(0, Chflags(file, 0x8000)); // UF_HIDDEN
            var report = Read(file);
            Assert.StartsWith("hidden", Field(Section(report, "Item"), "Flags"), StringComparison.Ordinal);
            Assert.Contains(Section(report, "Times").Fields, f => f.Name == "Birth");
            ulong inode = UnixFiles.Stat(file)!.Value.Inode;
            Assert.Equal(inode.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), Field(Section(report, "Item"), "File ID (inode)"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_record_lists_the_items_attributes_with_what_they_say()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "notes.txt");
            File.WriteAllText(file, "x");
            string name = OperatingSystem.IsMacOS() ? "com.example.note" : "user.note";
            byte[] value = "a hidden note"u8.ToArray();
            int set = OperatingSystem.IsMacOS() ? MacSetAttribute(file, name, value, value.Length, 0, 1) : SetAttribute(file, name, value, value.Length, 0);
            if (set != 0)
            {
                Assert.Skip("This file system keeps no extended attributes here.");
                return;
            }
            var section = Section(Read(file), "Streams and attributes");
            Assert.Contains(section.Table!.Rows, r => r[0] == name && r[3] == "a hidden note");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [LibraryImport("libc", EntryPoint = "lsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int SetAttribute(string path, string name, byte[] value, nint size, int flags);

    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacSetAttribute(string path, string name, byte[] value, nint size, uint position, int options);

    [LibraryImport("libc", EntryPoint = "chflags", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Chflags(string path, uint flags);
}
