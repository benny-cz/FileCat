using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Tests;

internal static class ArchiveEditStorageCases
{
    internal static void Run(string scenario,Func<string,bool,CancellationToken,EditDirectoryLease> open)
    {
        var ct=TestContext.Current.CancellationToken;
        string baseRoot=Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath();
        if(OperatingSystem.IsMacOS()&&baseRoot.StartsWith("/var/",StringComparison.Ordinal)&&new DirectoryInfo("/var").ResolveLinkTarget(true)?.FullName=="/private/var")baseRoot="/private"+baseRoot;
        baseRoot=Path.GetFullPath(baseRoot);Directory.CreateDirectory(baseRoot);
        ArchiveEditCreationCases.RequireListedRoute(baseRoot);
        string fixture=Directory.CreateDirectory(Path.Combine(baseRoot,"filecat-edit-store-"+Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var store=new EditSessionStore(Path.Combine(fixture,"sessions"),new PortableFileOperations(),open);
            using var source=new MemoryContentSource("owned.txt","original complete working copy"u8.ToArray());
            if(ArchiveEditCreationCases.Handles(scenario)){ArchiveEditCreationCases.Run(scenario,store,open,fixture,source);return;}
            if(scenario is "interrupted-marker" or "failed-copy-marker-directory")
            {
                // A creation interrupted by a crash after its copy: its marker is there, its record never was. (A live
                // process removes what it made; only an ended one leaves this.)
                var made=store.CreateRemote("owned","owned","/owned.txt",source,source.GetRevision()!.Value,null,ct);
                // It ended while publishing the record: the record's unpublished stage is there, the record is not.
                string folder=store.SessionDirectory(made);
                File.Move(Path.Combine(folder,"session.json"),
                    Path.Combine(folder,EditSessionStore.CreationStagePrefix+Guid.NewGuid().ToString("N")+".tmp"));
                var recovery=Assert.Single(store.LoadAll());Assert.False(recovery.IsSupported);
                Assert.Equal(EditSessionStore.InterruptedCreationReason,recovery.UnsupportedReason);Assert.Equal(made.WorkingPath,recovery.WorkingPath);
                Assert.Equal(folder,store.SessionDirectory(recovery));Assert.Equal("original complete working copy",File.ReadAllText(made.WorkingPath));
                string marker=Path.Combine(folder,"creation.json");Assert.True(File.Exists(marker));
                if(scenario=="failed-copy-marker-directory")
                {
                    File.Delete(marker);Directory.CreateDirectory(marker);
                    var unreadable=Assert.Single(store.LoadAll());Assert.False(unreadable.IsSupported);
                    Assert.Contains("cannot be read",unreadable.UnsupportedReason);Assert.True(Directory.Exists(marker));
                    // Only a reviewed interrupted copy is removable; an unreadable one is not offered and refuses.
                    Assert.Throws<NotSupportedException>(()=>store.RemoveInterruptedCopy(unreadable,ct));
                    Assert.True(Directory.Exists(marker));
                    return;
                }
                // Its review action removes exactly the unfinished copy: the folder is gone and the store keeps its gate.
                var removed=store.RemoveInterruptedCopy(recovery,ct);
                Assert.True(removed.Complete,removed.Issue);Assert.Empty(store.LoadAll());Assert.False(Directory.Exists(folder));
                Assert.Equal([".edit-storage.lock"],Directory.GetFileSystemEntries(store.NativeRoot).Select(entry=>Path.GetFileName(entry)));
                return;
            }
            var first=store.CreateRemote("owned","owned","/owned.txt",source,source.GetRevision()!.Value,null,ct);
            Assert.Equal(first,Assert.Single(store.LoadAll()));Assert.Equal(EditSessionRecord.NativeStorageVersion,first.Version);
            Assert.NotEmpty(first.StorageRootIdentity);Assert.NotEmpty(first.SessionDirectoryIdentity);
            Assert.NotEmpty(first.WorkingDirectoryIdentity);Assert.NotEmpty(first.WorkingIdentity);Assert.Equal(0,first.StorageRevision);
            string directory=store.SessionDirectory(first);string anchor=Path.Combine(directory,"session.json");byte[] original=File.ReadAllBytes(anchor);
            Assert.Equal(EditState.Unchanged,store.StateOf(first));
            if(scenario=="record-unreadable")
            {
                // An edit whose record is there but whose working folder is gone (a discard stopped part way) cannot be read:
                // it is listed as such on every platform, never as a copy still being made or an interrupted copy.
                File.Delete(first.WorkingPath);Directory.Delete(Path.GetDirectoryName(first.WorkingPath)!);
                var row=Assert.Single(store.LoadAll());Assert.False(row.IsSupported);
                Assert.Equal("This edit record cannot be read completely. Its working folder is kept.",row.UnsupportedReason);
                return;
            }
            if(scenario=="discard-leftover")
            {
                // A folder that lost its record but still holds the edit's history (a discard stopped part way) is not an
                // interrupted copy: it is kept for review, and the unfinished-copy removal refuses it.
                var saved=store.Save(first with{LastCommitUtc=DateTime.UnixEpoch.AddSeconds(1700000001)});
                File.Delete(anchor);
                var row=Assert.Single(store.LoadAll());Assert.False(row.IsSupported);
                Assert.Equal(EditSessionStore.IncompleteEditReason,row.UnsupportedReason);
                Assert.Throws<NotSupportedException>(()=>store.RemoveInterruptedCopy(row,ct));
                Assert.True(File.Exists(Path.Combine(directory,"session-state-0000000000000000001.json")));
                Assert.Equal(1,saved.StorageRevision);return;
            }
            if(scenario=="vanished-during-review")
            {
                // A working copy whose name goes once a review has opened it (an editor's save that deletes, then creates) is
                // unavailable for now, not missing; a review that finds no working copy at all says it is missing.
                store.NativeWorkingReads=openWorking=>{var held=openWorking();File.Delete(first.WorkingPath);return held;};
                Assert.Equal(EditState.Unavailable,store.ReviewWorking(first,new LocalFileSystemProvider()).State);
                store.NativeWorkingReads=null;
                Assert.Equal(EditState.Missing,store.ReviewWorking(first,new LocalFileSystemProvider()).State);
                return;
            }
            if(scenario=="earlier-marker")
            {
                // An edit made by an earlier native build has a marker naming only its id, version and working path: it is
                // still discarded, and one that build left interrupted is still removed through its review.
                void Earlier(EditSessionRecord made)=>File.WriteAllBytes(Path.Combine(store.SessionDirectory(made),"creation.json"),
                    JsonSerializer.SerializeToUtf8Bytes(new{made.Id,Version=int.MaxValue,made.WorkingPath}));
                Earlier(first);
                var discarded=store.DiscardReviewed(first,store.ReviewWorking(first,new LocalFileSystemProvider()),ct);
                Assert.True(discarded.Complete,discarded.Issue);Assert.Empty(store.LoadAll());Assert.False(Directory.Exists(directory));
                // A folder whose record is gone and whose marker is an earlier build's does not say how long its copy was
                // admitted to be, so FileCat cannot tell an unfinished copy from an edit of the user's: it is kept whole.
                var older=store.CreateRemote("owned","owned","/owned.txt",source,source.GetRevision()!.Value,null,ct);
                string folder=store.SessionDirectory(older);Earlier(older);File.Delete(Path.Combine(folder,"session.json"));
                var kept=Assert.Single(store.LoadAll());
                Assert.Equal(EditSessionStore.IncompleteEditReason,kept.UnsupportedReason);Assert.Equal(older.WorkingPath,kept.WorkingPath);
                Assert.Throws<NotSupportedException>(()=>store.RemoveInterruptedCopy(kept,ct));
                // Even a row that claims it was interrupted removes nothing of it.
                var refused=store.RemoveInterruptedCopy(kept with{UnsupportedReason=EditSessionStore.InterruptedCreationReason},ct);
                Assert.False(refused.Complete);Assert.False(refused.MayHaveRemovedFiles);
                Assert.Contains("does not say how long",refused.Issue,StringComparison.Ordinal);
                Assert.Equal("original complete working copy",File.ReadAllText(older.WorkingPath));
                Assert.True(File.Exists(Path.Combine(folder,"creation.json")));
                return;
            }
            if(scenario=="working-missing")
            {
                // A deleted working copy is missing, not unreadable, on every platform: its edit can then be discarded.
                File.Delete(first.WorkingPath);
                var review=store.ReviewWorking(first,new LocalFileSystemProvider());Assert.Equal(EditState.Missing,review.State);
                var discarded=store.DiscardReviewed(first,review,ct);Assert.True(discarded.Complete,discarded.Issue);
                Assert.Empty(store.LoadAll());return;
            }
            if(scenario.StartsWith("working-mode-",StringComparison.Ordinal))
            {
                File.WriteAllText(first.WorkingPath,"current private editor bytes");
                File.SetUnixFileMode(first.WorkingPath,(UnixFileMode)Convert.ToInt32(scenario["working-mode-".Length..],8));
                Assert.Equal(EditState.Modified,store.StateOf(first));return;
            }
            if(scenario=="working-directory-public")
            {
                string files=Path.GetDirectoryName(first.WorkingPath)!;
                try{File.SetUnixFileMode(files,(UnixFileMode)0x1ed);Assert.Equal(EditState.Unavailable,store.StateOf(first));}
                finally{File.SetUnixFileMode(files,(UnixFileMode)0x1c0);}
                Assert.Equal("original complete working copy",File.ReadAllText(first.WorkingPath));return;
            }
            if(scenario=="working-symlink")
            {
                string outside=Path.Combine(fixture,"outside.txt");File.WriteAllText(outside,"unrelated outside bytes");File.Delete(first.WorkingPath);
                try{File.CreateSymbolicLink(first.WorkingPath,outside);}
                catch(UnauthorizedAccessException)when(OperatingSystem.IsWindows()){Assert.Skip("The standard account cannot create a symbolic link.");}
                Assert.Equal(EditState.Unavailable,store.StateOf(first));Assert.Equal("unrelated outside bytes",File.ReadAllText(outside));return;
            }
            if(scenario=="working-read")
            {
                File.WriteAllText(first.WorkingPath,"reviewed current bytes");var review=store.ReviewWorking(first,new LocalFileSystemProvider());
                Assert.Equal(EditState.Modified,review.State);
                Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("reviewed current bytes"u8)),review.Sha256);return;
            }
            if(scenario=="working-directory-replaced")
            {
                string files=Path.GetDirectoryName(first.WorkingPath)!;Directory.Move(files,files+".retained");Directory.CreateDirectory(files);
                var error=Record.Exception(()=>store.Save(first with{LastCommitUtc=DateTime.UtcNow}));Assert.True(error is IOException or UnauthorizedAccessException,error?.ToString());
                Assert.False(Assert.Single(store.LoadAll()).IsSupported);Assert.Equal(EditState.Unavailable,store.StateOf(first));
                Assert.Equal(original,File.ReadAllBytes(anchor));return;
            }
            if(scenario=="future-state"||scenario=="collision")
            {
                string name="session-state-0000000000000000001.json";
                using(var root=open(store.Root,false,ct))
                using(var session=root.OpenDirectory(first.Id,first.SessionDirectoryIdentity,ct))
                using(var output=session.CreateFile(name,ct))
                    output.Write(JsonSerializer.SerializeToUtf8Bytes(new{Id=first.Id,Version=999,WorkingPath=first.WorkingPath}));
                Assert.False(Assert.Single(store.LoadAll()).IsSupported);
                Assert.Throws<IOException>(()=>store.Save(first with{LastCommitUtc=DateTime.UtcNow}));
                Assert.Equal(original,File.ReadAllBytes(anchor));Assert.Equal("original complete working copy",File.ReadAllText(first.WorkingPath));return;
            }
            if(scenario=="atomic-editor-save")
            {
                string replacement=first.WorkingPath+".editor-new";File.WriteAllText(replacement,"later atomic editor save");File.Move(replacement,first.WorkingPath,true);
                Assert.Equal(EditState.Modified,store.StateOf(first));
            }
            if(scenario=="concurrent-save")
            {
                var attempts=Enumerable.Range(0,2).Select(index=>Task.Run(()=>
                {
                    try{return(Record:store.Save(first with{LastCommitUtc=DateTime.UnixEpoch.AddSeconds(1700000010+index)}),Error:(Exception?)null);}
                    catch(Exception error){return(Record:(EditSessionRecord?)null,Error:error);}
                },ct)).ToArray();
                Task.WaitAll(attempts,ct);var results=attempts.Select(task=>task.Result).ToArray();
                var winner=Assert.Single(results,result=>result.Record is not null).Record!;
                Assert.All(results.Where(result=>result.Error is not null),result=>Assert.IsAssignableFrom<IOException>(result.Error));
                Assert.Equal(winner,Assert.Single(store.LoadAll()));Assert.Equal(original,File.ReadAllBytes(anchor));
                Assert.Single(Directory.GetFiles(directory,"session-state-*.json"));return;
            }
            var second=store.Save(first with{LastCommitUtc=DateTime.UnixEpoch.AddSeconds(1700000001)});
            Assert.Equal(1,second.StorageRevision);Assert.Equal(first.StoredRecordSha256,second.PreviousRecordSha256);
            Assert.Equal(original,File.ReadAllBytes(anchor));Assert.Equal(second,Assert.Single(store.LoadAll()));
            if(scenario is "damaged-ancestor" or "missing-ancestor" or "revision-budget")
            {
                var third=store.Save(second with{LastCommitUtc=DateTime.UnixEpoch.AddSeconds(1700000002)});
                var fourth=store.Save(third with{LastCommitUtc=DateTime.UnixEpoch.AddSeconds(1700000003)});
                string ancestor=Path.Combine(directory,"session-state-0000000000000000001.json");
                if(scenario=="damaged-ancestor")File.WriteAllText(ancestor,"{}");
                else if(scenario=="missing-ancestor")File.Delete(ancestor);
                else
                {
                    using var root=open(store.Root,false,ct);using var session=root.OpenDirectory(first.Id,first.SessionDirectoryIdentity,ct);
                    using var output=session.CreateFile("session-state-9223372036854775807.json",ct);output.Write("{}"u8);
                }
                var unreadable=Assert.Single(store.LoadAll());Assert.False(unreadable.IsSupported);
                // Its record is there: the folder is an edit that cannot be read, never a creation's.
                Assert.NotEqual(EditSessionStore.CopyingElsewhereReason,unreadable.UnsupportedReason);
                Assert.NotEqual(EditSessionStore.InterruptedCreationReason,unreadable.UnsupportedReason);
                Assert.ThrowsAny<IOException>(()=>store.Save(fourth with{LastCommitUtc=DateTime.UtcNow}));
                Assert.Equal(original,File.ReadAllBytes(anchor));Assert.Equal("original complete working copy",File.ReadAllText(first.WorkingPath));return;
            }
            if(scenario=="stale-save")
            {
                Assert.Throws<IOException>(()=>store.Save(first with{LastCommitUtc=DateTime.UtcNow}));
                Assert.Equal(second,Assert.Single(store.LoadAll()));
            }
            else if(scenario=="broken-link")
            {
                string path=Path.Combine(directory,"session-state-0000000000000000001.json");var node=JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
                node["PreviousRecordSha256"]=new string('A',64);File.WriteAllText(path,node.ToJsonString());
                Assert.False(Assert.Single(store.LoadAll()).IsSupported);Assert.Equal(original,File.ReadAllBytes(anchor));
            }
            else if(scenario=="roundtrip")
            {
                var third=store.Save(second with{LastCommitUtc=DateTime.UnixEpoch.AddSeconds(1700000002)});
                Assert.Equal(2,third.StorageRevision);Assert.Equal(second.StoredRecordSha256,third.PreviousRecordSha256);
                Assert.Equal(third,Assert.Single(store.LoadAll()));Assert.Equal(original,File.ReadAllBytes(anchor));
            }
            if(scenario=="atomic-editor-save")Assert.Equal("later atomic editor save",File.ReadAllText(first.WorkingPath));
            else Assert.Equal("original complete working copy",File.ReadAllText(first.WorkingPath));
        }
        finally
        {
            string full=Path.GetFullPath(fixture);
            if(!full.StartsWith(Path.TrimEndingDirectorySeparator(baseRoot)+Path.DirectorySeparatorChar,StringComparison.Ordinal)||
                !Path.GetFileName(full).StartsWith("filecat-edit-store-",StringComparison.Ordinal))throw new IOException("Unexpected storage test cleanup root.");
            Directory.Delete(full,true);
        }
    }
}
