using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

/// <summary>
/// "All dialogs respect all themes": every dialog layout FileCat shows, and the main window's dialogs themselves, read in
/// each palette and keep reading when the theme changes while they are open (System following the OS, the theme picker,
/// the Settings preview). <see cref="ThemeAudit"/> says what is checked.
/// </summary>
[Collection("Shared theme state")]
public sealed class DialogThemeTests(ITestOutputHelper output)
{
    public static TheoryData<string> Themes()
    {
        var data = new TheoryData<string>();
        foreach (var palette in ThemePalette.All) data.Add(palette.Name);
        return data;
    }

    /// <summary>The theme a live switch goes to: each palette is left once and reached once.</summary>
    private static string Next(string theme)
    {
        var all = ThemePalette.All;
        int index = all.ToList().FindIndex(palette => palette.Name == theme);
        return all[(index + 1) % all.Count].Name;
    }

    private static void Settle(Window window)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static DialogCard TopCard(Visual root) =>
        root.GetVisualDescendants().OfType<DialogCard>().Last(card => card.IsEffectivelyVisible && card.IsEffectivelyEnabled);

    /// <summary>Audits the topmost dialog, with its first combo box's list open; returns how many texts it read.</summary>
    private static int Audit(Window window, string label, List<string> problems, bool focus)
    {
        Settle(window);
        var card = TopCard(window);
        var combo = card.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(box =>
            box.IsEffectivelyVisible && box.IsEffectivelyEnabled && box.ItemCount > 0 && box.TemplatedParent is null);
        if (combo is not null)
        {
            combo.IsDropDownOpen = true;
            Settle(window);
        }
        problems.AddRange(ThemeAudit.Problems(card, label));
        if (combo is not null)
        {
            combo.IsDropDownOpen = false;
            Settle(window);
        }
        if (focus) problems.AddRange(ThemeAudit.FocusProblems(card, label));
        return card.GetVisualDescendants().OfType<TextBlock>().Count(text => text.IsEffectivelyVisible &&
            (!string.IsNullOrWhiteSpace(text.Text) || text.Inlines is { Count: > 0 }));
    }

    private static TextBlock Wrapped(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    /// <summary>Every kind of dialog the overlay service builds, plus representative custom dialogs, in their rarer states.</summary>
    private static List<(string Name, Func<Task> Open, Action<DialogCard>? Prepare)> Layouts(OverlayDialogService dialogs)
    {
        var approve = new CheckBox { Content = "Approve the reviewed rule removals and inherited replacements", IsChecked = true };
        return
        [
            ("Prompt with a refused value", () => dialogs.PromptAsync(new PromptOptions("Rename", "New name for report.txt")
            {
                Text = "report.txt", CheckboxText = "Keep the extension", Hint = "Enter renames", History = ["old.txt"],
                Validate = text => text.Contains('/') ? "A name cannot contain a slash." : null,
            }), card =>
            {
                var box = card.GetVisualDescendants().OfType<TextBox>().First();
                box.Text = "bad/name.txt";
                box.SelectAll();
            }),
            ("Confirmation", () => dialogs.ConfirmAsync("Copy", @"Copy 2 items to D:\backup?", "Copy"), null),
            ("Destructive confirmation", () => dialogs.ConfirmAsync("Delete permanently", "Delete 2 items permanently?\n\nThis cannot be undone.",
                "Delete", danger: true), null),
            ("Review with key points", () => dialogs.ConfirmReviewAsync("Apply attributes",
                "Source: C:\\fixture\\a long folder\\report.txt\nTarget: D:\\backup\\report.txt\n\nEffects:\nRead-only set · Hidden cleared",
                ["Apply to 3 items", "Existing items: keep", @"Scope: C:\fixture"]), null),
            ("Review with warnings and details", () => dialogs.ConfirmWithCancellationAsync("Reset descendant ACLs",
                "Full review:\nSource path: C:\\fixture\\parent\nTarget path: C:\\fixture\\parent\\child\n\nSafety notes:\nProtected branches are excluded.",
                "Reset", danger: true, CancellationToken.None, ["Reset 4 ACLs", "Existing items: replaced", "Owner: unchanged"],
                warnings: ["Explicit rules are removed.", "Inherited replacements follow the parent."], collapseDetails: true), card =>
            {
                card.GetVisualDescendants().OfType<ToggleButton>().First(toggle => toggle.Name == "ReviewDetailsToggle").IsChecked = true;
            }),
            ("Alert", () => dialogs.AlertAsync("Copy failed", "Could not copy report.txt.\n\nAccess is denied."), null),
            ("Chooser", async () => await dialogs.ChooseAsync(new ChoiceOptions("Go to",
            [
                new ChoiceItem("Drives") { IsHeader = true },
                new ChoiceItem(@"C:\", "Local disk · 120 GB free", "Alt+1"),
                new ChoiceItem(@"D:\", "Backup disk", "Alt+2") { Pinned = true },
                new ChoiceItem(@"\\server\share", "Network share"),
            ]) { SelectedIndex = 1, AllowPin = true, AllowDelete = true }), null),
            ("Keyboard reference", async () => await dialogs.KeyboardReferenceAsync(
            [
                new KeyboardHelpEntry(CommandIds.Copy, "Copy", "File", "F5", "Copy the marked items to the target panel."),
                new KeyboardHelpEntry(CommandIds.Move, "Move", "File", "F6", "Move or rename.", ContextLabel: "Panel"),
                new KeyboardHelpEntry(CommandIds.Settings, "Settings", "Application", null, "Open the settings.", Enabled: false, UnavailableReason: "A dialog is open."),
            ], CommandIds.Copy), null),
            ("Form controls", async () => await dialogs.ShowCustomAsync("Form controls", FormBody(),
                [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Discard", "discard", IsDanger: true), new DialogButton("Apply", "apply", IsDefault: true)]), null),
            ("Reviewed change list", async () => await dialogs.ShowCustomAsync("Reset Windows descendant ACLs", ReviewBody(approve),
                [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Reset captured ACLs", "apply", IsDanger: true) { IsAvailable = () => approve.IsChecked == true }],
                resizable: true, behavior: new DialogBehavior { DismissOnBackdrop = false, ConfirmOnListItemEnter = false, PreferredSize = new Size(760, 665) }), null),
        ];
    }

    /// <summary>The controls FileCat's custom dialogs are made of (Settings, copy and attribute dialogs, editors), each in a less common state.</summary>
    private static Control FormBody()
    {
        var name = new TextBox { PlaceholderText = "Folder name" };
        var kind = new ComboBox { ItemsSource = new[] { "Size and metadata (fast)", "Read back and compare content" }, SelectedIndex = 1, MinWidth = 220 };
        var size = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 12, Width = 130, HorizontalAlignment = HorizontalAlignment.Left };
        var tabs = new TabControl { Items = { new TabItem { Header = "Appearance", Content = Wrapped("First page") }, new TabItem { Header = "Behavior", Content = Wrapped("Second page") } } };
        var details = new ToggleButton { Content = "Hide details", IsChecked = true };
        var link = new HyperlinkButton { Content = "support@example.invalid", NavigateUri = new Uri("mailto:support@example.invalid") };
        var disabled = new Button { Content = "Unavailable action", IsEnabled = false };
        var expander = new Expander { Header = "Options", IsExpanded = true, Content = new CheckBox { Content = "Verify after copying", IsChecked = true } };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new DialogForm(("Name", name), ("Verification", kind), ("Size", size)));
        body.Children.Add(new CheckBox { Content = "Show hidden and system items (dimmed)", IsChecked = true });
        body.Children.Add(new CheckBox { Content = "Three-state option", IsThreeState = true, IsChecked = null });
        body.Children.Add(new RadioButton { Content = "Overwrite older", GroupName = "conflict", IsChecked = true });
        body.Children.Add(new RadioButton { Content = "Skip", GroupName = "conflict" });
        body.Children.Add(expander);
        body.Children.Add(tabs);
        body.Children.Add(new TextBlock { Text = "A note in the muted color.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "A small muted hint.", Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "The target folder does not exist.", Classes = { "error" }, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "Existing files will be replaced.", Classes = { "warning" }, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "All checksums match.", Classes = { "success" }, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBox { Text = "*.tmp;*.bak", Classes = { "warning" } });
        body.Children.Add(OverlayDialogService.ReviewLine(@"Path: C:\fixture\a long folder\report.txt"));
        body.Children.Add(DialogLayout.Actions(details, link, disabled));
        body.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = 40, Height = 6 });
        return body;
    }

    /// <summary>
    /// The layout of the reviewed permission changes (the Windows ACL reset, "Convert to automatic inheritance", the tree and
    /// reviewed-change dialogs): wrapped facts, a target list with its row chosen, the rules it changes, and an approval.
    /// </summary>
    private static Control ReviewBody(CheckBox approve)
    {
        var preview = new StackPanel { Spacing = 8 };
        preview.Children.Add(Wrapped(@"C:\fixture\a long folder name\selected"));
        preview.Children.Add(Wrapped("2 ACL changes among 3 captured descendants. This review covers the listed objects. Protected branches are excluded."));
        var rows = new[] { @"C:\fixture\selected\captured.txt · Reset ACL", @"C:\fixture\selected\kept.txt · Already matches", @"C:\fixture\selected\sub · Reset ACL" };
        var targets = new ListBox { MaxHeight = 160, ItemsSource = rows, ItemTemplate = new FuncDataTemplate<string>((row, _) => Wrapped(row ?? string.Empty)) };
        AutomationProperties.SetName(targets, "Captured Windows ACL reset targets");
        var rules = new ListBox { MaxHeight = 230, ItemTemplate = new FuncDataTemplate<string>((rule, _) => Wrapped(rule ?? string.Empty)),
            ItemsSource = new[] { "Remove explicit · 1 · Allow · S-1-5-32-546 · ReadAttributes", "Replacement inherited · 1 · Allow · S-1-5-21-1 · FullControl" } };
        AutomationProperties.SetName(rules, "Removed and replacement access rules");
        preview.Children.Add(targets);
        preview.Children.Add(rules);
        targets.SelectedIndex = 0;
        preview.Children.Add(approve);
        return preview;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Themes))]
    public async Task Built_in_dialog_layouts_read_in_the_theme_and_after_a_live_switch(string theme)
    {
        string before = ThemeManager.RequestedName;
        var fallback = new TextBox { Text = "fallback" };
        var host = new Grid();
        var window = new Window { Width = 1100, Height = 950, Content = new Grid { Children = { fallback, host } } };
        window.Styles.Add(ThemeAudit.ProductStyles());
        window.Show();
        fallback.Focus();
        var dialogs = new OverlayDialogService(host, () => fallback);
        var problems = new List<string>();
        try
        {
            foreach (var (name, open, prepare) in Layouts(dialogs))
            {
                ThemeManager.Apply(theme);
                Settle(window);
                var pending = open();
                Settle(window);
                prepare?.Invoke(TopCard(window));
                int texts = Audit(window, $"{theme}/{name}", problems, focus: true);
                Assert.True(texts > 0, $"{name} showed no text");
                ThemeManager.Apply(Next(theme));
                Audit(window, $"{theme}→{Next(theme)}/{name}", problems, focus: false);
                dialogs.CancelAll();
                await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                output.WriteLine($"{theme} · {name}: {texts} texts read, then again in {Next(theme)}");
            }
        }
        finally
        {
            dialogs.CancelAll();
            Dispatcher.UIThread.RunJobs();
            window.Close();
            ThemeManager.Apply(before);
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Distinct().Take(80)));
    }

    /// <summary>The main window's own dialogs, opened as people open them: by command.</summary>
    private static readonly (string Name, Func<MainViewModel, Task?> Open)[] MainWindowDialogs =
    [
        ("Settings", vm => Run(vm, CommandIds.Settings)),
        ("Theme picker", vm => Run(vm, CommandIds.ThemePick)),
        ("About", vm => Run(vm, CommandIds.About)),
        ("Keyboard reference", vm => Run(vm, CommandIds.Help)),
        ("Change shortcut", vm => Run(vm, CommandIds.ChangeShortcut)),
        ("Copy", vm => Run(vm, CommandIds.Copy)),
        ("Move", vm => Run(vm, CommandIds.Move)),
        ("Delete permanently", vm => Run(vm, CommandIds.DeletePermanent)),
        ("Make folder", vm => Run(vm, CommandIds.MakeDirectory)),
        ("Attributes", vm => Run(vm, CommandIds.Attributes)),
        ("Storage attributes", vm => Run(vm, CommandIds.StorageAttributes)),
        ("Create link", vm => Run(vm, CommandIds.CreateLink)),
        ("Bulk rename", vm => Run(vm, CommandIds.BulkRename)),
        ("Apply command", vm => Run(vm, CommandIds.ApplyCommand)),
        ("Checksum", vm => Run(vm, CommandIds.Checksum)),
        ("Pack", vm => Run(vm, CommandIds.Pack)),
        ("Select by mask", vm => Run(vm, CommandIds.MarkSelectMask)),
        ("Go to", vm => Run(vm, CommandIds.GoTo)),
        ("Location menu", vm => Run(vm, CommandIds.LocationMenuSource)),
        ("Folder history", vm => Run(vm, CommandIds.FolderHistory)),
        ("Tab list", vm => Run(vm, CommandIds.TabList)),
        ("SFTP connection", vm => Run(vm, CommandIds.SftpConnect)),
        ("Permissions and ownership", vm => Run(vm, CommandIds.PermissionsOwnership)),
        ("File list", vm => Run(vm, CommandIds.CreateFileList)),
        ("Working sets", vm => Run(vm, CommandIds.WorkingSets)),
        ("Bookmarks", vm => Run(vm, CommandIds.Bookmarks)),
        ("Recent notifications", vm => Run(vm, CommandIds.RecentNotifications)),
        ("Save workspace", vm => Run(vm, CommandIds.SaveWorkspace)),
        ("Load workspace", vm => Run(vm, CommandIds.LoadWorkspace)),
        ("Unselect by mask", vm => Run(vm, CommandIds.MarkUnselectMask)),
        ("Find folder", vm => Run(vm, CommandIds.FindFolder)),
        ("Command history", vm => Run(vm, CommandIds.CommandHistory)),
        ("Choose target", vm => Run(vm, CommandIds.ChooseTarget)),
        ("User menu", vm => Run(vm, CommandIds.UserMenu)),
        ("Edit sessions", vm => Run(vm, CommandIds.EditSessions)),
        ("Clear history", vm => Run(vm, CommandIds.ClearHistory)),
        ("Operation issue details", vm => OperationIssueDialog.ShowAsync(vm, IssueJob(out var issue), issue)),
    ];

    private static Task? Run(MainViewModel vm, string command)
    {
        vm.Execute(command);
        return null;
    }

    private static Job IssueJob(out JobIssue issue)
    {
        var job = new Job(new JobRequest { Kind = JobKind.Move }, "Move", "local", [], []);
        issue = new JobIssue(IssueSeverity.Error, @"C:\fixture\report.txt", "Copy failed\nAccess is denied", StepOutcome.Failed)
        {
            SourcePath = @"C:\fixture\report.txt", TargetPath = @"D:\backup\report.txt", RequestedAction = "Move",
            CommittedEffect = "Copy failed; no published copy confirmed", SourceDisposition = "Original kept",
            TargetDisposition = "Target state not confirmed", Certainty = "Failure reported by copy", Cause = "access",
        };
        job.AddIssue(issue);
        return job;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Themes))]
    public async Task Main_window_dialogs_read_in_the_theme_and_after_a_live_switch(string theme)
    {
        string before = ThemeManager.RequestedName;
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        window.Styles.Add(ThemeAudit.ProductStyles());
        var dialogs = (OverlayDialogService)vm.Dialogs;
        var problems = new List<string>();
        var opened = new List<string>();
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 3); i++) await Task.Delay(20, ct);
            listing.SetFocus(1); // a file, so the file commands open their dialogs
            foreach (var (name, open) in MainWindowDialogs)
            {
                ThemeManager.Apply(theme);
                Settle(window);
                var pending = open(vm);
                for (int i = 0; i < 150 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
                if (!dialogs.IsOpen)
                {
                    output.WriteLine($"{theme} · {name}: no dialog here");
                    continue;
                }
                await Task.Delay(80, ct); // content that fills in after opening
                opened.Add(name);
                int texts = Audit(window, $"{theme}/{name}", problems, focus: true);
                if (name == "Settings")
                {
                    var tabs = TopCard(window).GetVisualDescendants().OfType<TabControl>().First();
                    for (int page = 1; page < tabs.ItemCount; page++)
                    {
                        tabs.SelectedIndex = page;
                        Settle(window);
                        texts += Audit(window, $"{theme}/Settings page {page}", problems, focus: false);
                    }
                    tabs.SelectedIndex = 0;
                }
                ThemeManager.Apply(Next(theme));
                Audit(window, $"{theme}→{Next(theme)}/{name}", problems, focus: false);
                for (int i = 0; i < 250 && dialogs.IsOpen; i++)
                {
                    dialogs.CancelAll();
                    await Task.Delay(20, ct);
                }
                Assert.False(dialogs.IsOpen, name + " did not close");
                if (pending is not null) await pending.WaitAsync(TimeSpan.FromSeconds(10), ct);
                output.WriteLine($"{theme} · {name}: {texts} texts read, then again in {Next(theme)}");
            }
        }
        finally
        {
            dialogs.CancelAll();
            AccessibilityTests.Close(services, window, root);
            ThemeManager.Apply(before);
        }
        foreach (string required in new[] { "Settings", "Theme picker", "About", "Keyboard reference", "Copy", "Make folder", "Attributes",
                     "Create link", "Bulk rename", "Checksum", "Select by mask", "Location menu", "SFTP connection", "Operation issue details" })
            Assert.Contains(required, opened);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Distinct().Take(80)));
    }

    /// <summary>
    /// About dresses its header in the theme: the effect themes draw their art behind the name, which the brush audit
    /// cannot see. The words are measured over that art as FileCat draws it (Skia, the psychedelic light across its cycle),
    /// through the glass under them. A theme change while About is open redresses the header and renames the theme fact.
    /// </summary>
    [AvaloniaFact]
    public async Task About_header_reads_over_each_theme_dress_and_follows_a_live_switch()
    {
        string before = ThemeManager.RequestedName;
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 820);
        window.Styles.Add(ThemeAudit.ProductStyles());
        var dialogs = (OverlayDialogService)vm.Dialogs;
        var problems = new List<string>();
        var ct = TestContext.Current.CancellationToken;
        try
        {
            foreach (var palette in ThemePalette.All)
            {
                ThemeManager.Apply(palette.Name);
                var pending = AboutDialog.ShowAsync(vm);
                Settle(window);
                Measure(palette, palette.Name);
                var next = ThemePalette.Find(Next(palette.Name))!;
                ThemeManager.Apply(next.Name);
                Settle(window);
                Measure(next, $"{palette.Name}→{next.Name}");
                Assert.Contains(TopCard(window).GetVisualDescendants().OfType<SelectableTextBlock>(), value => value.Text == next.DisplayName);
                Assert.DoesNotContain(TopCard(window).GetVisualDescendants().OfType<SelectableTextBlock>(), value => value.Text == palette.DisplayName);
                dialogs.CancelAll();
                await pending.WaitAsync(TimeSpan.FromSeconds(10), ct);
            }
        }
        finally
        {
            dialogs.CancelAll();
            AccessibilityTests.Close(services, window, root);
            ThemeManager.Apply(before);
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Distinct().Take(80)));

        void Measure(ThemePalette palette, string label)
        {
            var card = TopCard(window);
            problems.AddRange(ThemeAudit.Problems(card, label + "/About"));
            var band = card.GetVisualDescendants().OfType<Border>().First(border => border.Height == 132);
            bool dressed = band.GetVisualDescendants().OfType<ThemeBackdrop>().Any();
            Assert.Equal(palette.Effect != ThemeEffect.None, dressed);
            Assert.Equal(palette.Name == ThemePalette.DosCommander.Name ? 0 : 1, band.GetVisualDescendants().OfType<Image>().Count());
            if (!dressed) return;
            int width = (int)Math.Ceiling(band.Bounds.Width), height = (int)Math.Ceiling(band.Bounds.Height);
            var words = band.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text)).ToList();
            Assert.Equal(3, words.Count);
            var phases = palette.Effect == ThemeEffect.Psychedelic ? Enumerable.Range(0, 24).Select(step => step * 7.5) : [0.0];
            foreach (double seconds in phases)
            {
                using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(width, height));
                var backing = Color.Parse(palette.Window);
                surface.Canvas.Clear(new SkiaSharp.SKColor(backing.R, backing.G, backing.B));
                var rect = new SkiaSharp.SKRect(0, 0, width, height);
                switch (palette.Effect)
                {
                    case ThemeEffect.Cyberpunk: EffectArt.Cyberpunk(surface.Canvas, rect); break;
                    case ThemeEffect.Psychedelic: EffectArt.Psychedelic(surface.Canvas, rect, seconds); break;
                    case ThemeEffect.Steampunk: EffectArt.Steampunk(surface.Canvas, rect, 26, 106, 0, width); break;
                }
                using var snapshot = surface.Snapshot();
                using var art = SkiaSharp.SKBitmap.FromImage(snapshot);
                foreach (var text in words)
                {
                    // The glass between the words and the art, outermost first.
                    var glass = text.GetVisualAncestors().TakeWhile(node => !ReferenceEquals(node, band)).OfType<Border>()
                        .Select(border => border.Background).OfType<ISolidColorBrush>().Reverse().Select(brush => brush.Color).ToList();
                    var inks = text.Foreground switch
                    {
                        ISolidColorBrush solid => [solid.Color],
                        IGradientBrush gradient => gradient.GradientStops.Select(stop => stop.Color).ToList(),
                        _ => new List<Color>(),
                    };
                    var origin = text.TranslatePoint(default, band)!.Value;
                    double worst = 21;
                    for (double y = 0; y < text.Bounds.Height; y += 2)
                        for (double x = 0; x < text.Bounds.Width; x += 2)
                        {
                            int px = (int)(origin.X + x), py = (int)(origin.Y + y);
                            if (px < 0 || py < 0 || px >= width || py >= height) continue;
                            var pixel = art.GetPixel(px, py);
                            var under = Color.FromRgb(pixel.Red, pixel.Green, pixel.Blue);
                            foreach (var pane in glass) under = ThemeManager.Opaque(pane, under);
                            foreach (var ink in inks) worst = Math.Min(worst, ThemeManager.Contrast(ThemeManager.Opaque(ink, under), under));
                        }
                    bool large = text.FontSize >= 24 || text.FontSize >= 18.66 && text.FontWeight >= FontWeight.Bold;
                    if (worst < (large ? 3 : 4.5))
                        problems.Add($"{label}/About: \"{text.Text}\" over the {palette.Effect} art at {seconds:0}s is {worst:0.00}:1");
                }
            }
        }
    }

    /// <summary>
    /// The text styles dialogs use, on every surface a dialog has: the card, its header, the review highlight and warning
    /// boxes, the primary action card, a list, a chosen row of a chooser and of a list. Each palette, each surface.
    /// </summary>
    [AvaloniaFact]
    public void Dialog_text_styles_read_on_every_dialog_surface_in_every_theme()
    {
        string before = ThemeManager.RequestedName;
        var problems = new List<string>();
        try
        {
            foreach (var palette in ThemePalette.All)
            {
                ThemeManager.Apply(palette.Name);
                Control Samples() => new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "Plain text" },
                        new TextBlock { Text = "Muted text", Classes = { "muted" } },
                        new TextBlock { Text = "Small muted text", Classes = { "muted", "small" } },
                        new TextBlock { Text = "Error text", Classes = { "error" } },
                        new TextBlock { Text = "Warning text", Classes = { "warning" } },
                        new TextBlock { Text = "Success text", Classes = { "success" } },
                        OverlayDialogService.ReviewLine(@"Path: C:\fixture\report.txt"),
                    },
                };
                ListBox List(params string[] classes)
                {
                    var list = new ListBox { ItemsSource = new[] { "first", "second" }, ItemTemplate = new FuncDataTemplate<string>((_, _) => Samples()) };
                    foreach (string name in classes) list.Classes.Add(name);
                    list.SelectedIndex = 1;
                    return list;
                }
                var body = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        Samples(),
                        new Border { Classes = { "reviewHighlights" }, Child = Samples() },
                        new Border { Classes = { "reviewWarnings" }, Child = Samples() },
                        new Border { Classes = { "reviewActionCard" }, Child = Samples() },
                        new Border { Classes = { "reviewDetailSection" }, Child = Samples() },
                        List(),
                        List("choices"),
                    },
                };
                var footer = new WrapPanel { Children = { new Button { Content = "Cancel" }, new Button { Content = "Apply", Classes = { "primary" } },
                    new Button { Content = "Delete", Classes = { "danger" } } } };
                var card = new DialogCard("Every surface", body, footer, 900);
                var window = new Window { Width = 960, Height = 2600, Content = card };
                window.Styles.Add(ThemeAudit.ProductStyles());
                window.Show();
                try
                {
                    Settle(window);
                    problems.AddRange(ThemeAudit.Problems(card, palette.Name));
                }
                finally { window.Close(); }
            }
        }
        finally { ThemeManager.Apply(before); }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Distinct().Take(80)));
    }
}
