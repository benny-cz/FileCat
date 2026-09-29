using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.Core.Commands;
using FileCat.Core.HiddenData;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>D-55 in the window: a file's streams and attributes open as a list, are copied out, and deleted.</summary>
public sealed class HiddenDataUiTests
{
    private static async Task Until(Func<bool> condition, string what)
    {
        for (int i = 0; i < 400 && !condition(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition(), what);
    }

    private static async Task Click(Window window, string text)
    {
        await Until(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == text && b.IsEffectivelyEnabled), $"a {text} button");
        window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == text && b.IsEffectivelyEnabled).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [AvaloniaFact]
    public async Task A_files_streams_and_attributes_open_as_a_list_are_copied_out_and_deleted()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string folder = Path.Combine(root, "files");
            string file = Path.Combine(folder, "tool.bin");
            File.WriteAllText(file, "contents");
            string mark, payload;
            if (OperatingSystem.IsWindows())
            {
                // The test services run on the portable platform: Windows' streams come in as the app registers them.
                services.Providers.Register(new HiddenDataProvider(new FileCat.Platform.Windows.WindowsHiddenData()));
                File.WriteAllText(file + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
                File.WriteAllText(file + ":payload", "hidden payload");
                (mark, payload) = ("Zone.Identifier", "payload");
            }
            else
            {
                (mark, payload) = OperatingSystem.IsMacOS() ? ("com.apple.quarantine", "org.filecat.payload") : ("user.xdg.origin.url", "user.filecat.payload");
                byte[] markValue = Encoding.UTF8.GetBytes(OperatingSystem.IsMacOS() ? "0083;66f1c2a3;Safari;" : "https://example.com/tool.bin");
                if (SetAttribute(file, mark, markValue) != 0)
                {
                    Assert.Skip("This file system keeps no extended attributes here.");
                    return;
                }
                SetAttribute(file, payload, Encoding.UTF8.GetBytes("hidden payload"));
            }
            string outFolder = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            var panels = vm.Workspace.Panels;
            panels[1].ActiveTab!.Navigate(Location.FileSystem(outFolder));
            var tab = panels[0].ActiveTab!;
            tab.Refresh();
            await Until(() => tab.Listing.State == ListingState.Complete && Rows(tab).Any(e => e.Name == "tool.bin"), "the file is listed");
            vm.Workspace.Activate(panels[0]);
            tab.Listing.SetFocus(Rows(tab).FindIndex(e => e.Name == "tool.bin") + (tab.Listing.HasParentRow ? 1 : 0));

            // Alt+Shift+Enter: its streams and attributes, each with what it says.
            vm.Execute(CommandIds.HiddenData);
            await Until(() => tab.Location?.Scheme == Schemes.HiddenData && tab.Listing.State == ListingState.Complete && Rows(tab).Count >= 2, "the hidden data is listed");
            var markRow = Assert.Single(Rows(tab), e => e.Name == mark);
            Assert.Matches("Mark of the Web|Quarantined|Downloaded from", tab.GetDetailsText(markRow));
            var payloadRow = Assert.Single(Rows(tab), e => e.Name == payload);
            Assert.Equal("hidden payload", tab.GetDetailsText(payloadRow));

            // F5: copied out as a file by a job.
            tab.Listing.SetFocus(Rows(tab).FindIndex(e => e.Name == payload) + (tab.Listing.HasParentRow ? 1 : 0));
            vm.Execute(CommandIds.Copy);
            await Click(window, "Copy");
            await Until(() => services.Jobs.Jobs.Count > 0 && services.Jobs.Jobs[^1].State.IsFinished(), "the copy finishes");
            Assert.Equal(JobState.Completed, services.Jobs.Jobs[^1].State);
            Assert.Equal("hidden payload", File.ReadAllText(Path.Combine(outFolder, payload)));

            // F8: deleted after one confirmation; the file's contents untouched.
            await Until(() => !((FileCat.App.Views.OverlayDialogService)vm.Dialogs).IsOpen, "the copy dialog closed");
            tab.Listing.SetFocus(Rows(tab).FindIndex(e => e.Name == payload) + (tab.Listing.HasParentRow ? 1 : 0));
            vm.Execute(CommandIds.Delete);
            await Click(window, "Delete");
            await Until(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing && Rows(tab).All(e => e.Name != payload), "the payload is gone");
            Assert.Contains(Rows(tab), e => e.Name == mark);
            Assert.Equal("contents", File.ReadAllText(file));

            // Up: the folder, with the file under the cursor.
            tab.GoUp();
            await Until(() => tab.Location?.IsFileSystem == true && tab.Listing.State == ListingState.Complete, "back in the folder");
            Assert.True(tab.Listing.TryGetFocused(out var focused));
            Assert.Equal("tool.bin", focused.Name);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    private static List<EntryData> Rows(FileCat.App.ViewModels.TabViewModel tab) =>
        Enumerable.Range(0, tab.Listing.VisibleCount).Select(tab.Listing.GetVisible).Where(e => e.Kind != EntryKind.Parent).ToList();

    private static int SetAttribute(string path, string name, byte[] value) =>
        OperatingSystem.IsMacOS() ? MacSet(path, name, value, value.Length, 0, 1) : LinuxSet(path, name, value, value.Length, 0);

    [DllImport("libc", EntryPoint = "setxattr", SetLastError = true)]
    private static extern int MacSet(string path, string name, byte[] value, nint size, uint position, int options);

    [DllImport("libc", EntryPoint = "lsetxattr", SetLastError = true)]
    private static extern int LinuxSet(string path, string name, byte[] value, nint size, int flags);
}
