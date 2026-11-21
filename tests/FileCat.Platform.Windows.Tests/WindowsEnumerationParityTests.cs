using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// F-ENUM / N-ENUM parity of the native tag-bearing listing with the managed route: container detection, refusals,
/// the drive root, case-distinct and 255-unit names, and other filesystems. Only directory listings are read.
/// </summary>
public sealed partial class WindowsEnumerationParityTests
{
    private sealed class Sink : IEnumerationSink
    {
        public readonly Dictionary<string, EntryData> Entries = new(StringComparer.Ordinal);
        public readonly List<string> Issues = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var entry in entries) Assert.True(Entries.TryAdd(entry.Name, entry), "Duplicate entry " + entry.Name);
        }
        public void ReportIssue(string message) => Issues.Add(message);
    }

    private sealed class ZipDetector : IContainerDetector
    {
        public bool IsContainer(string fileName) => fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        public Location? GetContainerLocation(string filePath) => null;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(Sink Managed, Sink Native)> Both(string path, IContainerDetector? detector = null)
    {
        var location = Location.FileSystem(path);
        var managed = new Sink(); var native = new Sink();
        await new LocalFileSystemProvider { ContainerDetector = detector }.EnumerateAsync(location, managed, Ct);
        await new WindowsFileSystemProvider { UseNativeEnumeration = true, ContainerDetector = detector }.EnumerateAsync(location, native, Ct);
        return (managed, native);
    }

    private static void SameEntries(Sink managed, Sink native, bool compareTimes = true)
    {
        Assert.Equal(managed.Entries.Keys.Order(StringComparer.Ordinal), native.Entries.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, entry) in managed.Entries)
        {
            var got = native.Entries[name];
            Assert.Equal((entry.Kind, entry.Attributes, entry.Flags), (got.Kind, got.Attributes, got.Flags));
            if (!compareTimes) continue;
            Assert.Equal(entry.Size, got.Size);
            Assert.Equal(entry.Modified, got.Modified);
        }
        Assert.Equal(managed.Issues, native.Issues);
    }

    [Fact]
    public async Task Container_detection_marks_the_same_files_on_both_routes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native enumeration.");
        string root = Directory.CreateTempSubdirectory("fc-enum-container-").FullName;
        try
        {
            File.WriteAllText(Path.Join(root, "archive.zip"), "not really a zip");
            File.WriteAllText(Path.Join(root, "UPPER.ZIP"), "not really a zip");
            File.WriteAllText(Path.Join(root, "plain.txt"), "text");
            Directory.CreateDirectory(Path.Join(root, "folder.zip"));
            var (managed, native) = await Both(root, new ZipDetector());
            SameEntries(managed, native);
            Assert.True((native.Entries["archive.zip"].Flags & EntryFlags.Container) != 0);
            Assert.True((native.Entries["UPPER.ZIP"].Flags & EntryFlags.Container) != 0);
            Assert.False((native.Entries["plain.txt"].Flags & EntryFlags.Container) != 0);
            Assert.False((native.Entries["folder.zip"].Flags & EntryFlags.Container) != 0);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task A_folder_that_cannot_be_listed_fails_the_same_way_on_both_routes_before_publication()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native enumeration.");
        Assert.SkipWhen(Core.State.WindowsTokenAuthority.IsHighIntegrity, "An elevated test process may hold rights that bypass the deny entry.");
        string root = Directory.CreateTempSubdirectory("fc-enum-denied-").FullName;
        string closed = Path.Join(root, "closed");
        DirectorySecurity? original = null;
        try
        {
            Directory.CreateDirectory(closed);
            File.WriteAllText(Path.Join(closed, "hidden.txt"), "inside");
            var info = new DirectoryInfo(closed); original = info.GetAccessControl();
            var security = info.GetAccessControl();
            using (var identity = WindowsIdentity.GetCurrent())
                security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
            info.SetAccessControl(security);
            var location = Location.FileSystem(closed);
            var managed = new Sink(); var native = new Sink();
            var managedError = await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => new LocalFileSystemProvider().EnumerateAsync(location, managed, Ct));
            var nativeError = await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() =>
                new WindowsFileSystemProvider { UseNativeEnumeration = true }.EnumerateAsync(location, native, Ct));
            Assert.Equal(managedError.GetType(), nativeError.GetType());
            Assert.Empty(managed.Entries); Assert.Empty(native.Entries);
        }
        finally
        {
            if (original is not null)
            {
                var restored = new DirectorySecurity();
                restored.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
                new DirectoryInfo(closed).SetAccessControl(restored);
            }
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task A_compatibility_junction_that_refuses_listing_names_the_same_listable_target_on_both_routes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native enumeration.");
        string link = Path.Join(Path.GetPathRoot(Environment.SystemDirectory)!, "Documents and Settings");
        Assert.SkipUnless(Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0,
            "The Windows compatibility junction is not present.");
        Assert.SkipWhen(Core.State.WindowsTokenAuthority.IsHighIntegrity, "Administrators can list the compatibility junction.");
        var location = Location.FileSystem(link);
        var managed = await Assert.ThrowsAsync<LinkListingRefusedException>(() => new LocalFileSystemProvider().EnumerateAsync(location, new Sink(), Ct));
        var native = await Assert.ThrowsAsync<LinkListingRefusedException>(() =>
            new WindowsFileSystemProvider { UseNativeEnumeration = true }.EnumerateAsync(location, new Sink(), Ct));
        Assert.Equal(managed.Target, native.Target);
        Assert.Equal(managed.Link, native.Link);
    }

    [Fact]
    public async Task The_system_drive_root_lists_the_same_names_kinds_and_attributes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native enumeration.");
        string root = Path.GetPathRoot(Environment.SystemDirectory)!;
        var (managed, native) = await Both(root);
        // Sizes and times of live system files (page and swap files, logs) can change between the two listings.
        SameEntries(managed, native, compareTimes: false);
        Assert.Contains(native.Entries.Values, entry => entry.Kind == EntryKind.Directory && entry.Name == "Windows");
        Assert.Contains(native.Entries.Values, entry => entry.NativeReparseTag != 0); // e.g. Documents and Settings
    }

    [Fact]
    public async Task Case_distinct_and_255_unit_names_are_listed_exactly_on_both_routes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native enumeration.");
        string root = Directory.CreateTempSubdirectory("fc-enum-names-").FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(root, "case")).FullName;
            CreateCaseSiblings(folder, "a", "A", "ß", "SS");
            var (managed, native) = await Both(folder);
            SameEntries(managed, native);
            Assert.Equal(["A", "SS", "a", "ß"], native.Entries.Keys.Order(StringComparer.Ordinal));

            const string middle = " 名前 ü ";
            string longName = new string('n', 200) + middle + new string('z', 255 - 200 - middle.Length - 4) + ".txt";
            Assert.Equal(255, longName.Length);
            File.WriteAllText(Path.Join(root, longName), "longest component");
            var (managedLong, nativeLong) = await Both(root);
            SameEntries(managedLong, nativeLong);
            Assert.Contains(longName, nativeLong.Entries.Keys);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("FILECAT_STORAGE_NATIVE_FAT_TARGET")]
    [InlineData("FILECAT_STORAGE_NATIVE_EXFAT_TARGET")]
    [InlineData("FILECAT_STORAGE_NATIVE_REFS_TARGET")]
    [InlineData("FILECAT_STORAGE_NATIVE_NOFILTER_ROOT")]
    public async Task Prepared_volumes_list_the_same_entries_on_both_routes(string variable)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native enumeration.");
        string? target = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(target)) Assert.Skip("Set " + variable + " to an item on the prepared volume.");
        string folder = Directory.Exists(target) ? target : Path.GetDirectoryName(target)!;
        string probe = Directory.CreateDirectory(Path.Join(folder, "fc-enum-parity-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            for (int i = 0; i < 600; i++) File.WriteAllText(Path.Join(probe, "entry " + i + ".txt"), "x");
            File.SetAttributes(Path.Join(probe, "entry 0.txt"), FileAttributes.Hidden | FileAttributes.ReadOnly);
            Directory.CreateDirectory(Path.Join(probe, "nested"));
            var (managed, native) = await Both(probe);
            SameEntries(managed, native);
            Assert.Equal(601, native.Entries.Count);
        }
        finally
        {
            File.SetAttributes(Path.Join(probe, "entry 0.txt"), FileAttributes.Normal);
            Directory.Delete(probe, true);
        }
    }

    private static unsafe void CreateCaseSiblings(string folder, params string[] names)
    {
        using var directory = NtFile.OpenOrThrow(null, @"\??\" + folder, NtFile.WriteAttributes | NtFile.ReadAttributes | NtFile.Synchronize,
            NtFile.ShareAll, NtFile.Open, NtFile.DirectoryFile | NtFile.OpenReparsePoint, "Set the disposable directory case flag");
        uint mode = 1;
        Assert.True(SetInfo(directory, 23, &mode, 4), "Set case flag: " + Marshal.GetLastPInvokeError());
        foreach (string name in names)
        {
            int status = NtFile.TryOpen(directory, name, NtFile.WriteData | NtFile.ReadAttributes | NtFile.Synchronize,
                NtFile.ShareAll, NtFile.Create, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint, out var created, caseSensitive: true);
            using (created) Assert.True(status >= 0, NtFile.Error(status, "Create case sibling").Message);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetInfo(Microsoft.Win32.SafeHandles.SafeFileHandle file, int infoClass, void* input, uint length);
}
