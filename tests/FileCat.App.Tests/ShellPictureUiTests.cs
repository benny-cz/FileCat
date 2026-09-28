using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Platform.Windows.Shell;

namespace FileCat.App.Tests;

public sealed class ShellPictureUiTests
{
    private static void WriteBitmap(string path, int width, int height)
    {
        int stride = (width * 3 + 3) & ~3;
        var bytes = new byte[54 + stride * height];
        void U32(int at, int v) => BitConverter.GetBytes(v).CopyTo(bytes, at);
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        U32(2, bytes.Length);
        U32(10, 54);
        U32(14, 40);
        U32(18, width);
        U32(22, height);
        bytes[26] = 1;
        bytes[28] = 24;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                bytes[54 + y * stride + x * 3] = (byte)(x * 255 / width);
        File.WriteAllBytes(path, bytes);
    }

    [AvaloniaFact]
    public async Task Quick_view_shows_the_Shells_thumbnail_of_a_picture_through_the_restricted_helper()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Shell thumbnails exist only on Windows.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            if (services.ShellPictures is null) Assert.Skip("This build has no Shell helper beside the tests.");
            var picture = Path.Combine(root, "files", "c.bmp");
            WriteBitmap(picture, 300, 200);
            var left = vm.Workspace.Panels[0];
            var listing = left.ActiveTab!.Listing;
            left.ActiveTab.Refresh();
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 4); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            vm.Workspace.Activate(left);
            Assert.True(listing.FocusName("c.bmp"));
            vm.Execute(CommandIds.QuickView);

            // Every panel has a quick view pane; the target panel's is the one in use.
            Image? Thumbnail() => window.GetVisualDescendants().OfType<Image>()
                .Where(i => AutomationProperties.GetName(i) == "Thumbnail").OrderByDescending(i => i.IsVisible).FirstOrDefault();
            for (int i = 0; i < 1500 && Thumbnail() is not { IsVisible: true, Source: not null }; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            if (Thumbnail() is not { IsVisible: true })
            {
                // Distinguish "no handler on this machine" from a broken quick view.
                var direct = services.ShellPictures.Client.Get(ShellImageKind.Thumbnail, picture, 64, TimeSpan.FromSeconds(30));
                if (direct is null) Assert.Skip("This Windows installation has no thumbnail handler for .bmp files.");
                Assert.Fail("The helper made a thumbnail, but quick view did not show it.");
            }
            Assert.Equal(1, services.ShellPictures.Client.Starts);
            Assert.True(services.ShellPictures.Client.RunsAtLowIntegrity);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
