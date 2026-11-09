using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using System.Globalization;
using System.Text.Json.Nodes;

namespace FileCat.Tests;

internal static class ArchiveEditLegacyAdoptionCases
{
    internal static void Run(string scenario,IFileSystemOperations operations,
        Func<string,bool,CancellationToken,EditDirectoryLease> nativeOpen,
        Func<string,EditSessionRecord,CancellationToken,ILegacyEditSource> legacyOpen)
    {
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-adopt-"+Guid.NewGuid().ToString("N"))).FullName;
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(fixture,
            UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        try
        {
            string oldRoot=Path.Combine(fixture,"legacy"),nativeRoot=Path.Combine(fixture,"native");
            var oldStore=new EditSessionStore(oldRoot,operations);
            byte[] initial="original remote bytes"u8.ToArray();using var content=new MemoryContentSource("old.txt",initial);
            string? mark=scenario=="origin"?"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/old.txt\r\n":null;
            var old=oldStore.CreateRemote("server","server","/old.txt",content,content.GetRevision()!.Value,mark,ct);
            byte[] expected=initial;
            if(scenario=="edited")
            {expected="unsaved editor changes"u8.ToArray();File.WriteAllBytes(old.WorkingPath,expected);}
            byte[] oldRecord=File.ReadAllBytes(Path.Combine(oldRoot,old.Id,"session.json"));
            var store=new EditSessionStore(oldRoot,operations,nativeOpen,null,null,nativeRoot);
            using var source=legacyOpen(oldRoot,old,ct);
            if(scenario=="pending-failure")
            {
                using var failing=new FailBeforeReady(source);
                Assert.ThrowsAny<IOException>(()=>store.CopyLegacyForAdoption(failing,ct));
                var after=store.LoadAll();Assert.Equal(2,after.Count);
                Assert.True(Assert.Single(after,row=>row.Id==old.Id).IsSupported);
                Assert.False(Assert.Single(after,row=>row.Id!=old.Id).IsSupported);
                Assert.True(File.Exists(old.WorkingPath));
                Assert.Equal(oldRecord,File.ReadAllBytes(Path.Combine(oldRoot,old.Id,"session.json")));
                return;
            }
            var adopted=store.CopyLegacyForAdoption(source,ct);
            Assert.Equal(EditSessionRecord.NativeStorageVersion,adopted.Version);
            Assert.True(adopted.LegacyAdoptionReady);Assert.True(adopted.IsSupported);
            Assert.Equal(old.Id,adopted.LegacyAdoption!.SourceId);
            Assert.Equal(source.Working.Sha256,adopted.LegacyAdoption.WorkingSha256);
            Assert.Equal(expected,File.ReadAllBytes(adopted.WorkingPath));
            Assert.Equal(expected,File.ReadAllBytes(old.WorkingPath));
            Assert.Equal(oldRecord,File.ReadAllBytes(Path.Combine(oldRoot,old.Id,"session.json")));
            Assert.Equal(scenario=="origin"?ArchiveOriginState.Present:ArchiveOriginState.Absent,source.Origin.State);
            var linked=Assert.Single(store.ReviewLegacyAdoptions(legacyOpen,ct));
            Assert.Equal(EditLegacyAdoptionCatalogState.LinkedRetainedCopy,linked.State);
            Assert.Equal(old.Id,linked.ActiveLegacy!.Id);
            Assert.Equal(adopted.Id,linked.Native.Id);
            var refused=Assert.Single(store.ReviewLegacyAdoptions(
                (_,_,_)=>throw new IOException("The original is unavailable during restart review."),ct));
            Assert.Equal(EditLegacyAdoptionCatalogState.NeedsReview,refused.State);
            Assert.True(File.Exists(old.WorkingPath));Assert.True(File.Exists(adopted.WorkingPath));
            if(scenario=="source-changed")
            {
                source.Dispose();
                File.WriteAllBytes(old.WorkingPath,"changed after adoption"u8.ToArray());
                var changed=Assert.Single(store.ReviewLegacyAdoptions(legacyOpen,ct));
                Assert.Equal(EditLegacyAdoptionCatalogState.NeedsReview,changed.State);
                Assert.True(File.Exists(old.WorkingPath));
                Assert.Equal(expected,File.ReadAllBytes(adopted.WorkingPath));
                return;
            }
            if(scenario=="duplicate")
            {
                using var second=legacyOpen(oldRoot,old,ct);
                store.CopyLegacyForAdoption(second,ct);
                var ambiguous=store.ReviewLegacyAdoptions(legacyOpen,ct);
                Assert.Equal(2,ambiguous.Count);
                Assert.All(ambiguous,row=>Assert.Equal(EditLegacyAdoptionCatalogState.NeedsReview,row.State));
                Assert.True(File.Exists(old.WorkingPath));
                return;
            }
            if(scenario=="tampered-link")
            {
                string revision=Path.Combine(nativeRoot,adopted.Id,
                    "session-state-"+adopted.StorageRevision.ToString("D19",CultureInfo.InvariantCulture)+".json");
                var json=JsonNode.Parse(File.ReadAllText(revision))!.AsObject();
                json["LegacyAdoption"]!["SourceId"]=Guid.NewGuid().ToString("N");
                File.WriteAllText(revision,json.ToJsonString());
                var changed=store.LoadAll();Assert.Equal(2,changed.Count);
                Assert.True(Assert.Single(changed,row=>row.Id==old.Id).IsSupported);
                Assert.False(Assert.Single(changed,row=>row.Id==adopted.Id).IsSupported);
                Assert.Equal(oldRecord,File.ReadAllBytes(Path.Combine(oldRoot,old.Id,"session.json")));
                return;
            }
            if(scenario=="discard")
            {
                var review=store.ReviewWorking(adopted,new LocalFileSystemProvider());
                Assert.Throws<NotSupportedException>(()=>store.DiscardReviewed(adopted,review,ct));
                Assert.True(File.Exists(old.WorkingPath));Assert.True(File.Exists(adopted.WorkingPath));
            }
            var loaded=store.LoadAll();Assert.Equal(2,loaded.Count);
            var restarted=Assert.Single(loaded,row=>row.Id==adopted.Id);
            Assert.True(restarted.IsSupported);Assert.True(restarted.LegacyAdoptionReady);
            Assert.Equal(adopted.LegacyAdoption,restarted.LegacyAdoption);
            Assert.Equal(scenario=="edited"?EditState.Modified:EditState.Unchanged,
                store.StateOf(restarted,new LocalFileSystemProvider()));
        }
        finally{TestCleanup.DeleteTree(fixture);}
    }

    private sealed class FailBeforeReady(ILegacyEditSource inner):ILegacyEditSource
    {
        private int reviews;
        public EditSessionRecord Session=>inner.Session;
        public EditFileVersion Working=>inner.Working;
        public ArchiveOriginSnapshot Origin=>inner.Origin;
        public string RootPath=>inner.RootPath;
        public string RootIdentity=>inner.RootIdentity;
        public string SessionIdentity=>inner.SessionIdentity;
        public string FilesIdentity=>inner.FilesIdentity;
        public string RecordIdentity=>inner.RecordIdentity;
        public string RecordSha256=>inner.RecordSha256;
        public void Review(CancellationToken ct=default)
        {if(++reviews==4)throw new IOException("Injected final old-source review failure.");inner.Review(ct);}
        public void CopyTo(Stream destination,CancellationToken ct=default)=>inner.CopyTo(destination,ct);
        public void Dispose(){ /* The fixture owns the old source lease. */ }
    }
}
