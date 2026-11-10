using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Tests;

[CollectionDefinition("Native edit snapshot reservations",DisableParallelization=true)]
public sealed class NativeEditSnapshotReservationCollection { }

internal static class ArchiveEditSnapshotCases
{
    internal static IEnumerable<object[]> Scenarios()
    {
        foreach(string value in new[]{"regular","empty","large","origin","late-save","save-copy-capture","cancel-before","cancel-copy",
            "stale-record","working-directory-replaced","snapshot-directory-replaced","snapshot-file-replaced","snapshot-bytes-changed",
            "snapshot-origin-changed","view-after-dispose","request-binding","cleanup-extra-file","cleanup-extra-capture-child",
            "cleanup-record-replaced","cleanup-record-bytes-changed","cleanup-record-origin-changed","cleanup-reused-path","capture-quota","capture-byte-quota"})yield return [value];
        foreach(var profile in ArchiveNativeJobCases.EditProfiles())yield return [$"plan-{(int)(ArchiveWriteFormat)profile[0]}-{profile[1]}"];
    }
    internal static void Run(string scenario,Func<string,bool,CancellationToken,EditDirectoryLease> open,IFileSystemOperations? nativeFiles=null)
    {
        var ct=TestContext.Current.CancellationToken;
        string parent=Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath();
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        parent=Path.GetFullPath(parent);Directory.CreateDirectory(parent);
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-edit-snapshot-"+Guid.NewGuid().ToString("N"))).FullName;
        EditCommitCopy? copy=null;string? capturePath=null;bool retainCapture=false;
        try
        {
            var store=new EditSessionStore(Path.Combine(fixture,"sessions"),nativeFiles??new UnixFileOperations(),open);
            var profile=new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
            if(scenario.StartsWith("plan-",StringComparison.Ordinal))
            {var parts=scenario.Split('-');profile=ArchiveNativeJobCases.Profile((ArchiveWriteFormat)int.Parse(parts[1]),int.Parse(parts[2]));}
            var manifest=Original(profile);var source=new ArchiveAssessment(manifest,new string('B',64),2048,new string('B',64));
            var target=new ArchiveTargetReview(Path.Combine(fixture,"target.zip"),profile,
                new(new("outer",2048,new string('B',64)),"outer-parent","target.zip",new string('C',64),
                    ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier),new(1700000000,0),0x180),source);
            var record=store.CreateArchive(new Source(target,store.Root,open),"owned.txt",ct);
            Assert.True(record.IsNativeArchive);Assert.Equal(EditSessionRecord.NativeStorageVersion,record.Version);
            byte[] bytes=scenario=="empty"?[]:Enumerable.Range(0,scenario is "large" or "cancel-copy"?2097153:73).Select(index=>(byte)(index*31)).ToArray();
            File.WriteAllBytes(record.WorkingPath,bytes);
            const string mark="[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/owned\r\nQuarantine=0081;6553f100;FileCat;public-test\r\n";
            if(scenario=="origin")Mark(record.WorkingPath,mark);
            DateTime time=DateTime.UnixEpoch.AddSeconds(1700000000).AddTicks(7);File.SetLastWriteTimeUtc(record.WorkingPath,time);
            string session=store.SessionDirectory(record);
            if(scenario=="stale-record")
            {
                store.Save(record with{LastCommitUtc=DateTime.UtcNow});
                Assert.ThrowsAny<IOException>(()=>store.PrepareCommit(record,fixture,ct));Assert.Empty(Directory.GetDirectories(session,"commit-*"));return;
            }
            if(scenario=="working-directory-replaced")
            {
                string files=Path.GetDirectoryName(record.WorkingPath)!;Directory.Move(files,files+".retained");Directory.CreateDirectory(files);
                var error=Record.Exception(()=>store.PrepareCommit(record,fixture,ct));
                Assert.True(error is IOException or UnauthorizedAccessException,error?.ToString());Assert.Empty(Directory.GetDirectories(session,"commit-*"));return;
            }
            if(scenario=="cancel-before")
            {
                using var stop=new CancellationTokenSource();stop.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(()=>store.PrepareCommit(record,fixture,stop.Token));Assert.Empty(Directory.GetDirectories(session,"commit-*"));return;
            }
            if(scenario=="cancel-copy")
            {
                int checks=0;Assert.ThrowsAny<OperationCanceledException>(()=>store.PrepareSaveCopy(record,fixture,new LocalFileSystemProvider(),
                    ()=>{if(++checks>=6)throw new OperationCanceledException();}));
                string capture=Assert.Single(Directory.GetDirectories(session,"commit-*"));
                Assert.True(File.Exists(Path.Combine(capture,"capture.json")));Assert.False(File.Exists(Path.Combine(capture,"captured.json")));
                Assert.Equal(bytes,File.ReadAllBytes(record.WorkingPath));return;
            }
            copy=scenario=="save-copy-capture"?store.PrepareSaveCopy(record,fixture,new LocalFileSystemProvider(),()=>ct.ThrowIfCancellationRequested()):store.PrepareCommit(record,fixture,ct);
            var native=Assert.IsType<NativeEditSnapshot>(copy.Native);Assert.Equal(bytes.Length,copy.Length);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),copy.Sha256);Assert.Equal(Crc32.Append(0,bytes),copy.Crc32);
            Assert.Equal(time,File.GetLastWriteTimeUtc(copy.Path));
            string captureRoot=Path.GetDirectoryName(Path.GetDirectoryName(copy.Path))!;
            capturePath=captureRoot;
            Assert.True(File.Exists(Path.Combine(captureRoot,"captured.json")));
            using(var view=native.OpenRead(native.Expected,ct))
            {Assert.False(view.CanWrite);using var memory=new MemoryStream();view.CopyTo(memory);Assert.Equal(bytes,memory.ToArray());}
            if(scenario=="origin")
            {
                Assert.Equal(ArchiveOriginState.Present,native.Expected.Origin.State);
                using var root=open(store.Root,false,ct);using var owned=root.OpenDirectory(record.Id,record.SessionDirectoryIdentity,ct);
                using var files=owned.OpenDirectory("files",record.WorkingDirectoryIdentity,ct);using var working=files.OpenWorkingFile("owned.txt",ct);
                Assert.Equal(files.ReadOrigin(working,"owned.txt",ct).Digest,native.Expected.Origin.Digest);
            }
            if(scenario=="capture-quota")
            {
                var copies=new List<EditCommitCopy>();
                try
                {
                    for(int i=1;i<EditSnapshotAdmission.MaximumCopies;i++)copies.Add(store.PrepareCommit(record,fixture,ct));
                    Assert.ThrowsAny<IOException>(()=>store.PrepareCommit(record,fixture,ct));
                    Assert.Equal(EditSnapshotAdmission.MaximumCopies,Directory.GetDirectories(session,"commit-*").Length);
                    var released=copies[0];copies.RemoveAt(0);var error=Record.Exception(released.Dispose);
                    if(OperatingSystem.IsWindows())Assert.Null(error);else Assert.IsAssignableFrom<IOException>(error);
                    copies.Add(store.PrepareCommit(record,fixture,ct));
                }
                finally{foreach(var item in copies){var error=Record.Exception(item.Dispose);Assert.True(error is null or IOException,error?.ToString());}}
                Assert.Equal(bytes,File.ReadAllBytes(record.WorkingPath));return;
            }
            if(scenario=="capture-byte-quota")
            {
                using var first=EditSnapshotAdmission.Acquire(ct);first.ReserveBytes(EditSessionStore.MaxMemberBytes);
                using var second=EditSnapshotAdmission.Acquire(ct);second.ReserveBytes(EditSnapshotAdmission.MaximumActiveBytes-EditSessionStore.MaxMemberBytes-copy.Length);
                using var refused=EditSnapshotAdmission.Acquire(ct);Assert.ThrowsAny<IOException>(()=>refused.ReserveBytes(1));
                second.Dispose();using var retry=EditSnapshotAdmission.Acquire(ct);retry.ReserveBytes(1);
                Assert.Equal(bytes,File.ReadAllBytes(record.WorkingPath));return;
            }
            if(scenario=="late-save")
            {
                string later=record.WorkingPath+".later";File.WriteAllText(later,"later atomic editor save");File.Move(later,record.WorkingPath,true);
                using var view=native.OpenRead(native.Expected,ct);using var memory=new MemoryStream();view.CopyTo(memory);
                Assert.Equal(bytes,memory.ToArray());Assert.Equal("later atomic editor save",File.ReadAllText(record.WorkingPath));return;
            }
            if(scenario=="view-after-dispose")
            {
                using var view=native.OpenRead(native.Expected,ct);var error=Record.Exception(copy.Dispose);
                if(OperatingSystem.IsWindows())Assert.Null(error);else Assert.IsAssignableFrom<IOException>(error);
                Assert.ThrowsAny<IOException>(()=>view.ReadByte());copy.Dispose();Assert.Equal(bytes,File.ReadAllBytes(record.WorkingPath));return;
            }
            if(scenario.StartsWith("cleanup-",StringComparison.Ordinal))
            {
                if(scenario=="cleanup-reused-path")
                {
                    var first=Record.Exception(copy.Dispose);
                    if(OperatingSystem.IsWindows()){Assert.Null(first);Assert.False(Directory.Exists(captureRoot));}
                    else{Assert.IsAssignableFrom<IOException>(first);File.Move(copy.Path,copy.Path+".retained");}
                    Directory.CreateDirectory(Path.GetDirectoryName(copy.Path)!);File.WriteAllText(copy.Path,"unrelated later occupant");retainCapture=true;
                    copy.Dispose();Assert.Equal("unrelated later occupant",File.ReadAllText(copy.Path));return;
                }
                string foreign=scenario=="cleanup-extra-stream"?copy.Path+":FileCat.Unexpected":scenario=="cleanup-extra-file"?Path.Combine(Path.GetDirectoryName(copy.Path)!,"foreign.txt"):
                    scenario=="cleanup-extra-capture-child"?Path.Combine(captureRoot,"foreign.txt"):Path.Combine(captureRoot,"captured.json");
                void ChangeRecord()
                {
                    if(scenario=="cleanup-record-origin-changed")Mark(foreign,mark);
                    else
                    {
                        if(scenario=="cleanup-record-replaced")File.Move(foreign,foreign+".retained");
                        File.WriteAllText(foreign,"unreviewed child or record");
                    }
                }
                var error=Record.Exception(ChangeRecord);
                if(error is IOException or UnauthorizedAccessException){Assert.True(OperatingSystem.IsWindows(),error.ToString());return;}
                Assert.Null(error);retainCapture=true;
                Assert.IsAssignableFrom<IOException>(Record.Exception(copy.Dispose));Assert.True(File.Exists(copy.Path));
                Assert.True(File.Exists(Path.Combine(captureRoot,"capture.json")));if(scenario!="cleanup-extra-stream")Assert.True(File.Exists(foreign));
                if(scenario is not "cleanup-record-origin-changed")Assert.Equal("unreviewed child or record",File.ReadAllText(foreign));
                Assert.Equal(bytes,File.ReadAllBytes(record.WorkingPath));return;
            }
            if(scenario is "snapshot-directory-replaced" or "snapshot-file-replaced" or "snapshot-bytes-changed" or "snapshot-origin-changed")
            {
                void Change()
                {
                    if(scenario=="snapshot-directory-replaced")
                    {Directory.Move(captureRoot,captureRoot+".retained");Directory.CreateDirectory(Path.GetDirectoryName(copy.Path)!);File.WriteAllBytes(copy.Path,bytes);}
                    else if(scenario=="snapshot-file-replaced")
                    {File.Move(copy.Path,copy.Path+".retained");File.WriteAllBytes(copy.Path,bytes);}
                    else if(scenario=="snapshot-bytes-changed")File.WriteAllBytes(copy.Path,new byte[bytes.Length]);
                    else Mark(copy.Path,mark);
                }
                var error=Record.Exception(Change);
                if(error is IOException or UnauthorizedAccessException)
                {Assert.True(OperatingSystem.IsWindows(),error.ToString());native.Review(ct);}
                else
                {Assert.Null(error);retainCapture=true;Assert.ThrowsAny<IOException>(()=>native.Review(ct));Assert.IsAssignableFrom<IOException>(Record.Exception(copy.Dispose));Assert.True(File.Exists(copy.Path));}
                Assert.Equal(bytes,File.ReadAllBytes(record.WorkingPath));return;
            }
            var plan=EditSessionStore.CommitArchivePlan(record,target,copy,false);Assert.Same(native,plan.EditInput);
            if(scenario=="request-binding")
            {
                Assert.ThrowsAny<IOException>(()=>new ArchiveJobPlanning(plan with{MoveSources=true},ct));
                Assert.ThrowsAny<IOException>(()=>new ArchiveJobPlanning(plan with{Changes=[plan.Changes[0] with{SourcePath=record.WorkingPath}]},ct));
                Assert.ThrowsAny<IOException>(()=>new ArchiveJobPlanning(plan with{Changes=[plan.Changes[0],plan.Changes[0]]},ct));
                Assert.ThrowsAny<IOException>(()=>new ArchiveJobPlanning(plan with{Baseline=null},ct));
                Assert.ThrowsAny<IOException>(()=>new ArchiveJobPlanning(plan with{ArchivePath=Path.Combine(fixture,"other.zip")},ct));
                Assert.ThrowsAny<IOException>(()=>new ArchiveJobPlanning(plan with{Profile=ArchiveNativeJobCases.Profile(ArchiveWriteFormat.SevenZip,0)},ct));
                Assert.ThrowsAny<IOException>(()=>new ArchiveJobPlanning(plan with{Changes=[plan.Changes[0] with{MemberPath="other.txt"}]},ct));
                Assert.ThrowsAny<IOException>(()=>new ArchiveJobPlanning(plan with{Changes=[plan.Changes[0] with{Ordinal=1}]},ct));
                Assert.ThrowsAny<IOException>(()=>new ArchiveJobPlanning(plan with{Changes=[plan.Changes[0] with{NewMemberPath="other.txt"}]},ct));
                Assert.ThrowsAny<IOException>(()=>new ArchiveJobPlanning(plan with{NativeBaseline=null},ct));
                Assert.ThrowsAny<IOException>(()=>new ArchiveJobPlanning(plan with{ReviewedManifestDigest=null},ct));
                Assert.ThrowsAny<IOException>(()=>new ArchiveJobPlanning(plan with{ReplaceExisting=true},ct));
                Assert.ThrowsAny<IOException>(()=>new ArchiveJobPlanning(plan with{AddConflictPolicy=ArchiveAddConflictPolicy.Replace},ct));
                native.RequireSession(record);Assert.ThrowsAny<IOException>(()=>native.RequireSession(record with{Id="unrelated"}));
                using var remoteContent=new MemoryContentSource("owned.txt","old member"u8.ToArray());
                var remote=store.CreateRemote("test","test","/owned.txt",remoteContent,remoteContent.GetRevision()!.Value,null,ct);
                var remoteCopy=store.PrepareCommit(remote,fixture,ct);
                try{Assert.ThrowsAny<IOException>(()=>new ArchiveJobPlanning(plan with{EditInput=remoteCopy.Native,
                    Changes=[plan.Changes[0] with{SourcePath=remoteCopy.Path}]},ct));}
                finally{Assert.True(Record.Exception(remoteCopy.Dispose) is null or IOException);}
                var changedNative=new ArchiveFileObservation(new("new-outer",2048,new string('D',64)),target.Native.ParentIdentity,
                    target.Native.LeafName,target.Native.MetadataDigest,target.Native.Origin,new(1700000001,0),0x180);
                var rebased=new ArchiveTargetReview(target.Path,profile,changedNative,new(manifest,new string('D',64),2048,new string('D',64)));
                Assert.ThrowsAny<IOException>(()=>EditSessionStore.CommitArchivePlan(record,rebased,copy,false));
                var reviewedRebase=EditSessionStore.CommitArchivePlan(record,rebased,copy,true);
                native.RequireRequest(reviewedRebase);Assert.Same(changedNative,reviewedRebase.NativeBaseline);
            }
            var planning=new ArchiveJobPlanning(plan,ct);
            using var selection=planning.CaptureInputs(_=>throw new InvalidOperationException("A retained edit must never reopen its source path."),ct)!;
            var transformed=planning.Review(source,selection,selection.Inputs,ct).CreatePlan(false,false,ct,acceptTimestampPrecision:true);
            var member=Assert.Single(transformed.Expected.Members);Assert.Equal(copy.Sha256,member.Sha256);Assert.Equal(bytes.Length,member.Length);
            var binding=Assert.Single(selection.Inputs.Bindings).Value;
            using(var view=binding.File.OpenReadView(binding.Reviewed,ct))
            {using var memory=new MemoryStream();view.CopyTo(memory);Assert.Equal(bytes,memory.ToArray());}
            Assert.DoesNotContain("EditInput",JsonSerializer.Serialize(plan));
        }
        finally
        {
            if(copy is not null)
            {
                var error=Record.Exception(copy.Dispose);
                if(OperatingSystem.IsWindows()&&!retainCapture){Assert.Null(error);Assert.False(Directory.Exists(capturePath));Assert.False(File.Exists(copy.Path));}
                else{Assert.True(error is null or IOException,error?.ToString());Assert.True(Directory.Exists(capturePath));}
            }
            string full=Path.GetFullPath(fixture);
            if(!full.StartsWith(Path.TrimEndingDirectorySeparator(parent)+Path.DirectorySeparatorChar,StringComparison.Ordinal)||
                !Path.GetFileName(full).StartsWith("filecat-edit-snapshot-",StringComparison.Ordinal))throw new IOException("Unexpected snapshot fixture cleanup root.");
            Directory.Delete(full,true);
        }
    }
    private static void Mark(string path,string value)
    {if(OperatingSystem.IsWindows())File.WriteAllText(path+":Zone.Identifier",value);else Assert.True(new UnixFileOperations().WriteOriginMark(path,value));}
    private sealed class Source(ArchiveTargetReview target,string root,Func<string,bool,CancellationToken,EditDirectoryLease> open):IArchiveEditSource
    {
        public ArchiveTargetReview Target=>target;
        public ArchiveManifestMember Member=>target.Assessment.Manifest.Members.Single();
        public IContentSource OpenContent(CancellationToken ct){ct.ThrowIfCancellationRequested();return new MemoryContentSource("owned.txt","old member"u8.ToArray());}
        public void ReviewSource(CancellationToken ct)=>ct.ThrowIfCancellationRequested();
        public string WorkingIdentity(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
        {using var owner=open(root,false,default);return owner.Identify(handle);}
        public void PreserveOrigin(FileStream working,string path,CancellationToken ct)=>ct.ThrowIfCancellationRequested();
    }

    private static ArchiveWriteManifest Original(ArchiveWriteProfile profile)
    {
        var ct=TestContext.Current.CancellationToken;byte[] bytes="old member"u8.ToArray();
        ArchiveTransformInput[] inputs=[new(1,bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)),new ArchiveTimestamp(1700000000,0))];
        ArchiveTransformAddition[] additions=[new("member.txt",ArchiveMemberType.File,1)];
        return (profile.Format switch
        {
            ArchiveWriteFormat.Rar5=>(ArchiveContainerTransformPlan)ArchiveRar5TransformPlan.Create(profile,null,inputs,[],additions,ct),
            ArchiveWriteFormat.SevenZip=>ArchiveSevenZipTransformPlan.Create(profile,null,inputs,[],additions,ct),
            ArchiveWriteFormat.Zip=>ArchiveZipTransformPlan.Create(profile,null,inputs,[],additions,ct),
            _=>ArchiveTarTransformPlan.Create(profile,null,inputs,[],additions,ct),
        }).Expected;
    }
}
