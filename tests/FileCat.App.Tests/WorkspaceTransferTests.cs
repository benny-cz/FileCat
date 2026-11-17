using System.Security.Cryptography;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class WorkspaceTransferTests
{
    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\?\C:\Windows")]
    [InlineData(@"C:\folder\..\Windows")]
    [InlineData(@"C:relative")]
    [InlineData(@"C:\folder\NUL.txt")]
    [InlineData(@"C:\folder:file")]
    [InlineData(@"C:\folder.")]
    public void Copy_format_rejects_paths_outside_the_drive_letter_folder_syntax(string path)
    {
        var transfer = new WorkspaceTransfer([new([path], 0, null)], new(0, false, 1, []), 0);
        Assert.Throws<IOException>(() => transfer.Encode());
    }

    [Fact]
    public void Clipboard_parser_refuses_excess_fields_duplicate_keys_and_inconsistent_layouts()
    {
        var transfer = new WorkspaceTransfer([new([@"C:\Data"], 0, null)], new(0, false, 1, []), 0);
        string text = transfer.Encode();
        Assert.NotNull(WorkspaceTransfer.Decode(text));
        foreach (string bad in new[]
        {
            text.Replace("Workspace-1", "Workspace-2"),
            text.Replace("\"ActivePanel\":0", "\"ActivePanel\":0,\"ActivePanel\":0"),
            text.Replace("\"ActivePanel\":0", "\"ActivePanel\":0,\"WorkspaceFile\":\"read this\""),
            text.Replace("\"Panel\":0", "\"Panel\":1"),
            text.Replace("\"Size\":1", "\"Size\":1e300"),
            text + new string(' ', WorkspaceTransfer.MaxCharacters),
        })
            Assert.Null(WorkspaceTransfer.Decode(bad));
    }

    [AvaloniaFact]
    public async Task Normal_copy_uses_current_layout_and_local_tabs_without_saving()
    {
        if (!OperatingSystem.IsWindows() || WindowsTokenAuthority.IsHighIntegrity) return;
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var paths = services.Paths;
            string firstFolder = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
            string secondFolder = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
            SeedFiles(paths);
            var before = Hashes(paths);
            vm.Workspace.LoadState(ThreePanels(firstFolder, secondFolder));
            var settings = SettingsDialog.ShowAsync(vm);
            Click(window, AppearanceTransferDialog.ActionLabel);
            Click(window, "Copy current workspace for administrator");
            string copied = Field(window, "Workspace text to copy").Text!;
            var packet = Assert.IsType<WorkspaceTransfer>(WorkspaceTransfer.Decode(copied));
            Assert.Equal(3, packet.Panels.Length);
            Assert.Equal([firstFolder, secondFolder], packet.Panels[0].Folders);
            Assert.Equal(1, packet.Panels[0].ActiveTab);
            Assert.Empty(packet.Panels[1].Folders);
            Assert.Equal(1, packet.ActivePanel);
            Assert.True(packet.Layout.Stacked);
            Assert.False(packet.Layout.Children[0].Stacked);
            Assert.Equal(3, packet.Layout.Children[0].Size);
            Assert.Equal(2, packet.Panels[0].Target);
            Assert.True(Shows(window, "Use copied workspace for this session"));

            Click(window, "Copy saved appearance for administrator");
            Assert.True(Field(window, "Appearance text to copy").IsEffectivelyVisible);
            double size = vm.Workspace.LayoutTree.Size;
            try
            {
                vm.Workspace.LayoutTree.Size = 0;
                Click(window, "Copy current workspace for administrator");
                Assert.False(Field(window, "Appearance text to copy").IsEffectivelyVisible);
                Assert.True(Shows(window, "Could not copy the workspace."));
            }
            finally { vm.Workspace.LayoutTree.Size = size; }
            Click(window, "Close");
            Click(window, "Cancel");
            await settings.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(before, Hashes(paths));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Genuine_high_token_workspace_apply_and_undo_leave_normal_files_byte_identical()
    {
        if (!OperatingSystem.IsWindows() || !WindowsTokenAuthority.IsHighIntegrity)
            Assert.Skip("Run in a high-integrity Windows test process.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var paths = services.Paths;
            SeedFiles(paths);
            var before = Hashes(paths);
            var original = vm.Workspace.Panels.Select(p => p.Id).ToArray();
            string available = Directory.CreateDirectory(Path.Combine(root, "available")).FullName;
            string another = Directory.CreateDirectory(Path.Combine(root, "another")).FullName;
            string missing = Path.Combine(root, "missing");
            string junctionTarget = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            string link = Path.Combine(root, "linked");
            Directory.CreateSymbolicLink(link, junctionTarget);
            try
            {
                var packet = new WorkspaceTransfer(
                    [new([available, another], 1, 2), new([missing, link], 0, null), new([available], 0, null)],
                    new(null, true, 1, [new(null, false, 3, [new(0, false, 1, []), new(1, false, 2, [])]), new(2, false, 1, [])]), 1);
                var settings = SettingsDialog.ShowAsync(vm);
                Click(window, AppearanceTransferDialog.ActionLabel);
                Click(window, "Use copied workspace for this session");
                var input = Field(window, "Workspace text");
                input.Text = AppearanceTransferV1.Export(new AppSettings { Theme = "Psychedelic" }, ThemeManager.Names);
                Click(window, "Review");
                Assert.True(Shows(window, "This is appearance text."));
                Assert.True(Shows(window, "Copy current workspace for administrator"));
                Assert.Equal(original, vm.Workspace.Panels.Select(p => p.Id).ToArray());
                input.Text = packet.Encode();
                Click(window, "Review");
                Assert.Equal(original, vm.Workspace.Panels.Select(p => p.Id).ToArray());
                Click(window, "Apply workspace for this session");
                await settings.WaitAsync(TimeSpan.FromSeconds(10));

                var panels = vm.Workspace.Panels;
                Assert.Equal(3, panels.Count);
                Assert.Equal([available, another], panels[0].Tabs.Select(t => t.Location?.Path));
                Assert.Equal(another, panels[0].ActiveTab?.Location?.Path);
                Assert.Equal(panels[2].Id, panels[0].TargetPanelId);
                Assert.Same(panels[1], vm.Workspace.ActivePanel);
                Assert.Equal(ViewModels.WorkspaceViewModel.DefaultLocation(), Assert.Single(panels[1].Tabs).Location);
                Assert.True(vm.Workspace.LayoutTree.Stacked);
                Assert.Equal(3, vm.Workspace.LayoutTree.Children[0].Size);
                Assert.Empty(original.Intersect(panels.Select(p => p.Id)));
                vm.SaveWorkspace(null);
                Assert.Equal(before, Hashes(paths));
                Assert.Null(vm.LoadSavedWorkspace());
                await vm.RestorePreviousWorkspaceAsync();
                Assert.Equal(original, vm.Workspace.Panels.Select(p => p.Id).ToArray());
                Assert.Equal(before, Hashes(paths));
            }
            finally { Directory.Delete(link); }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static WorkspaceState ThreePanels(string first, string second)
    {
        var a = new PanelState
        {
            ActiveTab = 2,
            Tabs = [new() { Location = Location.FileSystem(first) }, new() { Location = new(Schemes.Computer, "") }, new() { Location = Location.FileSystem(second) }],
        };
        var b = new PanelState { Tabs = [new() { Location = new(Schemes.Computer, "") }] };
        var c = new PanelState { Tabs = [new() { Location = Location.FileSystem(second) }] };
        a.TargetPanelId = c.Id;
        return new WorkspaceState
        {
            Panels = [a, b, c], ActivePanelId = b.Id,
            Tree = PanelLayoutNode.Split(true,
                [PanelLayoutNode.Split(false, [PanelLayoutNode.Panel(a.Id), PanelLayoutNode.Panel(b.Id, 2)], 3), PanelLayoutNode.Panel(c.Id)]),
        };
    }

    private static string[] StateFiles(AppPaths paths) =>
        [paths.SettingsFile, paths.SettingsFile + ".bak", paths.WorkspaceFile, paths.WorkspaceFile + ".bak"];

    private static void SeedFiles(AppPaths paths)
    {
        foreach (string path in StateFiles(paths)) File.WriteAllText(path, """{"SchemaVersion":1}""");
    }

    private static string[] Hashes(AppPaths paths) =>
        StateFiles(paths).Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

    private static TextBox Field(Window window, string name) => window.GetVisualDescendants().OfType<TextBox>()
        .Single(box => AutomationProperties.GetName(box) == name);

    private static bool Shows(Window window, string text) => window.GetVisualDescendants().OfType<TextBlock>()
        .Any(label => label.IsEffectivelyVisible && label.Text?.Contains(text, StringComparison.Ordinal) == true);

    private static void Click(Window window, string text)
    {
        Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<Button>()
            .Last(button => button.IsEffectivelyVisible && Equals(button.Content, text))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
}
