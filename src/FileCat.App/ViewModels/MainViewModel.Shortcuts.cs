using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.Core.Commands;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// A command's shortcut, changed by pressing it (from the keyboard reference, F2) instead of writing Settings →
    /// Keyboard lines: the chord replaces the command's shortcuts, and a command that used it gives it up, after the
    /// dialog says which. "No shortcut" leaves the command unbound; "Default" restores its built-in shortcuts.
    /// </summary>
    internal async Task ChangeShortcutAsync(string id)
    {
        if (Services.Commands.Get(id) is not { } def) return;
        var now = Services.Keymap.GetChords(id);
        KeyChord? pressed = null;
        string? taker = null;
        var keys = new TextBlock { Text = "Press the new shortcut", FontSize = 18, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        var status = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center };
        var capture = new Border
        {
            Focusable = true,
            MinWidth = 420,
            MinHeight = 60,
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = keys,
            [!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("FcFocusBorder"),
        };
        AutomationProperties.SetName(capture, "New shortcut");
        var closer = new DialogCloser();
        capture.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            // Enter assigns (at once, however quickly it follows the shortcut) and Esc cancels, as in every dialog,
            // and Tab moves on; any other key (with or without modifiers) is the shortcut. A modifier alone waits.
            if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Enter && pressed is not null)
            {
                e.Handled = true;
                closer.Close("assign");
                return;
            }
            if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Enter or Key.Escape or Key.Tab) return;
            if (KeyMapper.ToChord(e.Key, e.KeyModifiers) is not { } chord) return;
            e.Handled = true;
            pressed = chord;
            keys.Text = chord.ToDisplayString();
            taker = ShortcutOwner(chord, def);
            status.Text = now.Contains(chord) ? "Already this command's shortcut."
                : taker is null ? "Free: no other command uses it here."
                : $"Used by \"{Services.Commands.Get(taker)?.Title}\": assigning takes it from there.";
            status.Classes.Set("warning", taker is not null);
        }, RoutingStrategies.Tunnel);
        string current = now.Count == 0 ? "no shortcut" : string.Join(", ", now.Select(c => c.ToDisplayString()));
        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = $"\"{def.Title}\" · now {current}", TextWrapping = TextWrapping.Wrap },
                capture,
                status,
            },
        };
        var result = await Dialogs.ShowCustomAsync("Change shortcut", body,
        [
            new DialogButton("Cancel", "cancel", IsCancel: true),
            new DialogButton("No shortcut", "none") { IsAvailable = () => now.Count > 0 },
            new DialogButton("Default", "default") { IsAvailable = () => Services.Settings.KeyBindings.ContainsKey(id) },
            new DialogButton("Assign", "assign", IsDefault: true) { IsAvailable = () => pressed is not null },
        ], capture, closer: closer);
        var bindings = new Dictionary<string, string[]>(Services.Settings.KeyBindings, StringComparer.Ordinal);
        string done;
        switch (result as string)
        {
            case "assign" when pressed is { } chord:
                bindings[id] = [chord.ToDisplayString()];
                if (taker is not null)
                    bindings[taker] = Services.Keymap.GetChords(taker).Where(c => c != chord).Select(c => c.ToDisplayString()).ToArray();
                done = $"\"{def.Title}\": {chord.ToDisplayString()}" + (taker is null ? "." : $" (no longer \"{Services.Commands.Get(taker)?.Title}\").");
                break;
            case "none":
                bindings[id] = [];
                done = $"\"{def.Title}\" has no shortcut now.";
                break;
            case "default":
                bindings.Remove(id);
                done = $"\"{def.Title}\" has its default shortcut again.";
                break;
            default:
                return;
        }
        Services.Settings.KeyBindings = bindings;
        ApplySettings();
        Notify(done);
    }

    /// <summary>The other command a chord runs where <paramref name="def"/> would run (its context, or everywhere).</summary>
    private string? ShortcutOwner(KeyChord chord, CommandDefinition def) =>
        Services.Commands.All.FirstOrDefault(d => d.Id != def.Id && Services.Keymap.GetChords(d.Id).Contains(chord) &&
            (d.Context == def.Context || d.Context == CommandContext.Global || def.Context == CommandContext.Global))?.Id;
}
