using System.Buffers.Binary;
using FileCat.Core.Records;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// A change journal that wraps while it is read (CI run 36941532909: a busy runner's journal overwrote its oldest entries
/// twice during one read, and the read failed): reading goes on from the new oldest entry as often as that happens, says
/// so, and never loops on a journal that does not move on. A stand-in for the file system plays the journal.
/// </summary>
public sealed class UsnJournalWrapTests
{
    private const uint Query = 0x0009_00F4, ReadJournal = 0x0009_00BB;

    private static byte[] Info(long first, long next)
    {
        var data = new byte[64];
        BinaryPrimitives.WriteUInt64LittleEndian(data, 7);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), first);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), next);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(56), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(58), 3);
        return data;
    }

    /// <summary>
    /// The journal: its oldest entry is <paramref name="oldest"/>[0], then moves on to the next value right after each
    /// query (it wraps again while FileCat reads on); a read from before the oldest fails as overwritten, a read from it
    /// reaches the end. Stuck, every read fails and the oldest never moves.
    /// </summary>
    private sealed class Journal(long[] oldest, bool stuck = false)
    {
        public readonly List<long> Reads = [];
        private int _wraps;

        public int Call(uint code, ReadOnlySpan<byte> input, Span<byte> output, out int returned)
        {
            if (code == Query)
            {
                var info = Info(oldest[Math.Min(_wraps, oldest.Length - 1)], 1000);
                if (!stuck) _wraps++;
                info.CopyTo(output);
                returned = info.Length;
                return 0;
            }
            Assert.Equal(ReadJournal, code);
            long start = BinaryPrimitives.ReadInt64LittleEndian(input);
            Reads.Add(start);
            if (stuck || _wraps < oldest.Length && start < oldest[_wraps])
            {
                returned = 0;
                return UsnJournalReader.ErrorJournalEntryDeleted;
            }
            // Nothing more: only the next USN, at the end.
            BinaryPrimitives.WriteInt64LittleEndian(output, 1000);
            returned = 8;
            return 0;
        }
    }

    [Fact]
    public void Reading_goes_on_from_the_new_oldest_entry_as_often_as_the_journal_wraps()
    {
        var journal = new Journal([200, 300, 450]);
        var skipped = new List<long>();
        UsnJournalReader.Read(journal.Call, UsnJournalInfo.Parse(Info(100, 1000))!, 100, TestContext.Current.CancellationToken, _ => { }, skipped.Add);
        Assert.Equal([100L, 200, 300, 450], journal.Reads);
        Assert.Equal([200L, 300, 450], skipped);
    }

    [Fact]
    public void A_journal_that_says_overwritten_but_does_not_move_on_ends_the_read()
    {
        // Contradictory answers (the oldest entry is still the one asked for) must not loop.
        var journal = new Journal([200], stuck: true);
        var skipped = new List<long>();
        UsnJournalReader.Read(journal.Call, UsnJournalInfo.Parse(Info(100, 1000))!, 100, TestContext.Current.CancellationToken, _ => { }, skipped.Add);
        Assert.Equal([100L, 200], journal.Reads);
        Assert.Equal([200L], skipped);
    }
}
