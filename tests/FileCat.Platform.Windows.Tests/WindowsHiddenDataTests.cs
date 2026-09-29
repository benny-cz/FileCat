using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.HiddenData;
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

    [Fact]
    public void Streams_of_files_and_folders_are_listed_read_and_deleted()
    {
        if (!OperatingSystem.IsWindows()) return;
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
        if (!OperatingSystem.IsWindows()) return;
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
        if (!OperatingSystem.IsWindows()) return;
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
            Assert.Equal("Mark of the Web: from the Internet · https://example.com/setup.exe", tag.DetailsText);
            using var content = provider.OpenContent(new ItemRef(location, zone.Name, EntryKind.File, zone.Size))!;
            var buffer = new byte[64];
            int read = content.Read(0, buffer);
            Assert.StartsWith("[ZoneTransfer]", Encoding.UTF8.GetString(buffer, 0, read));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_path_with_a_colon_after_it_names_its_streams_as_NTFS_writes_it()
    {
        if (!OperatingSystem.IsWindows()) return;
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
        if (!OperatingSystem.IsWindows()) return;
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

    /// <summary>Writes an EA as FILE_FULL_EA_INFORMATION: next offset, flags, name length, value length, name, NUL, value.</summary>
    private static unsafe void WriteEa(string path, string name, byte[] value)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        var buffer = new byte[(8 + nameBytes.Length + 1 + value.Length + 3) & ~3];
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
