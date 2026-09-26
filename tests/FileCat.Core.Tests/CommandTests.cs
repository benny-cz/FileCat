using FileCat.Core.Commands;

namespace FileCat.Core.Tests;

public class CommandTests
{
    [Theory]
    [InlineData("Ctrl+Shift+F5", "F5", KeyMods.Ctrl | KeyMods.Shift)]
    [InlineData("Num+", "Add", KeyMods.None)]
    [InlineData("Ctrl+Num+", "Add", KeyMods.Ctrl)]
    [InlineData("Shift+Num-", "Subtract", KeyMods.Shift)]
    [InlineData("Ctrl+PgUp", "PageUp", KeyMods.Ctrl)]
    [InlineData("Ctrl+\\", "Backslash", KeyMods.Ctrl)]
    [InlineData("Ctrl+Shift+3", "D3", KeyMods.Ctrl | KeyMods.Shift)]
    [InlineData("alt+left", "left", KeyMods.Alt)]
    [InlineData("Ctrl+a", "A", KeyMods.Ctrl)]
    public void Parses_chords(string text, string key, KeyMods mods)
    {
        var c = KeyChord.Parse(text);
        Assert.Equal(key, c.Key, ignoreCase: true);
        Assert.Equal(mods, c.Mods);
    }

    [Theory]
    [InlineData("Ctrl+Shift+F5")]
    [InlineData("Num+")]
    [InlineData("Ctrl+PgUp")]
    [InlineData("Alt+F7")]
    public void Display_round_trips(string text)
    {
        var c = KeyChord.Parse(text);
        Assert.Equal(c, KeyChord.Parse(c.ToDisplayString()));
    }

    [Fact]
    public void Default_keymap_has_no_conflicts()
    {
        var keymap = new Keymap(CommandRegistry.CreateDefault());
        Assert.Empty(keymap.Conflicts);
    }

    [Fact]
    public void Canonical_intents_are_bound_as_planned()
    {
        var k = new Keymap(CommandRegistry.CreateDefault());
        Assert.Equal(CommandIds.View, k.Resolve(KeyChord.Parse("F3"), CommandContext.Panel));
        Assert.Equal(CommandIds.Edit, k.Resolve(KeyChord.Parse("F4"), CommandContext.Panel));
        Assert.Equal(CommandIds.Copy, k.Resolve(KeyChord.Parse("F5"), CommandContext.Panel));
        Assert.Equal(CommandIds.Move, k.Resolve(KeyChord.Parse("F6"), CommandContext.Panel));
        Assert.Equal(CommandIds.MakeDirectory, k.Resolve(KeyChord.Parse("F7"), CommandContext.Panel));
        Assert.Equal(CommandIds.Delete, k.Resolve(KeyChord.Parse("F8"), CommandContext.Panel));
        Assert.Equal(CommandIds.CloseTab, k.Resolve(KeyChord.Parse("Ctrl+W"), CommandContext.Panel));
        Assert.Equal(CommandIds.MarkRestore, k.Resolve(KeyChord.Parse("Num/"), CommandContext.Panel));
        Assert.Equal(CommandIds.Palette, k.Resolve(KeyChord.Parse("Ctrl+Shift+P"), CommandContext.Panel));
    }

    [Fact]
    public void Overrides_replace_defaults()
    {
        var k = new Keymap(CommandRegistry.CreateDefault(), new Dictionary<string, string[]> { [CommandIds.View] = ["F12"] });
        Assert.Null(k.Resolve(KeyChord.Parse("F3"), CommandContext.Panel));
        // F12 was already the panel picker's default: the conflict is reported, and the earlier command wins.
        Assert.Equal(CommandIds.View, k.Resolve(KeyChord.Parse("F12"), CommandContext.Panel));
        Assert.Contains(k.Conflicts, c => c.Contains(CommandIds.FocusPanelPicker));
    }

    [Fact]
    public void Function_key_bar_reflects_modifiers()
    {
        var k = new Keymap(CommandRegistry.CreateDefault());
        var plain = k.GetFunctionKeyBar(KeyMods.None, CommandContext.Panel);
        Assert.Equal("Copy", plain[4].Label);
        var shift = k.GetFunctionKeyBar(KeyMods.Shift, CommandContext.Panel);
        Assert.Equal("Delete!", shift[7].Label);
        var alt = k.GetFunctionKeyBar(KeyMods.Alt, CommandContext.Panel);
        Assert.Equal("Find", alt[6].Label);
    }
}
