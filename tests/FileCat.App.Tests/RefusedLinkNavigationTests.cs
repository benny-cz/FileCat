using System.Diagnostics;
using Avalonia.Headless.XUnit;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// Entering a link that refuses to be listed itself — Windows' compatibility junctions such as "Local Settings" deny
/// everyone "List folder", elevated or not — shows the folder it leads to, says why, and leaves Back where the link was
/// entered from, as Open Salamander does. Before, the panel stopped at "Access to the path … is denied".
/// </summary>
public sealed class RefusedLinkNavigationTests
{
    [AvaloniaFact]
    public async Task Entering_a_link_that_refuses_to_be_listed_shows_the_folder_it_leads_to()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Only Windows gives a link permissions of its own.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        string files = Path.Combine(root, "files");
        string link = Path.Combine(files, "Local Settings");
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string target = Directory.CreateDirectory(Path.Combine(root, "AppData", "Local")).FullName;
            Directory.CreateDirectory(Path.Combine(target, "Microsoft"));
            File.WriteAllText(Path.Combine(target, "kept.txt"), "kept");
            Run("cmd.exe", "/d", "/c", "mklink", "/J", link, target);
            Run("icacls.exe", link, "/deny", "*S-1-1-0:(RD)", "/L");
            Assert.Throws<UnauthorizedAccessException>(() => Directory.EnumerateFileSystemEntries(link).GetEnumerator().Dispose());

            var tab = vm.Workspace.Panels[1].ActiveTab!;
            tab.Refresh();
            for (int i = 0; i < 250 && !(tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("Local Settings")); i++) await Task.Delay(20, ct);
            Assert.Equal(files, tab.Location?.Path);

            // Enter on the link, as in the panel.
            Assert.True(tab.TryEnterFocused(out _));
            for (int i = 0; i < 250 && (tab.Location?.Path != target || tab.Listing.State != ListingState.Complete); i++) await Task.Delay(20, ct);
            Assert.True(target == tab.Location?.Path, $"at {tab.Location}, state {tab.Listing.State}, error {tab.Listing.Error}");
            Assert.Equal(["Microsoft", "kept.txt"], Names(tab.Listing));
            Assert.Equal(target, tab.DisplayPath);
            Assert.Equal("“Local Settings” is a link that refuses to be listed itself, as Windows' compatibility junctions do. This is the folder it leads to.", tab.Banner);
            // The folder took the link's place: Back returns to where the link was entered from.
            Assert.Equal(Location.FileSystem(files), tab.BackHistory[^1]);
            Assert.DoesNotContain(tab.BackHistory, l => l.Path == link);

            // A name to put the cursor on carries over, as when going up from a folder reached through the link.
            tab.Navigate(Location.FileSystem(link), "kept.txt");
            for (int i = 0; i < 250 && !(tab.Location?.Path == target && tab.Listing.State == ListingState.Complete && tab.Listing.TryGetFocused(out var f) && f.Name == "kept.txt"); i++)
                await Task.Delay(20, ct);
            Assert.True(tab.Listing.TryGetFocused(out var focused) && focused.Name == "kept.txt", $"at {tab.Location}, focused {(tab.Listing.TryGetFocused(out var other) ? other.Name : "nothing")}");
        }
        finally
        {
            // A recursive delete does not remove a junction here: it goes first, by itself.
            try { Directory.Delete(link); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            AccessibilityTests.Close(services, window, root);
        }
    }

    private static List<string> Names(ListingModel listing)
    {
        var names = new List<string>();
        for (int i = 0; i < listing.VisibleCount; i++)
            if (listing.GetVisible(i) is { Kind: not EntryKind.Parent } e) names.Add(e.Name);
        return names;
    }

    private static void Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Join(Environment.SystemDirectory, program))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), $"{program} timed out.");
        Assert.True(process.ExitCode == 0, $"{program} {string.Join(' ', arguments)} failed: {output}");
    }
}
