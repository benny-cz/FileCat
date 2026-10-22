using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

/// <summary>
/// Create folder (F7) nests folders with the platform's separators. Linux and macOS were told to use \, which there is
/// part of a name: following the hint created one folder named "a\b" instead of "b" inside "a".
/// </summary>
public sealed class CreateFolderPromptTests
{
    [AvaloniaFact]
    public async Task The_hint_names_the_platform_separator_and_both_kinds_of_name_are_created_as_typed()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var listing = vm.ActiveTab!.Listing;
            await FileListFixture.Wait(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
            string folder = vm.ActiveTab.Location!.Path;

            var box = await Prompt(window, vm);
            Assert.Equal($"Folder name (use {Path.DirectorySeparatorChar} for nested folders):", AutomationProperties.GetName(box));
            box.Text = "outer/inner";
            await Create(window, services, 1);
            Assert.True(Directory.Exists(Path.Combine(folder, "outer", "inner")));

            box = await Prompt(window, vm);
            box.Text = @"left\right";
            await Create(window, services, 2);
            if (OperatingSystem.IsWindows()) Assert.True(Directory.Exists(Path.Combine(folder, "left", "right")));
            else
            {
                Assert.True(Directory.Exists(Path.Combine(folder, @"left\right")));
                Assert.False(Directory.Exists(Path.Combine(folder, "left")));
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task<TextBox> Prompt(Window window, ViewModels.MainViewModel vm)
    {
        vm.Execute(CommandIds.MakeDirectory);
        await FileListFixture.Wait(() => window.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsFocused));
        return window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsFocused);
    }

    private static async Task Create(Window window, Services.AppServices services, int jobs)
    {
        var overlay = window.FindControl<Panel>("OverlayHost")!;
        await FileListFixture.Wait(() => overlay.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Create" && b.IsEffectivelyEnabled));
        overlay.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Create" && b.IsEffectivelyEnabled)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await FileListFixture.Wait(() => services.Jobs.Jobs.Count >= jobs && services.Jobs.Jobs[jobs - 1].State.IsFinished());
        Assert.Equal(JobState.Completed, services.Jobs.Jobs[jobs - 1].State);
    }
}
