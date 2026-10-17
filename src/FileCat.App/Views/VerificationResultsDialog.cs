using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;
using FileCat.Core.Verification;

namespace FileCat.App.Views;

/// <summary>
/// What "Verify checksums and signatures" found (D-57), shown whenever the check ends: the verdict, every file checked
/// (problems first) with the mark its icon shows, and the selected file's claims in full: each checksum's expected and
/// computed values and which file and line state it, each signature and who made it, and what a match does not show.
/// The whole report copies as text; failing files open as a list in a panel.
/// </summary>
internal static class VerificationResultsDialog
{
    /// <summary>The answer when the failing files are to be listed in a panel.</summary>
    public const string ShowFailed = "show";
    private static readonly FontFamily Mono = new("Cascadia Mono,Consolas,Menlo,monospace");
    /// <summary>How long Enter and Esc wait when the results open by themselves (a long check ends while the user types elsewhere).</summary>
    private static readonly TimeSpan KeyGuard = TimeSpan.FromMilliseconds(700);
    /// <summary>The notes about the check as a whole show this high at most, and scroll: the files and the buttons stay on screen.</summary>
    private const double NotesHeight = 84;

    /// <param name="jobs">The jobs one Verify started (files beside checksum files, then checksum files), all ended.</param>
    /// <param name="report">What they found, together.</param>
    /// <param name="failing">The files that failed or could not be read and are still there (to be listed in a panel); none offers no button.</param>
    /// <param name="title">The title when it is not the jobs' own (a whole folder's check names the folder).</param>
    /// <param name="notes">What the results say besides what the jobs found (checksum files not checked: the check was canceled).</param>
    /// <param name="guardKeys">The results open by themselves after a longer check: keys pressed at once (for something else) do not close them.</param>
    public static Task<object?> ShowAsync(MainViewModel vm, IReadOnlyList<Job> jobs, VerificationReport report, IReadOnlyList<VerifiedFile> failing,
        string? title = null, IReadOnlyList<string>? notes = null, bool guardKeys = false)
    {
        var files = report.Files;
        var icons = vm.Services.Icons;
        notes ??= [];
        title = Formatters.SafeName(title ?? Title(jobs));
        // As wide as the window leaves the card room for (its margins and padding aside), at most 700.
        var room = vm.View.TopLevel?.ClientSize ?? new Size(1000, 800);
        var body = new StackPanel { Spacing = 6, Width = Math.Clamp(room.Width - 140, 300, 700) };

        // The verdict first, marked as the panels mark it, then what it counts and how the files were read.
        var (headline, mark) = Verdict(jobs, report);
        string counts = report.Total > 1 ? Counts(report) : "";
        string summary = headline + (counts.Length > 0 ? ". " + counts : "");
        var verdict = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        if (mark is { } state && icons.VerificationMark(state) is { } markImage)
            verdict.Children.Add(new Image { Source = markImage, Width = 22, Height = 22, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        var headlineText = new TextBlock { Text = headline, FontWeight = FontWeight.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(headlineText, 1);
        verdict.Children.Add(headlineText);
        // It can hold the keyboard (read out as the verdict) while keys pressed for something else are held off.
        var verdictBox = new Border { Child = verdict, Focusable = true, Background = Brushes.Transparent };
        AutomationProperties.SetName(verdictBox, summary);
        body.Children.Add(verdictBox);
        // One file's verdict says it all; more are counted.
        if (counts.Length > 0) body.Children.Add(Muted(counts));
        body.Children.Add(Muted(HowRead(jobs)));
        var general = GeneralNotes(jobs, report, notes).ToList();
        if (general.Count > 0)
        {
            var noteList = new StackPanel { Spacing = 2 };
            foreach (string note in general) noteList.Children.Add(Muted(Formatters.SafeName(note)));
            body.Children.Add(new ScrollViewer { Content = noteList, MaxHeight = NotesHeight, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        }
        // The list and the details share the height the rest leaves (a small window still shows the buttons).
        double share = Math.Clamp((room.Height - 330 - (general.Count > 0 ? NotesHeight : 0)) / 2, 70, 200);

        ListBox? list = null;
        if (files.Count > 0)
        {
            list = new ListBox
            {
                ItemsSource = files,
                Classes = { "choices" },
                MaxHeight = share,
                ItemTemplate = new FuncDataTemplate<VerifiedFile>((f, _) => f is null ? new TextBlock() : Row(f, icons)),
            };
            AutomationProperties.SetName(list, "Files checked");
            // Whoever reaches the list by the keyboard hears the verdict too.
            AutomationProperties.SetHelpText(list, summary);
            // On the selected row, what was found is drawn in the selection's text color: the state colors lack contrast there
            // (the mark still says it).
            list.Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected").Descendant().OfType<TextBlock>().Class("status"))
            {
                Setters = { new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("FcFocusText")) },
            });
            // Each row is announced as what it says.
            list.ContainerPrepared += (_, e) =>
            {
                if (e.Index >= 0 && e.Index < files.Count) AutomationProperties.SetName(e.Container, Spoken(files[e.Index]));
            };
            var details = new StackPanel { Spacing = 2 };
            // A fixed height: moving through the list must not resize the dialog (it is centered, so it would jump).
            var detailsView = new ScrollViewer { Content = details, Height = share - 10, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = true };
            AutomationProperties.SetName(detailsView, "Details of the selected file");
            var detailsBox = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8),
                // Opaque, as tooltips are: a see-through theme would show the panels behind the values.
                [!Border.BackgroundProperty] = new DynamicResourceExtension("FcTipBackground"),
                [!Border.BorderBrushProperty] = new DynamicResourceExtension("FcBorder"),
                Child = detailsView,
            };
            var shown = list;
            list.SelectionChanged += (_, _) => ShowDetails(details, shown.SelectedItem as VerifiedFile);
            list.SelectedIndex = 0;
            body.Children.Add(list);
            body.Children.Add(detailsBox);
        }

        // Copied without closing: the failing files may still be wanted in a panel.
        var copy = new Button { Content = "Copy report", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        ToolTip.SetTip(copy, "Every file listed here with its checksums (expected and computed), signatures, and notes, as text");
        copy.Click += (_, _) =>
        {
            vm.CopyTextToClipboard(ReportText(jobs, report, title, notes));
            copy.Content = "Report copied";
            AutomationProperties.SetName(copy, "Report copied");
        };
        body.Children.Add(copy);

        var buttons = new List<DialogButton>();
        if (failing.Count > 0)
        {
            bool allFailed = failing.All(f => f.Outcome == VerifiedOutcome.Failed);
            buttons.Add(new DialogButton(
                $"Show {Formatters.Plural(failing.Count, allFailed ? "failed file" : "file with problems", allFailed ? "failed files" : "files with problems")} in a panel", ShowFailed));
        }
        buttons.Add(new DialogButton("Close", "close", IsDefault: true, IsCancel: true));

        // The list has the keyboard: arrows go through the files at once, Enter and Esc close (with no list, Close has it).
        // Results that open by themselves first hold the keyboard on the verdict for a moment, where Enter and Esc do nothing.
        Control? initial = list;
        if (guardKeys)
        {
            initial = verdictBox;
            bool guarding = true;
            body.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (guarding && e.Key is Key.Enter or Key.Escape) e.Handled = true;
            }, RoutingStrategies.Tunnel);
            DispatcherTimer.RunOnce(() =>
            {
                guarding = false;
                if (!verdictBox.IsFocused) return;
                if (list is not null) Controls.ListKeys.Focus(list);
                else DefaultButton(body)?.Focus();
            }, KeyGuard);
        }
        return vm.Dialogs.ShowCustomAsync(title, body, buttons, initialFocus: initial);
    }

    /// <summary>The job's own title ("Verify "x.iso""), or the command's for several.</summary>
    private static string Title(IReadOnlyList<Job> jobs) => jobs is [var job] ? job.Title : "Verify checksums and signatures";

    /// <summary>The dialog's default button (Close), in the card that holds <paramref name="body"/>.</summary>
    private static Button? DefaultButton(Control body) =>
        body.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("card"))?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsDefault);

    /// <summary>One file in the list: its mark, its name, what was found, and the claim it was checked against.</summary>
    private static Control Row(VerifiedFile f, IconProvider icons)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*,Auto,Auto"), Margin = new Thickness(0, 1) };
        // A file nothing was compared for has no mark, as in the panel.
        if (MarkOf(f) is { } state && icons.VerificationMark(state) is { } image)
            row.Children.Add(new Image { Source = image, Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
        // A long name (a path from a list of found files) keeps its end: the file's own name.
        var name = new TextBlock { Text = Formatters.SafeName(f.Name), TextTrimming = TextTrimming.PrefixCharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        var found = new TextBlock { Text = Word(f), Classes = { "status" }, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        found.Classes.Add(f.Outcome switch
        {
            VerifiedOutcome.Good => "success",
            VerifiedOutcome.Failed => "error",
            VerifiedOutcome.Missing or VerifiedOutcome.Unreadable or VerifiedOutcome.Unsure => "warning",
            _ => "muted",
        });
        Grid.SetColumn(found, 2);
        row.Children.Add(found);
        var against = new TextBlock
        {
            Text = Formatters.SafeName(Against(f)),
            Classes = { "muted", "small", "status" },
            Margin = new Thickness(12, 0, 0, 0),
            MaxWidth = 260,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(against, 3);
        row.Children.Add(against);
        return row;
    }

    /// <summary>A row as a screen reader says it: the name, what was found, and against what, in words.</summary>
    private static string Spoken(VerifiedFile f)
    {
        string spoken = $"{Formatters.SafeName(f.Name)}: {Word(f)}";
        if (f.Checksums.Count > 0)
        {
            var c = f.Checksums.FirstOrDefault(c => !c.Matches) ?? f.Checksums[0];
            spoken += $", {Checksums.Name(c.Claim.Kind)} from {Formatters.SafeName(Path.GetFileName(c.Claim.Source))}, line {c.Claim.Line}"
                      + (f.Checksums.Count > 1 ? $", and {f.Checksums.Count - 1} more" : "");
        }
        else if (f.Signatures.Count > 0) spoken += $", signature {Formatters.SafeName(Path.GetFileName(f.Signatures[0].Claim.Signature))}";
        return spoken;
    }

    /// <summary>The selected file in full: what was found, each checksum with both values, each signature, and the notes.</summary>
    private static void ShowDetails(StackPanel pane, VerifiedFile? f)
    {
        pane.Children.Clear();
        if (f is null) return;
        pane.Children.Add(new TextBlock { Text = Describe(f), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (f.Path is { } path && !string.Equals(path, f.Name, StringComparison.Ordinal))
            pane.Children.Add(new SelectableTextBlock
            {
                Text = Formatters.SafeName(path),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                IsTabStop = false,
                [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("FcTextMuted"),
            });
        foreach (var c in f.Checksums)
        {
            pane.Children.Add(new TextBlock
            {
                Text = $"{Checksums.Name(c.Claim.Kind)} · {Formatters.SafeName(Path.GetFileName(c.Claim.Source))}, line {c.Claim.Line}: {(c.Matches ? "matches" : "differs")}",
                Classes = { c.Matches ? "success" : "error" },
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });
            Value(pane, "expected", c.Claim.Expected);
            Value(pane, "computed", c.Actual);
        }
        foreach (var s in f.Signatures)
        {
            pane.Children.Add(new TextBlock
            {
                Text = Formatters.SafeName(SignatureLine(s)),
                Classes = { s.Result.State switch { VerificationState.SignatureGood => "success", VerificationState.SignatureBad => "error", _ => "warning" } },
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }
        if (f.Notes.Count > 0) pane.Children.Add(new Border { Height = 4 });
        foreach (string note in f.Notes) pane.Children.Add(Muted(Formatters.SafeName(note)));
    }

    /// <summary>A checksum value under its label, in a fixed-width font and selectable (a SHA-512 value wraps); Tab passes it by.</summary>
    private static void Value(StackPanel pane, string label, string value)
    {
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("72,*") };
        line.Children.Add(new TextBlock { Text = label, Classes = { "muted" }, FontFamily = Mono, FontSize = 12 });
        var text = new SelectableTextBlock { Text = value, FontFamily = Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTabStop = false };
        AutomationProperties.SetName(text, $"{label}: {value}");
        Grid.SetColumn(text, 1);
        line.Children.Add(text);
        pane.Children.Add(line);
    }

    /// <summary>
    /// The sentence on top, and the mark beside it (none when nothing was compared). "Failed" means a checksum differs or a
    /// signature is bad; what could not be read, is missing, or is unsure is said as such.
    /// </summary>
    private static (string Text, VerificationState? Mark) Verdict(IReadOnlyList<Job> jobs, VerificationReport report)
    {
        int failed = report.Count(VerifiedOutcome.Failed), unreadable = report.Count(VerifiedOutcome.Unreadable), missing = report.Count(VerifiedOutcome.Missing);
        int unsure = report.Count(VerifiedOutcome.Unsure), good = report.Count(VerifiedOutcome.Good), lines = report.Lines;
        int files = report.Total - lines, notChecked = report.Count(VerifiedOutcome.NotChecked) - lines;
        VerificationState? worst = failed > 0 ? VerificationState.Differs : unreadable + missing > 0 ? VerificationState.Unreadable : null;
        // What else was found, after what the sentence leads with.
        string Also(bool withFailed)
        {
            var parts = new List<string>();
            if (withFailed && failed > 0) parts.Add($"{failed:N0} failed");
            if (unreadable > 0) parts.Add($"{unreadable:N0} could not be read");
            if (missing > 0) parts.Add($"{missing:N0} missing");
            return parts.Count > 0 ? "; " + string.Join("; ", parts) : "";
        }
        // A job that broke off (it could not start, or stopped on an error of its own) is never hidden behind a good verdict.
        if (jobs.Any(j => j.State == JobState.Failed && j.VerificationReport is not { IsComplete: true }))
            return ("The check stopped on an error before it reached every file" + Also(withFailed: true), worst ?? VerificationState.Unreadable);
        if (jobs.Any(j => j.State == JobState.Canceled))
        {
            long planned = jobs.Sum(j => j.ItemsTotal) - lines;
            bool known = jobs.All(j => j.ItemsTotal > 0 || j.State != JobState.Canceled);
            string how = files == 0 ? "Canceled before any file was read"
                : known && planned > files ? $"Canceled after {files:N0} of {Formatters.Plural((int)Math.Min(int.MaxValue, planned), "file", "files")}"
                : $"Canceled after {Formatters.Plural(files, "file", "files")}";
            return (how + Also(withFailed: true), worst);
        }
        if (report.Total == 0) return ("Nothing was verified", null);
        if (files == 0) return ($"Nothing was checked: {(lines == 1 ? "the line read names" : $"none of the {lines:N0} lines read names")} a file to check", null);
        // One file: said of it by name.
        if (files == 1 && report.Files.Where(f => !f.IsLine).ToList() is [var only]) return (Describe(only), MarkOf(only));
        string all = Formatters.Plural(files, "file", "files");
        if (failed > 0) return ($"{failed:N0} of {all} failed" + Also(withFailed: false), worst);
        if (unreadable > 0) return ($"{unreadable:N0} of {all} could not be read" + (missing > 0 ? $"; {missing:N0} missing" : ""), worst);
        if (missing > 0) return ($"No file failed, but {missing:N0} of {all} {(missing == 1 ? "is" : "are")} missing", worst);
        if (unsure > 0) return ($"No file failed, but no signature could vouch for {unsure:N0} of {all}", VerificationState.SignatureUnknownKey);
        if (good == 0) return ("Nothing to check these files against", null);
        string verb = report.Authentic == good ? "authentic" : "intact";
        if (notChecked > 0) return ($"{good:N0} of {all} {verb}; {notChecked:N0} with nothing to check against", MarkOfGood(report.Authentic == good));
        return report.Authentic == good
            ? ($"All {Formatters.Plural(good, "file", "files")} are authentic: a key you trust vouches for each", VerificationState.SignatureGood)
            : ($"All {Formatters.Plural(good, "file", "files")} are intact", VerificationState.Matches);
    }

    private static VerificationState MarkOfGood(bool authentic) => authentic ? VerificationState.SignatureGood : VerificationState.Matches;

    /// <summary>One file's result in a sentence (the verdict for one file, and the details' first line).</summary>
    private static string Describe(VerifiedFile f)
    {
        string name = Formatters.SafeName(f.Name);
        string sums = f.Checksums.Count > 1 ? "its checksums" : "its checksum";
        int differ = f.Checksums.Count(c => !c.Matches);
        bool overFile(SignatureCheck s) => s.Manifest is null;
        return f.Outcome switch
        {
            // A good signature over the checksum file that names it vouches for it as much as one over the file.
            VerifiedOutcome.Good when f.Authentic => f.Signatures.Any(s => overFile(s) && s.Result.State == VerificationState.SignatureGood)
                ? $"{name} is authentic: a key you trust signed it"
                : $"{name} is authentic: a key you trust signed its checksum file",
            VerifiedOutcome.Good => f.Checksums.Count > 0 ? $"{name} is intact: it matches {sums}" : $"{name} is intact",
            VerifiedOutcome.Failed when differ > 0 => f.Checksums.Count == 1 ? $"{name} does not match its checksum"
                : differ == f.Checksums.Count ? $"{name} matches none of its checksums"
                : $"{name} does not match {differ:N0} of its {f.Checksums.Count:N0} checksums",
            VerifiedOutcome.Failed when f.Signatures.Any(s => overFile(s) && s.Result.State == VerificationState.SignatureBad) => $"{name} has a bad signature",
            VerifiedOutcome.Failed => $"{name} matches {sums}, but the signature over its checksum file is bad",
            VerifiedOutcome.Missing => f.Text switch
            {
                "a folder, not a file" => $"{name} is a folder, not a file",
                "not there any more" => $"{name} is not there any more",
                _ => $"{name} is missing",
            },
            VerifiedOutcome.Unreadable => $"{name} could not be read",
            VerifiedOutcome.Unsure => f.Checksums.Count > 0
                ? $"{name} matches {sums}, but no signature could vouch for it: {Reason(f)}"
                : $"{name} was not verified: {Reason(f)}",
            _ => $"{name}: {Formatters.SafeName(f.Text)}",
        };
    }

    /// <summary>Why no signature vouched for an unsure file, in a few words.</summary>
    private static string Reason(VerifiedFile f) =>
        f.Signatures.FirstOrDefault(s => s.Result.State != VerificationState.SignatureGood) is { } s
            ? Formatters.SafeName(Short(s.Result).TrimStart('?', ' '))
            : "whether a signature vouches for its checksum file is not known";

    /// <summary>A signature's result in a few words, as its row says it.</summary>
    private static string Short(SignatureResult r) => r.Short ?? r.State switch
    {
        VerificationState.SignatureBad => "bad signature",
        VerificationState.SignatureUnknownKey => "signed by an unknown key",
        _ => "signature not checked",
    };

    /// <summary>Every outcome counted ("2 intact · 1 failed · 1 missing"), and how many are not listed.</summary>
    private static string Counts(VerificationReport report)
    {
        var parts = new List<string>();
        int authentic = report.Authentic, intact = report.Count(VerifiedOutcome.Good) - authentic, lines = report.Lines;
        if (authentic > 0) parts.Add($"{authentic:N0} authentic");
        if (intact > 0) parts.Add($"{intact:N0} intact");
        if (report.Count(VerifiedOutcome.Failed) is var failed and > 0) parts.Add($"{failed:N0} failed");
        if (report.Count(VerifiedOutcome.Unreadable) is var unreadable and > 0) parts.Add($"{unreadable:N0} could not be read");
        if (report.Count(VerifiedOutcome.Unsure) is var unsure and > 0) parts.Add($"{unsure:N0} unsure");
        if (report.Count(VerifiedOutcome.Missing) is var missing and > 0) parts.Add($"{missing:N0} missing");
        if (report.Count(VerifiedOutcome.NotChecked) - lines is var notChecked and > 0) parts.Add($"{notChecked:N0} not checked");
        if (lines > 0) parts.Add(Formatters.Plural(lines, "line", "lines") + " not checked");
        if (report.Unlisted > 0) parts.Add($"{report.Unlisted:N0} more not listed (problems are listed first)");
        return string.Join(" · ", parts);
    }

    /// <summary>The files were read now, whatever earlier checks found: how much, how long it took, and when it ended.</summary>
    private static string HowRead(IReadOnlyList<Job> jobs)
    {
        var started = jobs.Select(j => j.StartedUtc).OfType<DateTime>().ToList();
        var ended = jobs.Select(j => j.FinishedUtc).OfType<DateTime>().ToList();
        string when = ended.Count > 0 ? ", finished " + Formatters.Date(ended.Max().ToLocalTime()) : "";
        long read = jobs.Sum(j => j.DataBytesProcessed);
        if (read == 0) return $"Nothing had to be read{when}.";
        string took = started.Count > 0 && ended.Count > 0 ? " in " + Took(ended.Max() - started.Min()) : "";
        return $"Read {Formatters.SizeWithUnit(read)} from disk{took}{when}. Results kept from earlier checks were not used.";
    }

    private static string Took(TimeSpan t) => t.TotalSeconds < 1 ? "under a second"
        : t.TotalMinutes < 1 ? $"{t.TotalSeconds.ToString("0.#", CultureInfo.CurrentCulture)} s"
        : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s"
        : $"{(int)t.TotalHours} h {t.Minutes} min";

    /// <summary>
    /// The check's own notes (a checksum file's signature, one that could not be read), what the results say besides, and what
    /// stopped a job that broke off (its errors are about no listed file).
    /// </summary>
    private static IEnumerable<string> GeneralNotes(IReadOnlyList<Job> jobs, VerificationReport report, IReadOnlyList<string> notes)
    {
        foreach (string note in report.Notes) yield return note;
        foreach (string note in notes) yield return note;
        foreach (var job in jobs.Where(j => j.State == JobState.Failed && j.VerificationReport is not { IsComplete: true }))
            foreach (var issue in job.Issues.Where(i => i.Severity >= IssueSeverity.Warning && i.Path.Length == 0).Take(8))
                yield return "It stopped on: " + issue.Message;
    }

    /// <summary>The mark of a file's outcome, as its icon in the panel shows it; none when nothing was compared.</summary>
    private static VerificationState? MarkOf(VerifiedFile f) => f.Outcome switch
    {
        VerifiedOutcome.Good => MarkOfGood(f.Authentic),
        VerifiedOutcome.Failed => VerificationState.Differs,
        VerifiedOutcome.Missing or VerifiedOutcome.Unreadable => VerificationState.Unreadable,
        VerifiedOutcome.Unsure => VerificationState.SignatureUnknownKey,
        _ => null,
    };

    /// <summary>What was found, in a word or two.</summary>
    private static string Word(VerifiedFile f) => f.Outcome switch
    {
        VerifiedOutcome.Good => f.Authentic ? "authentic" : "intact",
        VerifiedOutcome.Failed => f.Checksums.Any(c => !c.Matches) ? "differs" : "bad signature",
        VerifiedOutcome.Missing => f.Text == "a folder, not a file" ? f.Text : "missing",
        VerifiedOutcome.Unreadable => "could not be read",
        VerifiedOutcome.Unsure => "unsure",
        _ => "not checked",
    };

    /// <summary>What it was checked against: the first claim ("SHA-256 · SHA256SUMS:3"), or its signature.</summary>
    private static string Against(VerifiedFile f)
    {
        if (f.Checksums.Count > 0)
        {
            var c = f.Checksums.FirstOrDefault(c => !c.Matches) ?? f.Checksums[0];
            return $"{Checksums.Name(c.Claim.Kind)} · {Path.GetFileName(c.Claim.Source)}:{c.Claim.Line}" + (f.Checksums.Count > 1 ? $" +{f.Checksums.Count - 1}" : "");
        }
        if (f.Signatures.Count > 0) return Path.GetFileName(f.Signatures[0].Claim.Signature) + (f.Signatures.Count > 1 ? $" +{f.Signatures.Count - 1}" : "");
        return "";
    }

    private static string SignatureLine(SignatureCheck s) =>
        Path.GetFileName(s.Claim.Signature) + (s.Manifest is { } manifest ? $" (over {manifest})" : "") + ": " + s.Result.Text;

    /// <summary>The results as text for the clipboard: every file listed, each claim with both values in full.</summary>
    private static string ReportText(IReadOnlyList<Job> jobs, VerificationReport report, string title, IReadOnlyList<string> notes)
    {
        var text = new StringBuilder();
        text.AppendLine($"{Formatters.SafeName(title)}: {Verdict(jobs, report).Text}");
        if (Counts(report) is { Length: > 0 } counts) text.AppendLine(counts);
        text.AppendLine(HowRead(jobs));
        foreach (string note in GeneralNotes(jobs, report, notes)) text.AppendLine(Formatters.SafeName(note));
        foreach (var f in report.Files)
        {
            text.AppendLine();
            // What its row says, unless that is the word again ("missing (missing)").
            string word = Word(f), more = f.Text.Length > 0 && !string.Equals(f.Text, word, StringComparison.OrdinalIgnoreCase) ? $" ({Formatters.SafeName(f.Text)})" : "";
            text.AppendLine($"{Formatters.SafeName(f.Name)}: {word}{more}");
            if (f.Path is { } path && !string.Equals(path, f.Name, StringComparison.Ordinal)) text.AppendLine("  " + Formatters.SafeName(path));
            foreach (var c in f.Checksums)
            {
                text.AppendLine($"  {Checksums.Name(c.Claim.Kind)}, {Formatters.SafeName(Path.GetFileName(c.Claim.Source))} line {c.Claim.Line}: {(c.Matches ? "matches" : "differs")}");
                text.AppendLine($"    expected {c.Claim.Expected}");
                text.AppendLine($"    computed {c.Actual}");
            }
            foreach (var s in f.Signatures) text.AppendLine("  " + Formatters.SafeName(SignatureLine(s)));
            foreach (string note in f.Notes) text.AppendLine("  " + Formatters.SafeName(note));
        }
        return text.ToString();
    }

    private static TextBlock Muted(string text) => new() { Text = text, Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap };
}
