using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

/// <summary>
/// A job that meets a locked file asks what to do; closing FileCat meanwhile asks about the running operations. Both
/// questions show (their text once spun Avalonia's headless layout without end, and CI hung when an antivirus scan held
/// a file a test deleted).
/// </summary>
public sealed class ExitWithWaitingJobTests
{
    [AvaloniaFact]
    public async Task A_delete_that_meets_a_locked_file_asks_and_closing_then_asks_about_it()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Only Windows keeps an open file from being deleted.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        string original = Path.Combine(root, "files", "a.txt");
        var hold = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var dialogs = (OverlayDialogService)vm.Dialogs;
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount >= 3); i++) await Task.Delay(20, ct);
            vm.Execute(CommandIds.FindFiles);
            for (int i = 0; i < 250 && FindWindow.OpenWindows.Count == 0; i++) await Task.Delay(20, ct);
            var find = Assert.Single(FindWindow.OpenWindows);
            find.Activate();
            for (int i = 0; i < 250 && !find.NamesBox.IsFocused; i++) await Task.Delay(20, ct);
            find.NamesBox.Text = "a.txt";
            find.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            for (int i = 0; i < 250 && !find.IsIdle; i++) await Task.Delay(20, ct);
            var results = find.ResultsTab!.Listing;
            for (int i = 0; i < 250 && results.VisibleCount != 1; i++) await Task.Delay(20, ct);
            find.List.Focus();
            results.SetFocus(0);
            find.KeyPress(Key.F8, RawInputModifiers.Shift, PhysicalKey.F8, null);
            for (int i = 0; i < 250 && !find.Dialogs.IsOpen; i++) await Task.Delay(20, ct);
            find.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            // Background decisions stay in Operations until the user explicitly reviews them.
            for (int i = 0; i < 400 && !services.Jobs.Jobs.Any(j => j.State == JobState.AwaitingDecision); i++) await Task.Delay(20, ct);
            Assert.Contains(services.Jobs.Jobs, j => j.State == JobState.AwaitingDecision);
            Assert.False(dialogs.IsOpen);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Review decision…" && b.IsVisible);
            Assert.True(File.Exists(original));

            // Closing now asks about the operation still waiting.
            find.Close();
            window.Close();
            bool Asked() => window.GetVisualDescendants().OfType<TextBlock>().Any(b => b.Text == "Operations are still running");
            for (int i = 0; i < 100 && !Asked(); i++) await Task.Delay(20, ct);
            Assert.True(Asked());
            Assert.True(window.IsVisible, "FileCat stays open until the question is answered");
        }
        finally
        {
            foreach (var w in FindWindow.OpenWindows.ToList()) w.Close();
            AccessibilityTests.Close(services, window, root);
            hold.Dispose();
        }
    }
}
