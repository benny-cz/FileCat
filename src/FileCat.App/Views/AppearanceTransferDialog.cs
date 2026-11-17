using System.Runtime.Versioning;
using System.Security.Principal;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.State;

namespace FileCat.App.Views;

[SupportedOSPlatform("windows")]
internal static class AppearanceTransferDialog
{
    internal const string ActionLabel = "Copy appearance between sessions…";
    private const string Keep = "Keep my settings";
    private static readonly DialogBehavior Modal = new() { DismissOnBackdrop = false };

    internal static async Task<bool> ShowAsync(MainViewModel vm, bool hasDraftChanges)
    {
        bool elevated = WindowsTokenAuthority.IsHighIntegrity;
        var services = vm.Services;
        using var identity = WindowsIdentity.GetCurrent();
        var paths = services.Paths;
        var context = new SelectableTextBlock
        {
            Text = $"Account {identity.Name}\nProfile {paths.ProfileName}\n{paths.SettingsFile}",
            TextWrapping = TextWrapping.Wrap,
        };
        var status = Note("");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var copied = new TextBox { IsReadOnly = true, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(copied, "Appearance text to copy");
        var primary = new Button
        {
            Content = elevated ? "Use copied appearance for this session" : "Copy saved appearance for administrator",
        };
        var reverse = new Button
        {
            Content = elevated ? "Copy this session’s appearance" : "Review administrator appearance",
        };
        var reverseActions = new StackPanel { Spacing = 6, Children = { reverse } };
        var closer = new DialogCloser();

        void Copy()
        {
            try
            {
                var source = elevated ? services.Settings : services.NormalAppearanceSettings.ReadSaved();
                string text = AppearanceTransferV1.Export(source, ThemeManager.Names);
                copied.Text = text;
                copied.IsVisible = true;
                status.Text = WindowsClipboardFileDrop.WriteText(vm.NativeOwner(), text)
                    ? "Appearance copied. Open the other FileCat window to use it."
                    : "The clipboard is unavailable. You can select and copy the appearance text below.";
            }
            catch (FileNotFoundException) { status.Text = "This profile has no saved settings yet. Save them in a normal FileCat window first."; }
            catch (Exception ex) when (IsSettingsError(ex)) { status.Text = ex.Message; }
        }

        if (elevated)
        {
            primary.Click += async (_, _) =>
            {
                var value = await ReadTransferAsync(vm, "Use copied appearance");
                if (value is null) return;
                var changes = value.DescribeChanges(services.Settings);
                if (await ConfirmChangesAsync(vm, "Use appearance in this administrator session",
                    "This applies to the current session. The next administrator launch starts with defaults.",
                    changes, "Apply for this session"))
                {
                    value.ApplyTo(services.Settings);
                    vm.RefreshAppearance();
                    vm.Notify("Appearance applied to this administrator session. The next administrator launch starts with defaults.");
                    closer.Close(true);
                }
            };
            reverse.Click += (_, _) => Copy();
        }
        else
        {
            primary.Click += (_, _) => Copy();
            var undo = new Button { Content = "Undo appearance replacement" };
            reverseActions.Children.Add(undo);
            reverse.Click += async (_, _) => await ReviewNormalAsync(undo: false);
            undo.Click += async (_, _) => await ReviewNormalAsync(undo: true);
            reverse.IsEnabled = undo.IsEnabled = !services.SettingsReadOnly;
        }

        async Task ReviewNormalAsync(bool undo)
        {
            if (hasDraftChanges)
            {
                status.Text = "Save or discard the open Settings draft before replacing saved appearance.";
                return;
            }
            try
            {
                var value = undo ? null : await ReadTransferAsync(vm, "Review administrator appearance");
                if (!undo && value is null) return;
                var saved = services.NormalAppearanceSettings;
                var review = undo ? saved.ReviewUndo() : saved.ReviewReplacement(value!);
                if (review.Changes.Count == 0)
                {
                    status.Text = "These appearance settings already match.";
                    return;
                }
                if (!await ConfirmChangesAsync(vm, undo ? "Undo appearance replacement" : "Replace normal appearance settings",
                    $"Profile {paths.ProfileName}\n{paths.SettingsFile}", review.Changes,
                    undo ? "Restore previous appearance" : "Replace these appearance settings")) return;
                string backup = services.TryReplaceNormalAppearance(review);
                vm.RefreshAppearance();
                await vm.Dialogs.ShowCustomAsync("Normal appearance saved", new SelectableTextBlock
                {
                    Text = $"Previous settings were saved to\n{backup}",
                    TextWrapping = TextWrapping.Wrap,
                }, [new DialogButton("Close", false, IsDefault: true, IsCancel: true)], behavior: Modal);
                closer.Close(true);
            }
            catch (Exception ex) when (IsSettingsError(ex)) { status.Text = ex.Message; }
        }

        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                context, primary,
                new Expander { Header = "Copy back…", Content = reverseActions }, status, copied,
            },
        };
        return await vm.Dialogs.ShowCustomAsync(ActionLabel.TrimEnd('…'), body,
            [new DialogButton("Close", false, IsDefault: true, IsCancel: true)],
            initialFocus: primary, closer: closer, behavior: Modal) is true;
    }

    private static async Task<AppearanceTransferV1?> ReadTransferAsync(MainViewModel vm, string title)
    {
        var input = new TextBox { MaxLength = AppearanceTransferV1.MaxCharacters, PlaceholderText = "Paste or enter appearance text" };
        AutomationProperties.SetName(input, "Appearance text");
        var status = Note("");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Assertive);
        var paste = new Button { Content = "Paste appearance text" };
        void Paste()
        {
            string? text = WindowsClipboardFileDrop.ReadBoundedText(AppearanceTransferV1.MaxCharacters);
            if (text is null) status.Text = "No readable appearance text is available on the clipboard.";
            else
            {
                input.Text = text;
                status.Text = "";
            }
        }
        paste.Click += (_, _) => Paste();
        input.PastingFromClipboard += (_, e) =>
        {
            e.Handled = true;
            Paste();
        };
        AppearanceTransferV1? value = null;
        bool Validate()
        {
            if (AppearanceTransferV1.TryParse(input.Text, ThemeManager.Names, out value)) return true;
            status.Text = "Enter appearance text copied from FileCat. This text is invalid or uses an unsupported version.";
            input.Focus();
            return false;
        }
        var body = new StackPanel { Spacing = 8, Children = { input, paste, status } };
        var result = await vm.Dialogs.ShowCustomAsync(title, body,
            [new DialogButton("Cancel", false, IsCancel: true), new DialogButton("Review", true, IsDefault: true) { Validate = Validate }],
            initialFocus: input, behavior: Modal);
        return result is true ? value : null;
    }

    private static async Task<bool> ConfirmChangesAsync(MainViewModel vm, string title, string description,
        IReadOnlyList<string> changes, string confirm)
    {
        var body = new StackPanel { Spacing = 8, Children = { Note(description) } };
        foreach (string change in changes) body.Children.Add(Note(change));
        return await vm.Dialogs.ShowCustomAsync(title, body,
            [new DialogButton(Keep, false, IsDefault: true, IsCancel: true), new DialogButton(confirm, true)],
            behavior: Modal) is true;
    }

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    private static bool IsSettingsError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException;
}
