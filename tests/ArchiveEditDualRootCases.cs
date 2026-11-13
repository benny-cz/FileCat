using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Tests;

internal static class ArchiveEditDualRootCases
{
    internal static void Run(Func<string,bool,CancellationToken,EditDirectoryLease> open)
    {
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-edit-dual-root-"+Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string oldRoot=Path.Combine(fixture,"legacy"),newRoot=Path.Combine(fixture,"native");
            var files=new PortableFileOperations();
            var oldStore=new EditSessionStore(oldRoot,files);
            using var oldSource=new MemoryContentSource("old.txt","old working bytes"u8.ToArray());
            var old=oldStore.CreateRemote("old","old","/old.txt",oldSource,oldSource.GetRevision()!.Value,null,ct);
            var store=new EditSessionStore(oldRoot,files,open,null,null,newRoot);
            using var newSource=new MemoryContentSource("new.txt","new working bytes"u8.ToArray());
            var fresh=store.CreateRemote("new","new","/new.txt",newSource,newSource.GetRevision()!.Value,null,ct);
            Assert.Equal(EditSessionRecord.CurrentVersion,old.Version);
            Assert.Equal(EditSessionRecord.NativeStorageVersion,fresh.Version);
            Assert.Equal(oldRoot,store.Root);Assert.Equal(newRoot,store.NativeRoot);
            Assert.Equal(Path.Combine(oldRoot,old.Id),store.SessionDirectory(old));
            Assert.Equal(Path.Combine(newRoot,fresh.Id),store.SessionDirectory(fresh));
            var loaded=store.LoadAll();Assert.Equal(2,loaded.Count);
            Assert.Equal(old,Assert.Single(loaded,entry=>entry.Id==old.Id));
            Assert.Equal(fresh,Assert.Single(loaded,entry=>entry.Id==fresh.Id));
            Assert.Equal(old,store.FindRemote("old","/old.txt"));
            Assert.Equal(fresh,store.FindRemote("new","/new.txt"));
            Assert.Equal(EditState.Unchanged,store.StateOf(old,new LocalFileSystemProvider()));
            Assert.Equal(EditState.Unchanged,store.StateOf(fresh,new LocalFileSystemProvider()));
            var savedOld=store.Save(old with{LastCommitUtc=DateTime.UnixEpoch.AddSeconds(1700000001)});
            var savedNew=store.Save(fresh with{LastCommitUtc=DateTime.UnixEpoch.AddSeconds(1700000002)});
            Assert.Equal(savedOld,Assert.Single(store.LoadAll(),entry=>entry.Id==old.Id));
            Assert.Equal(savedNew,Assert.Single(store.LoadAll(),entry=>entry.Id==fresh.Id));
            Assert.True(File.Exists(Path.Combine(oldRoot,old.Id,"session.json")));
            Assert.True(File.Exists(Path.Combine(newRoot,fresh.Id,"session-state-0000000000000000001.json")));
            string wrongOld=Directory.CreateDirectory(Path.Combine(newRoot,old.Id)).FullName;
            File.Copy(Path.Combine(oldRoot,old.Id,"session.json"),Path.Combine(wrongOld,"session.json"));
            string wrongNew=Directory.CreateDirectory(Path.Combine(oldRoot,fresh.Id)).FullName;
            File.Copy(Path.Combine(newRoot,fresh.Id,"session.json"),Path.Combine(wrongNew,"session.json"));
            var misplaced=store.LoadAll();
            Assert.Equal(4,misplaced.Count);
            Assert.Equal(2,misplaced.Count(entry=>entry.IsSupported));
            Assert.Equal(2,misplaced.Count(entry=>!entry.IsSupported));
            Assert.Equal(wrongOld,store.SessionDirectory(Assert.Single(misplaced,entry=>!entry.IsSupported&&entry.Id==old.Id)));
            Assert.Equal(wrongNew,store.SessionDirectory(Assert.Single(misplaced,entry=>!entry.IsSupported&&entry.Id==fresh.Id)));
            // An edit made before native storage was listed stays discardable beside a native root: its folder goes
            // through the legacy store's bound removal (R21), and nothing else does, not even a copy of its record that
            // was misplaced in the native root. A native record still needs the native route.
            store.Discard(old);
            Assert.False(Directory.Exists(Path.Combine(oldRoot,old.Id)));Assert.False(File.Exists(old.WorkingPath));
            Assert.True(File.Exists(Path.Combine(wrongOld,"session.json")));
            Assert.True(Directory.Exists(wrongNew));Assert.True(File.Exists(fresh.WorkingPath));
            Assert.Throws<NotSupportedException>(()=>store.Discard(fresh));
            Assert.True(File.Exists(fresh.WorkingPath));
        }
        finally{TestCleanup.DeleteTree(fixture);}
    }
}
