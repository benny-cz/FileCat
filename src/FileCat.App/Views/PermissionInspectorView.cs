using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Core.State;

namespace FileCat.App.Views;

internal sealed class PermissionInspectorView : StackPanel
{
    private readonly StackPanel _subject = new() { Spacing = 6 };
    private readonly Func<string, Task>? _copy;
    private readonly string?[]? _editRefusals;
    private readonly PermissionPrincipalNames? _principalNames;
    private CancellationTokenSource? _lookup;

    internal PermissionInspectorView((string Name, PermissionSnapshot Snapshot)[] subjects, Func<string, Task>? copy,
        string?[]? editRefusals = null, PermissionPrincipalNames? principalNames = null)
    {
        _copy = copy;
        _editRefusals = editRefusals;
        _principalNames = principalNames;
        Spacing = 10;
        if (subjects.Length > 1)
        {
            Children.Add(Label(Formatters.Plural(subjects.Length, "selected object", "selected objects")));
            var target = new ComboBox { ItemsSource = subjects.Select(item => Formatters.SafeName(item.Name)).ToArray(), SelectedIndex = 0 };
            AutomationProperties.SetName(target, "Permission subject");
            target.SelectionChanged += (_, _) =>
            {
                if (target.SelectedIndex < 0) return;
                SelectedSubject = target.SelectedIndex;
                Render(subjects[SelectedSubject].Snapshot);
            };
            Children.Add(target);
        }
        Children.Add(_subject);
        Render(subjects[0].Snapshot);
    }

    internal int SelectedSubject { get; private set; }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _lookup?.Cancel();
        base.OnDetachedFromVisualTree(e);
    }

    private void Render(PermissionSnapshot snapshot)
    {
        _lookup?.Cancel();
        _subject.Children.Clear();
        AddValue(snapshot.Subject.DisplayPath, "Copy exact path");
        if (snapshot.BindingProblem is { } problem)
        {
            _subject.Children.Add(Label(problem));
            if (snapshot.Subject.LastMountObservation is { } observedAt)
            {
                string source = snapshot.Subject.Authority.Kind == PermissionAuthorityKind.MountedServer ? "mounted client" : "filesystem";
                _subject.Children.Add(Label($"Last observation from the {source} at {observedAt.ToLocalTime():g}."));
            }
            return;
        }
        if (snapshot.Ftp is { } ftp)
        {
            _subject.Children.Add(Label($"{RemoteProtocols.Describe(ftp.Protocol)} · {ftp.Source} · Observed at the server path {ftp.ObservedAt.ToLocalTime():g}"));
            AddPrincipal("Server owner ID", snapshot.Owner);
            AddPrincipal("Server group ID", snapshot.Group);
            if (ftp.OwnerName is { } owner) AddValue(owner, "Copy exact server owner name", "Server owner name · ");
            if (ftp.GroupName is { } group) AddValue(group, "Copy exact server group name", "Server group name · ");
            if (snapshot.Mode.Value is ushort reportedMode)
                _subject.Children.Add(Label($"Server mode · {UnixPermissions.Octal((UnixFileMode)reportedMode)} · {UnixPermissions.Format((UnixFileMode)reportedMode)}"));
            else _subject.Children.Add(Label("Server mode · " + ObservationText(snapshot.Mode.Status, snapshot.Mode.Reason)));
            _subject.Children.Add(Label("Reported FTP action codes · " + (ftp.TransferRights switch
            {
                null => "Unreported",
                "" => "None",
                { } rights => rights
            })));
            if (ftp.Unique is { } unique) AddValue(unique, "Copy exact server identifier", "Server identifier · ");
            _subject.Children.Add(Label("Exact-item changes are unavailable over FTP and FTPS."));
            return;
        }
        if (snapshot.Sftp is { } sftp)
        {
            _subject.Children.Add(Label($"{sftp.ObjectType} · SFTP LSTAT · Observed at the server path {sftp.ObservedAt.ToLocalTime():g}"));
            if (sftp.Connection is { } connection) _subject.Children.Add(Label("Connection · " + Formatters.SafeName(connection)));
            AddPrincipal("Server owner ID", snapshot.Owner);
            AddPrincipal("Server group ID", snapshot.Group);
            if (sftp.UserName is { } ownerName) AddValue(ownerName, "Copy exact server owner name", "Server owner name · ");
            if (sftp.GroupName is { } groupName) AddValue(groupName, "Copy exact server group name", "Server group name · ");
            if (snapshot.Mode.Value is ushort serverMode)
                _subject.Children.Add(Label($"Server mode · {UnixPermissions.Octal((UnixFileMode)serverMode)} · {UnixPermissions.Format((UnixFileMode)serverMode)}"));
            else _subject.Children.Add(Label("Server mode · " + ObservationText(snapshot.Mode.Status, snapshot.Mode.Reason)));
            if (sftp.ChangeRefusal is { } serverRefusal) _subject.Children.Add(Label(serverRefusal));
            else if (sftp.ModeChangeRefusal is { } modeRefusal) _subject.Children.Add(Label(modeRefusal));
            return;
        }
        if (snapshot.Binding is { } binding)
        {
            string kind = binding.Identity.Kind == PermissionObjectKind.VolumeRoot ? "Volume root" : binding.Identity.Kind.ToString();
            _subject.Children.Add(Label($"{kind} · {binding.FileSystem} · {ModelLabel(snapshot.Model)}"));
            if (snapshot.Mount is null) _subject.Children.Add(Label("Authority: " + Formatters.SafeName(binding.Identity.Authority.Id)));
        }
        if (snapshot.Mount is { } mount)
        {
            _subject.Children.Add(Label(mount.Presentation switch
            {
                PermissionMountPresentation.Synthetic => "Mount-derived permissions",
                PermissionMountPresentation.Translated => "Permissions presented by the mounted client",
                PermissionMountPresentation.Native => "Permissions reported by the filesystem",
                _ => "The mount's permission model is unidentified"
            }));
            if (mount.Source is { } source) _subject.Children.Add(Label("Mount source · " + Formatters.SafeName(source)));
            if (mount.Root != "/") _subject.Children.Add(Label("Filesystem root · " + Formatters.SafeName(mount.Root)));
            if (mount.Protocol is { } protocol) _subject.Children.Add(Label(protocol));
            if (mount.ClientPolicy is { } policyNote) _subject.Children.Add(Label(policyNote));
            if (mount.IsServerProjection)
                _subject.Children.Add(Label($"Read from the mounted client at {snapshot.Mode.ObservedAt.ToLocalTime():g}. The client may use cached metadata. Server reachability and freshness are unverified."));
            if (snapshot.Binding?.ReadOnly == true) _subject.Children.Add(Label("This mount is read-only."));
        }
        bool projectsOwnership = snapshot.Mount is not null || snapshot.VolumePolicy.Value is { HonorsOwnership: false };
        if (snapshot.VolumePolicy.Value is { } policy)
            _subject.Children.Add(Label(policy.HonorsOwnership ? "This volume honors file ownership." : "This volume ignores file ownership."));
        AddPrincipal(projectsOwnership ? "Presented owner" : "Owner", snapshot.Owner);
        AddPrincipal(projectsOwnership ? "Presented primary group" : "Primary group", snapshot.Group);
        if (snapshot.Mode.Value is ushort mode)
            _subject.Children.Add(Label($"{(snapshot.Mount is null ? "Mode" : "Presented mode")}: " +
                $"{UnixPermissions.Octal((UnixFileMode)mode)} · {UnixPermissions.Format((UnixFileMode)mode)}"));
        if (snapshot.Times?.Value?.Modified is { } modified)
            _subject.Children.Add(Label($"Modified: {modified.ToLocalTime():g}"));
        if (snapshot.Times?.Value?.Created is { } created)
            _subject.Children.Add(Label($"Created: {created.ToLocalTime():g}"));
        if (snapshot.Model == PermissionModel.DarwinAcl && snapshot.Flags.Value is { } flags)
        {
            if ((flags.Bits & MacMaterializationPolicy.DatalessFlag) != 0)
                _subject.Children.Add(Label("Dataless item. Showing metadata without downloading content."));
            if ((flags.Bits & DarwinPermissionService.UserLocked) != 0) _subject.Children.Add(Label("Finder Locked"));
            if ((flags.Bits & DarwinPermissionService.UserAppendOnly) != 0) _subject.Children.Add(Label("User append-only"));
            if ((flags.Bits & DarwinPermissionService.SystemImmutable) != 0) _subject.Children.Add(Label("System immutable · read only"));
            if ((flags.Bits & DarwinPermissionService.SystemAppendOnly) != 0) _subject.Children.Add(Label("System append-only · read only"));
            if ((flags.Bits & 0x8000) != 0) _subject.Children.Add(Label("Finder Hidden"));
            _subject.Children.Add(Label($"BSD flags · 0x{flags.Bits:X8}"));
        }
        else if (snapshot.Model is PermissionModel.PosixAcl or PermissionModel.UnixMode && snapshot.Flags.Status != PermissionReadStatus.Unloaded)
        {
            if (snapshot.Flags.Value is { } inode)
            {
                if ((inode.Bits & LinuxPermissionService.ImmutableInode) != 0) _subject.Children.Add(Label("Immutable"));
                if ((inode.Bits & LinuxPermissionService.AppendOnlyInode) != 0) _subject.Children.Add(Label("Append-only"));
                if (inode.KnownBits == uint.MaxValue) _subject.Children.Add(Label($"Inode flags · 0x{inode.Bits:X8}"));
            }
            if (snapshot.Flags.Reason is { } reason) _subject.Children.Add(Label(Formatters.SafeName(reason)));
        }
        bool directory = (snapshot.Binding?.Identity.Kind ?? snapshot.Subject.Kind) is PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot;
        AddAcl("Access now", snapshot.Access, snapshot.Subject.Authority, directory);
        if (snapshot.Binding?.Identity.Kind is PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot && snapshot.Model == PermissionModel.PosixAcl)
            AddAcl("Defaults for new children", snapshot.Defaults, snapshot.Subject.Authority);
        if (snapshot.Inheritance.Value is { } control)
            _subject.Children.Add(Label(control.IsProtected ? "Inheritance: protected" : "Inheritance: enabled"));
        if (snapshot.Model == PermissionModel.WindowsDescriptor && snapshot.Audit.Status != PermissionReadStatus.Unloaded)
            AddAcl("Auditing", snapshot.Audit, snapshot.Subject.Authority, directory);
        if (snapshot.Mount is { } observedMount)
            _subject.Children.Add(Label(observedMount.ChangeRefusal));
        else if (_editRefusals is null)
            _subject.Children.Add(Label(snapshot.Binding?.ReadOnly == true ? "This volume is read-only." : "This view shows the observed permissions."));
        else if (_editRefusals[SelectedSubject] is { } refusal)
            _subject.Children.Add(Label(refusal));
    }

    private void AddPrincipal(string label, PermissionObservation<Principal> principal)
    {
        if (principal.Value is { } value) AddValue(value.Id, "Copy exact " + label.ToLowerInvariant() + " ID", label + ": ");
        else _subject.Children.Add(Label(label + ": " + ObservationText(principal.Status, principal.Reason)));
    }

    private void AddAcl(string title, PermissionObservation<PermissionAclValue> observation, PermissionAuthority authority, bool directory = false)
    {
        _subject.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (observation.Value?.Acl is DarwinAcl darwin)
            _subject.Children.Add(Label($"Extended ACL flags · 0x{darwin.Flags:X8}"));
        if (observation.Value?.Acl is WindowsSacl { Present: true } sacl)
            _subject.Children.Add(Label($"System ACL flags · 0x{sacl.Control.Flags:X4}"));
        string[] entries = observation.Value?.Acl switch
        {
            WindowsDacl { Form: WindowsDaclForm.Null } => ["NULL DACL · unrestricted discretionary access"],
            WindowsDacl { Form: WindowsDaclForm.Absent } => ["DACL absent · unrestricted discretionary access"],
            WindowsDacl { Entries.IsEmpty: true } => ["Empty DACL · no discretionary grants"],
            WindowsDacl windows => windows.Entries.Select(WindowsRule).ToArray(),
            WindowsSacl { Present: false } => ["System ACL absent"],
            WindowsSacl { Revision: null } => ["NULL system ACL"],
            WindowsSacl { Entries.IsEmpty: true } => ["Empty system ACL"],
            WindowsSacl audit => audit.Entries.Select(WindowsAuditEditor.Describe).ToArray(),
            PosixAcl posix => PosixRules(posix),
            DarwinAcl { Entries.IsEmpty: true } => ["Empty extended ACL"],
            DarwinAcl acl => acl.Entries.Select(entry => DarwinRule(entry, directory)).ToArray(),
            OpaquePermissionAcl => ["This ACL uses a format FileCat cannot interpret."],
            _ => [ObservationText(observation.Status, observation.Reason)]
        };
        PermissionAcl? model = observation.Value?.Acl;
        if (entries.Length == 1)
        {
            _subject.Children.Add(Label(entries[0]));
            if (AclPrincipal(model, 0) is { } principal) AddAccountActions(authority, () => principal, null);
            return;
        }
        var list = new ListBox { ItemsSource = entries, MaxHeight = 260, MinHeight = 90,
            ItemTemplate = new FuncDataTemplate<string>((entry, _) => Label(entry ?? string.Empty)) };
        AutomationProperties.SetName(list, title);
        _subject.Children.Add(list);
        if (entries.Select((_, index) => AclPrincipal(model, index)).Any(principal => principal is not null))
            AddAccountActions(authority, () => AclPrincipal(model, list.SelectedIndex), list);
    }

    private static Principal? AclPrincipal(PermissionAcl? acl, int index) => acl switch
    {
        DarwinAcl darwin when (uint)index < (uint)darwin.Entries.Length => darwin.Entries[index].Principal,
        WindowsDacl windows when (uint)index < (uint)windows.Entries.Length => windows.Entries[index].Principal,
        WindowsSacl audit when (uint)index < (uint)audit.Entries.Length => audit.Entries[index].Principal,
        PosixAcl posix when (uint)index < (uint)posix.Entries.Length => posix.Entries[index].Principal,
        _ => null
    };

    private void AddAccountActions(PermissionAuthority authority, Func<Principal?> selected, ListBox? list)
    {
        if (_copy is null && (_principalNames is null || authority.Kind != PermissionAuthorityKind.Local)) return;
        var actions = new WrapPanel();
        var status = Label(string.Empty);
        status.IsVisible = false;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var copy = new Button { Content = "Copy exact account ID", IsVisible = _copy is not null };
        copy.Click += async (_, _) => { if (_copy is not null && selected() is { } principal) await _copy(principal.Id); };
        var lookup = new Button { Content = "Look up account", IsVisible = _principalNames is not null && authority.Kind == PermissionAuthorityKind.Local };
        CancellationTokenSource? activeForControls = null;
        lookup.Click += async (_, _) =>
        {
            if (!lookup.IsEnabled || _principalNames is null || selected() is not { } principal) return;
            _lookup?.Cancel();
            using var request = new CancellationTokenSource();
            _lookup = request;
            activeForControls = request;
            lookup.IsEnabled = false;
            status.Text = "Looking up the account…";
            status.IsVisible = true;
            try
            {
                var answer = await _principalNames.ResolveAsync(authority, principal, request.Token);
                if (request.IsCancellationRequested || selected() != principal) return;
                status.Text = answer.Status switch
                {
                    NameStatus.Resolved when answer.Name is { } name => $"{answer.Type ?? "Account"} · {Formatters.SafeName(name)}",
                    NameStatus.NotFound => "No account name was found for this ID.",
                    _ => answer.Reason is { } reason ? Formatters.SafeName(reason) : "The account name is unavailable."
                };
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            finally
            {
                if (ReferenceEquals(_lookup, request)) _lookup = null;
                if (ReferenceEquals(activeForControls, request))
                {
                    activeForControls = null;
                    if (request.IsCancellationRequested) status.IsVisible = false;
                    lookup.IsEnabled = selected().HasValue;
                }
            }
        };
        void UpdateSelection()
        {
            _lookup?.Cancel();
            copy.IsEnabled = lookup.IsEnabled = selected().HasValue;
            status.IsVisible = false;
        }
        if (list is not null) list.SelectionChanged += (_, _) => UpdateSelection();
        UpdateSelection();
        actions.Children.Add(copy);
        actions.Children.Add(lookup);
        _subject.Children.Add(actions);
        _subject.Children.Add(status);
    }

    private static string WindowsRule(WindowsAce entry)
    {
        string identity = entry.Principal is { } principal ? " · " + Formatters.SafeName(principal.Id) : string.Empty;
        string rights = entry.Mask is uint mask ? $" · mask 0x{mask:X8}" : string.Empty;
        string type = entry.Kind == WindowsAceKind.Opaque ? $"Opaque ACE 0x{entry.Type:X2}" : entry.Kind.ToString();
        var inheritance = new List<string> { (entry.Flags & 0x10) != 0 ? "inherited · source unknown" : "explicit" };
        if ((entry.Flags & 1) != 0) inheritance.Add("files inherit");
        if ((entry.Flags & 2) != 0) inheritance.Add("subfolders inherit");
        if ((entry.Flags & 4) != 0) inheritance.Add("limit to immediate children");
        if ((entry.Flags & 8) != 0) inheritance.Add("inherit only");
        return $"{type}{identity}{rights} · {string.Join(" · ", inheritance)} · flags 0x{entry.Flags:X2}";
    }

    private static string[] PosixRules(PosixAcl acl)
    {
        byte mask = PosixAccessDraft.Mask(acl.Entries);
        return acl.Entries.Select(entry => $"{PosixAccessEditor.PrincipalLabel(entry)} · {PosixAccessEditor.DescribeRights(entry, mask)}").ToArray();
    }

    internal static IEnumerable<(uint Bit, string Label)> DarwinRights(bool directory)
    {
        string[] labels = [string.Empty, directory ? "List directory" : "Read data", directory ? "Add file" : "Write data",
            directory ? "Search" : "Execute", "Delete", directory ? "Add subdirectory" : "Append data", "Delete child",
            "Read attributes", "Write attributes", "Read extended attributes", "Write extended attributes",
            "Read security", "Write security", "Change owner"];
        for (int bit = 1; bit < labels.Length; bit++) yield return (1u << bit, labels[bit]);
        yield return (1u << 20, "Synchronize");
    }

    internal static string DarwinRule(DarwinAce entry, bool directory)
    {
        var rights = DarwinRights(directory).Where(right => (entry.Rights & right.Bit) != 0).Select(right => right.Label).ToList();
        if ((entry.Rights & ~DarwinPermissionAcl.EditableRights) != 0 || rights.Count == 0) rights.Add($"Rights 0x{entry.Rights:X8}");
        var flags = new List<string>();
        flags.Add((entry.Flags & 0x10) != 0 ? "Inherited" : "Explicit");
        if ((entry.Flags & 0x20) != 0) flags.Add("File inherit");
        if ((entry.Flags & 0x40) != 0) flags.Add("Directory inherit");
        if ((entry.Flags & 0x80) != 0) flags.Add("Limit inheritance");
        if ((entry.Flags & 0x100) != 0) flags.Add("Inherit only");
        if (entry.Kind == DarwinAceKind.Opaque || (entry.Flags & ~0x1ffu) != 0) flags.Add($"Flags 0x{entry.Flags:X8}");
        return $"{entry.Kind} · {Formatters.SafeName(entry.Principal.Id)} · {string.Join(", ", rights)} · {string.Join(", ", flags)}";
    }

    private void AddValue(string value, string copyText, string prefix = "")
    {
        _subject.Children.Add(Label(prefix + Formatters.SafeName(value)));
        if (_copy is null) return;
        var button = new Button { Content = copyText };
        button.Click += async (_, _) => await _copy(value);
        _subject.Children.Add(button);
    }

    private static TextBlock Label(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
    private static string ModelLabel(PermissionModel model) => model switch
    {
        PermissionModel.WindowsDescriptor => "Windows security descriptor",
        PermissionModel.PosixAcl => "POSIX ACL",
        PermissionModel.UnixMode => "Unix mode",
        PermissionModel.DarwinAcl => "Darwin extended ACL",
        PermissionModel.Projected => "Mount observation",
        _ => model.ToString()
    };
    private static string ObservationText(PermissionReadStatus status, string? reason) => reason ?? status switch
    {
        PermissionReadStatus.Absent => "No extended ACL or separate value is present.",
        PermissionReadStatus.Denied => "Access was denied.",
        PermissionReadStatus.Unloaded => "This section is not loaded.",
        _ => status.ToString()
    };
}
