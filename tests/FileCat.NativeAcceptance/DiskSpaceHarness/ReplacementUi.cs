using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Analysis;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private object? _replacementUiProbe;

    // An actual native window/control-event and filesystem test. It does not inject OS
    // pointer/keyboard input or claim compositor, assistive-technology or package coverage.
    private async Task ReplacementUiProbeAsync()
    {
        var controls = new List<object>();
        var token = _indexedLifetime.Token;
        AnalysisDatasetLease? lease = null;
        IAnalysisBackend? source = null;
        try
        {
            var window = _window!; var services = _services!; var memory = _models[0];
            if (OperatingSystem.IsWindows()) WindowsGuestInput.DesktopGuard();
            await memory.StartAsync([_fixture]);
            await ReadyAsync(() => memory.Phase == AnalysisPhase.Finished);
            await memory.RebuildAsync(true);
            Require(memory.Tree?.Snapshot.Phase == AnalysisPhase.Finished, "The actual memory scan did not finish.");
            const string memoryName = "Native memory replacement.fcdsa";
            var memoryReceipt = await memory.SaveNewAsync(memoryName, token);
            var oldMemory = await memory.OpenSavedAsync(memoryName, token);
            await oldMemory.RebuildAsync(true); _models.Add(oldMemory); window.AddSession(oldMemory);
            _tabs!.SelectedIndex = 0;
            await ExerciseAsync(false, memoryName, memoryReceipt.InventoryId, oldMemory);

            const string indexedName = "Native indexed replacement.fcdsa";
            var indexedReceipt = await memory.SaveNewAsync(indexedName, token);
            var oldIndexed = await memory.OpenSavedAsync(indexedName, token);
            await oldIndexed.RebuildAsync(true); _models.Add(oldIndexed); window.AddSession(oldIndexed);
            _indexedAcquisition = await AnalysisIndexedAcquisition.PrepareAsync(services, token);
            source = await AnalysisIo.Run(services.Io, PathUtil.GetDeviceKey(_fixture), _ => OperatingSystem.IsWindows()
                ? (IAnalysisBackend)new WindowsAnalysisBackend(_fixture) : new UnixAnalysisBackend(_fixture), token);
            var scan = _indexedAcquisition.Start([source]); source = null;
            using var stop = token.Register(scan.Stop);
            await scan.Completion.WaitAsync(TimeSpan.FromMinutes(3), token);
            Require(scan.Snapshot().Phase == AnalysisPhase.Finished && scan.SourceCloseFailures == 0, "The actual indexed scan did not close its sources.");
            lease = await scan.AcquireDatasetAsync() ?? throw new IOException("No native indexed revision was published.");
            await window.AddIndexedSessionAsync(lease, services: services, cancellation: token);
            await _indexedAcquisition.DisposeAsync(); _indexedAcquisition = null;
            await lease.DisposeAsync(); lease = null;
            // Review and replacement must remain possible when no additional tab fits.
            for (int n = 0; n < 4; n++) window.AddSession(new DiskSpaceViewModel(memory.Tree!.Snapshot));
            Require(window.SessionCount == 8 && !window.CanAddSession, "The native fixture did not fill the shared eight slots.");
            _tabs.SelectedIndex = 3;
            await ExerciseAsync(true, indexedName, indexedReceipt.InventoryId, oldIndexed);
            _replacementUiProbe = new { finished = true, passed = true, cases = controls, sessions = window.SessionCount,
                sourceCloseFailures = scan.SourceCloseFailures, nativeWindowControlEvents = true,
                osInputQualified = false, compositorCaptureQualified = false, assistiveTechnologyQualified = false };
            WriteReport("replacement-ui-finished");

            async Task ExerciseAsync(bool indexed, string name, Guid oldId, DiskSpaceViewModel oldView)
            {
                string path = Path.Combine(services.Paths.AnalysisSnapshotsDirectory, name), before = Hash(path);
                foreach (bool replace in new[] { false, true })
                {
                    await ReadyAsync(() => Button(indexed ? "Save named analysis" : "Save…")?.IsEnabled == true);
                    Click(indexed ? "Save named analysis" : "Save…");
                    await ReadyAsync(() => Button("Save new inventory") is not null);
                    Control<TextBox>("Inventory filename").Text = name[..^6].ToUpperInvariant() + ".fcdsa";
                    await ReadyAsync(() => Button("Save new inventory")?.IsEnabled == true);
                    Click("Save new inventory");
                    await ReadyAsync(() => Button("Keep existing") is not null);
                    var keep = Button("Keep existing")!; var change = Button("Replace analysis")!;
                    Require(keep.IsDefault && keep.IsCancel && !change.IsDefault && change.Classes.Contains("danger"), "The native replacement review lost its preserving default or danger semantics.");
                    Require(ReferenceEquals(window.FocusManager?.GetFocusedElement(), keep), "The native replacement review did not initially focus Keep existing.");
                    Require(Hash(path) == before, "The named inventory changed before the review decision.");
                    string reviewText = string.Join('\n', window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
                    Require(reviewText.Contains("Previous:", StringComparison.Ordinal) && reviewText.Contains("Incoming:", StringComparison.Ordinal) && reviewText.Contains("Source files are unchanged.", StringComparison.Ordinal), "The native replacement review is missing its version/consequence explanation.");
                    var captures = new List<object>();
                    foreach (var display in replace ? new[] { ("ClassicDark", 1100d, 14d) }
                             : new[] { ("ClassicDark", 1100d, 14d), ("Classic", 480d, 20d), ("HighContrast", 1100d, 20d) })
                    {
                        ThemeManager.Apply(display.Item1); window.WindowState = WindowState.Normal;
                        window.Width = display.Item2; window.Height = 620; window.FontSize = display.Item3;
                        await ReadyAsync(() => Math.Abs(window.ClientSize.Width - display.Item2) < 2 && Math.Abs(window.ClientSize.Height - 620) < 2);
                        await Task.Delay(350, token); window.UpdateLayout();
                        string frame = $"replacement-{(indexed ? "indexed" : "memory")}-{(replace ? "replace" : "keep")}-{display.Item1}.png";
                        RenderNativeFrame(frame);
                        if (Program.ReplacementCompositorCapture)
                        {
                            string request = Path.Combine(_run, "capture.request"), response = Path.Combine(_run, "capture.response");
                            File.Delete(response); await File.WriteAllTextAsync(request, frame, token);
                            await ReadyAsync(() => File.Exists(response));
                            Require(await File.ReadAllTextAsync(response, token) == frame, "The compositor capture did not confirm the requested owned window.");
                            File.Delete(request); File.Delete(response);
                        }
                        captures.Add(new { frame, width = window.ClientSize.Width, height = window.ClientSize.Height,
                            window.RenderScaling, windowState = window.WindowState.ToString(), fontSize = window.FontSize,
                            compositorCaptured = Program.ReplacementCompositorCapture });
                    }
                    ThemeManager.Apply("ClassicDark"); window.Width = 1100; window.FontSize = 14;
                    await Task.Delay(150, token);
                    Click(replace ? "Replace analysis" : "Keep existing");
                    await ReadyAsync(() => Button("Keep existing") is null && window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible &&
                        (replace ? t.Text?.StartsWith("The named analysis was replaced.", StringComparison.Ordinal) == true : t.Text == "The existing saved analysis was kept.")));
                    await using var owned = await AnalysisOwnedStoreAccess.OpenAsync(services, token);
                    using var catalog = await owned.ReadCatalogAsync(token);
                    var entry = catalog.Entries.Single(e => e.Name == name);
                    Require(catalog.PendingReplacements.IsEmpty && (entry.InventoryId != oldId) == replace, "The native catalog does not agree with the reviewed outcome.");
                    Require((Hash(path) != before) == replace && (oldView.SavedInventoryName is null) == replace, "The native named bytes or existing saved reference do not agree with the decision.");
                    controls.Add(new { indexed, replace, name, preservedDefaultAndFocus = true, dangerClass = true,
                        bytesChanged = Hash(path) != before, savedReferenceInvalidated = oldView.SavedInventoryName is null,
                        sessionCount = window.SessionCount, captures });
                    _replacementUiProbe = new { finished = false, passed = false, cases = controls };
                    WriteReport("replacement-ui-progress");
                }
            }
        }
        catch (Exception error)
        {
            string? captureError = null;
            try { RenderNativeFrame("replacement-failure.png"); }
            catch (Exception capture) { captureError = capture.GetType().Name; }
            _replacementUiProbe = new { finished = true, passed = false, cases = controls, error = error.ToString(), captureError,
                visibleText = _window!.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToArray() };
            WriteReport("replacement-ui-failed");
        }
        finally
        {
            if (lease is not null) await lease.DisposeAsync();
            if (source is not null) await AnalysisIo.ReleaseAsync(_services!.Io, source);
        }

        Button? Button(string text) => _window!.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.IsEffectivelyVisible && b.Content as string == text);
        T Control<T>(string name) where T : Control => _window!.GetVisualDescendants().OfType<T>().Single(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);
        void Click(string text)
        {
            var button = Button(text) ?? throw new IOException("Native control not found: " + text);
            Require(button.IsEnabled, "Native control disabled: " + text); button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        }
        static void Require(bool value, string detail) { if (!value) throw new IOException(detail); }
    }

    private void RenderNativeFrame(string name)
    {
        var window = _window!; double scale = window.RenderScaling;
        var pixels = new PixelSize(checked((int)Math.Ceiling(window.ClientSize.Width * scale)), checked((int)Math.Ceiling(window.ClientSize.Height * scale)));
        using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        bitmap.Render(window); bitmap.Save(Path.Combine(_run, name), new PngBitmapEncoderOptions());
    }
}
