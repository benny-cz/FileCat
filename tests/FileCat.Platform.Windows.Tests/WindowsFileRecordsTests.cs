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
    public void Without_administrator_rights_the_report_says_first_what_it_leaves_out()
    {
        if (!OperatingSystem.IsWindows()) return;
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "plain.txt");
            File.WriteAllText(file, "x");
            if (!OnNtfs(file)) return;
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
        if (!OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess) return;
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "stomped.bin");
            File.WriteAllText(file, "small enough to stay in its record");
            if (!OnNtfs(file)) return;
            File.SetCreationTimeUtc(file, new DateTime(2019, 5, 1, 12, 0, 0, DateTimeKind.Utc));
            var report = Read(file);
            Assert.Contains(report.Warnings, w => w.StartsWith("Timestamp checks: 1 sign", StringComparison.Ordinal));
            Assert.Contains(Section(report, "Timestamp checks").Lines, l => l.StartsWith("⚠ Its creation time (2019-05-01 12:00:00.0000000 UTC)", StringComparison.Ordinal));
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
            if (!OnNtfs(file)) return;
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

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string link, string existing, nint security);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, nint input, uint inputSize, nint output, uint outputSize, out uint returned, nint overlapped);
}
