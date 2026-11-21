using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// F-ENUM / N-ENUM runtime proof of "no per-entry opens": a temporary Cloud Files provider in this process is told about
/// every open of its placeholders. A child copy of the test assembly lists the sync root through the native tag-bearing
/// route and the managed route. Later children make one explicit open each (the positive controls). The listing and the
/// controls run in other processes because a provider may not be told about its own process's opens. Listing must cause
/// no open and no data request, while a control open must be reported, or the zero proves nothing.
/// </summary>
public sealed partial class WindowsFileRecordsTests
{
    private const string CloudListingRoot = "FILECAT_TEST_CLOUD_LISTING_ROOT";
    private const string CloudListingMode = "FILECAT_TEST_CLOUD_LISTING_MODE";
    private const int CloudListingCount = 40;
    private static int _placeholderOpens;

    // Ordered from what the claim needs to what the provider may be told: metadata-only opens first, then an open for
    // data access without a read. Each control uses its own placeholder.
    private static readonly (string Kind, uint Access, uint Flags)[] PlaceholderControls =
    [
        ("metadata-only no-follow", 0x0010_0080, 0x0220_0000),
        ("metadata-only", 0x0010_0080, 0x0200_0000),
        ("data access without a read", 0x0012_0089, 0x0200_0000),
    ];

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void PlaceholderOpened(nint info, nint parameters) => Interlocked.Increment(ref _placeholderOpens);

    private sealed class CountingSink : IEnumerationSink
    {
        public readonly Dictionary<string, EntryData> Entries = new(StringComparer.Ordinal);
        public void AddBatch(ReadOnlySpan<EntryData> entries) { foreach (var entry in entries) Entries[entry.Name] = entry; }
        public void ReportIssue(string message) => Assert.Fail(message);
    }

    [Fact]
    public async Task Cloud_listing_child_lists_or_opens_as_its_parent_asks()
    {
        string? root = Environment.GetEnvironmentVariable(CloudListingRoot), mode = Environment.GetEnvironmentVariable(CloudListingMode);
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(mode)) Assert.Skip("Started only by the Cloud Files listing parent fixture.");
        if (mode == "list")
        {
            var location = Location.FileSystem(root);
            var managed = new CountingSink(); var native = new CountingSink();
            await new LocalFileSystemProvider().EnumerateAsync(location, managed, TestContext.Current.CancellationToken);
            await new WindowsFileSystemProvider { UseNativeEnumeration = true }.EnumerateAsync(location, native, TestContext.Current.CancellationToken);
            Assert.Equal(CloudListingCount, native.Entries.Count);
            Assert.Equal(managed.Entries.Keys.Order(StringComparer.Ordinal), native.Entries.Keys.Order(StringComparer.Ordinal));
            Assert.All(native.Entries, pair => Assert.Equal(managed.Entries[pair.Key].Attributes, pair.Value.Attributes));
            // Windows may disguise another provider's placeholders as ordinary files for this process; record what it saw.
            var tags = native.Entries.Values.Select(entry => entry.NativeReparseTag).Distinct().Order().Select(tag => $"0x{tag:X8}");
            TestContext.Current.TestOutputHelper?.WriteLine($"Child: process placeholder mode {RtlQueryProcessPlaceholderCompatibilityMode()}; " +
                $"native tags {string.Join(',', tags)}; attributes 0x{(int)native.Entries.Values.First().Attributes:X8}");
            return;
        }
        int index = int.Parse(mode, CultureInfo.InvariantCulture);
        var control = PlaceholderControls[index];
        using var probe = CreateFile(Path.Combine(root, $"placeholder-{index}.txt"), control.Access, 7, 0, 3, control.Flags, 0);
        Assert.False(probe.IsInvalid, $"Open the {control.Kind} control: {Marshal.GetLastPInvokeError()}");
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
        nint table = 0;
        try
        {
            if (!OnNtfs(root)) Assert.Skip("The test folder is not on NTFS.");
            (registered, connected, connection, table) = RegisterCountingProvider(root, CloudListingCount);
            string exe = Path.ChangeExtension(typeof(WindowsFileRecordsTests).Assembly.Location, ".exe");
            int opensBefore = Volatile.Read(ref _placeholderOpens);
            await RunCloudListingChild(exe, root, "list");
            await Task.Delay(2000, TestContext.Current.CancellationToken); // room for a late notification of a listing open
            int listingOpens = Volatile.Read(ref _placeholderOpens) - opensBefore;
            Assert.Equal(0, listingOpens);
            Assert.Equal(fetchesBefore, Volatile.Read(ref _fetches));
            int observed = -1;
            for (int i = 0; i < PlaceholderControls.Length && observed < 0; i++)
            {
                int seen = Volatile.Read(ref _placeholderOpens);
                await RunCloudListingChild(exe, root, i.ToString(CultureInfo.InvariantCulture));
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (Volatile.Read(ref _placeholderOpens) == seen && DateTime.UtcNow < deadline) await Task.Delay(50, TestContext.Current.CancellationToken);
                if (Volatile.Read(ref _placeholderOpens) != seen) observed = i;
            }
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Placeholder opens: listing {listingOpens}; first control the provider observed: {(observed < 0 ? "none" : PlaceholderControls[observed].Kind)}");
            if (observed < 0)
                Assert.Skip("No control open from another process reached the provider on this Windows build, so it cannot show whether listing opened placeholders.");
            if (observed == 2)
                Assert.Skip("This Windows build reports only data-access opens to the provider. Listing caused none, but metadata-only opens are unobservable here.");
        }
        finally
        {
            try { for (int i = 0; i < CloudListingCount; i++) { string file = Path.Combine(root, $"placeholder-{i}.txt"); if (File.Exists(file)) File.Delete(file); } }
            finally
            {
                if (connected) CfDisconnectSyncRoot(connection);
                if (registered) CfUnregisterSyncRoot(root);
                if (table != 0) FreeTable(table);
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Runs the child test once and requires that it actually ran and passed: a skipped child proves nothing.</summary>
    private static Task RunCloudListingChild(string exe, string root, string mode) =>
        RunCloudChild(exe, nameof(Cloud_listing_child_lists_or_opens_as_its_parent_asks), new() { [CloudListingRoot] = root, [CloudListingMode] = mode }, mode);

    internal static async Task RunCloudChild(string exe, string method, Dictionary<string, string> environment, string mode)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-method", typeof(WindowsFileRecordsTests).FullName + "." + method, "-noLogo", "-showLiveOutput" })
            start.ArgumentList.Add(argument);
        foreach (var (name, value) in environment) start.Environment[name] = value;
        using var child = Process.Start(start)!;
        try
        {
            var output = child.StandardOutput.ReadToEndAsync();
            var errors = child.StandardError.ReadToEndAsync();
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            limit.CancelAfter(TimeSpan.FromMinutes(2));
            await child.WaitForExitAsync(limit.Token);
            string text = await output + await errors;
            Assert.True(child.ExitCode == 0 && Regex.IsMatch(text, @"Total: 1, Errors: 0, Failed: 0, Skipped: 0"),
                $"The '{mode}' child did not run and pass its one test (exit {child.ExitCode}): {text}");
            foreach (Match line in Regex.Matches(text, @"Child: [^\r\n]*")) TestContext.Current.TestOutputHelper?.WriteLine(line.Value);
        }
        finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
    }

    private static unsafe void FreeTable(nint table) => NativeMemory.Free((void*)table);

    [LibraryImport("ntdll.dll")]
    private static partial sbyte RtlQueryProcessPlaceholderCompatibilityMode();

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
