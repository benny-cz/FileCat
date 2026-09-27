using Avalonia.Controls;

namespace FileCat.App.ViewModels;

public sealed record PromptOptions(string Title, string Message)
{
    public string Text { get; init; } = string.Empty;
    public Func<string, string?>? Validate { get; init; }
    public IReadOnlyList<string>? History { get; init; }
    /// <summary>Select the name without its extension (rename, create file).</summary>
    public bool SelectStem { get; init; }
    public string ConfirmText { get; init; } = "OK";
    public string? CheckboxText { get; init; }
    public bool CheckboxValue { get; init; }
    public string? Hint { get; init; }
}

public sealed record PromptResult(string Text, bool Checked);

public sealed record ChoiceItem(string Title, string? Detail = null, string? Gesture = null, object? Tag = null)
{
    public bool IsHeader { get; init; }
    public bool Pinned { get; init; }
}

/// <param name="Index">Chosen item index, or -1 when canceled.</param>
/// <param name="Alternate">Shift+Enter: the alternate action (e.g. open in the target panel).</param>
/// <param name="Deleted">Items the user removed with Delete (history lists).</param>
public sealed record ChoiceResult(int Index, bool Alternate, IReadOnlyList<int> Deleted)
{
    public static ChoiceResult Canceled { get; } = new(-1, false, []);

    /// <summary>Items whose pinned state the user toggled with Insert (history lists).</summary>
    public IReadOnlyList<int> PinToggled { get; init; } = [];
}

public sealed record KeyboardHelpEntry(string Id, string Title, string Category, string? Gestures, string? Description, bool Enabled = true, string? UnavailableReason = null);

public sealed record ChoiceOptions(string Title, IReadOnlyList<ChoiceItem> Items)
{
    public string? Hint { get; init; }
    public int SelectedIndex { get; init; }
    public bool AllowDelete { get; init; }
    /// <summary>Insert pins or unpins the selected item (history lists keep pinned items first and never trim them).</summary>
    public bool AllowPin { get; init; }
    public string? AlternateHint { get; init; }
}

/// <summary>A button in a custom dialog; <see cref="Result"/> is returned when chosen.</summary>
public sealed record DialogButton(string Text, object Result, bool IsDefault = false, bool IsCancel = false, bool IsDanger = false);

/// <summary>Overlay dialogs owned by the main window. All are keyboard-first: Enter confirms, Esc cancels.</summary>
public interface IDialogService
{
    Task<PromptResult?> PromptAsync(PromptOptions options);
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool danger = false, string cancelText = "Cancel");
    Task AlertAsync(string title, string message);
    Task<ChoiceResult> ChooseAsync(ChoiceOptions options);
    Task<string?> KeyboardReferenceAsync(IReadOnlyList<KeyboardHelpEntry> commands);
    /// <summary>Hosts arbitrary content with buttons; returns the chosen button result or null on Esc.</summary>
    Task<object?> ShowCustomAsync(string title, Control content, IReadOnlyList<DialogButton> buttons, Control? initialFocus = null);
    bool IsOpen { get; }
}

/// <summary>UI-only actions a view model asks the window to perform.</summary>
public interface IViewActions
{
    void FocusActivePanel();
    Task<string?> RenameInlineAsync(PromptOptions options);
    void FocusPathBox();
    void FocusCommandLine();
    /// <summary>Closes the main window (used when the user chose to exit once operations finish).</summary>
    void CloseWhenIdle();
    void OpenMenuBar();
    void ShowContextMenu();
    void ShowNotification(string message, bool isError = false);
    /// <summary>Rebuilds menus and gesture hints after key bindings change.</summary>
    void ReloadChrome();
    Avalonia.Input.Platform.IClipboard? Clipboard { get; }
    TopLevel? TopLevel { get; }
}
