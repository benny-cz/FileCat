using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.HiddenData;
using FileCat.Core.Records;
using FileCat.Core.Resources;
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
}
