using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Tests;

internal static class ArchiveEditRecoveredSaveCopyCases
{
    internal static IEnumerable<object[]> Scenarios()=>new[]{"complete","later-revision","target-collision","source-changed",
        "receipt-changed","unknown-child","second-attempt","stale-record","cancel-before"}.Select(value=>new object[]{value});

    internal static void Run(string scenario,Func<string,bool,CancellationToken,EditDirectoryLease> open,
        IFileSystemOperations operations,Func<string,CancellationToken,EditSaveCopyDestination> copyOpen)
    {
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string fixture=PrivateDirectory(Path.Combine(parent,"filecat-recovered-copy-"+Guid.NewGuid().ToString("N")));
        try
        {
            var store=new EditSessionStore(Path.Combine(fixture,"sessions"),operations,open,copyOpen);
            using var initial=new MemoryContentSource("owned.txt","initial bytes"u8.ToArray());
            var record=store.CreateRemote("test","test","/owned.txt",initial,initial.GetRevision()!.Value,null,ct);
            byte[] edited="retained editor bytes after restart"u8.ToArray();File.WriteAllBytes(record.WorkingPath,edited);
            var copy=store.PrepareCommit(record,fixture,ct);
            string capture=Path.GetDirectoryName(Path.GetDirectoryName(copy.Path))!;
            string sentinel=Path.Combine(capture,"keep-for-restart.txt");File.WriteAllText(sentinel,"keep");
            Assert.IsAssignableFrom<IOException>(Record.Exception(copy.Dispose));File.Delete(sentinel);
            Assert.Equal(edited,File.ReadAllBytes(copy.Path));
            var latest=scenario is "later-revision" or "stale-record"
                ?store.Save(record with{LastCommitUtc=DateTime.UnixEpoch.AddSeconds(1700000001)}):record;
            string destination=PrivateDirectory(Path.Combine(fixture,"destination"));
            string target=Path.Combine(destination,"saved.txt");
            if(scenario=="target-collision")File.WriteAllText(target,"unrelated destination");
            if(scenario=="source-changed")File.WriteAllText(copy.Path,"changed captured bytes");
            if(scenario=="receipt-changed")File.WriteAllText(Path.Combine(capture,"captured.json"),"{}");
            if(scenario=="unknown-child")File.WriteAllText(Path.Combine(capture,"foreign.txt"),"unrelated");
            var restarted=new EditSessionStore(store.Root,operations,open,copyOpen);
            EditSaveCopyPublicationResult? result=null;
            try
            {
                if(scenario=="cancel-before")
                {
                    using var cancelled=new CancellationTokenSource();cancelled.Cancel();
                    Assert.ThrowsAny<OperationCanceledException>(()=>restarted.SaveRecoveredNativeCopy(latest,capture,target,cancelled.Token));
                }
                else if(scenario is "source-changed" or "receipt-changed" or "unknown-child" or "stale-record")
                {
                    var selected=scenario=="stale-record"?record:latest;
                    Assert.ThrowsAny<IOException>(()=>restarted.SaveRecoveredNativeCopy(selected,capture,target,ct));
                }
                else
                {
                    result=restarted.SaveRecoveredNativeCopy(latest,capture,target,ct);
                    if(scenario=="target-collision")
                    {
                        Assert.Null(result.Published);Assert.NotNull(result.Issue);
                        Assert.Equal("unrelated destination",File.ReadAllText(target));
                    }
                    else
                    {
                        Assert.NotNull(result.Published);Assert.Null(result.Issue);Assert.True(result.OutcomeRecorded);
                        Assert.Equal(edited,File.ReadAllBytes(target));
                        if(scenario=="second-attempt")
                        {
                            string second=Path.Combine(destination,"second.txt");
                            Assert.ThrowsAny<IOException>(()=>restarted.SaveRecoveredNativeCopy(latest,capture,second,ct));
                            Assert.False(File.Exists(second));
                        }
                    }
                    result.Dispose();result=null;
                    var review=Assert.Single(restarted.ReviewRetainedCaptures(latest,ct));
                    Assert.Equal(EditCaptureRecoveryState.Complete,review.State);
                    Assert.NotNull(review.SavedCopy);
                }
                Assert.Equal(edited,File.ReadAllBytes(record.WorkingPath));
                if(scenario!="source-changed")Assert.Equal(edited,File.ReadAllBytes(copy.Path));
                if(scenario is "source-changed" or "receipt-changed" or "unknown-child" or "stale-record" or "cancel-before")
                {
                    Assert.False(File.Exists(target));
                    Assert.False(File.Exists(Path.Combine(capture,"save-copy.json")));
                }
            }
            finally{result?.Dispose();}
        }
        finally
        {
            if(!Path.GetFullPath(fixture).StartsWith(Path.TrimEndingDirectorySeparator(parent)+Path.DirectorySeparatorChar,StringComparison.Ordinal)||
                !Path.GetFileName(fixture).StartsWith("filecat-recovered-copy-",StringComparison.Ordinal))throw new IOException("Unexpected recovery fixture root.");
            Directory.Delete(fixture,true);
        }
    }
    private static string PrivateDirectory(string path)=>OperatingSystem.IsWindows()?Directory.CreateDirectory(path).FullName:
        Directory.CreateDirectory(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute).FullName;
}
