using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// F-ENUM / N-ENUM runtime proof of "no per-entry opens": a temporary Cloud Files provider is told about every open of
/// its placeholders. Listing the sync root through the native tag-bearing route and the managed route must cause none,
/// while one explicit metadata open (the positive control) is reported. No data is requested either way.
/// </summary>
public sealed partial class WindowsFileRecordsTests
{
    private static int _placeholderOpens;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void PlaceholderOpened(nint info, nint parameters) => Interlocked.Increment(ref _placeholderOpens);

    private sealed class CountingSink : IEnumerationSink
    {
        public readonly Dictionary<string, EntryData> Entries = new(StringComparer.Ordinal);
        public void AddBatch(ReadOnlySpan<EntryData> entries) { foreach (var entry in entries) Entries[entry.Name] = entry; }
        public void ReportIssue(string message) => Assert.Fail(message);
    }

    [Fact]
    public async Task Listing_a_sync_root_opens_none_of_its_placeholders_on_either_route()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Cloud Files are Windows'.");
        if (!Opted("FILECAT_TEST_CLOUD_FILES")) Assert.Skip("Set FILECAT_TEST_CLOUD_FILES=1 to register a temporary Cloud Files sync root in a test folder.");
        string root = NewFolder();
        bool registered = false, connected = false;
        long connection = 0;
        int fetchesBefore = Volatile.Read(ref _fetches);
        const int count = 40;
        nint table = 0;
        try
        {
            if (!OnNtfs(root)) Assert.Skip("The test folder is not on NTFS.");
            (registered, connected, connection, table) = RegisterCountingProvider(root, count);
            int opensBefore = Volatile.Read(ref _placeholderOpens);
            var location = Location.FileSystem(root);
            var managed = new CountingSink(); var native = new CountingSink();
            await new LocalFileSystemProvider().EnumerateAsync(location, managed, TestContext.Current.CancellationToken);
            await new WindowsFileSystemProvider { UseNativeEnumeration = true }.EnumerateAsync(location, native, TestContext.Current.CancellationToken);
            Assert.Equal(count, native.Entries.Count);
            Assert.Equal(managed.Entries.Keys.Order(StringComparer.Ordinal), native.Entries.Keys.Order(StringComparer.Ordinal));
            Assert.All(native.Entries.Values, entry => Assert.True(ReparsePresentation.IsCloudTag(entry.NativeReparseTag),
                $"{entry.Name} carries tag 0x{entry.NativeReparseTag:X8}"));
            Assert.All(native.Entries, pair => Assert.Equal(managed.Entries[pair.Key].Attributes, pair.Value.Attributes));
            int listingOpens = Volatile.Read(ref _placeholderOpens) - opensBefore;
            Assert.Equal(0, listingOpens);
            Assert.Equal(fetchesBefore, Volatile.Read(ref _fetches));
            // Positive controls: an explicit open must reach the provider, or the zero above proves nothing. Not every build
            // reports a metadata-only open (26100 in CI and 26340 reported no no-follow one), so the controls step from
            // metadata-only opens to an open for data access without a read; each uses its own placeholder.
            (string Kind, uint Access, uint Flags)[] controls =
            [
                ("metadata-only no-follow", 0x0010_0080, 0x0220_0000),
                ("metadata-only", 0x0010_0080, 0x0200_0000),
                ("data access without a read", 0x0012_0089, 0x0200_0000),
            ];
            int observed = -1;
            for (int i = 0; i < controls.Length && observed < 0; i++)
            {
                int seen = Volatile.Read(ref _placeholderOpens);
                using (var probe = CreateFile(Path.Combine(root, $"placeholder-{i}.txt"), controls[i].Access, 7, 0, 3, controls[i].Flags, 0))
                    Assert.False(probe.IsInvalid, $"Open the {controls[i].Kind} control: {Marshal.GetLastPInvokeError()}");
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (Volatile.Read(ref _placeholderOpens) == seen && DateTime.UtcNow < deadline) await Task.Delay(50);
                if (Volatile.Read(ref _placeholderOpens) != seen) observed = i;
            }
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Placeholder opens: listing {listingOpens}; first control the provider observed: {(observed < 0 ? "none" : controls[observed].Kind)}");
            if (observed < 0)
                Assert.Skip("No control open reached the provider on this Windows build, so it cannot show whether listing opened placeholders.");
            if (observed == 2)
                Assert.Skip("This Windows build reports only data-access opens to the provider. Listing caused none, but metadata-only opens are unobservable here.");
        }
        finally
        {
            try { for (int i = 0; i < count; i++) { string file = Path.Combine(root, $"placeholder-{i}.txt"); if (File.Exists(file)) File.Delete(file); } }
            finally
            {
                if (connected) CfDisconnectSyncRoot(connection);
                if (registered) CfUnregisterSyncRoot(root);
                if (table != 0) FreeTable(table);
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static unsafe void FreeTable(nint table) => NativeMemory.Free((void*)table);

    private static unsafe (bool Registered, bool Connected, long Connection, nint Table) RegisterCountingProvider(string root, int count)
    {
        bool registered = false;
        fixed (char* name = "FileCat enumeration test")
        fixed (char* version = "1.0")
        fixed (byte* rootIdentity = "filecat-enumeration-root"u8)
        {
            var registration = new CfSyncRegistration
            {
                StructSize = (uint)sizeof(CfSyncRegistration), ProviderName = (nint)name, ProviderVersion = (nint)version,
                SyncRootIdentity = (nint)rootIdentity, SyncRootIdentityLength = 24, ProviderId = Guid.NewGuid(),
            };
            var policies = new CfSyncPolicies { StructSize = (uint)sizeof(CfSyncPolicies), HydrationPrimary = 2, PopulationPrimary = 3 };
            int result = CfRegisterSyncRoot(root, &registration, &policies, 0x2 | 0x4);
            Assert.True(result >= 0, $"CfRegisterSyncRoot: 0x{result:X8}");
            registered = true;
        }
        var table = (CfCallbackRegistration*)NativeMemory.AllocZeroed((nuint)(3 * sizeof(CfCallbackRegistration)));
        table[0] = new CfCallbackRegistration { Type = 0 /* FETCH_DATA */, Callback = (nint)(delegate* unmanaged[Stdcall]<nint, nint, void>)&Fetched };
        table[1] = new CfCallbackRegistration { Type = 5 /* NOTIFY_FILE_OPEN_COMPLETION */, Callback = (nint)(delegate* unmanaged[Stdcall]<nint, nint, void>)&PlaceholderOpened };
        table[2] = new CfCallbackRegistration { Type = -1 };
        int connect = CfConnectSyncRoot(root, table, 0, 0, out long connection);
        Assert.True(connect >= 0, $"CfConnectSyncRoot: 0x{connect:X8}");
        long now = DateTime.UtcNow.ToFileTimeUtc();
        var identity = "filecat-enumeration"u8.ToArray();
        for (int i = 0; i < count; i++)
        {
            fixed (char* relative = $"placeholder-{i}.txt")
            fixed (byte* id = identity)
            {
                var create = new CfPlaceholderCreateInfo
                {
                    RelativeFileName = (nint)relative, CreationTime = now, LastAccessTime = now, LastWriteTime = now, ChangeTime = now,
                    FileAttributes = 0x80, FileSize = 4096, FileIdentity = (nint)id, FileIdentityLength = (uint)identity.Length, Flags = 0x2,
                };
                int made = CfCreatePlaceholders(root, &create, 1, 0, out uint processed);
                Assert.True(made >= 0 && processed == 1 && create.Result >= 0, $"CfCreatePlaceholders: 0x{made:X8}");
            }
        }
        return (registered, true, connection, (nint)table);
    }
}
