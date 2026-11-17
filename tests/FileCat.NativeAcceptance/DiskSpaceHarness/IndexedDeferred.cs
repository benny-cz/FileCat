using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private const int DeferredNativePaths = 50_000;
    private object? _indexedDeferredProbe;

    // Real ordinary metadata scan and native Avalonia window. Control events are
    // intentional test inputs; this does not establish OS input or AT qualification.
    private async Task IndexedDeferredProbeAsync()
    {
        var frames = new List<object>(); var token = _indexedLifetime.Token;
        try
        {
            var window = _window!; var model = _models[0];
            if (OperatingSystem.IsWindows()) WindowsGuestInput.DesktopGuard();
            await model.StartAsync([_fixture]);
            await ReadyAsync(() => model.CanRequestDeferredSelection && model.PublishedReportView.Query == "name:future-" &&
                model.Tree!.Snapshot.Nodes.Any(node => node.Kind == AnalysisKind.File));
            Require(model.Running, "The scan completed before the native deferred request could be captured.");
            model.PauseOrResume();
            await ReadyAsync(() => model.Phase == AnalysisPhase.Paused);
            Require(model.IsIndexedRuntimePreview, "This native fixture requires actual indexed storage.");
            int previewPaths = model.Tree!.Snapshot.Nodes.Length;
            Require(previewPaths < DeferredNativePaths, "The progressive preview unexpectedly contains the completed namespace.");
            ulong mark = model.Tree.Snapshot.Nodes.First(node => node.Kind == AnalysisKind.File).Id;
            model.Selection.MarkCaptured([mark]); model.Selection.StageCaptured([mark]);
            var marks = model.Selection.Marks; var basket = model.Selection.Basket;
            Click("Select all after scan completes");
            Require(model.HasDeferredSelection, "The native request was not captured.");
            model.PauseOrResume();
            await ReadyAsync(() => model.HasIndexedDeferredReview);
            Require(model.DeferredSelectionCount == DeferredNativePaths, "The completed captured match count differs from the generated regular files.");
            var tree = model.Tree;
            Click("Review deferred matches");
            await ReadyAsync(() => Button("Open reviewed matches in a new tab")?.IsEnabled == true);
            var keep = Button("Keep preview unchanged / Close review")!;
            Require(keep.IsDefault && keep.IsCancel, "The review lost its preserving Enter/Escape default.");
            Require(ReferenceEquals(window.FocusManager?.GetFocusedElement(), keep), "The native review did not focus its preserving default.");
            Require(!Button("Review deferred matches")!.IsEnabled, "The opener is still enabled while its review owns work.");
            foreach (var display in new[] { ("ClassicDark", 1100d, 14d), ("Classic", 480d, 20d), ("HighContrast", 1100d, 20d) })
            {
                ThemeManager.Apply(display.Item1); window.WindowState = WindowState.Normal;
                window.Width = display.Item2; window.Height = 620; window.FontSize = display.Item3;
                await ReadyAsync(() => Math.Abs(window.ClientSize.Width - display.Item2) < 2 && Math.Abs(window.ClientSize.Height - 620) < 2);
                await Task.Delay(350, token); window.UpdateLayout();
                string frame = "deferred-review-" + display.Item1 + ".png"; RenderNativeFrame(frame);
                if (Program.ReplacementCompositorCapture)
                {
                    string request = Path.Combine(_run, "capture.request"), response = Path.Combine(_run, "capture.response");
                    File.Delete(response); await File.WriteAllTextAsync(request, frame, token);
                    await ReadyAsync(() => File.Exists(response));
                    Require(await File.ReadAllTextAsync(response, token) == frame, "The compositor capture did not confirm this owned frame.");
                    File.Delete(request); File.Delete(response);
                }
                var safePosition = keep.TranslatePoint(default, window) ?? throw new IOException("The safe footer is not in its window.");
                Require(safePosition.X >= 0 && safePosition.Y >= 0 && safePosition.X + keep.Bounds.Width <= window.ClientSize.Width + 1 &&
                    safePosition.Y + keep.Bounds.Height <= window.ClientSize.Height + 1, "The preserving close button is clipped outside the native window.");
                frames.Add(new { frame, width = window.ClientSize.Width, height = window.ClientSize.Height, scale = window.RenderScaling, fontSize = window.FontSize,
                    safeDefault = keep.IsDefault && keep.IsCancel, initialPage = CapturedList().ItemCount,
                    safePosition = safePosition.ToString(), safeSize = keep.Bounds.Size.ToString(), compositorCaptured = Program.ReplacementCompositorCapture });
            }
            Click("Next deferred matches"); await ReadyAsync(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                AutomationProperties.GetAutomationId(text) == "Deferred recorded page status" && text.Text?.Contains("129", StringComparison.Ordinal) == true));
            Require(CapturedList().ItemCount == 128, "The second captured page is not bounded to 128 entries.");
            Click("Keep preview unchanged / Close review");
            await ReadyAsync(() => Button("Review deferred matches")?.IsEnabled == true);
            Require(ReferenceEquals(marks, model.Selection.Marks) && ReferenceEquals(basket, model.Selection.Basket) && ReferenceEquals(tree, model.Tree),
                "Closing review changed the preview or independent references.");
            Click("Review deferred matches"); await ReadyAsync(() => Button("Open reviewed matches in a new tab")?.IsEnabled == true);
            Click("Open reviewed matches in a new tab"); await ReadyAsync(() => window.SessionCount == 2);
            await ReadyAsync(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                AutomationProperties.GetAutomationId(text) == "Indexed captured selection" && text.Text?.StartsWith($"{DeferredNativePaths:N0} marked", StringComparison.Ordinal) == true));
            Require(!model.HasDeferredSelection && ReferenceEquals(marks, model.Selection.Marks) && ReferenceEquals(basket, model.Selection.Basket) && ReferenceEquals(tree, model.Tree),
                "Publishing the recorded tab changed or retargeted the original preview.");
            RenderNativeFrame("deferred-recorded-tab.png");
            _indexedDeferredProbe = new { finished = true, passed = true, previewPaths, completedMatches = DeferredNativePaths, reviewedMarks = DeferredNativePaths,
                sessions = window.SessionCount, preservingDefault = true, previewAndReferencesKept = true, pageLimit = 128, frames,
                nativeWindowControlEvents = true, osInputQualified = false, compositorCaptureQualified = false, assistiveTechnologyQualified = false };
            WriteReport("indexed-deferred-finished");
        }
        catch (Exception error)
        {
            string? captureError = null;
            try { RenderNativeFrame("deferred-failure.png"); } catch (Exception capture) { captureError = capture.GetType().Name; }
            _indexedDeferredProbe = new { finished = true, passed = false, frames, error = error.ToString(), captureError,
                visibleText = _window!.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray() };
            WriteReport("indexed-deferred-failed");
        }

        Button? Button(string text) => _window!.GetVisualDescendants().OfType<Button>().SingleOrDefault(button => button.IsEffectivelyVisible && Equals(button.Content, text));
        ListBox CapturedList() => _window!.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationProperties.GetName(list) == "Deferred captured matches");
        void Click(string text)
        {
            var button = Button(text) ?? throw new IOException("Native control not found: " + text);
            Require(button.IsEnabled, "Native control disabled: " + text);
            button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        }
        static void Require(bool value, string detail) { if (!value) throw new IOException(detail); }
    }
}

