using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.HiddenData;
using FileCat.Core.Platform;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class FileDetailsFindScopeTests
{
    private sealed class Data : IHiddenData
    {
        public bool IsSupported => true;
        public IReadOnlyList<HiddenItem> List(string path) => [new("fixture", HiddenKind.Attribute, 0)];
        public byte[] Read(string path, HiddenItem item, int max) => [];
        public Stream Open(string path, HiddenItem item) => new MemoryStream();
        public void Delete(string path, HiddenItem item) => throw new NotSupportedException();
    }
    private sealed class TestPlatform : PortablePlatform
    {
        public TestPlatform() => HiddenData = new Data();
    }

    [AvaloniaFact]
    public async Task Find_supplementary_command_opens_captured_source_in_new_workspace_tab_without_replacing_results()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("This fixture injects the Windows platform factory; Unix scope qualification is separate."); return; }
        var previous = PlatformFactory.WindowsFactory;
        PlatformFactory.WindowsFactory = () => new TestPlatform();
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        PlatformFactory.WindowsFactory = previous;
        try
        {
            var ct = TestContext.Current.CancellationToken;
            async Task Until(Func<bool> condition)
            {
                for (int i = 0; i < 300 && !condition(); i++) await Task.Delay(10, ct);
                Assert.True(condition());
            }
            await Until(() => vm.ActiveTab?.Listing.State == Core.Listing.ListingState.Complete);
            vm.Execute(CommandIds.FindFiles);
            await Until(() => FindWindow.OpenWindows.Count == 1);
            var find = FindWindow.OpenWindows.Single();
            find.NamesBox.Text = "a.txt";
            find.NamesBox.Focus();
            find.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Until(() => find.IsIdle && find.ResultsTab?.Listing.VisibleCount == 1);
            var results = find.ResultsTab!;
            results.Listing.FocusName("a.txt");
            var originalLocation = results.Location;
            string source = results.Listing.GetItemRef(results.Listing.FocusedStoreIndex).FileSystemPath!;
            int tabs = vm.Workspace.Panels.Sum(p => p.Tabs.Count);
            vm.ActiveTab!.Navigate(Location.FileSystem(root));
            find.Run(CommandIds.HiddenData);
            await Until(() => vm.Workspace.Panels.Sum(p => p.Tabs.Count) == tabs + 1);
            Assert.Equal(originalLocation, results.Location);
            Assert.Equal(1, results.Listing.VisibleCount);
            Assert.Equal("a.txt", results.Listing.GetVisible(0).Name);
            Assert.Equal(Schemes.HiddenData, vm.Workspace.ActiveTab!.Location!.Scheme);
            Assert.Equal(source, vm.Workspace.ActiveTab.Location.Container!.Path);
            find.Close();
            Assert.Equal(Schemes.HiddenData, vm.Workspace.ActiveTab.Location.Scheme);
        }
        finally
        {
            PlatformFactory.WindowsFactory = previous;
            foreach (var find in FindWindow.OpenWindows.ToArray()) find.Close();
            AccessibilityTests.Close(services, main, root);
        }
    }
}
