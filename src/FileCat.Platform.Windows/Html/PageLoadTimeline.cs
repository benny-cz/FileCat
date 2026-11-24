using System.Diagnostics;
using System.Globalization;
using System.Text;
using FileCat.Core.Diagnostics;

namespace FileCat.Platform.Windows.Html;

/// <summary>
/// What happened while a page view loaded its pages, in order, with the time since the view was made and the managed
/// thread that saw it (release issue I648: WebView2 sometimes never reports that a navigation completed). Bounded: the
/// oldest entries go first. Tests print it whole; the diagnostics log gets it with addresses and request paths hashed
/// unless diagnostic mode is on.
/// </summary>
internal sealed class PageLoadTimeline
{
    internal const int Capacity = 400;

    private readonly long _origin = Stopwatch.GetTimestamp();
    private readonly Queue<Entry> _entries = new();
    private readonly Lock _lock = new();
    private int _dropped;

    private readonly record struct Entry(long Timestamp, int Thread, string Text, string? Name);

    /// <summary>Milliseconds since the view was made.</summary>
    public double ElapsedMilliseconds => Stopwatch.GetElapsedTime(_origin).TotalMilliseconds;

    /// <summary>One event; <paramref name="name"/> is an address or a request path, hashed when the log needs it.</summary>
    public void Add(string text, string? name = null)
    {
        var entry = new Entry(Stopwatch.GetTimestamp(), Environment.CurrentManagedThreadId, text, name);
        lock (_lock)
        {
            _entries.Enqueue(entry);
            if (_entries.Count > Capacity)
            {
                _entries.Dequeue();
                _dropped++;
            }
        }
    }

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    /// <summary>A point in the timeline, for <see cref="Describe"/>'s <c>since</c>.</summary>
    public long Mark() => Stopwatch.GetTimestamp();

    /// <summary>
    /// The kept entries from <paramref name="since"/> on (a <see cref="Mark"/>; 0: all), one per line:
    /// "+12.3 ms t1 navigation 4 starting: accepted https://…".
    /// </summary>
    public string Describe(bool hashNames = false, long since = 0)
    {
        Entry[] entries;
        int dropped;
        lock (_lock)
        {
            entries = [.. _entries];
            dropped = _dropped;
        }
        var text = new StringBuilder();
        if (dropped > 0 && since == 0) text.Append(CultureInfo.InvariantCulture, $"({dropped} earlier entries dropped)\n");
        foreach (var entry in entries)
        {
            if (entry.Timestamp < since) continue;
            double ms = Stopwatch.GetElapsedTime(_origin, entry.Timestamp).TotalMilliseconds;
            text.Append(CultureInfo.InvariantCulture, $"+{ms:0.0} ms t{entry.Thread} {entry.Text}");
            if (entry.Name is { } name) text.Append(' ').Append(hashNames ? AppLog.P(name) : name);
            text.Append('\n');
        }
        return text.ToString();
    }
}
