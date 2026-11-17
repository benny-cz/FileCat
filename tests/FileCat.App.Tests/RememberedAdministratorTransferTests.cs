using System.Security.Principal;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.State;
using FileCat.Platform.Windows;
using Microsoft.Win32;

namespace FileCat.App.Tests;

public sealed class RememberedAdministratorTransferTests
{
    [Fact]
    public void Normal_session_cannot_open_or_create_administrator_transfer_storage()
    {
        if (!OperatingSystem.IsWindows() || WindowsTokenAuthority.IsHighIntegrity) return;
        var registry = new AdministratorTransferRegistry(@"SOFTWARE\FileCat.TransferTests." + Guid.NewGuid().ToString("N"));
        Assert.Throws<UnauthorizedAccessException>(() => registry.Read("scope", 10));
        Assert.Throws<UnauthorizedAccessException>(() => registry.Save("scope", "data", 10));
        Assert.Throws<UnauthorizedAccessException>(() => registry.Forget("scope"));
    }

    [AvaloniaFact]
    public async Task Genuine_high_token_remembers_appearance_and_local_workspace_across_startups()
    {
        if (!OperatingSystem.IsWindows() || !WindowsTokenAuthority.IsHighIntegrity)
            Assert.Skip("Run in a high-integrity Windows test process.");
        string root = Path.Combine(Path.GetTempPath(), "filecat-remembered-transfer-" + Guid.NewGuid().ToString("N"));
        string keyName = @"SOFTWARE\FileCat.TransferTests." + Guid.NewGuid().ToString("N");
        var store = new AdministratorTransferRegistry(keyName);
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        try
        {
            var paths = AppPaths.Resolve("Work", overrideRoot: root);
            string local = Directory.CreateDirectory(Path.Combine(root, "local-folder")).FullName;
            const string primary = """{"SchemaVersion":1,"Theme":"ClassicDark","Editor":{"Executable":"normal-only.exe"}}""";
            const string workspace = """{"SchemaVersion":1,"Name":"normal-only"}""";
            File.WriteAllText(paths.SettingsFile, primary);
            File.WriteAllText(paths.SettingsFile + ".bak", primary);
            File.WriteAllText(paths.WorkspaceFile, workspace);
            File.WriteAllText(paths.WorkspaceFile + ".bak", workspace);
            var packet = new WorkspaceTransfer([new([local], 0, null)], new(0, false, 1, []), 0);
            string appearance = AppearanceTransferV1.Export(new AppSettings { Theme = "Psychedelic", FontSize = 17 }, ThemeManager.Names);
            var first = AppServices.CreateForPathsWithAdministratorRegistry(paths, store);
            var firstVm = new MainViewModel(first);
            var window = new MainWindow(firstVm, null) { Width = 1200, Height = 800 };
            try
            {
                Assert.Equal("System", first.Settings.Theme);
                Assert.Null(first.AdministratorTransfers!.LoadWorkspace());
                firstVm.Initialize(null);
                window.Show();
                await ApplyAndRememberAsync(firstVm, window, "Use copied appearance for this session",
                    "Appearance text", appearance, "Apply for this session");
                await ApplyAndRememberAsync(firstVm, window, "Use copied workspace for this session",
                    "Workspace text", packet.Encode(), "Apply workspace for this session");
            }
            finally { AccessibilityTests.Close(first, window, Path.Combine(root, "window")); }
            using (var reopened = AppServices.CreateForPathsWithAdministratorRegistry(paths, store))
            {
                Assert.Equal("Psychedelic", reopened.Settings.Theme);
                Assert.Equal(17, reopened.Settings.FontSize);
                Assert.Null(reopened.Settings.Editor);
                var vm = new MainViewModel(reopened);
                using var prepared = vm.PrepareRememberedWorkspace();
                Assert.Equal(local, Assert.Single(Assert.Single(prepared!.State.Panels).Tabs).Location?.Path);
                Assert.Equal(0, prepared.Skipped);
                reopened.SaveSettings();
            }
            using (var other = AppServices.CreateForPathsWithAdministratorRegistry(AppPaths.Resolve("Other", overrideRoot: root), store))
            {
                Assert.Equal("System", other.Settings.Theme);
                Assert.Null(other.AdministratorTransfers!.LoadWorkspace());
            }
            var withData = AppPaths.Resolve("Work", dataRoot: Path.Combine(root, "data"));
            using (var isolated = AppServices.CreateForPathsWithAdministratorRegistry(withData, store))
                Assert.Null(isolated.AdministratorTransfers);
            using var identity = WindowsIdentity.GetCurrent();
            string otherAccount = AdministratorTransferPreferences.ProfileScope("S-1-5-21-7-7-7-1001", paths.SettingsDirectory);
            Assert.NotEqual(AdministratorTransferPreferences.ProfileScope(identity.User!.Value, paths.SettingsDirectory), otherAccount);
            Assert.Null(store.Read(otherAccount + ".appearance", AppearanceTransferV1.MaxCharacters));
            AdministratorTransferPreferences.ForProfile(paths, store)!.Forget();
            using var reset = AppServices.CreateForPathsWithAdministratorRegistry(paths, store);
            Assert.Equal("System", reset.Settings.Theme);
            Assert.Null(reset.AdministratorTransfers!.LoadWorkspace());
            Assert.Equal(primary, File.ReadAllText(paths.SettingsFile));
            Assert.Equal(primary, File.ReadAllText(paths.SettingsFile + ".bak"));
            Assert.Equal(workspace, File.ReadAllText(paths.WorkspaceFile));
            Assert.Equal(workspace, File.ReadAllText(paths.WorkspaceFile + ".bak"));
        }
        finally
        {
            machine.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
            FileCat.Tests.TestCleanup.DeleteTree(root);
        }
    }

    [Fact]
    public void Genuine_high_token_refuses_an_existing_registry_key_with_inherited_permissions()
    {
        if (!OperatingSystem.IsWindows() || !WindowsTokenAuthority.IsHighIntegrity)
            Assert.Skip("Run in a high-integrity Windows test process.");
        string name = @"SOFTWARE\FileCat.TransferTests." + Guid.NewGuid().ToString("N");
        var store = new AdministratorTransferRegistry(name);
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        try
        {
            using (var key = machine.CreateSubKey(name)) key.SetValue("scope", "value");
            Assert.Throws<UnauthorizedAccessException>(() => store.Read("scope", 10));
            Assert.Throws<UnauthorizedAccessException>(() => store.Save("scope", "value", 10));
        }
        finally { machine.DeleteSubKeyTree(name, throwOnMissingSubKey: false); }
    }

    private static async Task ApplyAndRememberAsync(MainViewModel vm, Window window, string open, string field,
        string text, string apply)
    {
        var settings = SettingsDialog.ShowAsync(vm);
        Click(AppearanceTransferDialog.ActionLabel);
        Click(open);
        window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == field).Text = text;
        Click("Review");
        var remember = window.GetVisualDescendants().OfType<CheckBox>().Single(box =>
            box.IsEffectivelyVisible && Equals(box.Content, "Remember for future administrator launches"));
        Assert.False(remember.IsChecked);
        remember.IsChecked = true;
        Click(apply);
        await settings.WaitAsync(TimeSpan.FromSeconds(10));

        void Click(string label)
        {
            Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<Button>().Last(button => button.IsEffectivelyVisible && Equals(button.Content, label))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
    }
}
