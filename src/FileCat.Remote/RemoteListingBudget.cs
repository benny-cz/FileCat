using System.Text;

namespace FileCat.Remote;

/// <summary>One remote folder may use a bounded number of entries and bytes of names.</summary>
internal sealed class RemoteListingBudget
{
    internal const int MaxEntries = 20_000;
    internal const int MaxNameBytes = 4 << 20;
    internal const int MaxWireBytes = 16 << 20;
    internal const int MaxLineBytes = 64 << 10;

    internal int Entries { get; private set; }
    internal int NameBytes { get; private set; }

    internal void Add(string name)
    {
        int bytes = Encoding.UTF8.GetByteCount(name);
        if (Entries >= MaxEntries || bytes > MaxNameBytes - NameBytes)
            throw new RemoteListingLimitException();
        Entries++;
        NameBytes += bytes;
    }
}

internal sealed class RemoteListingLimitException() : IOException(
    $"This remote folder exceeds FileCat's listing limit of {RemoteListingBudget.MaxEntries:N0} entries or " +
    $"{RemoteListingBudget.MaxNameBytes >> 20} MiB of names. Nothing from the incomplete listing was shown.");
