using System.Runtime.InteropServices;
using FileCat.App.Services;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

// Remaining-work ledger rows 4 and 8: the application composes its edit store through the native storage gate.
public sealed class EditStoreCompositionTests
{
    private static string Fixture()
    {
        string parent = Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT") ?? Path.GetTempPath());
        // macOS: the temporary folder is reached through /var, a link; the native leases bind the real path.
        if (OperatingSystem.IsMacOS() && parent.StartsWith("/var/", StringComparison.Ordinal)) parent = "/private" + parent;
        string fixture = Directory.CreateDirectory(Path.Combine(parent, "filecat-edit-store-" + Guid.NewGuid().ToString("N"))).FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fixture, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return fixture;
    }

    [Fact]
    public void Ordinary_startup_composes_native_storage_where_listed_and_the_legacy_store_elsewhere()
    {
        string fixture = Fixture();
        try
        {
            var paths = AppPaths.Resolve(overrideRoot: fixture);
            var store = AppServices.ProductionEditSessions(paths, new PortableFileOperations());
            Assert.Equal(paths.EditSessionsDirectory, store.Root);
            // Listed for this runtime and the fixture's file system, and its folder held by the platform's own lease without
            // following links (which an elevated process, root, or a folder reached through a link cannot): native storage
            // beside the legacy folder. Otherwise the legacy store.
            bool native = NativeEditStorage.IsQualified(NativeEditStorage.Qualified, AppServices.RouteRuntime(),
                AppServices.FileSystemOf(paths.LocalDirectory)) && Holdable(paths.LocalDirectory);
            Assert.Equal(native ? paths.NativeEditSessionsDirectory : store.Root, store.NativeRoot);
            Assert.Equal(native, store.SupportsRetainedCaptureRemoval);
            Assert.False(Directory.Exists(paths.NativeEditSessionsDirectory)); // Created on first use only.
            Assert.Contains(paths.WriteFolders, folder => folder.Folder == paths.NativeEditSessionsDirectory);
        }
        finally { Directory.Delete(fixture, true); }
    }

    private static bool Holdable(string folder)
    {
        try
        {
            using EditDirectoryLease lease = OperatingSystem.IsWindows()
                ? FileCat.Platform.Windows.Archives.WindowsEditDirectoryLease.OpenRoot(folder, false, TestContext.Current.CancellationToken)
                : UnixEditDirectoryLease.OpenRoot(folder, false, TestContext.Current.CancellationToken);
            return lease.Path == folder;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.ComponentModel.Win32Exception) { return false; }
    }

    // A qualified runtime: the platform's own private leases keep new edits in the native root, while an edit made before
    // stays in the legacy folder with its own commit route, and both load and are found.
    [Fact]
    public void A_qualified_runtime_creates_new_edits_in_native_storage_beside_the_legacy_folder()
    {
        if (Environment.IsPrivilegedProcess) Assert.Skip("Edit sessions refuse an elevated token; run unelevated.");
        var ct = TestContext.Current.CancellationToken;
        string fixture = Fixture();
        try
        {
            var paths = AppPaths.Resolve(overrideRoot: fixture);
            string? fileSystem = AppServices.FileSystemOf(paths.LocalDirectory);
            Assert.SkipWhen(fileSystem is null, "The fixture's filesystem cannot be established here.");
            var fs = new PortableFileOperations();
            using var oldContent = new MemoryContentSource("old.txt", "edit made before"u8.ToArray());
            var old = new EditSessionStore(paths.EditSessionsDirectory, fs)
                .CreateRemote("server", "server", "/old.txt", oldContent, oldContent.GetRevision()!.Value, null, ct);

            var store = AppServices.ComposeEditSessions([new(RuntimeInformation.RuntimeIdentifier, fileSystem!, "test")], paths, fs);
            Assert.Equal(paths.EditSessionsDirectory, store.Root);
            Assert.Equal(paths.NativeEditSessionsDirectory, store.NativeRoot);
            Assert.True(store.SupportsRetainedCaptureRemoval);

            using var newContent = new MemoryContentSource("new.txt", "edit made now"u8.ToArray());
            var created = store.CreateRemote("server", "server", "/new.txt", newContent, newContent.GetRevision()!.Value, null, ct);
            Assert.Equal(EditSessionRecord.NativeStorageVersion, created.Version);
            Assert.StartsWith(paths.NativeEditSessionsDirectory + Path.DirectorySeparatorChar, created.WorkingPath, StringComparison.Ordinal);
            Assert.Equal("edit made now", File.ReadAllText(created.WorkingPath));

            var all = store.LoadAll();
            Assert.Contains(all, row => row.Id == old.Id && row.IsSupported && row.Version != EditSessionRecord.NativeStorageVersion);
            Assert.Contains(all, row => row.Id == created.Id && row.IsSupported);
            Assert.Equal(old.Id, store.FindRemote("server", "/old.txt")!.Id);
            Assert.Equal(created.Id, store.FindRemote("server", "/new.txt")!.Id);
            Assert.StartsWith(paths.EditSessionsDirectory + Path.DirectorySeparatorChar, old.WorkingPath, StringComparison.Ordinal);
        }
        finally { Directory.Delete(fixture, true); }
    }
}
