using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class NativeSearchAttributeTests
{
    [Fact]
    public unsafe void Raw_reparse_sparse_and_offline_predicates_use_the_native_bits_without_content_search()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows native attribute fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-find-attributes-").FullName;
        try
        {
            string plain = Path.Combine(root, "plain"), sparse = Path.Combine(root, "sparse"), offline = Path.Combine(root, "offline"), link = Path.Combine(root, "link");
            foreach (string path in new[] { plain, sparse, offline }) File.WriteAllText(path, "untouched payload");
            using (var file = File.OpenHandle(sparse, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                Assert.True(Control(file, 0x900C4 /* FSCTL_SET_SPARSE */, null, 0, null, 0, out _, 0), $"SET_SPARSE: {Marshal.GetLastPInvokeError()}");
            File.SetAttributes(offline, File.GetAttributes(offline) | FileAttributes.Offline);
            File.CreateSymbolicLink(link, plain);
            foreach (var (attribute, name) in new[] { (FileAttributes.SparseFile, "sparse"), (FileAttributes.Offline, "offline"), (FileAttributes.ReparsePoint, "link") })
            {
                var yes = new ResultSet("set", "set", "native fixture");
                var required = new SearchSession(new SearchQuery { Roots = [root], AttributesSet = attribute }, yes);
                required.Run(TestContext.Current.CancellationToken);
                Assert.Equal(name, Assert.Single(yes.Snapshot()).Item.Name); Assert.Equal(0, required.FilesExamined); Assert.True(yes.IsComplete);
                var no = new ResultSet("clear", "clear", "native fixture");
                var excluded = new SearchSession(new SearchQuery { Roots = [root], AttributesClear = attribute }, no);
                excluded.Run(TestContext.Current.CancellationToken);
                Assert.Equal(3, no.Count); Assert.DoesNotContain(no.Snapshot(), r => r.Item.Name == name); Assert.Equal(0, excluded.FilesExamined); Assert.True(no.IsComplete);
            }
            Assert.Equal("untouched payload", File.ReadAllText(plain));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static bool Efs => Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1";

    /// <summary>A disposable root in the user's temporary folder: EFS refuses system folders, which an elevated TEMP can be.</summary>
    private static string UserTempRoot(string prefix) => Directory.CreateDirectory(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", prefix + Guid.NewGuid().ToString("N"))).FullName;

    private static (SearchSession Session, ResultSet Set) Find(SearchQuery query)
    {
        var set = new ResultSet(Guid.NewGuid().ToString("N"), "find", "native fixture");
        var session = new SearchSession(query, set);
        session.Run(TestContext.Current.CancellationToken);
        return (session, set);
    }

    private static string[] Names(ResultSet set) => [.. set.Snapshot().Select(r => r.Item.Name).Order(StringComparer.Ordinal)];

    private static unsafe void Compress(string path)
    {
        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        ushort format = 1; // COMPRESSION_FORMAT_DEFAULT
        Assert.True(Control(file, 0x9C040 /* FSCTL_SET_COMPRESSION */, &format, 2, null, 0, out _, 0), $"SET_COMPRESSION: {Marshal.GetLastPInvokeError()}");
    }

    /// <summary>
    /// F-FIND: compression and, with FILECAT_TEST_EFS=1, EFS predicates come from the directory listing alone. A file
    /// whose delete is pending stays listed but refuses every new open, metadata-only opens included, so a complete,
    /// exact result over such files proves the search opened none of them.
    /// </summary>
    [Fact]
    public unsafe void Compressed_and_EFS_predicates_match_listed_files_that_refuse_every_open()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native attribute fixture.");
        string root = UserTempRoot("filecat-find-pending-");
        var pending = new List<SafeFileHandle>();
        try
        {
            string compressed = Path.Combine(root, "compressed.bin"), encrypted = Path.Combine(root, "encrypted.bin"), plain = Path.Combine(root, "plain.bin");
            foreach (string path in new[] { compressed, encrypted, plain }) File.WriteAllBytes(path, new byte[64 * 1024]);
            Compress(compressed);
            if (Efs) File.Encrypt(encrypted);
            foreach (string path in new[] { compressed, encrypted, plain })
            {
                var handle = CreateFile(path, 0x10000 /* DELETE */ | 0x80 /* READ_ATTRIBUTES */, 7, 0, 3, 0, 0);
                Assert.False(handle.IsInvalid, $"open {path}: {Marshal.GetLastPInvokeError()}");
                pending.Add(handle);
                byte delete = 1;
                Assert.True(SetInfo(handle, 4 /* FileDispositionInfo */, &delete, 1), $"pend delete: {Marshal.GetLastPInvokeError()}");
            }
            // Control: still listed, yet even a FILE_READ_ATTRIBUTES open is refused while the delete is pending.
            Assert.Equal(3, Directory.EnumerateFiles(root).Count());
            using (var probe = CreateFile(compressed, 0x80, 7, 0, 3, 0, 0))
                Assert.True(probe.IsInvalid && Marshal.GetLastPInvokeError() == 5, "A delete-pending file must refuse a metadata-only open.");

            var cases = new List<(FileAttributes Set, FileAttributes Clear, string[] Expected)>
            {
                (FileAttributes.Compressed, 0, ["compressed.bin"]),
                (0, FileAttributes.Compressed, ["encrypted.bin", "plain.bin"]),
            };
            if (Efs)
            {
                cases.Add((FileAttributes.Encrypted, 0, ["encrypted.bin"]));
                cases.Add((0, FileAttributes.Encrypted, ["compressed.bin", "plain.bin"]));
            }
            foreach (var (set, clear, expected) in cases)
            {
                var (session, results) = Find(new SearchQuery { Roots = [root], AttributesSet = set, AttributesClear = clear });
                Assert.Equal(expected, Names(results));
                Assert.True(results.IsComplete, $"{set}/{clear}: " + string.Join("; ", session.Log.Select(e => e.Describe())));
                Assert.Empty(session.Log);
                Assert.Equal(0, session.FilesExamined);
            }
            TestContext.Current.TestOutputHelper?.WriteLine(Efs ? "Compression and EFS predicates verified." : "Compression predicates verified; set FILECAT_TEST_EFS=1 for the EFS half.");
        }
        finally
        {
            foreach (var handle in pending) handle.Dispose(); // the last close completes each pending delete
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// F-FIND: NTFS keeps a copy of a file's attributes in the directory entry of each of its names. This fixture
    /// compresses (and with FILECAT_TEST_EFS=1 encrypts) a file through one name, then records what the other name's
    /// folder listing reports before and after an open through that name. Find reads only listings, so it must agree
    /// with each folder's listing exactly; the test output records whether that listing was stale.
    /// </summary>
    [Fact]
    public void A_change_through_one_hard_link_is_found_exactly_as_each_folder_listing_reports()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native attribute fixture.");
        string root = UserTempRoot("filecat-find-links-");
        try
        {
            string first = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
            string second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
            var changes = new List<(string Name, FileAttributes Attribute, Action<string> Apply)> { ("compressed.bin", FileAttributes.Compressed, Compress) };
            if (Efs) changes.Add(("encrypted.bin", FileAttributes.Encrypted, File.Encrypt));
            foreach (var (name, attribute, apply) in changes)
            {
                string original = Path.Combine(first, name), alias = Path.Combine(second, name);
                File.WriteAllBytes(original, new byte[64 * 1024]);
                Assert.True(CreateHardLink(alias, original, 0), $"CreateHardLink: {Marshal.GetLastPInvokeError()}");
                apply(original);

                bool firstListed = Listed(first, name, attribute), secondListed = Listed(second, name, attribute);
                var (inFirst, firstResults) = Find(new SearchQuery { Roots = [first], AttributesSet = attribute });
                var (inSecond, secondResults) = Find(new SearchQuery { Roots = [second], AttributesSet = attribute });
                bool held = (Held(alias) & attribute) != 0; // opens the object through the second name
                bool secondAfterOpen = Listed(second, name, attribute);
                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"{attribute} set through first\\{name}: held object {held}; first listing {firstListed}; second listing {secondListed}, " +
                    $"after an open through it {secondAfterOpen}; Find over first {firstResults.Count == 1}, over second {secondResults.Count == 1}.");

                Assert.True(held, $"The {attribute} change did not reach the object.");
                Assert.Equal(firstListed ? [name] : Array.Empty<string>(), Names(firstResults));
                Assert.Equal(secondListed ? [name] : Array.Empty<string>(), Names(secondResults));
                Assert.True(firstResults.IsComplete && secondResults.IsComplete);
                Assert.Equal(0, inFirst.FilesExamined + inSecond.FilesExamined);
            }
        }
        finally { Directory.Delete(root, recursive: true); }

        static bool Listed(string folder, string name, FileAttributes attribute) =>
            (Assert.Single(new DirectoryInfo(folder).EnumerateFiles(name)).Attributes & attribute) != 0;
    }

    /// <summary>The attributes NTFS keeps on the object itself, through a metadata-only handle on this name.</summary>
    private static unsafe FileAttributes Held(string path)
    {
        using var handle = CreateFile(path, 0x80 /* READ_ATTRIBUTES */, 7, 0, 3, 0x00200000 /* OPEN_REPARSE_POINT */, 0);
        Assert.False(handle.IsInvalid, $"open {path}: {Marshal.GetLastPInvokeError()}");
        byte* basic = stackalloc byte[40];
        Assert.True(GetInfo(handle, 0 /* FileBasicInfo */, basic, 40), $"FileBasicInfo: {Marshal.GetLastPInvokeError()}");
        return (FileAttributes)(*(uint*)(basic + 32));
    }

    private sealed class FolderHook(Action<string> onFile) : IArchiveMembers
    {
        public bool IsArchive(string fileName)
        {
            onFile(fileName);
            return false;
        }
        public IEnumerable<ItemRef> List(string archivePath, CancellationToken ct) => throw new InvalidOperationException("No archive is listed.");
    }

    /// <summary>
    /// F-FIND: a folder that disappears after its parent was listed is not searched. Find logs it as inaccessible,
    /// keeps the other matches and leaves the result incomplete. Files are examined before subfolders, so the archive
    /// probe of a file in the parent is a deterministic point to remove it.
    /// </summary>
    [Fact]
    public void A_folder_that_vanishes_after_its_parent_was_listed_leaves_the_attribute_search_incomplete()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native attribute fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-find-vanished-").FullName;
        try
        {
            string gone = Directory.CreateDirectory(Path.Combine(root, "gone")).FullName;
            string kept = Directory.CreateDirectory(Path.Combine(root, "kept")).FullName;
            foreach (string path in new[] { Path.Combine(root, "trigger.bin"), Path.Combine(gone, "lost.bin"), Path.Combine(kept, "keep.bin") })
            {
                File.WriteAllText(path, "x");
                File.SetAttributes(path, FileAttributes.Archive);
            }
            var probed = new List<string>();
            var hook = new FolderHook(name =>
            {
                probed.Add(name);
                if (name == "trigger.bin") Directory.Delete(gone, recursive: true);
            });
            var (session, results) = Find(new SearchQuery
                { Roots = [root], AttributesSet = FileAttributes.Archive, AttributesClear = FileAttributes.Directory, Archives = hook });
            Assert.Equal(["keep.bin", "trigger.bin"], Names(results));
            Assert.Equal(["trigger.bin", "keep.bin"], probed);
            Assert.False(results.IsComplete);
            var entry = Assert.Single(session.Log);
            Assert.Equal(SearchLogKind.Inaccessible, entry.Kind);
            Assert.Equal(gone, entry.Path, ignoreCase: true);
            Assert.Empty(session.DeniedFolders);
            Assert.Contains(results.Issues, issue => issue.StartsWith("Not searched (inaccessible): ", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>
    /// F-FIND: a folder whose listing is denied is logged as inaccessible, offered for an explicit retry with its root
    /// and depth, and leaves the attribute search incomplete while the other matches remain.
    /// </summary>
    [Fact]
    public void A_folder_that_refuses_listing_is_logged_offered_for_retry_and_leaves_the_search_incomplete()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native attribute fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-find-denied-").FullName;
        string closed = Path.Combine(root, "closed");
        DirectorySecurity? original = null;
        try
        {
            Directory.CreateDirectory(closed);
            string open = Directory.CreateDirectory(Path.Combine(root, "open")).FullName;
            foreach (string path in new[] { Path.Combine(closed, "inside.bin"), Path.Combine(open, "visible.bin") })
            {
                File.WriteAllText(path, "x");
                File.SetAttributes(path, FileAttributes.Archive);
            }
            var info = new DirectoryInfo(closed);
            original = info.GetAccessControl();
            var denied = info.GetAccessControl();
            using (var user = WindowsIdentity.GetCurrent())
                denied.AddAccessRule(new FileSystemAccessRule(user.User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
            info.SetAccessControl(denied);
            var control = Record.Exception(() => Directory.EnumerateFileSystemEntries(closed).ToList());
            Assert.SkipWhen(control is null, "This process lists the folder despite the deny entry (an enabled backup privilege bypasses it).");
            Assert.IsType<UnauthorizedAccessException>(control);

            var (session, results) = Find(new SearchQuery { Roots = [root], AttributesSet = FileAttributes.Archive, AttributesClear = FileAttributes.Directory });
            Assert.Equal(["visible.bin"], Names(results));
            Assert.False(results.IsComplete);
            var retry = Assert.Single(session.DeniedFolders);
            Assert.Equal((root, closed, 1), (retry.Root, retry.Folder, retry.Depth), new PathTupleComparer());
            Assert.Equal(closed, Assert.Single(session.Inaccessible), ignoreCase: true);
        }
        finally
        {
            if (original is not null)
            {
                var restored = new DirectorySecurity();
                restored.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
                new DirectoryInfo(closed).SetAccessControl(restored);
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class PathTupleComparer : IEqualityComparer<(string, string, int)>
    {
        public bool Equals((string, string, int) x, (string, string, int) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase) && x.Item3 == y.Item3;
        public int GetHashCode((string, string, int) value) => value.Item3;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool Control(SafeFileHandle file, uint code, void* input, uint inputLength, void* output, uint outputLength, out uint returned, nint overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetInfo(SafeFileHandle file, int infoClass, void* buffer, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetInfo(SafeFileHandle file, int infoClass, void* buffer, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string name, string existing, nint security);
}
