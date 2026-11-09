using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Tests;

internal static class ArchiveEditSaveCopySessionCases
{
    internal static IEnumerable<object[]> Scenarios()=>new[]{"complete","empty","large","origin","late-save","later-revision","cancel-before","cancel-after-intent",
        "target-collision","stage-create-failure","outcome-receipt-failure","wrong-session","wrong-destination","inside-store","second-attempt",
        "intent-malformed","intent-future","intent-invalid-utf8","source-payload-changed","target-changed","target-replaced",
        "prepared-receipt-corrupt","destination-replaced","missing-outcome","missing-ready","reader-unavailable"}
        .Select(value=>new object[]{value});

    internal static void Run(string scenario,Func<string,bool,CancellationToken,EditDirectoryLease> open,IFileSystemOperations files,
        Func<string,CancellationToken,EditSaveCopyDestination> destinationOpen)
    {
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string root=PrivateDirectory(Path.Combine(parent,"filecat-edit-save-session-"+Guid.NewGuid().ToString("N")));
        EditCommitCopy? copy=null;EditSaveCopyPublicationResult? result=null;
        try
        {
            EditSaveCopyDestination Destination(string path,CancellationToken token)=>new FaultDestination(
                destinationOpen(scenario=="wrong-destination"?PrivateDirectory(Path.Combine(root,"other-destination")):path,token),scenario);
            var store=new EditSessionStore(Path.Combine(root,"sessions"),files,open,Destination);
            using var initial=new MemoryContentSource("owned.txt","complete initial edit"u8.ToArray());
            var record=store.CreateRemote("test","test","/owned.txt",initial,initial.GetRevision()!.Value,null,ct);
            byte[] bytes=scenario=="empty"?[]:Enumerable.Range(0,scenario=="large"?2097163:113).Select(i=>(byte)(31*i)).ToArray();
            File.WriteAllBytes(record.WorkingPath,bytes);File.SetLastWriteTimeUtc(record.WorkingPath,DateTime.UnixEpoch.AddSeconds(1700000000).AddTicks(7));
            if(scenario=="origin")Assert.True(files.WriteOriginMark(record.WorkingPath,
                "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/notes\r\nQuarantine=0081;6553f100;FileCat;public-test\r\n"));
            copy=store.PrepareSaveCopy(record,root,new LocalFileSystemProvider(),()=>ct.ThrowIfCancellationRequested());
            string capture=Path.GetDirectoryName(Path.GetDirectoryName(copy.Path))!;
            string intentPath=Path.Combine(capture,EditSaveCopyIntent.FileName);
            string folder=PrivateDirectory(Path.Combine(root,"destination")),target=Path.Combine(folder,"saved.txt");
            if(scenario=="inside-store")target=Path.Combine(Path.GetDirectoryName(record.WorkingPath)!,"saved.txt");
            if(scenario=="late-save")File.WriteAllText(record.WorkingPath,"later editor save stays separate");
            if(scenario=="target-collision")File.WriteAllText(target,"keep existing destination");
            using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
            if(scenario=="cancel-before")stop.Cancel();
            void Check(){if(scenario=="cancel-after-intent"&&File.Exists(intentPath))stop.Cancel();}
            var selected=scenario=="wrong-session"?record with{Id=Guid.NewGuid().ToString("N")}:record;
            var error=Record.Exception(()=>result=store.SaveNewNativeCopy(selected,copy,target,stop.Token,Check));
            TestContext.Current.TestOutputHelper?.WriteLine(error?.ToString()??result?.Issue?.ToString()??"Saved through a session-bound destination intent.");
            if(scenario is "cancel-before" or "wrong-session" or "wrong-destination" or "inside-store")
            {
                if(scenario=="cancel-before")Assert.IsAssignableFrom<OperationCanceledException>(error);else Assert.IsAssignableFrom<IOException>(error);
                Assert.False(File.Exists(intentPath));Assert.False(File.Exists(target));return;
            }
            Assert.True(File.Exists(intentPath));
            if(scenario=="cancel-after-intent")Assert.IsAssignableFrom<OperationCanceledException>(error);
            else if(scenario=="stage-create-failure")Assert.IsAssignableFrom<IOException>(error);
            else
            {
                Assert.Null(error);Assert.NotNull(result);
                if(scenario=="target-collision")
                {Assert.Null(result.Published);Assert.NotNull(result.Issue);Assert.Equal("keep existing destination",File.ReadAllText(target));}
                else
                {
                    Assert.NotNull(result.Published);
                    if(scenario=="outcome-receipt-failure"){Assert.NotNull(result.Issue);Assert.False(result.OutcomeRecorded);}
                    else{Assert.Null(result.Issue);Assert.True(result.OutcomeRecorded);}
                }
            }
            if(scenario=="second-attempt")
            {
                Assert.ThrowsAny<IOException>(()=>store.SaveNewNativeCopy(record,copy,Path.Combine(folder,"second.txt"),ct));
                Assert.False(File.Exists(Path.Combine(folder,"second.txt")));
            }
            // Retire all live native owners before simulating restart or editing the test-owned recovery files.
            result?.Dispose();result=null;
            Assert.IsAssignableFrom<IOException>(Record.Exception(copy.Dispose));
            Assert.True(File.Exists(copy.Path));
            var intent=EditSaveCopyIntent.Decode(File.ReadAllBytes(intentPath));
            Assert.Equal(target,intent.DestinationPath);Assert.Equal(copy.Sha256,intent.Sha256);Assert.Equal(record.Id,intent.Session);
            if(File.Exists(target)&&scenario!="target-collision")Assert.Equal(bytes,File.ReadAllBytes(target));
            if(scenario=="intent-malformed")File.WriteAllText(intentPath,"{");
            if(scenario=="intent-future")
            {var node=JsonNode.Parse(File.ReadAllBytes(intentPath))!.AsObject();node["Version"]=2;File.WriteAllText(intentPath,node.ToJsonString());}
            if(scenario=="intent-invalid-utf8")
            {byte[] data=File.ReadAllBytes(intentPath);data[2]=0xff;File.WriteAllBytes(intentPath,data);}
            if(scenario=="source-payload-changed")File.WriteAllText(copy.Path,"changed retained source");
            if(scenario=="target-changed")File.WriteAllText(target,"changed");
            if(scenario=="target-replaced")
            {File.Move(target,Path.Combine(root,"original-saved-file"));File.WriteAllBytes(target,bytes);}
            if(scenario=="destination-replaced")
            {Directory.Move(folder,folder+".retained");PrivateDirectory(folder);File.WriteAllBytes(target,bytes);}
            if(scenario=="prepared-receipt-corrupt")
            {var path=Path.Combine(folder,intent.PreparedLeaf);var node=JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();node["Nonce"]=Guid.NewGuid();File.WriteAllText(path,node.ToJsonString());}
            if(scenario=="missing-outcome")File.Delete(Path.Combine(folder,intent.OutcomeLeaf));
            if(scenario=="missing-ready")File.Delete(Path.Combine(folder,intent.PreparedLeaf));
            var reviewRecord=scenario=="later-revision"?store.Save(record with{LastCommitUtc=DateTime.UnixEpoch.AddSeconds(1700000001)}):record;
            var before=Snapshot();
            var restarted=new EditSessionStore(store.Root,files,open,scenario=="reader-unavailable"?null:destinationOpen);
            var review=Assert.Single(restarted.ReviewRetainedCaptures(reviewRecord,ct));
            if(scenario is "intent-malformed" or "intent-future" or "intent-invalid-utf8" or "source-payload-changed")
            {Assert.Equal(EditCaptureRecoveryState.NeedsReview,review.State);Assert.Null(review.SavedCopy);}
            else
            {
                Assert.Equal(EditCaptureRecoveryState.Complete,review.State);Assert.NotNull(review.SavedCopy);Assert.Equal(target,review.SavedCopy.Destination);
                var expected=scenario is "cancel-after-intent" or "stage-create-failure"?EditSavedCopyRecoveryState.Interrupted:
                    scenario is "target-collision" or "outcome-receipt-failure" or "target-changed" or "target-replaced" or "prepared-receipt-corrupt" or
                        "destination-replaced" or "missing-outcome" or "missing-ready" or "reader-unavailable"?EditSavedCopyRecoveryState.NeedsReview:
                        EditSavedCopyRecoveryState.MatchesRecordedCopy;
                Assert.Equal(expected,review.SavedCopy.State);Assert.NotEmpty(review.SavedCopy.Reason);
            }
            Assert.Equal(before,Snapshot());
            Assert.Equal(scenario=="late-save"?"later editor save stays separate":Encoding.UTF8.GetString(bytes),File.ReadAllText(record.WorkingPath));
            string[] Snapshot()=>Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(path=>Path.GetRelativePath(root,path)+":"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
        }
        finally
        {
            try{result?.Dispose();}finally{if(copy is not null)Assert.True(Record.Exception(copy.Dispose) is null or IOException);}
            if(!Path.GetFullPath(root).StartsWith(Path.TrimEndingDirectorySeparator(parent)+Path.DirectorySeparatorChar,StringComparison.Ordinal)||
                !Path.GetFileName(root).StartsWith("filecat-edit-save-session-",StringComparison.Ordinal))throw new IOException("Unexpected saved-copy session fixture root.");
            Directory.Delete(root,true);
        }
    }
    private static string PrivateDirectory(string path)=>OperatingSystem.IsWindows()?Directory.CreateDirectory(path).FullName:
        Directory.CreateDirectory(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute).FullName;
    private sealed class FaultDestination(EditSaveCopyDestination owner,string scenario):EditSaveCopyDestination
    {
        private int receipts;
        internal override string Path=>owner.Path;
        internal override string Identity=>owner.Identity;
        internal override IArchiveDestinationSpace Space=>owner.Space;
        internal override ArchiveArtifactReservation Reserve(Guid nonce,ArchiveArtifactKind kind,long maximum,CancellationToken ct)=>owner.Reserve(nonce,kind,maximum,ct);
        internal override IArchiveRetainedStage Create(ArchiveArtifactReservation reservation,CancellationToken ct)
        {
            if(scenario=="stage-create-failure"&&reservation.Kind==ArchiveArtifactKind.Stage||
                scenario=="outcome-receipt-failure"&&reservation.Kind==ArchiveArtifactKind.Manifest&&++receipts==2)
                throw new IOException("Controlled saved-copy destination creation failure.");
            return owner.Create(reservation,ct);
        }
        internal override IArchiveRetainedArtifact? OpenReadOrAbsent(string leaf,long maximum,CancellationToken ct)=>owner.OpenReadOrAbsent(leaf,maximum,ct);
        internal override void Review(CancellationToken ct)=>owner.Review(ct);
        public override void Dispose()=>owner.Dispose();
    }
}
