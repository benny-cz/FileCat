using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Tests;

internal static class ArchiveEditCaptureRecoveryCases
{
    internal static IEnumerable<object[]> Scenarios()
    {
        foreach(string scenario in new[]{"none","complete","interrupted-empty","interrupted-partial","header-missing","receipt-version",
            "receipt-version-string","receipt-duplicate","receipt-unknown","receipt-malformed","receipt-depth","receipt-large","receipt-length",
            "receipt-length-string","receipt-digest","receipt-session","header-files","header-leaf","extra-child","extra-file","payload-changed",
            "payload-missing","payload-replaced","stale-record","later-revision","later-second-revision","cancel","capture-budget","unknown-name",
            "header-utf8-name","receipt-utf8-value","header-unpaired-name","receipt-unpaired-value",
            "payload-folder-missing"})yield return [scenario];
    }
    internal static void Run(string scenario,Func<string,bool,CancellationToken,EditDirectoryLease> open)
    {
        var ct=TestContext.Current.CancellationToken;string parent=Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath();
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        parent=Path.GetFullPath(parent);Directory.CreateDirectory(parent);
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-capture-recovery-"+Guid.NewGuid().ToString("N"))).FullName;
        EditCommitCopy? copy=null;
        try
        {
            var store=new EditSessionStore(Path.Combine(fixture,"sessions"),new PortableFileOperations(),open);
            using var source=new MemoryContentSource("owned.txt","complete recovery source"u8.ToArray());
            var record=store.CreateRemote("owned","owned","/owned.txt",source,source.GetRevision()!.Value,null,ct);
            string session=store.SessionDirectory(record);
            if(scenario=="none"){Assert.Empty(store.ReviewRetainedCaptures(record,ct));return;}
            if(scenario is "capture-budget" or "unknown-name")
            {
                int count=scenario=="capture-budget"?EditSessionStore.MaximumRecoveryCaptures+1:1;
                for(int i=0;i<count;i++)Directory.CreateDirectory(Path.Combine(session,"commit-"+(count==1?"unknown":Guid.NewGuid().ToString("N"))));
                var limited=Assert.Single(store.ReviewRetainedCaptures(record,ct));Assert.Equal(EditCaptureRecoveryState.NeedsReview,limited.State);
                Assert.Equal(count,Directory.GetDirectories(session,"commit-*").Length);return;
            }
            copy=store.PrepareCommit(record,fixture,ct);
            string capture=Path.GetDirectoryName(Path.GetDirectoryName(copy.Path))!,header=Path.Combine(capture,"capture.json"),receipt=Path.Combine(capture,"captured.json");
            string sentinel=Path.Combine(capture,"keep-for-restart.txt");File.WriteAllText(sentinel,"public retained-copy fixture");
            Assert.IsAssignableFrom<IOException>(Record.Exception(copy.Dispose));File.Delete(sentinel);
            Assert.True(File.Exists(copy.Path));
            if(scenario=="interrupted-empty"){File.Delete(receipt);File.Delete(copy.Path);}
            else if(scenario=="interrupted-partial"){File.Delete(receipt);File.WriteAllText(copy.Path,"partial captured bytes");}
            else if(scenario=="header-missing")File.Delete(header);
            else if(scenario=="receipt-malformed")File.WriteAllText(receipt,"{");
            else if(scenario=="receipt-depth")File.WriteAllText(receipt,"{\"Nested\":[[[[[0]]]]]}");
            else if(scenario=="receipt-large")File.WriteAllText(receipt,new string(' ',16385));
            else if(scenario=="receipt-duplicate")File.WriteAllText(receipt,File.ReadAllText(receipt).Insert(1,"\"Version\":1,"));
            else if(scenario is "header-utf8-name" or "receipt-utf8-value" or "header-unpaired-name" or "receipt-unpaired-value")
            {
                string target=scenario.StartsWith("header-",StringComparison.Ordinal)?header:receipt;byte[] bytes=File.ReadAllBytes(target);
                if(scenario=="header-utf8-name")
                {int at=bytes.AsSpan().IndexOf("\"Version\""u8);Assert.True(at>=0);bytes[at+1]=0xff;}
                else if(scenario=="receipt-utf8-value")
                {int at=bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(record.Id));Assert.True(at>=0);bytes[at]=0xff;}
                else bytes=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(
                    scenario=="header-unpaired-name"?"\"Version\"":"\""+record.Id+"\"","\"\\uD800\"",StringComparison.Ordinal));
                File.WriteAllBytes(target,bytes);
            }
            else if(scenario.StartsWith("receipt-",StringComparison.Ordinal)||scenario.StartsWith("header-",StringComparison.Ordinal))
            {
                string target=scenario.StartsWith("header-",StringComparison.Ordinal)?header:receipt;
                var node=JsonNode.Parse(File.ReadAllBytes(target))!.AsObject();
                switch(scenario)
                {
                    case "receipt-version":node["Version"]=2;break;
                    case "receipt-version-string":node["Version"]="1";break;
                    case "receipt-unknown":node["Future"]=true;break;
                    case "receipt-length":node["Length"]=-1;break;
                    case "receipt-length-string":node["Length"]="24";break;
                    case "receipt-digest":node["Sha256"]=new string('A',64);break;
                    case "receipt-session":node["Session"]=new string('0',32);break;
                    case "header-files":node["Files"]="unrelated-directory";break;
                    case "header-leaf":node["Leaf"]="../unrelated.txt";break;
                    default:throw new InvalidOperationException(scenario);
                }
                File.WriteAllText(target,node.ToJsonString());
            }
            else if(scenario=="extra-child")File.WriteAllText(Path.Combine(capture,"foreign.txt"),"unrelated child");
            else if(scenario=="extra-file")File.WriteAllText(Path.Combine(Path.GetDirectoryName(copy.Path)!,"foreign.txt"),"unrelated payload sibling");
            else if(scenario=="payload-changed")File.WriteAllText(copy.Path,"modified captured bytes");
            else if(scenario=="payload-missing")File.Delete(copy.Path);
            // A removal of this copy that was stopped: after its payload's whole folder, or after its own record, which
            // such a removal takes first. The discard removes what is left of either with the edit, so this review says
            // the same rather than offering nothing.
            else if(scenario=="payload-folder-missing"){File.Delete(copy.Path);Directory.Delete(Path.GetDirectoryName(copy.Path)!);}
            else if(scenario=="payload-replaced"){File.Move(copy.Path,Path.Combine(fixture,"preserved-payload"));File.WriteAllText(copy.Path,"complete recovery source");}
            else if(scenario=="stale-record")store.Save(record with{LastCommitUtc=DateTime.UtcNow});
            var reviewRecord=record;
            if(scenario is "later-revision" or "later-second-revision")
            {
                reviewRecord=store.Save(record with{LastCommitUtc=DateTime.UnixEpoch.AddSeconds(1700000001)});
                if(scenario=="later-second-revision")reviewRecord=store.Save(reviewRecord with{LastCommitUtc=DateTime.UnixEpoch.AddSeconds(1700000002)});
            }
            var before=Files();
            // A new store and freshly opened native handles must inspect durable evidence without creator authority.
            var restarted=new EditSessionStore(store.Root,new PortableFileOperations(),open);
            if(scenario=="stale-record")Assert.ThrowsAny<IOException>(()=>restarted.ReviewRetainedCaptures(record,ct));
            else if(scenario=="cancel")
            {using var stop=new CancellationTokenSource();stop.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>restarted.ReviewRetainedCaptures(record,stop.Token));}
            else
            {
                var review=Assert.Single(restarted.ReviewRetainedCaptures(reviewRecord,ct));
                // A copy whose payload is gone while its receipt is there is interrupted, not in need of review: there
                // is nothing left to recover from it, what remains is this copy's own records, and only a verified or an
                // interrupted copy can be removed from the list at all.
                Assert.Equal(scenario is "complete" or "later-revision" or "later-second-revision"?EditCaptureRecoveryState.Complete:
                    scenario.StartsWith("interrupted-",StringComparison.Ordinal)||scenario is "payload-missing" or
                    "payload-folder-missing" or "header-missing"?
                    EditCaptureRecoveryState.Interrupted:EditCaptureRecoveryState.NeedsReview,review.State);
                Assert.Equal(capture,review.Directory);Assert.NotEmpty(review.Reason);
                if(scenario is "complete" or "later-revision" or "later-second-revision"){Assert.Equal(copy.Length,review.Length);Assert.Equal(copy.Sha256,review.Sha256);}
                else{Assert.Null(review.Length);Assert.Null(review.Sha256);}
            }
            Assert.Equal(before,Files());Assert.Equal("complete recovery source",File.ReadAllText(record.WorkingPath));
            string[] Files()=>Directory.EnumerateFiles(fixture,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(path=>Path.GetRelativePath(fixture,path)+":"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
        }
        finally
        {
            if(copy is not null)Assert.Null(Record.Exception(copy.Dispose));
            if(!Path.GetFullPath(fixture).StartsWith(Path.TrimEndingDirectorySeparator(parent)+Path.DirectorySeparatorChar,StringComparison.Ordinal)||
                !Path.GetFileName(fixture).StartsWith("filecat-capture-recovery-",StringComparison.Ordinal))throw new IOException("Unexpected recovery fixture root.");
            Directory.Delete(fixture,true);
        }
    }

    internal static IEnumerable<object[]> RemovalScenarios()
    {
        foreach(string scenario in new[]{"complete","interrupted-empty","interrupted-partial","two-copies","extra-child","extra-file",
            "payload-changed","payload-replaced","payload-missing","payload-folder-missing","stale-record","wrong-folder","live-capture",
            "cancel"})yield return [scenario];
    }

    /// <summary>
    /// The restart recovery action: one reviewed retained copy is removed object by object; the edit, its working copy and
    /// every other retained copy stay. A copy that does not match its records is kept, with nothing removed.
    /// </summary>
    internal static void RunRemoval(string scenario,Func<string,bool,CancellationToken,EditDirectoryLease> open)
    {
        var ct=TestContext.Current.CancellationToken;string parent=Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath();
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        parent=Path.GetFullPath(parent);Directory.CreateDirectory(parent);
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-capture-removal-"+Guid.NewGuid().ToString("N"))).FullName;
        EditCommitCopy? live=null;
        try
        {
            var store=new EditSessionStore(Path.Combine(fixture,"sessions"),new PortableFileOperations(),open);
            Assert.True(store.SupportsRetainedCaptureRemoval);
            using var source=new MemoryContentSource("owned.txt","complete recovery source"u8.ToArray());
            var record=store.CreateRemote("owned","owned","/owned.txt",source,source.GetRevision()!.Value,null,ct);
            string session=store.SessionDirectory(record);
            string Retain()
            {
                // A capture whose cleanup stopped: an unexpected child keeps it, and is then taken away again.
                var copy=store.PrepareCommit(record,fixture,ct);string capture=Path.GetDirectoryName(Path.GetDirectoryName(copy.Path))!;
                string sentinel=Path.Combine(capture,"keep-for-restart.txt");File.WriteAllText(sentinel,"kept");
                Assert.IsAssignableFrom<IOException>(Record.Exception(copy.Dispose));File.Delete(sentinel);return capture;
            }
            string capture=Retain(),payload=Path.Combine(capture,"files","owned.txt"),receipt=Path.Combine(capture,"captured.json");
            string? other=scenario=="two-copies"?Retain():null;
            if(scenario=="interrupted-empty"){File.Delete(receipt);File.Delete(payload);}
            else if(scenario=="interrupted-partial"){File.Delete(receipt);File.WriteAllText(payload,"partial captured bytes");}
            else if(scenario=="extra-child")File.WriteAllText(Path.Combine(capture,"foreign.txt"),"unrelated child");
            else if(scenario=="extra-file")File.WriteAllText(Path.Combine(capture,"files","foreign.txt"),"unrelated payload sibling");
            else if(scenario=="payload-changed")File.WriteAllText(payload,"modified captured bytes");
            else if(scenario=="payload-replaced"){File.Move(payload,Path.Combine(fixture,"preserved-payload"));File.WriteAllText(payload,"complete recovery source");}
            // A removal of this copy that was stopped once the payload, or the payload's whole folder, was gone: this
            // removal takes them before the copy's own records, which are what is left to remove.
            else if(scenario=="payload-missing")File.Delete(payload);
            else if(scenario=="payload-folder-missing"){File.Delete(payload);Directory.Delete(Path.Combine(capture,"files"));}
            if(scenario=="stale-record")_=store.Save(record with{LastCommitUtc=DateTime.UnixEpoch.AddSeconds(1700000001)});
            var before=Files();
            var restarted=new EditSessionStore(store.Root,new PortableFileOperations(),open);
            if(scenario=="wrong-folder")
            {
                Assert.ThrowsAny<IOException>(()=>restarted.RemoveRetainedCapture(record,Path.Combine(fixture,Path.GetFileName(capture)),ct));
                Assert.ThrowsAny<IOException>(()=>restarted.RemoveRetainedCapture(record,Path.Combine(session,"files"),ct));
                Assert.Equal(before,Files());return;
            }
            if(scenario=="live-capture")
            {
                live=restarted.PrepareCommit(record,fixture,ct);
                Assert.ThrowsAny<IOException>(()=>restarted.RemoveRetainedCapture(record,capture,ct));
                Assert.True(Directory.Exists(capture));return;
            }
            if(scenario=="cancel")
            {
                using var stop=new CancellationTokenSource();stop.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(()=>restarted.RemoveRetainedCapture(record,capture,stop.Token));
                Assert.Equal(before,Files());return;
            }
            var result=restarted.RemoveRetainedCapture(record,capture,ct);
            if(scenario is "complete" or "interrupted-empty" or "interrupted-partial" or "two-copies" or "payload-missing" or
                "payload-folder-missing")
            {
                Assert.True(result.Complete,result.Issue);Assert.False(Directory.Exists(capture));
                Assert.Equal(other is null?0:1,Directory.GetDirectories(session,"commit-*").Length);
                if(other is not null)Assert.True(File.Exists(Path.Combine(other,"captured.json")));
                var reviews=restarted.ReviewRetainedCaptures(record,ct);Assert.Equal(other is null?0:1,reviews.Count);
                // The edit itself is untouched and still commits: a later capture and its own cleanup work.
                Assert.Equal("complete recovery source",File.ReadAllText(record.WorkingPath));
                var again=restarted.PrepareCommit(record,fixture,ct);Assert.Null(Record.Exception(again.Dispose));
            }
            else
            {
                Assert.False(result.Complete);Assert.False(result.MayHaveRemovedFiles,result.Issue);
                Assert.Contains("kept",result.Issue,StringComparison.Ordinal);Assert.Equal(before,Files());
            }
            string[] Files()=>Directory.EnumerateFiles(fixture,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(path=>Path.GetRelativePath(fixture,path)+":"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
        }
        finally
        {
            if(live is not null)Assert.Null(Record.Exception(live.Dispose));
            if(!Path.GetFullPath(fixture).StartsWith(Path.TrimEndingDirectorySeparator(parent)+Path.DirectorySeparatorChar,StringComparison.Ordinal)||
                !Path.GetFileName(fixture).StartsWith("filecat-capture-removal-",StringComparison.Ordinal))throw new IOException("Unexpected removal fixture root.");
            Directory.Delete(fixture,true);
        }
    }
}
