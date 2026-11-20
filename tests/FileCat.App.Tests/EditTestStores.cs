using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>Which edit store an application edit test runs against.</summary>
public enum EditStoreKind { Legacy, Native }

/// <summary>
/// Remaining-work ledger rows 4 and 8: the application's edit tests run against both stores explicitly, whatever this build
/// lists for ordinary startup. The legacy store keeps the sessions made before native storage and every place native
/// storage is not composed; native storage, beside the legacy folder, is the default where it is listed.
/// </summary>
internal static class EditTestStores
{
    internal static Func<AppPaths, IFileSystemOperations, EditSessionStore> Factory(EditStoreKind kind) => kind == EditStoreKind.Legacy
        ? (paths, fs) => new EditSessionStore(paths.EditSessionsDirectory, fs)
        : (paths, fs) => AppServices.ComposeEditSessions(
            [new(AppServices.RouteRuntime(), AppServices.FileSystemOf(paths.LocalDirectory) ?? "unknown", "test")], paths, fs);

    /// <summary>
    /// Set to 1 by qualification runs on a listed route: a native test that cannot hold native storage there fails instead
    /// of being skipped, so a composition regression cannot pass as skips.
    /// </summary>
    internal const string NativeRequiredVariable = "FILECAT_EDIT_NATIVE_REQUIRED";

    /// <summary>
    /// In a qualification run, a native test folder must be on a file system this build lists for this runtime (tests that
    /// make their own native store call this for its folder).
    /// </summary>
    internal static void RequireListedRoute(string folder)
    {
        if (Environment.GetEnvironmentVariable(NativeRequiredVariable) != "1") return;
        string runtime = AppServices.RouteRuntime();
        string? fileSystem = AppServices.FileSystemOf(folder);
        if (!NativeEditStorage.IsQualified(NativeEditStorage.Qualified, runtime, fileSystem))
            Assert.Fail($"The test folder's file system ({fileSystem ?? "unknown"}) is not a listed native edit storage route for {runtime}.");
    }

    /// <summary>A main window over the chosen store; a native one where this process can hold native storage, else skipped.</summary>
    internal static (AppServices Services, MainViewModel Vm, MainWindow Window, string Root) OpenMainWindow(EditStoreKind kind,
        Remote.Sftp.ISftpConnector? sftp = null, bool nativeWindowsOriginMarks = false)
    {
        bool required = Environment.GetEnvironmentVariable(NativeRequiredVariable) == "1";
        if (kind == EditStoreKind.Native && Environment.IsPrivilegedProcess)
        {
            const string elevated = "Native edit storage refuses an elevated or root process; run the tests as an ordinary user.";
            if (required) Assert.Fail(elevated); else Assert.Skip(elevated);
        }
        var opened = AccessibilityTests.OpenMainWindow(sftp, nativeWindowsOriginMarks, editSessionFactory: Factory(kind),
            realTemporaryPath: kind == EditStoreKind.Native);
        if (kind == EditStoreKind.Native && opened.Services.EditSessions.NativeRoot == opened.Services.EditSessions.Root)
        {
            AccessibilityTests.Close(opened.Services, opened.Window, opened.Root);
            const string unheld = "Native edit storage cannot be held for this test folder (an unknown file system or a folder reached through a link).";
            if (required) Assert.Fail(unheld); else Assert.Skip(unheld);
        }
        if (kind == EditStoreKind.Native && required)
        {
            // A qualification run proves a listed route: the test folder must be on a file system this build lists for
            // this runtime (the factory composes native storage on whichever file system holds it).
            try { RequireListedRoute(Path.GetDirectoryName(opened.Services.EditSessions.NativeRoot)!); }
            catch { AccessibilityTests.Close(opened.Services, opened.Window, opened.Root); throw; }
        }
        return opened;
    }
}
