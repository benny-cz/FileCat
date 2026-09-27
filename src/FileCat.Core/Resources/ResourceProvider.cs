namespace FileCat.Core.Resources;

/// <summary>
/// What a location supports. Advisory only: permissions and media state can change before execution,
/// so every operation revalidates (plan §7.1).
/// </summary>
[Flags]
public enum LocationCapabilities : uint
{
    None = 0,
    Enumerate = 1 << 0,
    CreateDirectory = 1 << 1,
    CreateFile = 1 << 2,
    Delete = 1 << 3,
    Recycle = 1 << 4,
    Rename = 1 << 5,
    /// <summary>Can be the destination of copy/move/extract.</summary>
    TransferTarget = 1 << 6,
    /// <summary>Items expose byte content (F3, copy source).</summary>
    ReadContent = 1 << 7,
    Watch = 1 << 8,
    /// <summary>A result/working set: membership only; F7 unavailable; removal never deletes.</summary>
    ReferenceContainer = 1 << 9,
    /// <summary>Items are local files an external editor can open in place.</summary>
    ExternalEdit = 1 << 10,
    /// <summary>Items can be moved out of this location (transfer source with deletion).</summary>
    MoveSource = 1 << 11,
}

/// <summary>Receives enumeration batches. Implementations are single-writer and non-blocking.</summary>
public interface IEnumerationSink
{
    void AddBatch(ReadOnlySpan<EntryData> entries);

    /// <summary>A non-fatal problem (an entry or subtree that could not be read).</summary>
    void ReportIssue(string message);
}

/// <summary>
/// Common navigation surface for one scheme (plan §7.1). Typed operations live in separate services;
/// a provider never has to fake a stream, directory, or path syntax it does not have.
/// </summary>
public abstract class ResourceProvider
{
    public abstract string Scheme { get; }

    /// <summary>Human-readable location for address bars and dialogs.</summary>
    public abstract string GetDisplayPath(Location location);

    /// <summary>Short name for tab headers.</summary>
    public virtual string GetDisplayName(Location location)
    {
        var display = GetDisplayPath(location).TrimEnd('\\', '/');
        int i = display.LastIndexOfAny(['\\', '/']);
        return i >= 0 && i < display.Length - 1 ? display[(i + 1)..] : display;
    }

    public abstract Location? GetParent(Location location);

    /// <summary>The raw name of <paramref name="location"/> inside its parent, used to focus it after "go up".</summary>
    public virtual string? GetNameInParent(Location location)
    {
        var display = location.Path.TrimEnd('\\', '/');
        int i = display.LastIndexOfAny(['\\', '/']);
        return i >= 0 ? display[(i + 1)..] : display;
    }

    /// <summary>Key of the device/server that serves I/O for this location (queues, hang isolation).</summary>
    public virtual string GetDeviceKey(Location location) => Scheme;

    public abstract LocationCapabilities GetCapabilities(Location location);

    /// <summary>Why a capability is unavailable, in plain terms (PI-03).</summary>
    public virtual string ExplainUnavailable(Location location, LocationCapabilities capability) =>
        $"This location does not support {Describe(capability)}.";

    /// <summary>Streams entries in batches. Throws for fatal failures (not found, access denied).</summary>
    public abstract Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct);

    /// <summary>The location to navigate to when an entry is opened, or null when it is not navigable.</summary>
    public abstract Location? GetChildLocation(Location parent, in EntryData entry);

    /// <summary>Identity of an entry listed at <paramref name="listing"/>. Result sets override this.</summary>
    public virtual ItemRef GetItemRef(Location listing, in EntryData entry) => ItemRef.FromEntry(listing, entry);

    /// <summary>True when every item of a listing has the listing itself as its parent (false for result sets).</summary>
    public virtual bool ItemsShareListingParent => true;

    /// <summary>Opens item content for random-access reading, or null when unsupported.</summary>
    public virtual IContentSource? OpenContent(ItemRef item) => null;

    /// <summary>Parses user-typed text into a location of this provider.</summary>
    public virtual bool TryParse(string text, Location? current, out Location? location)
    {
        location = null;
        return false;
    }

    /// <summary>True when both locations denote the same container for this provider's equality rules.</summary>
    public virtual bool IsSameLocation(Location a, Location b) => a.Equals(b);

    protected static string Describe(LocationCapabilities c) => c switch
    {
        LocationCapabilities.CreateDirectory => "creating folders",
        LocationCapabilities.CreateFile => "creating files",
        LocationCapabilities.Delete => "deleting",
        LocationCapabilities.Recycle => "the Recycle Bin",
        LocationCapabilities.Rename => "renaming",
        LocationCapabilities.TransferTarget => "receiving copied or moved items",
        LocationCapabilities.ReadContent => "reading content",
        LocationCapabilities.ExternalEdit => "editing in an external editor",
        LocationCapabilities.MoveSource => "moving items out",
        _ => c.ToString(),
    };
}

/// <summary>Scheme → provider map plus shared parsing of user-typed locations.</summary>
public sealed class ProviderRegistry
{
    private readonly Dictionary<string, ResourceProvider> _providers = new(StringComparer.Ordinal);
    private readonly List<ResourceProvider> _parseOrder = [];

    public void Register(ResourceProvider provider)
    {
        _providers[provider.Scheme] = provider;
        _parseOrder.Remove(provider);
        _parseOrder.Add(provider);
    }

    public bool IsRegistered(string scheme) => _providers.ContainsKey(scheme);

    public ResourceProvider Get(string scheme) =>
        _providers.TryGetValue(scheme, out var p) ? p : throw new InvalidOperationException($"No provider for scheme '{scheme}'.");

    public ResourceProvider For(Location location) => Get(location.Scheme);

    public bool TryGet(string scheme, out ResourceProvider? provider) => _providers.TryGetValue(scheme, out provider);

    public string Display(Location location) => For(location).GetDisplayPath(location);

    public bool TryParse(string text, Location? current, out Location? location)
    {
        location = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        // The current provider interprets relative input first (e.g. "sub" inside an archive).
        if (current is not null && _providers.TryGetValue(current.Scheme, out var cur) && cur.TryParse(text, current, out location))
            return true;
        foreach (var p in _parseOrder)
        {
            if (current is not null && p.Scheme == current.Scheme) continue;
            if (p.TryParse(text, current, out location)) return true;
        }
        return false;
    }
}
