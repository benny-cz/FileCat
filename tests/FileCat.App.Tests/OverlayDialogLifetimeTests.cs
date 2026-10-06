using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;

namespace FileCat.App.Tests;

public sealed class OverlayDialogLifetimeTests
{
    [AvaloniaFact]
    public void A_cancelled_prompt_is_collectible_while_its_service_lives() => CheckCollection(escape: false);

    [AvaloniaFact]
    public void An_escape_closed_prompt_is_collectible_while_its_service_lives() => CheckCollection(escape: true);

    private static void CheckCollection(bool escape)
    {
        var host = new Grid();
        var dialogs = new OverlayDialogService(host, () => null);
        var views = OpenAndClose(dialogs, host, escape);
        Dispatcher.UIThread.RunJobs(); // Drain the old dialog's queued initial/restored focus callbacks.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.Empty(host.Children);
        Assert.False(dialogs.IsOpen);
        Assert.All(views, view => Assert.False(view.TryGetTarget(out _), "A closed prompt is still rooted by the dialog service."));
        GC.KeepAlive(dialogs);
        GC.KeepAlive(host);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Control>[] OpenAndClose(OverlayDialogService dialogs, Grid host, bool escape)
    {
        var task = dialogs.PromptAsync(new PromptOptions("Lifetime control", "Owned prompt"));
        var layer = Assert.Single(host.Children.OfType<Border>());
        var source = Assert.Single(layer.GetVisualDescendants().OfType<TextBox>());
        var views = new[] { new WeakReference<Control>(layer), new WeakReference<Control>(source) };
        if (escape)
            layer.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = source });
        else
            dialogs.CancelAll();
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Null(task.Result);
        return views;
    }

    [AvaloniaFact]
    public void Escape_from_an_inner_dialog_closes_only_that_dialog()
    {
        var host = new Grid();
        var window = new Window { Content = host, Width = 800, Height = 600 };
        window.Show();
        var dialogs = new OverlayDialogService(host, () => null);
        try
        {
            var outer = dialogs.PromptAsync(new PromptOptions("Outer", "Owned outer prompt"));
            var inner = dialogs.PromptAsync(new PromptOptions("Inner", "Owned inner prompt"));
            var layer = host.Children.OfType<Border>().Last();
            var source = Assert.Single(layer.GetVisualDescendants().OfType<TextBox>());
            layer.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = source });
            Assert.True(inner.IsCompletedSuccessfully);
            Assert.Null(inner.Result);
            Assert.False(outer.IsCompleted);
            Assert.True(dialogs.IsOpen);
            Assert.Single(host.Children);
            dialogs.CancelAll();
            Assert.True(outer.IsCompletedSuccessfully);
            Assert.Null(outer.Result);
        }
        finally
        {
            dialogs.CancelAll();
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
    }
}
