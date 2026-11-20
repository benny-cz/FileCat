using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FileCat.Core.Analysis;

/// <summary>
/// Private, uncompressed schema-1 comparison bundle (.fcdsc). Contains two ordinary
/// schema-1 .fcdsa byte streams and display state, never external references or authority.
/// The caller owns guarded publication. Checksums detect corruption, not authenticity.
/// </summary>
public static partial class AnalysisComparisonFormat
{
    private const int HeaderBytes = 128 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static InvalidDataException Invalid(string message) => new(message);

    public static void Write(Stream destination, Guid id, AnalysisSnapshot previous, AnalysisSnapshot current,
        AnalysisComparisonView view, IReadOnlyDictionary<string, string>? structuralRoots, string previousLabel,
        CancellationToken cancellation = default, IReadOnlyDictionary<string, string>? previousRootDisplayIds = null,
        IReadOnlyDictionary<string, string>? currentRootDisplayIds = null)
    {
        ArgumentNullException.ThrowIfNull(destination); ArgumentNullException.ThrowIfNull(previous); ArgumentNullException.ThrowIfNull(current);
        if (!destination.CanWrite || id == Guid.Empty) throw new ArgumentException("A writable private stream and comparison identity are required.");
        AnalysisComparisonView.Validate(view); Label(previousLabel);
        var roots = WireRoots(previous.Roots, current.Roots, structuralRoots);
        var beforeRoots = RootDisplayIds(previous.Roots, previousRootDisplayIds); var nowRoots = RootDisplayIds(current.Roots, currentRootDisplayIds);
        Guid beforeId = Guid.NewGuid(), nowId = Guid.NewGuid();
        using var beforeCount = new Counter(cancellation); using var nowCount = new Counter(cancellation);
        // Measure without retaining serialized inventories or reading any source. The
        // immutable captured inputs are encoded identically again into one sibling.
        AnalysisInventoryFormat.Write(beforeCount, previous, beforeId, cancellation);
        AnalysisInventoryFormat.Write(nowCount, current, nowId, cancellation);
        var taint = AnalysisPrivacy.Join(previous.Taint, current.Taint);
        byte[] header = Header(id, beforeId, nowId, beforeCount.Bytes, nowCount.Bytes, DateTimeOffset.UtcNow, previousLabel, taint, roots, view, beforeRoots, nowRoots);
        if (beforeCount.Bytes > AnalysisLimits.InventoryBytes - header.Length - nowCount.Bytes - 256)
            throw Invalid("Saved comparison exceeds the private bundle size limit.");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var writer = new HashWriter(destination, digest, cancellation);
        writer.Write(header); long start = writer.Bytes;
        AnalysisInventoryFormat.Write(writer, previous, beforeId, cancellation);
        if (writer.Bytes - start != beforeCount.Bytes) throw Invalid("The previous captured input changed during encoding.");
        start = writer.Bytes;
        AnalysisInventoryFormat.Write(writer, current, nowId, cancellation);
        if (writer.Bytes - start != nowCount.Bytes) throw Invalid("The current captured input changed during encoding.");
        cancellation.ThrowIfCancellationRequested();
        byte[] trailer = Encode(w => { w.WriteString("kind", "comparison-trailer"); w.WriteString("sha256", Convert.ToHexStringLower(digest.GetHashAndReset())); });
        destination.Write(trailer);
    }

    public static AnalysisSavedComparison Read(Stream source, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source); if (!source.CanRead) throw new ArgumentException("A readable private stream is required.");
        using var buffered = new BufferedStream(new NonClosing(source), 64 * 1024);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] header = Line(buffered, HeaderBytes, cancellation); digest.AppendData(header);
        var (id, beforeId, nowId, beforeBytes, nowBytes, label, saved, taint, view, roots, beforeRoots, nowRoots) = ParseHeader(header);
        AnalysisInventory? previous = null, current = null;
        try
        {
            using (var input = new Slice(buffered, beforeBytes, digest, cancellation))
            { previous = AnalysisInventoryFormat.Read(input, cancellation); if (input.Remaining != 0) throw Invalid("Previous comparison input has trailing data."); }
            using (var input = new Slice(buffered, nowBytes, digest, cancellation))
            { current = AnalysisInventoryFormat.Read(input, cancellation); if (input.Remaining != 0) throw Invalid("Current comparison input has trailing data."); }
            if (previous.InventoryId != beforeId || current.InventoryId != nowId) throw Invalid("Saved comparison input identity mismatch.");
            byte[] trailer = Line(buffered, 256, cancellation); using var end = Document(trailer);
            Fields(end.RootElement, "kind", "sha256");
            if (Text(end.RootElement, "kind") != "comparison-trailer" || Text(end.RootElement, "sha256") != Convert.ToHexStringLower(digest.GetHashAndReset()) || buffered.ReadByte() != -1)
                throw Invalid("Saved comparison checksum or final boundary is invalid.");
            var joined = AnalysisPrivacy.Join(previous.Snapshot.Taint, current.Snapshot.Taint);
            if ((joined & taint) != taint) throw Invalid("Saved comparison privacy declarations disagree with their inputs.");
            ValidateRoots(previous.Snapshot.Roots, current.Snapshot.Roots, roots);
            if (view.FocusNode != 0 && !(view.PreviousFocus ? previous.Snapshot : current.Snapshot).Nodes.Any(n => n.Id == view.FocusNode))
                throw Invalid("Saved comparison focus does not belong to its captured input.");
            cancellation.ThrowIfCancellationRequested();
            var result = new AnalysisSavedComparison(id, previous, current, view, roots, label, saved, taint,
                BindRootDisplayIds(previous.Snapshot.Roots, beforeRoots), BindRootDisplayIds(current.Snapshot.Roots, nowRoots)); previous = current = null; return result;
        }
        finally { previous?.Dispose(); current?.Dispose(); }
    }

    private static ImmutableDictionary<string, string>? WireRoots(ImmutableArray<RootBinding> previous, ImmutableArray<RootBinding> current, IReadOnlyDictionary<string, string>? roots)
    {
        if (roots is null) return null;
        ValidateRoots(previous, current, roots);
        string Wire(ImmutableArray<RootBinding> input, string id) => (input.Select((root, index) => (root, index)).Single(v => v.root.Id == id).index + 1).ToString(CultureInfo.InvariantCulture);
        return roots.ToImmutableDictionary(pair => Wire(previous, pair.Key), pair => Wire(current, pair.Value), StringComparer.Ordinal);
    }
    private static void ValidateRoots(ImmutableArray<RootBinding> previous, ImmutableArray<RootBinding> current, IReadOnlyDictionary<string, string>? roots)
    {
        if (roots is null) return;
        if (roots.Count is < 1 or > AnalysisLimits.Roots || roots.Values.Distinct(StringComparer.Ordinal).Count() != roots.Count ||
            roots.Any(pair => !previous.Any(root => root.Id == pair.Key) || !current.Any(root => root.Id == pair.Value)))
            throw Invalid("Invalid saved structural root mapping.");
    }
    private static void Label(string value)
    {
        if (value is null || !AnalysisPrivateInventoryStore.WellFormedText(value) || Utf8.GetByteCount(value) > 240)
            throw Invalid("Invalid previous comparison label.");
    }
    private static byte[] Encode(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartObject(); write(writer); writer.WriteEndObject(); }
        if (stream.Length >= HeaderBytes) throw Invalid("Saved comparison metadata limit reached.");
        stream.WriteByte(10); return stream.ToArray();
    }
    private static byte[] Line(Stream source, int maximum, CancellationToken cancellation)
    {
        using var output = new MemoryStream();
        for (int i = 0; i < maximum; i++)
        {
            cancellation.ThrowIfCancellationRequested(); int value = source.ReadByte();
            if (value < 0) throw Invalid("Saved comparison record is truncated."); output.WriteByte((byte)value);
            if (value == 10) return output.ToArray();
        }
        throw Invalid("Saved comparison record exceeds its limit.");
    }
    private static JsonDocument Document(byte[] bytes)
    {
        try { _ = Utf8.GetString(bytes); return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 }); }
        catch (Exception error) when (error is JsonException or DecoderFallbackException) { throw Invalid("Invalid saved comparison JSON."); }
    }
    private static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("Invalid saved comparison object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw Invalid("Unknown or duplicate saved comparison property.");
        if (seen.Count != names.Length) throw Invalid("Missing saved comparison property.");
    }
    private static string Text(JsonElement parent, string name) => parent.GetProperty(name).ValueKind == JsonValueKind.String ? parent.GetProperty(name).GetString()! : throw Invalid("Saved comparison field must be a string.");
    private static Guid Id(JsonElement parent, string name) => Guid.TryParseExact(Text(parent, name), "D", out var id) && id != Guid.Empty ? id : throw Invalid("Invalid saved comparison identity.");
    private static long Count(JsonElement parent, string name) => long.TryParse(Text(parent, name), NumberStyles.None, CultureInfo.InvariantCulture, out long count) && count > 0 && count <= AnalysisLimits.InventoryBytes && count.ToString(CultureInfo.InvariantCulture) == Text(parent, name) ? count : throw Invalid("Invalid saved comparison byte count.");
    private static int Integer(JsonElement parent, string name) => int.TryParse(Text(parent, name), NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value.ToString(CultureInfo.InvariantCulture) == Text(parent, name) ? value : throw Invalid("Invalid saved comparison integer.");
    private sealed class NonClosing(Stream inner) : SequentialStream
    {
        public override bool CanRead => inner.CanRead; public override bool CanWrite => false;
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
