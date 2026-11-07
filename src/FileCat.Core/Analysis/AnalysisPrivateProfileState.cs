using System.Text.Json;

namespace FileCat.Core.Analysis;

/// <summary>Initial private-state schema. Immutable create-new profile identity; never inventory-selected or diagnostic settings.</summary>
public sealed class AnalysisPrivateProfileState(AnalysisPrivateInventoryStore store)
{
    private const int MaximumBytes = 512;
    internal Guid ReadExisting(CancellationToken cancellation = default) => Read(cancellation);
    public Guid LoadOrCreate(CancellationToken cancellation = default)
    {
        try { return Read(cancellation); }
        catch (FileNotFoundException) { }
        cancellation.ThrowIfCancellationRequested(); Guid id = Guid.NewGuid();
        using var buffer = new MemoryStream(128);
        using (var writer = new Utf8JsonWriter(buffer))
        { writer.WriteStartObject(); writer.WriteString("schema", "1"); writer.WriteString("kind", "analysis-private-state"); writer.WriteString("profile", id.ToString("D")); writer.WriteEndObject(); }
        byte[] data = buffer.ToArray();
        try { store.WriteMetadataNew(AnalysisPrivateNames.PrivateState, data, cancellation); return id; }
        catch (AnalysisPublicationUncertainException) { throw; }
        catch (IOException error) when (!AnalysisCloseUnconfirmedException.IsPresent(error))
        {
            // A concurrent create-new winner is accepted only after the complete guarded strict read.
            // No replacement, repair or write retry follows any error.
            return Read(cancellation);
        }
    }
    private Guid Read(CancellationToken cancellation)
    {
        byte[] data = store.ReadMetadata(AnalysisPrivateNames.PrivateState, cancellation, MaximumBytes);
        try
        {
            using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement; var names = new HashSet<string>(StringComparer.Ordinal);
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(p => p.Name is not ("schema" or "kind" or "profile") || !names.Add(p.Name)) || names.Count != 3 ||
                root.GetProperty("schema").GetString() != "1" || root.GetProperty("kind").GetString() != "analysis-private-state") throw Invalid();
            string? profile = root.GetProperty("profile").GetString();
            if (!Guid.TryParseExact(profile, "D", out var id) || id == Guid.Empty || profile != id.ToString("D")) throw Invalid();
            cancellation.ThrowIfCancellationRequested(); return id;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException) { throw Invalid(); }
    }
    private static InvalidDataException Invalid() => new("The private analysis profile identity has an unsupported or invalid format. Existing state was preserved.");
}
