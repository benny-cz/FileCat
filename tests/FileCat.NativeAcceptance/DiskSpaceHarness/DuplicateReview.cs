using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private object? _duplicateReviewProbe;

    private void CreateDuplicateProbeFiles()
    {
        const string original = "duplicate-review-primary.bin";
        Add(original, 4);
        Add("duplicate-review-unique.bin", 1);
        string separate = Path.Combine(_fixture, "duplicate-review-same-size.bin");
        File.WriteAllBytes(separate, [4, 3, 2, 1]);
        _originals.Add(separate, Hash(separate));
        string source = Path.Combine(_fixture, original), alias = Path.Combine(_fixture, "duplicate-review-alias.bin");
        if (!(OperatingSystem.IsWindows() ? HardLink(alias, source, 0) : UnixLink(source, alias) == 0))
            throw new IOException("The native duplicate-review fixture needs an actual hard link.");
        _originals.Add(alias, _originals[source]);
    }

    private async Task DuplicateReviewProbeAsync()
    {
        const string filter = "name:duplicate-review-";
        try
        {
            var window = _window!;
            if (OperatingSystem.IsWindows()) WindowsGuestInput.DesktopGuard();
            Click("Start analysis");
            await ReadyAsync(() => Button("Open recorded result")?.IsEnabled == true);
            Click("Open recorded result");
            await ReadyAsync(() => window.SessionCount == 2 && Button("Apply filter")?.IsEnabled == true);

            Control<TextBox>("Filter indexed observations")!.Text = filter;
            Click("Apply filter");
            await ReadyAsync(() => Matches(4) &&
                Button("Mark matching files in this scope")?.IsEnabled == true);
            Click("Mark matching files in this scope");
            await ReadyAsync(() => Marks(4, 0));
            Click("Add to review basket");
            await ReadyAsync(() => Marks(4, 4));
            Control<TextBox>("Filter indexed observations")!.Text = "name:duplicate-review-unique.bin";
            Click("Apply filter");
            await ReadyAsync(() => Matches(1) && Marks(4, 4));
            Click("Review duplicate candidates…");
            await ReadyAsync(() => Control<ListBox>("Captured duplicate candidates and exclusions")?.ItemCount == 4);

            var rows = Control<ListBox>("Captured duplicate candidates and exclusions")!.Items.Cast<string>().ToArray();
            var close = Button("Close review")!;
            bool preservingDefault = close.IsDefault && close.IsCancel;
            bool correct = rows.Count(row => row.Contains("Same-size candidate", StringComparison.Ordinal)) == 2 &&
                rows.Count(row => row.Contains("Additional name for the same recorded file", StringComparison.Ordinal)) == 1 &&
                rows.Count(row => row.Contains("No separate marked object with this size", StringComparison.Ordinal)) == 1 &&
                window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
                    text.Text?.Contains("8 B for 2 distinct candidate objects", StringComparison.Ordinal) == true);
            if (!correct || !preservingDefault) throw new IOException("Duplicate review changed its exact cost, alias treatment or preserving default.");
            await RecipeFrameAsync("duplicate-review-estimate.png", true, _indexedLifetime.Token);
            Click("Close review");
            await ReadyAsync(() => Control<ListBox>("Captured duplicate candidates and exclusions") is null && Marks(4, 4));
            _duplicateReviewProbe = new
            {
                finished = true, passed = true, sessions = window.SessionCount, candidateObjects = 2, knownReadBytes = 8,
                capturedMarks = 4, additionalHardLinkNames = 1, preservingDefault, retainedHiddenMarksAndBasket = true
            };
        }
        catch (Exception error)
        {
            _duplicateReviewProbe = new { finished = true, passed = false, error = error.ToString() };
            try { RenderNativeFrame("duplicate-review-failure.png"); }
            catch { /* Preserve the original failure if rendering also fails. */ }
        }
        WriteReport("duplicate-review-complete");

        T? Control<T>(string name) where T : Avalonia.Controls.Control => _window!.GetVisualDescendants().OfType<T>()
            .SingleOrDefault(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == name);
        Button? Button(string name) => _window!.GetVisualDescendants().OfType<Button>()
            .SingleOrDefault(button => button.IsEffectivelyVisible && Equals(button.Content, name));
        bool Matches(int count) => _window!.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
            AutomationProperties.GetAutomationId(text) == "Indexed analysis status" &&
            text.Text?.Contains($" {count:N0} definite matches;", StringComparison.Ordinal) == true);
        bool Marks(int marked, int basket) => _window!.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
            AutomationProperties.GetAutomationId(text) == "Indexed captured selection" &&
            text.Text?.StartsWith($"{marked:N0} marked · {basket:N0} in review basket", StringComparison.Ordinal) == true);
        void Click(string name)
        {
            var button = Button(name);
            if (button?.IsEnabled != true) throw new InvalidOperationException($"Native command unavailable: {name}");
            button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        }
    }
}
