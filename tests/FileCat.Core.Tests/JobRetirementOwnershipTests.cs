using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class JobRetirementOwnershipTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> RetiredCases()
    {
        foreach (string route in new[] { "remove", "clear", "trim" })
        foreach (int count in new[] { 1, 128, 4096 }) yield return [route, count];
    }
    [Theory, MemberData(nameof(RetiredCases))]
    public void Removed_finished_jobs_do_not_own_the_manager_or_its_live_request(string route, int count) => ManagerCase(route, count, false);
    [Theory]
    [InlineData(1)] [InlineData(128)] [InlineData(4096)]
    public void An_intentionally_live_manager_keeps_its_current_request(int count) => ManagerCase("remove", count, true);
    [Theory, MemberData(nameof(RetiredCases))]
    public void Cancelled_waiters_release_removed_blockers_and_their_frozen_sources(string route, int count) => WaitingCase(route, count, false);
    [Theory]
    [InlineData(1)] [InlineData(128)] [InlineData(4096)]
    public void A_live_waiter_preserves_its_actual_blocker_and_reason(int count) => WaitingCase("live", count, true);

    private sealed record Rig(Job Held, JobManager? Manager, WeakReference ManagerWeak, WeakReference TargetWeak,
        WeakReference SourcesWeak, bool FrozenSourcesMatch, bool OriginalBlocked, int CurrentJobs);
    private static byte[] KnownBytes() => Enumerable.Range(0,32768).Select(i => (byte)(i*31+17)).ToArray();
    private static JobRequest Copy(string root, string path, int count) => new() {
        Kind=JobKind.Copy, Sources=Enumerable.Range(0,count).Select(_ => ItemRef.ForFileSystemPath(path, EntryKind.File)).ToArray(),
        Destination=Location.FileSystem(Path.Join(root,"destination")) };
    private static JobManager Manager(string root)
    {
        var registry=new ProviderRegistry();registry.Register(new LocalFileSystemProvider());
        return new JobManager(new PortableFileOperations(),registry,Path.Join(root,"journal")){MaxConcurrent=0};
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Rig Removed(string root,string path,string route,int count,bool live)
    {
        var manager=Manager(root); if(route=="trim")manager.KeepFinished=0;
        Job retired=manager.Submit(Copy(root,path,1));retired.Cancel();Assert.Equal(JobState.Canceled,retired.State);
        Job current=manager.Submit(Copy(root,path,count));
        bool sourcesMatch=current.Request.Sources.Count==count&&current.Request.Sources.All(v=>v.FileSystemPath==path);
        if(route=="remove")manager.Remove(retired);else if(route=="clear")manager.ClearFinished();
        Assert.DoesNotContain(retired,manager.Jobs);Assert.Same(current,Assert.Single(manager.Jobs));
        Assert.Equal(JobState.Queued,current.State);Assert.Null(current.StartedUtc);Assert.Null(retired.StartedUtc);
        return new Rig(retired,live?manager:null,new WeakReference(manager),new WeakReference(current),
            new WeakReference(current.Request.Sources),sourcesMatch,false,manager.Jobs.Count);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Rig Waiting(string root,string path,string route,int count,bool live)
    {
        var manager=Manager(root);
        Job blocker=manager.Submit(Copy(root,path,count));Job waiter=manager.Submit(Copy(root,path,1));
        Assert.Same(blocker,waiter.WaitingFor);Assert.False(string.IsNullOrEmpty(waiter.WaitReason));
        bool sourcesMatch=blocker.Request.Sources.Count==count&&blocker.Request.Sources.All(v=>v.FileSystemPath==path);
        if(!live)
        {
            waiter.Cancel();blocker.Cancel();Assert.Equal(JobState.Canceled,waiter.State);Assert.Equal(JobState.Canceled,blocker.State);
            if(route=="remove")manager.Remove(blocker);
            else if(route=="clear")manager.ClearFinished();
            else {manager.KeepFinished=0;manager.Submit(new JobRequest{Kind=JobKind.CreateDirectory,Sources=[],Destination=Location.FileSystem(root),NewName="queued-witness"});}
            Assert.DoesNotContain(blocker,manager.Jobs);
        }
        Assert.Null(blocker.StartedUtc);Assert.Null(waiter.StartedUtc);
        return new Rig(waiter,manager,new WeakReference(manager),new WeakReference(blocker),
            new WeakReference(blocker.Request.Sources),sourcesMatch,true,manager.Jobs.Count);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Collect(){for(int i=0;i<4;i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}}
    private void ManagerCase(string route,int count,bool live)
    {
        string root=Directory.CreateTempSubdirectory("filecat-job-manager-retirement-").FullName;
        string path=Path.Join(root,"known.bin");File.WriteAllBytes(path,KnownBytes());string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Rig? rig=null;
        try
        {
            rig=Removed(root,path,route,count,live);Collect();
            bool managerAlive=rig.ManagerWeak.IsAlive,targetAlive=rig.TargetWeak.IsAlive,sourcesAlive=rig.SourcesWeak.IsAlive;
            bool unchanged=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))==hash;
            output.WriteLine("JOB_RETIREMENT_OWNERSHIP "+JsonSerializer.Serialize(new{kind="manager",route,count,live,root,managerAlive,targetAlive,sourcesAlive,
                rig.FrozenSourcesMatch,rig.CurrentJobs,heldState=rig.Held.State.ToString(),unchanged,knownBytes=32768,allKnownBytes=File.ReadAllBytes(path).SequenceEqual(KnownBytes()),
                noExecutorStarted=rig.Held.StartedUtc is null,queuedCancellationOnly=true,managedReachabilityNotProcessPeak=true}));
            GC.KeepAlive(rig.Held);GC.KeepAlive(rig.Manager);
            Assert.True(unchanged);Assert.True(rig.FrozenSourcesMatch);Assert.Equal(live,managerAlive);Assert.Equal(live,targetAlive);Assert.Equal(live,sourcesAlive);
        }
        finally {if(rig?.Manager is{} manager)foreach(var job in manager.Jobs)job.Cancel();Directory.Delete(root,true);}
    }
    private void WaitingCase(string route,int count,bool live)
    {
        string root=Directory.CreateTempSubdirectory("filecat-job-waiter-retirement-").FullName;
        string path=Path.Join(root,"known.bin");File.WriteAllBytes(path,KnownBytes());string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Rig? rig=null;
        try
        {
            rig=Waiting(root,path,route,count,live);Collect();
            bool targetAlive=rig.TargetWeak.IsAlive,sourcesAlive=rig.SourcesWeak.IsAlive,hasBlocker=rig.Held.WaitingFor is not null,hasReason=rig.Held.WaitReason is not null;
            bool unchanged=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))==hash;
            output.WriteLine("JOB_RETIREMENT_OWNERSHIP "+JsonSerializer.Serialize(new{kind="waiting",route,count,live,root,targetAlive,sourcesAlive,hasBlocker,hasReason,
                rig.OriginalBlocked,rig.FrozenSourcesMatch,rig.CurrentJobs,heldState=rig.Held.State.ToString(),unchanged,knownBytes=32768,
                allKnownBytes=File.ReadAllBytes(path).SequenceEqual(KnownBytes()),noExecutorStarted=rig.Held.StartedUtc is null,
                queuedCancellationOnly=true,managedReachabilityNotProcessPeak=true}));
            GC.KeepAlive(rig.Held);GC.KeepAlive(rig.Manager);
            Assert.True(unchanged);Assert.True(rig.FrozenSourcesMatch);Assert.Equal(live,targetAlive);Assert.Equal(live,sourcesAlive);Assert.Equal(live,hasBlocker);Assert.Equal(live,hasReason);
        }
        finally {if(rig?.Manager is{} manager)foreach(var job in manager.Jobs)job.Cancel();Directory.Delete(root,true);}
    }
}
