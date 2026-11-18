using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed record DuplicateReadDisplay(AnalysisPrimaryReadItem Item, string Label, string Details)
    {
        public override string ToString() => Label;
    }

    private async Task ShowDuplicateReadConsentAsync(AnalysisDuplicateReadPlan plan, int unknownBytePaths, CancellationToken lifetime)
    {
        var selected = plan.Items.Select(item => item.Target.PathId).ToImmutableHashSet();
        var reviewScope = plan.Select(selected);
        var summary = DuplicateText("");
        AutomationProperties.SetName(summary, "Approved primary-content read scope");
        AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        var list = DuplicateList("Eligible primary-content reads");
        var details = DuplicateDetails("Exact reviewed content path");
        var toggle = new Button { Content = "Exclude selected file", IsEnabled = false };
        var read = new Button { Content = "Read selected contents", IsEnabled = false };
        ToolTip.SetTip(read, new RichTip("Read primary file contents", detail: "Read exactly the listed same-size peers within the displayed byte limit. Close review keeps the scan and selections."));
        var body = new StackPanel { Spacing = 8, Children =
        {
            DuplicateText("FileCat will read the primary contents of the included same-size files. It compares those bytes using SHA-256."),
            DuplicateText($"Captured revision {plan.Identity.Revision:N0} · {unknownBytePaths:N0} paths with unknown size · {plan.WithoutBinding:N0} candidates without a qualified binding."),
            summary, list, details,
            new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { toggle, read } }
        } };
        if (plan.Items.IsEmpty)
            body.Children.Insert(3, DuplicateText("No marked candidates have a qualified local NTFS file binding."));

        ImmutableHashSet<ulong>? consent = null;
        var closer = new DialogCloser();
        list.SelectionChanged += (_, _) => UpdateSelected();
        toggle.Click += (_, _) =>
        {
            if (list.SelectedItem is not DuplicateReadDisplay row) return;
            int index = list.SelectedIndex;
            ulong id = row.Item.Target.PathId;
            selected = selected.Contains(id) ? selected.Remove(id) : selected.Add(id);
            Refresh(index);
        };
        read.Click += (_, _) =>
        {
            if (!reviewScope.CanRead) return;
            consent = selected;
            closer.Close(null);
        };
        Refresh(-1);
        using var close = lifetime.Register(() => Dispatcher.UIThread.Post(() => closer.Close(null)));
        lifetime.ThrowIfCancellationRequested();
        await _dialogs.ShowCustomAsync("Review primary-content reads", body,
            [new DialogButton("Close read review", false, IsDefault: true, IsCancel: true)], closer: closer, resizable: true);
        lifetime.ThrowIfCancellationRequested();
        if (consent is null) return;
        var result = await IndexedComparisonProgressAsync("Reading approved primary contents", token => plan.ReadAsync(consent, token), lifetime,
            "Reading the approved primary contents on their source devices. Cancel requests a stop and waits for the active read to finish.");
        await ShowDuplicateReadResultAsync(result, lifetime);

        void Refresh(int index)
        {
            reviewScope = plan.Select(selected);
            var reads = reviewScope.Items.Select(item => item.Target.PathId).ToHashSet();
            string limit = reviewScope.Bytes > AnalysisDuplicateReadScope.MaximumBytes
                ? " Narrow the selection to at most 8 GiB." : reviewScope.Items.Length < 2
                    ? " Select at least two files of the same size." : "";
            summary.Text = $"Read {reviewScope.Bytes:N0} B from {reviewScope.Items.Length:N0} files. " +
                $"{reviewScope.WithoutSizePeer:N0} included files lack a selected size peer." + limit;
            list.ItemsSource = plan.Items.Select(item =>
            {
                ulong id = item.Target.PathId;
                string status = reads.Contains(id) ? "Include in read" : selected.Contains(id) ? "No selected size peer" : "Excluded";
                return new DuplicateReadDisplay(item, $"{status} · {ShortDuplicatePath(item)} · {item.Target.Bytes:N0} B",
                    ExactDuplicatePath(item));
            }).ToArray();
            list.SelectedIndex = index;
            read.IsEnabled = reviewScope.CanRead;
            UpdateSelected();
        }

        void UpdateSelected()
        {
            var row = list.SelectedItem as DuplicateReadDisplay;
            toggle.IsEnabled = row is not null;
            details.Text = row?.Details ?? "";
            toggle.Content = row is not null && !selected.Contains(row.Item.Target.PathId)
                ? "Include selected file" : "Exclude selected file";
        }
    }

    private async Task ShowDuplicateReadResultAsync(AnalysisDuplicateReadResult result, CancellationToken lifetime)
    {
        var groups = result.Groups;
        var seen = new HashSet<ulong>();
        var rows = new List<DuplicateReadDisplay>(result.Rows.Length);
        for (int group = 0; group < groups.Length; group++)
        {
            var matches = groups[group];
            for (int member = 0; member < matches.Length; member++)
            {
                var row = matches[member];
                seen.Add(row.Item.Target.PathId);
                string label = $"Primary match {group + 1:N0} · {ShortDuplicatePath(row.Item)}";
                if (member == 0) label += " · Proposed keeper";
                rows.Add(Format(row, label));
            }
        }
        foreach (var row in result.Rows.OrderBy(row => row.Item.Target.PathId))
        {
            if (seen.Contains(row.Item.Target.PathId)) continue;
            string state = row.State switch
            {
                AnalysisDuplicateReadState.Read => "Primary bytes read · no matching digest",
                AnalysisDuplicateReadState.Changed => "Source changed · read refused",
                AnalysisDuplicateReadState.Unreadable => "Primary contents unreadable",
                AnalysisDuplicateReadState.Unavailable => "Bound read unavailable",
                _ => throw new ArgumentOutOfRangeException()
            };
            rows.Add(Format(row, state + " · " + ShortDuplicatePath(row.Item)));
        }

        var list = DuplicateList("Primary-content evidence rows");
        list.ItemsSource = rows;
        var details = DuplicateDetails("Primary-content evidence details");
        list.SelectionChanged += (_, _) => details.Text = (list.SelectedItem as DuplicateReadDisplay)?.Details ?? "";
        int read = result.Rows.Count(row => row.State == AnalysisDuplicateReadState.Read);
        var body = new StackPanel { Spacing = 8, Children =
        {
            DuplicateText($"Captured revision {result.Identity.Revision:N0} · {groups.Length:N0} {(groups.Length == 1 ? "group" : "groups")} with matching primary digests · {read:N0} files read · {result.Rows.Length - read:N0} unavailable."),
            DuplicateText("Evidence covers the primary bytes read at the shown times. Streams, forks, permissions and other metadata require separate review."),
            list, details
        } };
        if (!groups.IsEmpty)
            body.Children.Insert(2, DuplicateText("Each group suggests its first captured entry as a keeper. Your marks and review basket still carry your own selections."));
        var closer = new DialogCloser();
        using var close = lifetime.Register(() => Dispatcher.UIThread.Post(() => closer.Close(null)));
        lifetime.ThrowIfCancellationRequested();
        await _dialogs.ShowCustomAsync("Primary-content results", body,
            [new DialogButton("Close content results", false, IsDefault: true, IsCancel: true)], closer: closer, resizable: true);

        static DuplicateReadDisplay Format(AnalysisDuplicateReadRow row, string label)
        {
            string detail = ExactDuplicatePath(row.Item);
            if (row.Proof is { } proof)
                detail += $"\nStable primary-content read · {proof.BytesRead:N0} B · {proof.ObservedUtc.ToLocalTime():G}\nSHA-256 {proof.Sha256}";
            return new(row.Item, label, detail);
        }
    }

    private static string ShortDuplicatePath(AnalysisPrimaryReadItem item) =>
        AnalysisNames.Display(item.RelativePath, 800, escapeTrailing: true) + " · " + AnalysisNames.Display(item.Root.Label, 120);

    private static string ExactDuplicatePath(AnalysisPrimaryReadItem item) =>
        AnalysisRecipeDialog.LocationLabel(item.Root) + "\n" + AnalysisNames.Display(item.RelativePath, 8192, escapeTrailing: true);

    private static TextBlock DuplicateText(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    private static ListBox DuplicateList(string name)
    {
        var list = new ListBox { MinHeight = 100, MaxHeight = 260,
            ItemTemplate = new FuncDataTemplate<DuplicateReadDisplay>((row, _) => DuplicateText(row?.Label ?? "")) };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(list, name);
        return list;
    }

    private static TextBox DuplicateDetails(string name)
    {
        var detail = new TextBox { IsReadOnly = true, AcceptsReturn = true, MaxHeight = 106, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(detail, name);
        ScrollViewer.SetVerticalScrollBarVisibility(detail, ScrollBarVisibility.Auto);
        return detail;
    }
}
