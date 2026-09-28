using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class ChecksumManifestDialogTests
{
    [AvaloniaFact]
    public async Task A_manifest_is_verified_on_request_and_failing_files_open_as_a_result_set()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files");
            string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            File.WriteAllText(Path.Combine(folder, "sums.sha256"), $"{Sha("alpha")}  a.txt\n{Sha("not beta")}  b.txt\n");
            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 4); i++)
                await Task.Delay(20, ct);
            listing.SetFocus(3);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            Button ButtonNamed(string text) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text);
            async Task Click(string text)
            {
                for (int i = 0; i < 250 && !window.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == text); i++) await Task.Delay(20, ct);
                ButtonNamed(text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            vm.Execute(CommandIds.Checksum);
            await Click("Verify listed files");
            var job = Assert.Single(services.Jobs.Jobs);
            for (int i = 0; i < 250 && !job.State.IsFinished(); i++) await Task.Delay(20, ct);
            Assert.Equal(JobState.CompletedWithIssues, job.State);
            Assert.Equal("1 match, 1 do not match", job.Summary);

            await Click("Show 1 file in a panel");
            for (int i = 0; i < 250 && (dialogs.IsOpen || vm.ActiveTab?.Location?.Scheme != Schemes.ResultSet); i++) await Task.Delay(20, ct);
            var results = vm.ActiveTab!;
            Assert.Equal(Schemes.ResultSet, results.Location!.Scheme);
            for (int i = 0; i < 250 && !(results.Listing.State == Core.Listing.ListingState.Complete && results.Listing.VisibleCount >= 1); i++)
                await Task.Delay(20, ct);
            Assert.Equal(1, results.Listing.VisibleCount);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
