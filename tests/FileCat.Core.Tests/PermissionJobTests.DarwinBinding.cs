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
}
