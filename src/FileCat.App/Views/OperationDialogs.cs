using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using FileCat.Core.Operations;

namespace FileCat.App.Views;

/// <param name="Panels">The folders of the panels F5 and F6 can go to, offered as one-click destinations when there are several.</param>
public sealed record TransferDialogInput(JobKind Kind, IReadOnlyList<ItemRef> Items, string Summary, string Destination, string? TargetLabel, int HiddenMarked, bool FromResultSet,
    IReadOnlyList<PanelDestination>? Panels = null);

/// <summary>A panel's folder as a destination: its number, a short name, and the text that goes in the destination box.</summary>
public sealed record PanelDestination(int Number, string Name, string Destination);

public sealed record TransferDialogResult(string Destination, TransferOptions Options, bool Queue, bool IncludeHidden);

/// <param name="Unrecyclable">Examples (bounded); <paramref name="UnrecyclableCount"/> is the full count.</param>
public sealed record DeleteDialogInput(IReadOnlyList<ItemRef> Items, string Summary, bool Permanent, int HiddenMarked, IReadOnlyList<(string Name, string Why)> Unrecyclable, bool FromResultSet, int UnrecyclableCount = 0)
{
    public bool SkipDialog { get; init; }
}

public sealed record DeleteDialogResult(bool IncludeHidden, bool DeleteUnrecyclablePermanently);

/// <summary>Operation dialogs: concise, keyboard-first, and explicit about scope and destination (PI-05).</summary>
public static class OperationDialogs
{
    private static TextBlock Muted(string text) => new() { Text = text, Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Text(string text, bool bold = false) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left };

    /// <summary>The first names only: a captured selection may hold a million items.</summary>
    private static string NameList(IReadOnlyList<ItemRef> items, int max = 6)
    {
        var shown = string.Join(", ", Enumerable.Range(0, Math.Min(max, items.Count)).Select(i => "\"" + Formatters.SafeName(items[i].Name) + "\""));
        return items.Count > max ? $"{shown} and {items.Count - max:N0} more" : shown;
    }

    // ---- Copy / move ------------------------------------------------------------------------------------

    public static async Task<TransferDialogResult?> ShowTransferAsync(MainViewModel vm, TransferDialogInput input)
    {
        string verb = input.Kind == JobKind.Move ? "Move" : "Copy";
        var body = new StackPanel { Spacing = 6, MinWidth = 560 };
        // The title is the verb; the body says what (one item by name, several counted and then named).
        var single = input.Items.Count == 1 ? input.Items[0] : null;
        string what = single is null ? input.Summary
            : (single.IsContainer ? "the folder " : string.Empty) + $"\"{Formatters.SafeName(single.Name)}\"" +
              (single is { IsContainer: false, Size: >= 0 } ? $" ({Formatters.SizeWithUnit(single.Size)})" : string.Empty);
        body.Children.Add(Text($"{verb} {what}", bold: true));
        if (single is null) body.Children.Add(Muted(NameList(input.Items)));
        var toLabel = new TextBlock { Text = input.TargetLabel is null ? "To:" : $"To ({input.TargetLabel}):", Margin = new Thickness(0, 6, 0, 0) };
        body.Children.Add(toLabel);
        var dest = new TextBox { Text = input.Destination };
        AutomationProperties.SetName(dest, "Destination");
        // Typing a destination suggests the folders that complete it; choosing one fills it in (Enter then starts).
        // The box's own panel holds the suggestions' popup (the stack would space it like a row).
        var destHost = new Panel { Children = { dest } };
        body.Children.Add(destHost);
        // Several panels: each one's folder is a click (or Alt and its number) away, whichever panel is the target.
        if (input.Panels is { Count: > 1 } panels)
        {
            var row = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
            row.Children.Add(new TextBlock { Text = "Panels:", Classes = { "muted", "small" }, VerticalAlignment = VerticalAlignment.Center });
            var buttons = new List<(Button Button, PanelDestination Panel)>();
            foreach (var panel in panels)
            {
                var choice = panel;
                var button = new Button { Content = $"_{panel.Number} {panel.Name.Replace("_", "__", StringComparison.Ordinal)}", Padding = new Thickness(8, 2), MinHeight = 22 };
                ToolTip.SetTip(button, $"To panel {panel.Number}: {panel.Destination} (Alt+{panel.Number})");
                AutomationProperties.SetName(button, $"To panel {panel.Number}: {panel.Destination}");
                button.Click += (_, _) =>
                {
                    dest.Text = choice.Destination;
                    dest.CaretIndex = choice.Destination.Length;
                    toLabel.Text = $"To (panel {choice.Number}):";
                    dest.Focus();
                };
                buttons.Add((button, choice));
                row.Children.Add(button);
            }
            // The panel whose folder the box names stands out, typed or chosen.
            void Highlight()
            {
                foreach (var (button, panel) in buttons)
                    button.Classes.Set("accent", string.Equals((dest.Text ?? "").Trim(), panel.Destination, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            }
            dest.TextChanged += (_, _) => Highlight();
            Highlight();
            body.Children.Add(row);
        }
        var completion = new PathCompletion(dest, destHost, () => vm.Services.Settings.ShowHidden, () => input.Destination);
        completion.Chosen += path =>
        {
            dest.Text = path;
            dest.CaretIndex = path.Length;
        };
        var interpretation = Muted(string.Empty);
        body.Children.Add(interpretation);

        var conflicts = new ComboBox
        {
            ItemsSource = new[] { "Ask each time", "Skip existing", "Replace", "Replace if newer", "Keep both: rename the copy", "Keep both: rename the existing item" },
            SelectedIndex = 0,
            MinWidth = 260,
        };
        var verify = new ComboBox { ItemsSource = new[] { "Size and metadata (fast)", "Read back and compare content" }, SelectedIndex = vm.Services.Settings.DefaultVerify == "ReadBack" ? 1 : 0, MinWidth = 260 };
        var filter = new TextBox { PlaceholderText = "all files (mask, e.g. *.cs;*.axaml|*Test*)", MinWidth = 260 };
        var limit = new TextBox { PlaceholderText = "unlimited", MinWidth = 120 };
        var flatten = new CheckBox { Content = "Flatten: put all items directly into the destination (instead of keeping their folders)", IsVisible = input.FromResultSet };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        void Row(int r, string label, Control c)
        {
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 12, 3) };
            Grid.SetRow(l, r);
            Grid.SetRow(c, r);
            Grid.SetColumn(c, 1);
            c.Margin = new Thickness(0, 3);
            grid.Children.Add(l);
            grid.Children.Add(c);
        }
        Row(0, "If an item already exists:", conflicts);
        Row(1, "Verification:", verify);
        Row(2, "Only files matching:", filter);
        Row(3, "Speed limit (MB/s):", limit);
        var optionsPanel = new StackPanel { Spacing = 4 };
        optionsPanel.Children.Add(grid);
        optionsPanel.Children.Add(flatten);
        var expander = new Expander { Header = "Options", Content = optionsPanel, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(expander);
        CheckBox? includeHidden = null;
        if (input.HiddenMarked > 0)
        {
            includeHidden = new CheckBox { Content = $"Include {Formatters.Plural(input.HiddenMarked, "marked item", "marked items")} hidden by the filter", IsChecked = true };
            body.Children.Add(includeHidden);
        }
        var error = new TextBlock { Classes = { "error" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        body.Children.Add(error);
        body.Children.Add(Muted("Enter starts now · Queue waits for other operations on the same drive · the destination is fixed once started"));

        void Interpret()
        {
            var t = (dest.Text ?? string.Empty).Trim();
            if (t.Length == 0)
            {
                interpretation.Text = "Enter a destination folder.";
                return;
            }
            if (!vm.Services.Providers.TryParse(t, vm.ActiveTab?.Location, out var loc) || loc is null)
            {
                interpretation.Text = "Not a recognized location.";
                return;
            }
            if (!loc.IsFileSystem)
            {
                interpretation.Text = "→ " + vm.Services.Providers.Display(loc);
                return;
            }
            bool sep = t.EndsWith('\\') || t.EndsWith('/');
            // Asking a server about a folder can take long, and this runs as you type: network paths are checked when
            // the operation starts.
            if (IsNetworkPath(loc.Path))
            {
                interpretation.Text = $"→ {loc.Path} (on the network: checked when the operation starts)";
                return;
            }
            if (Directory.Exists(loc.Path)) interpretation.Text = $"→ into the folder {loc.Path}";
            else if (!sep && input.Items.Count == 1 && Directory.Exists(Path.GetDirectoryName(loc.Path) ?? ""))
                interpretation.Text = $"→ as \"{Path.GetFileName(loc.Path)}\" in {Path.GetDirectoryName(loc.Path)}";
            else interpretation.Text = $"→ the folder {loc.Path} will be created";
        }
        dest.TextChanged += (_, _) => Interpret();
        int historyIndex = -1;
        dest.KeyDown += (_, e) =>
        {
            var h = vm.Services.History.CopyDestinations;
            if (h.Count == 0 || (e.KeyModifiers & KeyModifiers.Alt) == 0 && e.Key is not (Key.Up or Key.Down)) return;
            if (e.Key == Key.Up && historyIndex < h.Count - 1) historyIndex++;
            else if (e.Key == Key.Down && historyIndex > 0) historyIndex--;
            else return;
            dest.Text = h[historyIndex];
            dest.CaretIndex = dest.Text.Length;
            e.Handled = true;
        };
        Interpret();
        Dispatcher.UIThread.Post(() => dest.SelectAll(), DispatcherPriority.Input);

        while (true)
        {
            var result = await vm.Dialogs.ShowCustomAsync(verb, body, [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Queue", "queue"), new DialogButton(verb, "start", IsDefault: true)], dest);
            if (result is not ("start" or "queue")) return null;
            Mask? mask = null;
            if (!string.IsNullOrWhiteSpace(filter.Text))
            {
                if (!Mask.TryParse(filter.Text, out var m, out var maskError))
                {
                    error.Text = "Filter: " + maskError;
                    error.IsVisible = true;
                    continue;
                }
                mask = m;
            }
            long rate = 0;
            if (!string.IsNullOrWhiteSpace(limit.Text))
            {
                if (!double.TryParse(limit.Text, out var mb) || mb < 0)
                {
                    error.Text = "Speed limit must be a number of megabytes per second.";
                    error.IsVisible = true;
                    continue;
                }
                rate = (long)(mb * 1024 * 1024);
            }
            if (string.IsNullOrWhiteSpace(dest.Text))
            {
                error.Text = "Enter a destination.";
                error.IsVisible = true;
                continue;
            }
            var options = new TransferOptions
            {
                Conflicts = conflicts.SelectedIndex switch
                {
                    1 => ConflictPolicy.Skip,
                    2 => ConflictPolicy.Replace,
                    3 => ConflictPolicy.ReplaceIfNewer,
                    4 => ConflictPolicy.KeepBothRenameIncoming,
                    5 => ConflictPolicy.KeepBothRenameExisting,
                    _ => ConflictPolicy.Ask,
                },
                Verify = verify.SelectedIndex == 1 ? VerifyMode.ReadBack : VerifyMode.Native,
                Filter = mask,
                RateLimit = rate,
                Flatten = flatten.IsChecked == true,
            };
            return new TransferDialogResult(dest.Text!.Trim(), options, result as string == "queue", includeHidden?.IsChecked != false);
        }
    }

    /// <summary>A server's share or a mapped network drive (Windows): what may take long to answer.</summary>
    internal static bool IsNetworkPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return true;
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            return Path.GetPathRoot(path) is { Length: > 0 } root && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // ---- Delete -------------------------------------------------------------------------------------------

    public static async Task<DeleteDialogResult?> ShowDeleteAsync(MainViewModel vm, DeleteDialogInput input)
    {
        var body = new StackPanel { Spacing = 6, MinWidth = 520 };
        string what = input.Items.Count == 1 ? $"\"{Formatters.SafeName(input.Items[0].Name)}\"" : input.Summary;
        body.Children.Add(Text(input.Permanent
            ? $"Delete {what} permanently? This cannot be undone."
            : $"Move {what} to the Recycle Bin?", bold: true));
        if (input.Items.Count > 1) body.Children.Add(Muted(NameList(input.Items, 8)));
        if (input.FromResultSet) body.Children.Add(Text("These are the original items at their locations, not just entries of the result set. To only drop them from the set, use \"Remove from result set\"."));
        CheckBox? includeHidden = null;
        if (input.HiddenMarked > 0)
        {
            includeHidden = new CheckBox { Content = $"Also delete {Formatters.Plural(input.HiddenMarked, "marked item", "marked items")} currently hidden by the filter", IsChecked = false };
            body.Children.Add(includeHidden);
        }
        RadioButton? deletePermanently = null;
        int unrecyclableCount = Math.Max(input.UnrecyclableCount, input.Unrecyclable.Count);
        if (unrecyclableCount > 0)
        {
            var warn = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
            warn.Children.Add(new TextBlock { Text = $"{Formatters.Plural(unrecyclableCount, "item", "items")} cannot go to the Recycle Bin:", Classes = { "warning" }, FontWeight = FontWeight.SemiBold });
            foreach (var (name, why) in input.Unrecyclable.Take(6)) warn.Children.Add(Muted($"• {Formatters.SafeName(name)} — {why}"));
            var skip = new RadioButton { Content = "Leave those items untouched", IsChecked = true, GroupName = "unrecyclable" };
            deletePermanently = new RadioButton { Content = "Delete those items permanently", GroupName = "unrecyclable" };
            warn.Children.Add(skip);
            warn.Children.Add(deletePermanently);
            body.Children.Add(warn);
        }
        var result = await vm.Dialogs.ShowCustomAsync(input.Permanent ? "Delete permanently" : "Delete", body,
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton(input.Permanent ? "Delete permanently" : "Delete", "ok", IsDefault: true, IsDanger: input.Permanent)]);
        if (result as string != "ok") return null;
        return new DeleteDialogResult(includeHidden?.IsChecked == true, deletePermanently?.IsChecked == true);
    }

    // ---- Decisions from running jobs -------------------------------------------------------------------

    public static async Task<Decision> ShowDecisionAsync(MainViewModel vm, PendingDecision pending)
    {
        var job = pending.Job;
        return pending.Request switch
        {
            ConflictRequest c => await ConflictAsync(vm, job, c),
            ErrorRequest e => await ErrorAsync(vm, job, e),
            ConfirmRequest cr => await ConfirmAsync(vm, job, cr),
            _ => new Decision(DecisionAction.CancelJob),
        };
    }

    private static async Task<Decision> ConflictAsync(MainViewModel vm, Job job, ConflictRequest c)
    {
        var body = new StackPanel { Spacing = 8, MinWidth = 600 };
        body.Children.Add(Muted(job.Title));
        // The title above usually names the destination already: the folder is repeated only when it says more.
        string folder = Path.GetDirectoryName(c.DestinationPath) ?? string.Empty;
        string where = folder.Length > 0 && job.Title.Contains(folder, StringComparison.OrdinalIgnoreCase) ? "there" : "in " + folder;
        body.Children.Add(Text($"\"{Formatters.SafeName(c.Message)}\" already exists {where}.", bold: true));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
        void Cell(int r, int col, string text, bool bold = false)
        {
            var t = new TextBlock { Text = text, Margin = new Thickness(0, 2, 16, 2), FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(t, r);
            Grid.SetColumn(t, col);
            grid.Children.Add(t);
        }
        Cell(0, 1, "Incoming", true);
        Cell(0, 2, "Existing", true);
        Cell(1, 0, "Size");
        Cell(1, 1, c.Incoming.IsDirectory ? "folder" : Formatters.ExactSize(c.Incoming.Size));
        Cell(1, 2, c.Existing.IsDirectory ? "folder" : Formatters.ExactSize(c.Existing.Size));
        Cell(2, 0, "Modified");
        Cell(2, 1, FormatTime(c.Incoming.ModifiedUtc) + (c.IncomingIsNewer ? "  (newer)" : ""));
        Cell(2, 2, FormatTime(c.Existing.ModifiedUtc) + (!c.IncomingIsNewer && c.Existing.ModifiedUtc > c.Incoming.ModifiedUtc ? "  (newer)" : ""));
        string incomingAttributes = Attr(c.Incoming.Attributes), existingAttributes = Attr(c.Existing.Attributes);
        if (incomingAttributes != Attr(0) || existingAttributes != Attr(0))
        {
            Cell(3, 0, "Attributes");
            Cell(3, 1, incomingAttributes);
            Cell(3, 2, existingAttributes);
        }
        body.Children.Add(grid);
        if (c.TypeMismatch) body.Children.Add(new TextBlock { Text = "A file and a folder cannot replace each other.", Classes = { "warning" } });
        if (c.SameItem) body.Children.Add(new TextBlock { Text = "Source and destination are the same item: only a renamed copy is possible.", Classes = { "warning" } });

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var compareResult = Muted(string.Empty);
        if (!c.Incoming.IsDirectory && !c.Existing.IsDirectory)
        {
            var viewIn = new Button { Content = "View incoming" };
            viewIn.Click += (_, _) => ViewerLauncher.OpenPath(vm.Services, c.SourcePath);
            var viewEx = new Button { Content = "View existing" };
            viewEx.Click += (_, _) => ViewerLauncher.OpenPath(vm.Services, c.DestinationPath);
            var compare = new Button { Content = "Compare content" };
            compare.Click += async (_, _) =>
            {
                compare.IsEnabled = false;
                compareResult.Text = "Comparing…";
                compareResult.Text = await Task.Run(() => CompareFiles(c.SourcePath, c.DestinationPath));
                compare.IsEnabled = true;
            };
            tools.Children.Add(viewIn);
            tools.Children.Add(viewEx);
            tools.Children.Add(compare);
        }
        body.Children.Add(tools);
        body.Children.Add(compareResult);
        var applyAll = new CheckBox { Content = "Do the same for other conflicts in this operation" };
        body.Children.Add(applyAll);

        var buttons = new List<DialogButton>();
        if (c.CanReplace)
        {
            buttons.Add(new DialogButton("Replace", DecisionAction.Replace, IsDefault: true));
            buttons.Add(new DialogButton("Replace if newer", DecisionAction.ReplaceIfNewer));
        }
        buttons.Add(new DialogButton("Skip", DecisionAction.Skip, IsDefault: !c.CanReplace && !c.SameItem));
        buttons.Add(new DialogButton($"Keep both{(c.SuggestedIncomingName is null ? "" : $" (\"{c.SuggestedIncomingName}\")")}", DecisionAction.KeepBothRenameIncoming, IsDefault: c.SameItem));
        if (c.SuggestedExistingName is not null && !c.TypeMismatch)
            buttons.Add(new DialogButton("Rename existing", DecisionAction.KeepBothRenameExisting));
        buttons.Add(new DialogButton("Cancel operation", DecisionAction.CancelJob, IsCancel: true));
        var r = await vm.Dialogs.ShowCustomAsync(c.Title, body, buttons);
        return r is DecisionAction a ? new Decision(a, applyAll.IsChecked == true) : new Decision(DecisionAction.CancelJob);
    }

    private static async Task<Decision> ErrorAsync(MainViewModel vm, Job job, ErrorRequest e)
    {
        var body = new StackPanel { Spacing = 6, MinWidth = 520 };
        body.Children.Add(Muted(job.Title));
        body.Children.Add(Text(e.Message));
        if (e.Path.Length > 0) body.Children.Add(Muted(e.Path));
        var applyAll = new CheckBox { Content = "Skip similar problems in this operation automatically" };
        body.Children.Add(applyAll);
        bool isAuth = e.ErrorClass == "access" && PathUtil.IsUncPath(e.Path);
        if (e.ErrorClass == "access" && !isAuth && OperatingSystem.IsWindows() &&
            Platform.Windows.Elevation.ElevationBroker.Locate(vm.Services.Paths.IsPortable, out _) is not null)
            body.Children.Add(Muted("To retry as administrator, choose Skip (with the checkbox for similar problems); when the operation ends, use Retry as administrator in the operations pane (Ctrl+J). One approval then covers every skipped item."));
        var buttons = new List<DialogButton>
        {
            new("Retry", DecisionAction.Retry, IsDefault: true),
            new("Skip", DecisionAction.Skip),
            new("Cancel operation", DecisionAction.CancelJob, IsCancel: true),
        };
        if (isAuth) buttons.Insert(0, new DialogButton("Sign in…", "signin"));
        while (true)
        {
            var r = await vm.Dialogs.ShowCustomAsync(e.Title, body, buttons);
            if (r as string == "signin")
            {
                await vm.SignInToServerAsync(PathUtil.GetUncServer(e.Path)!);
                continue;
            }
            if (r is DecisionAction a) return new Decision(a, applyAll.IsChecked == true && a == DecisionAction.Skip);
            return new Decision(DecisionAction.CancelJob);
        }
    }

    private static async Task<Decision> ConfirmAsync(MainViewModel vm, Job job, ConfirmRequest c)
    {
        var body = new StackPanel { Spacing = 6, MinWidth = 480 };
        body.Children.Add(Muted(job.Title));
        body.Children.Add(Text(c.Message));
        var applyAll = new CheckBox { Content = "Do the same for similar items in this operation" };
        body.Children.Add(applyAll);
        var buttons = c.Actions.Select((a, i) => new DialogButton(a switch
        {
            DecisionAction.Proceed => c.ProceedLabel ?? "Delete anyway",
            DecisionAction.KeepSource => "Copy instead (keep the original)",
            DecisionAction.Skip => "Skip",
            DecisionAction.FollowLink => "Copy the target's content",
            DecisionAction.CreateJunction => "Create a junction",
            DecisionAction.CancelJob => "Cancel operation",
            _ => a.ToString(),
        }, a, IsDefault: i == 0, IsCancel: a == DecisionAction.CancelJob)).ToList();
        var r = await vm.Dialogs.ShowCustomAsync(c.Title, body, buttons);
        return r is DecisionAction act ? new Decision(act, applyAll.IsChecked == true) : new Decision(DecisionAction.CancelJob);
    }

    private static string FormatTime(DateTime utc) => utc <= DateTime.MinValue.AddDays(1) ? "unknown" : Formatters.DateWithSeconds(utc.Ticks);

    private static string Attr(FileAttributes a)
    {
        var s = new List<string>();
        if ((a & FileAttributes.ReadOnly) != 0) s.Add("read-only");
        if ((a & FileAttributes.Hidden) != 0) s.Add("hidden");
        if ((a & FileAttributes.System) != 0) s.Add("system");
        if ((a & FileAttributes.ReparsePoint) != 0) s.Add("link");
        return s.Count == 0 ? "—" : string.Join(", ", s);
    }

    private static string CompareFiles(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return $"Different: sizes differ ({Formatters.ExactSize(fa.Length)} vs {Formatters.ExactSize(fb.Length)}).";
            var ha = PortableFileOperations.HashFile(a, HashAlgorithmName.SHA256, CancellationToken.None);
            var hb = PortableFileOperations.HashFile(b, HashAlgorithmName.SHA256, CancellationToken.None);
            return ha.AsSpan().SequenceEqual(hb) ? "Identical content (SHA-256 of both files matches)." : "Different content (same size, different SHA-256).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Could not compare: " + ErrorText.Describe(ex);
        }
    }

    // ---- Checksums ----------------------------------------------------------------------------------------

    public static async Task ShowChecksumsAsync(MainViewModel vm, IReadOnlyList<string> files)
    {
        ChecksumKind[] kinds = [ChecksumKind.Sha256, ChecksumKind.Sha512, ChecksumKind.Sha384, ChecksumKind.Sha1, ChecksumKind.Md5, ChecksumKind.Crc32];
        var algorithm = new ComboBox { ItemsSource = new[] { "SHA-256", "SHA-512", "SHA-384", "SHA-1 (compatibility)", "MD5 (compatibility)", "CRC-32 (compatibility)" }, SelectedIndex = 0 };
        Avalonia.Automation.AutomationProperties.SetName(algorithm, "Checksum algorithm");
        var output = new TextBox { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"), MinHeight = 160, MaxHeight = 360, TextWrapping = TextWrapping.NoWrap, MinWidth = 640 };
        Avalonia.Automation.AutomationProperties.SetName(output, "Checksums");
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, IsVisible = false };
        var save = new Button { Content = "Save as manifest…", IsEnabled = false };
        // The value a download page gives, pasted: compared with what the file has (the algorithm follows from its length).
        var expected = new TextBox { Watermark = "Paste a checksum to compare with, such as the one on the download page", FontFamily = output.FontFamily };
        Avalonia.Automation.AutomationProperties.SetName(expected, "Compare with");
        var verdict = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(Muted("Checksums verify integrity; MD5, SHA-1, and CRC-32 are compatibility checks, not proof of origin."));
        body.Children.Add(algorithm);
        body.Children.Add(progress);
        body.Children.Add(output);
        body.Children.Add(expected);
        body.Children.Add(verdict);
        body.Children.Add(save);
        var results = new List<(string Path, string Hash)>();
        var computedKind = ChecksumKind.Sha256;
        bool computing = false;
        CancellationTokenSource? cts = null;
        void Compare()
        {
            string text = expected.Text ?? "";
            verdict.IsVisible = text.Trim().Length > 0;
            if (!verdict.IsVisible) return;
            verdict.Classes.Set("success", false);
            verdict.Classes.Set("error", false);
            if (Checksums.ParseExpected(text) is not { } wanted)
            {
                verdict.Text = "Not a checksum FileCat computes: it takes 8 (CRC-32), 32 (MD5), 40 (SHA-1), 64 (SHA-256), 96 (SHA-384), or 128 (SHA-512) hexadecimal digits. SHA-3 and BLAKE values have the same lengths but are other algorithms.";
                return;
            }
            string name = Checksums.Name(wanted.Kind);
            if (computing || computedKind != wanted.Kind || results.Count < files.Count)
            {
                verdict.Text = $"A {name} value: compared when the file's {name} is ready…";
                return;
            }
            var same = results.Where(r => string.Equals(r.Hash, wanted.Hex, StringComparison.OrdinalIgnoreCase)).ToList();
            string weak = Checksums.IsCompatibilityOnly(wanted.Kind) ? $" ({name} shows the file is intact, not that it is authentic: it can be forged.)" : "";
            if (same.Count > 0)
            {
                verdict.Classes.Set("success", true);
                verdict.Text = (files.Count == 1 ? $"✓ Matches: the file's {name} is the one pasted." : $"✓ Matches {string.Join(", ", same.Select(s => Path.GetFileName(s.Path)))}.") + weak;
            }
            else
            {
                verdict.Classes.Set("error", true);
                verdict.Text = files.Count == 1 ? $"✗ Does not match: the file's {name} is another value." : $"✗ Matches none of the {files.Count:N0} files.";
            }
        }
        async Task Compute()
        {
            cts?.Cancel();
            cts = new CancellationTokenSource();
            var token = cts.Token;
            save.IsEnabled = false;
            results.Clear();
            progress.IsVisible = true;
            output.Text = string.Empty;
            computing = true;
            Compare();
            var kind = kinds[Math.Max(0, algorithm.SelectedIndex)];
            long total = files.Sum(f => new FileInfo(f).Length), done = 0;
            var lines = new List<string>();
            try
            {
                foreach (var f in files)
                {
                    var hash = await Task.Run(() =>
                    {
                        var before = Core.Verification.VerificationService.Stamp(f);
                        string value = Checksums.Compute(f, kind, token, n =>
                        {
                            long now = Interlocked.Add(ref done, n);
                            Dispatcher.UIThread.Post(() => progress.Value = total > 0 ? 100.0 * now / total : 0);
                        });
                        // Kept for the checks beside files (D-57): a checksum file added later need not read it again.
                        if (before is { } stamp) Core.Verification.VerificationService.Current?.Remember(f, stamp, new Dictionary<ChecksumKind, string> { [kind] = value });
                        return value;
                    }, token);
                    if (token.IsCancellationRequested) return;
                    results.Add((f, hash));
                    lines.Add($"{hash}  {Path.GetFileName(f)}");
                    output.Text = string.Join(Environment.NewLine, lines);
                }
                computedKind = kind;
                save.IsEnabled = results.Count > 0;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                output.Text += Environment.NewLine + "Error: " + ErrorText.Describe(ex);
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    computing = false;
                    progress.IsVisible = false;
                    Compare();
                }
            }
        }
        save.Click += async (_, _) => await SaveManifestAsync(vm, computedKind, results.ToList());
        algorithm.SelectionChanged += async (_, _) => await Compute();
        expected.TextChanged += (_, _) =>
        {
            // A pasted value of another length picks its algorithm (and computes it); the same one compares at once.
            if (Checksums.ParseExpected(expected.Text) is { } wanted && Array.IndexOf(kinds, wanted.Kind) is var index and >= 0 && index != algorithm.SelectedIndex)
                algorithm.SelectedIndex = index;
            else Compare();
        };
        // A checksum on the clipboard is most likely the one to compare with: it is filled in (and picks the algorithm).
        if (vm.View.TopLevel?.Clipboard is { } clipboard)
        {
            try
            {
                string? copied = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
                if (copied is { Length: < 400 } && Checksums.ParseExpected(copied) is not null) expected.Text = copied.Trim();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        }
        if (cts is null) _ = Compute(); // unless a pasted value of another length started it
        var r = await vm.Dialogs.ShowCustomAsync($"Checksums of {Formatters.Plural(files.Count, "file", "files")}", body,
            [new DialogButton("Copy", "copy"), new DialogButton("Close", "close", IsDefault: true, IsCancel: true)]);
        cts?.Cancel();
        if (r as string == "copy" && output.Text is { Length: > 0 } text) vm.CopyTextToClipboard(text);
    }

    /// <summary>
    /// Writes a manifest that other tools read too (GNU "hash  name", SFV for CRC-32), next to the files or in their
    /// common folder with relative names. An existing file is replaced only after asking.
    /// </summary>
    private static async Task SaveManifestAsync(MainViewModel vm, ChecksumKind kind, IReadOnlyList<(string Path, string Hash)> results)
    {
        string root = Path.GetDirectoryName(results[0].Path)!;
        foreach (var (path, _) in results)
        {
            while (!PathUtil.IsSameOrUnder(path, root)) root = Path.GetDirectoryName(root) ?? root;
        }
        string suggested = results.Count == 1 ? Path.GetFileName(results[0].Path) + Checksums.Extension(kind)
            : (Path.GetFileName(root.TrimEnd('\\', '/')) is { Length: > 0 } folder ? folder : "checksums") + Checksums.Extension(kind);
        var answer = await vm.Dialogs.PromptAsync(new PromptOptions("Save as manifest", $"File name in {root}:")
        {
            Text = suggested,
            Validate = t => PathUtil.ValidateNewName(t.Trim()),
        });
        if (answer is null) return;
        string target = Path.Combine(root, answer.Text.Trim());
        var lines = results.Select(r => Checksums.ManifestLine(kind, r.Hash, Path.GetRelativePath(root, r.Path)));
        if (kind == ChecksumKind.Crc32) lines = lines.Prepend("; Generated by FileCat");
        string content = string.Join("\n", lines) + "\n";
        try
        {
            if (File.Exists(target))
            {
                if (!await vm.Dialogs.ConfirmAsync("Replace manifest", $"\"{Path.GetFileName(target)}\" already exists. Replace it?", "Replace", danger: true)) return;
                await File.WriteAllTextAsync(target, content, new System.Text.UTF8Encoding(false));
            }
            else
            {
                await using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
                await writer.WriteAsync(content);
            }
            vm.Notify($"Saved {Formatters.Plural(results.Count, "checksum", "checksums")} to {Path.GetFileName(target)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            vm.Notify("The manifest was not saved: " + ErrorText.Describe(ex), true);
        }
    }
}
