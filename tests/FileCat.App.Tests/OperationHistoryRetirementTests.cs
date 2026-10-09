using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class OperationHistoryRetirementTests(ITestOutputHelper output)
{
    public static TheoryData<string, int> Cases => new()
    {
        { "trim-one", 1 }, { "trim-one", 128 }, { "trim-one", 4096 },
        { "trim-default", 1 }, { "trim-default", 128 }, { "trim-default", 4096 },
        { "clear", 1 }, { "clear", 128 }, { "clear", 4096 },
        { "remove", 1 }, { "remove", 128 }, { "remove", 4096 },
        { "before-added", 1 }, { "before-added", 128 }, { "before-added", 4096 },
        { "retained", 1 }, { "retained", 128 }, { "retained", 4096 },
    };

    private sealed record Rig(OperationCenterViewModel Held, WeakReference Row, WeakReference Job, WeakReference Sources,
        Job Witness, bool FrozenSourcesMatch, int BeforeRows, int ExpectedRows, bool DispatchDeferred);

    private static byte[] Known() => Enumerable.Range(0, 32768).Select(i => (byte)(23 + i * 47)).ToArray();

    [AvaloniaTheory, MemberData(nameof(Cases))]
    public void Operations_history_retires_only_jobs_removed_by_the_manager(string route, int count)
    {
        string root = Directory.CreateTempSubdirectory("fc-operation-history-retirement-").FullName;
        string path = Path.Join(root, "known.bin");
        File.WriteAllBytes(path, Known());
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Rig? rig = null;
        try
        {
            rig = Visit(root, path, route, count);
            Collect();
            bool rowAlive = rig.Row.IsAlive, jobAlive = rig.Job.IsAlive, sourcesAlive = rig.Sources.IsAlive;
            bool unchanged = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == hash;
            bool witnessRetained = rig.Held.Jobs.Any(v => ReferenceEquals(v.Job, rig.Witness)) &&
                rig.Held.Manager.Jobs.Contains(rig.Witness) && rig.Witness.State == JobState.Queued;
            output.WriteLine("OPERATION_HISTORY_RETIREMENT " + JsonSerializer.Serialize(new
            {
                root, route, count, rowAlive, jobAlive, sourcesAlive, rig.FrozenSourcesMatch,
                rig.BeforeRows, rig.ExpectedRows, currentRows = rig.Held.Jobs.Count,
                managerRows = rig.Held.Manager.Jobs.Count, selected = rig.Held.Selected is not null,
                rig.DispatchDeferred, witnessRetained, unchanged, knownBytes = 32768,
                allKnownBytes = File.ReadAllBytes(path).SequenceEqual(Known()), hash,
                queuedCancellationOnly = true, noVisualContextsCreated = true,
                publicManagerAndDispatcherAPIs = true, managedReachabilityNotProcessPeak = true,
            }));
            GC.KeepAlive(rig.Held);
            Assert.True(unchanged && rig.FrozenSourcesMatch && rig.DispatchDeferred && witnessRetained);
            Assert.Equal(rig.ExpectedRows, rig.Held.Jobs.Count);
            Assert.Equal(rig.ExpectedRows, rig.Held.Manager.Jobs.Count);
            Assert.Equal(route == "retained", rig.Held.Selected is not null);
            Assert.Equal(route == "retained", rowAlive);
            Assert.Equal(route == "retained", jobAlive);
            Assert.Equal(route == "retained", sourcesAlive);
        }
        finally
        {
            if (rig is not null)
            {
                rig.Witness.Cancel();
                rig.Held.RemoveFinished();
            }
            Dispatcher.UIThread.RunJobs();
            Directory.Delete(root, true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Rig Visit(string root, string path, string route, int count)
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var manager = new JobManager(new PortableFileOperations(), providers, Path.Join(root, "journal")) { MaxConcurrent = 0 };
        bool removedBeforeAdded = false;
        if (route == "before-added") manager.JobAdded += job =>
        {
            if (removedBeforeAdded) return;
            removedBeforeAdded = true;
            job.Cancel();
            manager.Remove(job);
        };
        var center = new OperationCenterViewModel(manager) { IsOpen = true };
        var victim = manager.Submit(Request(root, path, count));
        victim.Cancel();
        Assert.Equal(JobState.Canceled, victim.State);
        Assert.Null(victim.StartedUtc);
        Dispatcher.UIThread.RunJobs();
        if (route == "before-added")
        {
            var priorRow = center.Jobs.SingleOrDefault();
            int priorRows = center.Jobs.Count;
            var liveWitness = manager.Submit(Request(root, path, 1));
            bool addDeferred = center.Jobs.Count == priorRows;
            Dispatcher.UIThread.RunJobs();
            bool sourcesMatch = victim.Request.Sources.Count == count && victim.Request.Sources.All(v => v.FileSystemPath == path);
            return new Rig(center, new WeakReference(priorRow), new WeakReference(victim), new WeakReference(victim.Request.Sources),
                liveWitness, sourcesMatch, priorRows, 1, addDeferred);
        }
        var row = Assert.Single(center.Jobs);
        center.Selected = row;
        int otherFinished = route == "trim-default" ? 50 : 1;
        for (int i = 0; i < otherFinished; i++)
        {
            var next = manager.Submit(Request(root, path, 1));
            next.Cancel();
            Assert.Equal(JobState.Canceled, next.State);
            Assert.Null(next.StartedUtc);
            Dispatcher.UIThread.RunJobs();
        }
        int beforeRows = center.Jobs.Count;
        if (route == "trim-one") manager.KeepFinished = 1;
        var witness = manager.Submit(Request(root, path, 1));
        if (route == "clear") manager.ClearFinished();
        if (route == "remove") manager.Remove(victim);
        // All manager notifications are queued while the UI thread is occupied. No private map/list is touched.
        bool deferred = center.Jobs.Count == beforeRows && ReferenceEquals(center.Selected, row);
        Dispatcher.UIThread.RunJobs();
        int expected = route switch { "trim-default" => 51, "trim-one" or "remove" => 2, "clear" => 1, _ => 3 };
        bool match = victim.Request.Sources.Count == count && victim.Request.Sources.All(v => v.FileSystemPath == path);
        return new Rig(center, new WeakReference(row), new WeakReference(victim), new WeakReference(victim.Request.Sources),
            witness, match, beforeRows, expected, deferred);
    }

    private static JobRequest Request(string root, string path, int count) => new()
    {
        Kind = JobKind.Copy,
        Sources = Enumerable.Range(0, count).Select(_ => ItemRef.ForFileSystemPath(path, EntryKind.File)).ToArray(),
        Destination = Location.FileSystem(Path.Join(root, "destination")),
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Collect()
    {
        for (int i = 0; i < 4; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
