using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

internal sealed record AnalysisCleanupReviewEvidence(AnalysisObjectMetadata? Object, ImmutableArray<StorageFact> Facts, bool HasMoreFacts)
{
    internal const int MaximumFacts = 64;
}

public sealed partial class DiskSpaceWindow
{
    private static class AnalysisCleanupReviewDialog
    {
        private const int PageSize = 32;

        private sealed record Entry(ulong Id, AnalysisPathMetadata? Path, string Label)
        {
            public override string ToString() => Label;
        }

        private sealed record ActionChoice(AnalysisVerb Verb, string Label, string Confirmation, string SpaceEffect)
        {
            public override string ToString() => Label;
        }

        internal static async Task<bool> ShowAsync(IDialogService dialogs, ImmutableHashSet<ulong> paths, string selection, AnalysisVerb verb, bool basket,
            SessionIdentity identity, ImmutableArray<RootBinding> roots, AnalysisOrigin origin, AnalysisTaint taint, AnalysisPhase phase,
            NamespaceCoverage coverage, AnalysisFreshness freshness, bool hasRuntimeContext,
            Func<ulong, CancellationToken, Task<AnalysisPathMetadata?>> findPath,
            Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> readPath,
            Func<AnalysisPathMetadata, CancellationToken, Task<AnalysisCleanupReviewEvidence>> readEvidence,
            Func<CancellationToken, Task<AnalysisSelectionVisibility>> classify,
            Func<CancellationToken, Task<AnalysisCleanupTotals>> summarize, string? copyRefusal, CancellationToken lifetime)
        {
            var ids = paths.Order().ToArray();
            var locations = roots.ToDictionary(location => location.Id, StringComparer.Ordinal);
            var capabilities = new AnalysisCapabilities();
            ActionChoice[] choices =
            [
                new(AnalysisVerb.Recycle, "Recycle / Trash", "Recycle included entries",
                    "Files moved to Trash on the same volume may still occupy space there. Recovery and free-space gain are separate."),
                new(AnalysisVerb.PermanentDelete, "Permanent deletion", "Permanently delete included entries",
                    "Permanent deletion can release storage after open handles close. Other links, shared blocks and snapshots can retain it."),
                new(AnalysisVerb.Copy, "Copy to a new destination", "Copy included entries",
                    "Copying needs space at the destination and keeps the source data in place."),
                new(AnalysisVerb.Move, "Move to a new destination", "Move included entries",
                    "Moving within a filesystem usually keeps the data there. Moving across filesystems needs destination space, and source space depends on the outcome.")
            ];
            int initialChoice = Array.FindIndex(choices, choice => choice.Verb == verb);
            ActionChoice[] offered = basket ? choices : verb == AnalysisVerb.Recycle ? choices[..2] : [choices[initialChoice]];
            var action = new ComboBox { ItemsSource = offered, SelectedIndex = basket ? initialChoice : 0, IsVisible = offered.Length > 1 };
            AutomationProperties.SetName(action, "Cleanup action");
            var availability = Text("", live: true);
            var confirm = new DialogButtonPresentation(new(choices[initialChoice].Confirmation));
            string binding = origin != AnalysisOrigin.LiveSession || taint != AnalysisTaint.OrdinarySensitive
                ? "This review uses recorded observations. A fresh, ordinary live source is required for file actions."
                : phase != AnalysisPhase.Finished || freshness != AnalysisFreshness.Observed
                    ? "The observation is unfinished or may be stale. File actions require a fresh, finished live analysis."
                    : !hasRuntimeContext ? "This result has no current source binding for file actions." : "";
            action.SelectionChanged += (_, _) => DescribeAction();
            DescribeAction();

            var pageLabel = Text("", live: true);
            var visibility = Text("Checking selection against the displayed scope and filters…", live: true);
            var selectionTotals = Text("Measuring the selected observations…", live: true);
            AutomationProperties.SetName(selectionTotals, "Recorded cleanup selection totals");
            var allocationDetails = Text("");
            var list = new ListBox { MaxHeight = 248, ItemTemplate = DialogLayout.WrappingListText };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            AutomationProperties.SetName(list, "Cleanup review entries");
            var previous = new Button { Content = "Previous review entries", IsEnabled = false };
            var next = new Button { Content = "Next review entries", IsEnabled = false };
            var inspect = new Button { Content = "Inspect recorded entry", IsEnabled = false };
            var navigation = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { previous, next, inspect } };
            previous.IsVisible = next.IsVisible = ids.Length > PageSize;
            var details = Text("", live: true);
            AutomationProperties.SetName(details, "Recorded cleanup entry details");
            var technical = new Expander
            {
                Header = "Measurement and entry details",
                Content = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        Text($"{OriginLabel(origin)} · revision {identity.Revision:N0} · observed {identity.ObservedUtc.ToLocalTime():g}"),
                        allocationDetails, details
                    }
                }
            };
            var body = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Text($"{selection} · {ids.Length:N0} {(ids.Length == 1 ? "path" : "paths")} · 0 included · {ids.Length:N0} kept"),
                    action, availability, selectionTotals, visibility, pageLabel, list, navigation, technical
                }
            };
            if (coverage != NamespaceCoverage.CompleteUnderPolicy)
                body.Children.Insert(4, Text("Coverage is partial. The list shows the requested references."));

            var closer = new DialogCloser();
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            using var closeOnExit = lifetime.Register(() => Dispatcher.UIThread.Post(() => closer.Close(null)));
            var token = cancel.Token;
            token.ThrowIfCancellationRequested();
            int offset = 0;
            bool readingPage = false, readingDetails = false;
            Task pageWork = Task.CompletedTask, detailWork = Task.CompletedTask;
            Exception? unconfirmed = null;

            previous.Click += (_, _) => { if (!readingPage && !readingDetails && offset > 0) pageWork = LoadPageAsync(offset - PageSize); };
            next.Click += (_, _) => { if (!readingPage && !readingDetails && offset + PageSize < ids.Length) pageWork = LoadPageAsync(offset + PageSize); };
            inspect.Click += (_, _) =>
            {
                if (!readingDetails && !readingPage && list.SelectedItem is Entry { Path: { } path } entry)
                {
                    technical.IsExpanded = true;
                    detailWork = InspectAsync(entry, path);
                }
            };
            list.SelectionChanged += (_, _) =>
            {
                details.Text = "";
                RefreshControls();
            };

            string title = verb == AnalysisVerb.Copy ? "Review copy" : verb == AnalysisVerb.Move ? "Review move" : "Review cleanup";
            var completion = dialogs.ShowCustomAsync(title, body,
                [new("Cancel / Keep files", false, IsDefault: true, IsCancel: true),
                 new(choices[initialChoice].Confirmation, true) { Presentation = confirm, IsAvailable = CanChooseCopyFolder }],
                closer: closer, resizable: true);
            pageWork = LoadPageAsync(0);
            var countsWork = ReadVisibilityAsync();
            var totalsWork = ReadTotalsAsync();
            object? selected;
            try { selected = await completion; }
            finally
            {
                cancel.Cancel();
                await Task.WhenAll(pageWork, detailWork, countsWork, totalsWork);
            }
            if (unconfirmed is not null) throw unconfirmed;
            return selected is true && CanChooseCopyFolder() && !lifetime.IsCancellationRequested;

            bool CanChooseCopyFolder() => action.SelectedItem is ActionChoice { Verb: AnalysisVerb.Copy } &&
                binding.Length == 0 && copyRefusal is null && !lifetime.IsCancellationRequested;

            void DescribeAction()
            {
                var choice = (ActionChoice?)action.SelectedItem ?? choices[initialChoice];
                bool choose = CanChooseCopyFolder();
                string reason = choose ? "Review eligible files and choose which copies to queue. Existing destination files are kept."
                    : choice.Verb == AnalysisVerb.Copy && copyRefusal is not null ? copyRefusal : capabilities.For(choice.Verb).Reason;
                availability.Text = (offered.Length == 1 ? choice.Label + ". " : "") +
                    (binding.Length == 0 ? "" : binding + " ") + reason + "\n" + choice.SpaceEffect +
                    "\nExpected free-space gain is unknown.";
                confirm.Update(new(choose ? "Choose copy destination…" : choice.Confirmation, HelpText: reason));
            }

            void RefreshControls()
            {
                bool idle = !readingPage && !readingDetails && !token.IsCancellationRequested;
                previous.IsEnabled = idle && offset > 0;
                next.IsEnabled = idle && offset + PageSize < ids.Length;
                inspect.IsEnabled = idle && list.SelectedItem is Entry { Path: not null };
            }

            async Task LoadPageAsync(int start)
            {
                offset = start;
                readingPage = true;
                int end = Math.Min(start + PageSize, ids.Length);
                pageLabel.Text = $"Loading entries {start + 1:N0}–{end:N0} of {ids.Length:N0}…";
                list.ItemsSource = null;
                details.Text = "";
                RefreshControls();
                try
                {
                    var rows = new List<Entry>(end - start);
                    for (int index = start; index < end; index++)
                    {
                        ulong id = ids[index];
                        var path = await findPath(id, token);
                        token.ThrowIfCancellationRequested();
                        string label = path is null
                            ? $"Captured entry {id:N0} · absent from this revision"
                            : AnalysisNames.DisplayName(path, 160) + " · " + DescribeKind(path.Kind) +
                                $" · {AnalysisNames.Display(locations[path.RootId].Label, 120)} · entry {id:N0}" +
                                (path.IncludedByPolicy ? "" : " · excluded by scan policy");
                        rows.Add(new(id, path, label));
                    }
                    list.ItemsSource = rows;
                    pageLabel.Text = $"Entries {start + 1:N0}–{end:N0} of {ids.Length:N0}";
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error) when (IndexedExpectedError(error))
                {
                    if (CloseOnUnconfirmed(error) || token.IsCancellationRequested) return;
                    pageLabel.Text = "These recorded entries could not be loaded. " + AnalysisNames.Display(error.Message, 240);
                }
                finally
                {
                    readingPage = false;
                    if (!token.IsCancellationRequested) RefreshControls();
                }
            }

            async Task InspectAsync(Entry entry, AnalysisPathMetadata path)
            {
                readingDetails = true;
                details.Text = "Reading recorded entry details…";
                RefreshControls();
                try
                {
                    string location = await AnalysisRecipeDialog.RecordedPathAsync(entry.Id, path.RootId, readPath, token);
                    var evidence = await readEvidence(path, token);
                    if (!token.IsCancellationRequested && ReferenceEquals(list.SelectedItem, entry))
                    {
                        details.Text = DescribeEntry(path, locations[path.RootId], location, evidence);
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested && ReferenceEquals(list.SelectedItem, entry))
                                details.BringIntoView();
                        }, DispatcherPriority.Loaded);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error) when (IndexedExpectedError(error))
                {
                    if (CloseOnUnconfirmed(error) || token.IsCancellationRequested || !ReferenceEquals(list.SelectedItem, entry)) return;
                    details.Text = "Recorded entry details unavailable. " + AnalysisNames.Display(error.Message, 240);
                }
                finally
                {
                    readingDetails = false;
                    if (!token.IsCancellationRequested) RefreshControls();
                }
            }

            async Task ReadVisibilityAsync()
            {
                try
                {
                    var counts = await classify(token);
                    token.ThrowIfCancellationRequested();
                    if (counts.Identity != identity || counts.Total != ids.Length)
                        throw new InvalidDataException("Scope counts belong to a different selection.");
                    visibility.Text = $"{counts.InScope:N0} in the displayed scope · {counts.Elsewhere:N0} elsewhere · " +
                        $"{counts.HiddenByFilter:N0} hidden by filters · {counts.Missing:N0} absent from this revision.";
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error) when (IndexedExpectedError(error))
                {
                    if (!CloseOnUnconfirmed(error) && !token.IsCancellationRequested)
                        visibility.Text = "Scope and filter counts unavailable. " + AnalysisNames.Display(error.Message, 240);
                }
            }

            async Task ReadTotalsAsync()
            {
                try
                {
                    var totals = await summarize(token);
                    token.ThrowIfCancellationRequested();
                    if (totals.Identity != identity || totals.Selected != ids.Length)
                        throw new InvalidDataException("Totals belong to a different captured selection.");
                    (selectionTotals.Text, allocationDetails.Text) = DescribeTotals(totals);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error) when (IndexedExpectedError(error))
                {
                    if (!CloseOnUnconfirmed(error) && !token.IsCancellationRequested)
                        selectionTotals.Text = "Selection totals unavailable. " + AnalysisNames.Display(error.Message, 240);
                }
            }

            bool CloseOnUnconfirmed(Exception error)
            {
                if (!AnalysisCloseUnconfirmedException.IsPresent(error)) return false;
                unconfirmed ??= error;
                closer.Close(null);
                return true;
            }
        }

        private static (string Summary, string Details) DescribeTotals(AnalysisCleanupTotals totals)
        {
            var lines = new List<string>();
            var details = new List<string>();
            if (totals.MeasuredFiles > 0)
                lines.Add($"Recorded file size · {Bytes(totals.LogicalBytes)} across {totals.MeasuredFiles:N0} measured file paths.");
            if (totals.UnknownSizes > 0) lines.Add($"File size unavailable for {totals.UnknownSizes:N0} selected file paths.");
            if (totals.Folders > 0) lines.Add($"Selected folders · {totals.Folders:N0}. Folder contents require a separate review.");
            if (totals.Excluded > 0) lines.Add($"Excluded by scan policy · {totals.Excluded:N0} selected entries.");

            if (totals.AllocationLabelsExceeded)
                lines.Add("The selection has too many allocation bases to summarize. Inspect individual entries.");
            else if (!totals.Allocation.IsEmpty)
            {
                lines.Add($"Reported file allocation by basis · {totals.MeasuredAllocationObjects:N0} distinct verified objects measured.");
                foreach (var component in totals.Allocation.Take(4))
                {
                    lines.Add($"{Bytes(component.Bytes)} · {AnalysisNames.Display(component.Component, 64)}");
                    details.Add($"{Bytes(component.Bytes)} · {AnalysisNames.Display(component.Component, 64)} · {AnalysisNames.Display(component.Basis, 100)}");
                }
                if (totals.Allocation.Length > 4) lines.Add($"Showing 4 of {totals.Allocation.Length:N0} component and basis combinations.");
            }
            if (totals.IncompleteAllocationPaths > 0)
                lines.Add($"Complete, distinct allocation unavailable for {totals.IncompleteAllocationPaths:N0} selected file paths.");
            return (lines.Count == 0 ? "No measured file sizes or allocation in this selection." : string.Join(Environment.NewLine, lines),
                string.Join(Environment.NewLine, details));

            static string Bytes(System.Numerics.BigInteger value) => value == 1 ? "1 byte" : $"{value:N0} bytes";
        }

        private static string DescribeEntry(AnalysisPathMetadata path, RootBinding root, string location, AnalysisCleanupReviewEvidence evidence)
        {
            var lines = new List<string>
            {
                "Recorded location · " + AnalysisRecipeDialog.LocationLabel(root),
                "Recorded path · " + location,
                $"Entry {path.Id:N0} · {DescribeKind(path.Kind)} · metadata {path.MetadataState.ToString().ToLowerInvariant()}"
            };
            if (AnalysisNames.NameEvidence(path) is { } name) lines.Add(name);
            if (!path.IncludedByPolicy) lines.Add("Excluded by scan policy.");
            if (path.Kind == AnalysisKind.Directory)
                lines.Add("Folder descendants require a fresh, unfiltered operation review. Allocation below describes the folder entry itself.");
            if (path.Cloud == true) lines.Add("Cloud or offline entry. A deletion could sync to other devices.");
            if (path.Kind == AnalysisKind.File)
            {
                lines.Add("Recorded file size · " + DescribeAmount(evidence.Object?.LogicalLength));
                if (evidence.Object?.VerifiedIdentity is null) lines.Add("Recorded object identity is unverified.");
                if (evidence.Object?.LinkCount.Value is { } count && count > 1) lines.Add($"{count:N0} links reported for this object. Removing one link may leave allocation unchanged.");
            }
            if (path.Kind is AnalysisKind.File or AnalysisKind.Directory)
            {
                var facts = AnalysisAggregate.SelectComponents(evidence.Facts.ToArray());
                if (evidence.HasMoreFacts) lines.Add("Allocation observations exceed the bounded detail view. A complete amount is unavailable.");
                else lines.Add("Reported allocation · " + DescribeAmount(AnalysisAggregate.CompatibleAllocation(facts)));
                foreach (var fact in facts.Take(12))
                    lines.Add($"{AnalysisNames.Display(fact.ComponentId, 88)} · {DescribeAmount(fact.Measurement)} · basis {AnalysisNames.Display(fact.BasisId, 100)}");
                if (facts.Count > 12) lines.Add($"Showing 12 of {facts.Count:N0} displayed allocation components.");
                if (facts.Any(fact => fact.MayShareExtents)) lines.Add("Recorded allocation may include shared extents.");
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static string DescribeAmount(AnalysisMeasurement? amount) => amount is { IsKnown: true, Value: { } bytes }
            ? bytes == 1 ? "1 byte" : $"{bytes:N0} bytes"
            : amount is { State: not MeasurementState.Known } observed ? observed.State.ToString() : "Unavailable";

        private static string DescribeKind(AnalysisKind kind) => kind switch
        {
            AnalysisKind.Directory => "Folder",
            AnalysisKind.File => "File",
            AnalysisKind.Link => "Link",
            AnalysisKind.Special => "Special entry",
            _ => "Unknown kind"
        };

        private static string OriginLabel(AnalysisOrigin origin) => origin switch
        {
            AnalysisOrigin.OwnSavedInventory => "Saved inventory",
            AnalysisOrigin.ImportedInventory => "Imported inventory",
            _ => "Live analysis observation"
        };

        private static TextBlock Text(string value, bool live = false)
        {
            var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
            if (live) AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
            return text;
        }
    }
}
