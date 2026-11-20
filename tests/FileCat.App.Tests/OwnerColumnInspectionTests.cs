using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Metadata;

namespace FileCat.App.Tests;

public sealed partial class OwnerColumnTests
{
    [AvaloniaFact]
    public async Task Plain_resolved_name_has_no_tip_but_inspect_contains_full_identity_without_provider_work()
    {
        using var f = new OwnerFixture(new OwnerSource { NameAnswer = new(NameStatus.Resolved, "fixture-user", "User") });
        await f.Load();
        await f.ShowOwner();
        int references = f.Provider.References, reads = f.Source.ReadPaths.Count, names = f.Source.NameCalls;
        HoverOwner(f);
        Assert.Null(ToolTip.GetTip(f.List));
        Assert.Contains("User · UID 42", f.List.FocusedInspectionText());
        Assert.Equal(references, f.Provider.References);
        Assert.Equal(reads, f.Source.ReadPaths.Count);
        Assert.Equal(names, f.Source.NameCalls);
    }

    [AvaloniaTheory]
    [InlineData(OwnerReadStatus.Denied, "no access", "Access refused.\\u202E")]
    [InlineData(OwnerReadStatus.Unsupported, "—", "Not supported here.")]
    [InlineData(OwnerReadStatus.Failed, "error", "Temporarily busy.")]
    [InlineData(OwnerReadStatus.Gone, "(none)", "No longer there.")]
    public async Task Unavailable_and_gone_inspection_reasons_are_cached_and_sanitized(OwnerReadStatus outcome, string cell, string expectedReason)
    {
        string reason = outcome switch
        {
            OwnerReadStatus.Denied => "Access refused.\u202E",
            OwnerReadStatus.Unsupported => "Not supported here.",
            OwnerReadStatus.Gone => "No longer there.",
            _ => "Temporarily busy.",
        };
        using var f = new OwnerFixture(new OwnerSource { ReadOverride = _ => new(outcome, Reason: reason) });
        await f.Load();
        await f.ShowOwner();
        int refs = f.Provider.References, reads = f.Source.ReadPaths.Count;
        string report = f.List.FocusedInspectionText()!;
        Assert.Contains("Owner: " + cell, report);
        Assert.Contains(expectedReason, report);
        Assert.DoesNotContain('\u202E', report);
        HoverOwner(f);
        if (outcome == OwnerReadStatus.Gone) Assert.Null(ToolTip.GetTip(f.List));
        else Assert.Contains(expectedReason, Assert.IsType<string>(ToolTip.GetTip(f.List)));
        Assert.Equal(refs, f.Provider.References);
        Assert.Equal(reads, f.Source.ReadPaths.Count);
        Assert.Equal(0, f.Source.NameCalls);
    }

    [AvaloniaFact]
    public async Task Unnamed_identity_and_last_known_value_explain_their_meaning_even_when_unclipped()
    {
        using var f = new OwnerFixture(new OwnerSource { NameAnswer = new(NameStatus.NotFound) });
        await f.Load();
        await f.ShowOwner();
        HoverOwner(f);
        Assert.Contains("This system has no user name for UID 42", Assert.IsType<string>(ToolTip.GetTip(f.List)));
        f.Source.ReadOverride = _ => new(OwnerReadStatus.Failed, Reason: "Temporarily busy.");
        f.Services.Metadata.ExpireRefreshable();
        f.Frame();
        await FileListFixture.Wait(() => f.FocusedValue.Observation == MetadataObservation.LastKnown);
        f.Frame();
        int refs = f.Provider.References, reads = f.Source.ReadPaths.Count;
        HoverOwner(f);
        Assert.Contains("(last known)", Assert.IsType<string>(ToolTip.GetTip(f.List)));
        Assert.Contains("Showing the last value read", Assert.IsType<string>(ToolTip.GetTip(f.List)));
        string report = f.List.FocusedInspectionText()!;
        Assert.Contains("42 (last known)", report);
        Assert.Contains("Temporarily busy.", report);
        Assert.Equal(refs, f.Provider.References);
        Assert.Equal(reads, f.Source.ReadPaths.Count);
    }

    [AvaloniaFact]
    public async Task Vertically_visible_focus_requests_hidden_owner_and_offscreen_focus_uses_retained_context()
    {
        using var f = new OwnerFixture();
        await f.Load();
        HiddenOwner(f);
        f.Frame();
        await FileListFixture.Wait(() => f.FocusedValue.State == MetadataState.Available);
        await f.Names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Single(f.Source.ReadPaths);
        Assert.Contains("UID 42", f.List.FocusedInspectionText());
        var scroll = f.List.GetVisualDescendants().OfType<ScrollBar>().Single(s => s.Orientation == Avalonia.Layout.Orientation.Vertical);
        scroll.Value = 70; // retains focus on the now offscreen first row
        f.Frame();
        Assert.True(f.List.TopRow > 0);
        int references = f.Provider.References;
        Assert.Contains("UID 42", f.List.FocusedInspectionText());
        Assert.Single(f.Source.ReadPaths);
        Assert.Equal(references, f.Provider.References);
    }

    [AvaloniaFact]
    public async Task Never_painted_offscreen_focus_is_actionable_without_demand()
    {
        using var f = new OwnerFixture();
        await f.Load();
        HiddenOwner(f);
        f.Tab.Listing.SetFocus(90);
        var scroll = f.List.GetVisualDescendants().OfType<ScrollBar>().Single(s => s.Orientation == Avalonia.Layout.Orientation.Vertical);
        scroll.Value = 0;
        f.Frame();
        Assert.Equal(0, f.List.TopRow);
        int references = f.Provider.References;
        Assert.Contains(MetadataCellPresentation.NotRead, f.List.FocusedInspectionText());
        Assert.Empty(f.Source.ReadPaths);
        Assert.Equal(references, f.Provider.References);
    }

    [AvaloniaFact]
    public async Task Visible_focus_horizontal_exception_also_uses_normal_expensive_field_admission()
    {
        using var f = new OwnerFixture();
        await f.Load();
        int produced = 0;
        f.Services.Metadata.Register(new MetadataField("fixture-expensive", "Fixture", MetadataCost.Expensive, _ => true,
            (_, _) => Interlocked.Increment(ref produced), value => value?.ToString() ?? ""));
        HiddenOwner(f, "fixture-expensive");
        f.Frame();
        await FileListFixture.Wait(() => Volatile.Read(ref produced) > 0);
        Assert.Equal(1, produced);
        Assert.Empty(f.Source.ReadPaths);
    }

    [AvaloniaFact]
    public async Task Ownership_and_name_completions_do_not_change_the_focused_spoken_value()
    {
        using var f = new OwnerFixture(new OwnerSource { NameAnswer = new(NameStatus.Resolved, "first account", "User") });
        await f.Load();
        await f.ShowOwner();
        f.List.Focus();
        var peer = Assert.IsType<FileListAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(f.List));
        Dispatcher.UIThread.RunJobs();
        string before = peer.Value;
        var events = new List<AutomationPropertyChangedEventArgs>();
        peer.PropertyChanged += (_, e) => events.Add(e);
        if (peer.FocusedDescendant is { } row) row.PropertyChanged += (_, e) => events.Add(e);
        var repainted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Services.Metadata.ValuesChanged += () => repainted.TrySetResult();
        f.Source.NameAnswer = new(NameStatus.Resolved, "second account", "User");
        f.Names.Invalidate();
        f.Services.Metadata.ExpireRefreshable();
        f.Frame();
        await repainted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await f.Names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, peer.Value);
        Assert.DoesNotContain(events, e => e.Property == ValuePatternIdentifiers.ValueProperty || e.Property == AutomationElementIdentifiers.NameProperty);
        Assert.DoesNotContain("account", peer.Value);
    }

    [AvaloniaFact]
    public async Task Inapplicable_fields_are_omitted_and_providerless_fields_are_explained_without_new_references()
    {
        using var f = new FileListFixture("file.txt");
        await f.Load();
        f.Services.Columns.Replace(new[] { ("Inspect", new[]
        {
            new ColumnSpec(ColumnField.Name, "Name", 200, Star: true),
            new ColumnSpec(ColumnField.Metadata, "Dimensions", 110) { MetadataId = "dimensions" },
            new ColumnSpec(ColumnField.Metadata, "Link target", 110) { MetadataId = "linkTarget" },
        }) });
        f.Tab.ColumnProfile = 0;
        f.List.InvalidateVisual();
        f.Window.CaptureRenderedFrame()?.Dispose();
        int references = f.Provider.ItemReferencesRequested;
        string report = f.List.FocusedInspectionText()!;
        Assert.DoesNotContain("Dimensions:", report);
        Assert.Contains("Link target: (not available for items in this location)", report);
        Assert.Equal(references, f.Provider.ItemReferencesRequested);
    }

    [AvaloniaTheory]
    [InlineData(10_000, false)]
    [InlineData(1_100_000, true)]
    public async Task Existing_tooltip_and_inspection_limits_include_the_new_detail_text(int length, bool inspectionLimited)
    {
        string reason = new('r', length);
        using var f = new OwnerFixture(new OwnerSource { ReadOverride = _ => new(OwnerReadStatus.Failed, Reason: reason) });
        await f.Load();
        await f.ShowOwner();
        // Metadata completion retires a tip through a queued UI callback. Wait for the
        // observable hover result before measuring inspection: the UI wait can repaint
        // other visible rows. Unavailable-value tests cover hover's memory-only path.
        string? shownTip = null;
        await FileListFixture.Wait(() =>
        {
            shownTip = ToolTip.GetTip(f.List) as string;
            if (shownTip is not null) return true;
            HoverOwner(f);
            return false;
        });
        var tip = Assert.IsType<string>(shownTip);
        Assert.Contains("Ctrl+Shift+I", tip);
        Assert.InRange(tip.Length, 8192, 8300);
        int refs = f.Provider.References, reads = f.Source.ReadPaths.Count;
        string report = f.List.FocusedInspectionText()!;
        Assert.Equal(inspectionLimited, report.Contains("inspection display is limited to 1 MiB", StringComparison.Ordinal));
        Assert.True(report.Length <= 1_048_576 + 128);
        Assert.Equal(refs, f.Provider.References);
        Assert.Equal(reads, f.Source.ReadPaths.Count);
    }

    [AvaloniaFact]
    public async Task Clipped_untrusted_account_text_is_complete_and_safe_in_tip_and_inspect()
    {
        string name = new string('a', 200) + "\u202E";
        using var f = new OwnerFixture(new OwnerSource { NameAnswer = new(NameStatus.Resolved, name, "User\u200B") });
        await f.Load();
        await f.ShowOwner();
        HoverOwner(f);
        string tip = Assert.IsType<string>(ToolTip.GetTip(f.List));
        Assert.Contains(new string('a', 200) + "\\u202E", tip);
        Assert.Contains("User\\u200B · UID 42", tip);
        Assert.DoesNotContain('\u202E', tip);
        Assert.DoesNotContain('\u200B', tip);
        // MouseMove can dispatch a queued metadata-completion repaint of all visible rows.
        // Measure the synchronous inspection after that paint, as the tooltip-limit test above
        // does. Otherwise ordinary paint-time references race this cached-inspection assertion.
        int references = f.Provider.References, reads = f.Source.ReadPaths.Count, names = f.Source.NameCalls;
        Assert.Contains(new string('a', 200) + "\\u202E", f.List.FocusedInspectionText());
        Assert.Equal(references, f.Provider.References);
        Assert.Equal(reads, f.Source.ReadPaths.Count); Assert.Equal(names, f.Source.NameCalls);
    }

    [AvaloniaFact]
    public async Task Plain_permissions_keep_hover_quiet_and_expose_octal_detail_in_inspect()
    {
        using var f = new OwnerFixture();
        await f.Load();
        f.Services.Metadata.Register(BuiltInFields.Permissions with { Produce = (_, _) => (UnixFileMode)0x1ED });
        f.Services.Columns.Replace(new[] { ("Mode", new[]
        {
            new ColumnSpec(ColumnField.Name, "Name", 200, Star: true),
            new ColumnSpec(ColumnField.Metadata, "Permissions", 170) { MetadataId = "permissions" },
        }) });
        f.Tab.ColumnProfile = 0;
        f.Frame();
        await FileListFixture.Wait(() => f.Services.Metadata.Peek("permissions", Path.Combine(f.Root, "synthetic", f.FocusedEntry.Name), f.FocusedEntry, false).State == MetadataState.Available);
        f.Frame();
        HoverOwner(f, "permissions");
        Assert.Null(ToolTip.GetTip(f.List));
        Assert.Contains("Octal 755 · mode bits only", f.List.FocusedInspectionText());
    }

    private static void HiddenOwner(OwnerFixture f, string id = "owner")
    {
        f.Services.Columns.Replace(new[] { ("Hidden", new[]
        {
            new ColumnSpec(ColumnField.Name, "Name", 1000),
            new ColumnSpec(ColumnField.Metadata, "Owner", 170) { MetadataId = id },
        }) });
        f.Tab.ColumnProfile = 0;
        f.Window.Width = 400;
    }

    private static void HoverOwner(OwnerFixture f, string id = "owner")
    {
        int column = Array.FindIndex(f.Tab.Columns, c => c.MetadataId == id);
        var point = f.List.TranslatePoint(new Point(f.List.ColumnLeftEdge(column) + 12,
            f.List.GetRowBounds(f.Tab.Listing.FocusedIndex).Center.Y), f.Window)!.Value;
        f.Window.MouseMove(new Point(1, 1));
        f.Window.MouseMove(point);
    }
}
