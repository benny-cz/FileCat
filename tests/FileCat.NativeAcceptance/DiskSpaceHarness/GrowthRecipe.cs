using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private object? _growthRecipeProbe;

    private async Task GrowthRecipeProbeAsync()
    {
        const string inventory = "growth-before.fcdsa";
        const string name = "growth-payload.log";
        const int addedBytes = 32 * 1024;
        try
        {
            var window = _window!;
            var token = _indexedLifetime.Token;
            if (OperatingSystem.IsWindows()) WindowsGuestInput.DesktopGuard();
            Click("Start analysis");
            await ReadyAsync(() => Button("Open recorded result")?.IsEnabled == true);
            Click("Open recorded result");
            await ReadyAsync(() => window.SessionCount == 2 && Button("Save named analysis")?.IsEnabled == true);

            Click("Save named analysis");
            await ReadyAsync(() => Control<TextBox>("Inventory filename") is not null);
            Control<TextBox>("Inventory filename")!.Text = inventory;
            await ReadyAsync(() => Button("Save new inventory")?.IsEnabled == true);
            Click("Save new inventory");
            string savedPath = Path.Combine(_services!.Paths.AnalysisSnapshotsDirectory, inventory);
            await ReadyAsync(() => File.Exists(savedPath) && Button("Rescan all")?.IsEnabled == true && Button("Compare…")?.IsEnabled == true);
            string savedHash = Hash(savedPath);

            string source = Path.Combine(_fixture, name);
            long beforeLength = new FileInfo(source).Length;
            await using (var change = new FileStream(source, FileMode.Append, FileAccess.Write, FileShare.Read))
                await change.WriteAsync(new byte[addedBytes], token);
            _originals[source] = Hash(source);

            Click("Rescan all");
            await ReadyAsync(() => window.SessionCount == 3 && Button("Compare…")?.IsEnabled == true);
            for (int count = window.SessionCount; count > 1; count--)
            {
                _tabs!.SelectedIndex = 0;
                Click("Close analysis");
                await ReadyAsync(() => window.SessionCount == count - 1 || Button("Stop and close")?.IsEnabled == true);
                if (window.SessionCount == count) Click("Stop and close");
                await ReadyAsync(() => window.SessionCount == count - 1);
            }
            await window.WaitForIndexedCloseAsync();
            await ReadyAsync(() => Button("Compare…")?.IsEnabled == true);
            Click("Compare…");
            await ReadyAsync(() => Control<TextBox>("Saved inventory filename") is not null);
            Control<TextBox>("Saved inventory filename")!.Text = inventory;
            window.GetVisualDescendants().OfType<CheckBox>().Single(control => control.IsEffectivelyVisible &&
                Equals(control.Content, "Review structural root mapping")).IsChecked = true;
            await ReadyAsync(() => Button("Compare captured inventories")?.IsEnabled == true);
            Click("Compare captured inventories");
            await ReadyAsync(() => Mapping()?.ItemCount == 2);
            Mapping()!.SelectedIndex = 1;
            await ReadyAsync(() => Button("Compare mapped observations")?.IsEnabled == true);
            Click("Compare mapped observations");
            await ReadyAsync(() => window.SessionCount == 2 && Button("View observed growth")?.IsEnabled == true);
            Click("View observed growth");
            await ReadyAsync(() => Control<ListBox>("Captured comparison rows") is { ItemCount: 1, IsEnabled: true } &&
                Control<TextBlock>("Applied comparison view")?.Text?.Contains("change:growing AND delta>0", StringComparison.Ordinal) == true);
            Control<ListBox>("Captured comparison rows")!.SelectedIndex = 0;
            await ReadyAsync(() => Control<TextBox>("Previous comparison details")?.Text?.Contains(name, StringComparison.Ordinal) == true &&
                Control<TextBox>("Current comparison details")?.Text?.Contains(name, StringComparison.Ordinal) == true);
            if (new FileInfo(source).Length != beforeLength + addedBytes || Hash(savedPath) != savedHash || Hash(source) != _originals[source])
                throw new IOException("The captured comparison changed the source fixture or saved analysis.");
            await RecipeFrameAsync("growth-observed.png", true, token);
            _growthRecipeProbe = new
            {
                finished = true, passed = true, sessions = window.SessionCount, name, addedBytes,
                applied = Control<TextBlock>("Applied comparison view")!.Text, sourceAndSavedInventoryPreserved = true,
                earlierTabsClosed = 2
            };
        }
        catch (Exception error)
        {
            _growthRecipeProbe = new { finished = true, passed = false, error = error.ToString() };
            try { RenderNativeFrame("growth-failure.png"); }
            catch { /* Preserve the original failure if rendering also fails. */ }
        }
        WriteReport("growth-recipe-complete");

        T? Control<T>(string name) where T : Avalonia.Controls.Control => _window!.GetVisualDescendants().OfType<T>()
            .SingleOrDefault(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == name);
        ComboBox? Mapping() => _window!.GetVisualDescendants().OfType<ComboBox>().SingleOrDefault(control =>
            control.IsEffectivelyVisible && AutomationProperties.GetName(control)?.StartsWith("Previous root for current ", StringComparison.Ordinal) == true);
        Button? Button(string name) => _window!.GetVisualDescendants().OfType<Button>()
            .SingleOrDefault(button => button.IsEffectivelyVisible && Equals(button.Content, name));
        void Click(string name)
        {
            var button = Button(name);
            if (button?.IsEnabled != true) throw new InvalidOperationException($"Native command unavailable: {name}");
            button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        }
    }
}
