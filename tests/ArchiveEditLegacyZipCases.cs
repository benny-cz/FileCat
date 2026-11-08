using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Tests;

internal static class ArchiveEditLegacyZipCases
{
    internal static IEnumerable<object[]> Scenarios()
    {
        foreach(string scenario in new[]{"regular","empty","large","origin","late-save","rebase","stale-target","source-changed",
            "source-directory-replaced","disposed","wrong-member","wrong-archive","wrong-source","move","cancel","inconsistent-read",
            "duplicate-member","missing-member"})yield return [scenario];
    }
    internal static async Task Run(string scenario,Func<string,bool,CancellationToken,EditDirectoryLease> open,IFileSystemOperations native)
    {
        var ct=TestContext.Current.CancellationToken;string parent=Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath();
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        parent=Path.GetFullPath(parent);Directory.CreateDirectory(parent);
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-native-zip-edit-"+Guid.NewGuid().ToString("N"))).FullName;
        EditCommitCopy? copy=null;Job? active=null;
        try
        {
            string archive=Path.Combine(fixture,"original.zip");
            using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
            {
                zip.Comment="preserve archive comment";
                var member=zip.CreateEntry("notes.txt");member.Comment="preserve member comment";member.ExternalAttributes=0x20;
                using(var writer=new StreamWriter(member.Open()))writer.Write("original member");
                using(var writer=new StreamWriter(zip.CreateEntry("untouched.txt").Open()))writer.Write("untouched member");
            }
            string? mark=null;
            if(scenario=="origin")
            {
                Assert.True(native.WriteOriginMark(archive,"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/owned.zip\r\nQuarantine=0081;6553f100;FileCat;public-test\r\n"));
                mark=native.ReadOriginMark(archive);Assert.NotNull(mark);
            }
            var providers=new ProviderRegistry();providers.Register(new LocalFileSystemProvider());
            var reader=new ZipProvider(Path.Combine(fixture,"read-cache"));providers.Register(reader);
            var store=new EditSessionStore(Path.Combine(fixture,"sessions"),native,open);
            var location=reader.GetContainerLocation(archive)!;var sink=new Sink();await reader.EnumerateAsync(location,sink,ct);
            var record=store.Create(reader,reader.GetItemRef(location,sink.Entries.Single(entry=>entry.Name=="notes.txt")),ct);
            Assert.Equal(EditSessionRecord.NativeStorageVersion,record.Version);Assert.True(record.IsLegacyZip);
            byte[] bytes=scenario=="empty"?[]:scenario=="large"?Enumerable.Range(0,2097161).Select(value=>(byte)(value*31)).ToArray():"reviewed immutable edit"u8.ToArray();
            File.WriteAllBytes(record.WorkingPath,bytes);copy=store.PrepareCommit(record,fixture,ct);
            if(scenario is "rebase" or "duplicate-member" or "missing-member")
            {
                using var changed=ZipFile.Open(archive,ZipArchiveMode.Update);
                if(scenario=="missing-member")changed.GetEntry("notes.txt")!.Delete();
                else{using var writer=new StreamWriter(changed.CreateEntry(scenario=="duplicate-member"?"notes.txt":"later.txt").Open());writer.Write("concurrent independent addition");}
            }
            var review=store.ReviewCommit(record,ct);Assert.Equal(scenario is "duplicate-member" or "missing-member"?CommitCheck.MemberChanged:
                scenario=="rebase"?CommitCheck.ArchiveChanged:CommitCheck.Ready,review.Check);
            var plan=store.CommitPlan(record,review,copy);Assert.Same(copy.Native,plan.EditInput);
            bool refuse=scenario is "disposed" or "wrong-member" or "wrong-archive" or "wrong-source" or "move" or "stale-target" or "inconsistent-read" or "duplicate-member" or "missing-member";
            if(scenario=="late-save")File.WriteAllText(record.WorkingPath,"later editor save stays uncommitted");
            else if(scenario=="disposed")Assert.True(Record.Exception(copy.Dispose) is null or IOException);
            else if(scenario is "source-changed" or "source-directory-replaced")
            {
                var error=Record.Exception(()=>
                {
                    if(scenario=="source-changed")File.WriteAllText(copy.Path,"different captured bytes");
                    else{string folder=Path.GetDirectoryName(copy.Path)!;Directory.Move(folder,folder+".retained");Directory.CreateDirectory(folder);File.WriteAllBytes(copy.Path,bytes);}
                });
                if(error is null)refuse=true;else Assert.True(OperatingSystem.IsWindows()&&error is IOException or UnauthorizedAccessException,error.ToString());
            }
            else if(scenario=="wrong-member")plan=plan with{Changes=[plan.Changes[0] with{MemberPath="untouched.txt"}]};
            else if(scenario=="wrong-archive")plan=plan with{ArchivePath=Path.Combine(fixture,"unrelated.zip")};
            else if(scenario=="wrong-source")plan=plan with{Changes=[plan.Changes[0] with{SourcePath=record.WorkingPath}]};
            else if(scenario=="move")plan=plan with{MoveSources=true};
            else if(scenario=="inconsistent-read")plan=plan with{EditInput=new InconsistentInput(plan.EditInput!)};
            else if(scenario=="stale-target")
            {using var changed=ZipFile.Open(archive,ZipArchiveMode.Update);using var writer=new StreamWriter(changed.CreateEntry("later.txt").Open());writer.Write("later target version");}
            byte[] before=File.ReadAllBytes(archive);
            var guarded=new GuardedInputs(native);var jobs=new JobManager(guarded,providers,Path.Combine(fixture,"journal"));
            var request=new JobRequest{Kind=JobKind.ArchiveUpdate,Archive=plan};
            active=scenario=="cancel"?jobs.SubmitWithRegistration(request,job=>job.Cancel()):jobs.Submit(request);
            var clock=System.Diagnostics.Stopwatch.StartNew();
            while(!active.State.IsFinished()&&clock.Elapsed<TimeSpan.FromSeconds(30))await Task.Delay(10,ct);
            Assert.True(active.State.IsFinished(),"Owned native ZIP edit did not finish.");
            TestContext.Current.TestOutputHelper?.WriteLine(active.State+": "+active.Summary+" "+JobIssue.Summarize(active.Issues));
            Assert.Equal(0,guarded.PathInputOpens);
            if(scenario=="cancel"){Assert.Equal(JobState.Canceled,active.State);Assert.Equal(before,File.ReadAllBytes(archive));}
            else if(refuse){Assert.Equal(JobState.Failed,active.State);Assert.Equal(before,File.ReadAllBytes(archive));}
            else
            {
                Assert.Equal(JobState.Completed,active.State);
                using(var zip=ZipFile.OpenRead(archive))
                {
                    using(var input=zip.GetEntry("notes.txt")!.Open()){using var data=new MemoryStream();input.CopyTo(data);Assert.Equal(bytes,data.ToArray());}
                    using(var input=new StreamReader(zip.GetEntry("untouched.txt")!.Open()))Assert.Equal("untouched member",input.ReadToEnd());
                    Assert.Equal("preserve archive comment",zip.Comment);Assert.Equal("preserve member comment",zip.GetEntry("notes.txt")!.Comment);
                    Assert.Equal(0x20,zip.GetEntry("notes.txt")!.ExternalAttributes);
                    if(scenario=="rebase")Assert.NotNull(zip.GetEntry("later.txt"));
                }
                Assert.Equal(mark,native.ReadOriginMark(archive));
                var updated=store.Committed(record,copy);Assert.Equal(1,updated.StorageRevision);
                Assert.Equal(scenario=="late-save"?EditState.Modified:EditState.Unchanged,store.StateOf(updated));
            }
            Assert.Equal(scenario=="late-save"?"later editor save stays uncommitted":System.Text.Encoding.UTF8.GetString(bytes),File.ReadAllText(record.WorkingPath));
            Assert.Empty(Directory.GetFiles(fixture,".filecat-zip-*.tmp"));
        }
        finally
        {
            if(active is { } job&&!job.State.IsFinished())job.Cancel();
            if(copy is not null)Assert.True(Record.Exception(copy.Dispose) is null or IOException);
            if(!Path.GetFullPath(fixture).StartsWith(Path.TrimEndingDirectorySeparator(parent)+Path.DirectorySeparatorChar,StringComparison.Ordinal)||
                !Path.GetFileName(fixture).StartsWith("filecat-native-zip-edit-",StringComparison.Ordinal))throw new IOException("Unexpected native ZIP fixture root.");
            Directory.Delete(fixture,true);
        }
    }
    private sealed class Sink:IEnumerationSink
    {
        internal readonly List<EntryData> Entries=[];
        public void AddBatch(ReadOnlySpan<EntryData> entries){foreach(var entry in entries)Entries.Add(entry);}
        public void ReportIssue(string message)=>throw new IOException(message);
    }
    private sealed class GuardedInputs(IFileSystemOperations native):PortableFileOperations
    {
        internal int PathInputOpens;
        public override IDisposable HoldRegularFile(string path,string? root=null)
        {PathInputOpens++;throw new IOException("A retained ZIP edit must not reopen its input pathname.");}
        public override string? GetFileIdentity(string path)=>native.GetFileIdentity(path);
        public override string? ReadOriginMark(string path)=>native.ReadOriginMark(path);
        public override bool WriteOriginMark(string path,string mark)=>native.WriteOriginMark(path,mark);
        public override FileSystemItemInfo? TryGetInfo(string path)=>native.TryGetInfo(path);
    }
    private sealed class InconsistentInput(IArchiveOwnedEditInput input):IArchiveOwnedEditInput
    {
        public string SourcePath=>input.SourcePath;
        public string ObjectIdentity=>input.ObjectIdentity;
        public void RequireRequest(ArchivePlan request)=>input.RequireRequest(request);
        public IArchiveSelectionTree OpenTree(string parent,CancellationToken ct)=>input.OpenTree(parent,ct);
        public ArchiveFileObservation Observe(CancellationToken ct)=>input.Observe(ct);
        public void Review(ArchiveFileObservation expected,CancellationToken ct)=>input.Review(expected,ct);
        public Stream OpenReadView(ArchiveFileObservation expected,CancellationToken ct)=>new InconsistentView(input.OpenReadView(expected,ct));
    }
    private sealed class InconsistentView(Stream input):Stream
    {
        private bool changed;
        public override bool CanRead=>input.CanRead;
        public override bool CanSeek=>input.CanSeek;
        public override bool CanWrite=>false;
        public override long Length=>input.Length;
        public override long Position{get=>input.Position;set=>input.Position=value;}
        public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
        public override int Read(Span<byte> buffer)
        {int count=input.Read(buffer);if(count>0&&!changed){buffer[0]^=1;changed=true;}return count;}
        public override long Seek(long offset,SeekOrigin origin)=>input.Seek(offset,origin);
        public override void Flush()=>input.Flush();
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){if(disposing)input.Dispose();base.Dispose(disposing);}
    }
}
