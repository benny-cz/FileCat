using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Analysis;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private async Task FolderExclusionProbeAsync()
    {
        try
        {
            if (OperatingSystem.IsWindows()) WindowsGuestInput.DesktopGuard();
            var window = _window!;
            var model = _models.Single();
            Button? VisibleButton(string caption) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
                button.IsEffectivelyVisible && button.Content as string == caption);
            void Click(string caption) => (VisibleButton(caption) ?? throw new InvalidOperationException($"Missing button {caption}."))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var limits = VisibleButton("Resource limits…") ?? throw new InvalidOperationException("Missing resource limits in setup.");
            var setup = limits.GetVisualAncestors().OfType<ScrollViewer>().First();
            await ReadyAsync(() => setup.Viewport.Height > 0 && limits.Bounds.Height > 0);
            var limitsTop = limits.TranslatePoint(default, setup) ?? throw new InvalidOperationException("Resource limits are outside the setup view.");
            setup.Offset = new Vector(setup.Offset.X, Math.Max(0, setup.Offset.Y + limitsTop.Y + limits.Bounds.Height - setup.Viewport.Height));
            await ReadyAsync(() => limits.TranslatePoint(default, setup) is { } top && top.Y >= 0 &&
                top.Y + limits.Bounds.Height <= setup.Viewport.Height + 1);
            Click("Resource limits…");
            await ReadyAsync(() => VisibleButton("Close limits") is { IsDefault: true, IsCancel: true });
            var closeLimits = VisibleButton("Close limits")!;
            if (closeLimits.TranslatePoint(default, window) is not { } closeTop || closeTop.Y < 0 ||
                closeTop.Y + closeLimits.Bounds.Height > window.ClientSize.Height)
                throw new InvalidOperationException("The resource limits close button was clipped.");
            await RecipeFrameAsync("folder-exclusions-limits.png", true, _indexedLifetime.Token);
            Click("Close limits");
            await ReadyAsync(() => VisibleButton("Close limits") is null);
            if (model.Tree is not null) throw new InvalidOperationException("Viewing the limits started a scan.");
            var expander = window.GetVisualDescendants().OfType<Expander>().Single(item => item.Header as string == "Exclude folders…");
            expander.IsExpanded = true;
            TextBox? Editor() => window.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(box =>
                box.IsEffectivelyVisible && AutomationProperties.GetName(box) == "Excluded folders for location 1");
            Button? StartButton() => VisibleButton("Start analysis");
            await ReadyAsync(() => Editor() is not null);
            var editor = Editor()!;
            editor.Text = "Small files";
            editor.BringIntoView();
            await ReadyAsync(() => editor.Bounds.Height > 0 && StartButton()?.IsEnabled == true);
            var start = StartButton()!;
            var position = start.TranslatePoint(default, window);
            if (position is null || position.Value.Y < 0 || position.Value.Y + start.Bounds.Height > window.ClientSize.Height + 1)
                throw new InvalidOperationException("Start analysis was clipped after opening the exclusions editor.");
            if (window.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible &&
                button.Content as string is "Export entire inventory…" or "Export report…"))
                throw new InvalidOperationException("The ready scanner showed result export commands.");
            var panelFilter = window.GetVisualDescendants().OfType<CheckBox>().Single(box =>
                AutomationProperties.GetName(box) == "Use panel filter for display after scan");
            if (panelFilter.IsChecked == true) throw new InvalidOperationException("A new scan selected the panel display filter automatically.");
            panelFilter.BringIntoView();
            await ReadyAsync(() => panelFilter.TranslatePoint(default, window) is { } top && top.Y >= 0 &&
                top.Y + panelFilter.Bounds.Height <= window.ClientSize.Height + 1);
            panelFilter.IsChecked = true;
            await ReadyAsync(() => panelFilter.IsChecked == true);
            RenderNativeFrame("folder-exclusions-setup.png");
            start.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await ReadyAsync(() => model.Tree?.Snapshot.Phase == AnalysisPhase.Finished && model.PanelFilterApplied);
            var snapshot = model.Tree!.Snapshot;
            var skipped = snapshot.BoundaryTotals.GetValueOrDefault(AnalysisBoundaryKind.FolderExclusion);
            if (snapshot.Coverage != NamespaceCoverage.CompleteUnderPolicy || model.SourceCloseFailures != 0 || skipped != 1 ||
                snapshot.Nodes.Any(node => node.Name.StartsWith("item-", StringComparison.Ordinal)) ||
                !snapshot.Nodes.Any(node => node.Name == "Small files" && !node.IncludedByPolicy) ||
                !snapshot.Nodes.Any(node => node.Name == "photo.png" && node.IncludedByPolicy))
                throw new InvalidDataException("The native scan did not preserve the requested exclusion and sibling evidence.");
            if (model.Tree.Projection.Totals.Known != 60 || model.Tree.Projection.Totals.MatchingPaths != 1)
                throw new InvalidDataException("The native view did not apply only the requested panel filename mask.");
            await ReadyAsync(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
                text.Text?.Contains("Folder exclusion rules: 1. Folders skipped: 1.", StringComparison.Ordinal) == true));
            var panelStatus = window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Panel filter active");
            Point? panelTop = panelStatus.TranslatePoint(default, window);
            if (panelTop is null || panelTop.Value.Y < 0 || panelTop.Value.Y + panelStatus.Bounds.Height > window.ClientSize.Height)
                throw new InvalidOperationException("The display filter status was clipped in the compact results.");
            var table = window.GetVisualDescendants().OfType<ListBox>().Single(view =>
                view.IsEffectivelyVisible && AutomationProperties.GetName(view) == "Ranked analysis entries");
            var tableTop = table.TranslatePoint(default, window);
            if (tableTop is null || table.Bounds.Height < 30 || tableTop.Value.Y + 30 > window.ClientSize.Height)
                throw new InvalidOperationException("The ranked table was below the compact window viewport.");
            if (!window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Value" &&
                AutomationProperties.GetName(text) == "Active metric"))
                throw new InvalidOperationException("The compact table did not keep a complete value heading and its accessible name.");
            if (VisibleButton("Copy summary") is not { IsEnabled: true })
                throw new InvalidOperationException("The compact result did not offer its summary as text.");
            RenderNativeFrame("folder-exclusions-result.png");
            Click("Clear filter");
            await ReadyAsync(() => !model.PanelFilterApplied);
            string rootScope = AnalysisTree.Key(snapshot.Nodes.Single(node => node.ParentId == 0).Id);
            string packageScope = AnalysisTree.Key(snapshot.Nodes.Single(node => node.Name == "Package.app").Id);
            model.Focus(rootScope, open: true);
            await ReadyAsync(() => model.Selection.Scope == rootScope);
            model.Focus(packageScope);
            Click("Inspect selection");
            await ReadyAsync(() => VisibleButton("Close details") is not null);
            bool packageHint = window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
                text.Text?.Contains("The recorded scan policy includes package contents where accessible.", StringComparison.Ordinal) == true);
            if (packageHint != OperatingSystem.IsMacOS()) throw new InvalidOperationException("The package hint did not match the captured platform.");
            await RecipeFrameAsync("folder-exclusions-package.png", true, _indexedLifetime.Token);
            Click("Close details");
            await ReadyAsync(() => VisibleButton("Close details") is null);
            model.Focus(packageScope, open: true);
            await ReadyAsync(() => model.Rows.Any(row => row.Name == "Contents"));
            model.NavigateToScope(rootScope);
            await ReadyAsync(() => model.Selection.Scope == rootScope);
            model.Focus(AnalysisTree.Key(snapshot.Nodes.Single(node => node.Name == "Small files").Id));
            Click("Inspect selection");
            await ReadyAsync(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
                text.Text?.StartsWith("Small files\nExcluded by policy\nThis folder and its contents were skipped under the captured folder exclusion rules.",
                    StringComparison.Ordinal) == true));
            if (VisibleButton("Copy details") is not { IsEnabled: true })
                throw new InvalidOperationException("The compact inspector did not offer its selected details as text.");
            await RecipeFrameAsync("folder-exclusions-inspector.png", true, _indexedLifetime.Token);
            Click("Close details");
            await ReadyAsync(() => !window.GetVisualDescendants().OfType<Button>().Any(button =>
                button.IsEffectivelyVisible && button.Content as string == "Close details"));
            Click("New analysis");
            await ReadyAsync(() => window.SessionCount == 2 && _tabs?.SelectedIndex == 1);
            window.SelectTemporaryWork();
            await ReadyAsync(() => _tabs?.SelectedIndex == 0);
            await CheckIndexedCopyPathAsync(window);
            var saved = await model.SaveNewAsync("native-current-source.fcdsa", _indexedLifetime.Token);
            var historical = await model.OpenSavedAsync(saved.Name, _indexedLifetime.Token);
            historical.Chart = "Table";
            window.AddSession(historical);
            await historical.RebuildAsync(true, _indexedLifetime.Token);
            historical.Focus(AnalysisTree.Key(historical.Tree!.Snapshot.Nodes.Single(node => node.Name == "photo.png").Id));
            await ReadyAsync(() => VisibleButton("Inspect selection") is { IsEnabled: true });
            Click("Inspect selection");
            await ReadyAsync(() => VisibleButton("Bind current source…") is { IsEnabled: true });
            Click("Close details");
            await ReadyAsync(() => VisibleButton("Close details") is null);
            if (window.SessionCount != 4 || historical.Tree?.Snapshot.Origin == AnalysisOrigin.LiveSession)
                throw new InvalidOperationException("The historical inspector did not preserve its offline source.");
            var comparison = await model.CompareSavedAsync(saved.Name, _indexedLifetime.Token);
            window.AddSession(comparison);
            ListBox? ComparedRows() => window.GetVisualDescendants().OfType<ListBox>().SingleOrDefault(control =>
                control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Comparison rows");
            await ReadyAsync(() => VisibleButton("Copy summary") is { IsEnabled: true } && ComparedRows() is { ItemCount: > 0 });
            ComparedRows()!.SelectedIndex = 0;
            Click("Inspect comparison entry");
            await ReadyAsync(() => VisibleButton("Copy details") is { IsEnabled: true });
            var currentPaths = window.GetVisualDescendants().OfType<ListBox>().Single(list => list.IsEffectivelyVisible &&
                AutomationProperties.GetName(list) == "Current observed paths");
            currentPaths.SelectedIndex = 0;
            await ReadyAsync(() => VisibleButton("Copy displayed Current path") is { IsEnabled: true });
            var comparedPath = VisibleButton("Copy displayed Current path")!;
            comparedPath.BringIntoView();
            await ReadyAsync(() => comparedPath.TranslatePoint(default, window) is { } top && top.Y >= 0 &&
                top.Y + comparedPath.Bounds.Height <= window.ClientSize.Height);
            Click("Close");
            await ReadyAsync(() => VisibleButton("Close") is null);
            string photo = AnalysisTree.Key(snapshot.Nodes.Single(node => node.Name == "photo.png").Id);
            await CheckPanelRevealAsync(window, model, packageScope, photo);
            _indexedProbe = new { kind = "folder-exclusions", skipped, observed = snapshot.Nodes.Length, setupButtonVisible = true,
                sourceCloseFailures = model.SourceCloseFailures, coverage = snapshot.Coverage.ToString(), selectedTemporaryAnalysis = true,
                panelFilterViewApplied = true, exclusionReasonVisible = true, resourceLimitsReachable = true,
                packageHint, packageContentsReachable = true, indexedCopyPathReachable = true, summaryAndDetailsAvailable = true,
                currentSourceBindingAvailable = true, comparisonCopyOptionsAvailable = true, comparedPathCopyReachable = true,
                readOnlyPanelReveal = true, exactFilePanelFocus = true };
            _indexedProbeFinished = true;
            WriteReport("folder-exclusion-probe-finished");
        }
        catch (Exception error)
        {
            _indexedProbeError = error.GetType().Name + ": " + error.Message;
            try { RenderNativeFrame("folder-exclusions-failure.png"); } catch (Exception) { }
            WriteReport("folder-exclusion-probe-failed");
        }
    }

    private async Task CheckPanelRevealAsync(DiskSpaceWindow window, DiskSpaceViewModel model, string folder, string file)
    {
        var vm = new MainViewModel(_services!);
        var main = new MainWindow(vm, null);
        vm.Initialize(null);
        main.Show();
        try
        {
            await ReadyAsync(() => _tabs is { IsVisible: true });
            _tabs!.SelectedIndex = 0;
            var shown = await RevealAsync(folder);
            if (shown.Listing.Provider?.GetCapabilities(shown.Location!) != LocationCapabilities.Enumerate ||
                shown.Listing.Store.Count != 1 || shown.Listing.Store[0].Name != "Contents" || shown.Listing.GetItemRef(0).FileSystemPath is not null)
                throw new InvalidOperationException("Reveal did not show the verified package folder in a read-only workspace tab.");
            var selected = await RevealAsync(file);
            if (!selected.Listing.TryGetFocused(out var row) || row.Name != "photo.png")
                throw new InvalidOperationException("Reveal did not focus the exact selected file in its verified folder.");

            async Task<TabViewModel> RevealAsync(string key)
            {
                var previous = vm.ActiveTab;
                window.Activate();
                model.Focus(key);
                Button? Find(string label) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
                    button.IsEffectivelyVisible && button.Content as string == label);
                await ReadyAsync(() => Find("Inspect selection") is { IsEnabled: true });
                Find("Inspect selection")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await ReadyAsync(() => Find("Reveal in panel") is { IsEnabled: true });
                Find("Reveal in panel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await ReadyAsync(() => vm.ActiveTab is { Location.Scheme: Schemes.AnalysisFolder, Listing.State: ListingState.Complete } current &&
                    !ReferenceEquals(previous, current));
                return vm.ActiveTab!;
            }
        }
        finally
        {
            main.Close();
            foreach (var tab in vm.Workspace.Panels.SelectMany(panel => panel.Tabs).ToArray()) tab.Dispose();
        }
    }

    private async Task CheckIndexedCopyPathAsync(DiskSpaceWindow window)
    {
        var services = _services!;
        var cancellation = _indexedLifetime.Token;
        await using var capture = await AnalysisIndexedAcquisition.PrepareAsync(services, cancellation);
        IAnalysisBackend? source = await AnalysisIo.Run(services.Io, PathUtil.GetDeviceKey(_fixture), _ => OperatingSystem.IsWindows()
            ? (IAnalysisBackend)new WindowsAnalysisBackend(_fixture) : new UnixAnalysisBackend(_fixture), cancellation);
        try
        {
            var session = capture.Start([source]); source = null;
            using var stop = cancellation.Register(session.Stop);
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(45), cancellation);
            if (session.Snapshot().Phase != AnalysisPhase.Finished || session.SourceCloseFailures != 0)
                throw new IOException("The native clipboard fixture did not finish its indexed scan.");
            await using var lease = await session.AcquireDatasetAsync(cancellation) ?? throw new IOException("Missing indexed clipboard fixture.");
            var explorer = await window.AddIndexedSessionAsync(lease, AnalysisMetric.Files, cancellation: cancellation);
            ListBox? IndexedList() => window.GetVisualDescendants().OfType<ListBox>().SingleOrDefault(control =>
                control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Indexed analysis entries");
            await ReadyAsync(() => IndexedList() is { ItemCount: 1 });
            var list = IndexedList()!;
            var root = list.Items.OfType<AnalysisHierarchyRow>().Single();
            list.SelectedItem = root;
            window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible &&
                button.Content as string == "Explore selected folder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ReadyAsync(() => explorer.Page?.ParentId == root.Path.Id && explorer.PendingRequests == 0);
            list.SelectedItem = list.Items.OfType<AnalysisHierarchyRow>().Single(row => row.Path.Name == "Package.app");
            await ReadyAsync(() => explorer.Inspection?.Row.Path.Name == "Package.app" &&
                window.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible &&
                    button.IsEnabled && button.Content as string == "Copy path"));
            var copy = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible &&
                button.Content as string == "Copy path");
            copy.BringIntoView();
            await ReadyAsync(() => copy.TranslatePoint(default, window) is { } top && top.Y >= 0 &&
                top.Y + copy.Bounds.Height <= window.ClientSize.Height + 1);
        }
        finally { if (source is not null) await AnalysisIo.ReleaseAsync(services.Io, source); }
    }
}
