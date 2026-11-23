using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Permissions;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>
/// G-UX: the inspector as sections (summary, ownership, access rules as a table with the chosen rule in full, inheritance),
/// actions beside what they change, account names looked up as it opens, and an administrator window's read-only view.
/// </summary>
public sealed class PermissionInspectorLayoutTests
{
    private static readonly PermissionAuthority Local = new(PermissionAuthorityKind.Local, "local", 0);

    private static Principal Sid(string sid) => new(PrincipalKind.WindowsSid, sid, "local");

    private static WindowsAce Ace(WindowsAceKind kind, string sid, uint mask, byte flags) =>
        new((byte)(kind == WindowsAceKind.Deny ? 1 : 0), flags, kind, mask, Sid(sid), []);

    private static PermissionSnapshot Folder(params WindowsAce[] entries)
    {
        var subject = new PermissionSubject(Location.FileSystem(@"C:\fixture"), @"C:\fixture\shared", @"C:\fixture\shared", Local,
            PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
        var now = DateTimeOffset.UtcNow;
        return PermissionSnapshots.Create(subject, PermissionModel.WindowsDescriptor, null,
            PermissionSections.Owner | PermissionSections.Group | PermissionSections.Access | PermissionSections.Inheritance, now, "test") with
        {
            Owner = new(PermissionReadStatus.Present, "test", now, Sid("S-1-5-21-1-2-3-1001")),
            Group = new(PermissionReadStatus.Present, "test", now, Sid("S-1-5-18")),
            Access = new(PermissionReadStatus.Present, "test", now, new PermissionAclValue(new WindowsDacl(WindowsDaclForm.Entries, 2, [.. entries], []))),
            Inheritance = new(PermissionReadStatus.Present, "test", now, new WindowsAclControl(0x8404, 1)),
        };
    }

    private static readonly WindowsAce[] Rules =
    [
        Ace(WindowsAceKind.Deny, "S-1-1-0", 0x00100116, 0x00),
        Ace(WindowsAceKind.Allow, "S-1-5-18", 0x001F01FF, 0x13),
        Ace(WindowsAceKind.Allow, "S-1-5-21-1-2-3-1002", 0x001301BF, 0x03),
    ];

    private static Window Show(Control view)
    {
        var window = new Window { Width = 900, Height = 1600, Content = new DialogCard("Permissions and ownership", view, null, 900) };
        window.Styles.Add(ThemeAudit.ProductStyles());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static string[] Texts(Control root) => root.GetVisualDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? "").ToArray();

    private static Button Button(Control root, string text) =>
        root.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == text);

    private static string SectionOf(Control control) => control.GetVisualAncestors().OfType<Border>()
        .First(border => border.Classes.Contains("permissionSection")).GetVisualDescendants().OfType<TextBlock>()
        .First(text => text.Classes.Contains("permissionHeading")).Text!;

    // sudo leaves macOS's TMPDIR unset, and /tmp is a link to /private/tmp.
    private static bool ReachedThroughLink(string path)
    {
        for (var folder = new DirectoryInfo(path); folder is not null; folder = folder.Parent)
            if (folder.LinkTarget is not null) return true;
        return false;
    }

    private static async Task Until(Func<bool> condition, CancellationToken ct)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20, ct);
        Assert.True(condition());
    }

    [AvaloniaFact]
    public void Windows_rules_read_as_a_table_with_the_chosen_rule_in_full()
    {
        var view = new PermissionInspectorView([("shared", Folder(Rules))], copy: _ => Task.CompletedTask);
        var window = Show(view);
        try
        {
            var list = view.GetVisualDescendants().OfType<ListBox>().Single(box => AutomationProperties.GetName(box) == "Access now");
            var rows = list.Items.OfType<PermissionInspectorView.AclRow>().ToArray();
            Assert.Equal(["Deny", "", "Write", "This folder only", "Explicit"], rows[0].Cells);
            Assert.Equal(["Allow", "", "Full control", "This folder, subfolders and files", "Inherited"], rows[1].Cells);
            Assert.Equal(["Allow", "", "Modify", "This folder, subfolders and files", "Explicit"], rows[2].Cells);
            // Each rule is spoken as one sentence, its account named where Windows has a well-known name.
            Assert.Equal("Deny Everyone: Write; This folder only; Explicit", AutomationProperties.GetName(list.ContainerFromIndex(0)!));
            Assert.Equal("Allow SYSTEM: Full control; This folder, subfolders and files; Inherited",
                AutomationProperties.GetName(list.ContainerFromIndex(1)!));
            Assert.Equal("Allow S-1-5-21-…-1002: Modify; This folder, subfolders and files; Explicit",
                AutomationProperties.GetName(list.ContainerFromIndex(2)!));
            // The rule chosen as the dialog opens is shown in full: account, plain language, every right, and the exact entry.
            Assert.Equal(0, list.SelectedIndex);
            var texts = Texts(view);
            Assert.Contains("3 rules", texts);
            Assert.Contains("Deny · Everyone", texts);
            Assert.Contains("Write · applies to this folder only · explicit", texts);
            Assert.Contains("Rights: Create files, Create folders, Write attributes, Write extended attributes, Synchronize", texts);
            Assert.Contains(texts, text => text.StartsWith("Exact entry: Deny · S-1-1-0 · mask 0x00100116 · explicit", StringComparison.Ordinal));
            list.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            texts = Texts(view);
            Assert.Contains("Allow · SYSTEM", texts);
            Assert.Contains("S-1-5-18", texts);
            // Ownership names the owner and keeps its exact ID beside the name; inheritance says what it means.
            Assert.Contains("S-1-5-21-…-1001", texts);
            Assert.Contains("S-1-5-21-1-2-3-1001", texts);
            Assert.Contains("Inheritance: enabled", texts);
            Assert.Contains("The parent folder's inheritable rules reach this folder as inherited entries.", texts);
            Assert.Equal("Ownership", SectionOf(Button(view, "Copy exact owner ID")));
            Assert.Equal("Access now", SectionOf(Button(view, "Copy exact account ID")));
            // Section titles are headings a screen reader can move between.
            Assert.All(view.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Classes.Contains("permissionHeading")),
                heading => Assert.Equal(2, AutomationProperties.GetHeadingLevel(heading)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Actions_sit_beside_what_they_change_and_end_the_dialog_only_when_available()
    {
        bool editable = true;
        var actions = new PermissionInspectorAction[]
        {
            new("Change owner…", "owner", PermissionInspectorArea.Ownership, _ => true),
            new("Edit access ACL…", "acl", PermissionInspectorArea.Access, _ => editable),
            new("Review Windows inheritance…", "windows-propagation", PermissionInspectorArea.Inheritance, _ => true),
            // No server section on a local folder: the action is still offered, at the end.
            new("Review SFTP permissions…", "sftp", PermissionInspectorArea.Server, _ => true),
        };
        var closer = new DialogCloser();
        object? result = null;
        closer.Attach(value => result = value);
        var view = new PermissionInspectorView([("shared", Folder(Rules))], copy: null, actions: actions, closer: closer);
        var window = Show(view);
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            Assert.Equal("Ownership", SectionOf(Button(view, "Change owner…")));
            Assert.Equal("Access now", SectionOf(Button(view, "Edit access ACL…")));
            Assert.Equal("Inheritance: enabled", SectionOf(Button(view, "Review Windows inheritance…")));
            Assert.Equal("Actions", SectionOf(Button(view, "Review SFTP permissions…")));
            var acl = Button(view, "Edit access ACL…");
            Assert.True(acl.IsEnabled);
            // Unavailable, the action does nothing even before the button shows it, and then shows it disabled.
            editable = false;
            acl.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Null(result);
            await Until(() => !acl.IsEnabled, ct);
            editable = true;
            await Until(() => acl.IsEnabled, ct);
            acl.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Assert.Equal("acl", result);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Ways_to_read_more_sit_under_unread_audit_rules_or_atop_an_unreadable_item()
    {
        var actions = new PermissionInspectorAction[]
        {
            new("Inspect with authorization…", "authorized-windows", PermissionInspectorArea.Summary, _ => true),
            new("Open folder as administrator…", "administrator-window", PermissionInspectorArea.Summary, _ => true),
        };
        var now = DateTimeOffset.UtcNow;
        var unread = Folder(Rules) with
        {
            Audit = new(PermissionReadStatus.Unsupported, "test", now,
                Reason: "Reading the Windows audit ACL requires an authorized FileCat session with SeSecurityPrivilege.")
        };
        var view = new PermissionInspectorView([("shared", unread)], copy: null, actions: actions, closer: new DialogCloser());
        var window = Show(view);
        try
        {
            Assert.Equal("Auditing", SectionOf(Button(view, "Inspect with authorization…")));
            Assert.Equal("Auditing", SectionOf(Button(view, "Open folder as administrator…")));
        }
        finally { window.Close(); }

        var denied = unread with { Access = new(PermissionReadStatus.Denied, "test", now, Reason: "Access was denied.") };
        view = new PermissionInspectorView([("shared", denied)], copy: null, actions: actions, closer: new DialogCloser());
        window = Show(view);
        try
        {
            var note = Button(view, "Open folder as administrator…").GetVisualAncestors().OfType<Border>().First();
            Assert.Contains("permissionNote", note.Classes);
            Assert.Contains("warning", note.Classes);
            Assert.Contains("This account cannot read part of this item's security information.", Texts(view));
            Assert.Same(note, Button(view, "Inspect with authorization…").GetVisualAncestors().OfType<Border>().First());
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The inspector opens the item's folder in an administrator window through the usual confirmation, where every
    /// section is read (read-only); the ordinary window stays as it was.
    /// </summary>
    [AvaloniaFact]
    public async Task An_ordinary_Windows_window_offers_the_items_folder_as_administrator()
    {
        if (!OperatingSystem.IsWindows() || MainViewModel.PermissionChangeRefusal() is not null)
            Assert.Skip("Requires an ordinary Windows FileCat process.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
            File.WriteAllText(Path.Combine(folder, "witness.txt"), "administrator folder"));
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var offered = new TaskCompletionSource<StartupOptions>(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.LaunchAdministratorWindow = (start, _) => offered.TrySetResult(StartupOptions.Parse(start.ArgumentList.ToArray()));
            string folder = Path.Combine(root, "files");
            var tab = vm.ActiveTab!;
            tab.Navigate(Location.FileSystem(folder));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("witness.txt"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => window.GetVisualDescendants().OfType<PermissionInspectorView>().Any(), ct);
            var view = window.GetVisualDescendants().OfType<PermissionInspectorView>().Single();
            var open = Button(view, "Open folder as administrator…");
            Assert.True(open.IsEnabled);
            open.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await Until(() => window.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string == "Open as administrator"), ct);
            window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Open as administrator")
                .RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            var options = await offered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.True(options.NewInstance);
            Assert.Equal((folder, AdministratorWindow.LocalVolume(folder)!), options.AdministratorFolder);
            Assert.Equal(Location.FileSystem(folder), tab.Location);
            Assert.Empty(services.Jobs.Jobs);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private sealed class FixtureAccounts : IOwnership
    {
        public bool IsSupported => true;
        public OwnerRead ReadOwner(string path) => new(OwnerReadStatus.Unsupported);
        public OwnerRead ReadGroup(string path) => new(OwnerReadStatus.Unsupported);
        public PrincipalName LookUp(Principal principal) => principal.Id switch
        {
            "S-1-5-21-1-2-3-1001" => new(NameStatus.Resolved, @"HOST\alice", "User account"),
            "S-1-5-21-1-2-3-1002" => new(NameStatus.NotFound),
            _ => new(NameStatus.Unavailable, Reason: "The fixture names only two accounts.")
        };
    }

    [AvaloniaFact]
    public async Task Account_names_are_looked_up_as_the_inspector_opens()
    {
        using var names = new PermissionPrincipalNames(new FixtureAccounts());
        var view = new PermissionInspectorView([("shared", Folder(Rules))], copy: null, principalNames: names);
        var window = Show(view);
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            // The owner's name replaces its ID, which stays beside it with the account's full name and type.
            await Until(() => Texts(view).Contains("alice"), ct);
            Assert.Contains(@"HOST\alice · User account · S-1-5-21-1-2-3-1001", Texts(view));
            var list = view.GetVisualDescendants().OfType<ListBox>().Single(box => AutomationProperties.GetName(box) == "Access now");
            // An account without a name keeps its ID; well-known accounts keep their names when the lookup has none.
            list.SelectedIndex = 2;
            await Until(() => Texts(view).Contains("No account name was found for this ID · S-1-5-21-1-2-3-1002"), ct);
            Assert.Equal("Allow S-1-5-21-…-1002: Modify; This folder, subfolders and files; Explicit",
                AutomationProperties.GetName(list.ContainerFromIndex(2)!));
            Assert.Equal("Allow SYSTEM: Full control; This folder, subfolders and files; Inherited",
                AutomationProperties.GetName(list.ContainerFromIndex(1)!));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_POSIX_ACL_names_each_entry_and_its_effective_rights()
    {
        var subject = new PermissionSubject(Location.FileSystem("/fixture"), "/fixture/report.txt", "/fixture/report.txt", Local,
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        var now = DateTimeOffset.UtcNow;
        var acl = new PosixAcl([new(PosixAclTag.Owner, 6, null), new(PosixAclTag.NamedUser, 6, new(PrincipalKind.UnixUser, "1001", "local")),
            new(PosixAclTag.OwningGroup, 4, null), new(PosixAclTag.Mask, 4, null), new(PosixAclTag.Other, 0, null)], []);
        var snapshot = PermissionSnapshots.Create(subject, PermissionModel.PosixAcl, null,
            PermissionSections.Owner | PermissionSections.Group | PermissionSections.Mode | PermissionSections.Access, now, "test") with
        {
            Owner = new(PermissionReadStatus.Present, "test", now, new Principal(PrincipalKind.UnixUser, "1000", "local")),
            Group = new(PermissionReadStatus.Present, "test", now, new Principal(PrincipalKind.UnixGroup, "1000", "local")),
            Mode = new(PermissionReadStatus.Present, "test", now, (ushort)0x1a0),
            Access = new(PermissionReadStatus.Present, "test", now, new PermissionAclValue(acl)),
        };
        var view = new PermissionInspectorView([("report.txt", snapshot)], copy: null);
        var window = Show(view);
        try
        {
            var list = view.GetVisualDescendants().OfType<ListBox>().Single(box => AutomationProperties.GetName(box) == "Access now");
            var rows = list.Items.OfType<PermissionInspectorView.AclRow>().ToArray();
            Assert.Equal(["Owner", "", "rw-", "rw-"], rows[0].Cells);
            Assert.Equal(["User", "", "rw-", "r--"], rows[1].Cells);
            Assert.Equal("User UID 1001: rw-, effective r--", AutomationProperties.GetName(list.ContainerFromIndex(1)!));
            Assert.Equal("Others: ---", AutomationProperties.GetName(list.ContainerFromIndex(4)!));
            var texts = Texts(view);
            Assert.Contains("Mode: 640 · rw-r-----", texts);
            Assert.Contains("Read, write", texts);
            Assert.Contains("5 entries", texts);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_window_wide_note_stands_even_for_an_item_that_could_not_be_read()
    {
        var subject = new PermissionSubject(Location.FileSystem("/fixture"), "/fixture/item", "/fixture/item", Local,
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        using var inspection = PermissionSnapshots.Unavailable(subject, PermissionSections.Ordinary, PermissionReadStatus.Failed,
            "Permission metadata is unavailable.");
        const string note = "Read-only while FileCat runs as root. Change permissions from an ordinary FileCat window.";
        var view = new PermissionInspectorView([("item", inspection.Snapshot)], copy: null, notice: note);
        string?[] texts = view.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
        Assert.Contains(note, texts);
        Assert.Contains("Permission metadata is unavailable.", texts);
        Assert.DoesNotContain("This view shows the observed permissions.", texts);
    }

    /// <summary>
    /// G-PRIV: an administrator window inspects read-only. On Windows x64 its own backup and security privileges complete the
    /// read, so the audit rules appear, and no change is offered. Run from an elevated process; the ordinary run skips.
    /// </summary>
    [AvaloniaFact]
    public async Task An_administrator_window_inspects_read_only()
    {
        string readOnly = MainViewModel.PermissionChangeRefusal() ?? "";
        if (readOnly.Length == 0) Assert.Skip("Requires an administrator (elevated) or root FileCat process.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
            File.WriteAllText(Path.Combine(folder, "witness.txt"), "read-only"));
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            tab.Navigate(Location.FileSystem(Path.Combine(root, "files")));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("witness.txt"), ct);
            Assert.True(vm.GetAvailability(CommandIds.PermissionsOwnership).Enabled);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => window.GetVisualDescendants().OfType<PermissionInspectorView>().Any(), ct);
            var view = window.GetVisualDescendants().OfType<PermissionInspectorView>().Single();
            Dispatcher.UIThread.RunJobs();
            // The note holds whatever the read found (a fixture on a link-reached path is not bound, for one); a bound item
            // shows its sections.
            Assert.Contains(readOnly, Texts(view));
            if (!ReachedThroughLink(root)) Assert.Contains("Ownership", Texts(view));
            foreach (string change in new[] { "Change owner…", "Edit access ACL…", "Edit mode…", "Change group…", "Inspect with authorization…" })
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), button => button.Content as string == change);
            if (OperatingSystem.IsWindows() && FileCat.Platform.Windows.Elevation.ElevationBroker.IsNativeFileSecurityHost)
            {
                // Only the window's own security privilege reads audit rules: their section shows that read.
                Assert.Contains("Auditing", Texts(view));
                Assert.DoesNotContain(Texts(view), text => text.StartsWith("This administrator window could not", StringComparison.Ordinal));
            }
            Assert.Empty(services.Jobs.Jobs);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
