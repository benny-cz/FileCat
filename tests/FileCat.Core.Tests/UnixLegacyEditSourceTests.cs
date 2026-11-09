using System.IO.Compression;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Tests;

namespace FileCat.Core.Tests;

public sealed class UnixLegacyEditSourceTests
{
    private sealed class Sink(List<EntryData> rows):IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries){foreach(var entry in entries)rows.Add(entry);}
        public void ReportIssue(string message) { }
    }

    [Fact]
    public async Task Real_legacy_zip_member_record_without_a_version_is_admitted_read_only()
    {
        if(OperatingSystem.IsWindows())Assert.Skip("This is the Unix descriptor lane.");
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-legacy-zip-"+Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string archivePath=Path.Combine(fixture,"docs.zip");
            using(var archive=ZipFile.Open(archivePath,ZipArchiveMode.Create))
                using(var output=new StreamWriter(archive.CreateEntry("notes.txt").Open()))output.Write("old zip member");
            var provider=new ZipProvider(Path.Combine(fixture,"provider"));
            var location=provider.GetContainerLocation(archivePath)!.WithPath("");
            var rows=new List<EntryData>();await provider.EnumerateAsync(location,new Sink(rows),ct);
            var member=provider.GetItemRef(location,Assert.Single(rows,entry=>entry.Name=="notes.txt"));
            string root=Path.Combine(fixture,"legacy");var store=new EditSessionStore(root,new UnixFileOperations());
            var created=store.Create(provider,member,ct);
            string record=Path.Combine(store.SessionDirectory(created),"session.json");
            var json=JsonNode.Parse(File.ReadAllText(record))!.AsObject();json.Remove("Version");json.Remove("Kind");
            File.WriteAllText(record,json.ToJsonString());
            var legacy=Assert.Single(store.LoadAll());Assert.Equal(1,legacy.Version);Assert.True(legacy.IsSupported);
            using var held=UnixLegacyEditSource.Open(root,legacy,ct);
            using var copy=new MemoryStream();held.CopyTo(copy,ct);
            Assert.Equal("old zip member",System.Text.Encoding.UTF8.GetString(copy.ToArray()));
            Assert.True(File.Exists(legacy.WorkingPath));Assert.True(File.Exists(record));
        }
        finally{TestCleanup.DeleteTree(fixture);}
    }

    [Theory,InlineData("copy"),InlineData("in-place-change"),InlineData("working-replacement"),
        InlineData("record-replacement"),InlineData("working-symlink"),InlineData("exposed-files"),
        InlineData("working-hardlink"),InlineData("origin"),InlineData("unknown-xattr")]
    public void Old_edit_source_is_read_only_and_refuses_changed_or_exposed_objects(string scenario)
    {
        if(OperatingSystem.IsWindows())Assert.Skip("This is the Unix descriptor lane.");
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-legacy-source-"+Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string root=Path.Combine(fixture,"legacy");var operations=new UnixFileOperations();
            var store=new EditSessionStore(root,operations);
            byte[] bytes="owned old editor bytes"u8.ToArray();
            using var content=new MemoryContentSource("old.txt",bytes);
            string? mark=scenario=="origin"?"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/old.txt\r\n":null;
            var old=store.CreateRemote("test","test","/old.txt",content,content.GetRevision()!.Value,mark,ct);
            string session=store.SessionDirectory(old),record=Path.Combine(session,"session.json");
            Assert.Equal(EditSessionRecord.CurrentVersion,old.Version);
            Assert.True(File.Exists(old.WorkingPath));
            if(scenario=="working-symlink")
            {
                string retained=Path.Combine(session,"retained-original");File.Move(old.WorkingPath,retained);
                File.CreateSymbolicLink(old.WorkingPath,retained);
                Assert.ThrowsAny<IOException>(()=>UnixLegacyEditSource.Open(root,old,ct));
            }
            else if(scenario=="exposed-files")
            {
                File.SetUnixFileMode(Path.GetDirectoryName(old.WorkingPath)!,
                    UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute|
                    UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|
                    UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute);
                Assert.ThrowsAny<IOException>(()=>UnixLegacyEditSource.Open(root,old,ct));
            }
            else if(scenario=="working-hardlink")
            {
                operations.CreateLink(Path.Combine(fixture,"second-link"),old.WorkingPath,LinkKind.Hard,false);
                Assert.ThrowsAny<IOException>(()=>UnixLegacyEditSource.Open(root,old,ct));
            }
            else if(scenario=="unknown-xattr")
            {
                Assert.True(Xattr.Set(old.WorkingPath,OperatingSystem.IsMacOS()?"com.filecat.unexpected":"user.filecat.unexpected","retained metadata"));
                Assert.ThrowsAny<IOException>(()=>UnixLegacyEditSource.Open(root,old,ct));
            }
            else
            {
                using var held=UnixLegacyEditSource.Open(root,old,ct);
                Assert.Equal(old,held.Session);Assert.Equal(bytes.Length,held.Working.Length);
                Assert.False(string.IsNullOrEmpty(held.RootIdentity));
                Assert.False(string.IsNullOrEmpty(held.RecordSha256));
                if(scenario=="copy")
                {
                    using var copy=new MemoryStream();held.CopyTo(copy,ct);Assert.Equal(bytes,copy.ToArray());
                    held.Review(ct);
                }
                else if(scenario=="origin")
                {
                    Assert.Equal(ArchiveOriginState.Present,held.Origin.State);
                    Assert.NotEmpty(held.Origin.Bytes.ToArray());
                    using var copy=new MemoryStream();held.CopyTo(copy,ct);Assert.Equal(bytes,copy.ToArray());
                    Assert.Contains("ZoneId=3",operations.ReadOriginMark(old.WorkingPath));
                }
                else if(scenario=="in-place-change")
                {File.WriteAllText(old.WorkingPath,"changed in place");Assert.ThrowsAny<IOException>(()=>held.Review(ct));}
                else if(scenario=="working-replacement")
                {File.Move(old.WorkingPath,Path.Combine(session,"retained-original"));File.WriteAllText(old.WorkingPath,"replacement");
                    Assert.ThrowsAny<IOException>(()=>held.Review(ct));}
                else if(scenario=="record-replacement")
                {byte[] original=File.ReadAllBytes(record);File.Move(record,Path.Combine(session,"retained-record"));File.WriteAllBytes(record,original);
                    Assert.ThrowsAny<IOException>(()=>held.Review(ct));}
            }
            Assert.True(Directory.Exists(session));Assert.True(File.Exists(record));
        }
        finally{TestCleanup.DeleteTree(fixture);}
    }
}
