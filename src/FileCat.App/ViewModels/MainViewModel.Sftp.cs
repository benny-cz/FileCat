using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.Core.Diagnostics;
using FileCat.Core.State;
using FileCat.Remote.Sftp;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.ViewModels;

/// <summary>
/// SFTP connections (P6, plan §14.1): saved profiles without secrets, explicit host-key decisions, and prompts for
/// passwords, passphrases, and keyboard-interactive codes. Connecting happens on background threads; their questions
/// come here through the dispatcher.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Answers connection questions with this window's dialogs.</summary>
    private sealed class WindowInteraction(MainViewModel vm) : IRemoteInteraction
    {
        public HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check) =>
            OnUi(() => vm.DecideHostKeyAsync(profile, check), HostKeyDecision.Reject);

        public SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request) =>
            OnUi(() => vm.AskSecretAsync(profile, request), null);

        public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts) =>
            OnUi(() => vm.AnswerPromptsAsync(profile, instruction, prompts), null);

        private static T OnUi<T>(Func<Task<T>> ask, T refused)
        {
            // Blocking the UI thread for its own dialog would hang it: such a request is refused (and is a bug).
            if (Dispatcher.UIThread.CheckAccess())
            {
                AppLog.Warn("A connection question was asked on the UI thread and refused.");
                return refused;
            }
            return Dispatcher.UIThread.InvokeAsync(ask).GetAwaiter().GetResult();
        }
    }

    private void AttachRemoteInteraction() => Services.Sftp.Interaction = new WindowInteraction(this);

    private static TextBlock Para(string text, params string[] classes)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 600 };
        foreach (var c in classes) t.Classes.Add(c);
        return t;
    }

    private static TextBlock Mono(string text) =>
        new() { Text = text, FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"), TextWrapping = TextWrapping.Wrap, MaxWidth = 600 };

    internal async Task<HostKeyDecision> DecideHostKeyAsync(RemoteProfile profile, HostKeyCheck check)
    {
        string server = profile.Host + (profile.Port == 22 ? "" : ":" + profile.Port);
        string type = check.KeyType.Replace("ssh-", "").ToUpperInvariant();
        if (check.Status == HostKeyStatus.Changed)
        {
            var body = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Para($"The key of {server} is not the one that was remembered. Someone may be intercepting the connection, or the server was reinstalled or reconfigured.", "error"),
                    Para("Remembered" + (check.Source is { } src ? $" (in {src}):" : ":")),
                    Mono(check.KnownFingerprint ?? "?"),
                    Para($"Offered now ({type}):"),
                    Mono(check.Fingerprint),
                    Para("Connect only if the server's administrator confirms the new key."),
                },
            };
            var answer = await Dialogs.ShowCustomAsync("Server key changed", body,
                [new DialogButton("Cancel", "cancel", IsDefault: true, IsCancel: true), new DialogButton("Replace the remembered key and connect", "replace", IsDanger: true)]);
            return answer as string == "replace" ? HostKeyDecision.AcceptAndRemember : HostKeyDecision.Reject;
        }
        var first = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Para(check.OtherTypesKnown
                    ? $"FileCat knows a different type of key for {server}. The server now identifies itself with this {type} key:"
                    : $"This is the first connection to {server}. The server identifies itself with this {type} key:"),
                Mono(check.Fingerprint),
                Para("Compare it with the fingerprint from the server's administrator (ssh-keygen -lf on the server's public key). If they differ, cancel.", "muted"),
            },
        };
        var decision = await Dialogs.ShowCustomAsync("New server key", first,
        [
            new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Connect once", "once"),
            new DialogButton("Trust and connect", "trust", IsDefault: true),
        ]);
        return (decision as string) switch
        {
            "trust" => HostKeyDecision.AcceptAndRemember,
            "once" => HostKeyDecision.AcceptOnce,
            _ => HostKeyDecision.Reject,
        };
    }

    internal async Task<SecretAnswer?> AskSecretAsync(RemoteProfile profile, SecretRequest request)
    {
        string what = request.Passphrase ? "Key passphrase" : "Password";
        var box = new TextBox { PasswordChar = '•', MinWidth = 320 };
        Avalonia.Automation.AutomationProperties.SetName(box, what);
        var save = new CheckBox
        {
            Content = OperatingSystem.IsWindows() ? "Save in Windows Credential Manager" : "Save in the system keychain",
            IsChecked = profile.SaveSecret && !profile.Temporary,
            IsVisible = request.CanSave && !profile.Temporary,
        };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Para(request.Passphrase
            ? $"The key file {Path.GetFileName(profile.KeyFile)} for {profile.Display} is protected by a passphrase."
            : $"Sign in to {profile.Display}."));
        if (request.Retry) body.Children.Add(Para(request.Passphrase ? "That passphrase did not open the key. Try again." : "The server did not accept that password. Try again.", "error"));
        body.Children.Add(box);
        body.Children.Add(save);
        var answer = await Dialogs.ShowCustomAsync(what, body,
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Connect", "ok", IsDefault: true)], box);
        return answer as string == "ok" ? new SecretAnswer(box.Text ?? "", save.IsVisible && save.IsChecked == true) : null;
    }

    internal async Task<IReadOnlyList<string>?> AnswerPromptsAsync(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Para(instruction.Trim().Length > 0 ? instruction.Trim() : $"{profile.Display} asks:"));
        var boxes = new List<TextBox>();
        foreach (var (prompt, echo) in prompts)
        {
            var box = new TextBox { MinWidth = 320, PasswordChar = echo ? '\0' : '•' };
            Avalonia.Automation.AutomationProperties.SetName(box, prompt.Trim().TrimEnd(':'));
            body.Children.Add(Para(prompt.Trim()));
            body.Children.Add(box);
            boxes.Add(box);
        }
        var answer = await Dialogs.ShowCustomAsync($"Sign in to {profile.Host}", body,
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Continue", "ok", IsDefault: true)], boxes.FirstOrDefault());
        return answer as string == "ok" ? boxes.Select(b => b.Text ?? "").ToList() : null;
    }

    // ---- Commands ----------------------------------------------------------------------------------------

    /// <summary>Saved connections first; Enter connects, Shift+Enter edits, Delete removes (after asking).</summary>
    private async Task ConnectSftpAsync(PanelViewModel? panel = null)
    {
        // A snapshot: the chooser reports indexes into the list it was shown.
        var saved = Services.Settings.RemoteProfiles.ToList();
        if (saved.Count == 0)
        {
            await EditSftpConnectionAsync(null, panel);
            return;
        }
        var items = saved.Select(p => new ChoiceItem(p.Name.Length > 0 ? p.Name : p.Display, p.Display + (p.InitialPath is { Length: > 0 } ip ? " · " + ip : ""))).ToList();
        items.Add(new ChoiceItem("New connection…", "Server, user, and how to sign in"));
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("Connect to an SFTP server", items)
        {
            Hint = "Type to filter · Enter connects · Shift+Enter edits · Ctrl+Del removes",
            AllowDelete = true,
        });
        var removed = r.Deleted.Where(i => i < saved.Count).Select(i => saved[i]).ToList();
        if (removed.Count > 0 && await Dialogs.ConfirmAsync("Remove connections",
                $"Remove {Formatters.Plural(removed.Count, "saved connection", "saved connections")} ({string.Join(", ", removed.Select(p => p.Name))}) and any saved password? Remembered server keys stay.",
                "Remove", danger: true))
        {
            foreach (var p in removed)
            {
                Services.Sftp.ForgetSecret(p);
                Services.Sftp.Disconnect(p.Id);
                Services.Settings.RemoteProfiles.Remove(p);
            }
            Services.SaveSettings();
        }
        if (r.Index < 0) return;
        if (r.Index == saved.Count)
        {
            await EditSftpConnectionAsync(null, panel);
            return;
        }
        var profile = saved[r.Index];
        if (!Services.Settings.RemoteProfiles.Contains(profile)) return;
        if (r.Alternate) await EditSftpConnectionAsync(profile, panel);
        else OpenSftp(profile, panel);
    }

    private void OpenSftp(RemoteProfile profile, PanelViewModel? panel)
    {
        var target = panel ?? Workspace.ActivePanel;
        target?.ActiveTab?.Navigate(SftpProvider.At(profile, profile.InitialPath));
        if (target is not null) Workspace.Activate(target);
        View.FocusActivePanel();
    }

    /// <summary>New or edited connection. Changing where a saved password goes (server, port, user) forgets it.</summary>
    private async Task EditSftpConnectionAsync(RemoteProfile? existing, PanelViewModel? panel)
    {
        TextBox Box(string? text, string name, double width = 260)
        {
            var box = new TextBox { Text = text ?? "", MinWidth = width };
            Avalonia.Automation.AutomationProperties.SetName(box, name);
            return box;
        }
        var name = Box(existing?.Name, "Connection name");
        var host = Box(existing?.Host, "Server");
        var port = Box((existing?.Port ?? 22).ToString(System.Globalization.CultureInfo.InvariantCulture), "Port", 70);
        var user = Box(existing?.User ?? Environment.UserName, "User name", 160);
        var auth = new ComboBox { ItemsSource = new[] { "Password", "Private key file", "Keyboard-interactive (codes, prompts)" }, MinWidth = 260 };
        Avalonia.Automation.AutomationProperties.SetName(auth, "Sign in with");
        auth.SelectedIndex = existing?.Auth switch { RemoteAuth.Key => 1, RemoteAuth.KeyboardInteractive => 2, _ => 0 };
        var keyFile = Box(existing?.KeyFile ?? DefaultKeyFile(), "Private key file", 360);
        var browse = new Button { Content = "Browse…" };
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { keyFile, browse } };
        var folder = Box(existing?.InitialPath, "Start folder", 360);
        folder.PlaceholderText = "Home folder";
        bool persistent = Services.Platform.Secrets.IsPersistent;
        var save = new CheckBox
        {
            Content = persistent ? (OperatingSystem.IsWindows() ? "Save the password or passphrase in Windows Credential Manager" : "Save the password or passphrase in the system keychain")
                : "Passwords are kept only until FileCat closes (no system keychain is available)",
            IsChecked = persistent && existing?.SaveSecret == true,
            IsEnabled = persistent,
        };
        var problem = new TextBlock { Classes = { "error" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 600 };
        void UpdateKey() => keyRow.IsVisible = auth.SelectedIndex == 1;
        auth.SelectionChanged += (_, _) => UpdateKey();
        UpdateKey();
        browse.Click += async (_, _) =>
        {
            if (View.TopLevel is not { } top) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Private key file", AllowMultiple = false });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } picked) keyFile.Text = picked;
        };
        string? Validate()
        {
            if (string.IsNullOrWhiteSpace(host.Text)) return "Enter the server's name or address.";
            if (host.Text.Contains("://", StringComparison.Ordinal) || host.Text.Contains('/') || host.Text.Contains('@')) return "Enter only the server's name (without sftp://, user, or path).";
            if (!int.TryParse(port.Text, out int p) || p is < 1 or > 65535) return "The port is a number from 1 to 65535.";
            if (string.IsNullOrWhiteSpace(user.Text)) return "Enter the user name.";
            if (auth.SelectedIndex == 1 && !File.Exists(keyFile.Text)) return "The private key file does not exist.";
            return null;
        }
        void Refresh() => problem.Text = Validate() ?? "";
        foreach (var b in new[] { host, port, user, keyFile }) b.TextChanged += (_, _) => Refresh();
        auth.SelectionChanged += (_, _) => Refresh();
        Refresh();
        StackPanel Row(string label, Control control)
        {
            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(new TextBlock { Text = label });
            row.Children.Add(control);
            return row;
        }
        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Row("Name (optional):", name),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Row("Server:", host), Row("Port:", port), Row("User:", user) } },
                Row("Sign in with:", auth),
                keyRow,
                Row("Start folder (optional):", folder),
                save,
                problem,
            },
        };
        var answer = await Dialogs.ShowCustomAsync(existing is null ? "New SFTP connection" : "Edit SFTP connection", body,
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Save", "save"), new DialogButton("Connect", "connect", IsDefault: true)], host,
            () => Validate() is null);
        if (answer as string is not ("save" or "connect") || Validate() is not null) return;

        var profile = existing ?? new RemoteProfile();
        string newHost = host.Text!.Trim(), newUser = user.Text!.Trim();
        int newPort = int.Parse(port.Text!, System.Globalization.CultureInfo.InvariantCulture);
        // A saved password belongs to one server and user: never send it anywhere else.
        if (existing is not null && (!string.Equals(existing.Host, newHost, StringComparison.OrdinalIgnoreCase) || existing.Port != newPort || existing.User != newUser))
        {
            Services.Sftp.ForgetSecret(existing);
            existing.SaveSecret = false;
        }
        profile.Name = string.IsNullOrWhiteSpace(name.Text) ? $"{newUser}@{newHost}" : name.Text.Trim();
        profile.Host = newHost;
        profile.Port = newPort;
        profile.User = newUser;
        profile.Auth = auth.SelectedIndex switch { 1 => RemoteAuth.Key, 2 => RemoteAuth.KeyboardInteractive, _ => RemoteAuth.Password };
        profile.KeyFile = profile.Auth == RemoteAuth.Key ? keyFile.Text!.Trim() : null;
        profile.InitialPath = string.IsNullOrWhiteSpace(folder.Text) ? null : folder.Text.Trim();
        bool keep = save.IsChecked == true && persistent;
        if (!keep && profile.SaveSecret) Services.Sftp.ForgetSecret(profile);
        profile.SaveSecret = keep;
        profile.Temporary = false;
        if (existing is null) Services.Settings.RemoteProfiles.Add(profile);
        Services.Sftp.Disconnect(profile.Id);
        Services.SaveSettings();
        if (answer as string == "connect") OpenSftp(profile, panel);
        else Notify($"Saved the connection \"{profile.Name}\". Connect with Alt+F1 or Alt+F2.");
    }

    private static string? DefaultKeyFile()
    {
        string ssh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        return new[] { "id_ed25519", "id_ecdsa", "id_rsa" }.Select(n => Path.Combine(ssh, n)).FirstOrDefault(File.Exists);
    }

    /// <summary>Closes the connections of the active tab's server; tabs keep their locations and reconnect on refresh.</summary>
    private void DisconnectSftp()
    {
        if (ActiveTab?.Location is not { Scheme: Core.Resources.Schemes.Sftp, Session: { } id } || Services.FindRemoteProfile(id) is not { } profile)
        {
            Notify("The active panel does not show an SFTP server.");
            return;
        }
        Services.Sftp.Disconnect(id);
        Notify($"Disconnected from {profile.Display}. Refreshing a panel there connects again.");
    }
}
