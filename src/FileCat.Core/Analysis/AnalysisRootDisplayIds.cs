using System.Collections;
using System.Collections.Immutable;
using System.Text;

namespace FileCat.Core.Analysis;

/// <summary>Bounded, immutable query display context. Identifiers never reconnect a source.</summary>
public sealed class AnalysisRootDisplayIds : IReadOnlyDictionary<string, string>, IEquatable<AnalysisRootDisplayIds>
{
    private readonly ImmutableDictionary<string, string> _ids;
    public AnalysisRootDisplayIds(IEnumerable<KeyValuePair<string, string>> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var result = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in ids)
            if (result.Count >= AnalysisLimits.Roots || !Identifier(pair.Key) || !Identifier(pair.Value) ||
                !result.TryAdd(pair.Key, pair.Value) || !values.Add(pair.Value)) throw new ArgumentException("Invalid captured root identifiers.", nameof(ids));
        if (result.Count == 0) throw new ArgumentException("Captured root identifiers cannot be empty.", nameof(ids));
        _ids = result.ToImmutable();
        static bool Identifier(string? value) => !string.IsNullOrEmpty(value) && !value.Contains('\0') && Encoding.UTF8.GetByteCount(value) <= 256 &&
            AnalysisPrivateInventoryStore.WellFormedText(value);
    }
    public int Count => _ids.Count;
    public IEnumerable<string> Keys => _ids.Keys;
    public IEnumerable<string> Values => _ids.Values;
    public string this[string key] => _ids[key];
    public bool ContainsKey(string key) => _ids.ContainsKey(key);
    public bool TryGetValue(string key, out string value) => _ids.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _ids.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Equals(AnalysisRootDisplayIds? other) => ReferenceEquals(this, other) || other is not null && Count == other.Count &&
        _ids.All(p => other.TryGetValue(p.Key, out string? value) && value == p.Value);
    public override bool Equals(object? obj) => obj is AnalysisRootDisplayIds other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var pair in _ids.OrderBy(p => p.Key, StringComparer.Ordinal)) { hash.Add(pair.Key, StringComparer.Ordinal); hash.Add(pair.Value, StringComparer.Ordinal); }
        return hash.ToHashCode();
    }
}
