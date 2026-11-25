using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Microsoft.Win32.SafeHandles;
using FindData = FileCat.Platform.Windows.WindowsFileSystemProvider.FindData;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsEnumerationTests
{
    private sealed class Sink(Action<int>? batchPublished = null) : IEnumerationSink
    {
        public readonly Dictionary<string, EntryData> Entries = new(StringComparer.Ordinal);
        public int LargestBatch;
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            LargestBatch = Math.Max(LargestBatch, entries.Length);
            foreach (var entry in entries) Assert.True(Entries.TryAdd(entry.Name, entry));
            batchPublished?.Invoke(entries.Length);
        }
        public void ReportIssue(string message) => Assert.Fail(message);
    }

    [Fact]
    public async Task Native_listing_preserves_names_attributes_batches_and_metadata_without_child_opens()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows native enumeration."); return; }
        string root = Directory.CreateTempSubdirectory("fc-enum-").FullName;
        try
        {
            for (int i = 0; i < 1300; i++) File.WriteAllText(Path.Join(root, "file" + i + ".txt"), "sample");
            Directory.CreateDirectory(Path.Join(root, "folder"));
            File.WriteAllText(Path.Join(root, "Unicode 名前.txt"), "unicode");
            File.SetAttributes(Path.Join(root, "file0.txt"), FileAttributes.Hidden | FileAttributes.System);
            var managed = new Sink(); var native = new Sink();
            var location = Location.FileSystem(root);
            await new LocalFileSystemProvider().EnumerateAsync(location, managed, TestContext.Current.CancellationToken);
            var provider = new WindowsFileSystemProvider { UseNativeEnumeration = true };
            await provider.EnumerateAsync(location, native, TestContext.Current.CancellationToken);
            Assert.Equal(managed.Entries.Keys.Order(StringComparer.Ordinal), native.Entries.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(512, native.LargestBatch);
            foreach (var (name, entry) in managed.Entries)
            {
                var got = native.Entries[name];
                Assert.Equal(entry.Attributes, got.Attributes);
                Assert.Equal(entry.Flags, got.Flags);
                Assert.Equal(entry.Kind, got.Kind);
                Assert.Equal(entry.Size, got.Size);
                Assert.Equal(entry.Modified, got.Modified);
                Assert.Equal(entry.Created, got.Created);
            }
            Assert.Equal(56, Unsafe.SizeOf<EntryData>()); // 64-bit managed layout; spill remains 48 bytes
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cancel.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => provider.EnumerateAsync(location, new Sink(), cancel.Token));

            using var midstream = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var prefix = new Sink(_ => midstream.Cancel());
            await Assert.ThrowsAsync<OperationCanceledException>(() => provider.EnumerateAsync(location, prefix, midstream.Token));
            Assert.Equal(512, prefix.Entries.Count);

            using var terminal = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var allEntries = new Sink(length => { if (length < 512) terminal.Cancel(); });
            await Assert.ThrowsAsync<OperationCanceledException>(() => provider.EnumerateAsync(location, allEntries, terminal.Token));
            Assert.Equal(native.Entries.Count, allEntries.Entries.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Native_listing_matches_long_paths_and_distinguishes_junction_and_symlink_tags()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("FILECAT_ENUM_NATIVE_LINKS") == "1",
            "Requires the prepared Windows NTFS link fixture.");
        string root = Directory.CreateTempSubdirectory("fc-enum-reparse-").FullName;
        string junction = Path.Join(root, "junction"), link = Path.Join(root, "file-link");
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(root, new string('d', 128))).FullName;
            string file = Path.Join(folder, new string('字', 140) + ".bin");
            File.WriteAllBytes(file, [6, 12, 24]);
            Assert.True(file.Length > 260);
            Junction.Create(junction, folder);
            File.CreateSymbolicLink(link, file);

            var ct = TestContext.Current.CancellationToken;
            var native = new WindowsFileSystemProvider { UseNativeEnumeration = true };
            var legacy = new WindowsFileSystemProvider();
            var rootLocation = Location.FileSystem(root);
            var managedResult = new Sink();
            var nativeResult = new Sink();
            await legacy.EnumerateAsync(rootLocation, managedResult, ct);
            await native.EnumerateAsync(rootLocation, nativeResult, ct);
            Assert.Equal(managedResult.Entries.Keys.Order(StringComparer.Ordinal), nativeResult.Entries.Keys.Order(StringComparer.Ordinal));
            foreach (var (name, entry) in managedResult.Entries)
            {
                Assert.Equal(entry.Kind, nativeResult.Entries[name].Kind);
                Assert.Equal(entry.Flags, nativeResult.Entries[name].Flags);
            }
            Assert.Equal(0xA0000003u, nativeResult.Entries["junction"].NativeReparseTag);
            Assert.Equal(0xA000000Cu, nativeResult.Entries["file-link"].NativeReparseTag);

            var longPath = new Sink();
            await native.EnumerateAsync(Location.FileSystem(folder), longPath, ct);
            var item = Assert.Single(longPath.Entries).Value;
            Assert.Equal(Path.GetFileName(file), item.Name);
            Assert.Equal(3, item.Size);
            Assert.Equal(0u, item.NativeReparseTag);
        }
        finally
        {
            if (File.Exists(link)) File.Delete(link);
            if (Directory.Exists(junction)) Directory.Delete(junction);
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_empty_directory_has_no_entries_and_a_missing_directory_fails_before_publication(bool native)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows enumeration.");
        string root = Directory.CreateTempSubdirectory("fc-enum-empty-").FullName;
        try
        {
            var provider = new WindowsFileSystemProvider { UseNativeEnumeration = native };
            var entries = new Sink();
            var ct = TestContext.Current.CancellationToken;
            await provider.EnumerateAsync(Location.FileSystem(root), entries, ct);
            Assert.Empty(entries.Entries);
            var missing = Location.FileSystem(Path.Join(root, "missing"));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => provider.EnumerateAsync(missing, entries, ct));
            Assert.Empty(entries.Entries);
        }
        finally { Directory.Delete(root); }
    }

    [Fact]
    public async Task A_no_match_answer_is_settled_by_the_managed_route_before_any_publication()
    {
        // Some SMB servers answer a missing folder with "no such file" (ERROR_FILE_NOT_FOUND), which the find API also
        // reports for an empty share or FAT root. The managed route opens the folder first and tells the two apart.
        string root = Directory.CreateTempSubdirectory("fc-enum-no-match-").FullName;
        var ct = TestContext.Current.CancellationToken;
        try
        {
            File.WriteAllBytes(Path.Join(root, "managed.txt"), [1, 2, 3]);
            var api = new ScriptedFindApi([2]);
            var sink = new Sink(_ => Assert.All(api.Cursors, cursor => Assert.True(cursor.IsInvalid)));
            await new WindowsFileSystemProvider().EnumerateWithFindApi(Location.FileSystem(root), sink, ct, api);
            Assert.Equal("managed.txt", Assert.Single(sink.Entries).Value.Name);
            Assert.Equal(new[] { true }, api.LargeFetchCalls);
            Assert.Equal(0, api.AdvanceCalls);

            var missing = new ScriptedFindApi([2]);
            var nothing = new Sink();
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                new WindowsFileSystemProvider().EnumerateWithFindApi(Location.FileSystem(Path.Join(root, "missing")), nothing, ct, missing));
            Assert.Empty(nothing.Entries);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(87, 87)]
    [InlineData(87, 120)]
    [InlineData(120, 0)]
    public async Task An_unsupported_open_uses_managed_listing_before_any_publication(int firstError, int retryError)
    {
        string root = Directory.CreateTempSubdirectory("fc-enum-fallback-").FullName;
        try
        {
            File.WriteAllBytes(Path.Join(root, "managed.txt"), [1, 2, 3]);
            var api = new ScriptedFindApi(retryError == 0 ? [firstError] : [firstError, retryError]);
            var sink = new Sink(_ => Assert.All(api.Cursors, cursor => Assert.True(cursor.IsInvalid)));

            await new WindowsFileSystemProvider().EnumerateWithFindApi(
                Location.FileSystem(root), sink, TestContext.Current.CancellationToken, api);

            var item = Assert.Single(sink.Entries).Value;
            Assert.Equal("managed.txt", item.Name);
            Assert.Equal(3, item.Size);
            Assert.Equal(0u, item.NativeReparseTag);
            Assert.Equal(retryError == 0 ? new[] { true } : [true, false], api.LargeFetchCalls);
            Assert.All(api.Cursors, cursor => Assert.True(cursor.IsClosed));
            Assert.Equal(0, api.AdvanceCalls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task A_successful_open_without_large_fetch_keeps_native_tags()
    {
        var api = new ScriptedFindApi([87, 0], entries: 1);
        var sink = new Sink();
        await new WindowsFileSystemProvider().EnumerateWithFindApi(
            Location.FileSystem(Path.GetTempPath()), sink, TestContext.Current.CancellationToken, api);

        Assert.Equal(new[] { true, false }, api.LargeFetchCalls);
        Assert.Equal(0xA000000Cu, Assert.Single(sink.Entries).Value.NativeReparseTag);
        Assert.True(api.Cursors[0].IsClosed);
        Assert.True(api.Cursors[1].Released);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(87)]
    [InlineData(120)]
    public async Task A_find_next_failure_publishes_the_native_prefix_and_never_restarts(int error)
    {
        string root = Directory.CreateTempSubdirectory("fc-enum-partial-").FullName;
        try
        {
            File.WriteAllBytes(Path.Join(root, "managed-only.txt"), [1]);
            var api = new ScriptedFindApi([0], entries: 515, terminalError: error);
            var batches = new List<int>();
            var sink = new Sink(batches.Add);

            var failure = await Assert.ThrowsAsync<IOException>(() => new WindowsFileSystemProvider().EnumerateWithFindApi(
                Location.FileSystem(root), sink, TestContext.Current.CancellationToken, api));

            Assert.Equal(error, failure.HResult);
            Assert.Equal(new[] { 512, 3 }, batches);
            Assert.Equal(Enumerable.Range(0, 515).Select(i => $"native-{i}"), sink.Entries.Keys);
            Assert.True(Assert.Single(api.LargeFetchCalls));
            Assert.True(Assert.Single(api.Cursors).Released);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(@"\\server\share", 1326)] // ERROR_LOGON_FAILURE, which c9's Samba share answers (I672)
    [InlineData(@"\\server\share\sub\deeper", 1326)]
    [InlineData(@"\\server\share", 5)] // ERROR_ACCESS_DENIED at the share itself
    [InlineData(@"\\server\share\sub", 1327)] // ERROR_ACCOUNT_RESTRICTION (a blank password, for one)
    [InlineData(@"\\server\share\sub", 1909)] // ERROR_ACCOUNT_LOCKED_OUT
    public async Task A_share_whose_server_refused_the_logon_offers_a_sign_in_to_the_share(string path, int error)
    {
        // The share's own failure must say what Enter does, as the server's does (NET-004): until I672's second fix it was
        // Windows' bare "The user name or password is incorrect.", and Enter went up.
        var api = new ScriptedFindApi([error]);
        var nothing = new Sink();
        var failure = await Assert.ThrowsAsync<NetworkAuthenticationRequiredException>(() => new WindowsFileSystemProvider()
            .EnumerateWithFindApi(Location.FileSystem(path), nothing, TestContext.Current.CancellationToken, api));
        Assert.Equal(@"\\server\share", failure.Server);
        Assert.StartsWith(@"\\server\share requires credentials (", failure.Message);
        Assert.EndsWith("). Press Enter to sign in.", failure.Message);
        Assert.Empty(nothing.Entries);
        Assert.Equal(0, api.AdvanceCalls);
    }

    [Theory]
    [InlineData(@"C:\folder", 1326)] // not on a share: Windows' own answer
    [InlineData(@"\\server\share\private", 5)] // a denied folder inside a share keeps its refusal; its ".." goes up (I572)
    [InlineData(@"\\server", 1326)] // a server root lists shares through the Network, which asks for its own sign-in
    [InlineData(@"\\server\share", 53)] // ERROR_BAD_NETPATH: no sign-in reaches a server that does not answer
    [InlineData(@"\\server\share", 1219)] // ERROR_SESSION_CREDENTIAL_CONFLICT: Windows refuses a second sign-in
    public void Other_failures_keep_their_own_error(string path, int error)
    {
        Assert.Null(WindowsFileSystemProvider.SignInRequired(path, error));
        Assert.Null(WindowsFileSystemProvider.SignInRequired(path, new IOException("refused", unchecked((int)0x80070000 | error))));
    }

    [Fact]
    public void The_managed_route_offers_the_same_sign_in()
    {
        // .NET's own exceptions, as the managed listing throws them (an emulated process lists that way).
        var logon = new IOException(@"The user name or password is incorrect. : '\\server\share\sub'", unchecked((int)0x8007052E));
        Assert.Equal(@"\\server\share", WindowsFileSystemProvider.SignInRequired(@"\\server\share\sub", logon)?.Server);
        Assert.Equal(@"\\server\share", WindowsFileSystemProvider.SignInRequired(@"\\server\share", new UnauthorizedAccessException())?.Server);
        Assert.Null(WindowsFileSystemProvider.SignInRequired(@"\\server\share\private", new UnauthorizedAccessException()));
        Assert.Null(WindowsFileSystemProvider.SignInRequired(@"C:\folder", logon));
        // A link that refuses listing while its folder lists is shown as that folder, never as a sign-in.
        Assert.Null(WindowsFileSystemProvider.SignInRequired(@"\\server\share",
            new LinkListingRefusedException(@"\\server\share", @"C:\target", new UnauthorizedAccessException())));
    }

    private sealed unsafe class ScriptedFindApi(int[] openErrors, int entries = 0, int terminalError = 18)
        : WindowsFileSystemProvider.IFindApi
    {
        public List<bool> LargeFetchCalls { get; } = [];
        public List<ScriptedCursor> Cursors { get; } = [];
        public int AdvanceCalls { get; private set; }

        public SafeHandle Open(string pattern, bool largeFetch, FindData* data, out int error)
        {
            error = openErrors[LargeFetchCalls.Count];
            LargeFetchCalls.Add(largeFetch);
            var cursor = new ScriptedCursor(error == 0);
            Cursors.Add(cursor);
            if (!cursor.IsInvalid) WriteEntry(data, 0);
            return cursor;
        }

        public bool Advance(SafeHandle cursor, FindData* data, out int error)
        {
            Assert.Same(Cursors[^1], cursor);
            if (++AdvanceCalls == entries)
            {
                error = terminalError;
                return false;
            }

            WriteEntry(data, AdvanceCalls);
            error = 0;
            return true;
        }

        private static void WriteEntry(FindData* data, int index)
        {
            *data = default;
            data->Attributes = (uint)FileAttributes.ReparsePoint;
            data->ReparseTag = 0xA000000C;
            $"native-{index}".AsSpan().CopyTo(new Span<char>(data->Name, 260));
        }
    }

    private sealed class ScriptedCursor : SafeHandleZeroOrMinusOneIsInvalid
    {
        public bool Released { get; private set; }

        public ScriptedCursor(bool valid) : base(true) => SetHandle(valid ? 1 : -1);

        protected override bool ReleaseHandle()
        {
            Released = true;
            return true;
        }
    }

    [Fact]
    public async Task Native_listing_matches_managed_listing_on_a_prepared_mapped_and_unc_smb_folder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires the Windows SMB client.");
        string? mapped = Environment.GetEnvironmentVariable("FILECAT_ENUM_MAPPED_SMB_ROOT");
        string? unc = Environment.GetEnvironmentVariable("FILECAT_ENUM_NATIVE_SMB_ROOT");
        if (string.IsNullOrWhiteSpace(mapped) || string.IsNullOrWhiteSpace(unc))
            Assert.Skip("Set FILECAT_ENUM_MAPPED_SMB_ROOT and FILECAT_ENUM_NATIVE_SMB_ROOT to the same prepared SMB fixture folder.");
        if (Environment.GetEnvironmentVariable("FILECAT_ENUM_SMB_ARCHITECTURE") is { Length: > 0 } architecture)
        {
            Assert.Equal(architecture, RuntimeInformation.ProcessArchitecture.ToString());
            Assert.Equal(architecture, RuntimeInformation.OSArchitecture.ToString());
            Assert.False(WindowsTokenAuthority.IsHighIntegrity);
        }
        Assert.Equal(DriveType.Network, new DriveInfo(Path.GetPathRoot(mapped)!).DriveType);
        Assert.StartsWith(@"\\", unc, StringComparison.Ordinal);
        bool windowsServer = Environment.GetEnvironmentVariable("FILECAT_ENUM_SMB_WINDOWS_SERVER") == "1";
        var ct = TestContext.Current.CancellationToken;
        var nativeProvider = new WindowsFileSystemProvider { UseNativeEnumeration = true };
        var managedProvider = new WindowsFileSystemProvider();
        foreach (string root in new[] { mapped, unc })
        {
            var location = Location.FileSystem(root);
            var native = new Sink(); var managed = new Sink();
            var actualApi = new ObservedFindApi();
            await managedProvider.EnumerateAsync(location, managed, ct);
            await nativeProvider.EnumerateWithFindApi(location, native, ct, actualApi);
            Assert.True(actualApi.Completed, "The SMB comparison must complete a real Win32 find cursor.");
            Assert.Contains("Case.txt", native.Entries.Keys);
            if (!windowsServer) Assert.Contains("case.txt", native.Entries.Keys);
            Assert.Contains("Unicode 名前.bin", native.Entries.Keys);
            Assert.Equal(EntryKind.Directory, native.Entries["owned-folder"].Kind);
            Assert.Equal(managed.Entries.Keys.Order(StringComparer.Ordinal), native.Entries.Keys.Order(StringComparer.Ordinal));
            foreach (var (name, entry) in managed.Entries)
            {
                var got = native.Entries[name];
                Assert.Equal(entry.Kind, got.Kind);
                Assert.Equal(entry.Attributes, got.Attributes);
                Assert.Equal(entry.Flags, got.Flags);
                Assert.Equal(entry.Size, got.Size);
                Assert.Equal(entry.Modified, got.Modified);
                Assert.Equal(entry.Created, got.Created);
            }

            var empty = Location.FileSystem(Path.Join(root, "owned-folder"));
            var nativeEmpty = new Sink(); var managedEmpty = new Sink();
            await managedProvider.EnumerateAsync(empty, managedEmpty, ct);
            await nativeProvider.EnumerateAsync(empty, nativeEmpty, ct);
            Assert.Empty(managedEmpty.Entries);
            Assert.Empty(nativeEmpty.Entries);

            var missing = Location.FileSystem(Path.Join(root, "fixture-directory-does-not-exist"));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => managedProvider.EnumerateAsync(missing, new Sink(), ct));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => nativeProvider.EnumerateAsync(missing, new Sink(), ct));
            if (windowsServer)
            {
                const int caseSensitivePath = 442; // ERROR_CASE_SENSITIVE_PATH
                var sensitive = Location.FileSystem(Path.Join(root, "case-distinct"));
                var managedRefusal = new Sink(); var nativeRefusal = new Sink();
                var managedError = await Assert.ThrowsAsync<IOException>(() => managedProvider.EnumerateAsync(sensitive, managedRefusal, ct));
                var nativeError = await Assert.ThrowsAsync<IOException>(() => nativeProvider.EnumerateAsync(sensitive, nativeRefusal, ct));
                Assert.Equal(caseSensitivePath, managedError.HResult & 0xFFFF);
                Assert.Equal(caseSensitivePath, nativeError.HResult);
                Assert.Empty(managedRefusal.Entries);
                Assert.Empty(nativeRefusal.Entries);
            }
            TestContext.Current.TestOutputHelper?.WriteLine($"SMB listing {root}: {native.Entries.Count} entries, Win32 cursor completed.");
        }
    }

    private sealed unsafe class ObservedFindApi : WindowsFileSystemProvider.IFindApi
    {
        private readonly WindowsFileSystemProvider.Win32FindApi _api = new();
        internal bool Completed { get; private set; }

        public SafeHandle Open(string pattern, bool largeFetch, FindData* data, out int error) =>
            _api.Open(pattern, largeFetch, data, out error);

        public bool Advance(SafeHandle cursor, FindData* data, out int error)
        {
            bool advanced = _api.Advance(cursor, data, out error);
            if (!advanced && error == 18) Completed = true;
            return advanced;
        }
    }

    [Fact]
    public async Task Enumeration_performance_records_native_and_managed_timings_on_a_prepared_directory()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows enumeration performance."); return; }
        if (Environment.GetEnvironmentVariable("FILECAT_ENUM_FIXTURE") is not { Length: > 0 } path)
        { Assert.Skip("Set FILECAT_ENUM_FIXTURE to a deliberately prepared scale directory; no user-tree scan."); return; }
        var location = Location.FileSystem(path);
        if (Environment.GetEnvironmentVariable("FILECAT_ENUM_ROUTE") is { Length: > 0 } route)
        {
            bool native = route switch
            {
                "managed" => false,
                "native" => true,
                _ => throw new ArgumentException("FILECAT_ENUM_ROUTE must be managed or native."),
            };
            await Measure(0, native);
            return;
        }

        for (int run = 0; run < 5; run++)
        {
            foreach (bool useNative in run % 2 == 0 ? new[] { false, true } : new[] { true, false })
                await Measure(run, useNative);
        }

        async Task Measure(int run, bool native)
        {
            using var process = Process.GetCurrentProcess();
            int handlesBefore = process.HandleCount;
            long workingSetBefore = process.WorkingSet64, allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var sink = new CountingSink();
            var clock = Stopwatch.StartNew();
            await new WindowsFileSystemProvider { UseNativeEnumeration = native }.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
            clock.Stop();
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            process.Refresh();
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"run={run} native={native} count={sink.Count} elapsedMs={clock.Elapsed.TotalMilliseconds:F2} allocatedBytes={allocated} " +
                $"handlesBefore={handlesBefore} handlesAfter={process.HandleCount} " +
                $"workingSetBefore={workingSetBefore} workingSetAfter={process.WorkingSet64} peakWorkingSet={process.PeakWorkingSet64}");
            if (int.TryParse(Environment.GetEnvironmentVariable("FILECAT_ENUM_EXPECTED_COUNT"), out int expected))
                Assert.Equal(expected, sink.Count);
        }
    }

    private sealed class CountingSink : IEnumerationSink
    {
        public int Count;
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Count += entries.Length;
        public void ReportIssue(string message) => Assert.Fail(message);
    }
}
