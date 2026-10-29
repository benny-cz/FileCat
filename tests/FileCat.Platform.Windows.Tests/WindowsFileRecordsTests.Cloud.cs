using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FileCat.Core.Records;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// D-56: a Cloud Files placeholder's state, read without downloading it. The test registers a sync root of its own in a
/// temporary folder (a provider that serves no data, and counts every request for it), makes a placeholder there, reads
/// its record, and checks the placeholder stayed as it was; it unregisters the root at the end.
/// </summary>
public sealed partial class WindowsFileRecordsTests
{
    private const uint RecallOnDataAccess = 0x0040_0000;
    private static int _fetches;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void Fetched(nint info, nint parameters) => Interlocked.Increment(ref _fetches);

    [Fact]
    public unsafe void A_cloud_placeholder_reads_its_state_without_being_downloaded()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Cloud Files are Windows'.");
        // Registering a sync root writes this user's sync-root registration (removed at the end): only where that is wanted.
        if (!Opted("FILECAT_TEST_CLOUD_FILES")) Assert.Skip("Set FILECAT_TEST_CLOUD_FILES=1 to register a temporary Cloud Files sync root in a test folder.");
        string root = NewFolder();
        var identity = "filecat-identity-1"u8.ToArray();
        bool registered = false, connected = false;
        long connection = 0;
        var table = (CfCallbackRegistration*)NativeMemory.AllocZeroed((nuint)(2 * sizeof(CfCallbackRegistration)));
        string file = Path.Combine(root, "cloud.txt");
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
                // Full downloads, folders always complete (no listing is ever asked of the provider), in-sync kept as set.
                var policies = new CfSyncPolicies { StructSize = (uint)sizeof(CfSyncPolicies), HydrationPrimary = 2, PopulationPrimary = 3 };
                int registeredResult = CfRegisterSyncRoot(root, &registration, &policies, 0x2 | 0x4 /* no population on the root, root in sync */);
                Assert.True(registeredResult >= 0, $"CfRegisterSyncRoot: 0x{registeredResult:X8}");
                registered = true;
            }
            table[0] = new CfCallbackRegistration { Type = 0 /* CF_CALLBACK_TYPE_FETCH_DATA */, Callback = (nint)(delegate* unmanaged[Stdcall]<nint, nint, void>)&Fetched };
            table[1] = new CfCallbackRegistration { Type = -1 /* CF_CALLBACK_TYPE_NONE: the end */ };
            int connect = CfConnectSyncRoot(root, table, 0, 0, out connection);
            Assert.True(connect >= 0, $"CfConnectSyncRoot: 0x{connect:X8}");
            connected = true;

            // A 5,000-byte file that is only in the cloud, marked in sync.
            long now = DateTime.UtcNow.ToFileTimeUtc();
            fixed (char* relative = "cloud.txt")
            fixed (byte* id = identity)
            {
                var create = new CfPlaceholderCreateInfo
                {
                    RelativeFileName = (nint)relative, CreationTime = now, LastAccessTime = now, LastWriteTime = now, ChangeTime = now,
                    FileAttributes = 0x80, FileSize = 5000, FileIdentity = (nint)id, FileIdentityLength = (uint)identity.Length, Flags = 0x2 /* mark in sync */,
                };
                int made = CfCreatePlaceholders(root, &create, 1, 0, out uint processed);
                Assert.True(made >= 0 && processed == 1 && create.Result >= 0, $"CfCreatePlaceholders: 0x{made:X8}, {processed} made, 0x{create.Result:X8}");
            }
            var (record, sequence) = Reference(file);
            var (rootRecord, rootSequence) = Reference(root);
            int fetchesBefore = Volatile.Read(ref _fetches);

            bool[] modes = Environment.IsPrivilegedProcess ? [false, true] : [false];
            foreach (bool privileged in modes)
            {
                var report = Read(file, privileged);
                TestContext.Current.TestOutputHelper?.WriteLine(Excerpt(report, "Item", "Reparse point", "Cloud placeholder", "Layout on disk"));
                Assert.StartsWith("0x9000", Field(Section(report, "Reparse point"), "Tag"), StringComparison.Ordinal);
                var cloud = Section(report, "Cloud placeholder (Cloud Files)");
                Assert.Equal($"{RecordText.Bytes(0)} of {RecordText.Bytes(5000)}: none of it (only in the cloud)", Field(cloud, "On this device"));
                Assert.Equal(CloudPlaceholderInfo.PinStateText(0), Field(cloud, "Pin state"));
                Assert.Equal("yes", Field(cloud, "In sync"));
                Assert.EndsWith("(its own)", Field(cloud, "File ID"), StringComparison.Ordinal);
                Assert.StartsWith($"0x{(ulong)sequence << 48 | (ulong)record:X16} ", Field(cloud, "File ID"), StringComparison.Ordinal);
                Assert.EndsWith($"· file ID 0x{(ulong)rootSequence << 48 | (ulong)rootRecord:X16}", Field(cloud, "Sync root"), StringComparison.Ordinal);
                Assert.StartsWith("FileCat test 1.0 · ", Field(cloud, "Provider"), StringComparison.Ordinal);
                Assert.StartsWith("full (", Field(cloud, "Downloads"), StringComparison.Ordinal);
                Assert.StartsWith("always full (", Field(cloud, "Folders"), StringComparison.Ordinal);
                Assert.Equal($"{identity.Length} bytes: its provider's own name for it (below)", Field(cloud, "Its identity"));
                Assert.Contains(cloud.Lines, l => l.Contains("filecat-identity", StringComparison.Ordinal));
                Assert.DoesNotContain(report.Warnings, w => w.StartsWith("Cloud Files", StringComparison.Ordinal));
            }

            // It stayed a placeholder with nothing downloaded, and its provider was never asked for data.
            using (var handle = CreateFile(file, 0x0010_0080, 7, 0, 3, 0x0220_0000, 0))
            {
                var problems = new List<string>();
                var after = WindowsFileRecords.PlaceholderInfo(handle, problems, out string? failed);
                Assert.True(after is not null, failed);
                Assert.Equal((0L, 0, 1), (after.OnDiskDataSize, after.PinState, after.InSyncState));
            }
            Assert.True(((uint)File.GetAttributes(file) & RecallOnDataAccess) != 0, $"attributes 0x{(uint)File.GetAttributes(file):X8}");
            Assert.Equal(fetchesBefore, Volatile.Read(ref _fetches));
            Assert.Equal(0, Volatile.Read(ref _fetches));
        }
        finally
        {
            // The placeholder first, while its provider is connected; then the root's registration, then the folder.
            try { if (File.Exists(file)) File.Delete(file); }
            finally
            {
                if (connected) CfDisconnectSyncRoot(connection);
                if (registered) CfUnregisterSyncRoot(root);
                NativeMemory.Free(table);
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CfSyncRegistration
    {
        public uint StructSize;
        public nint ProviderName, ProviderVersion, SyncRootIdentity;
        public uint SyncRootIdentityLength;
        public nint FileIdentity;
        public uint FileIdentityLength;
        public Guid ProviderId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CfSyncPolicies
    {
        public uint StructSize;
        public ushort HydrationPrimary, HydrationModifier, PopulationPrimary, PopulationModifier;
        public uint InSync, HardLink, PlaceholderManagement;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CfCallbackRegistration
    {
        public int Type;
        public nint Callback;
    }

    /// <summary>CF_PLACEHOLDER_CREATE_INFO: the name, CF_FS_METADATA (FILE_BASIC_INFO and the size), the identity, flags, result, USN.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CfPlaceholderCreateInfo
    {
        public nint RelativeFileName;
        public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
        public uint FileAttributes;
        public long FileSize;
        public nint FileIdentity;
        public uint FileIdentityLength, Flags;
        public int Result;
        public long CreateUsn;
    }

    [LibraryImport("cldapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int CfRegisterSyncRoot(string path, CfSyncRegistration* registration, CfSyncPolicies* policies, uint flags);

    [LibraryImport("cldapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int CfConnectSyncRoot(string path, CfCallbackRegistration* table, nint context, uint flags, out long connection);

    [LibraryImport("cldapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int CfCreatePlaceholders(string directory, CfPlaceholderCreateInfo* placeholders, uint count, uint flags, out uint processed);

    [LibraryImport("cldapi.dll")]
    private static partial int CfDisconnectSyncRoot(long connection);

    [LibraryImport("cldapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CfUnregisterSyncRoot(string path);
}
