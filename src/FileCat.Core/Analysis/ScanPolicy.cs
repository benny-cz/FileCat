using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FileCat.Core.Analysis;

public sealed record ScanPolicy
{
    public int SchemaVersion { get; init; } = 1;
    public string AnalysisMode { get; init; } = "disk";
    public string ComponentPreset { get; init; } = "standard";
    public ImmutableArray<string> BasisIds { get; init; } = [];
    public ImmutableArray<string> Exclusions { get; init; } = [];
    public string MountPolicy { get; init; } = "stay-on-source";
    public string PackagePolicy { get; init; } = "opaque";
    public string CloudPolicy { get; init; } = "metadata-only";
    public ImmutableSortedDictionary<string, ulong> Limits { get; init; } = ImmutableSortedDictionary.Create<string, ulong>(StringComparer.Ordinal);
    public int ClassificationVersion { get; init; } = 1;
    public int AllocationVersion { get; init; } = 1;
    public int AttributionVersion { get; init; } = 1;

    public byte[] CanonicalBytes()
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("schemaVersion", SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            w.WriteString("analysisMode", AnalysisMode);
            w.WriteString("componentPreset", ComponentPreset);
            Strings(w, "basisIds", BasisIds);
            Strings(w, "exclusions", Exclusions);
            w.WriteString("mountPolicy", MountPolicy);
            w.WriteString("packagePolicy", PackagePolicy);
            w.WriteString("cloudPolicy", CloudPolicy);
            w.WriteStartObject("limits");
            foreach (var p in Limits.OrderBy(p => p.Key, StringComparer.Ordinal))
                w.WriteString(p.Key, p.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            w.WriteEndObject();
            w.WriteString("classificationVersion", ClassificationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            w.WriteString("allocationVersion", AllocationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            w.WriteString("attributionVersion", AttributionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }
    public string Fingerprint => Convert.ToHexStringLower(SHA256.HashData(CanonicalBytes()));
    public string CanonicalJson => Encoding.UTF8.GetString(CanonicalBytes());
    private static void Strings(Utf8JsonWriter w, string key, ImmutableArray<string> values)
    {
        w.WriteStartArray(key);
        foreach (var value in values.Order(StringComparer.Ordinal)) w.WriteStringValue(value);
        w.WriteEndArray();
    }
}
