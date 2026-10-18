using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class WorkspaceRestoreTests
{
    private static WorkspaceState FourPanelWorkspace(Location folder)
    {
        string[] ids = ["panel-a", "panel-b", "panel-c", "panel-d"];
        var panels = ids.Select((id, i) => new PanelState
        {
            Id = id,
            TargetPanelId = ids[(i + 1) % ids.Length],
            ActiveTab = i + 2,
            Tabs = Enumerable.Range(0, 10).Select(j => new TabState
            {
                Location = folder,
                Locked = j % 2 == 0,
                ReturnToRoot = j % 2 == 0,
                LockedRoot = Location.FileSystem(Path.Combine(folder.Path, $"root-{i}-{j}")),
            }).ToList(),
        }).ToList();
        return new WorkspaceState
        {
            Name = "Large arrangement",
            Panels = panels,
            ActivePanelId = ids[2],
            Tree = PanelLayoutNode.Split(false,
            [
                PanelLayoutNode.Split(true, [PanelLayoutNode.Panel(ids[0], 2), PanelLayoutNode.Panel(ids[1], 1)], 3),
                PanelLayoutNode.Split(true, [PanelLayoutNode.Panel(ids[2], 1), PanelLayoutNode.Panel(ids[3], 2)], 2),
            ]),
        };
    }

    [AvaloniaFact]
    public async Task Forty_tabs_restore_in_their_panels_with_layout_targets_and_active_state()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var folder = Location.FileSystem(Path.Combine(root, "files"));
            vm.Workspace.LoadState(FourPanelWorkspace(folder));
            var expected = vm.Workspace.ToState(null);
            vm.Workspace.RememberClosed(new TabState { Location = folder });
            int recent = vm.Workspace.RecentlyClosed.Count;
            Assert.False(vm.GetAvailability(CommandIds.RestorePreviousWorkspace).Enabled);

            var alternate = new WorkspaceState
            {
                Name = "Alternate",
                Panels = [new PanelState { Id = "other-a", Tabs = [new TabState { Location = folder }] },
                          new PanelState { Id = "other-b", Tabs = [new TabState { Location = folder }] }],
            };
            JsonFileStore.Save(Path.Combine(services.Paths.WorkspacesDirectory, "Alternate.json"), alternate,
                StateJsonContext.Default.WorkspaceState);
            await vm.OpenNamedWorkspaceAsync("Alternate");

            Assert.Equal(2, vm.Workspace.Panels.Count);
            Assert.True(vm.GetAvailability(CommandIds.RestorePreviousWorkspace).Enabled);
            Assert.True(vm.NotificationRestoreWorkspace);
            Assert.Equal(recent, vm.Workspace.RecentlyClosed.Count);
            var action = window.FindControl<Button>("RestoreWorkspaceNotificationButton")!;
            Assert.True(action.IsVisible);
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 100 && vm.Workspace.Panels.Count != 4; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);

            var restored = vm.Workspace.ToState(null);
            Assert.Equal(4, restored.Panels.Count);
            Assert.Equal(40, restored.Panels.Sum(p => p.Tabs.Count));
            Assert.Equal(expected.ActivePanelId, restored.ActivePanelId);
            Assert.Equal(expected.Tree!.ToString(), restored.Tree!.ToString());
            foreach (var (before, after) in expected.Panels.Zip(restored.Panels))
            {
                Assert.Equal(before.Id, after.Id);
                Assert.Equal(before.TargetPanelId, after.TargetPanelId);
                Assert.Equal(before.ActiveTab, after.ActiveTab);
                Assert.Equal(before.Tabs.Select(t => t.LockedRoot), after.Tabs.Select(t => t.LockedRoot));
                Assert.Equal(before.Tabs.Select(t => t.Locked), after.Tabs.Select(t => t.Locked));
                Assert.Equal(before.Tabs.Select(t => t.ReturnToRoot), after.Tabs.Select(t => t.ReturnToRoot));
            }
            var a = PanelLayout.Arrange(expected.Tree);
            var b = PanelLayout.Arrange(restored.Tree);
            foreach (string id in a.Keys)
            {
                Assert.Equal(a[id].X, b[id].X, 6);
                Assert.Equal(a[id].Y, b[id].Y, 6);
                Assert.Equal(a[id].Width, b[id].Width, 6);
                Assert.Equal(a[id].Height, b[id].Height, 6);
            }
            Assert.Equal(recent, vm.Workspace.RecentlyClosed.Count);

            await vm.RestorePreviousWorkspaceAsync();
            Assert.Equal(2, vm.Workspace.Panels.Count);
            Assert.Equal(recent, vm.Workspace.RecentlyClosed.Count);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [Fact]
    public void Unrestorable_tabs_are_dropped_without_shifting_the_active_tab_to_an_unrelated_item()
    {
        var folder = Location.FileSystem(Path.GetTempPath());
        var state = new WorkspaceState
        {
            Panels = [new PanelState
            {
                ActiveTab = 2,
                Tabs = [new TabState { Location = folder, FocusName = "first" },
                        new TabState { Location = null, FocusName = "transient" },
                        new TabState { Location = folder, FocusName = "active" }],
            }],
        };

        Assert.Equal(1, FileCat.App.ViewModels.MainViewModel.DropUnrestorableTabs(state));
        Assert.Equal(2, state.Panels[0].Tabs.Count);
        Assert.Equal(1, state.Panels[0].ActiveTab);
        Assert.Equal("active", state.Panels[0].Tabs[1].FocusName);
    }

    [AvaloniaFact]
    public async Task Transient_view_requires_disclosure_before_switch_and_is_never_restarted_on_restore()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var transient = new Location(Schemes.ResultSet, "unsaved-transient");
            vm.ActiveTab!.Navigate(transient);
            Assert.Null(vm.ActiveTab.ToState().Location);
            var folder = Location.FileSystem(Path.Combine(root, "files"));
            JsonFileStore.Save(Path.Combine(services.Paths.WorkspacesDirectory, "Alternate.json"),
                new WorkspaceState { Name = "Alternate", Panels =
                    [new PanelState { Id = "a", Tabs = [new TabState { Location = folder }] },
                     new PanelState { Id = "b", Tabs = [new TabState { Location = folder }] }] },
                StateJsonContext.Default.WorkspaceState);
            var dialogs = new RestoreDialogs();
            vm.Dialogs = dialogs;

            await vm.OpenNamedWorkspaceAsync("Alternate");
            Assert.Equal(1, dialogs.Confirmations);
            Assert.Contains("1 temporary view cannot be restored", dialogs.Message);
            Assert.Equal(transient, vm.ActiveTab!.Location);
            Assert.False(vm.CanRestorePreviousWorkspace);

            dialogs.Approve = true;
            await vm.OpenNamedWorkspaceAsync("Alternate");
            Assert.True(vm.CanRestorePreviousWorkspace);
            await vm.RestorePreviousWorkspaceAsync();
            Assert.DoesNotContain(vm.Workspace.Panels.SelectMany(p => p.Tabs),
                t => t.Location?.Scheme == Schemes.ResultSet);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Oversized_switch_target_is_refused_before_current_tabs_are_disposed()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var folder = Location.FileSystem(Path.Combine(root, "files"));
            var original = vm.Workspace.ToState(null);
            var many = Enumerable.Range(0, 513).Select(_ => new TabState { Location = folder }).ToList();
            JsonFileStore.Save(Path.Combine(services.Paths.WorkspacesDirectory, "Too many.json"),
                new WorkspaceState { Name = "Too many", Panels = [new PanelState { Tabs = many }] },
                StateJsonContext.Default.WorkspaceState);

            await vm.OpenNamedWorkspaceAsync("Too many");

            Assert.False(vm.CanRestorePreviousWorkspace);
            Assert.Equal(original.Panels.Select(p => p.Id), vm.Workspace.Panels.Select(p => p.Id));
            Assert.All(vm.Workspace.Panels.SelectMany(p => p.Tabs), tab => Assert.False(tab.Listing.IsDisposed));
            Assert.Contains("at most 512 tabs", vm.Notification);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private sealed class RestoreDialogs : IDialogService
    {
        public bool Approve { get; set; }
        public int Confirmations { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public bool IsOpen => false;
        public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool danger = false, string cancelText = "Cancel")
        {
            Confirmations++;
            Message = message;
            Assert.True(danger);
            return Task.FromResult(Approve);
        }
        public Task<PromptResult?> PromptAsync(PromptOptions options) => throw new NotSupportedException();
        public Task AlertAsync(string title, string message) => throw new NotSupportedException();
        public Task<ChoiceResult> ChooseAsync(ChoiceOptions options) => throw new NotSupportedException();
        public Task<KeyboardReferenceChoice?> KeyboardReferenceAsync(IReadOnlyList<KeyboardHelpEntry> commands, string? selectedId = null, bool remapOnly = false) => throw new NotSupportedException();
        public Task<object?> ShowCustomAsync(string title, Control content, IReadOnlyList<DialogButton> buttons, Control? initialFocus = null,
            Func<bool>? canConfirm = null, DialogCloser? closer = null) => throw new NotSupportedException();
    }
}
