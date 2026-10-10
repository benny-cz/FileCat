using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Views;
using FileCat.Core.Search;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class FindRefineScratchRetirementTests
{
    private sealed record Retired(WeakReference<ResultSet> Set, string Id, int Count);
    public static TheoryData<RefineMode, int, bool> Cases
    {
        get
        {
            var cases = new TheoryData<RefineMode, int, bool>();
            foreach (var mode in new[] { RefineMode.Intersect, RefineMode.Subtract, RefineMode.Append })
            foreach (int count in new[] { 1, 128, 4096 })
            foreach (bool supersede in new[] { false, true }) cases.Add(mode, count, supersede);
            return cases;
        }
    }
    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task Private_refinement_scratch_lives_only_while_its_consumers_need_it(RefineMode mode, int count, bool supersede)
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        FindWindow? find = null;
        var paths = Enumerable.Range(0, count).Select(i => Path.Join(root, "files", $"row{i:D6}.dat")).ToArray();
        var known = Enumerable.Range(0, 64).Select(i => (byte)(i * 47 + 23)).ToArray();
        try
        {
            foreach (var path in paths) File.WriteAllBytes(path, known);
            find = FindWindow.Open(vm, Path.Join(root, "files"), null);
            find.NamesBox.Text = "*.dat";
            find.StartSearch(RefineMode.Replace);
            await Wait(find, count);
            find.StartSearch(mode);
            await Wait(find, mode == RefineMode.Subtract ? 0 : count);
            var old = ObserveOnly(find);
            Assert.Equal(count, old.Count);
            if (supersede)
            {
                find.StartSearch(RefineMode.Replace);
                await Wait(find, count);
            }
            Collect();
            bool alive = Alive(old.Set);
            bool registered = services.ResultSets.Get(new Location(Schemes.ResultSet, string.Empty, session: old.Id)) is not null;
            bool currentReadable = services.ResultSets.Get(find.ResultsTab!.Location!)?.Count == (supersede || mode != RefineMode.Subtract ? count : 0);
            bool sourceUnchanged = paths.All(path => File.ReadAllBytes(path).SequenceEqual(known));
            TestContext.Current.TestOutputHelper?.WriteLine("FIND_REFINE_SCRATCH " + JsonSerializer.Serialize(new
            {
                root, mode = mode.ToString(), count, supersede, scratchCount = old.Count, alive, registered, currentReadable,
                sourceUnchanged, sourceFiles = paths.Length, knownBytesEach = known.Length,
                SHA256 = Convert.ToHexString(SHA256.HashData(known)), windowHeldAndUsable = find.IsVisible && find.IsIdle,
                existingSearchAndRefineEntryPoints = true, readOnlyReflectionObservesResultNoPrivateStateMutation = true,
                unpublishedScratchNeverShownInPanel = true, explicitManagedReachabilityNotProcessPeakOrDesktopInput = true,
            }));
            Assert.True(currentReadable && sourceUnchanged);
            if (supersede) Assert.False(registered);
            Assert.Equal(!supersede, alive);
            GC.KeepAlive(find); GC.KeepAlive(services);
        }
        finally
        {
            find?.Close();
            AccessibilityTests.Close(services, main, root);
        }
    }

    private static async Task Wait(FindWindow find, int count)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(30) && (!find.IsIdle || find.Found.Count != count))
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(find.IsIdle);
        Assert.Equal(count, find.Found.Count);
        Assert.Empty(find.Error);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Retired ObserveOnly(FindWindow find)
    {
        var set = (ResultSet)typeof(SearchSession).GetField("_results", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(find.Session)!;
        return new Retired(new WeakReference<ResultSet>(set), set.Id, set.Count);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Alive(WeakReference<ResultSet> reference) => reference.TryGetTarget(out _);
    private static void Collect()
    {
        Dispatcher.UIThread.RunJobs();
        for (int i = 0; i < 4; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Dispatcher.UIThread.RunJobs(); }
    }
}
