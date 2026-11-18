using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Archives;
using FileCat.Remote.Sftp;
using FileCat.Remote.Tests;

namespace FileCat.App.Tests;

[CollectionDefinition("Retained SFTP edits",DisableParallelization=true)]
public sealed class RetainedSftpEditCollection;

[Collection("Retained SFTP edits")]
public sealed class SftpRetainedEditTests
{
    public static IEnumerable<object[]> Cases()=>new[]{"regular","empty","large","late-save","source-changed","directory-replaced",
        "disposed","wrong-source","wrong-profile","wrong-folder","wrong-name","move","multi-source","local-destination",
        "missing-digest","stale-target-metadata","stale-target-hash","concurrent-target","corrupt-source","corrupt-server",
        "cancel","retry","fallback","snapshot-efs"}.Select(value=>new object[]{value});

    [Theory,MemberData(nameof(Cases))]
    public async Task Native_edit_uploads_verify_retained_bytes_and_the_originating_destination(string scenario)
    {
        if (scenario == "snapshot-efs" && (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") != "1"))
            Assert.Skip("Requires the Windows EFS qualification fixture.");
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string root=Directory.CreateDirectory(Path.Combine(parent,"filecat-retained-sftp-"+Guid.NewGuid().ToString("N"))).FullName;
        EditCommitCopy? copy=null;Job? active=null;SftpConnections? connections=null;
        try
        {
            IFileSystemOperations native=OperatingSystem.IsWindows()?new WindowsFileOperations():new UnixFileOperations();
            Func<string,bool,CancellationToken,EditDirectoryLease> open=OperatingSystem.IsWindows()?WindowsEditDirectoryLease.OpenRoot:UnixEditDirectoryLease.OpenRoot;
            var store=new EditSessionStore(Path.Combine(root,"sessions"),native,open);
            var server=new FakeSftpServer();var target=server.File("/up/notes.txt","original remote bytes");
            var profile=new RemoteProfile{Name="Owned test",Host="files.example",User="user"};
            var interaction=new ScriptedInteraction{HostKeyAnswer=HostKeyDecision.AcceptOnce};
            for(int i=0;i<20;i++)interaction.Secrets.Enqueue("secret");
            connections=new SftpConnections(id=>id==profile.Id?profile:null,new FakeConnector(server),
                new HostKeyTrust(Path.Combine(root,"known_hosts"),[]),new SessionSecretStore(),interaction);
            var sftp=new SftpProvider(connections,()=>[profile],p=>p);
            var providers=new ProviderRegistry();providers.Register(new LocalFileSystemProvider());providers.Register(sftp);SftpJobs.Register();
            using var original=new MemoryContentSource("notes.txt",target.Data);
            var record=store.CreateRemote(profile.Id,"Owned test","/up/notes.txt",original,original.GetRevision()!.Value,null,ct);
            byte[] bytes=scenario=="empty"?[]:scenario is "large" or "retry" or "snapshot-efs"?Enumerable.Range(0,2097169).Select(i=>(byte)(i*31)).ToArray():"reviewed immutable edit"u8.ToArray();
            File.WriteAllBytes(record.WorkingPath,bytes);copy=store.PrepareCommit(record,root,ct);
            IRetainedEditUpload input=store.RemoteCommitInput(record,copy)!;
            var expected=new ContentRevision(target.Data.Length,target.Modified.Ticks);
            string expectedHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(target.Data));
            bool refuse=scenario is "disposed" or "wrong-source" or "wrong-profile" or "wrong-folder" or "wrong-name" or "move" or
                "multi-source" or "local-destination" or "missing-digest" or "stale-target-metadata" or "stale-target-hash" or
                "concurrent-target" or "corrupt-source" or "corrupt-server";
            Exception? encryptionRejected = null;
            bool encryptionAttempted = false;
            if (scenario == "snapshot-efs")
            {
                server.DuringUpload = () =>
                {
                    if (encryptionAttempted) return;
                    encryptionAttempted = true;
                    encryptionRejected = Record.Exception(() => File.Encrypt(copy.Path));
                };
            }
            if(scenario=="late-save")File.WriteAllText(record.WorkingPath,"later editor save stays local");
            if(scenario=="disposed")Assert.True(Record.Exception(copy.Dispose) is null or IOException);
            if(scenario is "source-changed" or "directory-replaced")
            {
                var error=Record.Exception(()=>
                {
                    if(scenario=="source-changed")File.WriteAllText(copy.Path,"changed captured bytes");
                    else{string folder=Path.GetDirectoryName(copy.Path)!;Directory.Move(folder,folder+".retained");Directory.CreateDirectory(folder);File.WriteAllBytes(copy.Path,bytes);}
                });
                if(error is null)refuse=true;else Assert.True(OperatingSystem.IsWindows()&&error is IOException or UnauthorizedAccessException,error.ToString());
            }
            if(scenario=="stale-target-metadata")target.Modified=target.Modified.AddSeconds(5);
            if(scenario=="stale-target-hash")target.Data[0]^=1;
            if(scenario=="concurrent-target")server.DuringUpload=()=>target.Data[0]^=1;
            if(scenario=="corrupt-source")input=new InconsistentInput(input);
            if(scenario=="corrupt-server")server.CorruptAt=0;
            if(scenario=="fallback")server.PosixRename=false;
            if(scenario=="retry")server.DropAfterBytes=700000;
            byte[] unchangedTarget=target.Data.ToArray();
            var source=ItemRef.ForFileSystemPath(scenario=="wrong-source"?record.WorkingPath:copy.Path,EntryKind.File);
            var request=new JobRequest
            {
                Kind=scenario=="move"?JobKind.Move:JobKind.Copy,
                Sources=scenario=="multi-source"?[source,source]:[source],
                Destination=scenario=="local-destination"?Location.FileSystem(root):new Location(Schemes.Sftp,
                    scenario=="wrong-folder"?"/elsewhere":"/up",session:scenario=="wrong-profile"?"another-profile":profile.Id),
                NewName=scenario=="wrong-name"?"redirected.txt":"notes.txt",ExpectedTarget=expected,
                ExpectedTargetSha256=scenario=="missing-digest"?null:expectedHash,EditUpload=input,
                Options=new TransferOptions{Conflicts=ConflictPolicy.Skip},
            };
            var guarded=new GuardedOps(native,Path.GetDirectoryName(copy.Path)!);
            var jobs=new JobManager(guarded,providers,Path.Combine(root,"journal"));
            active=scenario=="cancel"?jobs.SubmitWithRegistration(request,job=>job.Cancel()):jobs.Submit(request);
            var clock=System.Diagnostics.Stopwatch.StartNew();int retries=0;
            while(!active.State.IsFinished()&&clock.Elapsed<TimeSpan.FromSeconds(45))
            {
                if(active.Decision is {Task.IsCompleted:false} decision)
                {
                    if(scenario=="retry"&&retries++==0){server.Down=false;decision.Resolve(new Decision(DecisionAction.Retry));}
                    else decision.Resolve(new Decision(DecisionAction.Skip));
                }
                await Task.Delay(10,ct);
            }
            Assert.True(active.State.IsFinished(),"Retained SFTP edit did not finish.");
            TestContext.Current.TestOutputHelper?.WriteLine(active.State+": "+active.Summary+" "+JobIssue.Summarize(active.Issues));
            Assert.Equal(0,guarded.PathReads);
            if(scenario=="cancel"){Assert.Equal(JobState.Canceled,active.State);Assert.Equal(unchangedTarget,server.Lookup("/up/notes.txt",false)!.Data);}
            else if(refuse)
            {
                Assert.True(active.State is JobState.Failed or JobState.CompletedWithIssues,active.State.ToString());
                if(scenario=="concurrent-target")unchangedTarget[0]^=1;
                Assert.Equal(unchangedTarget,server.Lookup("/up/notes.txt",false)!.Data);
            }
            else
            {
                Assert.Equal(JobState.Completed,active.State);Assert.Equal(bytes,server.Lookup("/up/notes.txt",false)!.Data);
                Assert.True(active.VerifyBytesDone>=2*bytes.LongLength);
                if(scenario=="retry"){Assert.Equal(1,retries);Assert.Empty(server.WritesAt);}
            }
            Assert.Equal(["notes.txt"],server.Lookup("/up",false)!.Children.Keys);
            if (scenario == "snapshot-efs")
            {
                const int efsInTransaction = unchecked((int)0x80071AAF), sharingViolation = unchecked((int)0x80070020),
                    transactionConflict = unchecked((int)0x80071A90);
                Assert.True(encryptionAttempted);
                Assert.True(encryptionRejected is IOException { HResult: efsInTransaction or sharingViolation or transactionConflict }, encryptionRejected?.ToString());
                Assert.False(File.GetAttributes(copy.Path).HasFlag(FileAttributes.Encrypted));
            }
            Assert.Equal(scenario=="late-save"?"later editor save stays local":System.Text.Encoding.UTF8.GetString(bytes),File.ReadAllText(record.WorkingPath));
            connections.Dispose(); // A returned lease remains connected in the reusable pool until the owner closes it.
            Assert.Equal(0,server.ActiveChannels);
        }
        finally
        {
            if(active is {} job&&!job.State.IsFinished())job.Cancel();
            connections?.Dispose();if(copy is not null)Assert.True(Record.Exception(copy.Dispose) is null or IOException);
            if(!Path.GetFullPath(root).StartsWith(Path.TrimEndingDirectorySeparator(parent)+Path.DirectorySeparatorChar,StringComparison.Ordinal)||
                !Path.GetFileName(root).StartsWith("filecat-retained-sftp-",StringComparison.Ordinal))throw new IOException("Unexpected SFTP fixture root.");
            Directory.Delete(root,true);
        }
    }

    private sealed class GuardedOps(IFileSystemOperations native,string captured):PortableFileOperations
    {
        internal int PathReads;
        public override FileSystemItemInfo? TryGetInfo(string path)
        {if(path.StartsWith(captured,StringComparison.Ordinal)){PathReads++;throw new IOException("Retained edit metadata must not reopen its pathname.");}return native.TryGetInfo(path);}
        public override IDisposable HoldRegularFile(string path,string? root=null)
        {PathReads++;throw new IOException("Retained edit input must not reopen its pathname.");}
    }
    private sealed class InconsistentInput(IRetainedEditUpload input):IRetainedEditUpload
    {
        public string ObjectIdentity=>input.ObjectIdentity;
        public void RequireRequest(JobRequest request)=>input.RequireRequest(request);
        public ArchiveFileObservation Observe(CancellationToken ct)=>input.Observe(ct);
        public void Review(ArchiveFileObservation expected,CancellationToken ct)=>input.Review(expected,ct);
        public Stream OpenReadView(ArchiveFileObservation expected,CancellationToken ct)=>new CorruptStream(input.OpenReadView(expected,ct));
    }
    private sealed class CorruptStream(Stream input):Stream
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
