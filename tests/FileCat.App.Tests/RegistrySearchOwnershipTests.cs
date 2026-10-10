using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Platform.Windows;
using Microsoft.Win32;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class RegistrySearchOwnershipTests
{
    public static TheoryData<int, string> Cases
    {
        get
        {
            var cases = new TheoryData<int, string>();
            foreach (int count in new[] { 16, 128, 4096 })
            foreach (string outcome in new[] { "close", "go", "replace", "show", "publish" }) cases.Add(count, outcome);
            return cases;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task Only_explicitly_published_registry_searches_become_session_results(int count, string outcome)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The real owned HKCU Registry search requires Windows.");
        string owned = @"Software\FileCat-Tests\RegistrySearchOwnership-" + Guid.NewGuid().ToString("N");
        byte[] bytes = Enumerable.Range(0, 64).Select(i => (byte)(i * 37 + 11)).ToArray();
        using (var key = Registry.CurrentUser.CreateSubKey(owned))
            for (int i = 0; i < count; i++) key.SetValue($"Needle{i:D6}", bytes, RegistryValueKind.Binary);
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        Task<SearchDialogResult>? dialog = null;
        try
        {
            bool publish = outcome is "show" or "publish";
            // The headless composition intentionally uses PortablePlatform; install the real local adapter for this test only.
            services.Providers.Register(new WindowsRegistryProvider());
            var before = Registered(services.ResultSets);
            dialog = RegistrySearchDialog.ShowAsync(vm, new Location(Schemes.Registry, "HKCU\\" + owned));
            Dispatcher.UIThread.RunJobs();
            await Wait(() => dialog.IsCompleted || main.GetVisualDescendants().OfType<TextBox>().Any(b => b.PlaceholderText == "Name or stored data"));
            if (dialog.IsCompleted) await dialog;
            var term = main.GetVisualDescendants().OfType<TextBox>().Single(b => b.PlaceholderText == "Name or stored data");
            var list = main.GetVisualDescendants().OfType<ListBox>().Single(b => b.Height == 240);
            term.Text = "Needle";
            int searches = outcome is "replace" or "show" ? 3 : 1;
            var checkpoints = new List<object>();
            for (int i = 0; i < searches; i++)
            {
                Click(main, "Search");
                await Wait(() => Button(main, "Search").IsEnabled && !Button(main, "Stop").IsEnabled && list.ItemCount == Math.Min(300, count));
                var actual = Registered(services.ResultSets);
                checkpoints.Add(new { search = i + 1, registered = actual.Count, retainedMembers = actual.Sum(s => s.Value.Count) });
            }
            if (outcome == "go") list.SelectedIndex = 0;
            Click(main, outcome == "go" ? "Go to selected" : publish ? "Show in panel" : "Close");
            var result = await dialog.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            var after = Registered(services.ResultSets);
            bool sourceUnchanged;
            using (var key = Registry.CurrentUser.OpenSubKey(owned))
                sourceUnchanged = key is not null && key.ValueCount == count && key.GetValueNames().All(n =>
                    key.GetValueKind(n) == RegistryValueKind.Binary && key.GetValue(n) is byte[] value && value.SequenceEqual(bytes));
            bool publishedReadable = !publish || result.Set is { } set && set.Count == count && set.IsComplete &&
                ReferenceEquals(set, services.ResultSets.Get(ResultSetProvider.LocationOf(set))) &&
                set.Snapshot().All(row => row.Item.Parent.Path == "HKCU\\" + owned && row.Item.Name.StartsWith("Needle", StringComparison.Ordinal));
            TestContext.Current.TestOutputHelper?.WriteLine("REGISTRY_SEARCH_OWNERSHIP " + JsonSerializer.Serialize(new
            {
                owned, count, outcome, searches, checkpoints, registeredBefore = before.Count, registeredAfter = after.Count,
                retainedMembersAfter = after.Sum(s => s.Value.Count), sourceUnchanged, publishedReadable,
                realRegistrySearch = true, actualHeadlessOverlay = true, first300DisplayLimit = true,
                readOnlyReflectionObservesProviderRegistration = true, noHostUI = true,
                notProcessPeakOrNativeInputOrCandidateQualification = true,
            }));
            Assert.True(sourceUnchanged && publishedReadable);
            Assert.Equal(outcome == "go" ? SearchDialogOutcome.GoTo : publish ? SearchDialogOutcome.ShowInPanel : SearchDialogOutcome.Closed, result.Outcome);
            if (outcome == "go") Assert.StartsWith("Needle", Assert.IsType<ItemRef>(result.Item).Name);
            Assert.Equal(before.Count + (publish ? 1 : 0), after.Count);
            if (publish) Assert.Equal(count, after.Single(s => !before.ContainsKey(s.Key)).Value.Count);
        }
        finally
        {
            if (dialog is { IsCompleted: false })
            {
                ((OverlayDialogService)vm.Dialogs).CancelAll();
                await dialog.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            }
            AccessibilityTests.Close(services, main, root);
            Registry.CurrentUser.DeleteSubKeyTree(owned, throwOnMissingSubKey: false);
            using var removed = Registry.CurrentUser.OpenSubKey(owned);
            TestContext.Current.TestOutputHelper?.WriteLine("REGISTRY_SEARCH_CLEANUP " + JsonSerializer.Serialize(new { owned, removed = removed is null }));
            Assert.Null(removed);
        }
    }

    private static Dictionary<string, ResultSet> Registered(ResultSetProvider provider) =>
        ((ConcurrentDictionary<string, ResultSet>)typeof(ResultSetProvider).GetField("_sets", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(provider)!).ToDictionary();
    private static Button Button(Window main, string title) => main.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == title);
    private static void Click(Window main, string title) => Button(main, title).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static async Task Wait(Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(done(), "The real search finished and published the bounded dialog rows.");
    }
}
