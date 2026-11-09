using System.Security.AccessControl;
using System.Security.Principal;
using System.IO.Compression;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsLegacyEditSourceTests
{
    private sealed class Sink(List<EntryData> entries):IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> batch){foreach(var entry in batch)entries.Add(entry);}
        public void ReportIssue(string message)=>throw new IOException(message);
    }

    [Fact]
    public async Task Real_legacy_zip_member_record_without_version_is_admitted_read_only()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(),"Windows source handles required.");
        Assert.SkipWhen(WindowsTokenAuthority.IsHighIntegrity,"Run legacy admission unelevated.");
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
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
            string root=Path.Combine(fixture,"legacy");var store=new EditSessionStore(root,new WindowsFileOperations());
            var created=store.Create(provider,member,ct);
            string record=Path.Combine(store.SessionDirectory(created),"session.json");
            var json=JsonNode.Parse(File.ReadAllText(record))!.AsObject();json.Remove("Version");json.Remove("Kind");
            File.WriteAllText(record,json.ToJsonString());
            var legacy=Assert.Single(store.LoadAll());Assert.Equal(1,legacy.Version);Assert.True(legacy.IsSupported);
            using var held=WindowsLegacyEditSource.Open(root,legacy,ct);
            using var copy=new MemoryStream();held.CopyTo(copy,ct);
            Assert.Equal("old zip member",System.Text.Encoding.UTF8.GetString(copy.ToArray()));
            Assert.True(File.Exists(legacy.WorkingPath));Assert.True(File.Exists(record));
        }
        finally{TestCleanup.DeleteTree(fixture);}
    }

    [Theory,InlineData("copy"),InlineData("origin"),InlineData("working-change"),
        InlineData("record-change"),InlineData("working-hardlink"),InlineData("unknown-stream"),
        InlineData("record-unknown-stream"),InlineData("exposed-file"),InlineData("exposed-files")]
    public void Old_edit_source_is_held_read_only_and_refuses_changed_or_exposed_objects(string scenario)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(),"Windows source handles required.");
        Assert.SkipWhen(WindowsTokenAuthority.IsHighIntegrity,"Run legacy admission unelevated.");
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-legacy-source-"+Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string root=Path.Combine(fixture,"legacy");var operations=new WindowsFileOperations();
            var store=new EditSessionStore(root,operations);
            byte[] bytes="owned old editor bytes"u8.ToArray();
            using var content=new MemoryContentSource("old.txt",bytes);
            string? mark=scenario=="origin"?"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/old.txt\r\n":null;
            var old=store.CreateRemote("test","test","/old.txt",content,content.GetRevision()!.Value,mark,ct);
            string session=store.SessionDirectory(old),record=Path.Combine(session,"session.json");
            Assert.Equal(EditSessionRecord.CurrentVersion,old.Version);
            if(scenario=="working-hardlink")
            {
                operations.CreateLink(Path.Combine(fixture,"second-link"),old.WorkingPath,LinkKind.Hard,false);
                Assert.NotNull(Record.Exception(()=>WindowsLegacyEditSource.Open(root,old,ct)));
            }
            else if(scenario=="unknown-stream")
            {
                File.WriteAllText(old.WorkingPath+":unexpected","retained data");
                Assert.NotNull(Record.Exception(()=>WindowsLegacyEditSource.Open(root,old,ct)));
            }
            else if(scenario=="record-unknown-stream")
            {
                File.WriteAllText(record+":unexpected","retained data");
                Assert.NotNull(Record.Exception(()=>WindowsLegacyEditSource.Open(root,old,ct)));
            }
            else if(scenario=="exposed-file")
            {
                var file=new FileInfo(old.WorkingPath);var access=file.GetAccessControl();
                access.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid,null),
                    FileSystemRights.Read,AccessControlType.Allow));file.SetAccessControl(access);
                Assert.NotNull(Record.Exception(()=>WindowsLegacyEditSource.Open(root,old,ct)));
            }
            else if(scenario=="exposed-files")
            {
                var folder=new DirectoryInfo(Path.GetDirectoryName(old.WorkingPath)!);var access=folder.GetAccessControl();
                access.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid,null),
                    FileSystemRights.Read,AccessControlType.Allow));folder.SetAccessControl(access);
                Assert.NotNull(Record.Exception(()=>WindowsLegacyEditSource.Open(root,old,ct)));
            }
            else
            {
                using var held=WindowsLegacyEditSource.Open(root,old,ct);
                Assert.Equal(old,held.Session);Assert.Equal(bytes.Length,held.Working.Length);
                Assert.NotEmpty(held.RootIdentity);Assert.NotEmpty(held.RecordSha256);
                if(scenario is "copy" or "origin")
                {
                    using var copy=new MemoryStream();held.CopyTo(copy,ct);Assert.Equal(bytes,copy.ToArray());
                    Assert.Equal(scenario=="origin"?ArchiveOriginState.Present:ArchiveOriginState.Absent,held.Origin.State);
                    held.Review(ct);
                }
                else
                {
                    string path=scenario=="record-change"?record:old.WorkingPath;
                    var error=Record.Exception(()=>File.AppendAllText(path,"changed"));
                    if(error is null)Assert.NotNull(Record.Exception(()=>held.Review(ct)));
                    else Assert.IsAssignableFrom<IOException>(error);
                }
            }
            Assert.True(Directory.Exists(session));Assert.True(File.Exists(record));
        }
        finally{TestCleanup.DeleteTree(fixture);}
    }
}
