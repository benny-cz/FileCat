using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Core.Inspect;

/// <summary>
/// Android packages: APK (binary XML manifest) and AAB (protocol-buffer manifest). Package name, versions, SDK levels,
/// permissions, components, debuggable, ABIs, and whether a signature is present (never verified). The ZIP is read under
/// the same entry and size limits as elsewhere; nothing is executed or decompiled.
/// </summary>
public static class ApkInspector
{
    private const int MaxEntries = 200_000;
    private const int MaxManifest = 4 * 1024 * 1024;
    private const int MaxPermissions = 200;

    public static InspectionReport? Inspect(IContentSource source, CancellationToken ct)
    {
        var r = new ContentReader(source);
        var head = r.Read(0, 4);
        if (head.Length < 4 || head[0] != 'P' || head[1] != 'K' || head[2] != 3 || head[3] != 4) return null;
        long cdOffset = CentralDirectory(r, out int declaredEntries);
        if (declaredEntries > MaxEntries) return null;
        ZipArchive zip;
        try { zip = new ZipArchive(new ContentStream(source), ZipArchiveMode.Read, leaveOpen: true); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException) { return null; }
        using (zip)
        {
            // The entry list is read on first use; a damaged one means this is no package FileCat can describe.
            ZipArchiveEntry? apkManifest, aabManifest;
            List<string> entries;
            try
            {
                apkManifest = zip.GetEntry("AndroidManifest.xml");
                aabManifest = apkManifest is null ? zip.GetEntry("base/manifest/AndroidManifest.xml") : null;
                if (apkManifest is null && aabManifest is null) return null;
                entries = zip.Entries.Take(MaxEntries).Select(e => e.FullName).ToList();
            }
            catch (InvalidDataException) { return null; }
            var warnings = new List<string>();
            var manifest = new Manifest();
            byte[]? data;
            try { data = ReadEntry(apkManifest ?? aabManifest!, warnings); }
            catch (InvalidDataException ex)
            {
                warnings.Add("The manifest cannot be read: " + ex.Message);
                data = null;
            }
            if (data is not null)
            {
                try
                {
                    if (apkManifest is not null) BinaryXml.Parse(data, manifest, ct);
                    else ProtoXml.Parse(data, manifest, ct);
                }
                catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidDataException or OverflowException)
                {
                    warnings.Add("The manifest is damaged: " + ex.Message);
                }
            }
            var package = new List<(string, string)>();
            void Add(string name, string? value)
            {
                if (!string.IsNullOrEmpty(value)) package.Add((name, value));
            }
            Add("Package", manifest.Package);
            Add("Version", manifest.VersionName is null ? manifest.VersionCode : $"{manifest.VersionName} (code {manifest.VersionCode ?? "?"})");
            Add("Minimum SDK", Sdk(manifest.MinSdk));
            Add("Target SDK", Sdk(manifest.TargetSdk));
            Add("Application label", manifest.Label);
            if (manifest.Debuggable) package.Add(("Debuggable", "yes: a debug build"));
            if (manifest.AllowBackup is { } backup) package.Add(("Allows backup", backup ? "yes" : "no"));
            var sections = new List<InspectionSection> { new("Package", package) };
            if (manifest.Components.Count > 0)
                sections.Add(new InspectionSection("Components", manifest.Components.Select(kv => (kv.Key, $"{kv.Value.Count:N0}{(kv.Value.Exported > 0 ? $" ({kv.Value.Exported} exported)" : "")}")).ToList()));
            if (manifest.Permissions.Count > 0)
                sections.Add(new InspectionSection($"Permissions ({manifest.Permissions.Count})", manifest.Permissions.Select(p => (p, string.Empty)).ToList()));

            // Contents: dex files, native code, modules.
            var contents = new List<(string, string)>();
            int dex = entries.Count(n => n.EndsWith(".dex", StringComparison.OrdinalIgnoreCase));
            if (dex > 0) contents.Add(("Dex files", dex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var abis = entries.Select(n => n.Split('/')).Where(p => p.Length >= 3 && p[^3] == "lib").Select(p => p[^2]).Distinct().Order().ToList();
            if (abis.Count > 0) contents.Add(("Native code", string.Join(", ", abis)));
            if (aabManifest is not null)
            {
                var modules = entries.Where(n => n.EndsWith("/manifest/AndroidManifest.xml", StringComparison.Ordinal)).Select(n => n.Split('/')[0]).Order().ToList();
                contents.Add(("Modules", string.Join(", ", modules)));
            }
            contents.Add(("Entries", entries.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)));
            sections.Add(new InspectionSection("Contents", contents));

            // Signatures: JAR signing (v1) files, and the APK Signing Block (v2+) just before the central directory.
            bool v1 = entries.Any(n => n.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) &&
                                       (n.EndsWith(".RSA", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".DSA", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".EC", StringComparison.OrdinalIgnoreCase)));
            bool block = cdOffset >= 16 && r.Read(cdOffset - 16, 16) is { Length: 16 } magic && magic.AsSpan().SequenceEqual("APK Sig Block 42"u8);
            sections.Add(new InspectionSection("Signature", [
                ("Status", v1 || block ? "Present, not verified: FileCat does not check signatures or trust here" : "No signature"),
                ("Schemes", string.Join(", ", new[] { v1 ? "JAR (v1)" : null, block ? "APK Signing Block (v2 or later)" : null }.OfType<string>()) is { Length: > 0 } schemes ? schemes : "none"),
            ]));
            return new InspectionReport(apkManifest is not null ? "Android package (APK)" : "Android App Bundle (AAB)", sections, warnings);
        }
    }

    private static string? Sdk(string? level) => level is null ? null : int.TryParse(level, out int n) ? $"{n} ({AndroidVersion(n)})" : level;

    private static string AndroidVersion(int api) => api switch
    {
        >= 36 => "Android 16 or later",
        35 => "Android 15",
        34 => "Android 14",
        33 => "Android 13",
        32 or 31 => "Android 12",
        30 => "Android 11",
        29 => "Android 10",
        28 => "Android 9",
        27 or 26 => "Android 8",
        25 or 24 => "Android 7",
        23 => "Android 6",
        22 or 21 => "Android 5",
        >= 14 => "Android 4",
        _ => "Android 1–3",
    };

    private static byte[]? ReadEntry(ZipArchiveEntry entry, List<string> warnings)
    {
        if (entry.Length > MaxManifest)
        {
            warnings.Add("The manifest is too large to read here.");
            return null;
        }
        using var s = entry.Open();
        var buffer = new byte[(int)Math.Max(0, entry.Length)];
        int total = 0, n;
        while (total < buffer.Length && (n = s.Read(buffer, total, buffer.Length - total)) > 0) total += n;
        return buffer[..total];
    }

    /// <summary>The central directory's offset and entry count from the end-of-central-directory record, or -1.</summary>
    private static long CentralDirectory(ContentReader r, out int entries)
    {
        entries = 0;
        long length = r.Length;
        if (length < 22) return -1;
        int window = (int)Math.Min(length, 66 * 1024);
        var tail = r.Read(length - window, window);
        for (int i = tail.Length - 22; i >= 0; i--)
        {
            if (tail[i] != 'P' || tail[i + 1] != 'K' || tail[i + 2] != 5 || tail[i + 3] != 6) continue;
            entries = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 10));
            return BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 16));
        }
        return -1;
    }

    internal sealed class Manifest
    {
        public string? Package, VersionCode, VersionName, MinSdk, TargetSdk, Label;
        public bool Debuggable;
        public bool? AllowBackup;
        public List<string> Permissions { get; } = [];
        public SortedDictionary<string, (int Count, int Exported)> Components { get; } = new(StringComparer.Ordinal);

        /// <summary>One element with its attributes (by name, falling back to known resource IDs).</summary>
        public void Element(string name, IReadOnlyList<(string Name, string? Value)> attributes)
        {
            string? A(string key) => attributes.FirstOrDefault(a => a.Name == key).Value;
            switch (name)
            {
                case "manifest":
                    Package ??= A("package");
                    VersionCode ??= A("versionCode");
                    VersionName ??= A("versionName");
                    break;
                case "uses-sdk":
                    MinSdk ??= A("minSdkVersion");
                    TargetSdk ??= A("targetSdkVersion");
                    break;
                case "uses-permission" or "uses-permission-sdk-23" when A("name") is { } permission && Permissions.Count < MaxPermissions:
                    Permissions.Add(permission);
                    break;
                case "application":
                    Label ??= A("label");
                    Debuggable |= A("debuggable") is "true";
                    if (A("allowBackup") is { } backup) AllowBackup = backup == "true";
                    break;
                case "activity" or "activity-alias" or "service" or "receiver" or "provider":
                    var (count, exported) = Components.GetValueOrDefault(name);
                    Components[name] = (count + 1, exported + (A("exported") is "true" ? 1 : 0));
                    break;
            }
        }
    }

    /// <summary>Android's binary XML (AXML) as found in APKs.</summary>
    internal static class BinaryXml
    {
        private static readonly Dictionary<uint, string> KnownIds = new()
        {
            [0x0101021B] = "versionCode", [0x0101021C] = "versionName", [0x0101020C] = "minSdkVersion", [0x01010270] = "targetSdkVersion",
            [0x01010003] = "name", [0x0101000F] = "debuggable", [0x01010001] = "label", [0x01010280] = "allowBackup", [0x01010010] = "exported",
        };

        public static void Parse(byte[] data, Manifest manifest, CancellationToken ct)
        {
            if (data.Length < 8 || BinaryPrimitives.ReadUInt16LittleEndian(data) != 0x0003) throw new InvalidDataException("not binary XML");
            string[] strings = [];
            uint[] resourceIds = [];
            int pos = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
            int chunks = 0;
            while (pos + 8 <= data.Length && chunks++ < 100_000)
            {
                ct.ThrowIfCancellationRequested();
                ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos));
                ushort headerSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 2));
                int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
                if (size < 8 || pos + size > data.Length) break;
                var chunk = data.AsSpan(pos, size);
                switch (type)
                {
                    case 0x0001: strings = StringPool(chunk); break;
                    case 0x0180:
                        resourceIds = new uint[(size - headerSize) / 4];
                        for (int i = 0; i < resourceIds.Length; i++) resourceIds[i] = BinaryPrimitives.ReadUInt32LittleEndian(chunk[(headerSize + i * 4)..]);
                        break;
                    case 0x0102 when size >= 36:
                        string Str(uint index) => index < strings.Length ? strings[index] : string.Empty;
                        var name = Str(BinaryPrimitives.ReadUInt32LittleEndian(chunk[20..]));
                        int attrStart = BinaryPrimitives.ReadUInt16LittleEndian(chunk[24..]);
                        int attrSize = BinaryPrimitives.ReadUInt16LittleEndian(chunk[26..]);
                        int attrCount = BinaryPrimitives.ReadUInt16LittleEndian(chunk[28..]);
                        var attributes = new List<(string, string?)>();
                        for (int i = 0; i < attrCount && attrSize >= 20; i++)
                        {
                            int a = 16 + attrStart + i * attrSize;
                            if (a + 20 > size) break;
                            uint nameIndex = BinaryPrimitives.ReadUInt32LittleEndian(chunk[(a + 4)..]);
                            uint raw = BinaryPrimitives.ReadUInt32LittleEndian(chunk[(a + 8)..]);
                            byte dataType = chunk[a + 15];
                            uint value = BinaryPrimitives.ReadUInt32LittleEndian(chunk[(a + 16)..]);
                            string attrName = Str(nameIndex);
                            if (attrName.Length == 0 && nameIndex < resourceIds.Length && KnownIds.TryGetValue(resourceIds[nameIndex], out var known)) attrName = known;
                            string? text = dataType switch
                            {
                                0x03 => Str(value),
                                0x10 => ((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture),
                                0x11 => $"0x{value:X}",
                                0x12 => value != 0 ? "true" : "false",
                                0x01 => $"@0x{value:X8}",
                                _ => raw != 0xFFFFFFFF ? Str(raw) : null,
                            };
                            attributes.Add((attrName, text));
                        }
                        manifest.Element(name, attributes);
                        break;
                }
                pos += size;
            }
        }

        private static string[] StringPool(ReadOnlySpan<byte> chunk)
        {
            if (chunk.Length < 28) return [];
            int count = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(chunk[8..]), 200_000);
            bool utf8 = (BinaryPrimitives.ReadUInt32LittleEndian(chunk[16..]) & 0x100) != 0;
            int start = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[20..]);
            int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);
            var result = new string[count];
            Array.Fill(result, string.Empty);
            for (int i = 0; i < count; i++)
            {
                int offsetAt = headerSize + i * 4;
                if (offsetAt + 4 > chunk.Length) break;
                int at = start + (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[offsetAt..]);
                result[i] = at < 0 || at >= chunk.Length ? string.Empty : utf8 ? Utf8(chunk[at..]) : Utf16(chunk[at..]);
            }
            return result;
        }

        private static string Utf16(ReadOnlySpan<byte> s)
        {
            if (s.Length < 2) return string.Empty;
            int length = BinaryPrimitives.ReadUInt16LittleEndian(s);
            int at = 2;
            if ((length & 0x8000) != 0 && s.Length >= 4)
            {
                length = ((length & 0x7FFF) << 16) | BinaryPrimitives.ReadUInt16LittleEndian(s[2..]);
                at = 4;
            }
            int bytes = Math.Min(length * 2, s.Length - at);
            return Printable(Encoding.Unicode.GetString(s.Slice(at, Math.Max(0, bytes))));
        }

        private static string Utf8(ReadOnlySpan<byte> s)
        {
            int at = 0;
            int Length(ReadOnlySpan<byte> b)
            {
                if (at >= b.Length) return 0;
                int v = b[at++];
                if ((v & 0x80) != 0 && at < b.Length) v = ((v & 0x7F) << 8) | b[at++];
                return v;
            }
            Length(s); // UTF-16 length
            int bytes = Length(s);
            return Printable(Encoding.UTF8.GetString(s.Slice(at, Math.Max(0, Math.Min(bytes, s.Length - at)))));
        }
    }

    /// <summary>The protocol-buffer XML of App Bundles (aapt2's XmlNode, read without a schema library).</summary>
    internal static class ProtoXml
    {
        public static void Parse(byte[] data, Manifest manifest, CancellationToken ct)
        {
            int budget = 100_000;
            Node(data, manifest, 0, ref budget, ct);
        }

        private static void Node(ReadOnlySpan<byte> node, Manifest manifest, int depth, ref int budget, CancellationToken ct)
        {
            if (depth > 64 || --budget < 0) return;
            foreach (var (field, value) in Fields(node))
                if (field == 1) Element(value, manifest, depth, ref budget, ct);
        }

        private static void Element(ReadOnlySpan<byte> element, Manifest manifest, int depth, ref int budget, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string name = string.Empty;
            var attributes = new List<(string, string?)>();
            foreach (var (field, value) in Fields(element))
            {
                if (field == 3) name = Printable(Encoding.UTF8.GetString(value));
                else if (field == 4)
                {
                    string attrName = string.Empty;
                    string? attrValue = null;
                    foreach (var (f, v) in Fields(value))
                    {
                        if (f == 2) attrName = Printable(Encoding.UTF8.GetString(v));
                        else if (f == 3) attrValue = Printable(Encoding.UTF8.GetString(v));
                    }
                    attributes.Add((attrName, attrValue));
                }
            }
            manifest.Element(name, attributes);
            foreach (var (field, value) in Fields(element))
                if (field == 5) Node(value, manifest, depth + 1, ref budget, ct);
        }

        /// <summary>The length-delimited fields of a message (other wire types are skipped).</summary>
        private static List<(int Field, byte[] Value)> Fields(ReadOnlySpan<byte> message)
        {
            var list = new List<(int, byte[])>();
            int at = 0;
            while (at < message.Length && list.Count < 100_000)
            {
                ulong key = Varint(message, ref at);
                int field = (int)(key >> 3), wire = (int)(key & 7);
                switch (wire)
                {
                    case 0: Varint(message, ref at); break;
                    case 1: at += 8; break;
                    case 5: at += 4; break;
                    case 2:
                        ulong length = Varint(message, ref at);
                        if (length > (ulong)(message.Length - at)) return list;
                        list.Add((field, message.Slice(at, (int)length).ToArray()));
                        at += (int)length;
                        break;
                    default: return list;
                }
            }
            return list;
        }

        private static ulong Varint(ReadOnlySpan<byte> b, ref int at)
        {
            ulong value = 0;
            for (int shift = 0; shift < 64 && at < b.Length; shift += 7)
            {
                byte x = b[at++];
                value |= (ulong)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) return value;
            }
            return value;
        }
    }

    private static string Printable(string s) => new(s.Select(c => char.IsControl(c) ? '?' : c).ToArray());
}

/// <summary>A read-only, seekable stream over content (for parsers that need a Stream).</summary>
internal sealed class ContentStream(IContentSource source) : Stream
{
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => source.Length;

    public override long Position
    {
        get => _position;
        set => _position = Math.Max(0, value);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int n = source.Read(_position, buffer);
        if (n > 0) _position += n;
        return Math.Max(0, n);
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        _ => Length + offset,
    };

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
