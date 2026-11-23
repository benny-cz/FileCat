using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Core.State;

namespace FileCat.App.Views;

/// <summary>Where the inspector offers an action: in the section showing what the action changes.</summary>
internal enum PermissionInspectorArea { Summary, Ownership, Mode, Flags, Access, Defaults, Inheritance, Auditing, Server }

/// <summary>An action offered in one of the inspector's sections. Choosing it ends the dialog with <see cref="Result"/>.</summary>
/// <param name="IsAvailable">Whether it acts on the subject at this index: cheap and side-effect free, asked again while the
/// dialog is open.</param>
internal sealed record PermissionInspectorAction(string Text, string Result, PermissionInspectorArea Area, Func<int, bool> IsAvailable)
{
    internal string? HelpText { get; init; }
}

/// <summary>
/// The permissions inspector: what the item is, then its ownership, mode, flags, access rules, inheritance and auditing,
/// each in its own section with the actions that change it. Rules are tables with plain-language columns (Explorer's names
/// for rights and scopes) and the exact entry beside the chosen rule; account names are looked up as the dialog opens.
/// </summary>
internal sealed class PermissionInspectorView : StackPanel
{
    // Names looked up when the dialog opens. The resolver queues 25 requests at most and serves two at once (plan §4).
    private const int NameLookupLimit = 24, NameLookupsAtOnce = 6;

    private static readonly TableShape EntryShape = new(["Type", "Account", "Access", "Applies to", "Source"],
        [0.9, 2.2, 1.5, 2.4, 1.0], [3.5, 6, 6, 7.5, 4.5], SourceColumn: 4);
    private static readonly TableShape AuditShape = new(["Type", "Account", "Access", "Applies to", "Source"],
        [1.6, 2.0, 1.4, 2.2, 1.0], [7, 6, 6, 7.5, 4.5], SourceColumn: 4);
    private static readonly TableShape PosixShape = new(["Entry", "Account", "Rights", "Effective"],
        [1.6, 2.6, 0.9, 0.9], [7, 7, 3.5, 4.5], SourceColumn: -1);

    private readonly StackPanel _subject = new() { Spacing = 12 };
    private readonly Func<string, Task>? _copy;
    private readonly string?[]? _editRefusals;
    private readonly PermissionPrincipalNames? _principalNames;
    private readonly IReadOnlyList<PermissionInspectorAction> _actions;
    private readonly DialogCloser? _closer;
    private readonly string? _notice;
    private readonly List<(Button Button, PermissionInspectorAction Action)> _actionButtons = [];
    private readonly HashSet<PermissionInspectorAction> _placed = [];
    private readonly Dictionary<Principal, PrincipalName> _names = [];
    private readonly List<Action> _nameListeners = [];
    private readonly List<Principal> _wanted = [];
    private PermissionAuthority? _authority;
    private DispatcherTimer? _requery;
    private CancellationTokenSource? _lookup;
    private CancellationTokenSource? _nameLookups;
    private bool _attached;

    /// <param name="actions">Actions offered in the sections they change; they end the dialog through <paramref name="closer"/>.</param>
    /// <param name="notice">A note shown under the summary for every subject (an administrator window's read-only view).</param>
    internal PermissionInspectorView((string Name, PermissionSnapshot Snapshot)[] subjects, Func<string, Task>? copy,
        string?[]? editRefusals = null, PermissionPrincipalNames? principalNames = null,
        IReadOnlyList<PermissionInspectorAction>? actions = null, DialogCloser? closer = null, string? notice = null)
    {
        _copy = copy;
        _editRefusals = editRefusals;
        _principalNames = principalNames;
        _actions = actions ?? [];
        _closer = closer;
        _notice = notice;
        Spacing = 12;
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

    /// <summary>One rule of a shown ACL: its cells in plain language and its exact entry.</summary>
    internal sealed class AclRow
    {
        /// <summary>The row's cells; the account column shows the account's name (see <see cref="Principal"/>).</summary>
        internal required string[] Cells { get; init; }
        internal Principal? Principal { get; init; }
        /// <summary>What the account column says for a row without a principal (a POSIX mask, Others).</summary>
        internal string AccountFallback { get; init; } = "";
        /// <summary>The entry's exact form: type, ID, mask or rights and flags.</summary>
        internal required string Exact { get; init; }
        /// <summary>The row in one line of plain language, for the chosen rule's details.</summary>
        internal required string Summary { get; init; }
        internal IReadOnlyList<string> Rights { get; init; } = [];
        internal bool Deny { get; init; }
        internal bool Inherited { get; init; }
        internal Action? Refresh { get; set; }
    }

    private sealed record TableShape(string[] Columns, double[] Weights, double[] MinimumEms, int SourceColumn);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateRequery();
        StartNameLookups();
    }

    // The section actions show whether they would act, as the dialog's own buttons do (re-asked every 200 ms).
    private void UpdateRequery()
    {
        RequeryActions();
        if (!_attached || _actionButtons.Count == 0)
        {
            _requery?.Stop();
            return;
        }
        if (_requery is null)
        {
            _requery = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _requery.Tick += OnRequery;
        }
        _requery.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _lookup?.Cancel();
        _nameLookups?.Cancel();
        _requery?.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnRequery(object? sender, EventArgs e) => RequeryActions();

    private void RequeryActions()
    {
        foreach (var (button, action) in _actionButtons) button.IsEnabled = action.IsAvailable(SelectedSubject);
    }

    private void Render(PermissionSnapshot snapshot)
    {
        _lookup?.Cancel();
        _nameLookups?.Cancel();
        _subject.Children.Clear();
        _actionButtons.Clear();
        _placed.Clear();
        _nameListeners.Clear();
        _wanted.Clear();
        _authority = snapshot.Subject.Authority;
        RenderSubject(snapshot);
        AddRemainingActions();
        UpdateRequery();
        if (_attached) StartNameLookups();
    }

    private void RenderSubject(PermissionSnapshot snapshot)
    {
        var kind = snapshot.Binding?.Identity.Kind ?? snapshot.Subject.Kind;
        bool directory = kind is PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot;
        _subject.Children.Add(Summary(snapshot));
        if (snapshot.BindingProblem is { } problem)
        {
            var lines = new List<string> { problem };
            if (snapshot.Subject.LastMountObservation is { } observedAt)
            {
                string source = snapshot.Subject.Authority.Kind == PermissionAuthorityKind.MountedServer ? "mounted client" : "filesystem";
                lines.Add($"Last observation from the {source} at {observedAt.ToLocalTime():g}.");
            }
            _subject.Children.Add(Note(lines, warning: true));
            return;
        }
        if (_notice is not null) _subject.Children.Add(Note([_notice], warning: false));
        if (snapshot.Ftp is { } ftp)
        {
            _subject.Children.Add(FtpSection(snapshot, ftp));
            return;
        }
        if (snapshot.Sftp is { } sftp)
        {
            _subject.Children.Add(SftpSection(snapshot, sftp));
            return;
        }
        if (snapshot.Mount is { } mount) _subject.Children.Add(MountNote(snapshot, mount));
        else if (_editRefusals is null && snapshot.Binding?.ReadOnly == true) _subject.Children.Add(Note(["This volume is read-only."], warning: false));
        else if (_editRefusals?[SelectedSubject] is { } refusal) _subject.Children.Add(Note([refusal], warning: false));
        if (snapshot.Model == PermissionModel.DarwinAcl && snapshot.Flags.Value is { } darwinFlags &&
            (darwinFlags.Bits & MacMaterializationPolicy.DatalessFlag) != 0)
            _subject.Children.Add(Note(["Dataless item. Showing metadata without downloading content."], warning: false));
        if (AuthorizationNote(snapshot) is { } authorization) _subject.Children.Add(authorization);
        _subject.Children.Add(OwnershipSection(snapshot));
        if (snapshot.Mode.Value is ushort mode) _subject.Children.Add(ModeSection(snapshot, mode));
        if (FlagsSection(snapshot) is { } flags) _subject.Children.Add(flags);
        _subject.Children.Add(AclSection("Access now", snapshot.Access, snapshot, directory, PermissionInspectorArea.Access));
        if (snapshot.Binding?.Identity.Kind is PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot && snapshot.Model == PermissionModel.PosixAcl)
            _subject.Children.Add(AclSection("Defaults for new children", snapshot.Defaults, snapshot, directory, PermissionInspectorArea.Defaults));
        if (snapshot.Inheritance.Value is { } control) _subject.Children.Add(InheritanceSection(snapshot, control, kind, directory));
        if (snapshot.Model == PermissionModel.WindowsDescriptor && snapshot.Audit.Status != PermissionReadStatus.Unloaded)
            _subject.Children.Add(AclSection("Auditing", snapshot.Audit, snapshot, directory, PermissionInspectorArea.Auditing));
    }

    // ---- Summary and notes ----

    private Control Summary(PermissionSnapshot snapshot)
    {
        var lines = new StackPanel { Spacing = 2 };
        lines.Children.Add(new TextBlock
        {
            Text = Formatters.SafeName(snapshot.Subject.DisplayPath), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap
        });
        if (snapshot.Binding is { } binding)
        {
            string kind = binding.Identity.Kind == PermissionObjectKind.VolumeRoot ? "Volume root" : binding.Identity.Kind.ToString();
            lines.Children.Add(Muted($"{kind} · {binding.FileSystem} · {ModelLabel(snapshot.Model)}"));
            if (snapshot.Mount is null) lines.Children.Add(Small(Muted("Authority: " + Formatters.SafeName(binding.Identity.Authority.Id))));
        }
        var times = new List<string>();
        if (snapshot.Times?.Value?.Modified is { } modified) times.Add($"Modified: {modified.ToLocalTime():g}");
        if (snapshot.Times?.Value?.Created is { } created) times.Add($"Created: {created.ToLocalTime():g}");
        if (times.Count > 0) lines.Children.Add(Small(Muted(string.Join(" · ", times))));
        if (_editRefusals is null && snapshot.Mount is null && snapshot.BindingProblem is null && snapshot.Binding?.ReadOnly != true)
            lines.Children.Add(Small(Muted("This view shows the observed permissions.")));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(lines);
        if (_copy is not null)
        {
            var copy = CopyButton("Copy exact path", snapshot.Subject.DisplayPath);
            copy.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);
        }
        return new Border { Classes = { "permissionSummary" }, Child = grid };
    }

    private static Border Note(IEnumerable<string> lines, bool warning, Control? trailing = null)
    {
        var stack = new StackPanel { Spacing = 3 };
        foreach (string line in lines) stack.Children.Add(Label(line));
        if (trailing is not null) stack.Children.Add(trailing);
        var note = new Border { Classes = { "permissionNote" }, Child = stack };
        if (warning) note.Classes.Add("warning");
        return note;
    }

    private static Control MountNote(PermissionSnapshot snapshot, PermissionMountObservation mount)
    {
        var lines = new List<string>
        {
            mount.Presentation switch
            {
                PermissionMountPresentation.Synthetic => "Mount-derived permissions",
                PermissionMountPresentation.Translated => "Permissions presented by the mounted client",
                PermissionMountPresentation.Native => "Permissions reported by the filesystem",
                _ => "The mount's permission model is unidentified"
            }
        };
        if (mount.Source is { } source) lines.Add("Mount source · " + Formatters.SafeName(source));
        if (mount.Root != "/") lines.Add("Filesystem root · " + Formatters.SafeName(mount.Root));
        if (mount.Protocol is { } protocol) lines.Add(protocol);
        if (mount.ClientPolicy is { } policyNote) lines.Add(policyNote);
        if (mount.IsServerProjection)
            lines.Add($"Read from the mounted client at {snapshot.Mode.ObservedAt.ToLocalTime():g}. The client may use cached metadata. " +
                "Server reachability and freshness are unverified.");
        if (snapshot.Binding?.ReadOnly == true) lines.Add("This mount is read-only.");
        lines.Add(mount.ChangeRefusal);
        var note = Note(lines, warning: false);
        if (note.Child is StackPanel stack && stack.Children.Count > 0 && stack.Children[0] is TextBlock first) first.FontWeight = FontWeight.SemiBold;
        return note;
    }

    // "Inspect with authorization…": what it adds, with the button.
    private Control? AuthorizationNote(PermissionSnapshot snapshot)
    {
        var buttons = ActionButtons(PermissionInspectorArea.Summary);
        if (buttons is null) return null;
        bool denied = snapshot.Owner.Status == PermissionReadStatus.Denied || snapshot.Group.Status == PermissionReadStatus.Denied ||
            snapshot.Access.Status == PermissionReadStatus.Denied || snapshot.Defaults.Status == PermissionReadStatus.Denied;
        string text = denied ? "This account cannot read part of this item's security information. An authorized inspection reads all of it."
            : snapshot.Model == PermissionModel.WindowsDescriptor
                ? "Auditing rules are read only with administrator authorization."
                : "An authorized inspection reads what this account cannot.";
        return Note([text], warning: denied, buttons);
    }

    // ---- Ownership, mode and flags ----

    private Control OwnershipSection(PermissionSnapshot snapshot)
    {
        bool projectsOwnership = snapshot.Mount is not null || snapshot.VolumePolicy.Value is { HonorsOwnership: false };
        var section = Section("Ownership", out var content);
        if (snapshot.VolumePolicy.Value is { } policy)
            content.Children.Add(Muted(policy.HonorsOwnership ? "This volume honors file ownership." : "This volume ignores file ownership."));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, RowSpacing = 8 };
        AddPrincipalRow(grid, projectsOwnership ? "Presented owner" : "Owner", snapshot.Owner);
        AddPrincipalRow(grid, projectsOwnership ? "Presented primary group" : "Primary group", snapshot.Group);
        content.Children.Add(grid);
        AddActions(content, PermissionInspectorArea.Ownership);
        return section;
    }

    private void AddPrincipalRow(Grid grid, string label, PermissionObservation<Principal> principal)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var caption = Muted(label);
        Grid.SetRow(caption, row);
        grid.Children.Add(caption);
        if (principal.Value is not { } value)
        {
            var missing = Label(ObservationText(principal.Status, principal.Reason));
            Grid.SetRow(missing, row);
            Grid.SetColumn(missing, 1);
            Grid.SetColumnSpan(missing, 2);
            grid.Children.Add(missing);
            return;
        }
        var title = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var subtitle = Small(Muted(""));
        void Show()
        {
            title.Text = PrincipalTitle(value);
            subtitle.Text = PrincipalSubtitle(value);
            subtitle.IsVisible = subtitle.Text.Length > 0;
        }
        Show();
        _nameListeners.Add(Show);
        Want(value);
        var names = new StackPanel { Spacing = 1, Children = { title, subtitle } };
        Grid.SetRow(names, row);
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);
        if (_copy is null) return;
        var copy = CopyButton("Copy exact " + label.ToLowerInvariant() + " ID", value.Id);
        copy.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRow(copy, row);
        Grid.SetColumn(copy, 2);
        grid.Children.Add(copy);
    }

    private Control ModeSection(PermissionSnapshot snapshot, ushort mode)
    {
        var bits = (UnixFileMode)mode;
        var section = Section($"{(snapshot.Mount is null ? "Mode" : "Presented mode")}: {UnixPermissions.Octal(bits)} · {UnixPermissions.Format(bits)}",
            out var content);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12, RowSpacing = 2 };
        AddFact(grid, "Owner", PermissionPresentation.UnixClassRights(mode, 6));
        AddFact(grid, "Group", PermissionPresentation.UnixClassRights(mode, 3));
        AddFact(grid, "Others", PermissionPresentation.UnixClassRights(mode, 0));
        if (PermissionPresentation.UnixSpecialBits(mode) is { Count: > 0 } special) AddFact(grid, "Special", string.Join(", ", special));
        content.Children.Add(grid);
        AddActions(content, PermissionInspectorArea.Mode);
        return section;
    }

    private Control? FlagsSection(PermissionSnapshot snapshot)
    {
        var set = new List<string>();
        var details = new List<string>();
        if (snapshot.Model == PermissionModel.DarwinAcl && snapshot.Flags.Value is { } flags)
        {
            if ((flags.Bits & DarwinPermissionService.UserLocked) != 0) set.Add("Finder Locked");
            if ((flags.Bits & DarwinPermissionService.UserAppendOnly) != 0) set.Add("User append-only");
            if ((flags.Bits & DarwinPermissionService.SystemImmutable) != 0) set.Add("System immutable · read only");
            if ((flags.Bits & DarwinPermissionService.SystemAppendOnly) != 0) set.Add("System append-only · read only");
            if ((flags.Bits & 0x8000) != 0) set.Add("Finder Hidden");
            details.Add($"BSD flags · 0x{flags.Bits:X8}");
        }
        else if (snapshot.Model is PermissionModel.PosixAcl or PermissionModel.UnixMode && snapshot.Flags.Status != PermissionReadStatus.Unloaded)
        {
            if (snapshot.Flags.Value is { } inode)
            {
                if ((inode.Bits & LinuxPermissionService.ImmutableInode) != 0) set.Add("Immutable");
                if ((inode.Bits & LinuxPermissionService.AppendOnlyInode) != 0) set.Add("Append-only");
                if (inode.KnownBits == uint.MaxValue) details.Add($"Inode flags · 0x{inode.Bits:X8}");
            }
            if (snapshot.Flags.Reason is { } reason) details.Add(Formatters.SafeName(reason));
        }
        bool offered = _actions.Any(action => action.Area == PermissionInspectorArea.Flags);
        if (set.Count == 0 && details.Count == 0 && !offered) return null;
        var section = Section("File flags", out var content);
        if (set.Count == 0) content.Children.Add(Label("No protection or visibility flags are set."));
        else
        {
            var chosen = new WrapPanel { ItemSpacing = 12, LineSpacing = 2 };
            foreach (string flag in set) chosen.Children.Add(new TextBlock { Text = flag, FontWeight = FontWeight.SemiBold });
            content.Children.Add(chosen);
        }
        foreach (string detail in details) content.Children.Add(Small(Muted(detail)));
        AddActions(content, PermissionInspectorArea.Flags);
        return section;
    }

    // ---- ACL tables ----

    private Control AclSection(string title, PermissionObservation<PermissionAclValue> observation, PermissionSnapshot snapshot, bool directory,
        PermissionInspectorArea area)
    {
        var acl = observation.Value?.Acl;
        var (shape, rows) = acl switch
        {
            WindowsDacl { Form: WindowsDaclForm.Entries, Entries.IsEmpty: false } windows =>
                (EntryShape, windows.Entries.Select(entry => WindowsRow(entry, directory)).ToList()),
            WindowsSacl { Present: true, Revision: not null, Entries.IsEmpty: false } audit =>
                (AuditShape, audit.Entries.Select(entry => AuditRow(entry, directory)).ToList()),
            PosixAcl { Entries.IsEmpty: false } posix => (PosixShape, PosixRows(posix, snapshot)),
            DarwinAcl { Entries.IsEmpty: false } darwin => (EntryShape, darwin.Entries.Select(entry => DarwinRow(entry, directory)).ToList()),
            _ => ((TableShape?)null, new List<AclRow>())
        };
        var section = Section(title, out var content,
            shape is null ? null : Formatters.Plural(rows.Count, acl is PosixAcl ? "entry" : "rule", acl is PosixAcl ? "entries" : "rules"));
        if (acl is DarwinAcl darwinAcl) content.Children.Add(Small(Muted($"Extended ACL flags · 0x{darwinAcl.Flags:X8}")));
        if (acl is WindowsSacl { Present: true } sacl) content.Children.Add(Small(Muted($"System ACL flags · 0x{sacl.Control.Flags:X4}")));
        // The section's actions come first, in view above a long table and the chosen rule's details.
        AddActions(content, area);
        if (shape is null)
        {
            var (text, warning) = acl switch
            {
                WindowsDacl { Form: WindowsDaclForm.Null } => ("NULL DACL · unrestricted discretionary access", true),
                WindowsDacl { Form: WindowsDaclForm.Absent } => ("DACL absent · unrestricted discretionary access", true),
                WindowsDacl => ("Empty DACL · no discretionary grants", false),
                WindowsSacl { Present: false } => ("System ACL absent", false),
                WindowsSacl { Revision: null } => ("NULL system ACL", false),
                WindowsSacl => ("Empty system ACL", false),
                DarwinAcl => ("Empty extended ACL", false),
                PosixAcl => ("No ACL entries", false),
                OpaquePermissionAcl => ("This ACL uses a format FileCat cannot interpret.", false),
                _ => (ObservationText(observation.Status, observation.Reason), false)
            };
            var line = Label(text);
            if (warning) line.Classes.Add("warning");
            content.Children.Add(line);
        }
        else content.Children.Add(Table(title, shape, rows, snapshot.Subject.Authority));
        return section;
    }

    // Every row of these bounded ACL lists exists, so a screen reader hears the true count and each row's position. A
    // virtualizing panel exposes only the rows in view: Orca said "List with 2 items" for a five-entry ACL.
    internal static FuncTemplate<Panel?> AllRows() => new(() => new StackPanel());

    private Control Table(string name, TableShape shape, List<AclRow> rows, PermissionAuthority authority, bool details = true)
    {
        var table = new StackPanel { Spacing = 4 };
        // The heading row sits where the rows' cells do: past the list's frame, a row's frame and its padding.
        var heading = new PermissionTablePanel(shape.Weights, shape.MinimumEms, heading: true) { Margin = new Thickness(10, 0, 10, 0) };
        foreach (string column in shape.Columns)
            heading.Children.Add(new TextBlock
            {
                Text = column, Classes = { "muted", "small" }, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis
            });
        table.Children.Add(heading);
        var list = new CountedListBox
        {
            ItemsSource = rows, MaxHeight = 300, ItemsPanel = AllRows(),
            ItemTemplate = new FuncDataTemplate<AclRow>((row, _) => row is null ? new TextBlock() : RowView(row, shape)),
        };
        list.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 4)),
                new Setter(Layoutable.MinHeightProperty, 0d),
            }
        });
        AutomationProperties.SetName(list, name);
        // Each rule is announced as one sentence; a name looked up later is spoken from then on.
        list.ContainerPrepared += (_, e) =>
        {
            if (e.Index >= 0 && e.Index < rows.Count) AutomationProperties.SetName(e.Container, Spoken(rows[e.Index], shape));
        };
        _nameListeners.Add(() =>
        {
            foreach (var row in rows) row.Refresh?.Invoke();
            for (int index = 0; index < rows.Count; index++)
                if (list.ContainerFromIndex(index) is { } container) AutomationProperties.SetName(container, Spoken(rows[index], shape));
        });
        foreach (var row in rows)
            if (row.Principal is { } principal) Want(principal);
        table.Children.Add(list);
        if (details)
        {
            table.Children.Add(Details(list, authority));
            list.SelectedIndex = 0;
        }
        return table;
    }

    private Control RowView(AclRow row, TableShape shape)
    {
        var panel = new PermissionTablePanel(shape.Weights, shape.MinimumEms);
        for (int column = 0; column < row.Cells.Length; column++)
        {
            var cell = new TextBlock
            {
                Text = column == 1 ? AccountText(row) : row.Cells[column], TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (column == 0 && row.Deny)
            {
                // A deny rule stands out by weight and color; the word itself says it.
                cell.Classes.Add("error");
                cell.FontWeight = FontWeight.SemiBold;
            }
            if (column == shape.SourceColumn && row.Inherited) cell.Classes.Add("muted");
            if (column == 1)
            {
                void Refresh()
                {
                    cell.Text = AccountText(row);
                    ToolTip.SetTip(cell, row.Principal is { } principal ? AccountTip(principal) : null);
                }
                Refresh();
                row.Refresh = Refresh;
            }
            panel.Children.Add(cell);
        }
        return panel;
    }

    private string Spoken(AclRow row, TableShape shape)
    {
        string account = AccountText(row);
        var cells = row.Cells;
        if (ReferenceEquals(shape, PosixShape))
            return $"{cells[0]}{(account.Length > 0 ? " " + account : "")}: {cells[2]}{(cells[3] != cells[2] ? $", effective {cells[3]}" : "")}";
        return $"{cells[0]} {account}: {string.Join("; ", cells.Skip(2).Where(cell => cell.Length > 0))}";
    }

    // The chosen rule: its account, plain-language summary, every right, and the exact entry, with the account actions.
    private Control Details(ListBox list, PermissionAuthority authority)
    {
        var title = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var subtitle = Small(Muted(""));
        var summary = Label("");
        var rights = Label("");
        var exact = Small(Muted(""));
        var stack = new StackPanel { Spacing = 3, Children = { title, subtitle, summary, rights, exact } };
        AclRow? Selected() => list.SelectedItem as AclRow;
        void Show()
        {
            var row = Selected();
            title.Text = row is null ? "No rule chosen" : $"{row.Cells[0]} · {AccountText(row)}".TrimEnd(' ', '·');
            subtitle.Text = row?.Principal is { } principal ? PrincipalSubtitle(principal) : "";
            subtitle.IsVisible = subtitle.Text.Length > 0;
            summary.Text = row?.Summary ?? "";
            summary.IsVisible = summary.Text.Length > 0;
            rights.Text = row is { Rights.Count: > 0 } ? "Rights: " + string.Join(", ", row.Rights) : "";
            rights.IsVisible = rights.Text.Length > 0;
            exact.Text = row is null ? "" : "Exact entry: " + row.Exact;
            exact.IsVisible = row is not null;
        }
        _nameListeners.Add(Show);
        list.SelectionChanged += (_, _) => Show();
        Show();
        AddAccountActions(stack, authority, () => Selected()?.Principal, list);
        return new Border { Classes = { "permissionDetails" }, Child = stack };
    }

    private void AddAccountActions(Panel target, PermissionAuthority authority, Func<Principal?> selected, ListBox list)
    {
        bool canLookUp = _principalNames is not null && authority.Kind == PermissionAuthorityKind.Local;
        if (_copy is null && !canLookUp) return;
        var actions = DialogLayout.Actions();
        var status = Label(string.Empty);
        status.IsVisible = false;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var copy = new Button { Content = "Copy exact account ID", IsVisible = _copy is not null };
        copy.Click += async (_, _) => { if (_copy is not null && selected() is { } principal) await _copy(principal.Id); };
        var lookup = new Button { Content = "Look up account", IsVisible = canLookUp };
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
                if (answer.Status is NameStatus.Resolved or NameStatus.NotFound)
                {
                    _names[principal] = answer;
                    NotifyNames();
                }
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
        list.SelectionChanged += (_, _) => UpdateSelection();
        UpdateSelection();
        actions.Children.Add(copy);
        actions.Children.Add(lookup);
        target.Children.Add(actions);
        target.Children.Add(status);
    }

    private static AclRow WindowsRow(WindowsAce entry, bool directory)
    {
        string type = PermissionPresentation.WindowsType(entry);
        string access = entry.Mask is uint mask ? PermissionPresentation.WindowsAccess(mask, entry.Flags, directory) : "Not interpreted";
        string applies = AppliesTo(entry.Flags, directory);
        string source = PermissionPresentation.WindowsSource(entry.Flags);
        return new AclRow
        {
            Cells = [type, "", access, applies, source],
            Principal = entry.Principal,
            AccountFallback = "Not interpreted",
            Exact = WindowsRule(entry),
            Summary = $"{access} · applies to {Lower(applies)} · {Lower(source)}",
            Rights = entry.Mask is uint rights ? PermissionPresentation.WindowsRights(rights, directory) : [],
            Deny = entry.Kind == WindowsAceKind.Deny,
            Inherited = (entry.Flags & 0x10) != 0,
        };
    }

    private static AclRow AuditRow(WindowsAuditAce entry, bool directory)
    {
        string type = PermissionPresentation.AuditType(entry);
        string access = entry switch
        {
            { Type: 0x11, Mask: uint policy, Principal: { } label } => PermissionPresentation.IntegrityLabel(label.Id, policy),
            { Type: 2, Mask: uint mask } => PermissionPresentation.WindowsAccess(mask, entry.Flags, directory),
            _ => "Not interpreted"
        };
        string applies = AppliesTo(entry.Flags, directory);
        string source = PermissionPresentation.WindowsSource(entry.Flags);
        return new AclRow
        {
            Cells = [type, "", access, applies, source],
            Principal = entry.Principal,
            AccountFallback = "Not interpreted",
            Exact = WindowsAuditEditor.Describe(entry),
            Summary = $"{access} · applies to {Lower(applies)} · {Lower(source)}",
            Rights = entry is { Type: 2, Mask: uint rights } ? PermissionPresentation.WindowsRights(rights, directory) : [],
            Inherited = (entry.Flags & 0x10) != 0,
        };
    }

    private static List<AclRow> PosixRows(PosixAcl acl, PermissionSnapshot snapshot)
    {
        byte mask = PosixAccessDraft.Mask(acl.Entries);
        return acl.Entries.Select(entry =>
        {
            bool groupClass = entry.Tag is PosixAclTag.OwningGroup or PosixAclTag.NamedUser or PosixAclTag.NamedGroup;
            string configured = Rwx(entry.Rights);
            string effective = groupClass ? Rwx(PosixAccessDraft.EffectiveRights(entry, mask)) : configured;
            return new AclRow
            {
                Cells =
                [
                    entry.Tag switch
                    {
                        PosixAclTag.Owner => "Owner",
                        PosixAclTag.NamedUser => "User",
                        PosixAclTag.OwningGroup => "Owning group",
                        PosixAclTag.NamedGroup => "Group",
                        PosixAclTag.Mask => "Group-class limit",
                        _ => "Others"
                    }, "", configured, effective
                ],
                Principal = entry.Tag switch
                {
                    PosixAclTag.NamedUser or PosixAclTag.NamedGroup => entry.Principal,
                    PosixAclTag.Owner => snapshot.Owner.Value,
                    PosixAclTag.OwningGroup => snapshot.Group.Value,
                    _ => null
                },
                AccountFallback = entry.Tag switch
                {
                    PosixAclTag.Owner => "File owner",
                    PosixAclTag.OwningGroup => "File group",
                    _ => ""
                },
                Exact = $"{PosixAccessEditor.PrincipalLabel(entry)} · {PosixAccessEditor.DescribeRights(entry, mask)}",
                Summary = entry.Tag switch
                {
                    PosixAclTag.Mask => "The most named users, the owning group and named groups get: " + Lower(RightWords(entry.Rights)),
                    PosixAclTag.Other => "Everyone else: " + Lower(RightWords(entry.Rights)),
                    _ when effective != configured =>
                        $"Configured {Lower(RightWords(entry.Rights))}; effective {Lower(RightWords(PosixAccessDraft.EffectiveRights(entry, mask)))}",
                    _ => RightWords(entry.Rights)
                },
            };
        }).ToList();
    }

    private static AclRow DarwinRow(DarwinAce entry, bool directory)
    {
        var names = DarwinRights(directory).Where(right => (entry.Rights & right.Bit) != 0).Select(right => right.Label).ToList();
        if ((entry.Rights & ~DarwinPermissionAcl.EditableRights) != 0) names.Add($"Other rights 0x{entry.Rights & ~DarwinPermissionAcl.EditableRights:X8}");
        string access = names.Count switch
        {
            0 => $"Rights 0x{entry.Rights:X8}",
            1 or 2 => string.Join(", ", names),
            _ => $"{names[0]}, {names[1]} and {names.Count - 2} more"
        };
        // Darwin's inheritance flags as Windows' (file → object, directory → container, limit → no propagation, only).
        byte flags = (byte)(((entry.Flags & 0x20) != 0 ? 1 : 0) | ((entry.Flags & 0x40) != 0 ? 2 : 0) |
            ((entry.Flags & 0x80) != 0 ? 4 : 0) | ((entry.Flags & 0x100) != 0 ? 8 : 0));
        string applies = AppliesTo(flags, directory);
        string source = (entry.Flags & 0x10) != 0 ? "Inherited" : "Explicit";
        string type = entry.Kind switch { DarwinAceKind.Allow => "Allow", DarwinAceKind.Deny => "Deny", _ => "Other" };
        return new AclRow
        {
            Cells = [type, "", access, applies, source],
            Principal = entry.Principal,
            Exact = DarwinRule(entry, directory),
            Summary = $"Applies to {Lower(applies)} · {Lower(source)}",
            Rights = names,
            Deny = entry.Kind == DarwinAceKind.Deny,
            Inherited = (entry.Flags & 0x10) != 0,
        };
    }

    // An inherit-only rule on a file reaches nothing: a file has no children.
    private static string AppliesTo(byte flags, bool directory) =>
        !directory && (flags & 8) != 0 ? "Nothing (inherit only)" : PermissionPresentation.WindowsAppliesTo(flags, directory);

    // "This folder only" inside a sentence; an acronym ("NULL") keeps its case.
    private static string Lower(string text) =>
        text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;

    private static string Rwx(int bits) => $"{((bits & 4) != 0 ? 'r' : '-')}{((bits & 2) != 0 ? 'w' : '-')}{((bits & 1) != 0 ? 'x' : '-')}";

    private static string RightWords(int bits)
    {
        var words = new List<string>();
        if ((bits & 4) != 0) words.Add("read");
        if ((bits & 2) != 0) words.Add("write");
        if ((bits & 1) != 0) words.Add("execute");
        return words.Count == 0 ? "No rights" : char.ToUpperInvariant(words[0][0]) + string.Join(", ", words)[1..];
    }

    // ---- Inheritance ----

    private Control InheritanceSection(PermissionSnapshot snapshot, WindowsAclControl control, PermissionObjectKind kind, bool directory)
    {
        bool legacy = snapshot.Model == PermissionModel.WindowsDescriptor && !control.IsAutoInherited;
        var section = Section((control.IsProtected ? "Inheritance: protected" : "Inheritance: enabled") + (legacy ? ", legacy form" : ""), out var content);
        string item = directory ? "folder" : "file";
        if (kind == PermissionObjectKind.VolumeRoot) content.Children.Add(Muted("A volume root has no parent folder to inherit from."));
        else if (!legacy)
            content.Children.Add(Muted(control.IsProtected
                ? $"The parent folder's rules do not reach this {item}: only its own entries apply."
                : $"The parent folder's inheritable rules reach this {item} as inherited entries."));
        if (legacy) content.Children.Add(Muted(LegacyDaclNote(control, kind)));
        // Beside the stored entries: how Windows tools read them against the parent (WindowsInheritedReading).
        if (legacy && snapshot.WindowsReading is { } reading)
        {
            string title = reading.Control.IsProtected ? "Windows tools read this DACL as protected" : "Windows tools read this DACL as";
            var rows = reading.Acl.Entries.Select(entry => WindowsRow(entry, directory)).ToList();
            var shown = new StackPanel { Spacing = 6, IsVisible = false };
            shown.Children.Add(new TextBlock { Text = title, Classes = { "permissionHeading" }, TextWrapping = TextWrapping.Wrap });
            if (rows.Count == 0) shown.Children.Add(Label("No entries"));
            else shown.Children.Add(Table(title, EntryShape, rows, snapshot.Subject.Authority, details: false));
            var toggle = new CheckBox { Content = "Show how Windows tools read this DACL" };
            toggle.IsCheckedChanged += (_, _) => shown.IsVisible = toggle.IsChecked == true;
            content.Children.Add(toggle);
            content.Children.Add(shown);
        }
        AddActions(content, PermissionInspectorArea.Inheritance);
        return section;
    }

    // A DACL stored without SE_DACL_AUTO_INHERITED. FileCat reads and writes the stored form; Explorer and other Windows tools
    // read and rewrite it differently (see WindowsPermissionService.ReadSecurityDescriptor and HasLegacyDacl).
    internal static string LegacyDaclNote(WindowsAclControl control, PermissionObjectKind kind) => kind switch
    {
        PermissionObjectKind.VolumeRoot => "Stored without the automatic-inheritance mark, as format leaves a volume root. Windows tools add the mark " +
            "when they change this DACL. FileCat keeps the stored form and offers no native propagation here.",
        _ when control.IsProtected => "Stored without the automatic-inheritance mark. Windows tools add the mark when they change this DACL. " +
            (kind == PermissionObjectKind.Directory ? "FileCat keeps the stored form and offers no native propagation or inheritance change here."
                : "FileCat keeps the stored form."),
        _ => "Stored without inheritance marks, as format and older tools leave it. Explorer shows the entries that match the parent's " +
            "inheritable rules as inherited, and Windows tools convert the DACL when they change it. " +
            (kind == PermissionObjectKind.Directory
                ? "FileCat shows and keeps the stored entries and offers no native propagation or inheritance change here."
                : "FileCat shows and keeps the stored entries.")
    };

    // ---- Remote servers ----

    private Control FtpSection(PermissionSnapshot snapshot, FtpPermissionObservation ftp)
    {
        var section = Section("Server permissions", out var content);
        content.Children.Add(Muted($"{RemoteProtocols.Describe(ftp.Protocol)} · {ftp.Source} · Observed at the server path {ftp.ObservedAt.ToLocalTime():g}"));
        AddServerPrincipal(content, "Server owner ID", snapshot.Owner);
        AddServerPrincipal(content, "Server group ID", snapshot.Group);
        if (ftp.OwnerName is { } owner) AddServerValue(content, owner, "Copy exact server owner name", "Server owner name · ");
        if (ftp.GroupName is { } group) AddServerValue(content, group, "Copy exact server group name", "Server group name · ");
        content.Children.Add(Label(ServerMode(snapshot)));
        content.Children.Add(Label("Reported FTP action codes · " + (ftp.TransferRights switch
        {
            null => "Unreported",
            "" => "None",
            { } rights => rights
        })));
        if (ftp.Unique is { } unique) AddServerValue(content, unique, "Copy exact server identifier", "Server identifier · ");
        content.Children.Add(Label("Exact-item changes are unavailable over FTP and FTPS."));
        AddActions(content, PermissionInspectorArea.Server);
        return section;
    }

    private Control SftpSection(PermissionSnapshot snapshot, SftpPermissionObservation sftp)
    {
        var section = Section("Server permissions", out var content);
        content.Children.Add(Muted($"{sftp.ObjectType} · SFTP LSTAT · Observed at the server path {sftp.ObservedAt.ToLocalTime():g}"));
        if (sftp.Connection is { } connection) content.Children.Add(Muted("Connection · " + Formatters.SafeName(connection)));
        AddServerPrincipal(content, "Server owner ID", snapshot.Owner);
        AddServerPrincipal(content, "Server group ID", snapshot.Group);
        if (sftp.UserName is { } ownerName) AddServerValue(content, ownerName, "Copy exact server owner name", "Server owner name · ");
        if (sftp.GroupName is { } groupName) AddServerValue(content, groupName, "Copy exact server group name", "Server group name · ");
        content.Children.Add(Label(ServerMode(snapshot)));
        if (sftp.ChangeRefusal is { } serverRefusal) content.Children.Add(Label(serverRefusal));
        else if (sftp.ModeChangeRefusal is { } modeRefusal) content.Children.Add(Label(modeRefusal));
        AddActions(content, PermissionInspectorArea.Server);
        return section;
    }

    private static string ServerMode(PermissionSnapshot snapshot) => snapshot.Mode.Value is ushort mode
        ? $"Server mode · {UnixPermissions.Octal((UnixFileMode)mode)} · {UnixPermissions.Format((UnixFileMode)mode)}"
        : "Server mode · " + ObservationText(snapshot.Mode.Status, snapshot.Mode.Reason);

    private void AddServerPrincipal(Panel target, string label, PermissionObservation<Principal> principal)
    {
        if (principal.Value is { } value) AddServerValue(target, value.Id, "Copy exact " + label.ToLowerInvariant() + " ID", label + ": ");
        else target.Children.Add(Label(label + ": " + ObservationText(principal.Status, principal.Reason)));
    }

    private void AddServerValue(Panel target, string value, string copyText, string prefix)
    {
        var line = Label(prefix + Formatters.SafeName(value));
        if (_copy is null)
        {
            target.Children.Add(line);
            return;
        }
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        row.Children.Add(line);
        var copy = CopyButton(copyText, value);
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        target.Children.Add(row);
    }

    // ---- Actions ----

    private WrapPanel? ActionButtons(PermissionInspectorArea area)
    {
        var mine = _actions.Where(action => action.Area == area && _placed.Add(action)).ToList();
        if (mine.Count == 0) return null;
        var row = DialogLayout.Actions();
        foreach (var action in mine) row.Children.Add(ActionButton(action));
        return row;
    }

    private void AddActions(Panel content, PermissionInspectorArea area)
    {
        if (ActionButtons(area) is { } row) content.Children.Add(row);
    }

    // Actions whose section this item does not show (an unreadable item) stay offered, together at the end.
    private void AddRemainingActions()
    {
        var rest = _actions.Where(action => !_placed.Contains(action)).ToList();
        if (rest.Count == 0) return;
        var section = Section("Actions", out var content);
        var row = DialogLayout.Actions();
        foreach (var action in rest)
        {
            _placed.Add(action);
            row.Children.Add(ActionButton(action));
        }
        content.Children.Add(row);
        _subject.Children.Add(section);
    }

    private Button ActionButton(PermissionInspectorAction action)
    {
        var button = new Button { Content = action.Text, ContentTemplate = DialogLayout.WrappingText, IsEnabled = action.IsAvailable(SelectedSubject) };
        if (!string.IsNullOrWhiteSpace(action.HelpText)) AutomationProperties.SetHelpText(button, action.HelpText);
        button.Click += (_, _) =>
        {
            if (action.IsAvailable(SelectedSubject)) _closer?.Close(action.Result);
        };
        _actionButtons.Add((button, action));
        return button;
    }

    // ---- Account names ----

    private void Want(Principal principal) => _wanted.Add(principal);

    private void NotifyNames()
    {
        foreach (var listener in _nameListeners.ToArray()) listener();
    }

    // Names of the accounts shown, looked up as the dialog opens: a bounded number, a few at a time, from the same local
    // resolver as "Look up account". Late answers for another subject or a closed dialog are dropped.
    private void StartNameLookups()
    {
        _nameLookups?.Cancel();
        if (_principalNames is not { } names || _authority is not { Kind: PermissionAuthorityKind.Local } authority) return;
        var pending = _wanted.Where(principal => principal.Authority == authority.Id && !_names.ContainsKey(principal))
            .Distinct().Take(NameLookupLimit).ToArray();
        if (pending.Length == 0) return;
        var cancellation = new CancellationTokenSource();
        _nameLookups = cancellation;
        _ = ResolveNamesAsync(names, authority, pending, cancellation.Token);
    }

    private async Task ResolveNamesAsync(PermissionPrincipalNames names, PermissionAuthority authority, Principal[] pending, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(NameLookupsAtOnce);
        async Task One(Principal principal)
        {
            try { await gate.WaitAsync(ct); }
            catch (OperationCanceledException) { return; }
            try
            {
                var answer = await names.ResolveAsync(authority, principal, ct);
                if (ct.IsCancellationRequested || answer.Status is not (NameStatus.Resolved or NameStatus.NotFound)) return;
                _names[principal] = answer;
                NotifyNames();
            }
            catch (OperationCanceledException) { }
            finally { gate.Release(); }
        }
        await Task.WhenAll(pending.Select(One));
    }

    private static string IdText(Principal principal) => principal.Kind switch
    {
        PrincipalKind.UnixUser => "UID " + principal.Id,
        PrincipalKind.UnixGroup => "GID " + principal.Id,
        _ => principal.Id
    };

    private static string ShortName(Principal principal, string name) =>
        principal.Kind == PrincipalKind.WindowsSid && name.LastIndexOf('\\') is int slash and >= 0 && slash < name.Length - 1 ? name[(slash + 1)..] : name;

    private string PrincipalTitle(Principal principal)
    {
        if (_names.TryGetValue(principal, out var answer) && answer is { Status: NameStatus.Resolved, Name: { Length: > 0 } name })
            return Formatters.SafeName(ShortName(principal, name));
        if (principal.Kind == PrincipalKind.WindowsSid && PermissionPresentation.WellKnownWindowsName(principal.Id) is { } known) return known;
        return principal.Kind == PrincipalKind.WindowsSid ? principal.DisplayId : IdText(principal);
    }

    private string PrincipalSubtitle(Principal principal)
    {
        var parts = new List<string>();
        if (_names.TryGetValue(principal, out var answer))
        {
            if (answer is { Status: NameStatus.Resolved, Name: { Length: > 0 } name })
            {
                if (ShortName(principal, name) != name) parts.Add(Formatters.SafeName(name));
                if (answer.Type is { Length: > 0 } type) parts.Add(type);
            }
            else if (answer.Status == NameStatus.NotFound) parts.Add("No account name was found for this ID");
        }
        string id = Formatters.SafeName(IdText(principal));
        if (id != PrincipalTitle(principal)) parts.Add(id);
        return string.Join(" · ", parts);
    }

    private string AccountText(AclRow row) => row.Principal is { } principal ? PrincipalTitle(principal) : row.AccountFallback;

    private string AccountTip(Principal principal) =>
        PrincipalSubtitle(principal) is { Length: > 0 } subtitle ? PrincipalTitle(principal) + " · " + subtitle : PrincipalTitle(principal);

    // ---- Exact entries (also used by the editors and reviews) ----

    internal static string WindowsRule(WindowsAce entry)
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

    // ---- Small parts ----

    private static Border Section(string title, out StackPanel content, string? aside = null)
    {
        content = new StackPanel { Spacing = 8 };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        heading.Children.Add(new TextBlock { Text = title, Classes = { "permissionHeading" }, TextWrapping = TextWrapping.Wrap });
        if (aside is not null)
        {
            var count = Muted(aside);
            count.TextWrapping = TextWrapping.NoWrap;
            count.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(count, 1);
            heading.Children.Add(count);
        }
        content.Children.Add(heading);
        return new Border { Classes = { "permissionSection" }, Child = content };
    }

    private static void AddFact(Grid grid, string label, string value)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var caption = Muted(label);
        Grid.SetRow(caption, row);
        grid.Children.Add(caption);
        var text = Label(value);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
    }

    private Button CopyButton(string text, string value)
    {
        var button = new Button { Content = text };
        // The value itself is plain text a keyboard never reaches: the copy button carries it, so a screen reader
        // tabbing through the inspector hears which item, owner or group the button copies. The button's name already
        // says which: Orca drops a description it finds similar to the name, as "Primary group: 1000" was.
        AutomationProperties.SetHelpText(button, Formatters.SafeName(value));
        button.Click += async (_, _) => { if (_copy is not null) await _copy(value); };
        return button;
    }

    private static TextBlock Label(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Muted(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };

    private static TextBlock Small(TextBlock text)
    {
        text.Classes.Add("small");
        return text;
    }

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
