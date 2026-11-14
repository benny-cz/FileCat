using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Tests;

internal static class ArchiveEditStorageAdmissionCases
{
    internal static IEnumerable<object[]> Scenarios()=>new[]{"empty","working","captured","retained-restart","large-logical",
        "extra-working","extra-session-file","dishonest-record","origin","native-extra-data","exclusive-working",
        "unknown-root","unknown-directory","malformed-capture","byte-review-limit","metadata-review-limit","entry-review-limit",
        "session-limit","capture-limit","new-session-bytes","new-capture-bytes","no-space","revision-admission",
        "cancel-before","cancel-wait","same-process","other-root","process-contention","process-death","process-quota-race",
        "gate-content","gate-replaced","wrong-root","invalid-policy","intent-wait","intent-cancel-wait",
        "intent-gate-failure","cleanup-admission","cleanup-gate-failure","intent-cleanup-race",
        "native-discard","native-discard-no-factory","native-discard-replaced","wrong-new-root-parent","gate-inspection-budget",
        "capture-gate","capture-gate-admits","capture-abandoned"}.Select(value=>new object[]{value});

    internal static async Task Run(string scenario,Func<string,bool,CancellationToken,EditDirectoryLease> open,IFileSystemOperations fs,string nativeAssembly)
    {
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string fixture=Private(Path.Combine(parent,"filecat-edit-admission-"+Guid.NewGuid().ToString("N")));
        var copies=new List<EditCommitCopy>();
        try
        {
            string path=Path.Combine(fixture,"sessions");
            bool refuseOpen=false;
            EditDirectoryLease Open(string directory,bool priv,CancellationToken token)
            {if(refuseOpen&&directory==path)throw new IOException("Injected fresh native root refusal.");return open(directory,priv,token);}
            var store=new EditSessionStore(path,fs,Open);
            if(scenario=="wrong-new-root-parent")
            {
                string other=Private(Path.Combine(fixture,"unrelated-parent"));
                var wrong=new EditSessionStore(path,fs,(_,priv,token)=>open(other,priv,token));
                Assert.ThrowsAny<IOException>(()=>Create(wrong,"refused.txt"));
                Assert.False(Directory.Exists(path));Assert.Empty(Directory.EnumerateFileSystemEntries(other));return;
            }
            using(var outer=open(fixture,false,ct))using(outer.CreateDirectory("sessions",ct)) { }
            if(scenario=="empty")
            {var empty=store.ReviewNativeStorageUsage(ct);Assert.Equal(0,empty.Sessions);Assert.Equal(0,empty.Files);Assert.Equal(1,empty.Directories);return;}
            if(scenario=="process-quota-race")
            {
                using var one=Child(path,nativeAssembly,"allocate");using var two=Child(path,nativeAssembly,"allocate");
                try
                {
                await Ready(one,ct);await Ready(two,ct);
                await one.StandardInput.WriteLineAsync("go");await two.StandardInput.WriteLineAsync("go");
                string? a=await one.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(35),ct);
                string? b=await two.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(35),ct);
                Assert.Equal(new[]{"ALLOWED","REFUSED"},new[]{a,b}.Order(StringComparer.Ordinal));
                await Exit(one,ct);await Exit(two,ct);Assert.Equal(1,store.ReviewNativeStorageUsage(ct).Sessions);return;
                }
                finally{await Retire(one);await Retire(two);}
            }
            var record=Create(store,"one.txt");
            if(scenario.StartsWith("intent-",StringComparison.Ordinal)||scenario.StartsWith("cleanup-",StringComparison.Ordinal))
            {await MutationCase(scenario,store,record,fixture,open,value=>refuseOpen=value,copies,ct);return;}
            long length=new FileInfo(record.WorkingPath).Length;
            if(scenario=="large-logical")using(var file=File.Open(record.WorkingPath,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete))file.SetLength(128L*1024*1024);
            if(scenario=="extra-working")File.WriteAllText(Path.Combine(Path.GetDirectoryName(record.WorkingPath)!,"editor.bak"),"ordinary editor backup");
            if(scenario=="extra-session-file")File.WriteAllText(Path.Combine(store.SessionDirectory(record),"extra.json"),"count this file too");
            if(scenario=="dishonest-record")File.WriteAllText(Path.Combine(store.SessionDirectory(record),"session.json"),"{\"Length\":0,\"Deleted\":true}");
            if(scenario=="origin")Assert.True(fs.WriteOriginMark(record.WorkingPath,"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/retained\r\nQuarantine=0081;6553f100;FileCat;public-test\r\n"));
            if(scenario=="native-extra-data")
            {
                if(OperatingSystem.IsWindows())File.WriteAllBytes(record.WorkingPath+":Extra.Retained.Data",new byte[4099]);
                else Assert.True(fs.WriteOriginMark(record.WorkingPath,"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/additional-native-metadata\r\nQuarantine=0081;6553f100;FileCat;public-test\r\n"));
            }
            if(scenario is "captured" or "retained-restart" or "capture-limit" or "new-capture-bytes")
            {
                copies.Add(store.PrepareCommit(record,fixture,ct));
                if(scenario=="capture-limit")copies.Add(store.PrepareCommit(record,fixture,ct));
                if(scenario=="retained-restart")
                {
                    string capture=Path.GetDirectoryName(Path.GetDirectoryName(copies[0].Path))!;
                    File.WriteAllText(Path.Combine(capture,"retained.txt"),"retain the owned fixture for restart review");
                    Assert.IsAssignableFrom<IOException>(Record.Exception(copies[0].Dispose));copies.Clear();
                    store=new EditSessionStore(path,fs,open);
                }
            }
            if(scenario is "capture-gate" or "capture-gate-admits")
            {
                // A commit's capture holds the storage gate to admit its copy and to make its objects, not while the
                // bytes are copied: another edit can be admitted then, and the capture is charged as its own record says
                // it was admitted - never measured while it grows - with the free space its copy has yet to write.
                bool measured=false,gateHeld=true;EditStorageUsage? seen=null;EditSessionRecord? second=null;
                using(var copy=store.PrepareCommit(record,fixture,ct,()=>
                {
                    // Only once this capture's copy is under way: its folder and its record are there and its receipt is
                    // not. The first checks of a capture come before it has taken the gate at all, which says nothing.
                    if(measured)return;
                    string? folder=Directory.EnumerateDirectories(store.SessionDirectory(record),"commit-*").FirstOrDefault();
                    if(folder is null||!File.Exists(Path.Combine(folder,"capture.json"))||File.Exists(Path.Combine(folder,"captured.json")))return;
                    try
                    {
                        using var gate=open(path,true,ct);
                        using(EditStorageAdmission.Acquire(gate,ct,TimeSpan.Zero)) { }
                        gateHeld=false;
                    }
                    catch(EditStorageBusyException){gateHeld=true;}
                    seen=store.ReviewNativeStorageUsage(ct);
                    if(scenario=="capture-gate-admits"&&!gateHeld)second=Create(store,"second.txt");
                    measured=true;
                }))
                {
                    Assert.True(measured,"the capture's copy was never seen under way");
                    Assert.False(gateHeld,"the capture held the storage gate while it copied");
                    Assert.NotNull(seen);
                    // Charged as admitted, apart from the bytes the store holds: the growing copy is not measured.
                    Assert.True(seen.RunningPayloadBytes>=length,$"running payload {seen.RunningPayloadBytes} < {length}");
                    Assert.True(seen.RunningEntries>=4,$"running entries {seen.RunningEntries}");
                    Assert.True(seen.RunningMetadataBytes>0);Assert.Equal(1,seen.Captures);
                    // What a complete capture reserves, reserved while its copy is still being made: a retained copy of
                    // this payload takes that room, and a second copy must not be admitted into it.
                    Assert.True(seen.RunningReservedBytes>=length+EditStorageAdmission.CaptureContingency,
                        $"running reserve {seen.RunningReservedBytes} < {length+EditStorageAdmission.CaptureContingency}");
                    if(scenario=="capture-gate-admits")
                    {
                        Assert.NotNull(second);Assert.NotEqual(record.Id,second.Id);
                        Assert.Equal(2,store.LoadAll().Count);
                    }
                    // Once the receipt is written the capture is an ordinary retained copy: measured, with its
                    // reservation, and charged as running no longer. Its bytes appear only now, which is what "not
                    // measured while it grows" means.
                    var after=store.ReviewNativeStorageUsage(ct);
                    Assert.Equal(0,after.RunningEntries);Assert.Equal(0,after.RunningPayloadBytes);
                    Assert.Equal(0,after.RunningMetadataBytes);Assert.Equal(0,after.PendingBytes);
                    Assert.True(after.PayloadBytes>seen.PayloadBytes,$"payload {after.PayloadBytes} after, {seen.PayloadBytes} during");
                    Assert.True(after.ReservedBytes>=length+EditStorageAdmission.CaptureContingency);
                    // The invariant admission rests on: a copy in flight is charged at least what it becomes. Charged for
                    // less, two copies were both admitted into room for one, and the next review of the store threw -
                    // which left the commit that had just finished unable to write its own new state down.
                    if(scenario=="capture-gate")
                        Assert.True(checked(seen.ChargedBytes+seen.RunningChargedBytes)>=after.ChargedBytes,
                            $"charged {seen.ChargedBytes}+{seen.RunningChargedBytes} while copying, {after.ChargedBytes} once complete");
                }
                // The copy is disposed with the capture: its folder goes, and nothing of it is charged.
                var ended=store.ReviewNativeStorageUsage(ct);
                Assert.Equal(0,ended.Captures);Assert.Equal(0,ended.RunningEntries);Assert.Equal(0,ended.PendingBytes);
                return;
            }
            if(scenario=="capture-abandoned")
            {
                // A commit copy abandoned where a crash would leave it: the capture's folder, its own record and a
                // partial copy, with no receipt and no process writing it.
                string? folder=null;
                var failed=Record.Exception(()=>store.PrepareCommit(record,fixture,ct,()=>
                {
                    string? made=Directory.EnumerateDirectories(store.SessionDirectory(record),"commit-*").FirstOrDefault();
                    if(made is null||!File.Exists(Path.Combine(made,"capture.json")))return;
                    folder=made;throw new IOException("the copy was abandoned where a crash would leave it");
                }));
                Assert.NotNull(failed);Assert.NotNull(folder);
                Assert.True(File.Exists(Path.Combine(folder,"capture.json")));
                Assert.False(File.Exists(Path.Combine(folder,"captured.json")));
                var left=store.ReviewNativeStorageUsage(ct);
                Assert.Equal(1,left.Captures);
                // Charged, so the review budget still judges it and the user is asked about it...
                Assert.True(left.RunningPayloadBytes>=length,$"running payload {left.RunningPayloadBytes} < {length}");
                Assert.True(left.RunningReservedBytes>=length,$"running reserve {left.RunningReservedBytes} < {length}");
                // ...but no free space is held for bytes no living process will write.
                Assert.Equal(0,left.PendingBytes);
                // Another edit is still admitted beside it, and it is offered for review and removable on its own.
                var second=Create(store,"beside.txt");Assert.NotEqual(record.Id,second.Id);
                var retained=store.ReviewRetainedCaptures(record,ct);
                var only=Assert.Single(retained);
                Assert.Equal(EditCaptureRecoveryState.Interrupted,only.State);
                var removed=store.RemoveRetainedCapture(record,only.Directory,ct);
                Assert.True(removed.Complete,removed.Issue);
                var cleared=store.ReviewNativeStorageUsage(ct);
                Assert.Equal(0,cleared.Captures);Assert.Equal(0,cleared.RunningPayloadBytes);
                Assert.Equal(0,cleared.RunningReservedBytes);Assert.Equal(0,cleared.PendingBytes);
                Assert.True(File.Exists(record.WorkingPath));
                return;
            }
            var before=Snapshot();
            if(scenario.StartsWith("native-discard",StringComparison.Ordinal))
            {
                if(scenario=="native-discard-replaced")
                {
                    string original=store.SessionDirectory(record);Directory.Move(original,original+".retained");
                    File.WriteAllText(Path.Combine(Private(original),"unrelated.txt"),"keep this replacement");before=Snapshot();
                }
                var discard=scenario=="native-discard-no-factory"?new EditSessionStore(path,fs):store;
                Assert.Throws<NotSupportedException>(()=>discard.Discard(record));Assert.Equal(before,Snapshot());return;
            }
            if(scenario is "working" or "large-logical" or "extra-working" or "extra-session-file" or "dishonest-record" or "origin" or "native-extra-data" or "captured" or "retained-restart")
            {
                var usage=store.ReviewNativeStorageUsage(ct);
                Assert.Equal(Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories).Sum(file=>new FileInfo(file).Length),usage.PayloadBytes);
                Assert.True(usage.MetadataBytes>0);Assert.Equal(1,usage.Sessions);
                if(scenario is "captured" or "retained-restart")Assert.True(usage.ReservedBytes>=length+EditStorageAdmission.CaptureContingency);
                if(scenario=="native-extra-data"&&OperatingSystem.IsWindows())Assert.True(usage.MetadataBytes>=4099);
                Assert.Equal(before,Snapshot());return;
            }
            if(scenario=="exclusive-working")
            {
                using var exclusive=File.Open(record.WorkingPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
                Assert.True(store.ReviewNativeStorageUsage(ct).PayloadBytes>=length);return;
            }
            if(scenario=="wrong-root")
            {
                using(var outer=open(fixture,false,ct))using(outer.CreateDirectory("other",ct)) { }
                var wrong=new EditSessionStore(path,fs,(_,priv,token)=>open(Path.Combine(fixture,"other"),priv,token));
                Assert.ThrowsAny<IOException>(()=>wrong.ReviewNativeStorageUsage(ct));return;
            }
            if(scenario=="invalid-policy")
            {Assert.Throws<ArgumentOutOfRangeException>(()=>store.ReviewNativeStorageUsage(ct,new(Entries:-1)));return;}
            if(scenario is "unknown-root" or "unknown-directory" or "malformed-capture")
            {
                string folder=scenario=="unknown-root"?Path.Combine(path,"foreign"):
                    Path.Combine(store.SessionDirectory(record),scenario=="malformed-capture"?"commit-not-a-guid":"unexpected-directory");
                Private(folder);before=Snapshot();
                var error=Record.Exception(()=>store.ReviewNativeStorageUsage(ct));
                Assert.True(error is IOException or UnauthorizedAccessException,error?.ToString());
                Assert.Equal(before,Snapshot());return;
            }
            var current=store.ReviewNativeStorageUsage(ct);
            if(scenario is "byte-review-limit" or "metadata-review-limit" or "entry-review-limit" or "capture-limit")
            {
                var policy=scenario switch
                {
                    "byte-review-limit"=>new EditStorageReviewLimits(Bytes:current.ChargedBytes-1),
                    "metadata-review-limit"=>new(MetadataBytes:current.MetadataBytes-1),
                    "entry-review-limit"=>new(Entries:current.Directories+current.Files-1),
                    _=>new(Captures:1)
                };
                Assert.ThrowsAny<IOException>(()=>store.ReviewNativeStorageUsage(ct,policy));Assert.Equal(before,Snapshot());return;
            }
            if(scenario is "session-limit" or "new-session-bytes" or "new-capture-bytes" or "no-space" or "revision-admission")
            {
                var policy=scenario=="session-limit"?new EditStorageReviewLimits(Sessions:1):
                    scenario=="new-session-bytes"?new(Bytes:current.ChargedBytes+length+12L*1024*1024-1):
                    scenario=="new-capture-bytes"?new(Bytes:current.ChargedBytes+2*length+16L*1024*1024-1):
                    scenario=="revision-admission"?new(Bytes:current.ChargedBytes):new();
                EditDirectoryLease Factory(string root,bool priv,CancellationToken token)
                {var lease=open(root,priv,token);return scenario=="no-space"&&root==path?new NoSpace(lease):lease;}
                var limited=new EditSessionStore(path,fs,Factory,null,policy);
                Assert.ThrowsAny<IOException>(()=>
                {
                    if(scenario=="new-capture-bytes")using(limited.PrepareCommit(record,fixture,ct)) { }
                    else if(scenario=="revision-admission")limited.Save(record with{LastCommitUtc=DateTime.UtcNow});
                    else Create(limited,"refused.txt");
                });
                Assert.Equal(before,Snapshot());return;
            }
            if(scenario=="cancel-before")
            {using var stop=new CancellationTokenSource();stop.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>store.PrepareCommit(record,fixture,stop.Token));Assert.Equal(before,Snapshot());return;}
            if(scenario=="gate-inspection-budget")
            {
                using var reader=open(path,true,ct);using var gate=reader.OpenStorageGate(ct);
                var expected=reader.Inspect(gate,ct,0);long allocated=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<128;i++)Assert.Equal(expected,reader.Inspect(gate,ct,0));
                Assert.InRange(GC.GetAllocatedBytesForCurrentThread()-allocated,0,2L*1024*1024);
                Assert.Equal(before,Snapshot());return;
            }
            if(scenario=="gate-content")
            {
                File.WriteAllText(Path.Combine(path,EditStorageAdmission.GateName),"preserve invalid gate contents");before=Snapshot();
                Assert.ThrowsAny<IOException>(()=>Create(store,"blocked.txt"));Assert.Equal(before,Snapshot());return;
            }
            if(scenario is "process-contention" or "process-death")
            {
                using var child=Child(path,nativeAssembly,"hold");
                try
                {
                    await Ready(child,ct);
                    using(var parentRoot=open(path,true,ct))using(var parentFile=parentRoot.OpenStorageGate(ct))
                        Assert.False(parentRoot.TryLockStorageGate(parentFile,ct));
                    var pending=Task.Run(()=>Create(store,"next.txt"),CancellationToken.None);
                    await Task.Delay(150,ct);Assert.False(pending.IsCompleted);
                    if(scenario=="process-death"){child.Kill(entireProcessTree:true);await child.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(15),ct);}
                    else{await child.StandardInput.WriteLineAsync("release");await Exit(child,ct);}
                    var added=await pending.WaitAsync(TimeSpan.FromSeconds(35),ct);Assert.True(File.Exists(added.WorkingPath));
                }
                finally{if(!child.HasExited){child.Kill(entireProcessTree:true);await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));}}
                Assert.Equal(2,store.ReviewNativeStorageUsage(ct).Sessions);return;
            }
            using var root=open(path,true,ct);using var held=EditStorageAdmission.Acquire(root,ct);
            if(scenario=="gate-replaced")
            {
                string gate=Path.Combine(path,EditStorageAdmission.GateName);File.Move(gate,gate+".retained");File.WriteAllBytes(gate,[]);
                Assert.ThrowsAny<IOException>(()=>held.Review(ct));return;
            }
            if(scenario=="other-root")
            {var other=new EditSessionStore(Path.Combine(fixture,"independent"),fs,open);Assert.NotNull(Create(other,"independent.txt"));return;}
            if(scenario=="cancel-wait")
            {
                using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
                var waiting=Task.Run(()=>store.PrepareCommit(record,fixture,stop.Token),CancellationToken.None);
                await Task.Delay(100,ct);stop.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await waiting.WaitAsync(TimeSpan.FromSeconds(10),ct));
                held.Review(ct);held.Dispose();
                Assert.Equal(before,Snapshot());return;
            }
            Assert.Equal("same-process",scenario);
            var next=Task.Run(()=>Create(store,"next.txt"),CancellationToken.None);await Task.Delay(100,ct);Assert.False(next.IsCompleted);
            held.Dispose();Assert.True(File.Exists((await next.WaitAsync(TimeSpan.FromSeconds(35),ct)).WorkingPath));

            string[] Snapshot()=>Directory.EnumerateFiles(fixture,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(file=>Path.GetRelativePath(fixture,file)+":"+new FileInfo(file).Length+":"+
                    (scenario=="large-logical"&&file==record.WorkingPath?"metadata-only-large-case":Convert.ToHexString(SHA256.HashData(ReadShared(file))))).ToArray();
            EditSessionRecord Create(EditSessionStore target,string name)
            {using var source=new MemoryContentSource(name,"complete owned edit contents"u8.ToArray());return target.CreateRemote("test","test","/"+name,source,source.GetRevision()!.Value,null,ct);}
        }
        finally
        {
            foreach(var copy in copies)Assert.True(Record.Exception(copy.Dispose) is null or IOException);
            if(!Path.GetFullPath(fixture).StartsWith(Path.TrimEndingDirectorySeparator(parent)+Path.DirectorySeparatorChar,StringComparison.Ordinal)||
                !Path.GetFileName(fixture).StartsWith("filecat-edit-admission-",StringComparison.Ordinal))throw new IOException("Unexpected storage fixture root.");
            TestCleanup.DeleteTree(fixture);
        }
    }
    private static async Task MutationCase(string scenario,EditSessionStore store,EditSessionRecord record,string fixture,
        Func<string,bool,CancellationToken,EditDirectoryLease> open,Action<bool> refuseOpen,List<EditCommitCopy> copies,CancellationToken ct)
    {
        var copy=store.PrepareCommit(record,fixture,ct);copies.Add(copy);var native=copy.Native!;
        string capture=Path.GetDirectoryName(Path.GetDirectoryName(copy.Path))!;
        string intentPath=Path.Combine(capture,EditSaveCopyIntent.FileName);
        var expected=native.Expected;var time=expected.MemberModified!.Value;Guid nonce=Guid.NewGuid();
        string prefix=".filecat-archive-"+nonce.ToString("N")+"-";
        var intent=new EditSaveCopyIntent(1,nonce,record.Id,record.StoredRecordSha256,native.CaptureIdentity,
            expected.Version.Identity,expected.Version.Length,expected.Version.Sha256,expected.MetadataDigest,expected.Origin.Digest,
            time.Seconds,time.Nanoseconds,fixture,"test-destination-identity","saved.txt",prefix+"data.stage",prefix+"ready.manifest",prefix+"outcome.manifest");
        byte[] original=ReadShared(copy.Path);
        if(scenario.EndsWith("gate-failure",StringComparison.Ordinal))
        {
            refuseOpen(true);
            try
            {
                if(scenario=="intent-gate-failure")
                {
                    Assert.ThrowsAny<IOException>(()=>native.AttachSavedCopy(intent,ct));
                    Assert.False(File.Exists(intentPath));Assert.Equal(expected,native.Review(ct));
                }
                else
                {
                    Assert.ThrowsAny<IOException>(copy.Dispose);Assert.Null(Record.Exception(copy.Dispose));
                    Assert.ThrowsAny<IOException>(()=>native.Review(ct));
                    // Every owner must retire even when admission refused cleanup, including all record handles.
                    foreach(string path in Directory.EnumerateFiles(capture,"*",SearchOption.AllDirectories))
                        using(new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) { }
                    var permits=new List<EditSnapshotAdmission>();
                    try{for(int i=0;i<EditSnapshotAdmission.MaximumCopies;i++)permits.Add(EditSnapshotAdmission.Acquire(ct));}
                    finally{foreach(var permit in permits)permit.Dispose();}
                }
                Assert.Equal(original,ReadShared(copy.Path));
            }
            finally{refuseOpen(false);}
            return;
        }
        using var root=open(store.Root,true,ct);using var held=EditStorageAdmission.Acquire(root,ct);
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=Task.Run(()=>
        {
            started.SetResult();
            return Record.Exception(()=>
            {if(scenario=="cleanup-admission")copy.Dispose();else native.AttachSavedCopy(intent,stop.Token);});
        },CancellationToken.None);
        Task<Exception?>? cleanup=null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10),ct);
            // A capture's own cleanup waits for the storage admission on every system (Unix removes captures too now).
            await Task.Delay(150,ct);Assert.False(pending.IsCompleted);Assert.False(File.Exists(intentPath));
            if(scenario=="intent-cancel-wait")
            {
                stop.Cancel();Assert.IsAssignableFrom<OperationCanceledException>(await pending.WaitAsync(TimeSpan.FromSeconds(10),ct));
                Assert.False(File.Exists(intentPath));Assert.Equal(expected,native.Review(ct));return;
            }
            if(scenario=="intent-cleanup-race")cleanup=Task.Run(()=>Record.Exception(copy.Dispose),CancellationToken.None);
            held.Review(ct);held.Dispose();
            var error=await pending.WaitAsync(TimeSpan.FromSeconds(10),ct);
            if(scenario=="cleanup-admission")
            {Assert.Null(error);Assert.False(File.Exists(copy.Path));}
            else if(cleanup is not null)
            {
                var cleanupError=await cleanup.WaitAsync(TimeSpan.FromSeconds(10),ct);
                Assert.True(error is null or IOException);Assert.True(cleanupError is null or IOException);
                Assert.Equal(error is null,File.Exists(intentPath));
                if(error is null)Assert.IsAssignableFrom<IOException>(cleanupError);
                if(File.Exists(copy.Path))Assert.Equal(original,ReadShared(copy.Path));
            }
            else
            {Assert.Null(error);Assert.Equal(intent,EditSaveCopyIntent.Decode(ReadShared(intentPath)));Assert.Equal(original,ReadShared(copy.Path));}
        }
        finally
        {
            stop.Cancel();held.Dispose();
            await pending.WaitAsync(TimeSpan.FromSeconds(35));
            if(cleanup is not null)await cleanup.WaitAsync(TimeSpan.FromSeconds(35));
        }
    }
    private static byte[] ReadShared(string path)
    {using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var bytes=new MemoryStream();file.CopyTo(bytes);return bytes.ToArray();}
    private static string Private(string path)=>OperatingSystem.IsWindows()?Directory.CreateDirectory(path).FullName:
        Directory.CreateDirectory(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute).FullName;
    private static Process Child(string root,string nativeAssembly,string mode)
    {
        var checkout=new DirectoryInfo(AppContext.BaseDirectory);while(checkout is not null&&!File.Exists(Path.Combine(checkout.FullName,"FileCat.slnx")))checkout=checkout.Parent;
        Assert.NotNull(checkout);string configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string harness=Path.Combine(checkout.FullName,"tests","FileCat.CoreCrashHarness","bin",configuration,"net10.0","FileCat.CoreCrashHarness.dll");
        string runtime=new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
        var start=new ProcessStartInfo(Path.Combine(runtime,OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"))
        {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in new[]{harness,"--edit-storage-gate",root,nativeAssembly,mode})start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }
    private static async Task Ready(Process child,CancellationToken ct)
    {
        string? line=await child.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(20),ct);
        Assert.True(line=="READY","Missing admission child checkpoint: "+line+(child.HasExited?"; "+await child.StandardError.ReadToEndAsync(ct):""));
    }
    private static async Task Retire(Process child)
    {if(!child.HasExited)child.Kill(entireProcessTree:true);await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));}
    private static async Task Exit(Process child,CancellationToken ct)
    {await child.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(35),ct);Assert.Equal(0,child.ExitCode);Assert.Equal("",await child.StandardError.ReadToEndAsync(ct));}
    private sealed class NoSpace(EditDirectoryLease owner):EditDirectoryLease
    {
        internal override string Path=>owner.Path;internal override string Identity=>owner.Identity;internal override SafeFileHandle Handle=>owner.Handle;
        internal override EditDirectoryLease CreateDirectory(string leaf,CancellationToken ct,bool removable=false)=>owner.CreateDirectory(leaf,ct,removable);
        internal override EditDirectoryLease OpenDirectory(string leaf,string? identity,CancellationToken ct)=>owner.OpenDirectory(leaf,identity,ct);
        internal override EditDirectoryLease OpenStorageDirectory(string leaf,CancellationToken ct)=>owner.OpenStorageDirectory(leaf,ct);
        internal override FileStream CreateFile(string leaf,CancellationToken ct)=>owner.CreateFile(leaf,ct);
        internal override FileStream OpenFile(string leaf,string? identity,CancellationToken ct)=>owner.OpenFile(leaf,identity,ct);
        internal override FileStream OpenWorkingFile(string leaf,CancellationToken ct)=>owner.OpenWorkingFile(leaf,ct);
        internal override FileStream OpenWorkingFileForReview(string leaf,CancellationToken ct)=>owner.OpenWorkingFileForReview(leaf,ct);
        internal override FileStream OpenSharedRecord(string leaf,CancellationToken ct)=>owner.OpenSharedRecord(leaf,ct);
        internal override void RequireRemovable(FileStream file,string leaf)=>owner.RequireRemovable(file,leaf);
        internal override ContentRevision ReviewWorkingFile(FileStream file,string leaf,CancellationToken ct)=>owner.ReviewWorkingFile(file,leaf,ct);
        internal override ArchiveOriginSnapshot ReadOrigin(FileStream file,string leaf,CancellationToken ct)=>owner.ReadOrigin(file,leaf,ct);
        internal override void CopyOrigin(FileStream file,string leaf,ArchiveOriginSnapshot origin,CancellationToken ct)=>owner.CopyOrigin(file,leaf,origin,ct);
        internal override void DeleteSnapshot(FileStream file,string leaf,ArchiveFileObservation expected,CancellationToken ct,bool working=false)=>owner.DeleteSnapshot(file,leaf,expected,ct,working);
        internal override void RemoveEmpty(CancellationToken ct)=>owner.RemoveEmpty(ct);
        internal override IReadOnlyList<string> Names(CancellationToken ct)=>owner.Names(ct);
        internal override IReadOnlyList<string> Names(CancellationToken ct,int maximum)=>owner.Names(ct,maximum);
        internal override EditStoredFileSize MeasureStoredFile(string leaf,CancellationToken ct)=>owner.MeasureStoredFile(leaf,ct);
        internal override EditStoredDirectorySize MeasureStoredDirectory(CancellationToken ct)=>owner.MeasureStoredDirectory(ct);
        internal override string Identify(SafeFileHandle file)=>owner.Identify(file);
        internal override string IdentifyWorking(SafeFileHandle file)=>owner.IdentifyWorking(file);
        internal override void PublishNewFile(FileStream file,string source,string target,EditFileVersion expected,CancellationToken ct)=>owner.PublishNewFile(file,source,target,expected,ct);
        internal override FileStream OpenStorageGate(CancellationToken ct)=>owner.OpenStorageGate(ct);
        internal override bool TryLockStorageGate(FileStream file,CancellationToken ct)=>owner.TryLockStorageGate(file,ct);
        internal override FileSystemCapacitySnapshot? StorageCapacity(CancellationToken ct)=>new("injected-no-space",0);
        protected override void ReviewCore(CancellationToken ct)=>owner.Review(ct);
        protected override void CloseCore()=>owner.Dispose();
    }
}
