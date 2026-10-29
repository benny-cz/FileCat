using System.Runtime.InteropServices;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsFileRecordsTests
{
    /// <summary>
    /// A Cloud Files sync root, and the placeholder folders in it, are reparse points but no links (review finding F-H3 of
    /// f134ba37): a OneDrive folder is copied into, read for icons and deleted like any other, elevated or not.
    /// </summary>
    [Fact]
    public unsafe void A_cloud_sync_root_and_its_placeholder_folders_are_folders_not_links()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Cloud Files are Windows'.");
        // Registering a sync root writes this user's sync-root registration (removed at the end): only where that is wanted.
        if (!Opted("FILECAT_TEST_CLOUD_FILES")) Assert.Skip("Set FILECAT_TEST_CLOUD_FILES=1 to register a temporary Cloud Files sync root in a test folder.");
        string root = NewFolder();
        bool registered = false, connected = false;
        long connection = 0;
        var table = (CfCallbackRegistration*)NativeMemory.AllocZeroed((nuint)(2 * sizeof(CfCallbackRegistration)));
        try
        {
            if (!OnNtfs(root)) Assert.Skip("The test folder is not on NTFS.");
            fixed (char* name = "FileCat test")
            fixed (char* version = "1.0")
            fixed (byte* rootIdentity = "filecat-sync-root"u8)
            {
                var registration = new CfSyncRegistration
                {
                    StructSize = (uint)sizeof(CfSyncRegistration), ProviderName = (nint)name, ProviderVersion = (nint)version,
                    SyncRootIdentity = (nint)rootIdentity, SyncRootIdentityLength = 17, ProviderId = Guid.NewGuid(),
                };
                var policies = new CfSyncPolicies { StructSize = (uint)sizeof(CfSyncPolicies), HydrationPrimary = 2, PopulationPrimary = 3 };
                int registeredResult = CfRegisterSyncRoot(root, &registration, &policies, 0x2 | 0x4);
                Assert.True(registeredResult >= 0, $"CfRegisterSyncRoot: 0x{registeredResult:X8}");
                registered = true;
            }
            table[0] = new CfCallbackRegistration { Type = 0, Callback = (nint)(delegate* unmanaged[Stdcall]<nint, nint, void>)&Fetched };
            table[1] = new CfCallbackRegistration { Type = -1 };
            int connect = CfConnectSyncRoot(root, table, 0, 0, out connection);
            Assert.True(connect >= 0, $"CfConnectSyncRoot: 0x{connect:X8}");
            connected = true;

            // A placeholder folder, complete (never populated on demand) and in sync, with an ordinary file put in it.
            long now = DateTime.UtcNow.ToFileTimeUtc();
            fixed (char* relative = "photos")
            fixed (byte* id = "filecat-folder"u8)
            {
                var create = new CfPlaceholderCreateInfo
                {
                    RelativeFileName = (nint)relative, CreationTime = now, LastAccessTime = now, LastWriteTime = now, ChangeTime = now,
                    FileAttributes = 0x10, FileSize = 0, FileIdentity = (nint)id, FileIdentityLength = 14, Flags = 0x1 | 0x2,
                };
                int made = CfCreatePlaceholders(root, &create, 1, 0, out uint processed);
                Assert.True(made >= 0 && processed == 1 && create.Result >= 0, $"CfCreatePlaceholders: 0x{made:X8}, {processed} made, 0x{create.Result:X8}");
            }
            string photos = Path.Combine(root, "photos");
            Assert.True((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0, "The sync root is a reparse point.");
            Assert.True((File.GetAttributes(photos) & FileAttributes.ReparsePoint) != 0, "The placeholder folder is a reparse point.");
            File.WriteAllBytes(Path.Combine(photos, "pic.ico"), [0, 0, 1, 0]);

            var ops = new WindowsFileOperations();
            using (ops.HoldDirectory(photos)) { }
            using (var lease = LocalIconFileLease.TryOpen(Path.Combine(photos, "pic.ico"))) Assert.NotNull(lease);
            var deletion = ops.DeleteTreeBound(photos, () => { });
            Assert.NotNull(deletion);
            Assert.Equal(0, deletion.Failed);
            Assert.False(Directory.Exists(photos));
        }
        finally
        {
            if (connected) CfDisconnectSyncRoot(connection);
            if (registered) CfUnregisterSyncRoot(root);
            NativeMemory.Free(table);
            Directory.Delete(root, recursive: true);
        }
    }
}
