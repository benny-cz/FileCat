using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    // G-BIND, O_SEARCH ancestor walk: the reader and the parent-relative writer reach a document through an ancestor that
    // grants search but not listing, without reading that directory.
    [Fact]
    public async Task Darwin_writer_reaches_a_document_through_a_search_only_ancestor()
    {
        RequireDarwinWriter();
        if (Environment.IsPrivilegedProcess) Assert.Skip("Requires ordinary macOS credentials.");
        using var fixture = new Fixture(nativeJournal: true);
        string searchOnly = Directory.CreateDirectory(Path.Join(fixture.Root, "search-only")).FullName;
        string inner = Directory.CreateDirectory(Path.Join(searchOnly, "inner")).FullName;
        var item = new ItemRef(Location.FileSystem(inner), "document", EntryKind.File);
        string path = item.FileSystemPath!;
        File.WriteAllText(path, "fixture");
        File.SetUnixFileMode(path, (UnixFileMode)0x1a0);
        File.SetUnixFileMode(inner, (UnixFileMode)0x1c0);
        File.SetUnixFileMode(searchOnly, (UnixFileMode)0x40); // search only: no listing, no new names
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => Directory.GetFileSystemEntries(searchOnly));
            var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
                PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            CancellationToken ct = TestContext.Current.CancellationToken;
            using var inspection = new DarwinPermissionService(qualifyParentWrites: true).Inspect(subject, DarwinSections, ct);
            Assert.True(inspection.Snapshot.Mode.Status == PermissionReadStatus.Present, inspection.Snapshot.Mode.Reason ?? inspection.Snapshot.BindingProblem);
            Assert.Equal(DarwinBindingRoute.ParentRelative, inspection.Snapshot.Binding?.DarwinRoute);
            using var review = DarwinPermissionService.Prepare(inspection, item,
                new(PermissionScope.Objects, [new ChangePermissionMode(0x180, 0xfff)]), false, ct).Review;
            var job = fixture.Manager.SubmitPermissions(review);
            await fixture.Finish(job);
            var result = Assert.Single(job.PermissionResults);
            Assert.True(result.Verdict == PermissionVerdict.Verified, $"{result.Verdict}: {result.Detail}");
            Assert.Equal((UnixFileMode)0x40, File.GetUnixFileMode(searchOnly));
        }
        finally { File.SetUnixFileMode(searchOnly, (UnixFileMode)0x1c0); }
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(path));
    }

    // G-BIND, firmlink crossing: a home document reached through the /Users firmlink and through its data-volume path
    // binds to one object on the data volume, and a reviewed change made through the firmlinked path is verified there.
    [Fact]
    public async Task Darwin_firmlinked_and_data_volume_paths_bind_one_object()
    {
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_FIRMLINK") != "1" ||
            Environment.IsPrivilegedProcess)
            Assert.Skip("Requires an ordinary macOS session with the firmlink check enabled.");
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.StartsWith("/Users/", home, StringComparison.Ordinal);
        string folder = Directory.CreateDirectory(Path.Join(home, ".filecat-firmlink-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        try
        {
            File.SetUnixFileMode(folder, (UnixFileMode)0x1c0);
            string path = Path.Join(folder, "document"), dataPath = "/System/Volumes/Data" + path;
            File.WriteAllText(path, "fixture");
            File.SetUnixFileMode(path, (UnixFileMode)0x1a0);
            Assert.True(File.Exists(dataPath));
            CancellationToken ct = TestContext.Current.CancellationToken;
            var service = new DarwinPermissionService(qualifyParentWrites: true);
            var item = ItemRef.ForFileSystemPath(path, EntryKind.File);
            PermissionSubject Subject(string locator) => new(Location.FileSystem(Path.GetDirectoryName(locator)!), locator, locator,
                new(PermissionAuthorityKind.Local, "fixture", 0), PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            using var throughFirmlink = service.Inspect(Subject(path), DarwinSections, ct);
            using var throughData = service.Inspect(Subject(dataPath), DarwinSections, ct);
            var firmlinked = throughFirmlink.Snapshot.Binding;
            var data = throughData.Snapshot.Binding;
            Assert.True(firmlinked is not null, throughFirmlink.Snapshot.BindingProblem);
            Assert.True(data is not null, throughData.Snapshot.BindingProblem);
            Assert.Equal(data.Identity, firmlinked.Identity);
            Assert.Equal(data.MountRoot, firmlinked.MountRoot);
            Assert.Equal("/System/Volumes/Data", data.MountRoot.TrimEnd('/'));

            using var fixture = new Fixture(nativeJournal: true);
            using var review = DarwinPermissionService.Prepare(throughFirmlink, item,
                new(PermissionScope.Objects, [new ChangePermissionMode(0x180, 0xfff)]), false, ct).Review;
            var job = fixture.Manager.SubmitPermissions(review);
            await fixture.Finish(job);
            var result = Assert.Single(job.PermissionResults);
            Assert.True(result.Verdict == PermissionVerdict.Verified, $"{result.Verdict}: {result.Detail}");
            Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(dataPath));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    // G-MAC same-user substitution after dispatch on the checked-parent lane: whatever replaced the reviewed name, the
    // read-back cannot attest the original object, so the result is uncertain and the substitute keeps its own mode. A
    // swap undone before the read-back (single-thread ABA) still reports the folder activity it caused.
    [Theory]
    [InlineData("rename-swap")]
    [InlineData("unlink-relink")]
    [InlineData("hard-link")]
    [InlineData("clonefile")]
    [InlineData("swap-and-back")]
    public void Darwin_parent_readback_reports_same_user_substitution_after_dispatch(string substitution)
    {
        RequireDarwinWriter();
        using var fixture = new Fixture();
        var (item, subject) = DarwinSubject(fixture);
        string path = item.FileSystemPath!, other = path + ".other", moved = path + ".moved";
        File.WriteAllText(other, "substitute");
        File.SetUnixFileMode(other, (UnixFileMode)0x180);
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var inspection = new DarwinPermissionService(qualifyParentWrites: true).Inspect(subject, DarwinSections, ct);
        using var review = DarwinPermissionService.Prepare(inspection, item,
            new(PermissionScope.Objects, [new ChangePermissionMode(0x1a0, 0xfff)]), false, ct).Review;
        var target = Assert.Single(review.Targets);
        var component = Assert.Single(target.Components);
        _ = target.Mutation.CheckCurrent(component.Before.Sections, ct);
        var call = target.Mutation.Apply(component.Change, ct);
        Assert.False(call.BindingConflict);
        Assert.Null(call.NativeError);
        string changed = moved;
        switch (substitution)
        {
            case "rename-swap":
                Swap(path, other);
                changed = other;
                break;
            case "unlink-relink":
                Native(Link(path, moved), "link");
                File.Delete(path);
                File.WriteAllText(path, "relinked");
                File.SetUnixFileMode(path, (UnixFileMode)0x180);
                break;
            case "hard-link":
                File.Move(path, moved);
                Native(Link(other, path), "link");
                break;
            case "clonefile":
                File.Move(path, moved);
                Native(CloneFile(other, path, 0), "clonefile");
                break;
            default:
                Swap(path, other);
                Swap(path, other);
                changed = path;
                break;
        }

        var observed = target.Mutation.Observe(component.Expected.Sections);
        Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(changed));
        if (substitution == "swap-and-back")
        {
            Assert.Equal(PermissionVerification.SameObject, observed.Verification);
            Assert.True(observed.FolderChanged, observed.Detail);
            return;
        }
        Assert.Equal(PermissionVerification.Unavailable, observed.Verification);
        Assert.Contains("may have reached another object", observed.Detail);
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(path));

        static void Swap(string first, string second) => Native(RenameSwap(first, second, 2 /* RENAME_SWAP */), "renamex_np");
        static void Native(int result, string call) =>
            Assert.True(result == 0, $"{call} failed with errno {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}.");
    }

    // G-MAC another account's private folder: an entry inside a folder that another account owns with mode 0700 cannot be
    // reached by the ordinary route, and inspection says it was denied instead of binding anything.
    [Fact]
    public void Darwin_inspection_inside_another_accounts_private_folder_is_denied_without_a_binding()
    {
        string folder = Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_FOREIGN_FOLDER") ?? string.Empty;
        if (!OperatingSystem.IsMacOS() || folder.Length == 0 || Environment.IsPrivilegedProcess)
            Assert.Skip("Requires a folder that another account owns with mode 0700.");
        string path = Path.Join(folder, "document");
        var item = ItemRef.ForFileSystemPath(path, EntryKind.File);
        var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        CancellationToken ct = TestContext.Current.CancellationToken;
        var service = new DarwinPermissionService(qualifyParentWrites: true, qualifyDescriptorWrites: true);
        using var inspection = service.Inspect(subject, DarwinSections, ct);
        Assert.Null(inspection.Snapshot.Binding);
        Assert.Equal(PermissionReadStatus.Denied, inspection.Snapshot.Mode.Status);
        Assert.Equal(13, inspection.Snapshot.Mode.NativeError);
        Assert.NotNull(DarwinPermissionService.ModeRefusal(inspection.Snapshot));
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "renamex_np", SetLastError = true)]
    private static extern int RenameSwap([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string from,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string to, uint flags);
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string existing,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string created);
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "clonefile", SetLastError = true)]
    private static extern int CloneFile([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string source,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string destination, int flags);
}
