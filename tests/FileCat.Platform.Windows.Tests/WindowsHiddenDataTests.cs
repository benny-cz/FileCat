using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.HiddenData;
using FileCat.Core.Records;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>D-55 on Windows: NTFS streams and extended attributes, and the place that lists them.</summary>
public sealed partial class WindowsHiddenDataTests
{
    private static string NewFolder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "filecat-hidden-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Theory]
    [InlineData(HiddenKind.Stream)]
    [InlineData(HiddenKind.NtfsAttribute)]
    public void Protected_values_are_viewable_and_refused_by_derived_output(HiddenKind kind)
    {
        WindowsStorageAbiTests.RequireNativeEfsOutputFixture();
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "protected.bin");
            const string name = "FILECAT.VALUE";
            byte[] expected = "owned supplementary value"u8.ToArray();
            File.WriteAllText(file, "main data");
            File.WriteAllBytes(file + ":" + name, expected);
            WriteEa(file, name, expected);
            var native = new WindowsHiddenData();
            var provider = new HiddenDataProvider(native);
            var output = (IUnprotectedContentOutput)provider;
            var ct = TestContext.Current.CancellationToken;
            var identity = native.ReadInventory(file, new(IncludeEaValues: kind == HiddenKind.NtfsAttribute), ct).Identity!.Value;
            var request = new ItemRef(HiddenDataProvider.Of(file), name, EntryKind.File)
                { Ordinal = HiddenIdentities.Ordinal(kind), SupplementarySourceIdentity = identity };
            var clearReport = provider.ReadValueReport(request, identity, ct);
            Assert.True(clearReport.CanPublish);
            Assert.Contains("owned supplementary value", clearReport.Text);
            using (var clear = output.OpenUnprotectedOutput(request, ContentOutputKind.Extraction, out var origin)!)
            {
                byte[] bytes = new byte[expected.Length];
                Assert.Equal(bytes.Length, clear.Read(0, bytes));
                Assert.Equal(expected, bytes);
                Assert.NotNull(origin);
                Assert.False(origin.Freeze().HasUnknownInput);
                Assert.Equal(OutputEfsObservation.State.Clear, origin.Status);
            }

            File.Encrypt(file);
            var refusal = Assert.Throws<NotSupportedException>(() => output.OpenUnprotectedOutput(request, ContentOutputKind.Extraction));
            Assert.Contains("Windows EFS", refusal.Message);
            var reportRefusal = Assert.Throws<NotSupportedException>(() => provider.ReadValueReport(request, identity, ct));
            Assert.Contains("Windows EFS", reportRefusal.Message);
            using var view = provider.OpenContent(request)!;
            byte[] viewed = new byte[expected.Length];
            Assert.Equal(viewed.Length, view.Read(0, viewed));
            Assert.Equal(expected, viewed);
            Assert.Equal([file], Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Opened_stream_output_refuses_a_later_efs_change_before_reading_more_bytes()
    {
        WindowsStorageAbiTests.RequireNativeEfsOutputFixture();
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "before.bin");
            File.WriteAllText(file, "original");
            File.WriteAllBytes(file + ":payload", [1, 2, 3]);
            var provider = (IUnprotectedContentOutput)new HiddenDataProvider(new WindowsHiddenData());
            var identity = new WindowsHiddenData().ReadInventory(file, new(), TestContext.Current.CancellationToken).Identity!.Value;
            var request = new ItemRef(HiddenDataProvider.Of(file), "payload", EntryKind.File)
                { Ordinal = HiddenIdentities.Ordinal(HiddenKind.Stream), SupplementarySourceIdentity = identity };
            using var output = provider.OpenUnprotectedOutput(request, ContentOutputKind.Extraction, out var origin)!;
            byte[] next = [0];
            Assert.Equal(1, output.Read(0, next));
            Assert.Equal((byte)1, next[0]);

            File.Encrypt(file);
            Assert.Contains("Windows EFS", Assert.Throws<NotSupportedException>(() => output.Read(1, next)).Message);
            Assert.Equal((byte)1, next[0]);
            Assert.Equal(OutputEfsObservation.State.Encrypted, origin!.Freeze().Protection);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData(HiddenKind.Stream)]
    [InlineData(HiddenKind.NtfsAttribute)]
    public void Reviewed_supplementary_actions_refuse_a_replacement_file(HiddenKind kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows supplementary data.");
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "source.bin"), previous = Path.Combine(dir, "previous.bin");
            var native = new WindowsHiddenData();
            var provider = new HiddenDataProvider(native);
            var ct = TestContext.Current.CancellationToken;
            const string name = "FILECAT.REPORT";
            Seed("original value");
            var original = native.ReadInventory(file, new(IncludeEaValues: kind == HiddenKind.NtfsAttribute), ct).Identity!.Value;
            var request = new ItemRef(HiddenDataProvider.Of(file), name, EntryKind.File)
                { Ordinal = HiddenIdentities.Ordinal(kind), SupplementarySourceIdentity = original };
            var report = provider.ReadValueReport(request, original, ct);
            Assert.Equal(ReportContentKind.Payload, report.ContentKind);
            Assert.True(report.CanPublish);
            Assert.Equal(original, Assert.Single(report.Origin.Contributors).Identity);
            Assert.Contains("original value", report.Text);

            File.Move(file, previous);
            Seed("another value");
            var refusal = Assert.Throws<IOException>(() => provider.ReadValueReport(request, original, ct));
            Assert.Contains("different file", refusal.Message);
            Assert.Contains("different file", Assert.Throws<IOException>(() => provider.OpenContent(request)).Message);
            Assert.Contains("different file", Assert.Throws<IOException>(() =>
                ((IUnprotectedContentOutput)provider).OpenUnprotectedOutput(request, ContentOutputKind.Extraction)).Message);
            Assert.True(report.CanPublish);
            Assert.Contains("original value", report.Text);
            Assert.Contains("different file", Assert.Throws<IOException>(() => provider.DeleteReviewedValue(request, original, ct)).Message);
            var replacement = native.ReadInventory(file, new(IncludeEaValues: true), ct).Identity!.Value;
            var fresh = provider.ReadValueReport(request, replacement, ct);
            Assert.Contains("another value", fresh.Text);
            Assert.NotEqual(report.Revision, fresh.Revision);

            provider.DeleteReviewedValue(request, replacement, ct);
            var remaining = Assert.Single(native.ReadInventory(file, new(IncludeEaValues: true), ct).Items, item => item.Name == name);
            Assert.NotEqual(kind, remaining.Kind);
            Assert.Equal("another value", Encoding.UTF8.GetString(native.Read(file, remaining, 64)));
            Assert.Equal(2, native.ReadInventory(previous, new(IncludeEaValues: true), ct).Items.Count(item => item.Name == name));
            Assert.Equal("main contents", File.ReadAllText(file));

            void Seed(string value)
            {
                File.WriteAllText(file, "main contents");
                File.WriteAllText(file + ":" + name, value);
                WriteEa(file, name, Encoding.UTF8.GetBytes(value));
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Streams_of_files_and_folders_are_listed_read_and_deleted()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows NTFS streams and extended attributes require Windows.");
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "tool.bin");
            File.WriteAllText(file, "contents");
            File.WriteAllText(file + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            File.WriteAllBytes(file + ":payload", new byte[300_000]);
            var hidden = new WindowsHiddenData();

            var items = hidden.List(file);
            Assert.Equal(["Zone.Identifier", "payload"], items.Where(i => i.Kind == HiddenKind.Stream).Select(i => i.Name).Order(StringComparer.Ordinal));
            var payload = Assert.Single(items, i => i.Name == "payload");
            Assert.Equal(300_000, payload.Size);
            Assert.Equal(1024, hidden.Read(file, payload, 1024).Length);
            using (var stream = hidden.Open(file, payload)) Assert.Equal(300_000, stream.Length);
            var zone = Assert.Single(items, i => i.Name == "Zone.Identifier");
            Assert.StartsWith("Mark of the Web: from the Internet", HiddenDataDecoder.Describe(zone, hidden.Read(file, zone, 4096)).Summary);

            hidden.Delete(file, payload);
            Assert.DoesNotContain(hidden.List(file), i => i.Name == "payload");
            Assert.Equal("contents", File.ReadAllText(file));

            // A folder can carry streams too.
            string folder = Directory.CreateDirectory(Path.Combine(dir, "sub")).FullName;
            File.WriteAllText(folder + ":note", "hidden in a folder");
            var note = Assert.Single(hidden.List(folder), i => i.Kind == HiddenKind.Stream);
            Assert.Equal("hidden in a folder", HiddenDataDecoder.Describe(note, hidden.Read(folder, note, 4096)).Summary);

            // Nothing beside a plain file.
            string plain = Path.Combine(dir, "plain.txt");
            File.WriteAllText(plain, "x");
            Assert.Empty(hidden.List(plain));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void NTFS_extended_attributes_are_listed_read_and_deleted()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows NTFS streams and extended attributes require Windows.");
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "ea.bin");
            File.WriteAllText(file, "x");
            // WSL's mode attribute, as WSL writes it: 0100644.
            WriteEa(file, "$LXMOD", BitConverter.GetBytes(0x81A4u));
            WriteEa(file, "FILECAT.TEST", Encoding.ASCII.GetBytes("an attribute"));
            var hidden = new WindowsHiddenData();
            var items = hidden.List(file).Where(i => i.Kind == HiddenKind.NtfsAttribute).ToList();
            var mode = Assert.Single(items, i => i.Name == "$LXMOD");
            Assert.Equal("WSL: mode 100644 (-rw-r--r--)", HiddenDataDecoder.Describe(mode, hidden.Read(file, mode, 64)).Summary);
            var test = Assert.Single(items, i => i.Name == "FILECAT.TEST");
            Assert.Equal("an attribute", Encoding.ASCII.GetString(hidden.Read(file, test, 64)));

            hidden.Delete(file, test);
            Assert.DoesNotContain(hidden.List(file), i => i.Name == "FILECAT.TEST");
            Assert.Contains(hidden.List(file), i => i.Name == "$LXMOD");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task The_hidden_data_place_lists_each_item_with_what_it_says_and_serves_it()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows NTFS streams and extended attributes require Windows.");
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "setup.exe");
            File.WriteAllText(file, "MZ");
            File.WriteAllText(file + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.com/setup.exe\r\n");
            var provider = new HiddenDataProvider(new WindowsHiddenData());
            var location = HiddenDataProvider.Of(file);
            Assert.Equal($"{file} › streams and attributes", provider.GetDisplayPath(location));
            Assert.Equal(dir, provider.GetParent(location)!.Path);
            Assert.Equal("setup.exe", provider.GetNameInParent(location));
            var entries = new List<EntryData>();
            await provider.EnumerateAsync(location, new ListSink(entries), TestContext.Current.CancellationToken);
            var zone = Assert.Single(entries);
            var tag = Assert.IsType<HiddenEntryTag>(zone.Tag);
            Assert.Equal("Stream", tag.KindText);
            Assert.Equal("Not read — F3 to view", tag.DetailsText);
            using var content = provider.OpenContent(provider.GetItemRef(location, zone))!;
            var buffer = new byte[64];
            int read = content.Read(0, buffer);
            Assert.StartsWith("[ZoneTransfer]", Encoding.UTF8.GetString(buffer, 0, read));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_path_with_a_colon_after_it_names_its_streams_as_NTFS_writes_it()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows NTFS streams and extended attributes require Windows.");
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "setup.exe");
            File.WriteAllText(file, "MZ");
            var registry = new ProviderRegistry();
            registry.Register(new FileCat.Core.FileSystem.LocalFileSystemProvider());
            registry.Register(new HiddenDataProvider(new WindowsHiddenData()));
            Assert.True(registry.TryParse(file + ":", null, out var streams));
            Assert.Equal(Schemes.HiddenData, streams!.Scheme);
            Assert.Equal(file, streams.Container!.Path);
            // A folder too; a path with no such item, or a stream's own name, is not taken.
            Assert.True(registry.TryParse(dir + ":", null, out var folderStreams));
            Assert.Equal(Schemes.HiddenData, folderStreams!.Scheme);
            Assert.False(registry.TryParse(Path.Combine(dir, "missing.exe") + ":", null, out _));
            Assert.False(registry.TryParse(file + ":Zone.Identifier", null, out _));
            // The drive letter's colon is the only one a folder's path has.
            Assert.True(registry.TryParse(dir, null, out var plain));
            Assert.True(plain!.IsFileSystem);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Find_finds_files_carrying_streams_besides_their_download_mark()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows NTFS streams and extended attributes require Windows.");
        string dir = NewFolder();
        try
        {
            File.WriteAllText(Path.Combine(dir, "carrier.exe"), "MZ");
            File.WriteAllText(Path.Combine(dir, "carrier.exe") + ":payload", "hidden");
            File.WriteAllText(Path.Combine(dir, "downloaded.exe"), "MZ");
            File.WriteAllText(Path.Combine(dir, "downloaded.exe") + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            File.WriteAllText(Path.Combine(dir, "plain.exe"), "MZ");
            string folder = Directory.CreateDirectory(Path.Combine(dir, "folder")).FullName;
            File.WriteAllText(folder + ":note", "a folder's stream");
            var set = new FileCat.Core.Search.ResultSet("t", "t", "t");
            new FileCat.Core.Search.SearchSession(new FileCat.Core.Search.SearchQuery { Roots = [dir], CarriesHiddenData = new WindowsHiddenData() }, set)
                .Run(TestContext.Current.CancellationToken);
            Assert.Equal(["carrier.exe", "folder"], set.Snapshot().Select(s => s.Item.Name).Order(StringComparer.Ordinal));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private sealed class ListSink(List<EntryData> entries) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> batch) => entries.AddRange(batch.ToArray());
        public void ReportIssue(string message) { }
    }

    [Fact]
    public async Task Bounded_inventory_and_actions_keep_ADS_EA_categories_folder_streams_and_required_roles()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows NTFS fixture.");
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "same.bin"); File.WriteAllText(file, "main");
            File.WriteAllText(file + ":SAME", "stream value");
            File.WriteAllText(file + ":$DATA", "named DATA value");
            WriteEa(file, "SAME", "ea value"u8.ToArray());
            WriteEa(file, "REQUIRED", "required value"u8.ToArray(), flags: 0x80);
            var hidden = new WindowsHiddenData();
            var initial = hidden.ReadInventory(file, new(), TestContext.Current.CancellationToken);
            Assert.False(initial.ValueBearingQueryIssued); Assert.False(initial.IsComplete); Assert.Null(initial.ExactCount);
            Assert.Equal(HiddenInventoryState.NotInspected, initial.Categories.Single(c => c.Kind == HiddenKind.NtfsAttribute).State);
            Assert.DoesNotContain(initial.Items, i => i.Kind == HiddenKind.NtfsAttribute);
            Assert.Contains(initial.Items, i => i.Name == "$DATA" && i.Kind == HiddenKind.Stream);
            var admitted = hidden.ReadInventory(file, new(IncludeEaValues: true), TestContext.Current.CancellationToken);
            Assert.True(admitted.IsComplete); Assert.True(admitted.ValueBearingQueryIssued); Assert.True(admitted.BoundToObject);
            Assert.Equal(4, admitted.ExactCount);
            var required = admitted.Items.Single(i => i.Name == "REQUIRED");
            Assert.Equal(0x80, required.NativeFlags); Assert.Equal(HiddenMetadataRole.RequiredMetadata, required.Role);
            Assert.Throws<NotSupportedException>(() => hidden.Delete(file, required with { NativeFlags = 0, Role = HiddenMetadataRole.Ordinary }));
            var provider = new HiddenDataProvider(hidden); var location = HiddenDataProvider.Of(file);
            var streamRef = new ItemRef(location, "SAME", EntryKind.File) { Ordinal = 1, SupplementarySourceIdentity = admitted.Identity };
            var eaRef = new ItemRef(location, "SAME", EntryKind.File) { Ordinal = 3, SupplementarySourceIdentity = admitted.Identity };
            using (var stream = provider.OpenContent(streamRef)!)
            { byte[] buffer = new byte[64]; Assert.Equal("stream value", Encoding.UTF8.GetString(buffer, 0, stream.Read(0, buffer))); }
            using (var ea = provider.OpenContent(eaRef)!)
            { byte[] buffer = new byte[64]; Assert.Equal("ea value", Encoding.UTF8.GetString(buffer, 0, ea.Read(0, buffer))); }
            hidden.Delete(file, provider.Find(file, eaRef, TestContext.Current.CancellationToken)!);
            Assert.Null(provider.Find(file, eaRef, TestContext.Current.CancellationToken)); Assert.NotNull(provider.Find(file, streamRef, TestContext.Current.CancellationToken));
            Assert.Equal("named DATA value", Encoding.UTF8.GetString(hidden.Read(file, admitted.Items.Single(i => i.Name == "$DATA"), 64)));
            string folder = Directory.CreateDirectory(Path.Combine(dir, "folder")).FullName;
            File.WriteAllText(folder + ":note", "directory stream");
            var note = Assert.Single(hidden.ReadInventory(folder, new(), TestContext.Current.CancellationToken).Items);
            Assert.Equal("directory stream", Encoding.UTF8.GetString(hidden.Read(folder, note, 64)));
            hidden.Delete(folder, note);
            Assert.Empty(hidden.ReadInventory(folder, new(), TestContext.Current.CancellationToken).Items);
            Assert.Equal("main", File.ReadAllText(file));
            await Task.CompletedTask;
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Actual_WOF_backing_is_not_exposed_to_generic_supplementary_delete()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows WOF fixture.");
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "wof.bin"); File.WriteAllText(file, new string('w', 256 * 1024));
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "compact.exe"))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "/C", "/EXE:LZX", "/F", "/I", "/Q", file }) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            Assert.True(process.ExitCode == 0, await output + await error);
            using (var handle = CreateFile(file, 0x80 /* READ_ATTRIBUTES */, 7, 0, 3, 0x00200000 /* OPEN_REPARSE_POINT */, 0))
            {
                var backing = WindowsStorageBacking.Query(handle, TestContext.Current.CancellationToken);
                Assert.True(backing.External, "compact did not produce held-query WOF backing: " + backing.Reason);
                Assert.Equal(2u, backing.Provider);
            }
            var hidden = new WindowsHiddenData();
            var inventory = hidden.ReadInventory(file, new(), TestContext.Current.CancellationToken);
            Assert.Equal(HiddenInventoryState.Unsupported, Assert.Single(inventory.Categories).State);
            Assert.Throws<NotSupportedException>(() => hidden.Delete(file, new("WofCompressedData", HiddenKind.Stream, 0)));

            var ct = TestContext.Current.CancellationToken;
            var records = new WindowsFileRecords();
            var summary = records.ReadSummary(file, ct);
            var full = records.ReadFull(file, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            Assert.Equal(summary.Identity, full.Summary.Identity);
            Assert.Equal(FactState.Known, full.Summary.Compression.State);
            Assert.False(full.Summary.Compression.Value);
            var backingSection = Assert.Single(full.Technical.Sections, section => section.Title == "Windows Overlay Filter backing");
            Assert.Contains(backingSection.Fields, field => field.Name == "Backing" && field.Value.Contains("LZX", StringComparison.Ordinal));
            Assert.Contains(("Observation", "FSCTL_GET_EXTERNAL_BACKING (held object)"), backingSection.Fields);
            Assert.DoesNotContain(full.Technical.Sections, section => section.Title is
                "Main-stream extent records" or "Main-stream allocated byte ranges");
            Assert.DoesNotContain(new string('w', 32), full.ToText());
            Assert.Equal(new string('w', 256 * 1024), File.ReadAllText(file));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// The Extra data column's inventory reads no EA name or value, yet FileEaInformation's size lets an object without
    /// EAs count exactly: zero for a plain file or folder, its streams for a downloaded file. An object with EAs counts as
    /// at least one more than its streams.
    /// </summary>
    [Fact]
    public void Without_EA_admission_an_object_with_no_EAs_still_counts_exactly()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows NTFS fixture.");
        string dir = NewFolder();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var native = new WindowsHiddenData();
            string plain = Path.Combine(dir, "plain.txt"), downloaded = Path.Combine(dir, "downloaded.txt"), carrier = Path.Combine(dir, "ea.txt");
            File.WriteAllText(plain, "plain");
            File.WriteAllText(downloaded, "main");
            File.WriteAllText(downloaded + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            File.WriteAllText(carrier, "main");
            WriteEa(carrier, "FILECAT.COLUMN", "ea value"u8.ToArray());

            foreach (string path in new[] { plain, dir })
            {
                var none = native.ReadInventory(path, new(), ct);
                Assert.Equal(("0", (int?)0, false), (none.CountText, none.ExactCount, none.ValueBearingQueryIssued));
                Assert.Equal(HiddenInventoryState.Complete, none.Categories.Single(c => c.Kind == HiddenKind.NtfsAttribute).State);
            }
            var stream = native.ReadInventory(downloaded, new(), ct);
            Assert.Equal(("1", (int?)1), (stream.CountText, stream.ExactCount));

            var withEa = native.ReadInventory(carrier, new(), ct);
            Assert.False(withEa.ValueBearingQueryIssued);
            Assert.Empty(withEa.Items);
            var eas = withEa.Categories.Single(c => c.Kind == HiddenKind.NtfsAttribute);
            Assert.Equal((HiddenInventoryState.NotInspected, 1), (eas.State, eas.Count));
            Assert.Contains("FileEaInformation reports", eas.Reason);
            Assert.Equal(("1+", (int?)null), (withEa.CountText, withEa.ExactCount));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// F-EXTRA: stream inventory, Find, F3 view, value report, F5 output and F8 delete issue no EA query. A deny on
    /// FILE_READ_EA makes that native: the EA-admitted request is the control and must be refused as typed denial.
    /// </summary>
    [Fact]
    public async Task Stream_actions_and_Find_issue_no_EA_query_under_an_EA_read_deny()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows NTFS fixture.");
        string dir = NewFolder();
        string file = Path.Combine(dir, "carrier.bin");
        FileSecurity? original = null;
        try
        {
            var ct = TestContext.Current.CancellationToken;
            File.WriteAllText(file, "main data");
            File.WriteAllText(file + ":payload", "stream value");
            File.WriteAllText(file + ":second", "second stream");
            WriteEa(file, "FILECAT.SECRET", "ea value"u8.ToArray());
            var native = new WindowsHiddenData();
            var provider = new HiddenDataProvider(native);
            var info = new FileInfo(file);
            original = info.GetAccessControl();
            var denied = info.GetAccessControl();
            using (var user = WindowsIdentity.GetCurrent())
                denied.AddAccessRule(new FileSystemAccessRule(user.User!, FileSystemRights.ReadExtendedAttributes, AccessControlType.Deny));
            info.SetAccessControl(denied);

            // Control: the EA-admitted inventory asks for FILE_READ_EA and is refused as typed denial, not as "no EAs".
            var control = Record.Exception(() => native.ReadInventory(file, new(IncludeEaValues: true), ct));
            Assert.SkipWhen(control is null, "This process read EAs despite the deny entry (an enabled backup privilege bypasses it).");
            Assert.IsType<UnauthorizedAccessException>(control);
            // The combined legacy list hides the same denial as an empty EA set, so the deny alone cannot catch a fallback to it.
            Assert.DoesNotContain(native.List(file), item => item.Kind == HiddenKind.NtfsAttribute);

            var inventory = native.ReadInventory(file, new(), ct);
            Assert.False(inventory.ValueBearingQueryIssued);
            Assert.Equal(["payload", "second"], inventory.Items.Select(i => i.Name).Order(StringComparer.Ordinal));
            Assert.All(inventory.Items, item => Assert.Equal(HiddenKind.Stream, item.Kind));
            Assert.Equal(HiddenInventoryState.Complete, inventory.Categories.Single(c => c.Kind == HiddenKind.Stream).State);
            Assert.Equal(HiddenInventoryState.NotInspected, inventory.Categories.Single(c => c.Kind == HiddenKind.NtfsAttribute).State);
            var identity = inventory.Identity!.Value;
            var location = HiddenDataProvider.Of(file);
            var stream = new ItemRef(location, "payload", EntryKind.File)
                { Ordinal = HiddenIdentities.Ordinal(HiddenKind.Stream), SupplementarySourceIdentity = identity };
            var ea = new ItemRef(location, "FILECAT.SECRET", EntryKind.File)
                { Ordinal = HiddenIdentities.Ordinal(HiddenKind.NtfsAttribute), SupplementarySourceIdentity = identity };

            Assert.Equal(HiddenKind.Stream, provider.Find(file, stream, ct)!.Kind);
            Assert.Equal("stream value", ReadAll(provider.OpenContent(stream)!));
            Assert.Contains("stream value", provider.ReadValueReport(stream, identity, ct).Text);
            Assert.Equal("stream value", ReadAll(((IUnprotectedContentOutput)provider).OpenUnprotectedOutput(stream, ContentOutputKind.Extraction)!));
            var names = new InventorySink(includeEaValues: false);
            await provider.EnumerateAsync(location, names, ct);
            Assert.Equal(["payload", "second"], names.Entries.Select(e => e.Name).Order(StringComparer.Ordinal));

            // Find over the folder: a complete, positive ADS-only result.
            var set = new ResultSet("deny", "deny", "native fixture");
            var search = new SearchSession(new SearchQuery { Roots = [dir], CarriesHiddenData = native }, set);
            search.Run(ct);
            Assert.Equal("carrier.bin", Assert.Single(set.Snapshot()).Item.Name);
            Assert.Equal(0, search.ExtraDataUnexamined);
            Assert.True(set.IsComplete);

            // The EA category itself stays a typed refusal through every route that admits it.
            Assert.Throws<UnauthorizedAccessException>(() => provider.Find(file, ea, ct));
            Assert.Throws<UnauthorizedAccessException>(() => provider.OpenContent(ea));
            var admitted = new InventorySink(includeEaValues: true);
            await provider.EnumerateAsync(location, admitted, ct);
            Assert.Empty(admitted.Entries);
            Assert.Equal(HiddenInventoryState.Denied, Assert.Single(admitted.Inventory!.Categories).State);

            // F8 keeps the chosen kind under the deny: the stream goes and the EA stays.
            provider.DeleteReviewedValue(stream, identity, ct);
            Assert.Equal(["second"], native.ReadInventory(file, new(), ct).Items.Select(i => i.Name));
            Restore(file, original);
            original = null;
            var restored = native.ReadInventory(file, new(IncludeEaValues: true), ct);
            var secret = Assert.Single(restored.Items, i => i.Kind == HiddenKind.NtfsAttribute);
            Assert.Equal("FILECAT.SECRET", secret.Name);
            Assert.Equal("ea value", Encoding.UTF8.GetString(native.Read(file, secret, 64)));
            Assert.Equal("main data", File.ReadAllText(file));
        }
        finally
        {
            if (original is not null) Restore(file, original);
            Directory.Delete(dir, recursive: true);
        }

        static void Restore(string path, FileSecurity security)
        {
            var restored = new FileSecurity();
            restored.SetSecurityDescriptorBinaryForm(security.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            new FileInfo(path).SetAccessControl(restored);
        }
    }

    /// <summary>
    /// F-EXTRA: Find's extra-data predicate and an exact stream lookup use only the names-only inventory. A counting
    /// wrapper catches what the EA deny cannot: any call of the combined legacy list, value read or open.
    /// </summary>
    [Fact]
    public async Task Find_and_stream_lookup_use_only_the_names_only_inventory()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows NTFS fixture.");
        string dir = NewFolder();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string carrier = Path.Combine(dir, "carrier.exe"), eaOnly = Path.Combine(dir, "ea-only.bin");
            File.WriteAllText(carrier, "MZ");
            File.WriteAllText(carrier + ":payload", "hidden");
            File.WriteAllText(Path.Combine(dir, "downloaded.exe"), "MZ");
            File.WriteAllText(Path.Combine(dir, "downloaded.exe") + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            File.WriteAllText(eaOnly, "x");
            WriteEa(eaOnly, "FILECAT.ONLY", "ea value"u8.ToArray());
            File.WriteAllText(Path.Combine(dir, "plain.txt"), "x");
            string folder = Directory.CreateDirectory(Path.Combine(dir, "folder")).FullName;
            File.WriteAllText(folder + ":note", "a folder's stream");
            var counting = new CountingHiddenData(new WindowsHiddenData());

            var set = new ResultSet("count", "count", "native fixture");
            var search = new SearchSession(new SearchQuery { Roots = [dir], CarriesHiddenData = counting }, set);
            search.Run(ct);
            // ADS-only scope: the EA-only file is a complete negative, the download mark alone is not a match.
            Assert.Equal(["carrier.exe", "folder"], set.Snapshot().Select(r => r.Item.Name).Order(StringComparer.Ordinal));
            Assert.True(set.IsComplete);
            Assert.Equal(0, search.ExtraDataUnexamined);
            Assert.Equal((5, 0, 0, 0, 0), (counting.StreamInventories, counting.EaInventories, counting.Lists, counting.Reads, counting.Opens));

            var provider = new HiddenDataProvider(counting);
            var identity = counting.ReadInventory(carrier, new(), ct).Identity!.Value;
            var stream = new ItemRef(HiddenDataProvider.Of(carrier), "payload", EntryKind.File)
                { Ordinal = HiddenIdentities.Ordinal(HiddenKind.Stream), SupplementarySourceIdentity = identity };
            Assert.Equal(HiddenKind.Stream, provider.Find(carrier, stream, ct)!.Kind);
            await provider.EnumerateAsync(HiddenDataProvider.Of(carrier), new InventorySink(includeEaValues: false), ct);
            Assert.Equal((8, 0, 0, 0, 0), (counting.StreamInventories, counting.EaInventories, counting.Lists, counting.Reads, counting.Opens));

            // Positive control: an EA lookup admits exactly one value-bearing inventory, and still no legacy list.
            var ea = new ItemRef(HiddenDataProvider.Of(eaOnly), "FILECAT.ONLY", EntryKind.File) { Ordinal = HiddenIdentities.Ordinal(HiddenKind.NtfsAttribute) };
            Assert.Equal(HiddenKind.NtfsAttribute, provider.Find(eaOnly, ea, ct)!.Kind);
            Assert.Equal((8, 1, 0, 0, 0), (counting.StreamInventories, counting.EaInventories, counting.Lists, counting.Reads, counting.Opens));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>F-EXTRA: zero-length streams on a file and on a folder are listed, read as empty, found, and deleted by kind.</summary>
    [Fact]
    public void Empty_streams_on_files_and_folders_are_listed_read_found_and_deleted()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows NTFS fixture.");
        string dir = NewFolder();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string file = Path.Combine(dir, "empty-carrier.bin");
            string folder = Directory.CreateDirectory(Path.Combine(dir, "empty-folder")).FullName;
            File.WriteAllText(file, "main data");
            File.WriteAllBytes(file + ":empty", []);
            File.WriteAllBytes(folder + ":empty", []);
            var native = new WindowsHiddenData();
            var provider = new HiddenDataProvider(native);
            foreach (string subject in new[] { file, folder })
            {
                var inventory = native.ReadInventory(subject, new(), ct);
                Assert.True(inventory.Categories.Single(c => c.Kind == HiddenKind.Stream).State == HiddenInventoryState.Complete, subject);
                var item = Assert.Single(inventory.Items);
                Assert.Equal(("empty", HiddenKind.Stream, 0L), (item.Name, item.Kind, item.Size));
                var reference = new ItemRef(HiddenDataProvider.Of(subject), "empty", EntryKind.File)
                    { Ordinal = HiddenIdentities.Ordinal(HiddenKind.Stream), SupplementarySourceIdentity = inventory.Identity };
                using (var content = provider.OpenContent(reference)!)
                {
                    Assert.Equal(0, content.Length);
                    Assert.Equal(0, content.Read(0, new byte[16]));
                }
                Assert.Matches(@"Bytes\s+0", provider.ReadValueReport(reference, inventory.Identity!.Value, ct).Text);
            }

            // Presence, not size, is the criterion: both carriers of an empty stream are found.
            var set = new ResultSet("empty", "empty", "native fixture");
            new SearchSession(new SearchQuery { Roots = [dir], CarriesHiddenData = native }, set).Run(ct);
            Assert.Equal(["empty-carrier.bin", "empty-folder"], set.Snapshot().Select(r => r.Item.Name).Order(StringComparer.Ordinal));
            Assert.True(set.IsComplete);

            foreach (string subject in new[] { file, folder })
            {
                var identity = native.ReadInventory(subject, new(), ct).Identity!.Value;
                provider.DeleteReviewedValue(new ItemRef(HiddenDataProvider.Of(subject), "empty", EntryKind.File)
                    { Ordinal = HiddenIdentities.Ordinal(HiddenKind.Stream), SupplementarySourceIdentity = identity }, identity, ct);
                Assert.Empty(native.ReadInventory(subject, new(), ct).Items);
            }
            Assert.Equal("main data", File.ReadAllText(file));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// F-EXTRA: native names round-trip or refuse. A non-BMP stream name is listed, read and deleted exactly; NTFS
    /// stores an EA name upper-cased, so only the listed spelling resolves; and a stream name that is not valid UTF-16
    /// makes the stream category refuse every action and leaves Find's candidate unexamined.
    /// </summary>
    [Fact]
    public void Native_names_round_trip_exactly_or_the_category_refuses()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows NTFS fixture.");
        string dir = NewFolder();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var native = new WindowsHiddenData();
            var provider = new HiddenDataProvider(native);
            string exact = Path.Combine(dir, "exact.bin");
            const string unicode = "Ünïcödé 名前 \U0001F600 stream";
            File.WriteAllText(exact, "main");
            File.WriteAllText(exact + ":" + unicode, "unicode value");
            WriteEa(exact, "filecat.lower", "ea value"u8.ToArray());
            var inventory = native.ReadInventory(exact, new(IncludeEaValues: true), ct);
            Assert.True(inventory.IsComplete);
            Assert.Equal(unicode, Assert.Single(inventory.Items, i => i.Kind == HiddenKind.Stream).Name);
            Assert.Equal("FILECAT.LOWER", Assert.Single(inventory.Items, i => i.Kind == HiddenKind.NtfsAttribute).Name);
            var identity = inventory.Identity!.Value;
            var location = HiddenDataProvider.Of(exact);
            ItemRef Ref(string name, HiddenKind kind) => new(location, name, EntryKind.File)
                { Ordinal = HiddenIdentities.Ordinal(kind), SupplementarySourceIdentity = identity };
            Assert.Equal("unicode value", ReadAll(provider.OpenContent(Ref(unicode, HiddenKind.Stream))!));
            Assert.Equal("ea value", ReadAll(provider.OpenContent(Ref("FILECAT.LOWER", HiddenKind.NtfsAttribute))!));
            Assert.Null(provider.Find(exact, Ref("filecat.lower", HiddenKind.NtfsAttribute), ct));
            provider.DeleteReviewedValue(Ref(unicode, HiddenKind.Stream), identity, ct);
            Assert.Equal("FILECAT.LOWER", Assert.Single(native.ReadInventory(exact, new(IncludeEaValues: true), ct).Items).Name);

            string broken = Path.Combine(dir, "broken.bin");
            const string lone = "lone \uD800 half";
            File.WriteAllText(broken, "main");
            File.WriteAllText(broken + ":" + lone, "undecodable name");
            var refused = native.ReadInventory(broken, new(), ct);
            var streams = refused.Categories.Single(c => c.Kind == HiddenKind.Stream);
            Assert.Equal(HiddenInventoryState.Failed, streams.State);
            Assert.Contains("not valid UTF-16", streams.Reason);
            Assert.Empty(refused.Items);
            var guess = new ItemRef(HiddenDataProvider.Of(broken), lone, EntryKind.File)
                { Ordinal = HiddenIdentities.Ordinal(HiddenKind.Stream), SupplementarySourceIdentity = refused.Identity };
            Assert.Contains("could not be completely resolved", Assert.Throws<IOException>(() => provider.Find(broken, guess, ct)).Message);
            Assert.Contains("could not be completely resolved", Assert.Throws<IOException>(() => provider.OpenContent(guess)).Message);

            var set = new ResultSet("names", "names", "native fixture");
            var search = new SearchSession(new SearchQuery { Roots = [dir], CarriesHiddenData = native }, set);
            search.Run(ct);
            Assert.Empty(set.Snapshot());
            Assert.Equal(1, search.ExtraDataUnexamined);
            Assert.False(set.IsComplete);
            Assert.Contains(search.Log, entry => entry.Kind == SearchLogKind.Warning &&
                string.Equals(entry.Path, broken, StringComparison.OrdinalIgnoreCase) && entry.Detail!.Contains("not valid UTF-16"));
            Assert.Equal("undecodable name", File.ReadAllText(broken + ":" + lone));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// F-EXTRA: growth stays typed. About 176 KB of stream records outgrow the first 64 KiB FileStreamInfo buffer and
    /// complete in the next; 1,025 streams stop at the 1,024-entry limit as a partial category that refuses actions,
    /// while Find still counts the names it saw as a positive. (NTFS itself refuses a 606th stream with a 200-character
    /// name once its attribute list is full, so the 1 MiB attempt is reachable only in a narrow band.)
    /// </summary>
    [Fact]
    public void Stream_inventory_grows_its_buffer_and_stops_typed_at_the_entry_limit()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows NTFS fixture.");
        string dir = NewFolder();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var native = new WindowsHiddenData();
            string wide = Path.Combine(dir, "wide.bin"), many = Path.Combine(dir, "many.bin");
            File.WriteAllText(wide, "main");
            File.WriteAllText(many, "main");
            // 400 records of 440 bytes: more than the first 64 KiB attempt, within the 256 KiB one.
            for (int i = 0; i < 400; i++) File.WriteAllBytes($"{wide}:{i:D4}{new string('w', 196)}", []);
            for (int i = 0; i <= HiddenInventory.MaxEntries; i++) File.WriteAllBytes($"{many}:n{i:D4}", []);

            // Control: the adapter's first 64 KiB FileStreamInfo attempt is too small for this file.
            using (var handle = CreateFile(wide, 0x80 /* READ_ATTRIBUTES */, 7, 0, 3, 0x02200000 /* backup semantics, open reparse point */, 0))
            {
                Assert.False(handle.IsInvalid, $"open: {Marshal.GetLastPInvokeError()}");
                byte[] first = new byte[64 * 1024];
                bool fitted = GetStreamInfo(handle, 7 /* FileStreamInfo */, first, (uint)first.Length);
                int error = Marshal.GetLastPInvokeError();
                Assert.False(fitted);
                Assert.True(error is 122 or 234, $"FileStreamInfo: {error}");
            }
            var grown = native.ReadInventory(wide, new(), ct);
            Assert.Equal(HiddenInventoryState.Complete, grown.Categories.Single(c => c.Kind == HiddenKind.Stream).State);
            Assert.Equal(400, grown.Items.Length);
            Assert.All(grown.Items, item => Assert.Equal(200, item.Name.Length));

            var capped = native.ReadInventory(many, new(), ct);
            var streams = capped.Categories.Single(c => c.Kind == HiddenKind.Stream);
            Assert.Equal(HiddenInventoryState.Partial, streams.State);
            Assert.Contains("1,024-entry", streams.Reason);
            Assert.Equal(HiddenInventory.MaxEntries, capped.Items.Length);
            Assert.Null(capped.ExactCount);
            var provider = new HiddenDataProvider(native);
            var listed = new ItemRef(HiddenDataProvider.Of(many), capped.Items[0].Name, EntryKind.File)
                { Ordinal = HiddenIdentities.Ordinal(HiddenKind.Stream), SupplementarySourceIdentity = capped.Identity };
            Assert.Contains("could not be completely resolved", Assert.Throws<IOException>(() => provider.Find(many, listed, ct)).Message);
            Assert.Contains("could not be completely resolved", Assert.Throws<IOException>(() => provider.OpenContent(listed)).Message);

            var set = new ResultSet("growth", "growth", "native fixture");
            var search = new SearchSession(new SearchQuery { Roots = [dir], CarriesHiddenData = native }, set);
            search.Run(ct);
            Assert.Equal(["many.bin", "wide.bin"], set.Snapshot().Select(r => r.Item.Name).Order(StringComparer.Ordinal));
            Assert.Equal(0, search.ExtraDataUnexamined);
            Assert.True(set.IsComplete);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string ReadAll(IContentSource source)
    {
        using (source)
        {
            var buffer = new byte[4096];
            int total = 0, read;
            while (total < buffer.Length && (read = source.Read(total, buffer.AsSpan(total))) > 0) total += read;
            return Encoding.UTF8.GetString(buffer, 0, total);
        }
    }

    private sealed class InventorySink(bool includeEaValues) : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public HiddenInventory? Inventory { get; private set; }
        public bool IncludeSupplementaryEaValues => includeEaValues;
        public void ReportSupplementaryInventory(HiddenInventory inventory) => Inventory = inventory;
        public void AddBatch(ReadOnlySpan<EntryData> batch) => Entries.AddRange(batch.ToArray());
        public void ReportIssue(string message) { }
    }

    /// <summary>Delegates to the native adapter and counts every entry point by kind of request.</summary>
    private sealed class CountingHiddenData(IHiddenData inner) : IHiddenData
    {
        private int _streamInventories, _eaInventories, _lists, _reads, _opens;
        public int StreamInventories => Volatile.Read(ref _streamInventories);
        public int EaInventories => Volatile.Read(ref _eaInventories);
        public int Lists => Volatile.Read(ref _lists);
        public int Reads => Volatile.Read(ref _reads);
        public int Opens => Volatile.Read(ref _opens);
        public bool IsSupported => inner.IsSupported;
        public bool SupportsEaValueQuery => inner.SupportsEaValueQuery;
        public IReadOnlyList<HiddenItem> List(string path)
        {
            Interlocked.Increment(ref _lists);
            return inner.List(path);
        }
        public HiddenInventory ReadInventory(string path, HiddenInventoryRequest request, CancellationToken ct)
        {
            if (request.IncludeEaValues) Interlocked.Increment(ref _eaInventories);
            else Interlocked.Increment(ref _streamInventories);
            return inner.ReadInventory(path, request, ct);
        }
        public byte[] Read(string path, HiddenItem item, int max)
        {
            Interlocked.Increment(ref _reads);
            return inner.Read(path, item, max);
        }
        public Stream Open(string path, HiddenItem item)
        {
            Interlocked.Increment(ref _opens);
            return inner.Open(path, item);
        }
        public void Delete(string path, HiddenItem item) => throw new InvalidOperationException("No deletion through the counting fixture.");
    }

    /// <summary>Writes an EA as FILE_FULL_EA_INFORMATION: next offset, flags, name length, value length, name, NUL, value.</summary>
    private static unsafe void WriteEa(string path, string name, byte[] value, byte flags = 0)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        var buffer = new byte[(8 + nameBytes.Length + 1 + value.Length + 3) & ~3];
        buffer[4] = flags;
        buffer[5] = (byte)nameBytes.Length;
        BitConverter.TryWriteBytes(buffer.AsSpan(6), (ushort)value.Length);
        nameBytes.CopyTo(buffer, 8);
        value.CopyTo(buffer, 8 + nameBytes.Length + 1);
        using var handle = CreateFile(path, 0x0010 /* FILE_WRITE_EA */, 7, 0, 3, 0, 0);
        Assert.False(handle.IsInvalid, $"open: {Marshal.GetLastPInvokeError()}");
        fixed (byte* start = buffer)
        {
            int status = NtSetEaFile(handle, out _, start, (uint)buffer.Length);
            Assert.True(status >= 0, $"NtSetEaFile: 0x{status:X8}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public nint Status;
        public nint Information;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("ntdll.dll")]
    private static unsafe partial int NtSetEaFile(SafeFileHandle file, out IoStatusBlock status, byte* buffer, uint length);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetStreamInfo(SafeFileHandle file, int infoClass, [Out] byte[] buffer, uint size);
}
