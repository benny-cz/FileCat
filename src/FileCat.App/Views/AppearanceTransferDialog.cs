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
    internal const string ActionLabel = "Copy appearance or workspace between sessions…";
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
        var primary = new Button
        {
            Content = elevated ? "Use copied appearance for this session" : "Copy saved appearance for administrator",
        };
        var reverse = new Button
        {
            Content = elevated ? "Copy this session’s appearance" : "Review administrator appearance",
        };
        var workspace = new Button
        {
            Content = elevated ? "Use copied workspace for this session" : "Copy current workspace for administrator",
        };
        var reverseActions = new StackPanel { Spacing = 6, Children = { reverse } };
        var rememberedActions = new StackPanel { Spacing = 6 };
        var closer = new DialogCloser();
        var store = services.AdministratorTransfers;

        CheckBox? RememberChoice() => store is null ? null : new CheckBox
        {
            Content = "Remember for future administrator launches",
        };

        void ShowCopy(string text, string kind)
        {
            AutomationProperties.SetName(copied, $"{kind} text to copy");
            copied.Text = text;
            copied.IsVisible = true;
            status.Text = WindowsClipboardFileDrop.WriteText(vm.NativeOwner(), text)
                ? $"{kind} copied. Open the other FileCat window to use it."
                : $"The clipboard is unavailable. You can select and copy the {kind.ToLowerInvariant()} text below.";
        }

        void Copy()
        {
            try
            {
                var source = elevated ? services.Settings : services.NormalAppearanceSettings.ReadSaved();
                ShowCopy(AppearanceTransferV1.Export(source, ThemeManager.Names), "Appearance");
            }
            catch (FileNotFoundException) { status.Text = "This profile has no saved settings yet. Save them in a normal FileCat window first."; }
            catch (Exception ex) when (IsSettingsError(ex)) { status.Text = ex.Message; }
        }

        if (elevated)
        {
            primary.Click += async (_, _) =>
            {
                var value = await ReadAppearanceAsync(vm, "Use copied appearance");
                if (value is null) return;
                var changes = value.DescribeChanges(services.Settings);
                var remember = RememberChoice();
                if (await ConfirmChangesAsync(vm, "Use appearance in this administrator session",
                    "Apply the copied appearance to this administrator session.",
                    changes, "Apply for this session", remember))
                {
                    value.ApplyTo(services.Settings);
                    vm.RefreshAppearance();
                    string message = "Appearance applied to this administrator session.";
                    bool saveFailed = false;
                    if (remember?.IsChecked == true && store is not null)
                    {
                        try
                        {
                            store.RememberAppearance(value);
                            message = "Appearance applied and remembered for future administrator launches.";
                        }
                        catch (Exception ex) when (AdministratorTransferPreferences.IsStoreFailure(ex))
                        {
                            saveFailed = true;
                            message += " Could not remember it. " + Formatters.SafeName(ex.Message);
                        }
                    }
                    vm.Notify(message, saveFailed);
                    closer.Close(true);
                }
            };
            workspace.Click += async (_, _) =>
            {
                var value = await ReadTransferAsync(vm, "Use copied workspace", "workspace",
                    WorkspaceTransfer.MaxCharacters, WorkspaceTransfer.Decode);
                if (value is null) return;
                var folders = value.Panels.SelectMany((panel, index) => panel.Folders.Length > 0
                    ? new[] { $"Panel {index + 1}" }.Concat(panel.Folders)
                    : [$"Panel {index + 1} · Home"]).ToArray();
                const string description = "Replace the panels and tabs in this administrator session. Only available local folders are opened. You can restore the previous workspace.";
                var remember = RememberChoice();
                if (await ConfirmChangesAsync(vm, "Use workspace in this administrator session",
                    description, folders, "Apply workspace for this session", remember) &&
                    await vm.UseTransferredWorkspaceAsync(value, remember?.IsChecked == true))
                    closer.Close(true);
            };
            if (store is not null)
            {
                var forget = new Button { Content = "Forget remembered transfers…" };
                forget.Click += async (_, _) =>
                {
                    if (!await vm.Dialogs.ConfirmAsync("Forget remembered transfers?",
                        "Future administrator launches use default appearance and panels. This window keeps its current appearance and workspace.",
                        "Forget remembered transfers", danger: true)) return;
                    try
                    {
                        store.Forget();
                        status.Text = "Remembered transfers cleared.";
                    }
                    catch (Exception ex) when (AdministratorTransferPreferences.IsStoreFailure(ex))
                    {
                        status.Text = "Could not clear remembered transfers. " + Formatters.SafeName(ex.Message);
                    }
                };
                rememberedActions.Children.Add(forget);
            }
            else if (paths.DataRoot is not null)
                rememberedActions.Children.Add(Note("Administrator launches using --data apply transfers for this session only."));
            reverse.Click += (_, _) => Copy();
        }
        else
        {
            primary.Click += (_, _) => Copy();
            workspace.Click += (_, _) =>
            {
                try
                {
                    ShowCopy(WorkspaceTransfer.Capture(vm.Workspace).Encode(), "Workspace");
                    status.Text += " This includes the current panel layout and local-folder tabs.";
                }
                catch (Exception ex) when (IsSettingsError(ex)) { status.Text = ex.Message; }
            };
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
                var value = undo ? null : await ReadAppearanceAsync(vm, "Review administrator appearance");
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
                context, primary, workspace, rememberedActions,
                new Expander { Header = "Copy back…", Content = reverseActions }, status, copied,
            },
        };
        return await vm.Dialogs.ShowCustomAsync(ActionLabel.TrimEnd('…'), body,
            [new DialogButton("Close", false, IsDefault: true, IsCancel: true)],
            initialFocus: primary, closer: closer, behavior: Modal) is true;
    }

    private static Task<AppearanceTransferV1?> ReadAppearanceAsync(MainViewModel vm, string title) =>
        ReadTransferAsync(vm, title, "appearance", AppearanceTransferV1.MaxCharacters, text =>
            AppearanceTransferV1.TryParse(text, ThemeManager.Names, out var value) ? value : null);

    private static async Task<T?> ReadTransferAsync<T>(MainViewModel vm, string title, string kind, int limit,
        Func<string?, T?> decode) where T : class
    {
        var input = new TextBox { MaxLength = limit, PlaceholderText = $"Paste or enter {kind} text" };
        AutomationProperties.SetName(input, $"{char.ToUpperInvariant(kind[0])}{kind[1..]} text");
        var status = Note("");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Assertive);
        var paste = new Button { Content = $"Paste {kind} text" };
        void Paste()
        {
            string? text = WindowsClipboardFileDrop.ReadBoundedText(limit);
            if (text is null) status.Text = $"No readable {kind} text is available on the clipboard.";
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
        T? value = null;
        bool Validate()
        {
            value = decode(input.Text);
            if (value is not null) return true;
            status.Text = $"Enter {kind} text copied from FileCat. This text is invalid or uses an unsupported version.";
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
        IReadOnlyList<string> changes, string confirm, CheckBox? remember = null)
    {
        var body = new StackPanel { Spacing = 8, Children = { Note(description) } };
        if (remember is not null) body.Children.Add(remember);
        foreach (string change in changes) body.Children.Add(Note(change));
        return await vm.Dialogs.ShowCustomAsync(title, body,
            [new DialogButton(Keep, false, IsDefault: true, IsCancel: true), new DialogButton(confirm, true)],
            behavior: Modal) is true;
    }

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    private static bool IsSettingsError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException;
}
