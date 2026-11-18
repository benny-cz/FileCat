using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

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
            string? packageFastNtfsReason = null;
            if (Program.PackageTier is { } tier)
            {
                packageFastNtfsReason = model.FastNtfsUnavailableReason;
                if (packageFastNtfsReason is null || !packageFastNtfsReason.Contains("Use standard analysis", StringComparison.Ordinal) ||
                    (tier == "portable" && !packageFastNtfsReason.Contains("requires the installed Windows edition", StringComparison.Ordinal)))
                    throw new InvalidOperationException("This package must offer Standard analysis instead of Fast NTFS.");
            }
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
            if (snapshot.Coverage != NamespaceCoverage.CompleteUnderPolicy || model.SourceCloseFailures != 0 || model.IsIndexedRuntimePreview || skipped != 1 ||
                snapshot.Nodes.Any(node => node.Name.StartsWith("item-", StringComparison.Ordinal)) ||
                !snapshot.Nodes.Any(node => node.Name == "Small files" && !node.IncludedByPolicy) ||
                !snapshot.Nodes.Any(node => node.Name == "photo.png" && node.IncludedByPolicy))
                throw new InvalidDataException("The native scan did not preserve the requested exclusion and sibling evidence.");
            if (model.Tree.Projection.Totals.Known != 60 || model.Tree.Projection.Totals.MatchingPaths != 1)
                throw new InvalidDataException("The native view did not apply only the requested panel filename mask.");
            await CheckNativeVolumeCapacityAsync(window, model, "folder-exclusions-capacity.png");
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
            var scratchThemes = await CheckIndexedCopyPathAsync(window);
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
                readOnlyPanelReveal = true, exactFilePanelFocus = true, readOnlySelectionResults = true, indexedScratchDetails = true,
                standardIndexChoice = true, scratchThemes, resourceNativeKeyboard = Program.ResourceKeyboardProbe, volumeCapacity = true,
                packageTier = Program.PackageTier, packageFastNtfsReason };
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

            window.Activate();
            model.Focus(file, toggleMark: true);
            Button? ResultsButton() => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
                button.IsEffectivelyVisible && button.IsEnabled && button.Content as string == "Show selection in results");
            await ReadyAsync(() => ResultsButton() is not null);
            ResultsButton()!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ReadyAsync(() => vm.ActiveTab is { Location.Scheme: Schemes.AnalysisFolder, Listing.State: ListingState.Complete } current &&
                !ReferenceEquals(selected, current));
            var results = vm.ActiveTab!;
            if (results.Listing.Store.Count != 1 || !results.Listing.Store[0].Name.EndsWith("photo.png", StringComparison.Ordinal) ||
                results.Listing.GetItemRef(0).FileSystemPath is not null)
                throw new InvalidOperationException("The workspace results did not preserve the marked observation as read-only metadata.");

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

    private sealed record ScratchThemeFrame(string Theme, string ResolvedTheme, string Frame, double Width, double Height, double Scale, double FontSize);

    private async Task CheckNativeVolumeCapacityAsync(DiskSpaceWindow window, DiskSpaceViewModel model, string frame)
    {
        var sample = model.VolumeCapacities.Values.Single();
        if (sample.WholeVolumeVerified || sample.TotalBytes == 0 || sample.AvailableBytes > sample.FreeBytes || sample.FreeBytes > sample.TotalBytes)
            throw new InvalidDataException("The test folder did not expose coherent volume capacity.");
        Button? Ribbon() => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
            button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == "AnalysisVolumeCapacity");
        Button? CloseCapacity() => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
            button.IsEffectivelyVisible && button.Content as string == "Close capacity");
        await ReadyAsync(() => Ribbon() is { IsEnabled: true });
        var ribbon = Ribbon()!;
        ribbon.BringIntoView();
        ribbon.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await ReadyAsync(() => CloseCapacity() is { IsDefault: true, IsCancel: true });
        var text = window.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text);
        if (!text.Any(value => value?.Contains("Reported free " + DiskSpaceViewModel.Bytes(sample.FreeBytes), StringComparison.Ordinal) == true &&
                value.Contains("Volume ID " + sample.VolumeIdentity, StringComparison.Ordinal) && value.Contains("scanned scope", StringComparison.Ordinal)) ||
            text.Any(value => value?.Contains("Difference from reported used space", StringComparison.Ordinal) == true))
            throw new InvalidDataException("The volume display changed the held sample or offered a whole-volume difference for a folder.");
        var close = CloseCapacity()!;
        if (close.TranslatePoint(default, window) is not { } top || top.Y < 0 || top.Y + close.Bounds.Height > window.ClientSize.Height)
            throw new InvalidOperationException("The compact capacity dialog clipped its close control.");
        await RecipeFrameAsync(frame, true, _indexedLifetime.Token);
        close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await ReadyAsync(() => CloseCapacity() is null);
    }

    private async Task<IReadOnlyList<ScratchThemeFrame>> CheckIndexedCopyPathAsync(DiskSpaceWindow window)
    {
        var services = _services!;
        var cancellation = _indexedLifetime.Token;
        _tabs!.SelectedIndex = 1;
        var model = _models[1];
        ListBox? IndexedList() => window.GetVisualDescendants().OfType<ListBox>().SingleOrDefault(control =>
            control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Indexed analysis entries");
        Button? VisibleButton(string label) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
            button.IsEffectivelyVisible && button.Content as string == label);
        CheckBox? IndexChoice() => window.GetVisualDescendants().OfType<CheckBox>().SingleOrDefault(control =>
            control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Use a private index for standard analysis");
        await ReadyAsync(() => IndexChoice() is { IsEnabled: true } && VisibleButton("Start analysis") is { IsEnabled: true });
        var option = IndexChoice()!;
        if (option.IsChecked == true) throw new InvalidOperationException("Standard setup selected private indexing automatically.");
        option.BringIntoView();
        await ReadyAsync(() => option.TranslatePoint(default, window) is { } top && top.Y >= 0 &&
            top.Y + option.Bounds.Height <= window.ClientSize.Height);
        option.IsChecked = true;
        VisibleButton("Start analysis")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try { await ReadyAsync(() => model.CanOpenRecordedResult && VisibleButton("Open recorded result") is { IsEnabled: true }); }
        catch (TimeoutException error)
        {
            throw new InvalidOperationException($"Standard recorded result did not become ready. Phase: {model.Phase}. Indexed preview: {model.IsIndexedRuntimePreview}. " +
                $"Reported state: {model.Status} {model.AcquisitionNotice} Open refusal: {model.RecordedResultUnavailableReason}", error);
        }
        if (model.Phase != AnalysisPhase.Finished || model.SourceCloseFailures != 0)
            throw new IOException("The native standard indexed scan did not finish.");
        await using var lease = await model.AcquireRecordedDatasetAsync(cancellation);
        VisibleButton("Open recorded result")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await ReadyAsync(() => IndexedList() is { ItemCount: 1 } && VisibleButton("Resource limits…") is { IsEnabled: true });
        await CheckNativeVolumeCapacityAsync(window, model, "folder-exclusions-indexed-capacity.png");
        var limits = VisibleButton("Resource limits…")!;
        limits.BringIntoView();
        await ReadyAsync(() => limits.TranslatePoint(default, window) is { } top && top.Y >= 0 &&
            top.Y + limits.Bounds.Height <= window.ClientSize.Height + 1);
        long reserved = lease.ScratchReservedBytes;
        if (reserved <= 0) throw new InvalidOperationException("The native recorded index held no scratch reservation.");
        if (Program.ResourceKeyboardProbe) limits.Focus();
        limits.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await ReadyAsync(() => VisibleButton("Close limits") is { IsDefault: true, IsCancel: true });
        var rows = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToArray();
        var rules = OperatingSystem.IsWindows() ? AnalysisNameRules.Windows : AnalysisNameRules.Unix;
        var reservation = rows.SingleOrDefault(text => text.Text == $"{DiskSpaceViewModel.Bytes((UInt128)reserved)} for this index and its derived data.");
        if (!rows.Any(text => text.Text == AnalysisNames.DisplayPath(services.Paths.AnalysisScratchDirectory, rules)) || reservation is null)
            throw new InvalidOperationException("The limits overlay did not show the recorded index's scratch location and actual reservation.");
        if (VisibleButton("Close limits") is not { } closeLimits || closeLimits.TranslatePoint(default, window) is not { } closeTop ||
            closeTop.Y < 0 || closeTop.Y + closeLimits.Bounds.Height > window.ClientSize.Height)
            throw new InvalidOperationException("The recorded resource limits close button was clipped.");
        if (reservation.TranslatePoint(default, window) is not { } reservationTop || reservationTop.Y < 0 ||
            reservationTop.Y + reservation.Bounds.Height >= closeTop.Y)
            throw new InvalidOperationException("The scratch reservation is below the limits viewport.");
        await RecipeFrameAsync("folder-exclusions-scratch.png", true, cancellation);
        var themes = new List<ScratchThemeFrame>();
        string previousTheme = ThemeManager.RequestedName;
        try
        {
            foreach (string theme in ThemeManager.Names)
            {
                ThemeManager.Apply(theme);
                await Task.Delay(100, cancellation);
                window.UpdateLayout();
                if (VisibleButton("Close limits") is not { IsDefault: true, IsCancel: true } close ||
                    close.TranslatePoint(default, window) is not { } closePoint || closePoint.X < 0 || closePoint.Y < 0 ||
                    closePoint.X + close.Bounds.Width > window.ClientSize.Width + 1 || closePoint.Y + close.Bounds.Height > window.ClientSize.Height + 1 ||
                    reservation.TranslatePoint(default, window) is not { } amount || amount.Y + reservation.Bounds.Height >= closePoint.Y)
                    throw new InvalidOperationException($"The scratch details and preserving footer were clipped in {theme}.");
                if (!ReferenceEquals(window.FocusManager?.GetFocusedElement(), close))
                    throw new InvalidOperationException($"The resource dialog lost the focused Close control in {theme}.");
                string frame = $"folder-exclusions-scratch-theme-{themes.Count:D2}.png";
                await RecipeFrameAsync(frame, true, cancellation);
                themes.Add(new(theme, ThemeManager.Current.Name, frame, window.ClientSize.Width, window.ClientSize.Height, window.RenderScaling, window.FontSize));
            }
        }
        finally { ThemeManager.Apply(previousTheme); }
        if (Program.ResourceKeyboardProbe)
        {
            await CloseWithNativeKeyAsync("Return");
            limits.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await CloseWithNativeKeyAsync("Escape");
        }
        else closeLimits.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await ReadyAsync(() => VisibleButton("Close limits") is null);
        var list = IndexedList()!;
        var root = list.Items.OfType<AnalysisHierarchyRow>().Single();
        list.SelectedItem = root;
        window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible &&
            button.Content as string == "Explore selected folder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await ReadyAsync(() => list.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Name == "Package.app"));
        list.SelectedItem = list.Items.OfType<AnalysisHierarchyRow>().Single(row => row.Path.Name == "Package.app");
        await ReadyAsync(() => VisibleButton("Copy path") is { IsEnabled: true } &&
            window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
                text.Text == "Captured metadata ready for Package.app."));
        var copy = VisibleButton("Copy path")!;
        var viewport = copy.GetVisualAncestors().OfType<ScrollViewer>().First();
        await ReadyAsync(() =>
        {
            if (copy.TranslatePoint(default, viewport) is not { } top || viewport.Viewport.Height <= 0) return false;
            double bottom = top.Y + copy.Bounds.Height;
            if (top.Y >= 0 && bottom <= viewport.Viewport.Height + 1) return true;
            double move = top.Y < 0 ? top.Y : bottom - viewport.Viewport.Height;
            viewport.Offset = new Vector(viewport.Offset.X, Math.Max(0, viewport.Offset.Y + move));
            return false;
        });
        return themes;

        async Task CloseWithNativeKeyAsync(string key)
        {
            await ReadyAsync(() => VisibleButton("Close limits") is { } dismiss && ReferenceEquals(window.FocusManager?.GetFocusedElement(), dismiss));
            string request = Path.Combine(_run, "resource-key.request"), response = Path.Combine(_run, "resource-key.response");
            string pending = request + ".pending";
            await File.WriteAllTextAsync(pending, key, cancellation);
            File.Move(pending, request);
            await ReadyAsync(() => File.Exists(response));
            if (await File.ReadAllTextAsync(response, cancellation) != key) throw new InvalidOperationException("Unexpected native key acknowledgement.");
            await ReadyAsync(() => VisibleButton("Close limits") is null && ReferenceEquals(window.FocusManager?.GetFocusedElement(), limits));
            File.Delete(request); File.Delete(response);
        }
    }
}
