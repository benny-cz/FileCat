using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Tests;

internal sealed class EditCopyPublicationDestination(IDisposable owner,IEditSaveCopyPublicationStage stage,ArchiveArtifactReservation reservation,
    IArchiveDestinationSpace space,Func<Guid,int,ArchiveArtifactReservation> reserve,Func<ArchiveArtifactReservation,IArchiveRetainedStage> create):IDisposable
{
    internal IEditSaveCopyPublicationStage Stage { get; }=stage;
    internal ArchiveArtifactReservation Reservation { get; }=reservation;
    internal IArchiveDestinationSpace Space { get; }=space;
    internal Func<Guid,int,ArchiveArtifactReservation> Reserve { get; }=reserve;
    internal Func<ArchiveArtifactReservation,IArchiveRetainedStage> Create { get; }=create;
    public void Dispose(){try{Stage.Dispose();}finally{owner.Dispose();}}
}

internal static class ArchiveEditSaveCopyPublicationCases
{
    internal static IEnumerable<object[]> Scenarios()=>new[]{"regular","empty","large","origin","late-save","name-collision",
        "stage-changed","source-changed","ready-receipt-failure","corrupt-ready-readback","ready-flush-failure","outcome-receipt-failure",
        "cancel-before","cancel-ready","late-cancel","post-rename-failure","target-race","stage-directory-replaced",
        "repeat-preparation","wrong-receipt-parent","ready-record-substitution","post-publish-replaced","witness-other-stage","witness-stale","platform-name"}
        .Select(value=>new object[]{value});

    /// <param name="unqualify">Makes the destination folder changeable by others for "unqualified-namespace" (default: group write).</param>
    internal static void Run(string scenario,Func<string,bool,CancellationToken,EditDirectoryLease> open,IFileSystemOperations files,
        Func<string,long,EditCopyPublicationDestination> create,Action<string>? unqualify=null)
    {
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string root=CreateOwnedDirectory(Path.Combine(parent,"filecat-edit-publish-"+Guid.NewGuid().ToString("N")));
        EditCommitCopy? copy=null;EditCopyPublicationDestination? destination=null,other=null;EditSaveCopyPublicationResult? result=null;
        try
        {
            var store=new EditSessionStore(Path.Combine(root,"sessions"),files,open);
            using var original=new MemoryContentSource("notes.txt","original working file"u8.ToArray());
            var record=store.CreateRemote("test","test","/notes.txt",original,original.GetRevision()!.Value,null,ct);
            byte[] bytes=scenario=="empty"?[]:Enumerable.Range(0,scenario=="large"?2097163:73).Select(i=>(byte)(i*31)).ToArray();
            File.WriteAllBytes(record.WorkingPath,bytes);
            File.SetLastWriteTimeUtc(record.WorkingPath,DateTime.UnixEpoch.AddSeconds(1700000000).AddTicks(7));
            if(scenario=="origin")Assert.True(files.WriteOriginMark(record.WorkingPath,
                "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/notes\r\nQuarantine=0081;6553f100;FileCat;public-test\r\n"));
            copy=store.PrepareSaveCopy(record,root,new LocalFileSystemProvider(),()=>ct.ThrowIfCancellationRequested());
            var source=Assert.IsType<NativeEditSnapshot>(copy.Native);var expected=((IArchiveRetainedFile)source).Observe(ct);
            string folder=CreateOwnedDirectory(Path.Combine(root,"destination"));
            string targetLeaf=scenario=="platform-name"?(OperatingSystem.IsWindows()?new string('\u754c',200)+".txt":"notes: draft?.txt"):"notes.txt";
            string target=Path.Combine(folder,targetLeaf);destination=create(folder,bytes.Length);
            using var cancel=CancellationTokenSource.CreateLinkedTokenSource(ct);bool blocked=false;
            var output=new StageEffects(destination.Stage,()=>
            {
                if(scenario=="target-race")File.WriteAllText(target,"other target owns this name");
            },()=>
            {
                if(scenario=="late-cancel")cancel.Cancel();
                if(scenario=="post-rename-failure")throw new IOException("Controlled failure after native rename.");
                if(scenario=="post-publish-replaced")Mutate(()=>{File.Move(target,target+".retained");File.WriteAllText(target,"other target owns this name");});
            });
            var prepared=EditSaveCopyPreparation.Prepare(source,output,destination.Reservation,destination.Space,ct);
            if(scenario=="unqualified-namespace")
            {
                if(unqualify is not null)unqualify(folder);
                else File.SetUnixFileMode(folder,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute|UnixFileMode.GroupWrite);
            }
            if(scenario=="late-save")File.WriteAllText(record.WorkingPath,"later edit stays local");
            if(scenario=="name-collision")File.WriteAllText(target,"other target owns this name");
            if(scenario=="source-changed")Mutate(()=>File.WriteAllText(copy.Path,"changed captured bytes"));
            if(scenario=="stage-changed")Mutate(()=>File.WriteAllText(destination.Reservation.Path,"changed output bytes"));
            if(scenario=="stage-directory-replaced")Mutate(()=>{Directory.Move(folder,folder+".retained");Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder,destination.Reservation.LeafName),bytes);});
            if(scenario is "wrong-receipt-parent" or "witness-other-stage")other=create(CreateOwnedDirectory(Path.Combine(root,"other")),bytes.Length);
            if(scenario=="witness-other-stage")
            {
                var alternate=EditSaveCopyPreparation.Prepare(source,other!.Stage,other.Reservation,other.Space,ct);
                var witness=other.Stage.ReviewSaveCopy(alternate.Output.Reviewed,"notes.txt",ct);
                Assert.ThrowsAny<IOException>(()=>destination.Stage.ClaimSaveCopy(witness,ct));
            }
            if(scenario=="witness-stale")
            {
                var stale=destination.Stage.ReviewSaveCopy(prepared.Output.Reviewed,"old.txt",ct);
                destination.Stage.ReviewSaveCopy(prepared.Output.Reviewed,"new.txt",ct);
                Assert.ThrowsAny<IOException>(()=>destination.Stage.ClaimSaveCopy(stale,ct));
                Assert.False(File.Exists(Path.Combine(folder,"old.txt")));Assert.False(File.Exists(Path.Combine(folder,"new.txt")));
            }
            int sequence=0;bool readyCallback=false;
            ArchiveArtifactReservation Reserve(Guid nonce,int next)
            {sequence=next;return (scenario=="wrong-receipt-parent"?other!:destination).Reserve(nonce,next);}
            IArchiveRetainedStage Create(ArchiveArtifactReservation reservation)
            {
                if(scenario=="ready-receipt-failure"&&sequence==1||scenario=="outcome-receipt-failure"&&sequence==2)
                    throw new IOException("Controlled receipt creation failure.");
                var file=destination.Create(reservation);
                return sequence==1&&(scenario is "corrupt-ready-readback" or "ready-flush-failure")
                    ?new ReceiptEffects(file,scenario=="corrupt-ready-readback",scenario=="ready-flush-failure"):file;
            }
            void Check()
            {
                if(readyCallback||!Directory.EnumerateFiles(folder,"*.manifest").Any())return;readyCallback=true;
                if(scenario=="cancel-ready")cancel.Cancel();
                if(scenario=="ready-record-substitution")Mutate(()=>
                {
                    string path=Directory.EnumerateFiles(folder,"*.manifest").Single();byte[] receipt=ReadShared(path);
                    File.Move(path,path+".retained");File.WriteAllBytes(path,receipt);
                });
            }
            if(scenario=="cancel-before")cancel.Cancel();
            var error=Record.Exception(()=>result=EditSaveCopyPublication.Publish(prepared,targetLeaf,destination.Space,Reserve,Create,cancel.Token,Check));
            TestContext.Current.TestOutputHelper?.WriteLine(error?.ToString()??result?.Issue?.ToString()??"Published and durably recorded.");
            bool beforeFailure=scenario is "ready-receipt-failure" or "corrupt-ready-readback" or "ready-flush-failure" or "wrong-receipt-parent" or "unqualified-namespace"||
                !blocked&&(scenario is "source-changed" or "stage-changed" or "stage-directory-replaced" or "ready-record-substitution");
            if(scenario is "cancel-before" or "cancel-ready")
            {Assert.IsAssignableFrom<OperationCanceledException>(error);Assert.Null(result);Assert.False(File.Exists(target));}
            else if(beforeFailure)
            {Assert.IsAssignableFrom<IOException>(error);Assert.Null(result);Assert.False(File.Exists(target));}
            else
            {
                Assert.Null(error);Assert.NotNull(result);
                bool uncertain=scenario is "name-collision" or "target-race"||scenario=="post-publish-replaced"&&!blocked;
                if(uncertain)
                {Assert.Null(result.Published);Assert.NotNull(result.Issue);Assert.Equal("other target owns this name",File.ReadAllText(target));}
                else
                {
                    Assert.NotNull(result.Published);Assert.Equal(bytes,ReadShared(target));
                    Assert.Equal(prepared.Output.Reviewed.Version,result.Published.Version);Assert.Equal(expected.Origin.Digest,result.Published.Origin.Digest);
                    Assert.Equal(expected.MemberModified,result.Published.MemberModified);
                    if(scenario is "post-rename-failure" or "outcome-receipt-failure")Assert.NotNull(result.Issue);else Assert.Null(result.Issue);
                }
                Assert.Equal(scenario!="outcome-receipt-failure",result.OutcomeRecorded);
                Assert.Equal(1,result.Receipts[0].Record.Sequence);Assert.Equal("prepared",result.Receipts[0].Record.Phase);
                foreach(var receipt in result.Receipts)
                {receipt.Review(ct);Assert.Equal(receipt.Record,EditSaveCopyRecord.Decode(ReadShared(Path.Combine(folder,receipt.Observation.LeafName))));}
                if(result.OutcomeRecorded)Assert.Equal(uncertain?"needs-review":"published",result.Receipts[1].Record.Phase);
                if(scenario=="repeat-preparation")
                {
                    Assert.ThrowsAny<IOException>(()=>EditSaveCopyPublication.Publish(prepared,"other.txt",destination.Space,Reserve,Create,ct));
                    Assert.False(File.Exists(Path.Combine(folder,"other.txt")));
                }
            }
            Assert.Equal(scenario=="late-save"?"later edit stays local":System.Text.Encoding.UTF8.GetString(bytes),File.ReadAllText(record.WorkingPath));
            void Mutate(Action mutation)
            {
                var failure=Record.Exception(mutation);
                if(failure is null)return;
                Assert.True(OperatingSystem.IsWindows()&&failure is IOException or UnauthorizedAccessException,failure.ToString());blocked=true;
            }
        }
        finally
        {
            try{result?.Dispose();}finally{try{other?.Dispose();}finally{try{destination?.Dispose();}finally{if(copy is not null)Assert.True(Record.Exception(copy.Dispose) is null or IOException);}}}
            if(!Path.GetFullPath(root).StartsWith(Path.TrimEndingDirectorySeparator(parent)+Path.DirectorySeparatorChar,StringComparison.Ordinal)||
                !Path.GetFileName(root).StartsWith("filecat-edit-publish-",StringComparison.Ordinal))throw new IOException("Unexpected edit publication fixture root.");
            Directory.Delete(root,true);
        }
    }

    private static string CreateOwnedDirectory(string path)=>OperatingSystem.IsWindows()?Directory.CreateDirectory(path).FullName:
        Directory.CreateDirectory(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute).FullName;
    private static byte[] ReadShared(string path)
    {
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var output=new MemoryStream();input.CopyTo(output);return output.ToArray();
    }

    private sealed class StageEffects(IEditSaveCopyPublicationStage stage,Action before,Action after):IEditSaveCopyPublicationStage
    {
        public string ObjectIdentity=>stage.ObjectIdentity;
        public ArchiveFileObservation Observe(CancellationToken ct)=>stage.Observe(ct);
        public void Review(ArchiveFileObservation expected,CancellationToken ct)=>stage.Review(expected,ct);
        public Stream OpenReadView(ArchiveFileObservation expected,CancellationToken ct)=>stage.OpenReadView(expected,ct);
        public string ReviewCreation(ArchiveArtifactReservation reservation,CancellationToken ct)=>stage.ReviewCreation(reservation,ct);
        public Stream BeginEvidence(ArchiveArtifactReservation reservation,CancellationToken ct)=>stage.BeginEvidence(reservation,ct);
        public ArchiveFileObservation SealEvidence(CancellationToken ct)=>stage.SealEvidence(ct);
        public void StopWrites()=>stage.StopWrites();
        public Stream BeginRebuild(long length,CancellationToken ct)=>stage.BeginRebuild(length,ct);
        public ArchiveFileObservation SealForVerification(CancellationToken ct)=>stage.SealForVerification(ct);
        public ArchiveFileObservation ApplyEditMetadata(ArchiveFileObservation copied,ArchiveFileObservation source,CancellationToken ct)=>stage.ApplyEditMetadata(copied,source,ct);
        public EditSaveCopyNameReview ReviewSaveCopy(ArchiveFileObservation prepared,string leaf,CancellationToken ct)=>stage.ReviewSaveCopy(prepared,leaf,ct);
        public void ClaimSaveCopy(EditSaveCopyNameReview review,CancellationToken ct){before();stage.ClaimSaveCopy(review,ct);after();}
        public ArchiveFileObservation ReviewSavedCopy(EditSaveCopyNameReview review,CancellationToken ct)=>stage.ReviewSavedCopy(review,ct);
        public void Dispose()=>throw new InvalidOperationException("Borrowed publication views do not own native disposal.");
    }
    private sealed class ReceiptEffects(IArchiveRetainedStage stage,bool corrupt,bool failFlush):IEditSaveCopyReceiptStage
    {
        public string ObjectIdentity=>stage.ObjectIdentity;
        public ArchiveFileObservation Observe(CancellationToken ct)=>stage.Observe(ct);
        public void Review(ArchiveFileObservation expected,CancellationToken ct)=>stage.Review(expected,ct);
        public Stream OpenReadView(ArchiveFileObservation expected,CancellationToken ct)
        {using var input=stage.OpenReadView(expected,ct);using var data=new MemoryStream();input.CopyTo(data);byte[] bytes=data.ToArray();if(corrupt)bytes[0]^=1;return new MemoryStream(bytes,false);}
        public void FlushReceipt(ArchiveFileObservation expected,CancellationToken ct)
        {if(failFlush)throw new IOException("Controlled native receipt flush failure.");((IEditSaveCopyReceiptStage)stage).FlushReceipt(expected,ct);}
        public string ReviewCreation(ArchiveArtifactReservation reservation,CancellationToken ct)=>stage.ReviewCreation(reservation,ct);
        public Stream BeginEvidence(ArchiveArtifactReservation reservation,CancellationToken ct)=>stage.BeginEvidence(reservation,ct);
        public ArchiveFileObservation SealEvidence(CancellationToken ct)=>stage.SealEvidence(ct);
        public void StopWrites()=>stage.StopWrites();
        public Stream BeginRebuild(long length,CancellationToken ct)=>stage.BeginRebuild(length,ct);
        public ArchiveFileObservation SealForVerification(CancellationToken ct)=>stage.SealForVerification(ct);
        public void Dispose()=>stage.Dispose();
    }
}
