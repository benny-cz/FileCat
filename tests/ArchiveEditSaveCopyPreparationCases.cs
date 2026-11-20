using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Tests;

internal sealed class EditCopyTestDestination(IDisposable owner,IEditSaveCopyStage stage,ArchiveArtifactReservation reservation,IArchiveDestinationSpace space):IDisposable
{
    internal IEditSaveCopyStage Stage { get; }=stage;
    internal ArchiveArtifactReservation Reservation { get; }=reservation;
    internal IArchiveDestinationSpace Space { get; }=space;
    public void Dispose(){try{Stage.Dispose();}finally{owner.Dispose();}}
}

internal static class ArchiveEditSaveCopyPreparationCases
{
    internal static IEnumerable<object[]> Scenarios()=>new[]{"regular","empty","large","origin","late-save","cancel-before","cancel-copy",
        "source-disposed","source-changed","source-directory-replaced","corrupt-source","corrupt-stage-readback","wrong-reservation",
        "wrong-length","wrong-kind","preexisting-stage-data","stage-directory-replaced"}.Select(value=>new object[]{value});

    internal static void Run(string scenario,Func<string,bool,CancellationToken,EditDirectoryLease> open,IFileSystemOperations files,
        Func<string,long,ArchiveArtifactKind,EditCopyTestDestination> create)
    {
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string root=Directory.CreateDirectory(Path.Combine(parent,"filecat-edit-copy-stage-"+Guid.NewGuid().ToString("N"))).FullName;
        EditCommitCopy? copy=null;EditCopyTestDestination? destination=null,other=null;
        try
        {
            var store=new EditSessionStore(Path.Combine(root,"sessions"),files,open);
            using var original=new MemoryContentSource("notes.txt","original working file"u8.ToArray());
            var record=store.CreateRemote("test","test","/notes.txt",original,original.GetRevision()!.Value,null,ct);
            byte[] bytes=scenario=="empty"?[]:Enumerable.Range(0,scenario=="large"?2097163:73).Select(i=>(byte)(i*31)).ToArray();
            File.WriteAllBytes(record.WorkingPath,bytes);
            DateTime modified=DateTime.UnixEpoch.AddSeconds(1700000000).AddTicks(7);File.SetLastWriteTimeUtc(record.WorkingPath,modified);
            if(scenario=="origin")Assert.True(files.WriteOriginMark(record.WorkingPath,
                "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/notes\r\nQuarantine=0081;6553f100;FileCat;public-test\r\n"));
            copy=store.PrepareSaveCopy(record,root,new LocalFileSystemProvider(),()=>ct.ThrowIfCancellationRequested());
            IArchiveRetainedFile source=Assert.IsType<NativeEditSnapshot>(copy.Native);
            var expected=source.Observe(ct);Assert.Equal(copy.Sha256,expected.Version.Sha256);
            string folder=Directory.CreateDirectory(Path.Combine(root,"destination")).FullName;
            destination=create(folder,bytes.LongLength+(scenario=="wrong-length"?1:0),scenario=="wrong-kind"?ArchiveArtifactKind.Manifest:ArchiveArtifactKind.Stage);
            var reservation=destination.Reservation;IEditSaveCopyStage output=destination.Stage;
            bool refuse=scenario is "source-disposed" or "corrupt-source" or "corrupt-stage-readback" or "wrong-reservation" or
                "wrong-length" or "wrong-kind" or "preexisting-stage-data";
            if(scenario=="late-save")File.WriteAllText(record.WorkingPath,"later editor save stays local");
            if(scenario=="source-disposed")Assert.True(Record.Exception(copy.Dispose) is null or IOException);
            if(scenario is "source-changed" or "source-directory-replaced" or "stage-directory-replaced")
            {
                var mutation=Record.Exception(()=>
                {
                    if(scenario=="source-changed")File.WriteAllText(copy.Path,"changed captured bytes");
                    else
                    {
                        string moved=scenario=="stage-directory-replaced"?folder:Path.GetDirectoryName(copy.Path)!;
                        Directory.Move(moved,moved+".retained");Directory.CreateDirectory(moved);
                        File.WriteAllBytes(Path.Combine(moved,scenario=="stage-directory-replaced"?reservation.LeafName:Path.GetFileName(copy.Path)),bytes);
                    }
                });
                if(mutation is null)refuse=true;else Assert.True(OperatingSystem.IsWindows()&&mutation is IOException or UnauthorizedAccessException,mutation.ToString());
            }
            if(scenario=="corrupt-source")source=new BadSource(source);
            if(scenario=="corrupt-stage-readback")output=new BadStage(output);
            if(scenario=="wrong-reservation")
            {other=create(Directory.CreateDirectory(Path.Combine(root,"other")).FullName,bytes.Length,ArchiveArtifactKind.Stage);reservation=other.Reservation;}
            if(scenario=="preexisting-stage-data")
            {output.BeginRebuild(bytes.Length,ct).Write("keep"u8);output.SealForVerification(ct);}
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(ct);
            if(scenario=="cancel-before")cancellation.Cancel();int checks=0;
            EditPreparedSaveCopy? prepared=null;
            var error=Record.Exception(()=>prepared=EditSaveCopyPreparation.Prepare(source,output,reservation,destination.Space,cancellation.Token,
                ()=>{if(scenario=="cancel-copy"&&++checks==5)cancellation.Cancel();}));
            TestContext.Current.TestOutputHelper?.WriteLine(error?.ToString()??"Prepared bounded native copy.");
            if(scenario is "cancel-before" or "cancel-copy")Assert.IsAssignableFrom<OperationCanceledException>(error);
            else if(refuse)Assert.IsAssignableFrom<IOException>(error);
            else
            {
                Assert.Null(error);Assert.NotNull(prepared);prepared.Review(ct);
                Assert.Equal(expected.Version.Sha256,prepared.Output.Reviewed.Version.Sha256);
                Assert.Equal(expected.Version.Length,prepared.Output.Reviewed.Version.Length);
                Assert.Equal(expected.Origin.Digest,prepared.Output.Reviewed.Origin.Digest);
                Assert.Equal(expected.MemberModified,prepared.Output.Reviewed.MemberModified);
                Assert.False(destination.Stage.CanWriteForTest());
                using var input=prepared.Output.File.OpenReadView(prepared.Output.Reviewed,ct);using var data=new MemoryStream();input.CopyTo(data);
                Assert.Equal(bytes,data.ToArray());
                if(scenario=="origin")Assert.Equal(ArchiveOriginState.Present,prepared.Output.Reviewed.Origin.State);
            }
            if(scenario=="preexisting-stage-data")
            {var held=destination.Stage.Observe(ct);using var input=destination.Stage.OpenReadView(held,ct);using var data=new MemoryStream();input.CopyTo(data);Assert.Equal("keep"u8.ToArray(),data.ToArray());}
            Assert.False(File.Exists(Path.Combine(folder,"notes.txt"))); // Preparation never publishes the selected name.
            Assert.Equal(scenario=="late-save"?"later editor save stays local":System.Text.Encoding.UTF8.GetString(bytes),File.ReadAllText(record.WorkingPath));
        }
        finally
        {
            try{other?.Dispose();}finally{try{destination?.Dispose();}finally{if(copy is not null)Assert.True(Record.Exception(copy.Dispose) is null or IOException);}}
            if(!Path.GetFullPath(root).StartsWith(Path.TrimEndingDirectorySeparator(parent)+Path.DirectorySeparatorChar,StringComparison.Ordinal)||
                !Path.GetFileName(root).StartsWith("filecat-edit-copy-stage-",StringComparison.Ordinal))throw new IOException("Unexpected edit-copy fixture root.");
            Directory.Delete(root,true);
        }
    }
    private static bool CanWriteForTest(this IEditSaveCopyStage stage)=>stage is Stream stream&&stream.CanWrite;
    private sealed class BadSource(IArchiveRetainedFile source):IArchiveRetainedFile
    {
        public string ObjectIdentity=>source.ObjectIdentity;
        public ArchiveFileObservation Observe(CancellationToken ct)=>source.Observe(ct);
        public void Review(ArchiveFileObservation expected,CancellationToken ct)=>source.Review(expected,ct);
        public Stream OpenReadView(ArchiveFileObservation expected,CancellationToken ct)=>new BadRead(source.OpenReadView(expected,ct));
    }
    private sealed class BadStage(IEditSaveCopyStage stage):IEditSaveCopyStage
    {
        public string ObjectIdentity=>stage.ObjectIdentity;
        public ArchiveFileObservation Observe(CancellationToken ct)=>stage.Observe(ct);
        public void Review(ArchiveFileObservation expected,CancellationToken ct)=>stage.Review(expected,ct);
        public Stream OpenReadView(ArchiveFileObservation expected,CancellationToken ct)=>new BadRead(stage.OpenReadView(expected,ct));
        public string ReviewCreation(ArchiveArtifactReservation reservation,CancellationToken ct)=>stage.ReviewCreation(reservation,ct);
        public Stream BeginEvidence(ArchiveArtifactReservation reservation,CancellationToken ct)=>stage.BeginEvidence(reservation,ct);
        public ArchiveFileObservation SealEvidence(CancellationToken ct)=>stage.SealEvidence(ct);
        public void StopWrites()=>stage.StopWrites();
        public Stream BeginRebuild(long length,CancellationToken ct)=>stage.BeginRebuild(length,ct);
        public ArchiveFileObservation SealForVerification(CancellationToken ct)=>stage.SealForVerification(ct);
        public ArchiveFileObservation ApplyEditMetadata(ArchiveFileObservation copied,ArchiveFileObservation source,CancellationToken ct)=>stage.ApplyEditMetadata(copied,source,ct);
        public void Dispose()=>throw new InvalidOperationException("Borrowed stage views do not own native disposal.");
    }
    private sealed class BadRead(Stream input):Stream
    {
        private bool changed;
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>input.Length;public override long Position{get=>input.Position;set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
        public override int Read(Span<byte> buffer){int n=input.Read(buffer);if(n>0&&!changed){buffer[0]^=1;changed=true;}return n;}
        protected override void Dispose(bool disposing){if(disposing)input.Dispose();base.Dispose(disposing);}
        public override void Flush()=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
