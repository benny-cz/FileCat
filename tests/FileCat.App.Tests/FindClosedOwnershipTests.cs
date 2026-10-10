using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Views;
using FileCat.Core.Search;

namespace FileCat.App.Tests;

public sealed class FindClosedOwnershipTests
{
    public static TheoryData<string, int, bool> Cases
    {
        get
        {
            var cases = new TheoryData<string, int, bool>();
            foreach (string kind in new[] { "scope", "intersect", "subtract", "append", "groups" })
            foreach (int count in new[] { 16, 128, 4096 })
            foreach (bool close in new[] { false, true }) cases.Add(kind, count, close);
            return cases;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task Private_find_data_retires_on_close_while_published_results_remain(string kind, int count, bool close)
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        FindWindow? find = null, scopeSource = null;
        var paths = Enumerable.Range(0, count).Select(i => Path.Join(root, "files", $"owned{i:D6}.dat")).ToArray();
        byte[] known = Enumerable.Range(0, 64).Select(i => (byte)(i * 37 + 11)).ToArray();
        try
        {
            foreach (string path in paths) File.WriteAllBytes(path, known);
            find = FindWindow.Open(vm, Path.Join(root, "files"), null);
            find.NamesBox.Text = "*.dat";
            find.StartSearch(RefineMode.Replace, kind == "groups" ? DuplicateCriteria.Size : null);
            await Wait(find, count, groups: kind == "groups");
            if (kind == "scope")
            {
                var input = services.ResultSets.Get(find.ResultsTab!.Location!)!;
                scopeSource = find;
                find = FindWindow.Open(vm, "", input);
            }
            else if (kind is "intersect" or "subtract" or "append")
            {
                var mode = kind == "intersect" ? RefineMode.Intersect : kind == "subtract" ? RefineMode.Subtract : RefineMode.Append;
                find.StartSearch(mode);
                await Wait(find, kind == "subtract" ? 0 : count);
            }
            var observed = Observe(find, kind);
            var publishedLocation = (scopeSource ?? find).ResultsTab!.Location!;
            int expectedPublishedCount = kind == "subtract" ? 0 : count;
            Assert.Equal(expectedPublishedCount, services.ResultSets.Get(publishedLocation)!.Count);
            Finish(find, close);
            Collect();
            bool[] alive = observed.Owners.Select(Alive).ToArray();
            bool publishedReadable = services.ResultSets.Get(publishedLocation)?.Count == expectedPublishedCount;
            bool sourceUnchanged = paths.All(p => File.ReadAllBytes(p).SequenceEqual(known));
            TestContext.Current.TestOutputHelper?.WriteLine("FIND_CLOSED_OWNERSHIP " + JsonSerializer.Serialize(new
            {
                kind, count, close, observed.Members, alive, publishedReadable, sourceUnchanged,
                expectedPublishedCount, heldFindWindow = true, realSearchAndGroupingEntryPoints = true,
                readOnlyReflectionNoPrivateStateMutation = true, noHostUIOrPhysicalSource = true,
                notProcessPeakOrNativeInputOrCandidate = true,
            }));
            Assert.True(publishedReadable && sourceUnchanged);
            Assert.Equal(count, observed.Members);
            Assert.All(alive, value => Assert.Equal(!close, value));
            GC.KeepAlive(find); GC.KeepAlive(services);
        }
        finally
        {
            find?.Close(); scopeSource?.Close();
            AccessibilityTests.Close(services, main, root);
        }
    }

    private sealed record Observed(WeakReference[] Owners, int Members);
    private static async Task Wait(FindWindow find, int count, bool groups = false)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(30) && (!Ready(find, count, groups)))
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(Ready(find, count, groups));
        Assert.Empty(find.Error);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Ready(FindWindow find, int count, bool groups) => find.IsIdle && find.Found.Count == count && (!groups || find.Groups.Sum(g => g.Count) == count);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Observed Observe(FindWindow find, string kind)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        object Field(string name) => typeof(FindWindow).GetField(name, fields)!.GetValue(find)!;
        if (kind == "scope")
        {
            var scope = (System.Collections.ICollection)Field("_within");
            return new([new WeakReference(scope)], scope.Count);
        }
        if (kind == "groups") return new([new WeakReference(find.Groups)], find.Groups.Sum(g => g.Count));
        var scratch = (ResultSet)Field("_refineScratch");
        return new([new WeakReference(scratch), new WeakReference(find.Session!)], scratch.Count);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Alive(WeakReference reference) => reference.IsAlive;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Finish(FindWindow find, bool close) { if (close) find.Close(); }
    private static void Collect()
    {
        for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    }
}
