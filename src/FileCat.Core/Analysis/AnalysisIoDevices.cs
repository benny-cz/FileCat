using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>Trusted devices touched by one bounded operation; never restored from inventory data.</summary>
public sealed class AnalysisIoDevices
{
    public string Primary { get; }
    internal ImmutableArray<string> Keys { get; }
    public AnalysisIoDevices(string primary, string? related = null)
    {
        Validate(primary); if (related is not null) Validate(related);
        Primary = primary;
        Keys = related is null || string.Equals(primary, related, StringComparison.OrdinalIgnoreCase)
            ? [primary] : [.. new[] { primary, related }.Order(StringComparer.OrdinalIgnoreCase)];
    }
    internal bool Contains(string key) => Keys.Contains(key, StringComparer.OrdinalIgnoreCase);
    internal AnalysisIoDevices Include(string key)
    {
        Validate(key);
        if (Contains(key)) return this;
        if (Keys.Length != 1) throw new NotSupportedException("This operation would require more than two captured devices.");
        return new(Primary, key);
    }
    private static void Validate(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 1024)
            throw new ArgumentException("A bounded provider device key is required.", nameof(key));
    }
    public static implicit operator AnalysisIoDevices(string key) => new(key);
}
