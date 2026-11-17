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
    private const string CopyWorkspace = "Copy current workspace for administrator";
    private const string UseWorkspace = "Use copied workspace for this session";
    private const string CopyAppearance = "Copy saved appearance for administrator";
    private const string UseAppearance = "Use copied appearance for this session";
    private const string CopyBackAppearance = "Copy this session’s appearance";
    private const string ReviewBackAppearance = "Review administrator appearance";
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
            Content = elevated ? UseAppearance : CopyAppearance,
        };
        var reverse = new Button
        {
            Content = elevated ? CopyBackAppearance : ReviewBackAppearance,
        };
        var workspace = new Button
        {
            Content = elevated ? UseWorkspace : CopyWorkspace,
        };
        var reverseActions = new StackPanel { Spacing = 6, Children = { reverse } };
        var rememberedActions = new StackPanel { Spacing = 6 };
        var closer = new DialogCloser();
        var store = services.AdministratorTransfers;

        CheckBox? RememberChoice() => store is null ? null : new CheckBox
        {
            Content = "Remember for future administrator launches",
        };

        void ClearCopy()
        {
            copied.Text = null;
            copied.IsVisible = false;
        }

        void ShowCopy(string text, string kind, string nextAction)
        {
            AutomationProperties.SetName(copied, $"{kind} text to copy");
            copied.Text = text;
            copied.IsVisible = true;
            status.Text = WindowsClipboardFileDrop.WriteText(vm.NativeOwner(), text)
                ? $"{kind} copied. Choose “{nextAction}” in the other FileCat window."
                : $"Clipboard copy failed. Select and copy the {kind.ToLowerInvariant()} text below, then choose “{nextAction}” in the other FileCat window.";
        }

        void Copy()
        {
            ClearCopy();
            try
            {
                var source = elevated ? services.Settings : services.NormalAppearanceSettings.ReadSaved();
                ShowCopy(AppearanceTransferV1.Export(source, ThemeManager.Names), "Appearance",
                    elevated ? ReviewBackAppearance : UseAppearance);
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
                    WorkspaceTransfer.MaxCharacters, WorkspaceTransfer.Decode, CopyWorkspace, "normal");
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
                ClearCopy();
                try
                {
                    ShowCopy(WorkspaceTransfer.Capture(vm.Workspace).Encode(), "Workspace", UseWorkspace);
                }
                catch (Exception ex) when (IsSettingsError(ex)) { status.Text = "Could not copy the workspace. " + ex.Message; }
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
                var value = undo ? null : await ReadAppearanceAsync(vm, ReviewBackAppearance, copyBack: true);
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

    private static Task<AppearanceTransferV1?> ReadAppearanceAsync(MainViewModel vm, string title, bool copyBack = false) =>
        ReadTransferAsync(vm, title, "appearance", AppearanceTransferV1.MaxCharacters,
            text => AppearanceTransferV1.TryParse(text, ThemeManager.Names, out var value) ? value : null,
            copyBack ? CopyBackAppearance : CopyAppearance, copyBack ? "administrator" : "normal");

    private static async Task<T?> ReadTransferAsync<T>(MainViewModel vm, string title, string kind, int limit,
        Func<string?, T?> decode, string copyAction, string sourceWindow) where T : class
    {
        var input = new TextBox { MaxLength = limit, PlaceholderText = $"Paste or enter {kind} text" };
        AutomationProperties.SetName(input, $"{char.ToUpperInvariant(kind[0])}{kind[1..]} text");
        string copyHelp = $"In the {sourceWindow} FileCat window, choose “{copyAction}”, then paste the text here.";
        var status = Note(copyHelp);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Assertive);
        var paste = new Button { Content = $"Paste {kind} text" };
        string? Mismatch(string? text)
        {
            if (kind == "workspace" && AppearanceTransferV1.HasHeader(text)) return "This is appearance text.";
            if (kind == "appearance" && WorkspaceTransfer.HasHeader(text)) return "This is workspace text.";
            return null;
        }

        void Paste()
        {
            // Read up to the larger format's limit so the appearance dialog can recognize a workspace paste.
            string? text = WindowsClipboardFileDrop.ReadBoundedText(WorkspaceTransfer.MaxCharacters);
            if (text is null)
            {
                status.Text = $"No readable text is available on the clipboard. {copyHelp}";
                return;
            }
            string? error = Mismatch(text);
            if (error is null && text.Length > limit) error = $"The {kind} text exceeds the size limit.";
            if (error is not null)
            {
                input.Text = null;
                status.Text = $"{error} {copyHelp}";
                return;
            }
            input.Text = text;
            status.Text = copyHelp;
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
            string message = Mismatch(input.Text) ?? $"Enter valid {kind} text copied from FileCat.";
            status.Text = $"{message} {copyHelp}";
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
