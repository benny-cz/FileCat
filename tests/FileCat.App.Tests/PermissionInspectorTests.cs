using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Permissions;
using FileCat.Core.State;
using FileCat.Platform.Windows.Permissions;
using FileCat.Remote.Sftp;
using FileCat.Remote.Tests;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class PermissionInspectorTests
{
    [AvaloniaFact]
    public async Task Mac_live_provider_permissions_panel_shows_dataless_metadata_without_downloading()
    {
        string? provider = Environment.GetEnvironmentVariable("FILECAT_TEST_MAC_PROVIDER_ROOT");
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess || string.IsNullOrWhiteSpace(provider))
            Assert.Skip("Requires an ordinary macOS worker and the disposable File Provider fixture.");

        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(
            permissionService: new DarwinPermissionService(qualifyAccessChecks: true));
        try
        {
            var tab = vm.ActiveTab!;
            var ct = TestContext.Current.CancellationToken;
            foreach (string path in new[] { Path.Join(provider, "dataless-file.bin"), Path.Join(provider, "dataless-folder", "child.bin") })
            {
                var location = Location.FileSystem(Path.GetDirectoryName(path)!);
                tab.Navigate(location);
                await Until(() => tab.Location == location && tab.Listing.State == ListingState.Complete && tab.Listing.FocusName(Path.GetFileName(path)), ct);
                vm.Execute(CommandIds.PermissionsOwnership);
                await Until(() => TextPresent(window, "Dataless item. Showing metadata without downloading content."), ct);
                Assert.Null(FindButton(window, "Edit mode…"));
                Assert.Null(FindButton(window, "Check current process access…"));
                Assert.Empty(services.Jobs.Jobs);
                Click(FindButton(window, "Close")!);
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData("fat-ro", "Mount-derived permissions", true)]
    [InlineData("smb", "Permissions presented by the mounted client", true)]
    [InlineData("gvfs", "Permissions presented by the mounted client", false)]
    [InlineData("nfs", "Permissions presented by the mounted client", true)]
    [InlineData("zfs-posix", "Permissions reported by the filesystem", false)]
    [InlineData("zfs-off", "Permissions reported by the filesystem", false)]
    [InlineData("zfs-nfsv4", "Permissions reported by the filesystem", false)]
    public async Task Linux_panel_labels_mount_presented_values_and_offers_no_permissions_write(string fixture, string label, bool readOnly)
    {
        string? fixtures = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_MOUNT_ROOT");
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(fixtures) || Environment.IsPrivilegedProcess)
            Assert.Skip("Requires a disposable native mount and an ordinary Linux process.");
        string? folder = fixture == "gvfs" ? Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_GVFS_ROOT") :
            Path.Combine(fixtures, fixture);
        if (string.IsNullOrEmpty(folder) || !File.Exists(Path.Combine(folder, "witness.txt"))) Assert.Skip("The mount fixture is unavailable.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(
            permissionService: new LinuxPermissionService(qualifyAccessChecks: true, qualifyMountProjections: true));
        try
        {
            var tab = vm.ActiveTab!;
            var cancellation = TestContext.Current.CancellationToken;
            tab.Navigate(Location.FileSystem(folder));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("witness.txt"), cancellation);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => TextPresent(window, label), cancellation);
            Assert.True(TextPresent(window, "Presented owner"));
            Assert.True(TextPresent(window, "Presented mode"));
            Assert.Equal(readOnly, TextPresent(window, "mount is read-only"));
            if (fixture is "smb" or "gvfs") Assert.True(TextPresent(window, "//127.0.0.1/permissions"));
            if (fixture is "smb" or "gvfs" or "nfs")
            {
                Assert.True(TextPresent(window, "Read from the mounted client at"));
                Assert.True(TextPresent(window, "Server reachability and freshness are unverified."));
            }
            if (fixture == "nfs")
            {
                Assert.True(TextPresent(window, "127.0.0.1:/permissions"));
                Assert.True(TextPresent(window, "NFS 4.1"));
                Assert.True(TextPresent(window, "AUTH_SYS"));
                Assert.True(TextPresent(window, "NFSv4 ACL"));
            }
            if (fixture.StartsWith("zfs-", StringComparison.Ordinal))
            {
                Assert.True(TextPresent(window, "/" + fixture[4..]));
                Assert.True(TextPresent(window, fixture == "zfs-posix" ? "POSIX ACLs" : "ACLs disabled"));
                Assert.True(TextPresent(window, "inheritance policies are unverified"));
            }
            Assert.Null(FindButton(window, "Edit mode…"));
            Assert.Null(FindButton(window, "Edit access ACL…"));
            Assert.Null(FindButton(window, "Check current process access…"));
            Assert.Null(FindButton(window, "Inspect with authorization…"));
            Assert.Empty(services.Jobs.Jobs);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(PermissionAuthorityKind.MountedServer, "mounted client")]
    [InlineData(PermissionAuthorityKind.Local, "filesystem")]
    public void An_unavailable_mount_shows_its_last_observation_when_known(PermissionAuthorityKind authority, string source)
    {
        var observed = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var subject = new PermissionSubject(Location.FileSystem("/mount"), "/mount/item", "/mount/item",
            new(authority, "fixture", 1), PermissionObjectKind.File, PermissionLinkPolicy.NoFollow)
            { LastMountObservation = observed };
        using var inspection = PermissionSnapshots.Unavailable(subject, PermissionSections.Ordinary, PermissionReadStatus.Failed,
            "Permission metadata is unavailable.");
        var view = new PermissionInspectorView([("item", inspection.Snapshot)], copy: null);
        string?[] labels = view.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();
        Assert.Contains("Permission metadata is unavailable.", labels);
        Assert.Contains($"Last observation from the {source} at {observed.ToLocalTime():g}.", labels);
        Assert.DoesNotContain(labels, label => label?.StartsWith("Presented mode", StringComparison.Ordinal) == true);
    }

    [AvaloniaFact]
    public void Copy_buttons_tell_a_screen_reader_which_value_they_copy()
    {
        // A keyboard reaches the copy buttons, not the plain text beside them: each carries its value as help text.
        var subject = new PermissionSubject(Location.FileSystem("/fixture"), "/fixture/report.txt", "/fixture/report.txt",
            new(PermissionAuthorityKind.Local, "local", 0), PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        using var inspection = PermissionSnapshots.Unavailable(subject, PermissionSections.Ordinary, PermissionReadStatus.Failed,
            "Permission metadata is unavailable.");
        var view = new PermissionInspectorView([("report.txt", inspection.Snapshot)], copy: _ => Task.CompletedTask);
        var copyPath = view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Copy exact path"));
        Assert.Equal("/fixture/report.txt", Avalonia.Automation.AutomationProperties.GetHelpText(copyPath));
        // The value alone: the name says what it is, and Orca drops a description that repeats the name.
        var owned = PermissionSnapshots.Create(subject, PermissionModel.PosixAcl, null, PermissionSections.Owner | PermissionSections.Group,
            DateTimeOffset.UtcNow, "test") with
        {
            Owner = new(PermissionReadStatus.Present, "test", DateTimeOffset.UtcNow, new Principal(PrincipalKind.UnixUser, "1000", "local")),
            Group = new(PermissionReadStatus.Present, "test", DateTimeOffset.UtcNow, new Principal(PrincipalKind.UnixGroup, "1000", "local"))
        };
        var principals = new PermissionInspectorView([("report.txt", owned)], copy: _ => Task.CompletedTask);
        // Every ACL row exists for a screen reader to count, not only the rows in view.
        var acl = PermissionSnapshots.Create(subject, PermissionModel.WindowsDescriptor, null, PermissionSections.Access, DateTimeOffset.UtcNow,
            "test") with { Access = new(PermissionReadStatus.Present, "test", DateTimeOffset.UtcNow, new PermissionAclValue(ManyEntryDacl(40))) };
        var aclView = new PermissionInspectorView([("report.txt", acl)], copy: null);
        var rows = aclView.GetVisualDescendants().OfType<ListBox>().Single(list => Avalonia.Automation.AutomationProperties.GetName(list) == "Access now");
        Assert.IsType<StackPanel>(rows.ItemsPanelRoot ?? rows.ItemsPanel.Build());
        foreach (string name in new[] { "Copy exact owner ID", "Copy exact primary group ID" })
            Assert.Equal("1000", Avalonia.Automation.AutomationProperties.GetHelpText(
                principals.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, name))));
    }

    private static WindowsDacl ManyEntryDacl(int count) => new(WindowsDaclForm.Entries, 2, [.. Enumerable.Range(0, count).Select(index =>
        WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Allow, new(PrincipalKind.WindowsSid, $"S-1-5-21-1-2-3-{1000 + index}", "local"), 0x120089))],
        []);

    [AvaloniaTheory]
    [InlineData((ushort)0x8404, false)]
    [InlineData((ushort)0x8004, true)]
    [InlineData((ushort)0x9004, true)]
    public void Windows_inspector_marks_a_DACL_stored_without_automatic_inheritance(ushort flags, bool legacy)
    {
        var subject = new PermissionSubject(Location.FileSystem(@"C:\fixture"), @"C:\fixture\legacy.txt", @"C:\fixture\legacy.txt",
            new(PermissionAuthorityKind.Local, "local", 0), PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        var now = DateTimeOffset.UtcNow;
        var snapshot = PermissionSnapshots.Create(subject, PermissionModel.WindowsDescriptor, null, PermissionSections.Inheritance, now, "test") with
        {
            Inheritance = new(PermissionReadStatus.Present, "test", now, new WindowsAclControl(flags, 1))
        };
        var view = new PermissionInspectorView([("legacy.txt", snapshot)], copy: null);
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text ?? "").ToArray();
        string state = (flags & 0x1000) != 0 ? "Inheritance: protected" : "Inheritance: enabled";
        Assert.Contains(legacy ? state + ", legacy form" : state, texts);
        Assert.Equal(legacy, texts.Any(text => text.StartsWith("Stored without", StringComparison.Ordinal)));
        Assert.Equal(legacy && (flags & 0x1000) == 0, texts.Any(text => text.Contains("Explorer shows the entries", StringComparison.Ordinal)));
    }

    private sealed class UnavailablePermissions : IPermissionService
    {
        internal readonly List<PermissionSubject> Subjects = [];

        public PermissionInspection Inspect(PermissionSubject subject, PermissionSections sections, CancellationToken cancellationToken)
        {
            lock (Subjects) Subjects.Add(subject);
            return PermissionSnapshots.Unavailable(subject, sections, PermissionReadStatus.Unsupported, "Fixture permissions reader.");
        }
    }

    [AvaloniaFact]
    public async Task A_place_opens_in_a_new_tab_and_inspects_that_folder()
    {
        if (FileCat.App.ViewModels.MainViewModel.PermissionProcessRefusal() is not null)
            Assert.Skip("Requires an ordinary FileCat process.");
        var permissions = new UnavailablePermissions();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(permissionService: permissions,
            prepareFiles: folder => Directory.CreateDirectory(Path.Combine(folder, "place")));
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var panel = vm.Workspace.Panels[0];
            var before = panel.ActiveTab!;
            var originalLocation = before.Location;
            string placePath = Path.Combine(root, "files", "place");
            var place = new FileCat.App.ViewModels.Place("Fixture place", null, Location.FileSystem(placePath), () => null);
            var view = window.GetVisualDescendants().OfType<PanelView>().First(v => ReferenceEquals(v.DataContext, panel));
            var item = Assert.IsAssignableFrom<IEnumerable<Control>>(view.PlaceMenu(place).ItemsSource).OfType<MenuItem>()
                .Single(menuItem => Equals(menuItem.Header, "Permissions and ownership…"));
            Assert.True(item.IsEnabled);
            Assert.Equal("Opens the place in a new tab and inspects that folder's permissions.", AutomationProperties.GetHelpText(item));

            var inspection = vm.InspectPlacePermissionsAsync(panel, place);
            await Until(() => TextPresent(window, "Fixture permissions reader."), ct);
            // The original tab keeps its location; the new tab shows exactly the place, and the inspected object is it.
            Assert.Equal(originalLocation, before.Location);
            Assert.NotSame(before, panel.ActiveTab);
            Assert.Equal(Location.FileSystem(placePath), panel.ActiveTab!.Location);
            Assert.Equal(placePath, Assert.Single(permissions.Subjects).RawLocator);
            Click(FindButton(window, "Close")!);
            await inspection.WaitAsync(TimeSpan.FromSeconds(10), ct);

            // A place outside the file system has no route here.
            var server = new FileCat.App.ViewModels.Place("Server", null, null, () => null) { Connects = true };
            Assert.DoesNotContain(Assert.IsAssignableFrom<IEnumerable<Control>>(view.PlaceMenu(server).ItemsSource).OfType<MenuItem>(),
                menuItem => Equals(menuItem.Header, "Permissions and ownership…"));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_drive_row_names_the_route_that_can_inspect_its_mounted_root()
    {
        if (FileCat.App.ViewModels.MainViewModel.PermissionProcessRefusal() is not null)
            Assert.Skip("Requires an ordinary FileCat process.");
        var permissions = new UnavailablePermissions();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(permissionService: permissions);
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            tab.Navigate(new Location(FileCat.Core.Resources.Schemes.Computer, string.Empty));
            await Until(() => tab.Listing.State == ListingState.Complete && Enumerable.Range(0, tab.Listing.VisibleCount)
                .Any(index => tab.Listing.GetVisible(index).Tag is DriveTag { Ready: true }), ct);
            var drive = Enumerable.Range(0, tab.Listing.VisibleCount).Select(tab.Listing.GetVisible)
                .First(entry => entry.Tag is DriveTag { Ready: true });
            tab.Listing.SetFocus(tab.Listing.GetVisibleIndex(tab.Listing.FindStoreIndex(drive.Name)));
            vm.Execute(CommandIds.PermissionsOwnership);
            // The drive listing captures no mount identity: the inspector names the bound route instead of reading.
            await Until(() => TextPresent(window, "Open the mounted folder and use its background menu"), ct);
            Assert.Empty(permissions.Subjects);
            Assert.Empty(services.Jobs.Jobs);
            Click(FindButton(window, "Close")!);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Editors_open_on_their_first_shown_field_instead_of_the_dialog_card()
    {
        // Hidden, collapsed and empty controls are skipped, so the dialog never starts on something a keyboard cannot use.
        var choice = new ComboBox { ItemsSource = new[] { "Allow", "Deny" } };
        var content = new StackPanel { Children = { new TextBlock { Text = "Scope" }, new Button { Content = "Hidden", IsVisible = false },
            new Expander { Header = "More", Content = new TextBox(), IsExpanded = false }, new ListBox { ItemsSource = Array.Empty<string>() },
            new Button { Content = "Disabled", IsEnabled = false }, choice, new Button { Content = "Later" } } };
        Assert.Same(choice, FileCat.App.ViewModels.MainViewModel.FirstField(content));
        var editor = new UnixPermissionModeEditor(_ => false, false);
        var mode = Assert.IsType<CheckBox>(FileCat.App.ViewModels.MainViewModel.FirstField(editor));
        Assert.Equal("Owner read", Avalonia.Automation.AutomationProperties.GetName(mode));
        // A definite mode has no "mixed" placeholder for a screen reader to read beside its value; a mixed selection does.
        TextBox Octal(UnixPermissionModeEditor view) => view.GetVisualDescendants().OfType<TextBox>()
            .Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == "Permissions as octal");
        Assert.Null(Octal(editor).PlaceholderText);
        Assert.Equal("mixed", Octal(new UnixPermissionModeEditor(bit => bit == UnixFileMode.OtherRead ? null : false, false)).PlaceholderText);
        Assert.Null(FileCat.App.ViewModels.MainViewModel.FirstField(new StackPanel { Children = { new TextBlock { Text = "Nothing to edit" } } }));
    }

    [AvaloniaFact]
    public void The_audit_editor_requires_an_event_and_returns_a_single_explicit_change()
    {
        var sid = new Principal(PrincipalKind.WindowsSid, "S-1-1-0", "local");
        var rule = WindowsPermissionAuditPatch.FileEntry(sid, 1, WindowsPermissionAuditPatch.Success);
        var baseline = new WindowsSacl(true, 2, new(0x10, 1, null), [rule], [], []);
        var editor = new WindowsAuditEditor(baseline, sid.Authority);
        var actions = editor.Children.OfType<WrapPanel>().SelectMany(panel => panel.Children).OfType<CheckBox>().ToArray();
        var success = Assert.Single(actions, box => Equals(box.Content, "Audit successful access"));
        var failure = Assert.Single(actions, box => Equals(box.Content, "Audit failed access"));
        Assert.Null(editor.ReadChange().Change);
        success.IsChecked = false;
        Assert.Equal("Select successful access, failed access or both.", editor.ReadChange().Error);
        failure.IsChecked = true;
        var change = Assert.IsType<ChangeWindowsAuditAce>(editor.ReadChange().Change);
        Assert.Equal(0, change.Occurrence);
        Assert.Same(rule, change.Expected);
        Assert.Equal(sid, change.Replacement!.Principal);
        Assert.True(change.Replacement.AuditsFailure);
        Assert.False(change.Replacement.AuditsSuccess);
        Assert.Contains("Failure", WindowsAuditEditor.Describe(change.Replacement), StringComparison.Ordinal);
    }

    [AvaloniaTheory]
    [InlineData("mode")]
    [InlineData("owner")]
    [InlineData("discard")]
    [InlineData("dismiss")]
    public async Task Linux_panel_reviews_authorized_metadata_before_submitting_a_separate_apply(string scenario)
    {
        string? bus = Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_BROKER_SOCKET");
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess || string.IsNullOrEmpty(bus))
            Assert.Skip("Requires the root-owned native permissions broker on a private bus.");
        const string modeLabel = "Edit mode…";
        string name = scenario == "dismiss" ? "dismiss-apply" : "authorized-target";
        bool owner = scenario == "owner";
        var initial = (UnixFileMode)(owner ? 0x9c0 : 0x180);
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(linuxPermissionBrokerSocket: bus,
            prepareFiles: folder =>
            {
                string path = Path.Combine(folder, name);
                File.WriteAllText(path, "witness");
                File.SetUnixFileMode(path, initial);
            });
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            string path = Path.Combine(root, "files", name);
            await Until(() => listing.State == ListingState.Complete && listing.FocusName(name), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Inspect with authorization…")?.IsEnabled == true, ct);
            Click(FindButton(window, "Inspect with authorization…")!);
            await Until(() => TextPresent(window, "Authorized Linux security observation") ||
                FindButton(window, "Change owner…") is not null, ct);
            Assert.Empty(services.Jobs.Jobs);
            Assert.Equal(initial, File.GetUnixFileMode(path));
            Click(FindButton(window, owner ? "Change owner…" : modeLabel)!);
            string inputName = owner ? "New owner UID" : "Permissions as octal";
            IEnumerable<TextBox> Inputs() => window.GetVisualDescendants().OfType<TextBox>()
                .Where(box => AutomationProperties.GetName(box) == inputName);
            await Until(() => FindButton(window, "Apply with authorization") is not null && Inputs().Any(), ct);
            Assert.False(FindButton(window, "Apply with authorization")!.IsEnabled);
            var input = Assert.Single(Inputs());
            input.Text = owner ? "12345" : "400";
            if (owner)
            {
                await Until(() => TextPresent(window, "Owner UID") && TextPresent(window, "12345"), ct);
                Assert.False(FindButton(window, "Apply with authorization")!.IsEnabled);
                window.GetVisualDescendants().OfType<CheckBox>().Single(box =>
                    Equals(box.Content, "Accept clearing the reviewed set-ID bits")).IsChecked = true;
            }
            await Until(() => FindButton(window, "Apply with authorization")?.IsEnabled == true, ct);
            if (scenario == "discard")
            {
                Click(FindButton(window, "Cancel")!);
                await Until(() => FindButton(window, "Discard") is not null, ct);
                Click(FindButton(window, "Discard")!);
                await Until(() => FindButton(window, "Apply with authorization") is null, ct);
                Assert.Empty(services.Jobs.Jobs);
                Assert.Equal(initial, File.GetUnixFileMode(path));
                return;
            }
            Click(FindButton(window, "Apply with authorization")!);
            await Until(() => services.Jobs.Jobs.SingleOrDefault()?.PermissionResults.Count == 1, ct);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobKind.Permissions, job.Kind);
            var result = Assert.Single(job.PermissionResults);
            if (scenario == "dismiss")
            {
                Assert.Equal(PermissionVerdict.Unattempted, result.Verdict);
                Assert.Equal(initial, File.GetUnixFileMode(path));
                return;
            }
            Assert.Equal(PermissionVerdict.Verified, result.Verdict);
            Assert.Equal(PermissionVerification.SameObject, Assert.Single(result.Components).Verification);
            Assert.Equal((UnixFileMode)(owner ? 0x1c0 : 0x100), File.GetUnixFileMode(path));
            if (owner)
            {
                var subject = new PermissionSubject(Location.FileSystem(Path.GetDirectoryName(path)!), path, path,
                    new(PermissionAuthorityKind.Local, "fixture", 0), PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
                using var actual = new LinuxPermissionService().Inspect(subject, PermissionSections.Owner, ct);
                Assert.Equal("12345", actual.Snapshot.Owner.Value?.Id);
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sftp_panel_inspects_mode_zero_and_exact_symbolic_link(bool link)
    {
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess) Assert.Skip("Requires an ordinary Linux OpenSSH fixture.");
        using var sshd = TestSshd.TryStart();
        if (sshd is null) Assert.Skip("Requires the owned OpenSSH test server.");
        string document = Path.Combine(sshd.Files, "mode zero");
        File.WriteAllText(document, "witness");
        File.SetUnixFileMode(document, UnixFileMode.None);
        File.CreateSymbolicLink(Path.Combine(sshd.Files, "link"), document);
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var profile = sshd.Profile;
            services.Settings.RemoteProfiles.Add(profile);
            new HostKeyTrust(Path.Combine(services.Paths.LocalDirectory, "known_hosts"), []).Remember(profile.Host, profile.Port, sshd.HostKey);
            var tab = vm.ActiveTab!;
            tab.Navigate(SftpProvider.At(profile, sshd.Files));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName(link ? "link" : "mode zero"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => TextPresent(window, link ? "Symbolic link · SFTP LSTAT" : "File · SFTP LSTAT"), ct);
            Assert.True(TextPresent(window, link ? "Server mode · 777" : "Server mode · 000"));
            Assert.Null(FindButton(window, "Edit mode…"));
            if (link) Assert.Null(FindButton(window, "Review SFTP permissions…"));
            Assert.Empty(services.Jobs.Jobs);
            Assert.Equal(UnixFileMode.None, File.GetUnixFileMode(document));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData("mode")]
    [InlineData("compound")]
    [InlineData("cancel")]
    [InlineData("drift-before-editor")]
    [InlineData("drift-before-apply")]
    [InlineData("profile-changed")]
    [InlineData("server-refusal")]
    public async Task Sftp_panel_reviews_exact_server_state_and_submits_only_the_accepted_draft(string scenario)
    {
        string? privateRoot = Environment.GetEnvironmentVariable("FILECAT_TEST_SFTP_ADMISSION_ROOT");
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess || privateRoot is null || !Directory.Exists(privateRoot))
            Assert.Skip("Requires the private ordinary-user OpenSSH permissions fixture.");
        using var sshd = TestSshd.TryStart(disableLSetStat: scenario == "server-refusal", privateReviewRoot: privateRoot);
        if (sshd is null) Assert.Skip("Requires the owned OpenSSH test server.");
        string path = Path.Combine(sshd.Files, "server witness");
        File.WriteAllText(path, "witness");
        bool compound = scenario == "compound";
        File.SetUnixFileMode(path, (UnixFileMode)(compound ? 0x1a0 : 0));
        var modified = File.GetLastWriteTimeUtc(path);
        string? newGroup = null;
        if (compound)
        {
            var credentials = LinuxPermissionService.ReadGroupAuthority(TestContext.Current.CancellationToken);
            newGroup = credentials.SupplementaryGroupIds.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(group => group != credentials.GroupId);
            if (newGroup is null) Assert.Skip("Requires a supplementary group for the ordinary fixture user.");
        }
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var profile = sshd.Profile;
            services.Settings.RemoteProfiles.Add(profile);
            new HostKeyTrust(Path.Combine(services.Paths.LocalDirectory, "known_hosts"), []).Remember(profile.Host, profile.Port, sshd.HostKey);
            var tab = vm.ActiveTab!;
            tab.Navigate(SftpProvider.At(profile, sshd.Files));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("server witness"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => TextPresent(window, "File · SFTP LSTAT"), ct);
            if (scenario == "server-refusal")
            {
                Assert.True(TextPresent(window, "version 1 no-follow security extension"));
                Assert.Null(FindButton(window, "Review SFTP permissions…"));
                Assert.Empty(services.Jobs.Jobs);
                return;
            }
            var review = Assert.IsType<Button>(FindButton(window, "Review SFTP permissions…"));
            if (scenario == "drift-before-editor") File.SetUnixFileMode(path, UnixFileMode.UserWrite);
            Click(review);
            if (scenario == "drift-before-editor")
            {
                await Until(() => TextPresent(window, "original server path, connection or permissions changed"), ct);
                Assert.Empty(services.Jobs.Jobs);
                Assert.Equal(UnixFileMode.UserWrite, File.GetUnixFileMode(path));
                return;
            }
            await Until(() => window.GetVisualDescendants().OfType<UnixPermissionModeEditor>().Any(), ct);
            Assert.True(TextPresent(window, "SFTP identifies this item by its server path"));
            Assert.True(TextPresent(window, profile.User + "@127.0.0.1"));
            var octal = window.GetVisualDescendants().OfType<TextBox>().Single(field => AutomationProperties.GetName(field) == "Permissions as octal");
            octal.Text = compound ? "2620" : "640";
            if (compound)
            {
                var group = window.GetVisualDescendants().OfType<TextBox>().Single(field => AutomationProperties.GetName(field) == "New server group ID");
                group.Text = "42949672950";
                await Until(() => TextPresent(window, "Choose a numerical group ID"), ct);
                Assert.False(FindButton(window, "Apply")!.IsEnabled);
                group.Text = newGroup;
                await Until(() => TextPresent(window, "Paired UID · 4294967295"), ct);
                Assert.False(FindButton(window, "Apply")!.IsEnabled);
                window.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Accept the reviewed set-ID effects")).IsChecked = true;
            }
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            if (scenario == "cancel")
            {
                Click(FindButton(window, "Cancel")!);
                await Until(() => FindButton(window, "Discard") is not null, ct);
                Click(FindButton(window, "Discard")!);
                await Until(() => !window.GetVisualDescendants().OfType<UnixPermissionModeEditor>().Any(), ct);
                Assert.Empty(services.Jobs.Jobs);
                Assert.Equal(UnixFileMode.None, File.GetUnixFileMode(path));
                return;
            }
            if (scenario == "drift-before-apply") File.SetUnixFileMode(path, UnixFileMode.UserWrite);
            if (scenario == "profile-changed") profile.InitialPath = "/changed";
            Click(FindButton(window, "Apply")!);
            if (scenario is "drift-before-apply" or "profile-changed")
            {
                await Until(() => TextPresent(window, scenario == "profile-changed" ? "authenticated SFTP connection changed" : "SFTP object changed after review"), ct);
                Assert.Empty(services.Jobs.Jobs);
                Assert.Equal(scenario == "drift-before-apply" ? UnixFileMode.UserWrite : UnixFileMode.None, File.GetUnixFileMode(path));
                return;
            }
            await Until(() => TextPresent(window, "Verified changed") && TextPresent(window, "server-path results"), ct);
            Assert.Equal((UnixFileMode)(compound ? 0x590 : 0x1a0), File.GetUnixFileMode(path));
            Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobKind.Permissions, job.Kind);
            Assert.True(job.PermissionUsesServerPath);
            var result = Assert.Single(job.PermissionResults);
            Assert.Equal(compound ? 4 : 1, result.Components.Length);
            Assert.All(result.Components, component => Assert.Equal(PermissionVerification.ServerPath, component.Verification));
            Click(FindButton(window, "Review current state…")!);
            await Until(() => TextPresent(window, compound ? "Server mode · 2620" : "Server mode · 640"), ct);
            if (compound) Assert.True(TextPresent(window, $"Server group ID: {newGroup}"));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Ftp_panel_reports_mode_and_transfer_actions_without_editing()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("FILECAT_TEST_FTP_PERMISSION_PORT"), out int port) || port is < 1024 or > 65535)
            Assert.Skip("Requires the task-owned FTP permissions fixture on loopback.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var profile = new RemoteProfile
            {
                Name = "FTP permissions fixture", Host = "127.0.0.1", Port = port, Protocol = RemoteProtocols.Ftp,
                User = "anonymous", PlainTextAccepted = true
            };
            services.Settings.RemoteProfiles.Add(profile);
            var tab = vm.ActiveTab!;
            var location = SftpProvider.At(profile, "/");
            tab.Navigate(location);
            await Until(() => tab.Location == location && tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("permission.txt"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => TextPresent(window, "Exact-item changes are unavailable over FTP and FTPS."), ct);
            Assert.True(TextPresent(window, "MLSD"));
            Assert.True(TextPresent(window, "Server mode · 640"));
            Assert.True(TextPresent(window, "Reported FTP action codes · r"));
            Assert.True(TextPresent(window, "Server owner ID: 42"));
            Assert.Null(FindButton(window, "Edit mode…"));
            Assert.Null(FindButton(window, "Edit access ACL…"));
            Assert.Empty(services.Jobs.Jobs);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mac_qualified_panel_mode_editor_applies_to_the_reviewed_document(bool descriptor)
    {
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess || Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT") is not { } volume ||
            !Path.GetTempPath().StartsWith(Path.TrimEndingDirectorySeparator(volume) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            Assert.Skip("Requires the ordinary macOS disposable permissions volume.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
            File.SetUnixFileMode(Path.Combine(folder, "a.txt"), descriptor ? (UnixFileMode)0x1c0 : 0),
            permissionService: new DarwinPermissionService(qualifyParentWrites: !descriptor, qualifyDescriptorWrites: descriptor));
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("a.txt"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Edit mode…") is not null, ct);
            Click(FindButton(window, "Edit mode…")!);
            await Until(() => window.GetVisualDescendants().OfType<UnixPermissionModeEditor>().Any(), ct);
            Assert.True(TextPresent(window, descriptor ? "keeps the selected object open" : "replacement restored before verification"));
            var octal = window.GetVisualDescendants().OfType<TextBox>().Single(field => AutomationProperties.GetName(field) == "Permissions as octal");
            octal.Text = "640";
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            Click(FindButton(window, "Apply")!);
            await Until(() => TextPresent(window, "Verified changed"), ct);
            Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(Path.Combine(root, "files", "a.txt")));
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobKind.Permissions, job.Kind);
            var result = Assert.Single(Assert.Single(job.PermissionResults).Components);
            Assert.Contains(descriptor ? "retained descriptor" : "checked parent and name", result.NativeCall);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Mac_panel_checks_current_process_access_without_submitting_a_job()
    {
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess || Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT") is not { } volume ||
            !Path.GetTempPath().StartsWith(Path.TrimEndingDirectorySeparator(volume) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            Assert.Skip("Requires the ordinary macOS disposable permissions volume.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
            File.SetUnixFileMode(Path.Combine(folder, "a.txt"), UnixFileMode.UserRead),
            permissionService: new DarwinPermissionService(qualifyAccessChecks: true));
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("a.txt"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Check current process access…") is not null, ct);
            Click(FindButton(window, "Check current process access…")!);
            await Until(() => TextPresent(window, "Delete this entry from its parent"), ct);
            Assert.True(TextPresent(window, "Read content · Allowed"));
            Assert.True(TextPresent(window, "Write content · Denied"));
            Assert.True(TextPresent(window, "effective UID"));
            Assert.True(TextPresent(window, "macOS credential groups"));
            Assert.Empty(services.Jobs.Jobs);
            Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(Path.Combine(root, "files", "a.txt")));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mac_panel_reviews_group_change_and_explicit_set_id_restoration(bool descriptor)
    {
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess || Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT") is not { } volume ||
            !Path.GetTempPath().StartsWith(Path.TrimEndingDirectorySeparator(volume) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            Assert.Skip("Requires the ordinary macOS disposable permissions volume.");
        var ct = TestContext.Current.CancellationToken;
        var authority = DarwinPermissionService.ReadGroupAuthority(ct);
        if (authority.Groups.Length < 2) Assert.Skip("Requires a supplementary group in the ordinary fixture credential.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            string file = Path.Combine(folder, "a.txt");
            File.SetUnixFileMode(file, (UnixFileMode)0xde0);
            RunAclTool("chflags", "nodump,uchg", file);
        }, permissionService: new DarwinPermissionService(qualifyParentWrites: !descriptor, qualifyDescriptorWrites: descriptor,
            qualifyProtectionWrites: true, qualifyGroupWrites: true));
        string path = Path.Combine(root, "files", "a.txt");
        try
        {
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("a.txt"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Change group…") is not null, ct);
            Click(FindButton(window, "Change group…")!);
            await Until(() => window.GetVisualDescendants().OfType<UnixPermissionModeEditor>().Any(), ct);
            var group = window.GetVisualDescendants().OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == "Assignable group ID");
            group.SelectedIndex = 0;
            string target = Assert.IsType<string>(group.SelectedItem);
            Assert.NotEqual(RunAclTool("stat", "-f", "%g", path).Trim(), target);
            var octal = window.GetVisualDescendants().OfType<TextBox>().Single(field => AutomationProperties.GetName(field) == "Permissions as octal");
            octal.Text = "2610";
            await Until(() => TextPresent(window, "Review clearing the active file protection"), ct);
            Assert.False(FindButton(window, "Apply")!.IsEnabled);
            var checks = window.GetVisualDescendants().OfType<CheckBox>().ToArray();
            checks.Single(box => Equals(box.Content, "Finder Locked")).IsChecked = false;
            checks.Single(box => Equals(box.Content, "Append only")).IsChecked = true;
            var review = window.GetVisualDescendants().OfType<ListBox>()
                .Single(list => AutomationProperties.GetName(list) == "Reviewed changes");
            var rows = review.Items.OfType<string>().ToArray();
            int clear = Array.IndexOf(rows, "Clear set-user-ID");
            int assignment = Array.FindIndex(rows, row => row.StartsWith("Change group ID", StringComparison.Ordinal));
            Assert.True(clear >= 0 && assignment > clear, string.Join(Environment.NewLine, rows));
            Assert.False(FindButton(window, "Apply")!.IsEnabled);
            checks.Single(box => Equals(box.Content, "Accept the reviewed set-ID effects")).IsChecked = true;
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            Click(FindButton(window, "Apply")!);
            await Until(() => TextPresent(window, "Verified changed"), ct);
            var outcome = Assert.Single(Assert.Single(services.Jobs.Jobs).PermissionResults);
            Assert.Equal(new[] { PermissionSections.Flags, PermissionSections.Mode, PermissionSections.Group,
                PermissionSections.Mode, PermissionSections.Mode, PermissionSections.Flags }, outcome.Components.Select(step => step.Section));
            Assert.Equal(target, RunAclTool("stat", "-f", "%g", path).Trim());
            Assert.Equal((UnixFileMode)0x588, File.GetUnixFileMode(path));
            string flags = RunAclTool("stat", "-f", "%Sf", path);
            Assert.Contains("nodump", flags);
            Assert.Contains("uappnd", flags);
        }
        finally
        {
            RunAclTool("chflags", "nouchg,nouappnd", path);
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mac_panel_reviews_an_ordered_ACL_edit_or_explicit_removal(bool remove)
    {
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess || Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT") is not { } volume ||
            !Path.GetTempPath().StartsWith(Path.TrimEndingDirectorySeparator(volume) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            Assert.Skip("Requires the ordinary macOS disposable permissions volume.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            string file = Path.Combine(folder, "a.txt");
            File.SetUnixFileMode(file, remove ? (UnixFileMode)0x1c0 : 0);
            RunAclTool("chmod", "+a#", "0", "everyone allow read", file);
            RunAclTool("chmod", "+a#", "1", "everyone deny delete", file);
            RunAclTool("chmod", "+a#", "2", "everyone allow read", file);
        }, permissionService: new DarwinPermissionService(qualifyParentWrites: !remove, qualifyDescriptorWrites: remove, qualifyAclWrites: true));
        string path = Path.Combine(root, "files", "a.txt");
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("a.txt"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Edit access ACL…") is not null, ct);
            Click(FindButton(window, "Edit access ACL…")!);
            await Until(() => window.GetVisualDescendants().OfType<DarwinAccessEditor>().Any(), ct);
            var view = window.GetVisualDescendants().OfType<DarwinAccessEditor>().Single();
            if (remove)
            {
                var operation = view.GetVisualDescendants().OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == "Darwin ACL operation");
                operation.SelectedIndex = 3;
                await Until(() => TextPresent(window, "Confirm removal of the extended ACL"), ct);
                Assert.False(FindButton(window, "Apply")!.IsEnabled);
                view.GetVisualDescendants().OfType<CheckBox>()
                    .Single(box => Equals(box.Content, "Review removal of all extended access entries")).IsChecked = true;
            }
            else
            {
                view.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationProperties.GetName(list) == "Ordered Darwin access entries").SelectedIndex = 2;
                view.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Read attributes")).IsChecked = true;
                await Until(() => TextPresent(window, "Entry 3 · Allow"), ct);
            }
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            Click(FindButton(window, "Apply")!);
            await Until(() => TextPresent(window, "Verified changed"), ct);
            var stage = Assert.Single(Assert.Single(Assert.Single(services.Jobs.Jobs).PermissionResults).Components);
            Assert.Equal(PermissionSections.Access, stage.Section);
            Assert.Equal(PermissionVerification.SameObject, stage.Verification);
            var rules = RunAclTool("ls", "-led", path);
            if (remove) Assert.DoesNotContain("everyone ", rules);
            else
            {
                var rows = rules.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                Assert.Contains(rows, row => row.StartsWith("0:", StringComparison.Ordinal) && row.EndsWith(" allow read", StringComparison.Ordinal));
                Assert.Contains(rows, row => row.StartsWith("1:", StringComparison.Ordinal) && row.EndsWith(" deny delete", StringComparison.Ordinal));
                Assert.Contains(rows, row => row.StartsWith("2:", StringComparison.Ordinal) && row.EndsWith(" allow read,readattr", StringComparison.Ordinal));
            }
        }
        finally
        {
            // The retained deny entry intentionally prevents deleting this fixture as the ordinary user.
            RunAclTool("chmod", "-N", path);
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mac_panel_reviews_compound_acl_group_mode_and_protection(bool descriptor)
    {
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess || Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT") is not { } volume ||
            !Path.GetTempPath().StartsWith(Path.TrimEndingDirectorySeparator(volume) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            Assert.Skip("Requires the ordinary macOS disposable permissions volume.");
        var ct = TestContext.Current.CancellationToken;
        if (DarwinPermissionService.ReadGroupAuthority(ct).Groups.Length < 2) Assert.Skip("Requires a supplementary group in the fixture credential.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            string file = Path.Combine(folder, "a.txt");
            File.SetUnixFileMode(file, (UnixFileMode)0x1e0);
            RunAclTool("chmod", "+a#", "0", "everyone deny write", file);
            RunAclTool("chflags", "nodump,uchg", file);
        }, permissionService: new DarwinPermissionService(qualifyParentWrites: !descriptor, qualifyDescriptorWrites: descriptor,
            qualifyProtectionWrites: true, qualifyAclWrites: true, qualifyGroupWrites: true));
        string path = Path.Combine(root, "files", "a.txt");
        try
        {
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("a.txt"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Edit access ACL…") is not null, ct);
            Click(FindButton(window, "Edit access ACL…")!);
            await Until(() => window.GetVisualDescendants().OfType<DarwinAccessEditor>().Any(), ct);
            var editor = window.GetVisualDescendants().OfType<DarwinAccessEditor>().Single();
            editor.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Read data")).IsChecked = true;
            editor.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Write data")).IsChecked = false;
            await Until(() => TextPresent(window, "Review clearing the active file protection"), ct);
            var octal = window.GetVisualDescendants().OfType<TextBox>().Single(field => AutomationProperties.GetName(field) == "Permissions as octal");
            octal.Text = "2450";
            var group = window.GetVisualDescendants().OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == "Assignable group ID");
            Assert.Equal("Keep current group", group.SelectedItem);
            group.SelectedIndex = 1;
            string targetGroup = Assert.IsType<string>(group.SelectedItem);
            var checks = window.GetVisualDescendants().OfType<CheckBox>().ToArray();
            checks.Single(box => Equals(box.Content, "Finder Locked")).IsChecked = false;
            checks.Single(box => Equals(box.Content, "Append only")).IsChecked = true;
            var preview = window.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationProperties.GetName(list) == "Reviewed changes");
            var rows = preview.Items.OfType<string>().ToArray();
            var groupStep = Array.FindIndex(rows, row => row.StartsWith("Change group ID", StringComparison.Ordinal));
            var aclStep = Array.FindIndex(rows, row => row.StartsWith("Entry 1 · Deny", StringComparison.Ordinal));
            Assert.True(groupStep >= 0 && aclStep > groupStep, string.Join(Environment.NewLine, rows));
            Assert.Contains(rows, row => row.Contains("temporarily allow writes", StringComparison.Ordinal));
            Assert.False(FindButton(window, "Apply")!.IsEnabled);
            checks.Single(box => Equals(box.Content, "Accept the reviewed set-ID effects")).IsChecked = true;
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            Click(FindButton(window, "Apply")!);
            await Until(() => TextPresent(window, "Verified changed"), ct);
            var result = Assert.Single(Assert.Single(services.Jobs.Jobs).PermissionResults);
            Assert.Equal(new[] { PermissionSections.Flags, PermissionSections.Mode, PermissionSections.Group, PermissionSections.Access,
                PermissionSections.Mode, PermissionSections.Mode, PermissionSections.Flags }, result.Components.Select(component => component.Section));
            Assert.Equal(targetGroup, RunAclTool("stat", "-f", "%g", path).Trim());
            Assert.Equal((UnixFileMode)0x528, File.GetUnixFileMode(path));
            Assert.Contains("everyone deny read", RunAclTool("ls", "-led", path));
            Assert.Contains("uappnd", RunAclTool("stat", "-f", "%Sf", path));
        }
        finally
        {
            RunAclTool("chflags", "nouchg,nouappnd", path);
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mac_panel_reviews_explicit_unlock_before_applying_mode_and_append_only(bool descriptor)
    {
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess || Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT") is not { } volume ||
            !Path.GetTempPath().StartsWith(Path.TrimEndingDirectorySeparator(volume) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            Assert.Skip("Requires the ordinary macOS disposable permissions volume.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            string file = Path.Combine(folder, "a.txt");
            File.SetUnixFileMode(file, descriptor ? (UnixFileMode)0x1c0 : 0);
            RunAclTool("chflags", "nodump,uchg", file);
        }, permissionService: new DarwinPermissionService(qualifyParentWrites: !descriptor, qualifyDescriptorWrites: descriptor,
            qualifyProtectionWrites: true));
        string path = Path.Combine(root, "files", "a.txt");
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("a.txt"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Edit file protection…") is not null && TextPresent(window, "Finder Locked"), ct);
            Click(FindButton(window, "Edit file protection…")!);
            await Until(() => window.GetVisualDescendants().OfType<UnixPermissionModeEditor>().Any(), ct);
            var fields = window.GetVisualDescendants().OfType<CheckBox>().ToArray();
            var locked = fields.Single(box => Equals(box.Content, "Finder Locked"));
            var append = fields.Single(box => Equals(box.Content, "Append only"));
            Assert.True(locked.IsChecked);
            Assert.False(append.IsChecked);
            var octal = window.GetVisualDescendants().OfType<TextBox>().Single(field => AutomationProperties.GetName(field) == "Permissions as octal");
            octal.Text = "640";
            await Until(() => TextPresent(window, "Review clearing the active file protection"), ct);
            Assert.False(FindButton(window, "Apply")!.IsEnabled);
            Assert.Empty(services.Jobs.Jobs);
            locked.IsChecked = false;
            append.IsChecked = true;
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            Assert.True(TextPresent(window, "Clear Finder Locked"));
            Assert.True(TextPresent(window, "Set append-only protection"));
            Click(FindButton(window, "Apply")!);
            await Until(() => TextPresent(window, "Verified changed"), ct);
            var outcome = Assert.Single(Assert.Single(services.Jobs.Jobs).PermissionResults);
            Assert.Equal(new[] { PermissionSections.Flags, PermissionSections.Mode, PermissionSections.Flags }, outcome.Components.Select(step => step.Section));
            Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(path));
            string native = RunAclTool("stat", "-f", "%Sf", path);
            Assert.Contains("uappnd", native);
            Assert.Contains("nodump", native);
            Assert.DoesNotContain("uchg", native);
        }
        finally
        {
            RunAclTool("chflags", "nouchg,nouappnd", path);
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Mac_folder_menu_reports_the_mounted_volume_ownership_policy()
    {
        string? volume = Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT");
        if (!OperatingSystem.IsMacOS() || string.IsNullOrWhiteSpace(volume) || Environment.IsPrivilegedProcess)
            Assert.Skip("Requires a disposable mounted macOS test volume.");
        bool ignores = Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_IGNORE_OWNERS") == "1";
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(
            permissionService: new DarwinPermissionService(qualifyParentWrites: ignores, qualifyDescriptorWrites: ignores));
        string? fixture = null;
        try
        {
            var tab = vm.ActiveTab!;
            var location = Location.FileSystem(volume);
            tab.Navigate(location);
            CancellationToken ct = TestContext.Current.CancellationToken;
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Location == location, ct);
            var menu = ContextMenuFactory.BuildBackground(vm, vm.Workspace.ActivePanel!, tab, location);
            string title = services.Commands.Get(CommandIds.PermissionsOwnership)!.Title;
            var permission = menu.Items.OfType<MenuItem>().Single(item => item.Header as string == title);
            permission.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => TextPresent(window, ignores ? "volume ignores file ownership" : "volume honors file ownership"), ct);
            Assert.True(TextPresent(window, "Volume root"));
            if (ignores)
            {
                Assert.True(TextPresent(window, "Presented owner"));
                Click(FindButton(window, "Close")!);
                fixture = Directory.CreateDirectory(Path.Combine(volume, "presented-" + Guid.NewGuid().ToString("N"))).FullName;
                File.WriteAllText(Path.Combine(fixture, "ordinary.txt"), "fixture");
                tab.Navigate(Location.FileSystem(fixture));
                await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("ordinary.txt"), ct);
                vm.Execute(CommandIds.PermissionsOwnership);
                await Until(() => TextPresent(window, "volume ignores file ownership"), ct);
                Assert.True(TextPresent(window, "Presented owner"));
                Assert.True(TextPresent(window, "Presented primary group"));
                Assert.True(TextPresent(window, "requires a volume that honors ownership"));
                Assert.Null(FindButton(window, "Edit mode…"));
            }
            Assert.Empty(services.Jobs.Jobs);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
            if (fixture is not null) Directory.Delete(fixture, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Mac_panel_inspector_shows_ordered_rules_for_an_ordinary_document()
    {
        string? volume = Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT");
        if (!OperatingSystem.IsMacOS() || string.IsNullOrWhiteSpace(volume)) Assert.Skip("Requires a disposable mounted macOS test volume.");
        string folder = Directory.CreateDirectory(Path.Combine(volume, "inspector-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string path = Path.Combine(folder, "ordinary.txt");
            File.WriteAllText(path, "owned");
            string owner = RunAclTool("/usr/bin/id", "-un").Trim();
            RunAclTool("chmod", "+a#", "0", "everyone deny write", path);
            RunAclTool("chmod", "+a#", "1", $"user:{owner} allow read", path);
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            try
            {
                var tab = vm.ActiveTab!;
                tab.Navigate(Location.FileSystem(folder));
                CancellationToken ct = TestContext.Current.CancellationToken;
                await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("ordinary.txt"), ct);
                vm.Execute(CommandIds.PermissionsOwnership);
                await Until(() => TextPresent(window, "Darwin extended ACL"), ct);
                var access = window.GetVisualDescendants().OfType<ListBox>().Single(box => AutomationProperties.GetName(box) == "Access now");
                var rules = access.Items.OfType<string>().ToArray();
                Assert.Collection(rules, deny => Assert.Contains("Write data", deny), allow => Assert.Contains("Read data", allow));
                Assert.StartsWith("Deny", rules[0]);
                Assert.StartsWith("Allow", rules[1]);
                var lookup = FindButton(window, "Look up account")!;
                Assert.False(lookup.IsEnabled);
                Assert.False(TextPresent(window, "User · " + owner));
                access.SelectedIndex = 1;
                Assert.True(lookup.IsEnabled);
                Click(lookup);
                await Until(() => TextPresent(window, "User · " + owner), ct);
                Assert.Equal(rules, access.Items.OfType<string>());
                Assert.True(TextPresent(window, "BSD flags"));
                Assert.Null(FindButton(window, "Edit access ACL…"));
                Assert.Null(FindButton(window, "Edit mode…"));
                Assert.Empty(services.Jobs.Jobs);
            }
            finally { AccessibilityTests.Close(services, window, root); }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Windows_SMB_inspector_identifies_server_permissions_and_withholds_local_editing()
    {
        string? share = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_SMB_UNC");
        if (!OperatingSystem.IsWindows() || share is null)
            Assert.Skip("Requires a private Windows SMB share.");

        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(
            permissionService: new WindowsPermissionService(qualifySmbReads: true));
        services.Providers.Register(new FileCat.Platform.Windows.NetworkShareProvider());
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            tab.Navigate(Location.FileSystem(share));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("witness.txt"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => window.GetVisualDescendants().OfType<PermissionInspectorView>().Any(), ct);
            var shown = string.Join(" | ", window.GetVisualDescendants().OfType<TextBlock>().Select(label => label.Text));
            Assert.True(TextPresent(window, "Windows SMB "), shown);
            Assert.True(TextPresent(window, "Presented owner"), shown);
            Assert.True(TextPresent(window, "Share-level authorization is separate"));
            Assert.True(TextPresent(window, "inspection-only"));
            Assert.Null(FindButton(window, "Edit access ACL…"));
            Assert.Null(FindButton(window, "Inspect with authorization…"));
            Assert.Empty(services.Jobs.Jobs);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Windows_ReFS_inspector_displays_the_descriptor_without_edit_actions()
    {
        string? witness = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_REFS_FILE");
        if (!OperatingSystem.IsWindows() || witness is null)
            Assert.Skip("Requires a task-owned witness on a Windows ReFS volume.");

        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(
            permissionService: new WindowsPermissionService(qualifyRefsReads: true));
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            tab.Navigate(Location.FileSystem(Path.GetDirectoryName(witness)!));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName(Path.GetFileName(witness)), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => TextPresent(window, "File · ReFS · Windows security descriptor"), ct);
            Assert.True(TextPresent(window, "ReFS permissions are inspection-only"));
            Assert.True(TextPresent(window, "Owner"));
            Assert.Null(FindButton(window, "Edit access ACL…"));
            Assert.Null(FindButton(window, "Inspect with authorization…"));
            Assert.Empty(services.Jobs.Jobs);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Windows_ACL_estimate_panel_explains_its_current_token_scope()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_NTFS") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires the ordinary Windows x64 NTFS test volume.");

        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(
            permissionService: new WindowsPermissionService(qualifyAccessEstimates: true));
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("a.txt"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Estimate Windows ACL access…") is not null, ct);
            Click(FindButton(window, "Estimate Windows ACL access…")!);
            await Until(() => TextPresent(window, "Discretionary grant mask"), ct);
            Assert.True(TextPresent(window, "Windows process token"));
            Assert.True(TextPresent(window, "mandatory integrity"));
            Assert.Empty(services.Jobs.Jobs);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Windows_folder_editor_requires_and_reports_native_inheritance_scope()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_WINDOWS_SCOPE") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires the ordinary Windows x64 NTFS scope fixture.");

        const string descendant = "inherited.txt";
        string? path = null;
        var account = new SecurityIdentifier("S-1-5-32-546");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            var selected = Directory.CreateDirectory(Path.Combine(folder, "selected"));
            using var actor = WindowsIdentity.GetCurrent();
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(actor.User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            selected.SetAccessControl(acl);
            path = Path.Combine(selected.FullName, descendant);
            File.WriteAllText(path, "inheritance witness");
        }, permissionService: new WindowsPermissionService(qualifyNativePropagation: true));
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("selected"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Review Windows inheritance…") is not null, ct);
            Assert.Null(FindButton(window, "Edit access ACL…"));
            Click(FindButton(window, "Review Windows inheritance…")!);
            await Until(() => window.GetVisualDescendants().OfType<WindowsAccessEditor>().Any(), ct);
            var editor = Assert.Single(window.GetVisualDescendants().OfType<WindowsAccessEditor>());
            editor.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Add an explicit entry")).IsChecked = true;
            editor.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "New Windows entry SID").Text = account.Value;
            editor.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Read attributes")).IsChecked = true;
            editor.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Inherit into files")).IsChecked = true;
            await Until(() => TextPresent(window, "Approve Windows inheritance before applying"), ct);
            Assert.False(FindButton(window, "Apply")!.IsEnabled);
            Assert.Empty(services.Jobs.Jobs);
            window.GetVisualDescendants().OfType<CheckBox>().Single(box =>
                Equals(box.Content, "Allow Windows to update inheriting descendants")).IsChecked = true;
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            Click(FindButton(window, "Apply")!);

            await Until(() => TextPresent(window, "Descendant effects are unverified."), ct);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(PermissionScope.NativePropagation, job.PermissionReviewScope);
            Assert.Equal(PermissionVerdict.Verified, Assert.Single(job.PermissionResults).Verdict);
            Assert.Contains(new FileInfo(path!).GetAccessControl().GetAccessRules(false, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>(),
                rule => rule.IsInherited && rule.IdentityReference == account && rule.FileSystemRights.HasFlag(FileSystemRights.ReadAttributes));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Windows_folder_inheritance_editor_previews_protection_and_parent_merge(bool enable)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_WINDOWS_SCOPE") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires the ordinary Windows x64 NTFS scope fixture.");

        string? path = null;
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            var parent = Directory.CreateDirectory(Path.Combine(folder, "parent"));
            using var actor = WindowsIdentity.GetCurrent();
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(actor.User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-546"), FileSystemRights.ReadAttributes,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            parent.SetAccessControl(acl);
            var selected = parent.CreateSubdirectory("selected");
            if (enable)
            {
                var protectedAcl = new DirectorySecurity();
                protectedAcl.SetAccessRuleProtection(true, false);
                protectedAcl.AddAccessRule(new FileSystemAccessRule(actor.User!, FileSystemRights.FullControl, AccessControlType.Allow));
                selected.SetAccessControl(protectedAcl);
            }
            path = selected.FullName;
        }, permissionService: new WindowsPermissionService(qualifyNativePropagation: true, qualifyInheritanceChanges: true));
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            tab.Navigate(Location.FileSystem(Path.GetDirectoryName(path!)!));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("selected"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Review Windows inheritance…") is not null, ct);
            Click(FindButton(window, "Review Windows inheritance…")!);
            await Until(() => window.GetVisualDescendants().OfType<ComboBox>().Any(box =>
                AutomationProperties.GetName(box) == "Windows DACL inheritance action"), ct);
            var choice = window.GetVisualDescendants().OfType<ComboBox>().Single(box =>
                AutomationProperties.GetName(box) == "Windows DACL inheritance action");
            choice.SelectedIndex = 1;
            await Until(() => TextPresent(window, enable ? "Inherited from reviewed parent" : "becomes explicit"), ct);
            Assert.False(FindButton(window, "Apply")!.IsEnabled);
            window.GetVisualDescendants().OfType<CheckBox>().Single(box =>
                Equals(box.Content, "Allow Windows to update inheriting descendants")).IsChecked = true;
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            Click(FindButton(window, "Apply")!);
            await Until(() => TextPresent(window, "Descendant effects are unverified."), ct);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(PermissionVerdict.Verified, Assert.Single(job.PermissionResults).Verdict);
            Assert.Equal(!enable, new DirectoryInfo(path!).GetAccessControl(AccessControlSections.Access).AreAccessRulesProtected);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Windows_acl_reset_reviews_explicit_rule_losses_and_requires_approval()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_WINDOWS_SCOPE") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires the ordinary Windows x64 NTFS scope fixture.");

        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            var source = Directory.CreateDirectory(Path.Combine(folder, "selected"));
            using var actor = WindowsIdentity.GetCurrent();
            var access = new DirectorySecurity();
            access.SetAccessRuleProtection(true, false);
            access.AddAccessRule(new FileSystemAccessRule(actor.User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            source.SetAccessControl(access);
            var file = new FileInfo(Path.Combine(source.FullName, "captured.txt"));
            File.WriteAllText(file.FullName, "captured child");
            var dacl = file.GetAccessControl(AccessControlSections.Access);
            dacl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-546"), FileSystemRights.ReadAttributes,
                AccessControlType.Allow));
            file.SetAccessControl(dacl);
        }, permissionService: new WindowsPermissionService(qualifyAclResets: true));
        string selected = Path.Combine(root, "files", "selected");
        string captured = Path.Combine(selected, "captured.txt");
        var sourceBefore = new DirectoryInfo(selected).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("selected"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Reset Windows descendant ACLs…") is not null, ct);
            Click(FindButton(window, "Reset Windows descendant ACLs…")!);
            await Until(() => TextPresent(window, "Remove explicit") && TextPresent(window, "Replacement inherited"), ct);
            Assert.Empty(services.Jobs.Jobs);
            Assert.False(FindButton(window, "Reset captured ACLs")!.IsEnabled);
            window.GetVisualDescendants().OfType<CheckBox>().Single(box =>
                Equals(box.Content, "Approve the reviewed rule removals and inherited replacements")).IsChecked = true;
            await Until(() => FindButton(window, "Reset captured ACLs")?.IsEnabled == true, ct);
            Click(FindButton(window, "Reset captured ACLs")!);
            await Until(() => services.Jobs.Jobs.SingleOrDefault()?.PermissionResults.Count == 1, ct);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(PermissionScope.ResetTree, job.PermissionReviewScope);
            Assert.Equal(PermissionVerdict.Verified, Assert.Single(job.PermissionResults).Verdict);
            Assert.Equal(captured, Assert.Single(job.Request.Sources).FileSystemPath);
            Assert.Equal(sourceBefore, new DirectoryInfo(selected).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm());
            Assert.Empty(new FileInfo(captured).GetAccessControl(AccessControlSections.Access).GetAccessRules(true, false, typeof(SecurityIdentifier)));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Windows_folder_only_editor_applies_one_reviewed_rule_for_the_selected_folder()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_WINDOWS_SCOPE") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires the ordinary Windows x64 NTFS scope fixture.");

        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            var parent = Directory.CreateDirectory(Path.Combine(folder, "parent"));
            using var actor = WindowsIdentity.GetCurrent();
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(actor.User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            parent.SetAccessControl(acl);
            var selected = parent.CreateSubdirectory("selected");
            File.WriteAllText(Path.Combine(selected.FullName, "existing.txt"), "existing child");
        }, permissionService: new WindowsPermissionService(qualifyFolderOnly: true));
        string selected = Path.Combine(root, "files", "parent", "selected");
        string child = Path.Combine(selected, "existing.txt");
        byte[] childAcl = new FileInfo(child).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            tab.Navigate(Location.FileSystem(Path.GetDirectoryName(selected)!));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("selected"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Edit selected folder access rules…") is not null, ct);
            Assert.Null(FindButton(window, "Review Windows inheritance…"));
            Click(FindButton(window, "Edit selected folder access rules…")!);
            await Until(() => window.GetVisualDescendants().OfType<WindowsAccessEditor>().Any(), ct);
            Assert.True(TextPresent(window, "Scope · Selected folder"));
            Assert.True(TextPresent(window, "Windows can inherit these rules when it creates new children."));
            var editor = window.GetVisualDescendants().OfType<WindowsAccessEditor>().Single();
            editor.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Add an explicit entry")).IsChecked = true;
            editor.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Inherit into files")).IsChecked = true;
            editor.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "New Windows entry SID").Text = "S-1-5-32-546";
            editor.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Windows access mask").Text = "00000080";
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            Assert.Empty(services.Jobs.Jobs);
            Click(FindButton(window, "Apply")!);
            await Until(() => services.Jobs.Jobs.SingleOrDefault()?.PermissionResults.Count == 1, ct);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(PermissionScope.Objects, job.PermissionReviewScope);
            var result = Assert.Single(job.PermissionResults);
            Assert.Equal(PermissionVerdict.Verified, result.Verdict);
            Assert.Equal("Windows NtSetSecurityObject folder DACL", Assert.Single(result.Components).NativeCall);
            Assert.Equal(childAcl, new FileInfo(child).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm());
            string future = Path.Combine(selected, "future.txt");
            File.WriteAllText(future, "future child");
            var rules = new FileInfo(future).GetAccessControl(AccessControlSections.Access).GetAccessRules(false, true, typeof(SecurityIdentifier));
            Assert.Contains(rules.OfType<FileSystemAccessRule>(), rule => rule.IdentityReference.Value == "S-1-5-32-546" && rule.IsInherited);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Windows_volume_root_editor_requires_explicit_approval_for_the_bound_drive()
    {
        string? drive = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_ACL_WRITE_ROOT");
        if (!OperatingSystem.IsWindows() || drive is null ||
            Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_WINDOWS_SCOPE") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires the ordinary Windows x64 volume-root fixture.");

        var volume = new DriveInfo(drive);
        Assert.Equal(drive, volume.RootDirectory.FullName);
        Assert.Equal("FileCat ACL Root Probe", volume.VolumeLabel);
        Assert.False(drive.StartsWith(Environment.GetEnvironmentVariable("SystemDrive")!, StringComparison.OrdinalIgnoreCase));
        var directory = volume.RootDirectory;
        byte[] before = directory.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(
            permissionService: new WindowsPermissionService(qualifyVolumeRootWrites: true));
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            var location = Location.FileSystem(drive);
            tab.Navigate(location);
            await Until(() => tab.Location == location && tab.Listing.State == ListingState.Complete, ct);
            var flow = vm.InspectFolderPermissionsAsync(tab, location, tab.Listing.Generation);
            await Until(() => FindButton(window, "Edit volume root access rules…") is not null, ct);
            Assert.Null(FindButton(window, "Edit selected folder access rules…"));
            Click(FindButton(window, "Edit volume root access rules…")!);
            await Until(() => window.GetVisualDescendants().OfType<WindowsAccessEditor>().Any(), ct);
            Assert.True(TextPresent(window, "Scope · Selected NTFS volume root"));
            var editor = window.GetVisualDescendants().OfType<WindowsAccessEditor>().Single();
            editor.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Add an explicit entry")).IsChecked = true;
            editor.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "New Windows entry SID").Text = "S-1-5-32-546";
            editor.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Windows access mask").Text = "00000080";
            await Until(() => TextPresent(window, "Approve the reviewed volume root change before applying."), ct);
            Assert.False(FindButton(window, "Apply")!.IsEnabled);
            Assert.Empty(services.Jobs.Jobs);
            window.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Approve this volume root access change")).IsChecked = true;
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            Click(FindButton(window, "Apply")!);
            await Until(() => services.Jobs.Jobs.SingleOrDefault()?.PermissionResults.Count == 1, ct);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(drive, Assert.Single(job.Request.Sources).FileSystemPath);
            Assert.Equal(PermissionScope.Objects, job.PermissionReviewScope);
            var result = Assert.Single(job.PermissionResults);
            Assert.Equal(PermissionVerdict.Verified, result.Verdict);
            Assert.Equal("Windows NtSetSecurityObject volume-root DACL", Assert.Single(result.Components).NativeCall);
            Assert.Contains(directory.GetAccessControl(AccessControlSections.Access).GetAccessRules(true, false, typeof(SecurityIdentifier))
                .OfType<FileSystemAccessRule>(), rule => rule.IdentityReference.Value == "S-1-5-32-546" &&
                    rule.FileSystemRights.HasFlag(FileSystemRights.ReadAttributes));
            await Until(() => FindButton(window, "Close")?.IsEnabled == true, ct);
            Click(FindButton(window, "Close")!);
            await flow.WaitAsync(ct);
        }
        finally
        {
            var restore = new DirectorySecurity();
            restore.SetSecurityDescriptorBinaryForm(before, AccessControlSections.Access);
            directory.SetAccessControl(restore);
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Windows_access_tree_editor_requires_path_review_and_limits_dispatch_to_captured_targets(bool folders)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_WINDOWS_SCOPE") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires the ordinary Windows x64 NTFS scope fixture.");

        string kind = folders ? "folder" : "file";
        string capturedName = folders ? "capture" : "capture.txt";
        string lateName = folders ? "late" : "late.txt";
        void CreateTarget(string path)
        {
            if (folders) Directory.CreateDirectory(path);
            else File.WriteAllText(path, "ACL witness");
        }
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            var child = Directory.CreateDirectory(Path.Combine(folder, "selected"));
            if (folders)
            {
                using var actor = WindowsIdentity.GetCurrent();
                var acl = new DirectorySecurity();
                acl.SetAccessRuleProtection(true, false);
                acl.AddAccessRule(new FileSystemAccessRule(actor.User!, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                child.SetAccessControl(acl);
            }
            CreateTarget(Path.Combine(child.FullName, capturedName));
        }, permissionService: new WindowsPermissionService(qualifyFileTrees: !folders, qualifyFolderTrees: folders));
        string selected = Path.Combine(root, "files", "selected");
        string captured = Path.Combine(selected, capturedName);
        string late = Path.Combine(selected, lateName);
        byte[] folderAcl = new DirectoryInfo(selected).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("selected"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, $"Add access rule to descendant {kind}s…") is not null, ct);
            Click(FindButton(window, $"Add access rule to descendant {kind}s…")!);
            await Until(() => window.GetVisualDescendants().OfType<WindowsAccessEditor>().Any(), ct);
            var editor = window.GetVisualDescendants().OfType<WindowsAccessEditor>().Single();
            Assert.False(FindButton(window, $"Capture {kind} review")!.IsEnabled);
            editor.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "New Windows entry SID").Text = "S-1-5-32-546";
            editor.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Windows access mask").Text = "00000080";
            await Until(() => FindButton(window, $"Capture {kind} review")?.IsEnabled == true, ct);
            Click(FindButton(window, $"Capture {kind} review")!);
            await Until(() => FindButton(window, $"Apply to captured {kind}s")?.IsEnabled == true, ct);
            Assert.Empty(services.Jobs.Jobs);
            Assert.True(TextPresent(window, "Later arrivals are outside this review."));
            CreateTarget(late);
            Click(FindButton(window, $"Apply to captured {kind}s")!);
            await Until(() => services.Jobs.Jobs.SingleOrDefault()?.PermissionResults.Count == 1, ct);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(PermissionScope.ReviewedTree, job.PermissionReviewScope);
            Assert.Equal(PermissionVerdict.Verified, Assert.Single(job.PermissionResults).Verdict);
            Assert.Equal(captured, Assert.Single(job.Request.Sources).FileSystemPath);
            Assert.Equal(folderAcl, new DirectoryInfo(selected).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm());
            Assert.True(HasCapturedRule(captured));
            Assert.False(HasCapturedRule(late));
        }
        finally { AccessibilityTests.Close(services, window, root); }

        bool HasCapturedRule(string path) => (folders ? (FileSystemSecurity)new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access) :
                new FileInfo(path).GetAccessControl(AccessControlSections.Access))
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>()
            .Any(rule => rule.IdentityReference.Value == "S-1-5-32-546" && rule.FileSystemRights.HasFlag(FileSystemRights.ReadAttributes));
    }

    [AvaloniaFact]
    public async Task Windows_editor_applies_a_preset_and_an_advanced_right_to_one_explicit_entry()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_NTFS") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires the ordinary Windows x64 NTFS test volume.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(Path.Combine(folder, "a.txt")).SetAccessControl(security);
        });
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3, ct);
            Assert.True(listing.FocusName("a.txt"));
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Edit access ACL…") is not null, ct);
            Assert.NotNull(FindButton(window, "Change owner…"));
            await Until(() => TextPresent(window, "Reading the Windows audit ACL requires an authorized FileCat session"), ct);
            Click(FindButton(window, "Edit access ACL…")!);
            await Until(() => window.GetVisualDescendants().OfType<WindowsAccessEditor>().Any(), ct);
            var editor = Assert.Single(window.GetVisualDescendants().OfType<WindowsAccessEditor>());
            // Apply is unavailable until a change exists, so the editor opens on its ordered entries, which a screen reader
            // names with the dialog.
            var entries = editor.GetVisualDescendants().OfType<ListBox>().Single(list =>
                AutomationProperties.GetName(list) == "Ordered Windows access entries");
            await Until(() => window.FocusManager?.GetFocusedElement() is Avalonia.Visual focused &&
                (ReferenceEquals(focused, entries) || entries.IsVisualAncestorOf(focused)), ct);
            editor.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == "Add an explicit entry").IsChecked = true;
            var sid = editor.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "New Windows entry SID");
            sid.Text = "S-1-5-32-546";
            Click(FindButton(window, "Use preset")!);
            editor.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == "Write attributes").IsChecked = true;
            var mask = editor.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Windows access mask");
            Assert.Equal("0x00120189", mask.Text);
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            Click(FindButton(window, "Apply")!);
            await Until(() => TextPresent(window, "Verified changed"), ct);
            var result = Assert.Single(Assert.Single(services.Jobs.Jobs).PermissionResults);
            Assert.Equal(PermissionVerdict.Verified, result.Verdict);
            var native = new FileInfo(Path.Combine(root, "files", "a.txt")).GetAccessControl(AccessControlSections.Access);
            Assert.Contains(native.GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>(), entry =>
                entry.IdentityReference.Value == "S-1-5-32-546" && entry.AccessControlType == AccessControlType.Allow &&
                (uint)entry.FileSystemRights == 0x00120189);
            // The App's own protected Windows journal holds the complete history, which needs no recovery review.
            string history = Path.Combine(services.Paths.JournalDirectory, PermissionJournalNames.Directory);
            Assert.True(PermissionJournalNames.TryParse(Path.GetFileName(Assert.Single(Directory.GetFiles(history))), out _, out _));
            Assert.Empty(services.ScanPermissionHistory(ct));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Windows_editor_keys_apply_once_from_a_field_and_never_from_a_rule_option()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_NTFS") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires the ordinary Windows x64 NTFS test volume.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(Path.Combine(folder, "a.txt")).SetAccessControl(security);
        });
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("a.txt"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Edit access ACL…") is not null, ct);
            Click(FindButton(window, "Edit access ACL…")!);
            await Until(() => window.GetVisualDescendants().OfType<WindowsAccessEditor>().Any(), ct);
            var editor = Assert.Single(window.GetVisualDescendants().OfType<WindowsAccessEditor>());
            editor.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == "Add an explicit entry").IsChecked = true;
            editor.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "New Windows entry SID").Text = "S-1-5-32-546";
            // An unavailable Apply says why, where the keyboard and a screen reader reach it.
            await Until(() => TextPresent(window, "Choose at least one right, or remove the entry explicitly."), ct);
            Assert.False(FindButton(window, "Apply")!.IsEnabled);
            var mask = editor.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Windows access mask");
            mask.Text = "0x00120089";
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);

            // Enter on a rule option does not Apply.
            var option = editor.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == "Write attributes");
            option.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Task.Delay(300, ct);
            Assert.Empty(services.Jobs.Jobs);
            Assert.Equal("0x00120089", mask.Text);

            // Enter in a field with a valid draft applies once; the editor stays open with the result, and a repeated
            // Enter submits nothing more.
            mask.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Until(() => TextPresent(window, "Verified changed"), ct);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Task.Delay(300, ct);
            Assert.Single(services.Jobs.Jobs);
            Assert.Single(window.GetVisualDescendants().OfType<WindowsAccessEditor>());
            Assert.False(FindButton(window, "Apply")!.IsEnabled);
            Assert.NotNull(FindButton(window, "Close"));
            Assert.NotNull(FindButton(window, "Review current state…"));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Access_editor_adds_one_named_rule_and_keeps_the_mask_and_other_rules()
    {
        RequireExt4();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
            RunAclTool("setfacl", "-m", "u:12345:r--,g:12346:-w-,m::r--", Path.Combine(folder, "a.txt")));
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3, ct);
            Assert.True(listing.FocusName("a.txt"));
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Edit access ACL…") is not null, ct);
            Click(FindButton(window, "Edit access ACL…")!);
            await Until(() => window.GetVisualDescendants().OfType<PosixAccessEditor>().Any(), ct);
            var editor = Assert.Single(window.GetVisualDescendants().OfType<PosixAccessEditor>());
            var principal = editor.GetVisualDescendants().OfType<TextBox>().Single(field => AutomationProperties.GetName(field) == "New ACL account ID");
            var write = editor.GetVisualDescendants().OfType<CheckBox>().Single(box => AutomationProperties.GetName(box) == "New entry write");
            principal.Text = "4294967295";
            Assert.Contains("4294967294", AutomationProperties.GetHelpText(principal));
            Assert.False(FindButton(window, "Add entry")!.IsEnabled);
            principal.Text = "12347";
            write.IsChecked = true;
            principal.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Empty(services.Jobs.Jobs);
            var preview = window.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationProperties.GetName(list) == "Reviewed changes");
            Assert.Contains("Add User 12347 · configured -w- · effective ---", preview.Items.OfType<string>());
            Assert.DoesNotContain(preview.Items.OfType<string>(), row => row.Contains("Group-class limit", StringComparison.Ordinal));
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            var apply = FindButton(window, "Apply")!;
            Click(apply);
            Click(apply);
            await Until(() => TextPresent(window, "Verified changed"), ct);

            Assert.True(vm.Dialogs.IsOpen);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(JobKind.Permissions, job.Kind);
            Assert.Equal(PermissionSections.Access, Assert.Single(Assert.Single(job.PermissionResults).Components).Section);
            string native = RunAclTool("getfacl", "-ncp", Path.Combine(root, "files", "a.txt"));
            Assert.Contains("user:12345:r--", native);
            Assert.Contains("user:12347:-w-", native);
            Assert.Contains("group:12346:-w-", native);
            Assert.Contains("mask::r--", native);
            Assert.DoesNotContain("12347", RunAclTool("getfacl", "-ncp", Path.Combine(root, "files", "b.txt")));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Linux_current_process_access_checks_the_selected_file_without_submitting_a_job()
    {
        RequireExt4();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(
            permissionService: new LinuxPermissionService(qualifyAccessChecks: true),
            prepareFiles: folder => File.SetUnixFileMode(Path.Combine(folder, "a.txt"), UnixFileMode.UserRead));
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3, ct);
            Assert.True(listing.FocusName("a.txt"));
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Check current process access…") is not null, ct);
            Click(FindButton(window, "Check current process access…")!);
            await Until(() => TextPresent(window, "Read content · Allowed by the current access check"), ct);
            Assert.True(TextPresent(window, "Write content · Denied by the current access check"));
            Assert.True(TextPresent(window, "Execute permission · Denied by the current access check"));
            Assert.True(TextPresent(window, "Effective UID "));
            Assert.True(TextPresent(window, "Supplementary groups"));
            Assert.Empty(services.Jobs.Jobs);
            Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(Path.Combine(root, "files", "a.txt")));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Posix_estimate_shows_a_masked_named_user_and_separately_handles_incomplete_groups()
    {
        RequireExt4();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(
            permissionService: new LinuxPermissionService(qualifyAccessChecks: true),
            prepareFiles: folder => RunAclTool("setfacl", "-m", "u:12345:r--,m::---,o::r--", Path.Combine(folder, "a.txt")));
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3, ct);
            Assert.True(listing.FocusName("a.txt"));
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Estimate POSIX ACL access…") is not null, ct);
            Click(FindButton(window, "Estimate POSIX ACL access…")!);
            await Until(() => window.GetVisualDescendants().OfType<PosixAccessEstimateView>().Any(), ct);
            var fields = window.GetVisualDescendants().OfType<TextBox>().ToArray();
            var uid = Assert.Single(fields, field => AutomationProperties.GetName(field) == "User ID");
            var gid = Assert.Single(fields, field => AutomationProperties.GetName(field) == "Effective group ID (optional)");
            var complete = window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == "I supplied every supplementary group");
            uid.Text = "12345";
            Assert.True(TextPresent(window, "Read content · ACL denies · named user entry and mask"));
            uid.Text = "12346";
            gid.Text = "12347";
            Assert.True(TextPresent(window, "Read content · ACL result unknown · group membership is incomplete"));
            complete.IsChecked = true;
            Assert.True(TextPresent(window, "Read content · ACL grants · others entry"));
            Assert.True(TextPresent(window, "Actual access is unknown"));
            Assert.Empty(services.Jobs.Jobs);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Linux_protection_editor_reviews_order_and_native_history_refuses_retained_capabilities()
    {
        RequireExt4();
        if (Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_IMMUTABLE_CAP") != "1")
            Assert.Skip("Requires non-root CAP_LINUX_IMMUTABLE in the private fixture.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(
            permissionService: new LinuxPermissionService(qualifyProtectionWrites: true),
            prepareFiles: folder =>
            {
                var file = Path.Combine(folder, "a.txt");
                File.SetUnixFileMode(file, (UnixFileMode)0x180);
                RunAclTool("chattr", "+di", file);
            });
        string path = Path.Combine(root, "files", "a.txt");
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3, ct);
            Assert.True(listing.FocusName("a.txt"));
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Edit file protection…")?.IsEnabled == true &&
                TextPresent(window, "Immutable") && TextPresent(window, "Inode flags"), ct);
            Click(FindButton(window, "Edit file protection…")!);
            await Until(() => window.GetVisualDescendants().OfType<UnixPermissionModeEditor>().Any(), ct);
            var mode = window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Permissions as octal");
            mode.Text = "400";
            Assert.False(FindButton(window, "Apply")!.IsEnabled);
            Assert.True(TextPresent(window, "explicitly clearing"));
            var flags = window.GetVisualDescendants().OfType<CheckBox>().ToArray();
            Assert.Single(flags, box => box.Content as string == "Immutable").IsChecked = false;
            Assert.Single(flags, box => box.Content as string == "Append only").IsChecked = true;
            var preview = window.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationProperties.GetName(list) == "Reviewed changes");
            string[] rows = preview.Items.OfType<string>().ToArray();
            Assert.StartsWith("Clear immutable protection", rows[0]);
            Assert.Contains("Mode 600 → 400", rows[2]);
            Assert.Equal("Set append-only protection", rows[^1]);
            Assert.Empty(services.Jobs.Jobs);
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            Click(FindButton(window, "Apply")!);
            await Until(() => TextPresent(window, "Review object details in Operations."), ct);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(PermissionVerdict.Unattempted, Assert.Single(job.PermissionResults).Verdict);
            Assert.Contains("capabilities are retained", JobIssue.Summarize(job.Issues));
            string native = RunAclTool("lsattr", "-d", path).Split(' ', 2)[0];
            Assert.Contains('i', native);
            Assert.DoesNotContain('a', native);
            Assert.Contains('d', native);
            Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(path));
        }
        finally
        {
            RunAclTool("chattr", "-iad", path);
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Linux_group_editor_reviews_restriction_before_group_assignment()
    {
        RequireExt4();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(
            permissionService: new LinuxPermissionService(qualifyGroupWrites: true),
            prepareFiles: folder =>
            {
                var path = Directory.CreateDirectory(Path.Combine(folder, "directory")).FullName;
                File.SetUnixFileMode(path, (UnixFileMode)0x5f8);
            });
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 4, ct);
            Assert.True(listing.FocusName("directory"));
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Change group…") is not null, ct);
            Click(FindButton(window, "Change group…")!);
            ComboBox? groups = null;
            await Until(() => (groups = window.GetVisualDescendants().OfType<ComboBox>()
                .SingleOrDefault(box => AutomationProperties.GetName(box) == "Assignable group ID")) is not null, ct);
            groups!.SelectedIndex = 0;
            var group = Assert.IsType<string>(groups.SelectedItem);
            var checkboxes = window.GetVisualDescendants().OfType<CheckBox>().ToArray();
            Assert.Single(checkboxes, box => AutomationProperties.GetName(box) == "Group write").IsChecked = false;
            Assert.Single(checkboxes, box => AutomationProperties.GetName(box) == "Others execute").IsChecked = true;
            var preview = window.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationProperties.GetName(list) == "Reviewed changes");
            string[] stages = preview.Items.OfType<string>().Where(line => line.Contains("Mode ", StringComparison.Ordinal) || line.StartsWith("Change group ID")).ToArray();
            Assert.Collection(stages,
                line => Assert.StartsWith("Before changing group · Mode ", line),
                line => Assert.EndsWith($"→ {group}", line),
                line => Assert.StartsWith("After changing group · Mode ", line));
            Assert.Empty(services.Jobs.Jobs);
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            Click(FindButton(window, "Apply")!);
            await Until(() => TextPresent(window, "Verified changed"), ct);
            var result = Assert.Single(Assert.Single(services.Jobs.Jobs).PermissionResults);
            Assert.Equal(PermissionVerdict.Verified, result.Verdict);
            string path = Path.Combine(root, "files", "directory");
            Assert.Equal(group, RunAclTool("stat", "-c", "%g", path).Trim());
            Assert.Equal((UnixFileMode)0x5e9, File.GetUnixFileMode(path));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Directory_default_editor_reviews_removal_separately_from_current_access()
    {
        RequireExt4();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(
            permissionService: new LinuxPermissionService(qualifyDefaultWrites: true),
            prepareFiles: folder =>
            {
                var path = Directory.CreateDirectory(Path.Combine(folder, "directory")).FullName;
                File.SetUnixFileMode(path, (UnixFileMode)0x1c0);
                RunAclTool("setfacl", "-m", "d:u:12345:r--", path);
                File.WriteAllText(Path.Combine(path, "existing"), "fixture");
            });
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 4, ct);
            Assert.True(listing.FocusName("directory"));
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Edit default ACL…") is not null, ct);
            Click(FindButton(window, "Edit default ACL…")!);
            await Until(() => FindButton(window, "Remove the default ACL") is not null, ct);
            Assert.True(TextPresent(window, "Defaults for new children of the selected directory"));
            Click(FindButton(window, "Remove the default ACL")!);
            var preview = window.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationProperties.GetName(list) == "Reviewed changes");
            Assert.Contains("Remove User 12345 · configured r-- · effective r--", preview.Items.OfType<string>());
            Assert.Empty(services.Jobs.Jobs);
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            Click(FindButton(window, "Apply")!);
            await Until(() => TextPresent(window, "Verified changed"), ct);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(PermissionSections.Defaults, Assert.Single(Assert.Single(job.PermissionResults).Components).Section);
            string path = Path.Combine(root, "files", "directory");
            Assert.Equal((UnixFileMode)0x1c0, File.GetUnixFileMode(path));
            Assert.DoesNotContain("default:", RunAclTool("getfacl", "-ncp", path));
            Assert.Contains("user:12345:r--", RunAclTool("getfacl", "-ncp", Path.Combine(path, "existing")));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mac_folder_acl_tree_reviews_explicit_rules_and_requires_approval(bool reset)
    {
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess || Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT") is not { } volume ||
            !Path.GetTempPath().StartsWith(Path.TrimEndingDirectorySeparator(volume) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            Assert.Skip("Requires the ordinary macOS disposable permissions volume.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            string tree = Directory.CreateDirectory(Path.Combine(folder, "acl-tree"), (UnixFileMode)0x1c0).FullName;
            string child = Path.Combine(tree, "child");
            File.WriteAllText(child, "fixture");
            RunAclTool("chmod", "+a", "everyone allow readsecurity", child);
            RunAclTool("chmod", "+a", "everyone allow readattr,file_inherit", tree);
        }, permissionService: new DarwinPermissionService(qualifyParentWrites: true, qualifyAclWrites: true, qualifyAclTrees: true));
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("acl-tree"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            string command = reset ? "Reset descendant access ACLs…" : "Refresh inherited access rules…";
            await Until(() => FindButton(window, command)?.IsEnabled == true, ct);
            Click(FindButton(window, command)!);
            await Until(() => FindButton(window, "Capture ACL review")?.IsEnabled == true, ct);
            Click(FindButton(window, "Capture ACL review")!);
            string apply = reset ? "Reset captured access ACLs" : "Refresh captured inherited rules";
            ListBox? ruleList = null;
            await Until(() => FindButton(window, apply) is not null && (ruleList = window.GetVisualDescendants().OfType<ListBox>()
                .SingleOrDefault(list => AutomationProperties.GetName(list) == "Ordered ACL changes for the selected descendant"))?.ItemCount > 0, ct);
            var rules = ruleList!.Items.OfType<string>().ToArray();
            Assert.Equal(reset, rules.Any(line => line.StartsWith("Remove explicit", StringComparison.Ordinal)));
            Assert.Contains(rules, line => line.StartsWith("After ·", StringComparison.Ordinal) && line.Contains("inherited", StringComparison.OrdinalIgnoreCase));
            Assert.False(FindButton(window, apply)!.IsEnabled);
            Assert.Empty(services.Jobs.Jobs);
            window.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content,
                "Approve the reviewed ACL changes and any access they grant")).IsChecked = true;
            await Until(() => FindButton(window, apply)?.IsEnabled == true, ct);
            Click(FindButton(window, apply)!);
            await Until(() => services.Jobs.Jobs.SingleOrDefault()?.State.IsFinished() == true, ct);
            var job = Assert.Single(services.Jobs.Jobs);
            Assert.Equal(reset ? PermissionScope.ResetTree : PermissionScope.ReviewedTree, job.PermissionReviewScope);
            Assert.Equal(PermissionVerdict.Verified, Assert.Single(job.PermissionResults).Verdict);
            var listingAcl = RunAclTool("ls", "-led", Path.Combine(root, "files", "acl-tree", "child"));
            Assert.Contains("inherited allow readattr", listingAcl);
            Assert.Equal(!reset, listingAcl.Contains("allow readsecurity", StringComparison.Ordinal));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData("inspector")]
    [InlineData("attributes")]
    [InlineData("cancel")]
    public async Task Mac_folder_tree_reviews_the_captured_modes_and_requires_set_id_consent(string entryPoint)
    {
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess || Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT") is not { } volume ||
            !Path.GetTempPath().StartsWith(Path.TrimEndingDirectorySeparator(volume) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            Assert.Skip("Requires the ordinary macOS disposable permissions volume.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            string tree = Directory.CreateDirectory(Path.Combine(folder, "reviewed-tree"), (UnixFileMode)0x1c8).FullName;
            File.SetUnixFileMode(tree, (UnixFileMode)0x1c8);
            string file = Path.Combine(tree, "executable");
            File.WriteAllText(file, "fixture");
            File.SetUnixFileMode(file, (UnixFileMode)0x9c0);
            RunAclTool("chmod", "+a#", "0", "everyone allow readattr", file);
        }, permissionService: new DarwinPermissionService(qualifyParentWrites: true, qualifyDescriptorWrites: true, qualifyModeTrees: true));
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("reviewed-tree"), ct);
            string reviewAction = "Capture review";
            if (entryPoint == "attributes")
            {
                listing.MarkItems([listing.GetItemRef(listing.FocusedStoreIndex)], [0], true);
                vm.Execute(CommandIds.Attributes);
                CheckBox? treeScope = null;
                await Until(() => (treeScope = window.GetVisualDescendants().OfType<CheckBox>().SingleOrDefault(box =>
                    Equals(box.Content, "Also change mode inside selected folders")))?.IsEffectivelyVisible == true, ct);
                treeScope!.IsChecked = true;
                reviewAction = "Apply";
            }
            else
            {
                vm.Execute(CommandIds.PermissionsOwnership);
                await Until(() => FindButton(window, "Review folder tree mode…")?.IsEnabled == true, ct);
                Click(FindButton(window, "Review folder tree mode…")!);
            }
            await Until(() => window.GetVisualDescendants().OfType<UnixPermissionModeEditor>().Any(), ct);
            var octal = window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Permissions as octal");
            octal.Text = "0710";
            await Until(() => FindButton(window, reviewAction)?.IsEnabled == true, ct);
            Click(FindButton(window, reviewAction)!);
            await Until(() => FindButton(window, "Apply to captured objects") is not null && TextPresent(window, "Later arrivals are outside this review"), ct);
            Assert.Empty(services.Jobs.Jobs);
            Assert.False(FindButton(window, "Apply to captured objects")!.IsEnabled);
            string dir = Path.Combine(root, "files", "reviewed-tree");
            string file = Path.Combine(dir, "executable"), late = Path.Combine(dir, "late");
            File.WriteAllText(late, "later");
            File.SetUnixFileMode(late, (UnixFileMode)0x180);
            if (entryPoint == "cancel")
            {
                Click(FindButton(window, "Cancel")!);
                await Until(() => FindButton(window, "Apply to captured objects") is null, ct);
                Assert.Empty(services.Jobs.Jobs);
                Assert.Equal((UnixFileMode)0x9c0, File.GetUnixFileMode(file));
            }
            else
            {
                window.GetVisualDescendants().OfType<CheckBox>().Last(box => Equals(box.Content, "Accept clearing the reviewed set-ID bits")).IsChecked = true;
                await Until(() => FindButton(window, "Apply to captured objects")?.IsEnabled == true, ct);
                Click(FindButton(window, "Apply to captured objects")!);
                await Until(() => services.Jobs.Jobs.SingleOrDefault()?.State.IsFinished() == true, ct);
                var job = Assert.Single(services.Jobs.Jobs);
                Assert.Equal(PermissionScope.ReviewedTree, job.PermissionReviewScope);
                Assert.Equal(2, job.PermissionResults.Count);
                Assert.Contains(job.PermissionResults, result => result.Verdict == PermissionVerdict.AppliedWithFolderDrift);
                Assert.Contains(job.PermissionResults, result => result.Verdict == PermissionVerdict.AlreadyMatching);
                Assert.Equal((UnixFileMode)0x1c8, File.GetUnixFileMode(file));
            }
            Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(late));
            Assert.Contains("everyone allow readattr", RunAclTool("ls", "-led", file));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ext4_folder_mode_editor_requires_captured_set_id_consent_and_leaves_later_arrivals_alone(bool apply)
    {
        RequireExt4();
        const UnixFileMode originalFolder = (UnixFileMode)0x1c0;
        const UnixFileMode documentMode = (UnixFileMode)0x180;
        const UnixFileMode originalExecutable = (UnixFileMode)0x9c0;
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            string tree = Path.Combine(folder, "reviewed-tree");
            Directory.CreateDirectory(tree, originalFolder);
            File.WriteAllText(Path.Combine(tree, "document"), "document");
            File.SetUnixFileMode(Path.Combine(tree, "document"), documentMode);
            File.WriteAllText(Path.Combine(tree, "executable"), "executable");
            RunAclTool("setfacl", "-m", "u:12345:r-x,m::---", Path.Combine(tree, "executable"));
            File.SetUnixFileMode(Path.Combine(tree, "executable"), originalExecutable);
        }, permissionService: new LinuxPermissionService(qualifyModeTrees: true));
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            string tree = Path.Combine(root, "files", "reviewed-tree");
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("reviewed-tree"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Review folder tree mode…") is { IsEnabled: true }, ct);
            Click(FindButton(window, "Review folder tree mode…")!);
            await Until(() => window.GetVisualDescendants().OfType<UnixPermissionModeEditor>().Any(), ct);
            var field = window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Permissions as octal");
            field.Text = "710";
            await Until(() => FindButton(window, "Capture review")?.IsEnabled == true, ct);
            Click(FindButton(window, "Capture review")!);
            await Until(() => FindButton(window, "Apply to captured objects") is not null, ct);
            Assert.Empty(services.Jobs.Jobs);
            await Until(() => TextPresent(window, "Later arrivals are outside this review") && TextPresent(window, "group-class limit"), ct);
            Assert.False(FindButton(window, "Apply to captured objects")!.IsEnabled);

            string late = Path.Combine(tree, "late");
            File.WriteAllText(late, "late arrival");
            File.SetUnixFileMode(late, documentMode);
            if (apply)
            {
                var accept = window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == "Accept clearing the reviewed set-ID bits");
                accept.IsChecked = true;
                await Until(() => FindButton(window, "Apply to captured objects")?.IsEnabled == true, ct);
                Click(FindButton(window, "Apply to captured objects")!);
                await Until(() => services.Jobs.Jobs.SingleOrDefault()?.State.IsFinished() == true, ct);
                var job = Assert.Single(services.Jobs.Jobs);
                Assert.Equal(PermissionScope.ReviewedTree, job.PermissionReviewScope);
                Assert.Equal(3, job.PermissionResults.Count);
                Assert.Equal((UnixFileMode)0x1c8, File.GetUnixFileMode(Path.Combine(tree, "executable")));
                Assert.Contains("mask::--x", RunAclTool("getfacl", "-ncp", Path.Combine(tree, "executable")));
                Assert.Equal((UnixFileMode)0x1c8, File.GetUnixFileMode(tree));
            }
            else
            {
                Click(FindButton(window, "Cancel")!);
                await Until(() => FindButton(window, "Apply to captured objects") is null, ct);
                Assert.Empty(services.Jobs.Jobs);
                Assert.Equal(originalExecutable, File.GetUnixFileMode(Path.Combine(tree, "executable")));
            }
            Assert.Equal(documentMode, File.GetUnixFileMode(late));
            Assert.Equal(documentMode, File.GetUnixFileMode(Path.Combine(tree, "document")));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Btrfs_noacl_inspector_offers_mode_edit_and_explains_disabled_acls()
    {
        string volumes = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSION_VOLUMES") ?? string.Empty;
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess || !Path.IsPathFullyQualified(volumes) ||
            Path.TrimEndingDirectorySeparator(Path.GetTempPath()) != Path.Join(volumes, "btrfs", "fixture"))
            Assert.Skip("Requires an ordinary user and a Btrfs noacl fixture for the workspace.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: files =>
            Directory.CreateDirectory(Path.Join(files, "mode-folder"), (UnixFileMode)0x1f8),
            permissionService: new LinuxPermissionService(qualifyDefaultWrites: true, qualifyAdditionalFileSystems: true));
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("mode-folder"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Edit mode…") is { IsEnabled: true } && TextPresent(window, "POSIX ACLs disabled (noacl)"), ct);
            Assert.True(TextPresent(window, "Unix mode"));
            Assert.Null(FindButton(window, "Edit access ACL…"));
            Assert.Null(FindButton(window, "Edit default ACL…"));
            Click(FindButton(window, "Edit mode…")!);
            await Until(() => window.GetVisualDescendants().OfType<UnixPermissionModeEditor>().Any(), ct);
            var octal = window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Permissions as octal");
            octal.Text = "700";
            await Until(() => FindButton(window, "Apply") is { IsEnabled: true }, ct);
            Click(FindButton(window, "Apply")!);
            await Until(() => services.Jobs.Jobs.SingleOrDefault()?.State.IsFinished() == true, ct);
            Assert.Equal((UnixFileMode)0x1c0, File.GetUnixFileMode(Path.Join(root, "files", "mode-folder")));
            await Until(() => TextPresent(window, "Verified changed"), ct);
            Assert.Equal(PermissionVerdict.Verified, Assert.Single(Assert.Single(services.Jobs.Jobs).PermissionResults).Verdict);
            string journals = Path.Join(services.Paths.JournalDirectory, PermissionJournalNames.Directory);
            Assert.StartsWith(root + Path.DirectorySeparatorChar, journals, StringComparison.Ordinal);
            var records = JobJournal.ReadAll(Assert.Single(Directory.GetFiles(journals, "*.fcj")));
            Assert.Contains(records, record => record.Type == "permission-intent");
            Assert.Contains(records, record => record.Type == "permission-outcome");
            Assert.Empty(services.ScanPermissionHistory(ct));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Ext4_descendant_acl_reset_displays_removed_entries_and_requires_expansion_approval()
    {
        RequireExt4();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
        {
            string tree = Path.Combine(folder, "reset-source");
            Directory.CreateDirectory(tree, (UnixFileMode)0x1c0);
            string file = Path.Combine(tree, "document");
            File.WriteAllText(file, "owned fixture");
            File.SetUnixFileMode(file, (UnixFileMode)0x180);
            RunAclTool("setfacl", "-n", "-m", "u:23456:r--,m::r--", file);
            RunAclTool("setfacl", "-n", "-m", "d:u::rwx,d:u:12345:r-x,d:g::r-x,d:m::r-x,d:o::---", tree);
        }, permissionService: new LinuxPermissionService(qualifyAclResets: true));
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("reset-source"), ct);
            vm.Execute(CommandIds.PermissionsOwnership);
            await Until(() => FindButton(window, "Reset descendant access ACLs…") is { IsEnabled: true }, ct);
            Click(FindButton(window, "Reset descendant access ACLs…")!);
            await Until(() => FindButton(window, "Capture reset review") is { IsEnabled: true }, ct);
            Click(FindButton(window, "Capture reset review")!);
            await Until(() => FindButton(window, "Reset captured access ACLs") is not null, ct);
            Assert.Empty(services.Jobs.Jobs);
            Assert.False(FindButton(window, "Reset captured access ACLs")!.IsEnabled);
            ListBox? Rights() => window.GetVisualDescendants().OfType<ListBox>().SingleOrDefault(box =>
                AutomationProperties.GetName(box) == "Access ACL changes for the selected descendant");
            await Until(() => Rights()?.ItemsSource is string[], ct);
            var rights = Rights()!;
            Assert.Contains(Assert.IsType<string[]>(rights.ItemsSource), line => line.StartsWith("Remove User 23456", StringComparison.Ordinal));
            var accept = window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == "Approve the reviewed rule replacements and any access they grant");
            accept.IsChecked = true;
            await Until(() => FindButton(window, "Reset captured access ACLs")?.IsEnabled == true, ct);
            Click(FindButton(window, "Reset captured access ACLs")!);
            await Until(() => services.Jobs.Jobs.SingleOrDefault()?.State.IsFinished() == true, ct);
            Assert.Equal(PermissionScope.ResetTree, Assert.Single(services.Jobs.Jobs).PermissionReviewScope);
            string file = Path.Combine(root, "files", "reset-source", "document");
            var current = RunAclTool("getfacl", "-ncp", file);
            Assert.Contains("user:12345:r-x", current);
            Assert.DoesNotContain("user:23456:", current);
            Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(file));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Item_menu_mode_editor_keeps_the_captured_subject_and_refuses_a_replacement(bool replace)
    {
        RequireExt4();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(prepareFiles: folder =>
            File.SetUnixFileMode(Path.Combine(folder, "a.txt"), (UnixFileMode)0x1a4));
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3, ct);
            Assert.True(listing.FocusName("a.txt"));
            var menu = ContextMenuFactory.Build(vm);
            var action = Assert.Single(menu.Items.OfType<MenuItem>(), item => item.Header as string == "Permissions and ownership…");
            Assert.True(listing.FocusName("b.txt"));
            action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => FindButton(window, "Edit mode…") is not null, ct);
            Click(FindButton(window, "Edit mode…")!);
            await Until(() => window.GetVisualDescendants().OfType<UnixPermissionModeEditor>().Any(), ct);
            var field = window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Permissions as octal");
            Assert.Equal("644", field.Text);
            field.Text = "600";
            await Until(() => FindButton(window, "Apply")?.IsEnabled == true, ct);
            var file = Path.Combine(root, "files", "a.txt");
            var other = Path.Combine(root, "files", "b.txt");
            var unaffected = File.GetUnixFileMode(other);
            var moved = file + ".old";
            if (replace)
            {
                var time = File.GetLastWriteTimeUtc(file);
                File.Move(file, moved);
                File.WriteAllText(file, "alpha");
                File.SetUnixFileMode(file, (UnixFileMode)0x1a4);
                File.SetLastWriteTimeUtc(file, time);
            }
            Click(FindButton(window, "Apply")!);
            if (replace)
            {
                await Until(() => TextPresent(window, "namespace changed"), ct);
                Assert.Empty(services.Jobs.Jobs);
                Assert.Equal((UnixFileMode)0x1a4, File.GetUnixFileMode(file));
                Assert.Equal((UnixFileMode)0x1a4, File.GetUnixFileMode(moved));
            }
            else
            {
                await Until(() => TextPresent(window, "Verified changed"), ct);
                Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(file));
                Assert.Equal(PermissionVerdict.Verified, Assert.Single(Assert.Single(services.Jobs.Jobs).PermissionResults).Verdict);
            }
            Assert.Equal(unaffected, File.GetUnixFileMode(other));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Item_menu_inspects_the_captured_object_after_focus_changes()
    {
        if (Environment.IsPrivilegedProcess) Assert.Skip("The permissions inspector requires an ordinary user process.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 3); i++)
                await Task.Delay(20, ct);
            Assert.True(listing.FocusName("a.txt"));
            var menu = ContextMenuFactory.Build(vm);
            var action = Assert.Single(menu.Items.OfType<MenuItem>(), item => (string?)item.Header == "Permissions and ownership…");
            Assert.True(listing.FocusName("b.txt"));
            action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var dialogs = Assert.IsType<OverlayDialogService>(vm.Dialogs);
            for (int i = 0; i < 250 && !window.GetVisualDescendants().OfType<PermissionInspectorView>().Any(); i++)
                await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen);
            var inspector = Assert.Single(window.GetVisualDescendants().OfType<PermissionInspectorView>());
            Assert.Contains(inspector.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.EndsWith("a.txt", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(inspector.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.EndsWith("b.txt", StringComparison.Ordinal) == true);
            if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_EXT4") == "1")
                Assert.Contains(inspector.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("ext4 · POSIX ACL") == true);
            Assert.Empty(services.Jobs.Jobs);
            dialogs.CancelAll();
            Assert.True(listing.TryGetFocused(out var focused));
            Assert.Equal("b.txt", focused.Name);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static void RequireExt4()
    {
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess ||
            Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_EXT4") != "1")
            Assert.Skip("Requires the ordinary Linux ext4 permissions fixture.");
    }

    private static Button? FindButton(Window window, string text) =>
        window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Content as string == text);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static bool TextPresent(Window window, string text) => window.GetVisualDescendants().OfType<TextBlock>()
        .Any(label => label.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);

    private static async Task Until(Func<bool> condition, CancellationToken ct)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20, ct);
        Assert.True(condition());
    }

    private static string RunAclTool(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000) && process.ExitCode == 0, error);
        return output;
    }
}
