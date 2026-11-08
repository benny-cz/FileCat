using System.IO.Compression;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class EditSessionVersionTests
{
    [Theory, InlineData(1,false), InlineData(1,true),InlineData(2,false),InlineData(2,true)]
    public void Legacy_archive_and_remote_records_remain_readable_without_rewriting(int version,bool remote)
    {
        using var temp = new TempDir(); var store = new EditSessionStore(temp.Path, new PortableFileOperations());
        string id = Guid.NewGuid().ToString("N"), directory = Directory.CreateDirectory(Path.Combine(temp.Path, id)).FullName;
        string working = Path.Combine(directory, "owned.txt"); File.WriteAllText(working, "owned legacy edit");
        var record = new EditSessionRecord { Version = version, Id = id, Kind = remote ? EditSessionRecord.RemoteKind : EditSessionRecord.ArchiveKind,
            WorkingPath = working, ArchivePath = Path.Combine(temp.Path, "renamed-as.rar"), MemberPath = "owned.txt", RemotePath = "/owned.txt" };
        string metadata = Path.Combine(directory, "session.json"); File.WriteAllText(metadata, JsonSerializer.Serialize(record));
        var bytes = File.ReadAllBytes(metadata); var loaded = Assert.Single(store.LoadAll());
        Assert.Equal(version, loaded.Version); Assert.True(loaded.IsSupported); Assert.Equal(remote, loaded.IsRemote);
        Assert.Equal(!remote, loaded.IsLegacyZip); Assert.Equal(bytes, File.ReadAllBytes(metadata)); Assert.Equal("owned legacy edit", File.ReadAllText(loaded.WorkingPath));
    }

    [Theory, InlineData(4), InlineData(999)]
    public void Future_versions_remain_visible_and_cannot_be_rewritten_or_discarded(int version)
    {
        using var temp = new TempDir(); var store = new EditSessionStore(temp.Path, new PortableFileOperations());
        string id = Guid.NewGuid().ToString("N"), directory = Directory.CreateDirectory(Path.Combine(temp.Path, id)).FullName;
        string working = Path.Combine(directory, "future.txt"); File.WriteAllText(working, "keep future bytes");
        string metadata = Path.Combine(directory, "session.json");
        File.WriteAllText(metadata, JsonSerializer.Serialize(new { Id = id, Version = version, WorkingPath = working,
            ArchiveFormat = new { FutureDiscriminator = "not a current enum" }, ArchiveProfile = new { UnknownType = "never instantiated" } }));
        byte[] bytes = File.ReadAllBytes(metadata); var loaded = Assert.Single(store.LoadAll());
        Assert.False(loaded.IsSupported); Assert.Equal(working, loaded.WorkingPath); Assert.Equal(directory, store.SessionDirectory(loaded));
        Assert.Throws<NotSupportedException>(() => store.Save(loaded)); Assert.Throws<NotSupportedException>(() => store.Discard(loaded));
        Assert.Equal(bytes, File.ReadAllBytes(metadata)); Assert.Equal("keep future bytes", File.ReadAllText(working));
    }

    [Theory, InlineData("session.json"), InlineData("SESSION.JSON")]
    public void Editing_a_member_named_like_session_metadata_keeps_both_original_bytes_and_its_own_file_name(string name)
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, "owned.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) writer.Write("edit content, never JSON metadata");
        var provider = new ZipProvider(Path.Combine(temp.Path, "scratch")); var store = new EditSessionStore(Path.Combine(temp.Path, "sessions"), new PortableFileOperations());
        try
        {
            var record = store.Create(provider, new(ZipProvider.ForFile(path), name, EntryKind.File), TestContext.Current.CancellationToken);
            Assert.Equal(EditSessionRecord.CurrentVersion, record.Version); Assert.Equal(name, Path.GetFileName(record.WorkingPath));
            Assert.Equal("edit content, never JSON metadata", File.ReadAllText(record.WorkingPath));
            Assert.NotEqual(record.WorkingPath, Path.Combine(store.SessionDirectory(record), "session.json"));
            Assert.Equal(record, Assert.Single(store.LoadAll()));
        }
        finally { provider.Release(path); }
    }
}
