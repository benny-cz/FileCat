using System.Runtime.InteropServices;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>D-56 on Windows: what NTFS records about a file, read without changing it.</summary>
public sealed partial class WindowsFileRecordsTests
{
    private static string NewFolder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "filecat-record-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static bool OnNtfs(string path) => new DriveInfo(Path.GetPathRoot(path)!).DriveFormat == "NTFS";

    private static InspectionReport Read(string path, bool privileged = true) =>
        new WindowsFileRecords { AssumeNotPrivileged = !privileged }.Read(path, TestContext.Current.CancellationToken);

    private static InspectionSection Section(InspectionReport report, string title) =>
        report.Sections.Single(s => s.Title.StartsWith(title, StringComparison.Ordinal));

    private static string Field(InspectionSection section, string name) => section.Fields.Single(f => f.Name == name).Value;

    /// <summary>The report's warnings and the named sections as it prints them: what a failure needs to be understood.</summary>
    private static string Excerpt(InspectionReport report, params string[] titles) =>
        string.Join("\n", report.Warnings) + "\n" +
        new InspectionReport(report.Format, report.Sections.Where(s => titles.Any(t => s.Title.StartsWith(t, StringComparison.Ordinal))).ToList(), []).ToText();

    [Fact]
    public void A_file_reads_with_its_exact_times_IDs_links_layout_and_permissions()
    {
        if (!OperatingSystem.IsWindows()) return;
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "data.bin");
            File.WriteAllBytes(file, new byte[300_000]);
            CreateHardLink(Path.Combine(dir, "link.bin"), file, 0);
            var report = Read(file, privileged: false);
            var item = Section(report, "Item");
            Assert.Equal("file", Field(item, "Kind"));
            if (OnNtfs(file)) Assert.Contains("sequence", Field(item, "MFT record"), StringComparison.Ordinal);
            Assert.StartsWith("2 names", Field(item, "Hard links"), StringComparison.Ordinal);
            Assert.Contains(item.Lines, l => l.EndsWith(@"\link.bin", StringComparison.OrdinalIgnoreCase));
            // Every time as NTFS keeps it, to 100 ns.
            Assert.Equal(RecordText.Time(File.GetCreationTimeUtc(file).ToFileTimeUtc()), Field(Section(report, "Times ("), "Created"));
            Assert.Equal(RecordText.Time(File.GetLastWriteTimeUtc(file).ToFileTimeUtc()), Field(Section(report, "Times ("), "Modified"));
            // Its clusters, and who may do what with it.
            var layout = Section(report, "Layout on disk");
            Assert.Contains(layout.Fields, f => f.Name == "Fragments");
            Assert.Equal(["VCN", "LCN", "Clusters", "Where"], layout.Table!.Columns);
            var access = Section(report, "Security").Children.Single(c => c.Title == "Access");
            Assert.Equal(["Type", "Rights", "Inherited", "Who"], access.Table!.Columns);
            Assert.NotEmpty(access.Table.Rows);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_record_lists_the_files_streams_with_what_they_say()
    {
        if (!OperatingSystem.IsWindows()) return;
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "download.txt");
            File.WriteAllText(file, "x");
            if (!OnNtfs(file)) Assert.Skip("Streams need NTFS or ReFS.");
            File.WriteAllText(file + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.org/download.txt\r\n");
            // Without administrator rights too: where the file came from is one look away.
            var section = Section(Read(file, privileged: false), "Streams and attributes (1)");
            var row = Assert.Single(section.Table!.Rows);
            Assert.Equal(("Zone.Identifier", "Stream"), (row[0], row[1]));
            Assert.Contains("https://example.org/download.txt", row[3], StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Without_administrator_rights_the_report_says_first_what_it_leaves_out()
    {
        if (!OperatingSystem.IsWindows()) return;
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "plain.txt");
            File.WriteAllText(file, "x");
            if (!OnNtfs(file)) Assert.Skip("The test folder is not on NTFS.");
            var report = Read(file, privileged: false);
            Assert.Equal("Needs administrator rights", report.Sections[1].Title);
            Assert.DoesNotContain(report.Sections, s => s.Title == "MFT record" || s.Title.StartsWith("Names", StringComparison.Ordinal));
            Assert.Contains(Section(report, "Timestamp checks").Lines, l => l.Contains("strongest checks did not run", StringComparison.Ordinal));
            Assert.Contains(Section(report, "Change journal").Fields, f => f.Name == "Latest USN");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void As_administrator_the_MFT_record_shows_a_creation_time_set_afterwards()
    {
        if (!OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess) Assert.Skip("Needs Windows and administrator rights.");
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "stomped.bin");
            File.WriteAllText(file, "small enough to stay in its record");
            if (!OnNtfs(file)) Assert.Skip("The test folder is not on NTFS.");
            File.SetCreationTimeUtc(file, new DateTime(2019, 5, 1, 12, 0, 0, DateTimeKind.Utc));
            // Flushed, so NTFS has written the change's log records to $LogFile on disk before it is read.
            using (var flush = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) flush.Flush(flushToDisk: true);
            var report = Read(file);
            // Two signs: $FILE_NAME's creation time, and $LogFile's before and after images of the change. (A failure
            // prints what the report saw: CI run 36797153928 on Windows ARM64 had one sign, in a second.)
            Assert.True(report.Warnings.Any(w => w.StartsWith("Timestamp checks: 2 signs", StringComparison.Ordinal)),
                "Expected two signs; the report:\n" + Excerpt(report, "Timestamp checks", "NTFS log ($LogFile)", "MFT record"));
            // The lines as one text (wrapped lines continue indented).
            var checks = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", Section(report, "Timestamp checks").Lines), @"\s+", " ");
            Assert.Contains("⚠ Its creation time (2019-05-01 12:00:00.0000000 UTC)", checks, StringComparison.Ordinal);
            Assert.Matches(@"⚠ \$LogFile \(LSN [\d\s\u00A0\u202F,.']+\) shows its created time set back from \d{4}-\d\d-\d\d \d\d:\d\d:\d\d to 2019-05-01 12:00:00", checks);
            var log = Section(report, "NTFS log ($LogFile)");
            Assert.Contains(log.Table!.Rows, r => r[1] == "InitializeFileRecordSegment" && r[2].Contains("as “stomped.bin”", StringComparison.Ordinal));
            Assert.Contains(log.Table.Rows, r => r[2].StartsWith("its name “stomped.bin” added to the index of", StringComparison.Ordinal));
            Assert.Contains(log.Table.Rows, r => r[2].Contains("Created 2019-05-01 12:00:00 (was", StringComparison.Ordinal));
            var secure = Section(report, "Security descriptor in $Secure");
            Assert.EndsWith("matches the descriptor", Field(secure, "Hash"), StringComparison.Ordinal);
            string compared = Field(secure, "As Windows reports");
            // (With "; Windows reports N of its entries as inherited" where the folder's DACL is not auto-inherited.)
            Assert.True(compared.StartsWith("the same owner, group, and DACL", StringComparison.Ordinal), compared + "\n" + string.Join("\n", secure.Lines));
            var names = Section(report, "Names ($FILE_NAME)");
            Assert.Contains(names.Children, c => c.Title.StartsWith("“stomped.bin”", StringComparison.Ordinal));
            var record = Section(report, "MFT record");
            Assert.Contains(record.Table!.Rows, r => r[1] == "$STANDARD_INFORMATION");
            Assert.Contains(record.Children, c => c.Title.StartsWith("Resident content", StringComparison.Ordinal));
            Assert.StartsWith("yes", Field(Section(report, "Layout on disk"), "Resident"), StringComparison.Ordinal);
            Assert.DoesNotContain(report.Warnings, w => w.StartsWith("MFT record:", StringComparison.Ordinal));
            // The journal, when it is on for this volume, saw it made and its times set.
            var journal = Section(report, "Change journal");
            if (journal.Children.FirstOrDefault(c => c.Title.StartsWith("History", StringComparison.Ordinal)) is { } history)
            {
                Assert.Contains(history.Table!.Rows, r => r[2].StartsWith("created", StringComparison.Ordinal));
                Assert.Contains(history.Table.Rows, r => r[2].Contains("times or attributes set", StringComparison.Ordinal));
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void As_administrator_a_folder_shows_the_names_its_index_kept_after_deletes()
    {
        if (!OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess) Assert.Skip("Needs Windows and administrator rights.");
        string dir = NewFolder();
        try
        {
            if (!OnNtfs(dir)) Assert.Skip("The test folder is not on NTFS.");
            string folder = Directory.CreateDirectory(Path.Combine(dir, "many")).FullName;
            for (int i = 0; i < 300; i++) File.WriteAllText(Path.Combine(folder, $"entry-{i:000}-with-a-longer-name.txt"), "x");
            for (int i = 250; i < 300; i++) File.Delete(Path.Combine(folder, $"entry-{i:000}-with-a-longer-name.txt"));
            var index = Section(Read(folder), "Folder index ($I30)");
            Assert.StartsWith("250 names in use", Field(index, "Entries"), StringComparison.Ordinal);
            var left = index.Children.Single(c => c.Title.StartsWith("Left behind in its index (", StringComparison.Ordinal));
            Assert.Contains(left.Table!.Rows, r => r[0].StartsWith("entry-2", StringComparison.Ordinal) && int.Parse(r[0][6..9]) >= 250 && r[5] == "gone");
            Assert.DoesNotContain(left.Table.Rows, r => r[0] == "entry-100-with-a-longer-name.txt" && r[5] == "gone");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_junction_names_its_target_and_a_sparse_file_its_stored_ranges()
    {
        if (!OperatingSystem.IsWindows()) return;
        string dir = NewFolder();
        try
        {
            string target = Directory.CreateDirectory(Path.Combine(dir, "target")).FullName;
            string junction = Path.Combine(dir, "junction");
            Junction.Create(junction, target);
            var report = Read(junction, privileged: false);
            Assert.Equal("junction (mount point)", Field(Section(report, "Item"), "Kind"));
            var reparse = Section(report, "Reparse point");
            Assert.StartsWith("0xA0000003 mount point (junction)", Field(reparse, "Tag"), StringComparison.Ordinal);
            Assert.Equal(target, Field(reparse, "Says"));

            string sparse = Path.Combine(dir, "sparse.bin");
            using (var stream = new FileStream(sparse, FileMode.Create, FileAccess.ReadWrite))
            {
                Assert.True(DeviceIoControl(stream.SafeFileHandle, 0x000900C4 /* FSCTL_SET_SPARSE */, 0, 0, 0, 0, out _, 0));
                stream.SetLength(10 << 20);
                stream.Position = 5 << 20;
                stream.Write(new byte[4096].Select(_ => (byte)1).ToArray());
            }
            string ranges = Field(Section(Read(sparse, privileged: false), "Layout on disk"), "Sparse");
            Assert.Contains("stored, in 1 range", ranges, StringComparison.Ordinal);
        }
        finally
        {
            // The junction itself first: a recursive delete stumbles on it (it tries to unmount it as a volume).
            if (Directory.Exists(Path.Combine(dir, "junction"))) Directory.Delete(Path.Combine(dir, "junction"));
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void An_object_ID_says_when_its_birth_ID_was_made()
    {
        if (!OperatingSystem.IsWindows()) return;
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "tracked.txt");
            File.WriteAllText(file, "x");
            if (!OnNtfs(file)) Assert.Skip("The test folder is not on NTFS.");
            using (var handle = CreateFile(file, 0xC000_0000 /* read, write */, 7, 0, 3, 0, 0))
            {
                var buffer = new byte[64];
                Assert.True(DeviceIoControl(handle, 0x000900C0 /* FSCTL_CREATE_OR_GET_OBJECT_ID */, 0, 0, Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0), 64, out _, 0),
                    $"FSCTL_CREATE_OR_GET_OBJECT_ID: {Marshal.GetLastPInvokeError()}");
            }
            var section = Section(Read(file, privileged: false), "Object ID ($OBJECT_ID)");
            Assert.Matches(@"^\{[0-9a-f-]{36}\}", Field(section, "Object ID"));
            // Birth IDs are said when the volume gave them (a volume without an object ID of its own leaves them empty).
            Assert.All(section.Fields.Where(f => f.Name.StartsWith("Birth", StringComparison.Ordinal)), f => Assert.Matches(@"^\{[0-9a-f-]{36}\}", f.Value));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_folder_says_where_each_permission_applies()
    {
        if (!OperatingSystem.IsWindows()) return;
        string dir = NewFolder();
        try
        {
            var report = Read(dir, privileged: false);
            Assert.Equal("folder", Field(Section(report, "Item"), "Kind"));
            var access = Section(report, "Security").Children.Single(c => c.Title == "Access");
            Assert.Equal(["Type", "Rights", "Applies to", "Inherited", "Who"], access.Table!.Columns);
            Assert.Contains(access.Table.Rows, r => r[2] == "This folder, subfolders and files");
            Assert.Contains(Section(report, "Security").Children, c => c.Title == "SDDL");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Security_descriptors_are_compared_part_by_part_not_as_text()
    {
        if (!OperatingSystem.IsWindows()) return;
        static byte[] Binary(string sddl)
        {
            var descriptor = new System.Security.AccessControl.RawSecurityDescriptor(sddl);
            var bytes = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(bytes, 0);
            return bytes;
        }
        static WindowsFileRecords.SecurityComparison Compare(string stored, string live) => WindowsFileRecords.CompareSecurity(Binary(stored), Binary(live));
        const string Stored = "O:BAG:SYD:AI(A;ID;FA;;;SY)(A;ID;FA;;;BA)(A;ID;0x1200a9;;;BU)";
        Assert.Empty(Compare(Stored, Stored).Differences);
        // A SACL, which the comparison leaves out, does not count.
        Assert.Empty(Compare(Stored, Stored + "S:(ML;;NW;;;LW)").Differences);
        Assert.Equal(["owner"], Compare(Stored, Stored.Replace("O:BA", "O:SY", StringComparison.Ordinal)).Differences);
        Assert.Equal(["DACL flags"], Compare(Stored, Stored.Replace("D:AI", "D:PAI", StringComparison.Ordinal)).Differences);
        Assert.Equal(["DACL order"], Compare(Stored, "O:BAG:SYD:AI(A;ID;FA;;;BA)(A;ID;FA;;;SY)(A;ID;0x1200a9;;;BU)").Differences);
        Assert.Equal(["group", "DACL entries"], Compare(Stored, "O:BAG:BAD:AI(A;ID;FA;;;SY)(A;ID;FA;;;BA)(A;ID;FA;;;BU)").Differences);
        // A DACL stored without inheritance marks (not auto-inherited): Windows marks the entries it finds in the folder
        // inherited and lists the item's own first. The same DACL, read by Windows.
        var unmarked = Compare("O:BAG:SYD:(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;WD)", "O:BAG:SYD:(A;;FR;;;WD)(A;ID;FA;;;SY)(A;ID;FA;;;BA)");
        Assert.Empty(unmarked.Differences);
        Assert.Equal(2, unmarked.MarkedInherited);
        // An entry that differs in more than its mark still differs.
        Assert.Equal(["DACL entries"], Compare("O:BAG:SYD:(A;;FA;;;SY)", "O:BAG:SYD:(A;ID;FR;;;SY)").Differences);
        // An auto-inherited DACL's marks are its own: a mark Windows reports that the stored copy lacks is a difference.
        Assert.Equal(["DACL entries"], Compare("O:BAG:SYD:AI(A;;FA;;;SY)", "O:BAG:SYD:AI(A;ID;FA;;;SY)").Differences);
    }

    [Fact]
    public void As_administrator_a_DACL_stored_without_inheritance_marks_is_the_same_as_Windows_reports()
    {
        if (!OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess) Assert.Skip("Needs Windows and administrator rights.");
        string dir = NewFolder();
        try
        {
            if (!OnNtfs(dir)) Assert.Skip("The test folder is not on NTFS.");
            // The folder's DACL set the pre-Windows 2000 way: inheritable entries, not marked auto-inherited (as some
            // profile folders are, a runner's Temp among them). Its files get the entries without inheritance marks.
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            string old = Path.Combine(dir, "old");
            Directory.CreateDirectory(old);
            SetRawDacl(old, $"D:(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;{user})");
            string file = Path.Combine(old, "plain.txt");
            File.WriteAllText(file, "x");
            var secure = Section(Read(file), "Security descriptor in $Secure");
            Assert.Equal("the same owner, group, and DACL; Windows reports 3 of its entries as inherited (see below)", Field(secure, "As Windows reports"));
            Assert.Contains(secure.Lines, l => l.StartsWith("• Its DACL is stored without inheritance marks", StringComparison.Ordinal));
            Assert.Contains(secure.Lines, l => l.Trim() == "(A;;FA;;;SY)");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>Sets a DACL as given (SetFileSecurity: no inheritance worked out, no flags added).</summary>
    private static void SetRawDacl(string path, string sddl)
    {
        var descriptor = new System.Security.AccessControl.RawSecurityDescriptor(sddl);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        Assert.True(SetFileSecurity(path, 4 /* DACL_SECURITY_INFORMATION */, bytes), $"SetFileSecurity: {Marshal.GetLastPInvokeError()}");
    }

    [LibraryImport("advapi32.dll", EntryPoint = "SetFileSecurityW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileSecurity(string path, uint information, byte[] descriptor);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string link, string existing, nint security);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, nint input, uint inputSize, nint output, uint outputSize, out uint returned, nint overlapped);
}
