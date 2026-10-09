using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;

namespace FileCat.App.Tests;

public sealed class AboutLogoOwnershipTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var theme in ThemePalette.All)
        foreach (string route in new[] { "close", "cancel", "copy", "data" }) yield return [theme.Name, route];
    }

    [AvaloniaFact]
    public void Actual_bitmap_pixel_access_distinguishes_live_and_disposed_owners()
    {
        using var input = AssetLoader.Open(new Uri("avares://FileCat/Assets/filecat.png"));
        using var bitmap = new Bitmap(input);
        var before = bitmap.PixelSize;
        Assert.True(before.Width > 0 && before.Height > 0);
        bitmap.Dispose();
        var error = Record.Exception(() => { _ = bitmap.PixelSize; });
        output.WriteLine("ABOUT_BITMAP_ORACLE " + JsonSerializer.Serialize(new { before.Width, before.Height, disposedErrorType = error?.GetType().FullName, noGCOracle = true }));
        Assert.NotNull(error);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task Finishing_about_retires_the_owned_logo_while_controls_remain_held(string theme, string route)
    {
        string previous = ThemeManager.RequestedName;
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var dialogs = (OverlayDialogService)vm.Dialogs;
        var held = new List<(StackPanel Body, Image Image, Bitmap Bitmap)>();
        var observations = new List<object>();
        int retainedSources = 0, readableOwners = 0;
        try
        {
            ThemeManager.Apply(theme);
            for (int iteration = 0; iteration < 3; iteration++)
            {
                var task = AboutDialog.ShowAsync(vm);
                Layout();
                Assert.True(dialogs.IsOpen); Assert.False(task.IsCompleted);
                var made = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Made by " + AboutDialog.Author);
                var body = Assert.IsType<StackPanel>(made.Parent!.Parent);
                var images = body.GetVisualDescendants().OfType<Image>().ToArray();
                Assert.Equal(theme == ThemePalette.DosCommander.Name ? 0 : 1, images.Length);
                Bitmap? bitmap = null; Image? image = null;
                int width = 0, height = 0;
                if (images.Length == 1)
                {
                    image = images[0]; bitmap = Assert.IsType<Bitmap>(image.Source);
                    width = bitmap.PixelSize.Width; height = bitmap.PixelSize.Height;
                    Assert.True(width > 0 && height > 0);
                    held.Add((body, image, bitmap));
                }
                if (route == "cancel") dialogs.CancelAll();
                else
                {
                    string button = route == "copy" ? "Copy details" : route == "data" ? "Show data folder" : "Close";
                    window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == button)
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                await task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Layout(); Assert.False(dialogs.IsOpen);
                Assert.Null(TopLevel.GetTopLevel(body));
                bool cleared = image?.Source is null;
                Exception? disposed = bitmap is null ? null : Record.Exception(() => { _ = bitmap.PixelSize; });
                if (!cleared) retainedSources++;
                if (bitmap is not null && disposed is null) readableOwners++;
                observations.Add(new { iteration, logoPresent = bitmap is not null, width, height, sourceCleared = cleared,
                    disposedOwnerError = disposed?.GetType().FullName, closedModalTask = task.IsCompletedSuccessfully, heldControlWithoutGC = true });
            }
            output.WriteLine("ABOUT_LOGO_OWNERSHIP " + JsonSerializer.Serialize(new { theme, route, observations, retainedSources, readableOwners,
                uniqueHeldImages = held.Select(v => v.Image).Distinct(ReferenceEqualityComparer.Instance).Count(),
                uniqueHeldBitmaps = held.Select(v => v.Bitmap).Distinct(ReferenceEqualityComparer.Instance).Count(),
                headlessNoNativeDesktopInput = true, ownedRoot = root }));
            Assert.Equal(0, retainedSources); Assert.Equal(0, readableOwners);
            GC.KeepAlive(held);
        }
        finally
        {
            dialogs.CancelAll();
            foreach (var v in held) { v.Image.Source = null; v.Bitmap.Dispose(); }
            AccessibilityTests.Close(services, window, root);
            ThemeManager.Apply(previous);
        }
        void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }
}
