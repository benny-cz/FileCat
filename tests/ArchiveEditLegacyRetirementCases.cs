using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Tests;

/// <summary>
/// Plan §12.1: retiring the old folder of a ready adopted copy. Nothing changes until the review and an exact-removal
/// probe pass; then a durable retiring revision makes the copy the edit, the old folder's recorded objects are removed
/// one by one, and a last revision drops the relationship. A stop resumes; a change or an extra file is kept, never
/// removed, so no edit is lost.
/// </summary>
internal static class ArchiveEditLegacyRetirementCases
{
    internal static IEnumerable<object[]> Scenarios()
    {
        foreach(string scenario in new[]{"complete","edited-old","extra-file","open-editor","stop-after-intent","stop-after-working",
            "changed-after-intent","extra-after-intent","not-adopted","cancel","unreadable-folder","atomic-save-at-removal",
            "open-at-removal"})yield return [scenario];
    }

    internal static void Run(string scenario,IFileSystemOperations operations,
        Func<string,bool,CancellationToken,EditDirectoryLease> nativeOpen,
        Func<string,EditSessionRecord,CancellationToken,ILegacyEditSource> legacyOpen,
        Func<EditLegacyRemoval,bool,CancellationToken,string?> remove)
    {
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-retire-"+Guid.NewGuid().ToString("N"))).FullName;
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(fixture,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        FileStream? editor=null;
        try
        {
            string oldRoot=Path.Combine(fixture,"legacy"),nativeRoot=Path.Combine(fixture,"native");
            var oldStore=new EditSessionStore(oldRoot,operations);
            using var content=new MemoryContentSource("old.txt","original remote bytes"u8.ToArray());
            var old=oldStore.CreateRemote("server","server","/old.txt",content,content.GetRevision()!.Value,null,ct);
            File.WriteAllText(old.WorkingPath,"unsaved editor changes");
            string oldFolder=Path.Combine(oldRoot,old.Id);
            var store=new EditSessionStore(oldRoot,operations,nativeOpen,null,null,nativeRoot);
            EditSessionRecord adopted;
            using(var source=legacyOpen(oldRoot,old,ct))adopted=store.CopyLegacyForAdoption(source,ct);
            if(scenario=="not-adopted")
            {
                Assert.ThrowsAny<IOException>(()=>store.RetireLegacyFolder(old,legacyOpen,remove,ct));
                Assert.True(File.Exists(old.WorkingPath));return;
            }
            string[] before=Files(oldFolder);
            void Unchanged()
            {
                Assert.Equal(before,Files(oldFolder));
                var row=Assert.Single(store.LoadAll(),r=>r.Id==adopted.Id);
                Assert.True(row.LegacyAdoptionReady);Assert.False(row.LegacyRetiring);
                Assert.Equal(old.Id,store.FindRemote("server","/old.txt")!.Id);
            }
            if(scenario is "edited-old" or "extra-file" or "open-editor")
            {
                if(scenario=="edited-old")File.WriteAllText(old.WorkingPath,"a later save in the old editor");
                if(scenario=="extra-file")File.WriteAllText(Path.Combine(oldFolder,"files","old.txt~"),"an editor's backup");
                if(scenario=="open-editor")editor=new FileStream(old.WorkingPath,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite);
                before=Files(oldFolder);
                var error=Record.Exception(()=>store.RetireLegacyFolder(adopted,legacyOpen,remove,ct));
                // macOS cannot tell that another program has a file open: an editor keeps its own unlinked copy there.
                if(scenario=="open-editor"&&OperatingSystem.IsMacOS()){Assert.Null(error);return;}
                Assert.IsAssignableFrom<IOException>(error);Unchanged();return;
            }
            // Unix only: folder permissions and the removal's own seam; a lease to break exists only on Linux.
            if(scenario is "unreadable-folder" or "atomic-save-at-removal" or "open-at-removal"&&OperatingSystem.IsWindows())
                Assert.Skip("Unix folder permissions and the Unix removal's own seam.");
            if(scenario=="open-at-removal"&&!OperatingSystem.IsLinux())Assert.Skip("Only Linux has a lease for another program's open to break.");
            if(scenario is "atomic-save-at-removal" or "open-at-removal")
            {RemovalRaces(scenario,store,adopted,old,oldFolder,legacyOpen,remove,ct);return;}
            Func<EditLegacyRemoval,bool,CancellationToken,string?> once=remove;
            if(scenario is "stop-after-intent" or "changed-after-intent" or "extra-after-intent" or "cancel" or "unreadable-folder")
                once=(request,probe,token)=>probe?remove(request,true,token):throw new OperationCanceledException();
            if(scenario=="stop-after-working")
                once=(request,probe,token)=>
                {
                    if(probe)return remove(request,true,token);
                    File.Delete(Path.Combine(oldFolder,"files",request.WorkingLeaf!));throw new OperationCanceledException();
                };
            var first=store.RetireLegacyFolder(adopted,legacyOpen,once,ct);
            if(scenario=="complete")
            {
                Assert.True(first.Complete,first.Issue);
            }
            else
            {
                Assert.False(first.Complete);Assert.True(first.Current.LegacyRetiring);
                // From the retiring revision the copy is the edit: the old record no longer speaks for it.
                Assert.Equal(adopted.Id,store.FindRemote("server","/old.txt")!.Id);
                Assert.DoesNotContain(EditSessionStore.Active(store.LoadAll()),row=>row.Id==old.Id);
                if(scenario=="changed-after-intent")File.WriteAllText(old.WorkingPath,"a save after the copy");
                if(scenario=="extra-after-intent")File.WriteAllText(Path.Combine(oldFolder,"files","old.txt~"),"an editor's backup");
                if(scenario=="cancel")
                {
                    // While the old edit is intact, the retirement can be stopped: the old edit is the edit again.
                    var back=store.CancelLegacyRetirement(Assert.Single(store.LoadAll(),row=>row.Id==adopted.Id),ct);
                    Assert.False(back.LegacyRetiring);Assert.True(back.LegacyAdoptionReady);
                    Assert.Equal(old.Id,store.FindRemote("server","/old.txt")!.Id);Assert.Equal(before,Files(oldFolder));
                }
                if(scenario=="unreadable-folder")
                {
                    // A folder that cannot be read is an error, never "already removed": the relationship and every file stay.
                    File.SetUnixFileMode(oldFolder,UnixFileMode.None);
                    try
                    {
                        var blocked=store.RetireLegacyFolder(Assert.Single(store.LoadAll(),row=>row.Id==adopted.Id),legacyOpen,remove,ct);
                        Assert.False(blocked.Complete);Assert.Contains("kept",blocked.Issue,StringComparison.Ordinal);
                        Assert.True(blocked.Current.LegacyRetiring);Assert.NotNull(blocked.Current.LegacyAdoption);
                    }
                    finally{File.SetUnixFileMode(oldFolder,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);}
                    Assert.Equal(before,Files(oldFolder));
                }
                var restarted=new EditSessionStore(oldRoot,operations,nativeOpen,null,null,nativeRoot);
                var resumed=Assert.Single(restarted.LoadAll(),row=>row.Id==adopted.Id);Assert.Equal(scenario!="cancel",resumed.LegacyRetiring);
                var second=restarted.RetireLegacyFolder(resumed,legacyOpen,remove,ct);
                if(scenario is "changed-after-intent" or "extra-after-intent")
                {
                    Assert.False(second.Complete);Assert.Contains("kept",second.Issue,StringComparison.Ordinal);
                    if(scenario=="changed-after-intent")Assert.Equal("a save after the copy",File.ReadAllText(old.WorkingPath));
                    else Assert.Equal("an editor's backup",File.ReadAllText(Path.Combine(oldFolder,"files","old.txt~")));
                    Assert.True(second.Current.LegacyRetiring);return;
                }
                Assert.True(second.Complete,second.Issue);
            }
            Assert.False(Directory.Exists(oldFolder));
            var rows=new EditSessionStore(oldRoot,operations,nativeOpen,null,null,nativeRoot).LoadAll();
            var now=Assert.Single(rows);Assert.Equal(adopted.Id,now.Id);
            Assert.Null(now.LegacyAdoption);Assert.False(now.LegacyAdoptionReady);Assert.False(now.LegacyRetiring);Assert.True(now.IsSupported);
            Assert.Equal("unsaved editor changes",File.ReadAllText(now.WorkingPath));
            Assert.Equal(now.Id,store.FindRemote("server","/old.txt")!.Id);
            // A plain native edit again: it can be discarded like any other.
            var discard=store.DiscardReviewed(now,store.ReviewWorking(now,new LocalFileSystemProvider()),ct);
            Assert.True(discard.Complete,discard.Issue);
        }
        finally{editor?.Dispose();TestCleanup.DeleteTree(fixture);}
    }

    /// <summary>
    /// The last moment of a removal (Unix): an editor's atomic save that takes the old working copy's name just before it
    /// is set aside keeps its file, and the retirement can then be cancelled; on Linux another program that opens the old
    /// working copy while the lease is held keeps it too, FileCat survives the lease-break signal, and the opener reads
    /// the intact bytes before the retirement completes.
    /// </summary>
    private static void RemovalRaces(string scenario,EditSessionStore store,EditSessionRecord adopted,EditSessionRecord old,string oldFolder,
        Func<string,EditSessionRecord,CancellationToken,ILegacyEditSource> legacyOpen,Func<EditLegacyRemoval,bool,CancellationToken,string?> remove,
        CancellationToken ct)
    {
        string leaf=Path.GetFileName(old.WorkingPath);System.Diagnostics.Process? reader=null;
        UnixExactRemoval.BeforeSetAside.Value=name=>
        {
            if(name!=leaf)return;
            if(scenario=="atomic-save-at-removal")
            {
                string saved=Path.Combine(Path.GetDirectoryName(old.WorkingPath)!,".old.txt.swp");
                File.WriteAllText(saved,"an atomic save");File.Move(saved,old.WorkingPath,overwrite:true);
            }
            else
            {
                reader=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cat",[old.WorkingPath])
                    {RedirectStandardOutput=true,UseShellExecute=false})!;
                Thread.Sleep(400); // its open() now waits for the lease to be released, having started a break
            }
        };
        EditRetirementResult first;
        try{first=store.RetireLegacyFolder(adopted,legacyOpen,remove,ct);}
        finally{UnixExactRemoval.BeforeSetAside.Value=null;}
        Assert.False(first.Complete);Assert.True(first.Current.LegacyRetiring);Assert.Contains("kept",first.Issue,StringComparison.Ordinal);
        Assert.True(Directory.Exists(oldFolder));
        if(scenario=="atomic-save-at-removal")
        {
            Assert.Equal("an atomic save",File.ReadAllText(old.WorkingPath));
            // The new save is not the copied object, so the retirement cannot finish; it can return to the old edit.
            var again=store.RetireLegacyFolder(Assert.Single(store.LoadAll(),row=>row.Id==adopted.Id),legacyOpen,remove,ct);
            Assert.False(again.Complete);Assert.Equal("an atomic save",File.ReadAllText(old.WorkingPath));
            var back=store.CancelLegacyRetirement(again.Current,ct);
            Assert.False(back.LegacyRetiring);Assert.Equal(old.Id,store.FindRemote("server","/old.txt")!.Id);
            return;
        }
        Assert.NotNull(reader);
        Assert.True(reader!.WaitForExit(15000),"The reader stayed blocked after the lease was released.");
        Assert.Equal(0,reader.ExitCode);Assert.Equal("unsaved editor changes",reader.StandardOutput.ReadToEnd());
        reader.Dispose();
        var done=store.RetireLegacyFolder(Assert.Single(store.LoadAll(),row=>row.Id==adopted.Id),legacyOpen,remove,ct);
        Assert.True(done.Complete,done.Issue);Assert.False(Directory.Exists(oldFolder));
        Assert.Null(Assert.Single(store.LoadAll()).LegacyAdoption);
    }

    private static string[] Files(string folder)=>!Directory.Exists(folder)?[]:
        Directory.EnumerateFileSystemEntries(folder,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path=>Path.GetRelativePath(folder,path)+(File.Exists(path)?":"+Shared(path).Length:"")).ToArray();
    // Read beside an editor that holds the file open for writing (Windows sharing).
    private static string Shared(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
}
