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
