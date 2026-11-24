using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Verification;
using Microsoft.Win32.SafeHandles;

namespace FileCat.App.Tests;

/// <summary>
/// I636: Analyze folder and a column-header sort keep every field's results in order, whatever value the field produces.
/// A value the ordering snapshot does not keep itself (an Origin mark and an app alias's or WSL link's target, each with
/// its explanation, and Verified's report) is kept as its sort key. Rc.2 kept no key for the first two: Analyze folder
/// said "Unsupported value or sort key could not be retained", and both sorts left those rows unordered, in name order.
/// </summary>
public sealed class AnalysisRecordValueOrderingTests
{
    /// <summary>A value as its field's producer returns it, and its place in an ascending sort (null: no key, last).</summary>
    private sealed record Sample(object? Produced, int? Rank);

    private const string AliasDetail = "App execution alias: Windows starts this program for FileCat.Fixture_1abc!App, " +
        "from the package FileCat.Fixture_1abc. FileCat neither starts nor follows it.";
    private const string WslDetail = "WSL symbolic link: programs in WSL follow this Linux path; Windows programs do not, and FileCat does not.";
    private const uint AppAlias = 0x8000001B, WslLink = 0xA000001D;

    private static MetadataValue Owned(Principal principal) => new(MetadataState.Available, principal);
    private static MetadataValue Observed(string text, IComparable? key, bool partial = false, bool quiet = false) =>
        new(MetadataState.Available, new FileDetailsFields.Cell(text, $"What {text} means.\nScope: entry · Source: fixture", key, partial, quiet));
    private static VerificationResult Verification(VerificationState state) => new(state, state.ToString(), ["A report line."]);

    /// <summary>
    /// Every field with a value of each type its producer returns, in ascending order of the field's key, then results
    /// that have no key. A new field fails <see cref="Every_field_this_platform_registers_has_samples"/> until it has some.
    /// </summary>
    private static Sample[] SamplesFor(string fieldId) => fieldId switch
    {
        // The file version's text; a text that names no version has no key.
        "version" => [new("1.2.3.4", 0), new("2.0", 1), new("10.0.0.0", 2), new("v1", null), new(null, null)],
        "dimensions" => [new((10, 10), 0), new((4, 50), 1), new((30, 30), 2), new(null, null)],
        // A symbolic link's or junction's stored target is text; an app alias's program and a WSL link's path are records.
        "linkTarget" => [new(new LinkTargetText(@"A:\WindowsApps\FileCat.Fixture\alias.exe", AliasDetail), 0),
            new(@"B:\junction-target", 1), new(new LinkTargetText("c-relative/wsl-target", WslDetail), 2), new(null, null)],
        // Windows' zones, a stream that names none, and macOS' quarantine are records; Linux's origin URL reads "Internet".
        "zone" => [new(new OriginMark("Internet", "Zone.Identifier: ZoneId=3, the Internet.\nDownloaded from: https://example.test/a.zip"), 0),
            new(new OriginMark("Intranet", "Zone.Identifier: ZoneId=1, the local intranet."), 1),
            new(new OriginMark("Marked", "A Zone.Identifier stream is present but names no zone."), 2),
            new(new OriginMark("Quarantined", "com.apple.quarantine: macOS checks this file when it is first opened."), 3), new(null, null)],
        "permissions" => [new((UnixFileMode)0b110_000_000, 0), new((UnixFileMode)0b110_100_100, 1), new((UnixFileMode)0b111_101_101, 2),
            new(BuiltInFields.PermissionsUnread(13), null)],
        "owner" => OperatingSystem.IsWindows()
            ? [new(Owned(new(PrincipalKind.WindowsSid, "S-1-5-21-1-2-3-500")), 0), new(Owned(new(PrincipalKind.WindowsSid, "S-1-5-21-1-2-3-1001")), 1),
                new(new MetadataValue(MetadataState.Denied, Detail: "The fixture denies this."), null)]
            : [new(Owned(new(PrincipalKind.UnixUser, "2")), 0), new(Owned(new(PrincipalKind.UnixUser, "10")), 1),
                new(new MetadataValue(MetadataState.Denied, Detail: "The fixture denies this."), null)],
        "group" => [new(Owned(new(PrincipalKind.UnixGroup, "2")), 0), new(Owned(new(PrincipalKind.UnixGroup, "10")), 1),
            new(new MetadataValue(MetadataState.Failed, Detail: "The fixture fails this."), null)],
        // A complete inventory's count; one with categories left unread has no count.
        "hidden" => [new(new FileDetailsFields.Cell("0", "No extra data.", 0), 0), new(new FileDetailsFields.Cell("2", "Two streams.", 2), 1),
            new(new FileDetailsFields.Cell("17", "Seventeen attributes.", 17), 2),
            new(new FileDetailsFields.Cell("Partial", "Some categories could not be read.", null, Partial: true), null)],
        "verified" => [new(Verification(VerificationState.Differs), 0), new(Verification(VerificationState.Matches), 1),
            new(Verification(VerificationState.Sidecar), 2), new(null, null)],
        FileDetailsFields.Allocated => [new(Observed("512 bytes", 512L), 0), new(Observed("4,096 bytes", 4096L), 1),
            new(Observed("1.05 MB", 1_048_576L), 2), new(Observed("Partial", null, partial: true), null),
            new(new MetadataValue(MetadataState.Absent, Detail: "The original filesystem item is no longer available.", Observation: MetadataObservation.Gone), null)],
        FileDetailsFields.HardLinks => [new(Observed("1", 1u), 0), new(Observed("2", 2u), 1), new(Observed("3", 3u), 2),
            new(new MetadataValue(MetadataState.Unsupported, Detail: "Hard-link counts are shown only for a qualified regular-file observation."), null)],
        // "none" sorts before every type; an unknown tag has no key.
        FileDetailsFields.ReparseType => [new(Observed("none", "", quiet: true), 0), new(Observed("Junction or volume mount", "Junction or volume mount"), 1),
            new(Observed("Symbolic link", "Symbolic link"), 2), new(Observed("Tag 0x00001234", null), null)],
        _ => throw new ArgumentException($"No samples for the field '{fieldId}': add a value of each type it produces.", nameof(fieldId)),
    };

    private static IEnumerable<string> FieldIds() => BuiltInFields.All.Select(f => f.Id)
        .Concat(OperatingSystem.IsWindows() ? [FileDetailsFields.Allocated, FileDetailsFields.HardLinks, FileDetailsFields.ReparseType]
            : [FileDetailsFields.Allocated, FileDetailsFields.HardLinks]);

    public static TheoryData<string, bool> FieldCases()
    {
        var cases = new TheoryData<string, bool>();
        foreach (string id in FieldIds()) { cases.Add(id, true); cases.Add(id, false); }
        return cases;
    }

    [AvaloniaFact]
    public async Task Every_field_this_platform_registers_has_samples()
    {
        using var folder = new AnalysisFolder();
        var tab = await folder.Open();
        Assert.Equal(FieldIds().Order(StringComparer.Ordinal), folder.Services.Metadata.Fields.Select(f => f.Id).Order(StringComparer.Ordinal));
        foreach (var field in folder.Services.Metadata.Fields)
        {
            Assert.Contains(SamplesFor(field.Id), sample => sample.Rank is not null);
            Assert.True(tab.CanAnalyzeMetadata(field.Id), field.Id);
        }
    }

    /// <summary>
    /// Each field reads a value of each type into its own file, named so that name order is the reverse of the key order.
    /// Analyze folder (<paramref name="analyze"/>) or a column-header sort of the cached values then orders them by key, and
    /// the reversed sort uses the retained keys without reading again.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(FieldCases))]
    public async Task Every_value_type_a_field_produces_is_retained_and_ordered(string fieldId, bool analyze)
    {
        var samples = SamplesFor(fieldId);
        string extension = fieldId switch { "version" => ".dll", "dimensions" => ".png", _ => ".dat" };
        string Name(int sample) => $"{(char)('a' + samples.Length - 1 - sample)}-sample{extension}";
        using var folder = new AnalysisFolder();
        for (int i = 0; i < samples.Length; i++) folder.File(Name(i));
        var field = folder.Services.Metadata.Field(fieldId)!;
        Assert.All(Enumerable.Range(0, samples.Length), i => Assert.True(field.AppliesToName(Name(i))));
        int reads = 0;
        folder.Services.Metadata.Register(field with
        {
            Produce = (path, _) =>
            {
                Interlocked.Increment(ref reads);
                return samples[Enumerable.Range(0, samples.Length).Single(i => Name(i) == Path.GetFileName(path))].Produced;
            },
        });
        var tab = await folder.Open();

        // By key, then the results without one by name (unknown keys sort last both ways); ties keep name order.
        string[] Expected(bool descending) => [.. samples.Select((sample, i) => (sample.Rank, Name: Name(i)))
            .OrderBy(s => s.Rank is null).ThenBy(s => s.Rank is { } rank ? (descending ? -rank : rank) : 0)
            .ThenBy(s => s.Name, StringComparer.Ordinal).Select(s => s.Name)];
        if (analyze)
        {
            await tab.AnalyzeAsync(fieldId);
            Assert.Contains($"for {samples.Length} items", tab.Banner);
            Assert.Contains($"The order retains all {samples.Length} items' results.", tab.Banner);
            Assert.DoesNotContain("could not be retained", tab.Banner);
        }
        else
        {
            folder.ComputeAll(tab, fieldId);
            tab.SortByMetadata(fieldId);
            await Applied(tab);
        }
        Assert.Equal(samples.Length, reads);
        Assert.Equal(samples.Length, tab.Listing.AppliedMetadataCoverage?.Count);
        Assert.Equal(MetadataCapacity.None, tab.Listing.AppliedMetadataCoverage?.Capacity);
        Assert.Equal(Expected(descending: false), folder.Names(tab));

        tab.SortByMetadata(fieldId);
        await Applied(tab);
        Assert.Equal(Expected(descending: true), folder.Names(tab));
        Assert.Equal(samples.Length, reads);
    }

    /// <summary>
    /// The rc.2 check: three pictures marked from the Internet, unmarked and from the intranet. Linux and macOS mark only
    /// downloads, so there two of them are marked alike. The cells still explain each mark: the order keeps only the key.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Origin_marks_read_from_files_are_retained_and_ordered(bool analyze)
    {
        bool windows = OperatingSystem.IsWindows(), mac = OperatingSystem.IsMacOS();
        using var folder = new AnalysisFolder();
        string a = folder.File("a.png"), c = folder.File("c.png");
        folder.File("b.png");
        if (windows)
        {
            try
            {
                File.WriteAllText(a + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/a.png\r\n");
                File.WriteAllText(c + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=1\r\n");
            }
            catch (IOException ex) { Assert.Skip("This volume keeps no alternate data streams: " + ex.Message); }
        }
        else
        {
            string attribute = mac ? UnixFileOperations.QuarantineAttribute : UnixFileOperations.OriginAttribute;
            Assert.SkipUnless(Xattr.Set(a, attribute, mac ? "0083;66f1c2a3;Safari;" : "https://example.test/a.png") &&
                Xattr.Set(c, attribute, mac ? "0083;66f1c2a3;Safari;" : "https://example.test/c.png"), "This file system keeps no extended attributes here.");
        }
        var tab = await folder.Open();
        if (analyze)
        {
            await tab.AnalyzeAsync("zone");
            Assert.Contains("Read Origin for 3 items: 2 current values. 1 without a value.", tab.Banner);
            Assert.Contains("The order retains all 3 items' results.", tab.Banner);
            Assert.DoesNotContain("could not be retained", tab.Banner);
        }
        else
        {
            folder.ComputeAll(tab, "zone");
            tab.SortByMetadata("zone");
            await Applied(tab);
            Assert.Equal(3, tab.Listing.AppliedMetadataCoverage?.Count);
            Assert.Equal(MetadataCapacity.None, tab.Listing.AppliedMetadataCoverage?.Capacity);
        }
        // Internet before Intranet, the unmarked picture last; equal marks keep name order.
        Assert.Equal(["a.png", "c.png", "b.png"], folder.Names(tab));
        var cell = folder.Presentation(tab, "a.png", "zone");
        Assert.Equal(mac ? "Quarantined" : "Internet", cell.Text);
        Assert.Contains(windows ? "ZoneId=3" : mac ? "com.apple.quarantine" : "Downloaded from: https://example.test/a.png", cell.Detail);

        tab.SortByMetadata("zone");
        await Applied(tab);
        string[] descending = windows ? ["c.png", "a.png", "b.png"] : ["a.png", "c.png", "b.png"];
        Assert.Equal(descending, folder.Names(tab));
    }

    /// <summary>
    /// An app execution alias and a WSL symbolic link as Windows writes them (the layouts LinkTargetColumnTests uses), and a
    /// plain file: both targets are retained and ordered, alias program (C:\…) before the WSL path (wsl-…).
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task App_alias_and_WSL_link_targets_read_from_files_are_retained_and_ordered(bool analyze)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows reparse points.");
        using var folder = new AnalysisFolder();
        string alias = folder.File("c-alias.exe", ""), wsl = folder.File("b-wsl", "");
        folder.File("a-plain.txt");
        int error = SetReparse(alias, AppAlias, [3, 0, 0, 0, .. Encoding.Unicode.GetBytes(
            "FileCat.Fixture_1abc\0FileCat.Fixture_1abc!App\0C:\\Program Files\\WindowsApps\\FileCat.Fixture\\fixture.exe\0" + "0\0")]);
        if (error == 0) error = SetReparse(wsl, WslLink, [2, 0, 0, 0, .. Encoding.UTF8.GetBytes("wsl-relative/target")]);
        Assert.SkipWhen(error is 5 or 1314, $"This token may not set the alias and WSL reparse tags (Win32 error {error}).");
        Assert.True(error == 0, $"FSCTL_SET_REPARSE_POINT: {new Win32Exception(error).Message}");
        var tab = await folder.Open();
        if (analyze)
        {
            await tab.AnalyzeAsync("linkTarget");
            Assert.Contains("Read Link target for 3 items: 2 current values. 1 without a value.", tab.Banner);
            Assert.Contains("The order retains all 3 items' results.", tab.Banner);
            Assert.DoesNotContain("could not be retained", tab.Banner);
        }
        else
        {
            folder.ComputeAll(tab, "linkTarget");
            tab.SortByMetadata("linkTarget");
            await Applied(tab);
            Assert.Equal(3, tab.Listing.AppliedMetadataCoverage?.Count);
            Assert.Equal(MetadataCapacity.None, tab.Listing.AppliedMetadataCoverage?.Capacity);
        }
        Assert.Equal(["c-alias.exe", "b-wsl", "a-plain.txt"], folder.Names(tab));
        var cell = folder.Presentation(tab, "c-alias.exe", "linkTarget");
        Assert.Equal(@"C:\Program Files\WindowsApps\FileCat.Fixture\fixture.exe", cell.Text);
        Assert.StartsWith("App execution alias:", cell.Detail);

        tab.SortByMetadata("linkTarget");
        await Applied(tab);
        Assert.Equal(["b-wsl", "c-alias.exe", "a-plain.txt"], folder.Names(tab));
    }

    /// <summary>
    /// The rc.2 check, on this account's own aliases (read only): in the qualification VM, Analyze folder retained the 11
    /// folders there and none of the 20 aliases. Every result is retained, and the aliases are ordered by the program each
    /// starts.
    /// </summary>
    [AvaloniaFact]
    public async Task The_accounts_own_app_aliases_are_retained_and_ordered_by_program()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows app execution aliases.");
        string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
        Assert.SkipUnless(Directory.Exists(apps) && Directory.EnumerateFiles(apps).Any(path => ReparseLinks.ReadPayload(path) is { Alias.Executable.Length: > 0 }),
            "No app execution alias in this account's WindowsApps folder.");
        using var folder = new AnalysisFolder();
        var tab = await folder.Open(apps);
        await tab.AnalyzeAsync("linkTarget");
        TestContext.Current.TestOutputHelper?.WriteLine(tab.Banner ?? "(no banner)");
        Assert.Contains("The order retains all", tab.Banner);
        Assert.DoesNotContain("could not be retained", tab.Banner);
        // The values the analysis read, and their keys: the program an alias starts.
        var field = folder.Services.Metadata.Field("linkTarget")!;
        var files = folder.Names(tab);
        var values = files.Select(name => folder.Cached(tab, name, "linkTarget")).ToArray();
        Assert.Contains(values, value => value.Value is LinkTargetText);
        var keys = values.Select(value => (string?)field.CaptureKey(value)).ToArray();
        // Files with a key first, each no later than the next by the culture's text order; then those without one.
        int known = Array.FindIndex(keys, key => key is null) is var end and >= 0 ? end : keys.Length;
        Assert.All(keys[known..], key => Assert.Null(key));
        var comparison = CultureInfo.CurrentCulture.CompareInfo;
        for (int i = 1; i < known; i++)
            Assert.True(comparison.Compare(keys[i - 1], keys[i], CompareOptions.None) <= 0, $"{files[i - 1]} before {files[i]}");
    }

    private static Task Applied(TabViewModel tab) => FileListFixture.Wait(() => tab.Listing.AppliedViewSpecVersion == tab.Listing.ViewSpecVersion);

    /// <summary>FSCTL_SET_REPARSE_POINT on the file itself, a Microsoft tag's 8-byte header first.</summary>
    private static int SetReparse(string path, uint tag, byte[] data)
    {
        byte[] buffer = new byte[8 + data.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), checked((ushort)data.Length));
        data.CopyTo(buffer, 8);
        using var handle = CreateFile(path, 0x40000000 /* GENERIC_WRITE */, 7, 0, 3 /* OPEN_EXISTING */, 0x00200000 /* OPEN_REPARSE_POINT */, 0);
        if (handle.IsInvalid) return Marshal.GetLastWin32Error();
        return DeviceIoControl(handle, 0x000900A4 /* FSCTL_SET_REPARSE_POINT */, buffer, buffer.Length, null, 0, out _, 0)
            ? 0 : Marshal.GetLastWin32Error();
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputLength, byte[]? output, int outputLength,
        out int returned, nint overlapped);

    /// <summary>A private folder of files, FileCat's state beside it, and a tab on it (no window: nothing is read for display).</summary>
    private sealed class AnalysisFolder : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "filecat-i636-" + Guid.NewGuid().ToString("N"));
        private TabViewModel? _tab;

        public AnalysisFolder()
        {
            Folder = Directory.CreateDirectory(Path.Combine(_root, "files")).FullName;
            Services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(_root, "state")));
        }

        public string Folder { get; }
        public AppServices Services { get; }

        public string File(string name, string content = "fixture")
        {
            string path = Path.Combine(Folder, name);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        public async Task<TabViewModel> Open(string? path = null)
        {
            var workspace = new WorkspaceViewModel(Services);
            var panel = new PanelViewModel(workspace, Services);
            workspace.Panels.Add(panel);
            var tab = _tab = panel.OpenTab(Location.FileSystem(path ?? Folder));
            await FileListFixture.Wait(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing);
            return tab;
        }

        /// <summary>Reads each listed file's value into the metadata cache, as the column would for rows in view.</summary>
        public void ComputeAll(TabViewModel tab, string fieldId)
        {
            var store = tab.Listing.Store;
            for (int i = 0; i < store.Count; i++)
                if (store[i].Kind == EntryKind.File)
                    Services.Metadata.Compute(fieldId, Path.Combine(tab.Location!.Path, store[i].Name), store[i], TestContext.Current.CancellationToken);
        }

        public string[] Names(TabViewModel tab) => [.. Enumerable.Range(0, tab.Listing.VisibleCount).Select(i => tab.Listing.GetVisible(i))
            .Where(entry => entry.Kind == EntryKind.File).Select(entry => entry.Name)];

        /// <summary>What the cell shows and explains, from the values already read (no read starts).</summary>
        internal MetadataCellPresentation Presentation(TabViewModel tab, string name, string fieldId)
        {
            int index = Index(tab, name);
            var entry = tab.Listing.Store[index];
            return tab.MetadataPresentation(entry, index, fieldId, tab.CaptureMetadataContext(entry, index), demand: false);
        }

        /// <summary>The value already read for a listed item (no read starts).</summary>
        public MetadataValue Cached(TabViewModel tab, string name, string fieldId) =>
            Services.Metadata.Peek(fieldId, Path.Combine(tab.Location!.Path, name), tab.Listing.Store[Index(tab, name)]);

        private static int Index(TabViewModel tab, string name) =>
            Enumerable.Range(0, tab.Listing.Store.Count).Single(i => tab.Listing.Store[i].Name == name);

        public void Dispose()
        {
            _tab?.Dispose();
            Services.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }
}
