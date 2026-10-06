using System.Runtime.CompilerServices;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

public sealed class ResultSetNoteLifetimeTests
{
    [Fact]
    public void Removing_a_member_releases_its_note_reference_while_the_set_lives()
    {
        var set = new ResultSet("notes", "Owned note lifetime", "Synthetic references, no files opened");
        var removed = AddAndRemove(set);
        var retained = new ItemRef(new Location("owned-test", "folder"), "retained", EntryKind.File);
        set.Add(retained, "kept");
        set.SetNote(retained, "group 2");
        Collect();
        Assert.False(removed.TryGetTarget(out _), "A removed noted member is still rooted by the live set.");
        Assert.Equal("group 2", set.NoteOf(retained));
        Assert.Single(set.Snapshot());
        GC.KeepAlive(set);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<ItemRef> AddAndRemove(ResultSet set)
    {
        var item = new ItemRef(new Location("owned-test", "folder"), "removed", EntryKind.File);
        set.Add(item, "relative");
        set.SetNote(item, "group 1");
        var reference = new WeakReference<ItemRef>(item);
        Assert.Equal(1, set.Remove([item]));
        Assert.False(set.Contains(item));
        return reference;
    }

    [Fact]
    public void Renaming_a_member_moves_its_group_note_to_the_replacement()
    {
        var set = new ResultSet("notes", "Owned note rename", "Synthetic references");
        var old = new ItemRef(new Location("owned-test", "folder"), "before", EntryKind.File);
        var replacement = new ItemRef(old.Parent, "after", EntryKind.File);
        set.Add(old, "relative");
        set.SetNote(old, "group 3");
        Assert.True(set.Replace(old, replacement));
        Assert.Equal("group 3", set.NoteOf(replacement));
        Assert.Null(set.NoteOf(old));
        Assert.Equal("relative", Assert.Single(set.Snapshot()).Relative);
        Assert.False(set.Contains(old));
    }

    [Fact]
    public void Merging_into_an_existing_member_keeps_that_members_note_and_releases_the_old_one()
    {
        var set = new ResultSet("notes", "Owned note merge", "Synthetic references");
        var replacement = new ItemRef(new Location("owned-test", "folder"), "existing", EntryKind.File);
        set.Add(replacement, "kept");
        set.SetNote(replacement, "existing group");
        var old = AddAndMerge(set, replacement);
        Collect();
        Assert.False(old.TryGetTarget(out _), "The merged member remains rooted by its old group note.");
        Assert.Equal("existing group", set.NoteOf(replacement));
        Assert.Equal("kept", Assert.Single(set.Snapshot()).Relative);
        GC.KeepAlive(set);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<ItemRef> AddAndMerge(ResultSet set, ItemRef replacement)
    {
        var old = new ItemRef(replacement.Parent, "merged", EntryKind.File);
        set.Add(old, "removed relative");
        set.SetNote(old, "old group");
        var reference = new WeakReference<ItemRef>(old);
        Assert.True(set.Replace(old, replacement));
        return reference;
    }

    [Fact]
    public void An_ordinary_unnoted_replacement_keeps_its_relative_folder()
    {
        var set = new ResultSet("notes", "Owned unnoted control", "Synthetic references");
        var old = new ItemRef(new Location("owned-test", "folder"), "before", EntryKind.File);
        var replacement = new ItemRef(old.Parent, "after", EntryKind.File);
        set.Add(old, "relative");
        Assert.True(set.Replace(old, replacement));
        Assert.Null(set.NoteOf(replacement));
        Assert.Equal("relative", Assert.Single(set.Snapshot()).Relative);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
