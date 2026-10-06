using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

public sealed class ResultSetBulkRemovalTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12000)]
    public void Unordered_repeated_and_missing_selections_preserve_remaining_order_notes_and_folders(int members)
    {
        var set = new ResultSet("bulk", "Owned bulk removal", "Synthetic references; no files opened");
        var parent = new Location("owned-test", "folder");
        var items = Enumerable.Range(0, members)
            .Select(i => new ItemRef(parent, $"member-{i:D6}", EntryKind.File)).ToArray();
        var ordinals = items.Select((item, index) => (item, index)).ToDictionary(x => x.item, x => x.index);
        for (int i = 0; i < items.Length; i++)
        {
            set.Add(items[i], $"folder-{i % 8}");
            set.SetNote(items[i], $"group-{i % 32}");
        }
        var chosen = items.Where((_, i) => i % 2 == 0).Reverse().ToArray();
        var requested = chosen.Concat(chosen).Append(new ItemRef(parent, "absent", EntryKind.File));
        int notifications = 0;
        set.Changed += () => notifications++;
        Assert.Equal(chosen.Length, set.Remove(requested));
        Assert.Equal(1, notifications);
        var remaining = set.Snapshot();
        Assert.Equal(items.Where((_, i) => i % 2 != 0), remaining.Select(x => x.Item));
        Assert.Equal(items.Length - chosen.Length, set.Count);
        foreach (var item in chosen)
        {
            Assert.False(set.Contains(item));
            Assert.Null(set.NoteOf(item));
        }
        foreach (var entry in remaining)
        {
            int i = ordinals[entry.Item];
            Assert.Equal($"folder-{i % 8}", entry.Relative);
            Assert.Equal($"group-{i % 32}", set.NoteOf(entry.Item));
        }
    }

    [Fact]
    public void A_failed_selection_enumerator_keeps_already_removed_members_and_remaining_metadata_consistent()
    {
        var set = new ResultSet("bulk", "Owned failed selection", "Synthetic references");
        var parent = new Location("owned-test", "folder");
        var items = Enumerable.Range(0, 5).Select(i => new ItemRef(parent, $"member-{i}", EntryKind.File)).ToArray();
        foreach (var item in items)
        {
            set.Add(item, "relative");
            set.SetNote(item, "group");
        }
        int notifications = 0;
        set.Changed += () => notifications++;
        Assert.Throws<InvalidDataException>(() => set.Remove(FailingSelection(items)));
        Assert.Equal(items.Where((_, i) => i is not (1 or 3)), set.Snapshot().Select(x => x.Item));
        Assert.Equal(3, set.Count);
        foreach (int i in new[] { 1, 3 })
        {
            Assert.False(set.Contains(items[i]));
            Assert.Null(set.NoteOf(items[i]));
        }
        Assert.All(set.Snapshot(), entry =>
        {
            Assert.Equal("relative", entry.Relative);
            Assert.Equal("group", set.NoteOf(entry.Item));
        });
        Assert.Equal(0, notifications); // Existing exception behavior: no completed-operation event.
    }

    private static IEnumerable<ItemRef> FailingSelection(ItemRef[] items)
    {
        yield return items[3];
        yield return items[1];
        throw new InvalidDataException("Owned selection stopped after two members.");
    }
}
