using Avalonia;
using FileCat.Core.State;

namespace FileCat.App.Services;

/// <summary>
/// The sizes the user gives resizable dialogs (<see cref="ViewModels.DialogBehavior.SizeKey"/>), kept in the settings and saved
/// as other settings are. Settings FileCat may not write (an administrator session, a profile from a newer FileCat) keep them
/// only until FileCat closes.
/// </summary>
internal sealed class DialogSizeStore(AppSettings settings, Action save)
{
    internal Size? Find(string key) => DialogSizeMemory.Find(settings, key) is { } size ? new Size(size.Width, size.Height) : null;

    /// <summary>Remembers the size for <paramref name="key"/>, or forgets it when null; saves the settings when that changed them.</summary>
    internal void Remember(string key, Size? size)
    {
        if (size is { } s ? DialogSizeMemory.Remember(settings, key, s.Width, s.Height) : DialogSizeMemory.Forget(settings, key))
            save();
    }
}
