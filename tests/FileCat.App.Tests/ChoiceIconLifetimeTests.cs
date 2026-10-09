using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class ChoiceIconLifetimeTests
{
    public static TheoryData<bool, bool, int> Cases => new()
    {
        {false, false, 32}, {false, false, 96}, {true, false, 32}, {true, false, 96},
        {false, true, 32}, {false, true, 96}, {true, true, 32}, {true, true, 96},
    };

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void Filtered_choice_rows_release_their_icon_borrowers(bool nativeUpdates, bool customSearch, int visited)
    {
        var host = new Grid();
        var window = new Window { Content = host, Width = 900, Height = 650 };
        var dialogs = new OverlayDialogService(host, () => null);
        var native = new NativeIcons();
        var provider = new IconProvider { Native = nativeUpdates ? native : null };
        window.Show();
        try
        {
            var observation = Visit(dialogs, window, host, provider, native, customSearch, visited);
            Collect();
            int imagesAlive = observation.Images.Count(Alive);
            int bitmapsAlive = observation.Bitmaps.Count(Alive);
            var retired = MeasureBorrowers(observation.Images);
            Assert.True(dialogs.IsOpen);
            Assert.False(observation.Task.IsCompleted);
            Assert.Equal(nativeUpdates ? 1 : 0, native.Subscribers);
            var filter = Assert.Single(host.GetVisualDescendants().OfType<TextBox>());
            // The dialog remains usable after the retired cohort has been collected.
            filter.Text = "row-0000";
            Layout(window);
            var image = Assert.Single(host.GetVisualDescendants().OfType<Image>());
            Assert.NotNull(image.Source);
            var oldSource = image.Source;
            if (nativeUpdates)
            {
                native.Publish();
                Assert.NotSame(oldSource, image.Source);
            }
            filter.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = filter });
            Assert.True(observation.Task.IsCompletedSuccessfully);
            Assert.Equal(0, observation.Task.Result.Index);
            Assert.False(dialogs.IsOpen);
            Assert.Empty(host.Children);
            Assert.Equal(0, native.Subscribers);
            TestContext.Current.TestOutputHelper?.WriteLine("CHOICE_ICON_LIFETIME " + JsonSerializer.Serialize(new
            {
                nativeUpdates, customSearch, visited, observedImages = observation.Images.Count,
                observedBitmaps = observation.Bitmaps.Count, imagesAlive, bitmapsAlive,
                uniqueRetiredControls = retired.Controls, uniqueRetiredBitmaps = retired.Bitmaps, retiredLogicalPixelBytes = retired.PixelBytes,
                dialogUsableAfterCollection = true, chosenIndex = observation.Task.Result.Index, subscribersAfterClose = native.Subscribers,
                headlessWindowWithoutNativeDesktopInput = true,
            }));
            Assert.Equal(0, imagesAlive);
            Assert.Equal(0, bitmapsAlive);
        }
        finally
        {
            dialogs.CancelAll();
            Layout(window);
            window.Close();
        }
    }


    [AvaloniaTheory]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    [InlineData(false, "accept")]
    [InlineData(true, "accept")]
    [InlineData(false, "accelerator")]
    [InlineData(true, "accelerator")]
    [InlineData(false, "delete")]
    [InlineData(true, "delete")]
    [InlineData(false, "pin")]
    [InlineData(true, "pin")]
    public void Closed_choice_dialogs_release_their_controls_and_icon_borrowers(bool nativeUpdates, string route)
    {
        var host = new Grid();
        var window = new Window { Content = host, Width = 900, Height = 650 };
        var dialogs = new OverlayDialogService(host, () => null);
        var native = new NativeIcons();
        window.Show();
        try
        {
            var observation = CloseOwnedDialog(dialogs, window, host, native, nativeUpdates, route);
            Collect();
            int controlsAlive = observation.Controls.Count(Alive);
            int bitmapsAlive = observation.Bitmaps.Count(Alive);
            Assert.False(dialogs.IsOpen);
            Assert.Empty(host.Children);
            Assert.Equal(0, native.Subscribers);
            native.Publish(); // A retired dialog must not be called by the retained source.
            TestContext.Current.TestOutputHelper?.WriteLine("CHOICE_ICON_CLOSED " + JsonSerializer.Serialize(new
            {
                nativeUpdates, route, controls = observation.Controls.Count, bitmaps = observation.Bitmaps.Count,
                controlsAlive, bitmapsAlive, chosenIndex = observation.Result.Index,
                deleted = observation.Result.Deleted.ToArray(), pinned = observation.Result.PinToggled.ToArray(),
                subscribersAfterClose = native.Subscribers, headlessWindowWithoutNativeDesktopInput = true,
            }));
            Assert.Equal(0, controlsAlive);
            Assert.Equal(0, bitmapsAlive);
        }
        finally { dialogs.CancelAll(); Layout(window); window.Close(); }
    }

    private sealed record ClosedObservation(ChoiceResult Result, List<WeakReference<Control>> Controls, List<WeakReference<IImage>> Bitmaps);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ClosedObservation CloseOwnedDialog(OverlayDialogService dialogs, Window window, Grid host, NativeIcons native, bool nativeUpdates, string route)
    {
        var task = dialogs.ChooseAsync(new ChoiceOptions("Owned closed icon control", new[]
        {
            new ChoiceItem("row-0000") { Icon = NewBitmap },
            new ChoiceItem("row-0001") { Icon = NewBitmap },
        })
        {
            Icons = new IconProvider { Native = nativeUpdates ? native : null }, AllowDelete = true, AllowPin = true,
            Accelerators = new Dictionary<char, int> { ['Z'] = 1 },
        });
        Layout(window);
        var filter = Assert.Single(host.GetVisualDescendants().OfType<TextBox>());
        var images = host.GetVisualDescendants().OfType<Image>().ToArray();
        Assert.Equal(2, images.Length);
        Assert.All(images, image => Assert.NotNull(image.Source));
        var controls = host.GetVisualDescendants().OfType<Control>().Where(c => c is Image or TextBox or ListBox).Append(host.Children[0]).Select(c => new WeakReference<Control>(c)).ToList();
        var bitmaps = images.Select(i => new WeakReference<IImage>(i.Source!)).ToList();
        Assert.All(controls, reference => Assert.True(Alive(reference)));
        Assert.All(bitmaps, reference => Assert.True(Alive(reference)));
        Assert.Equal(nativeUpdates ? 1 : 0, native.Subscribers);
        void Press(Key key, KeyModifiers modifiers = KeyModifiers.None) => filter.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = filter });
        if (route == "cancel") dialogs.CancelAll();
        else if (route == "accelerator") filter.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "z", Source = filter });
        else
        {
            filter.Text = "row-0001";
            Layout(window);
            if (route == "delete") { Press(Key.Delete, KeyModifiers.Control); dialogs.CancelAll(); }
            else { if (route == "pin") Press(Key.Insert); Press(Key.Enter); }
        }
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(route is "cancel" or "delete" ? -1 : 1, task.Result.Index);
        Assert.Equal(route == "delete" ? new[] { 1 } : Array.Empty<int>(), task.Result.Deleted);
        Assert.Equal(route == "pin" ? new[] { 1 } : Array.Empty<int>(), task.Result.PinToggled);
        return new(task.Result, controls, bitmaps);
    }

    private sealed record Observation(Task<ChoiceResult> Task, List<WeakReference<Image>> Images, List<WeakReference<IImage>> Bitmaps);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Observation Visit(OverlayDialogService dialogs, Window window, Grid host, IconProvider provider, NativeIcons native, bool customSearch, int visited)
    {
        var items = Enumerable.Range(0, visited).Select(i => new ChoiceItem($"row-{i:D4}") { Icon = NewBitmap }).ToArray();
        var task = dialogs.ChooseAsync(new ChoiceOptions("Owned icon lifetime control", items)
        {
            Icons = provider,
            Search = customSearch ? q => q.Length == 0 ? Enumerable.Range(0, items.Length).ToArray() : Enumerable.Range(0, items.Length).Where(i => items[i].Title == q).ToArray() : null,
        });
        var filter = Assert.Single(host.GetVisualDescendants().OfType<TextBox>());
        var images = new List<WeakReference<Image>>();
        var bitmaps = new List<WeakReference<IImage>>();
        var held = new List<Image>();
        for (int i = 0; i < visited; i++)
        {
            filter.Text = $"row-{i:D4}";
            Layout(window);
            var image = Assert.Single(host.GetVisualDescendants().OfType<Image>());
            Assert.NotNull(image.Source);
            images.Add(new(image));
            bitmaps.Add(new(image.Source));
            held.Add(image);
            if (i % 4 == 0) native.Publish();
        }
        // Leave no realized item or selected container. This is an actual user filter operation.
        filter.Text = "no-matching-owned-row";
        Layout(window);
        Assert.Empty(host.GetVisualDescendants().OfType<Image>());
        Assert.Empty(Assert.Single(host.GetVisualDescendants().OfType<ListBox>()).Items);
        Assert.All(images, reference => Assert.True(Alive(reference), "The held cohort must be positively observed before collection."));
        Assert.Equal(visited, held.Distinct(ReferenceEqualityComparer.Instance).Count());
        GC.KeepAlive(held);
        return new(task, images, bitmaps);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (int Controls, int Bitmaps, long PixelBytes) MeasureBorrowers(List<WeakReference<Image>> references)
    {
        var controls = new HashSet<Image>(ReferenceEqualityComparer.Instance);
        var bitmaps = new HashSet<WriteableBitmap>(ReferenceEqualityComparer.Instance);
        foreach (var reference in references)
            if (reference.TryGetTarget(out var image))
            {
                controls.Add(image);
                if (image.Source is WriteableBitmap bitmap) bitmaps.Add(bitmap);
            }
        return (controls.Count, bitmaps.Count, bitmaps.Sum(bitmap => (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4));
    }

    private static IImage NewBitmap() => new WriteableBitmap(new PixelSize(64, 64), new Vector(96, 96));
    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Alive<T>(WeakReference<T> reference) where T : class => reference.TryGetTarget(out _);
    private static void Collect()
    {
        Dispatcher.UIThread.RunJobs();
        // Retired controls may still be in a submitted frame or weak-table finalization cycle.
        // Drain both before asking whether the still-open dialog roots them.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
        Dispatcher.UIThread.RunJobs();
        for (int i = 0; i < 4; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Dispatcher.UIThread.RunJobs();
        }
    }
    private sealed class NativeIcons : INativeIconSource
    {
        private Action? _loaded;
        public int Subscribers => _loaded?.GetInvocationList().Length ?? 0;
        public event Action? IconsLoaded { add => _loaded += value; remove => _loaded -= value; }
        public IImage? GetIcon(in EntryData entry, Location? folder = null) => null;
        public void Publish() => _loaded?.Invoke();
    }
}
