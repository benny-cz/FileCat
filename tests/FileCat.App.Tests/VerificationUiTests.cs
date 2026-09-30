using System.Security.Cryptography;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Verification;

namespace FileCat.App.Tests;

/// <summary>D-57: files beside checksum files show what they match, in the list, its status line, and on request.</summary>
public sealed class VerificationUiTests
{
    [AvaloniaFact]
    public async Task Files_beside_a_checksum_file_show_their_result_and_a_large_one_is_checked_on_request()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files"); // a.txt ("alpha") and b.txt ("beta")
            var big = new byte[3 << 20];
            new Random(57).NextBytes(big);
            File.WriteAllBytes(Path.Combine(folder, "big.bin"), big);
            string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
            string Text(string text) => Sha(System.Text.Encoding.UTF8.GetBytes(text));
            File.WriteAllText(Path.Combine(folder, "SHA256SUMS"), $"{Text("alpha")}  a.txt\n{Text("not beta")}  b.txt\n{Sha(big)} *big.bin\n");
            services.Settings.VerifyAutomaticallyUpToMiB = 1; // big.bin waits for a request

            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 5); i++) await Task.Delay(20, ct);
            Assert.True(tab.HasSidecars);

            (EntryData Entry, int Store, int Row) Row(string name)
            {
                for (int i = 0; i < listing.VisibleCount; i++)
                    if (listing.GetVisible(i) is { } e && e.Name == name) return (e, listing.GetStoreIndex(i), i);
                throw new InvalidOperationException(name + " is not listed");
            }
            VerificationResult? Result(string name)
            {
                var (e, store, _) = Row(name);
                return tab.Verification(e, store);
            }
            async Task Until(Func<bool> done, string what)
            {
                for (int i = 0; i < 500 && !done(); i++)
                {
                    window.CaptureRenderedFrame(); // the rows drawn ask for their results
                    await Task.Delay(20, ct);
                }
                Assert.True(done(), what);
            }

            // Drawn rows ask; the answers come from the background.
            await Until(() => Result("a.txt")?.State == VerificationState.Matches && Result("b.txt")?.State == VerificationState.Differs
                              && Result("big.bin")?.State == VerificationState.NotChecked && Result("SHA256SUMS") is not null, "the results arrive");
            Assert.Equal("✓ SHA-256", Result("a.txt")!.Text);
            Assert.Equal("✗ SHA-256 differs", Result("b.txt")!.Text);
            Assert.StartsWith("not checked: 3 MiB", Result("big.bin")!.Text, StringComparison.Ordinal);
            Assert.Contains("File → Verify checksums and signatures", Result("big.bin")!.Details.Single(), StringComparison.Ordinal);
            var sums = Result("SHA256SUMS")!;
            Assert.Equal((VerificationState.Sidecar, "checks 3 files"), (sums.State, sums.Text));
            // The tooltip says which line of which file, and what the file's own value is.
            string tip = tab.VerificationTip(Row("b.txt").Entry, Row("b.txt").Store)!;
            Assert.Contains("SHA256SUMS, line 2 says", tip, StringComparison.Ordinal);
            Assert.Contains(Text("beta"), tip, StringComparison.Ordinal);
            Assert.Contains("; File → Verify checksums and signatures reads it again.", tip, StringComparison.Ordinal); // when it was checked

            // The status line: the focused file's result, and the folder's sum.
            listing.SetFocus(Row("a.txt").Row);
            await Until(() => tab.StatusRight.EndsWith(" · ✓ SHA-256", StringComparison.Ordinal), "the focused file's result is in the status line");
            await Until(() => tab.StatusLeft.Contains("checksums: 1 failed, 1 verified, 1 not checked", StringComparison.Ordinal),
                $"the folder's sum is in the status line ({tab.StatusLeft}; the service says {VerificationService.Current!.Summary(folder, ["a.txt", "b.txt", "big.bin", "SHA256SUMS"])})");

            // On request: the large file is read, its result kept, and its row shows it.
            listing.SetFocus(Row("big.bin").Row);
            vm.Execute(CommandIds.VerifyChecksums);
            await Until(() => services.Jobs.Jobs.Count == 1 && services.Jobs.Jobs[0].State.IsFinished(), "the job finishes");
            var job = services.Jobs.Jobs[0];
            Assert.Equal((JobKind.VerifyBeside, JobState.Completed, "1 verified, all good"), (job.Kind, job.State, job.Summary));
            await Until(() => Result("big.bin")?.State == VerificationState.Matches, "the large file's row shows its result");
            await Until(() => tab.StatusLeft.Contains("checksums: 1 failed, 2 verified", StringComparison.Ordinal), $"the sum follows ({tab.StatusLeft})");
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_folder_without_checksum_files_asks_nothing_and_the_command_says_what_it_needs()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 3); i++) await Task.Delay(20, ct);
            Assert.False(tab.HasSidecars);
            listing.SetFocus(1);
            Assert.True(listing.TryGetFocused(out var a));
            Assert.Null(tab.Verification(a, listing.FocusedStoreIndex));
            Assert.Equal("", tab.GetMetadataText(a, listing.FocusedStoreIndex, "verified", out _));

            vm.Execute(CommandIds.VerifyChecksums);
            for (int i = 0; i < 250 && vm.Notification is null; i++) await Task.Delay(20, ct);
            Assert.StartsWith("\"a.txt\" is not a checksum manifest, and no checksum file or signature beside it names it", vm.Notification, StringComparison.Ordinal);
            Assert.Empty(services.Jobs.Jobs);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
