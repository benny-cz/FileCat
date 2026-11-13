using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Tests;

internal static class ArchiveEditDiscardCases
{
    internal static IEnumerable<object[]> Scenarios()=>new[]{"working","empty","missing","revision","record-time","origin","mapped-editor",
        "complete-capture","old-capture","interrupted-empty","interrupted-partial","saved-intent","live-capture","open-editor",
        "cancel-before","cancel-preflight","cancel-partial","late-working","wrong-review","unavailable-review","stale-record",
        "record-changed","record-missing","marker-changed","marker-unknown","marker-origin","extra-session","extra-working",
        "extra-capture","extra-payload","capture-header","capture-receipt","capture-payload","intent-corrupt",
        "replaced-session","replaced-files","replaced-after-review","changed-after-review","extra-after-review","no-factory",
        "stage-session","aside-working","aside-missing"}
        .Select(value=>new object[]{value});

    internal static void Run(string scenario,Func<string,bool,CancellationToken,EditDirectoryLease> open,IFileSystemOperations fs)
    {
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string fixture=Private(Path.Combine(parent,"filecat-edit-discard-"+Guid.NewGuid().ToString("N")));
        EditCommitCopy? copy=null;FileStream? editor=null;
        System.IO.MemoryMappedFiles.MemoryMappedFile? mapping=null;
        System.IO.MemoryMappedFiles.MemoryMappedViewAccessor? view=null;
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var hook=new Faults(scenario,stop);bool refuse=false;
        EditDirectoryLease Factory(string path,bool priv,CancellationToken token)
        {if(refuse)throw new IOException("Retain this test capture without removing its artifacts.");return new FaultLease(open(path,priv,token),hook);}
        try
        {
            var store=new EditSessionStore(Path.Combine(fixture,"sessions"),fs,Factory);
            using var source=new MemoryContentSource("owned.txt",scenario=="empty"?[]:"complete reviewed working contents"u8.ToArray());
            var record=store.CreateRemote("test","test","/owned.txt",source,source.GetRevision()!.Value,null,ct);
            hook.Working=record.WorkingPath;
            string session=store.SessionDirectory(record),capture="";
            bool hasCapture=scenario.Contains("capture",StringComparison.Ordinal)||scenario is "saved-intent" or "intent-corrupt" or
                "interrupted-empty" or "interrupted-partial" or "extra-payload";
            if(hasCapture)
            {
                copy=store.PrepareCommit(record,fixture,ct);capture=Path.GetDirectoryName(Path.GetDirectoryName(copy.Path))!;
                if(scenario is "saved-intent" or "intent-corrupt")
                {
                    var native=copy.Native!;var observed=native.Expected;var modified=observed.MemberModified!.Value;Guid nonce=Guid.NewGuid();
                    string prefix=".filecat-archive-"+nonce.ToString("N")+"-";
                    File.WriteAllText(Path.Combine(fixture,"saved.txt"),"keep this separate saved file");
                    native.AttachSavedCopy(new(1,nonce,record.Id,record.StoredRecordSha256,native.CaptureIdentity,
                        observed.Version.Identity,observed.Version.Length,observed.Version.Sha256,observed.MetadataDigest,observed.Origin.Digest,
                        modified.Seconds,modified.Nanoseconds,fixture,"destination-identity","saved.txt",prefix+"data.stage",prefix+"ready.manifest",prefix+"outcome.manifest"),ct);
                }
                if(scenario!="live-capture")
                {
                    refuse=true;try{Assert.IsAssignableFrom<IOException>(Record.Exception(copy.Dispose));}finally{refuse=false;}
                    copy=null;
                }
                if(scenario is "interrupted-empty" or "interrupted-partial")
                {
                    File.Delete(Path.Combine(capture,"captured.json"));
                    if(scenario=="interrupted-empty")File.Delete(Path.Combine(capture,"files","owned.txt"));
                    else File.WriteAllText(Path.Combine(capture,"files","owned.txt"),"partial capture bytes");
                }
                if(scenario=="old-capture")record=store.Save(record with{LastCommitUtc=DateTime.UtcNow});
                if(scenario=="extra-capture")File.WriteAllText(Path.Combine(capture,"unexpected.txt"),"keep unexpected capture child");
                if(scenario=="extra-payload")File.WriteAllText(Path.Combine(capture,"files","unexpected.txt"),"keep unexpected payload");
                if(scenario=="capture-header")Change(Path.Combine(capture,"capture.json"),"Files","different-directory");
                if(scenario=="capture-receipt")Change(Path.Combine(capture,"captured.json"),"Sha256",new string('A',64));
                if(scenario=="capture-payload")File.WriteAllText(Path.Combine(capture,"files","owned.txt"),"keep changed captured bytes");
                if(scenario=="intent-corrupt")File.WriteAllText(Path.Combine(capture,EditSaveCopyIntent.FileName),"{");
            }
            if(scenario=="revision")record=store.Save(record with{LastCommitUtc=DateTime.UtcNow});
            if(scenario=="stale-record")store.Save(record with{LastCommitUtc=DateTime.UtcNow});
            if(scenario is "missing" or "aside-missing")File.Delete(record.WorkingPath);
            if(scenario=="origin")Assert.True(fs.WriteOriginMark(record.WorkingPath,"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/edit\r\nQuarantine=0081;6553f100;FileCat;public-test\r\n"));
            if(scenario=="record-time")File.SetLastWriteTimeUtc(Path.Combine(session,"session.json"),DateTime.UnixEpoch.AddSeconds(1700000000));
            bool gone=scenario is "missing" or "aside-missing";
            var reviewed=new EditWorkingReview(gone?EditState.Missing:EditState.Modified,
                gone?null:Convert.ToHexString(SHA256.HashData(Read(record.WorkingPath))));
            if(scenario=="open-editor")editor=new FileStream(record.WorkingPath,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete);
            if(scenario=="mapped-editor")
            {
                using var mappedFile=new FileStream(record.WorkingPath,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete);
                mapping=System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(mappedFile,null,0,
                    System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite,HandleInheritability.None,leaveOpen:true);
                view=mapping.CreateViewAccessor(0,0,System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite);
                // Retain writable pages after the original file handle closes, as an editor can.
            }
            if(scenario=="late-working")File.WriteAllText(record.WorkingPath,"keep the later editor save");
            if(scenario=="wrong-review")reviewed=reviewed with{Sha256=new string('A',64)};
            if(scenario=="unavailable-review")reviewed=new(EditState.Unavailable,null);
            if(scenario=="record-changed")Change(Path.Combine(session,"session.json"),"ServerDisplay","changed state");
            if(scenario=="record-missing")File.Delete(Path.Combine(session,"session.json"));
            if(scenario=="marker-changed")Change(Path.Combine(session,"creation.json"),"WorkingPath",Path.Combine(fixture,"another.txt"));
            if(scenario=="marker-unknown")Change(Path.Combine(session,"creation.json"),"Unknown",true);
            // With a source URL, which Linux records (a zone alone has no Linux origin value).
            if(scenario=="marker-origin")Assert.True(fs.WriteOriginMark(Path.Combine(session,"creation.json"),"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/marker\r\n"));
            if(scenario=="extra-session")File.WriteAllText(Path.Combine(session,"unexpected.txt"),"keep unexpected session data");
            // FileCat's own interrupted work, which a discard reviews and removes with the edit: the stage of a record it
            // was publishing when it ended, and a name an interrupted exact removal set aside (with the copy still there,
            // or gone with it). Written as FileCat writes its own files, which is what its removal review requires.
            if(scenario=="stage-session")Own(Path.Combine(session,".record-"+Guid.NewGuid().ToString("N")+".tmp"),"a revision this edit was publishing");
            if(scenario is "aside-working" or "aside-missing")
                Own(Path.Combine(Path.GetDirectoryName(record.WorkingPath)!,".filecat-removing-"+Guid.NewGuid().ToString("N")),"what a stopped removal set aside");
            if(scenario=="extra-working")File.WriteAllText(Path.Combine(Path.GetDirectoryName(record.WorkingPath)!,"editor.bak"),"keep editor backup");
            if(scenario=="replaced-session")
            {Directory.Move(session,session+".retained");File.WriteAllText(Path.Combine(Private(session),"replacement.txt"),"keep unrelated replacement");}
            if(scenario=="replaced-files")
            {string path=Path.GetDirectoryName(record.WorkingPath)!;Directory.Move(path,path+".retained");File.WriteAllText(Path.Combine(Private(path),"owned.txt"),"keep unrelated replacement");}
            if(scenario=="cancel-before")stop.Cancel();
            int checks=0;
            void Check(){if(scenario=="cancel-preflight"&&++checks==2)stop.Cancel();}
            hook.Armed=true;EditDiscardResult? result=null;var beforeAll=Snapshot();
            var selected=scenario=="no-factory"?new EditSessionStore(store.Root,fs):store;
            var error=Record.Exception(()=>result=selected.DiscardReviewed(record,reviewed,stop.Token,Check));
            TestContext.Current.TestOutputHelper?.WriteLine(error?.ToString()??result?.ToString()??"No discard result.");
            // Windows' file sharing and Linux's write lease see an editor that holds or maps the working copy; macOS has
            // neither (its leases need a privilege), so there the discard proceeds and the editor keeps its unlinked copy.
            bool seesEditors=!OperatingSystem.IsMacOS();
            bool success=scenario is "working" or "empty" or "missing" or "revision" or "record-time" or "origin" or "complete-capture" or
                "old-capture" or "interrupted-empty" or "interrupted-partial" or "saved-intent" or "stage-session" or "aside-working" or
                "aside-missing"||!seesEditors&&scenario is "open-editor" or "mapped-editor";
            if(success)
            {
                Assert.Null(error);Assert.NotNull(result);Assert.True(result.Complete,result.Issue);Assert.False(Directory.Exists(session));
                if(scenario=="saved-intent")Assert.Equal("keep this separate saved file",File.ReadAllText(Path.Combine(fixture,"saved.txt")));
                // An editor's mapping of the discarded copy keeps working (no truncation under it).
                if(scenario=="mapped-editor"){view!.Write(0,(byte)'x');view.Flush();Assert.Equal((byte)'x',view.ReadByte(0));}
                Assert.Empty(store.LoadAll());
            }
            else if(scenario=="mapped-editor")
            {
                Assert.True(error is IOException or UnauthorizedAccessException||error is null&&result is {Complete:false},error?.ToString()??result?.ToString());
                Assert.Equal(beforeAll,Snapshot());view!.Write(0,(byte)'x');view.Flush();Assert.Equal((byte)'x',Read(record.WorkingPath)[0]);
            }
            else if(scenario is "cancel-partial" or "extra-after-review")
            {
                Assert.Null(error);Assert.NotNull(result);Assert.False(result.Complete);Assert.True(result.MayHaveRemovedFiles);
                Assert.Contains("Remaining files",result.Issue);Assert.False(File.Exists(record.WorkingPath));Assert.True(File.Exists(Path.Combine(session,"session.json")));
                if(scenario=="extra-after-review")Assert.Equal("keep late arrival",File.ReadAllText(Path.Combine(Path.GetDirectoryName(record.WorkingPath)!,"late.txt")));
            }
            else
            {
                Assert.True(error is IOException or UnauthorizedAccessException or NotSupportedException or OperationCanceledException||
                    error is null&&result is {Complete:false,MayHaveRemovedFiles:false},error?.ToString()??result?.ToString());
                if(scenario=="replaced-after-review")
                {Assert.Equal("keep replacement",File.ReadAllText(record.WorkingPath));Assert.Equal("complete reviewed working contents",File.ReadAllText(record.WorkingPath+".retained"));}
                else if(scenario=="changed-after-review")Assert.Equal("keep concurrent changes",File.ReadAllText(record.WorkingPath));
                else Assert.Equal(beforeAll,Snapshot());
                if(scenario=="marker-origin")Assert.NotNull(fs.ReadOriginMark(Path.Combine(session,"creation.json")));
            }
            view?.Dispose();view=null;mapping?.Dispose();mapping=null;
            editor?.Dispose();editor=null;if(copy is not null){Record.Exception(copy.Dispose);copy=null;}
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using(var root=open(store.Root,true,ct))using(EditStorageAdmission.Acquire(root,timeout.Token)) { }
            // All native parent owners must close even after refusal/partial deletion.
            Directory.Move(store.Root,store.Root+".checked");Directory.Move(store.Root+".checked",store.Root);

            string[] Snapshot()=>Directory.EnumerateFiles(fixture,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(path=>Path.GetRelativePath(fixture,path)+":"+Convert.ToHexString(SHA256.HashData(Read(path)))) .ToArray();
        }
        finally
        {
            hook.Armed=false;view?.Dispose();mapping?.Dispose();editor?.Dispose();if(copy is not null)Assert.True(Record.Exception(copy.Dispose) is null or IOException);
            if(!Path.GetFullPath(fixture).StartsWith(Path.TrimEndingDirectorySeparator(parent)+Path.DirectorySeparatorChar,StringComparison.Ordinal))
                throw new IOException("Unexpected native discard fixture root.");
            TestCleanup.DeleteTree(fixture);
        }
    }
    private static byte[] Read(string path)
    {using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var output=new MemoryStream();file.CopyTo(output);return output.ToArray();}
    private static void Change(string path,string key,JsonNode? value)
    {var record=JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();record[key]=value;File.WriteAllText(path,record.ToJsonString());}
    private static void Own(string path,string contents)
    {
        File.WriteAllText(path,contents);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    }
    private static string Private(string path)=>OperatingSystem.IsWindows()?Directory.CreateDirectory(path).FullName:
        Directory.CreateDirectory(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute).FullName;
    internal sealed class Faults(string scenario,CancellationTokenSource stop)
    {
        internal bool Armed,Injected;internal bool DiscardAvailable=true;internal string Working="";
        internal void OpenFile(string directory,string name)
        {
            if(!Armed||Injected||System.IO.Path.Combine(directory,name)!=Working)return;
            if(scenario=="replaced-after-review"){Injected=true;File.Move(Working,Working+".retained");File.WriteAllText(Working,"keep replacement");}
            if(scenario=="changed-after-review"){Injected=true;File.WriteAllText(Working,"keep concurrent changes");}
        }
        internal void RemovedFile(){if(Armed&&scenario=="cancel-partial")stop.Cancel();}
        internal void RemoveDirectory(string path)
        {
            if(Armed&&!Injected&&scenario=="extra-after-review"&&path==System.IO.Path.GetDirectoryName(Working))
            {Injected=true;File.WriteAllText(System.IO.Path.Combine(path,"late.txt"),"keep late arrival");}
        }
    }
    internal sealed class FaultLease(EditDirectoryLease owner,Faults faults):EditDirectoryLease
    {
        internal override string Path=>owner.Path;internal override string Identity=>owner.Identity;internal override SafeFileHandle Handle=>owner.Handle;
        private EditDirectoryLease Wrap(EditDirectoryLease child)=>new FaultLease(child,faults);
        internal override EditDirectoryLease CreateDirectory(string leaf,CancellationToken ct,bool removable=false)=>Wrap(owner.CreateDirectory(leaf,ct,removable));
        internal override EditDirectoryLease OpenDirectory(string leaf,string? id,CancellationToken ct)=>Wrap(owner.OpenDirectory(leaf,id,ct));
        internal override EditDirectoryLease OpenStorageDirectory(string leaf,CancellationToken ct)=>Wrap(owner.OpenStorageDirectory(leaf,ct));
        internal override bool CanDiscardReviewedSession=>faults.DiscardAvailable&&owner.CanDiscardReviewedSession;
        internal override EditDirectoryLease OpenDiscardDirectory(string leaf,string? id,CancellationToken ct)=>Wrap(owner.OpenDiscardDirectory(leaf,id,ct));
        internal override FileStream OpenDiscardFile(string leaf,string? id,CancellationToken ct)
        {faults.OpenFile(Path,leaf);return owner.OpenDiscardFile(leaf,id,ct);}
        internal override FileStream CreateFile(string leaf,CancellationToken ct)=>owner.CreateFile(leaf,ct);
        internal override FileStream OpenFile(string leaf,string? id,CancellationToken ct)=>owner.OpenFile(leaf,id,ct);
        internal override FileStream OpenWorkingFile(string leaf,CancellationToken ct)=>owner.OpenWorkingFile(leaf,ct);
        internal override FileStream OpenWorkingFileForReview(string leaf,CancellationToken ct)=>owner.OpenWorkingFileForReview(leaf,ct);
        internal override FileStream OpenSharedRecord(string leaf,CancellationToken ct)=>owner.OpenSharedRecord(leaf,ct);
        internal override void RequireRemovable(FileStream file,string leaf)=>owner.RequireRemovable(file,leaf);
        internal override ContentRevision ReviewWorkingFile(FileStream file,string leaf,CancellationToken ct)=>owner.ReviewWorkingFile(file,leaf,ct);
        internal override ArchiveOriginSnapshot ReadOrigin(FileStream file,string leaf,CancellationToken ct)=>owner.ReadOrigin(file,leaf,ct);
        internal override void CopyOrigin(FileStream file,string leaf,ArchiveOriginSnapshot origin,CancellationToken ct)=>owner.CopyOrigin(file,leaf,origin,ct);
        internal override void ReviewRemoval(FileStream file,string leaf,ArchiveFileObservation expected,CancellationToken ct)=>owner.ReviewRemoval(file,leaf,expected,ct);
        internal override void DeleteSnapshot(FileStream file,string leaf,ArchiveFileObservation expected,CancellationToken ct)
        {owner.DeleteSnapshot(file,leaf,expected,ct);faults.RemovedFile();}
        internal override bool CanRemoveOwnedDirectories=>owner.CanRemoveOwnedDirectories;
        internal override void RemoveEmpty(CancellationToken ct){faults.RemoveDirectory(Path);owner.RemoveEmpty(ct);}
        internal override IReadOnlyList<string> Names(CancellationToken ct)=>owner.Names(ct);
        internal override IReadOnlyList<string> Names(CancellationToken ct,int max)=>owner.Names(ct,max);
        internal override EditStoredFileSize MeasureStoredFile(string leaf,CancellationToken ct)=>owner.MeasureStoredFile(leaf,ct);
        internal override EditStoredDirectorySize MeasureStoredDirectory(CancellationToken ct)=>owner.MeasureStoredDirectory(ct);
        internal override FileStream OpenStorageGate(CancellationToken ct)=>owner.OpenStorageGate(ct);
        internal override bool TryLockStorageGate(FileStream file,CancellationToken ct)=>owner.TryLockStorageGate(file,ct);
        internal override FileSystemCapacitySnapshot? StorageCapacity(CancellationToken ct)=>owner.StorageCapacity(ct);
        internal override string Identify(SafeFileHandle file)=>owner.Identify(file);
        internal override void PublishNewFile(FileStream file,string source,string target,EditFileVersion expected,CancellationToken ct)=>owner.PublishNewFile(file,source,target,expected,ct);
        protected override void ReviewCore(CancellationToken ct)=>owner.Review(ct);
        protected override void CloseCore()=>owner.Dispose();
    }
}
