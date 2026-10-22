using Avalonia.Input;
using FileCat.Core.Commands;

namespace FileCat.App.Services;

/// <summary>Maps Avalonia key events to portable chords. Aliased enum values are mapped explicitly.</summary>
public static class KeyMapper
{
    public static KeyMods ToMods(KeyModifiers m)
    {
        var r = KeyMods.None;
        if ((m & KeyModifiers.Control) != 0) r |= KeyMods.Ctrl;
        if ((m & KeyModifiers.Shift) != 0) r |= KeyMods.Shift;
        if ((m & KeyModifiers.Alt) != 0) r |= KeyMods.Alt;
        if ((m & KeyModifiers.Meta) != 0) r |= KeyMods.Meta;
        return r;
    }

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    /// <summary>
    /// The modifiers held once a modifier key's own press or release has taken effect. Windows and macOS send the key
    /// with that state; X11 sends it with the state before, so on Linux Ctrl's press came without Ctrl and its release
    /// with it, and the key bar showed each layer one key late.
    /// </summary>
    public static KeyModifiers AfterModifierKey(Key key, KeyModifiers modifiers, bool pressed)
    {
        var own = key switch
        {
            Key.LeftCtrl or Key.RightCtrl => KeyModifiers.Control,
            Key.LeftShift or Key.RightShift => KeyModifiers.Shift,
            Key.LeftAlt or Key.RightAlt => KeyModifiers.Alt,
            Key.LWin or Key.RWin => KeyModifiers.Meta,
            _ => KeyModifiers.None,
        };
        return pressed ? modifiers | own : modifiers & ~own;
    }

    public static KeyChord? ToChord(Key key, KeyModifiers modifiers)
    {
        if (IsModifierKey(key) || key == Key.None) return null;
        string name = key switch
        {
            Key.Enter => "Enter",
            Key.Back => "Back",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Add => "Add",
            Key.Subtract => "Subtract",
            Key.Multiply => "Multiply",
            Key.Divide => "Divide",
            Key.OemPipe => "Backslash",
            Key.OemComma => "Comma",
            Key.OemTilde => "Backtick",
            Key.Escape => "Escape",
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            >= Key.D0 and <= Key.D9 => "D" + (key - Key.D0),
            >= Key.F1 and <= Key.F24 => "F" + (key - Key.F1 + 1),
            >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
            _ => key.ToString(),
        };
        return new KeyChord(name, ToMods(modifiers));
    }

    /// <summary>⌘Q: quits on macOS.</summary>
    public static readonly KeyChord MacQuit = new("Q", KeyMods.Meta);

    /// <summary>
    /// The chord a macOS ⌘ combination stands for: ⌘Q itself (quit), otherwise the same keys with Ctrl in place of ⌘.
    /// Null when the chord has no ⌘ or already has Ctrl.
    /// </summary>
    public static KeyChord? MacCommandAlias(KeyChord chord)
    {
        if ((chord.Mods & KeyMods.Meta) == 0 || (chord.Mods & KeyMods.Ctrl) != 0) return null;
        if (chord == MacQuit) return MacQuit;
        return chord with { Mods = (chord.Mods & ~KeyMods.Meta) | KeyMods.Ctrl };
    }

    public static KeyGesture? ToGesture(KeyChord chord)
    {
        Key key = chord.Key switch
        {
            "Enter" => Key.Enter,
            "Back" => Key.Back,
            "PageUp" => Key.PageUp,
            "PageDown" => Key.PageDown,
            "Backslash" => Key.OemPipe,
            "Comma" => Key.OemComma,
            "Backtick" => Key.OemTilde,
            _ => Enum.TryParse<Key>(chord.Key, true, out var k) ? k : Key.None,
        };
        if (key == Key.None) return null;
        var m = KeyModifiers.None;
        if ((chord.Mods & KeyMods.Ctrl) != 0) m |= KeyModifiers.Control;
        if ((chord.Mods & KeyMods.Shift) != 0) m |= KeyModifiers.Shift;
        if ((chord.Mods & KeyMods.Alt) != 0) m |= KeyModifiers.Alt;
        if ((chord.Mods & KeyMods.Meta) != 0) m |= KeyModifiers.Meta;
        return new KeyGesture(key, m);
    }
}
