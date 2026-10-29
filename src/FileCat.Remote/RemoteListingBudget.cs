using System.Text;

namespace FileCat.Remote;

/// <summary>
/// One remote folder may use a bounded number of entries and bytes of names: a folder shown, few; a folder a job copies
/// into, moves, renames or deletes in, which it must see whole, many more (review finding R-M9), still bounded so that a
/// server cannot exhaust FileCat's memory.
/// </summary>
internal sealed class RemoteListingBudget(bool job = false)
{
    internal const int MaxEntries = 20_000;
    internal const int MaxNameBytes = 4 << 20;
    internal const int MaxWireBytes = 16 << 20;
    internal const int MaxJobEntries = 250_000;
    internal const int MaxJobNameBytes = 64 << 20;
    internal const int MaxJobWireBytes = 128 << 20;
    internal const int MaxLineBytes = 64 << 10;

    internal bool Job { get; } = job;
    internal int EntryLimit => Job ? MaxJobEntries : MaxEntries;
    internal int NameByteLimit => Job ? MaxJobNameBytes : MaxNameBytes;
    internal int WireByteLimit => Job ? MaxJobWireBytes : MaxWireBytes;
    internal int Entries { get; private set; }
    internal int NameBytes { get; private set; }

    internal void Add(string name)
    {
        int bytes = Encoding.UTF8.GetByteCount(name);
        if (Entries >= EntryLimit || bytes > NameByteLimit - NameBytes)
            throw Exceeded();
        Entries++;
        NameBytes += bytes;
    }

    internal RemoteListingLimitException Exceeded() => new(Job);
}

internal sealed class RemoteListingLimitException(bool job = false) : IOException(job
    ? $"This remote folder holds more than FileCat lists for a job ({RemoteListingBudget.MaxJobEntries:N0} entries or " +
      $"{RemoteListingBudget.MaxJobNameBytes >> 20} MiB of names). Nothing was done on the strength of the incomplete listing."
    : $"This remote folder exceeds FileCat's listing limit of {RemoteListingBudget.MaxEntries:N0} entries or " +
      $"{RemoteListingBudget.MaxNameBytes >> 20} MiB of names. Nothing from the incomplete listing was shown.");
