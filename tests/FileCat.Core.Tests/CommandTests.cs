using FileCat.Core.Commands;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileCat.Core.Tests;

public class CommandTests
{
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo(string path, uint mode);

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
        // Num/ opens a terminal in the panel's folder, as in Salamander; restoring the selection moved to Ctrl+Num/.
        Assert.Equal(CommandIds.OpenTerminal, k.Resolve(KeyChord.Parse("Num/"), CommandContext.Panel));
        Assert.Equal(CommandIds.OpenTerminal, k.Resolve(KeyChord.Parse("Ctrl+`"), CommandContext.Panel));
        Assert.Equal(CommandIds.MarkRestore, k.Resolve(KeyChord.Parse("Ctrl+Num/"), CommandContext.Panel));
        Assert.Equal(CommandIds.Palette, k.Resolve(KeyChord.Parse("Ctrl+Shift+P"), CommandContext.Panel));
    }

    [Fact]
    public void Find_bindings_are_contextual_and_can_be_remapped_or_cleared()
    {
        var registry = CommandRegistry.CreateDefault();
        var defaults = new Keymap(registry);
        Assert.Equal(CommandIds.FindRun, defaults.Resolve(KeyChord.Parse("Enter"), CommandContext.FindCriteria, CommandContext.Find));
        Assert.Equal(CommandIds.FindOpen, defaults.Resolve(KeyChord.Parse("Enter"), CommandContext.FindResults, CommandContext.Find, CommandContext.Panel));
        Assert.Equal(CommandIds.FindSubtract, defaults.Resolve(KeyChord.Parse("Ctrl+S"), CommandContext.FindCriteria, CommandContext.Find));
        Assert.Equal(CommandIds.FindHideSelected, defaults.Resolve(KeyChord.Parse("Ctrl+H"), CommandContext.FindResults, CommandContext.Find, CommandContext.Panel));
        Assert.Equal(CommandIds.FindAppend, defaults.Resolve(KeyChord.Parse("Ctrl+W"), CommandContext.FindResults, CommandContext.Find, CommandContext.Panel));
        Assert.Equal(CommandIds.CloseTab, defaults.Resolve(KeyChord.Parse("Ctrl+W"), CommandContext.Panel));
        Assert.Contains(defaults.Uses(KeyChord.Parse("Ctrl+W")), use => use.Id == CommandIds.FindAppend);
        Assert.Contains(defaults.Uses(KeyChord.Parse("Ctrl+W")), use => use.Id == CommandIds.CloseTab);

        var changed = new Keymap(registry, new Dictionary<string, string[]>
        {
            [CommandIds.FindSubtract] = ["Ctrl+Shift+S"],
            [CommandIds.FindAppend] = [],
        });
        Assert.Null(changed.Resolve(KeyChord.Parse("Ctrl+S"), CommandContext.FindCriteria, CommandContext.Find));
        Assert.Equal(CommandIds.FindSubtract, changed.Resolve(KeyChord.Parse("Ctrl+Shift+S"), CommandContext.FindResults, CommandContext.Find));
        Assert.Null(changed.GetGestureText(CommandIds.FindAppend));
        Assert.Equal(CommandIds.CloseTab, changed.Resolve(KeyChord.Parse("Ctrl+W"), CommandContext.FindResults, CommandContext.Find, CommandContext.Panel));
        Assert.Empty(changed.Conflicts);
    }

    [Fact]
    public void The_old_default_shell_becomes_automatic_once_and_a_later_choice_stays()
    {
        // Settings saved before "auto" existed: cmd was the default, not a choice.
        var old = System.Text.Json.JsonSerializer.Deserialize("{\"Shell\":\"cmd\"}", State.StateJsonContext.Default.TerminalSettings)!;
        old.Upgrade();
        Assert.Equal(OperatingSystem.IsWindows() ? "auto" : "cmd", old.Shell);
        Assert.Equal(1, old.Defaults);
        // Chosen afterwards (the marker is saved with it): kept.
        var chosen = System.Text.Json.JsonSerializer.Deserialize("{\"Shell\":\"cmd\",\"Defaults\":1}", State.StateJsonContext.Default.TerminalSettings)!;
        chosen.Upgrade();
        Assert.Equal("cmd", chosen.Shell);
        var fresh = new State.TerminalSettings();
        fresh.Upgrade();
        Assert.Equal(OperatingSystem.IsWindows() ? "auto" : "posix", fresh.Shell);
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

    [Fact]
    public void Language_files_translate_titles_and_key_bar_labels_with_culture_fallback()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "cs.json"), """
            // Czech sample
            { "file.copy": "Kopírovat", "file.copy#bar": "Kopie", "unknown.command": "x", }
            """);
        var translations = CommandTranslations.Load(dir.Path, new System.Globalization.CultureInfo("cs-CZ"));
        Assert.NotNull(translations);
        var registry = CommandRegistry.CreateDefault(translations);
        Assert.Equal("Kopírovat", registry.Get(CommandIds.Copy)!.Title);
        Assert.Equal("Kopie", registry.Get(CommandIds.Copy)!.KeyBarLabel);
        Assert.Equal("Move", registry.Get(CommandIds.Move)!.KeyBarLabel ?? "Move"); // untranslated ids keep English
        Assert.Null(CommandTranslations.Load(dir.Path, new System.Globalization.CultureInfo("de-DE")));
        File.WriteAllText(Path.Combine(dir.Path, "de.json"), "{ broken");
        Assert.Null(CommandTranslations.Load(dir.Path, new System.Globalization.CultureInfo("de-DE")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Oversized_language_file_is_ignored_before_json_is_materialized(bool linked)
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "cs-CZ.json");
        string target = linked ? Path.Combine(dir.Path, "large-language.json") : path;
        using (var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write("{\"file.copy\":\""u8);
            byte[] padding = new byte[64 * 1024];
            Array.Fill(padding, (byte)'x');
            for (int i = 0; i < 128; i++) stream.Write(padding);
            stream.Write("\"}"u8);
        }
        if (linked)
        {
            try { File.CreateSymbolicLink(path, target); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Assert.Skip("This account cannot create a file symbolic link: " + ex.Message);
            }
            if (new FileInfo(path).Length > 1024 * 1024)
                Assert.Skip("This filesystem reports the opened target length for a file symbolic link.");
        }
        Assert.Null(CommandTranslations.Load(dir.Path, new System.Globalization.CultureInfo("cs-CZ")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unix_language_file_fifo_is_ignored_without_waiting_for_a_writer(bool linked)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix FIFO control.");
        using var dir = new TempDir();
        string input = Path.Combine(dir.Path, "cs-CZ.json");
        string pipe = linked ? Path.Combine(dir.Path, "owned.pipe") : input;
        Assert.Equal(0, MakeFifo(pipe, 384));
        if (linked) File.CreateSymbolicLink(input, pipe);
        var clock = Stopwatch.StartNew();
        Assert.Null(CommandTranslations.Load(dir.Path, new System.Globalization.CultureInfo("cs-CZ")));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Language file admission waited {clock.Elapsed}.");
    }
}
