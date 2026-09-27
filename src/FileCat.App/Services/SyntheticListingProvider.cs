using FileCat.Core.Resources;

namespace FileCat.App.Services;

/// <summary>
/// TV-01 fixture: file-system locations under <see cref="Root"/> list <see cref="Count"/> synthetic files
/// (long Unicode names, descending sizes) so the real record store, spill tier, and indexes are exercised
/// without creating a million files. Everything else is delegated to the real file-system provider.
/// </summary>
public sealed class SyntheticListingProvider(ResourceProvider inner, string root, int count) : ResourceProvider
{
    public string Root { get; } = Path.TrimEndingDirectorySeparator(root);
    public int Count { get; } = count;

    private bool IsSynthetic(Location location) =>
        location.IsFileSystem && location.Path.StartsWith(Root, StringComparison.OrdinalIgnoreCase);

    public override string Scheme => Schemes.FileSystem;

    public override string GetDisplayPath(Location location) => inner.GetDisplayPath(location);

    public override string GetDisplayName(Location location) => inner.GetDisplayName(location);

    public override string? GetNameInParent(Location location) => inner.GetNameInParent(location);

    public override bool IsSameLocation(Location a, Location b) => inner.IsSameLocation(a, b);

    public override Location? GetParent(Location location) => IsSynthetic(location) ? null : inner.GetParent(location);

    public override LocationCapabilities GetCapabilities(Location location) =>
        IsSynthetic(location) ? LocationCapabilities.Enumerate : inner.GetCapabilities(location);

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) =>
        IsSynthetic(location) ? "This is a synthetic benchmark listing." : inner.ExplainUnavailable(location, capability);

    public override Location? GetChildLocation(Location parent, in EntryData entry) =>
        IsSynthetic(parent) ? null : inner.GetChildLocation(parent, entry);

    public override ItemRef GetItemRef(Location listing, in EntryData entry) => inner.GetItemRef(listing, entry);

    public override IContentSource? OpenContent(ItemRef item) => IsSynthetic(item.Parent) ? null : inner.OpenContent(item);

    public override bool TryParse(string text, Location? current, out Location? location) => inner.TryParse(text, current, out location);

    public override string GetDeviceKey(Location location) => inner.GetDeviceKey(location);

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        if (!IsSynthetic(location)) return inner.EnumerateAsync(location, sink, ct);
        var batch = new EntryData[512];
        for (int from = 0; from < Count; from += batch.Length)
        {
            ct.ThrowIfCancellationRequested();
            int n = Math.Min(batch.Length, Count - from);
            for (int i = 0; i < n; i++)
            {
                int sequence = Count - from - i;
                batch[i] = new EntryData($"file-{sequence:0000000}-long-αβγ.txt", EntryKind.File, sequence, 638_000_000_000_000_000 + sequence * 10_000_000L);
            }
            sink.AddBatch(batch.AsSpan(0, n));
        }
        return Task.CompletedTask;
    }
}
