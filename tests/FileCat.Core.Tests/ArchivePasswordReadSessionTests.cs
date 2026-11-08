using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Resources;
using FileCat.Core.Jobs;
using FileCat.Core.FileSystem;
using FileCat.Tests;

namespace FileCat.Core.Tests;

public sealed class ArchivePasswordReadSessionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public static TheoryData<bool, bool> Profiles => new() { { false, false }, { false, true }, { true, false }, { true, true } };

    [Theory, MemberData(nameof(Profiles))]
    public async Task Reviewed_edit_reader_binds_the_exact_native_identity_and_complete_archive_hash(bool rar, bool hidden)
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, rar ? "owned.rar" : "owned.7z");
        byte[] expected = ArchivePasswordReadFixture.Create(path, rar, hidden, name: "notes.txt");
        var files = new IdentityAwareTestFiles(); var provider = Provider(temp.Path); var root = ArchiveProvider.ForFile(path);
        using var password = new ArchiveJobPassword(ArchivePasswordReadFixture.Password);
        var profile = rar ? ArchiveWriteProfile.ForRar5(encryption: new(hidden ? ArchiveEncryptionMode.DataAndNames : ArchiveEncryptionMode.Data))
            : ArchiveWriteProfile.ForSevenZip(encryption: new(hidden ? ArchiveEncryptionMode.DataAndNames : ArchiveEncryptionMode.Data));
        ArchiveTransformInput[] inputs = [new(1, expected.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(expected)), new ArchiveTimestamp(1700000000, 0), 0x1a4)];
        ArchiveTransformAddition[] additions = [new("notes.txt", ArchiveMemberType.File, 1)];
        var manifest = (rar ? (ArchiveContainerTransformPlan)ArchiveRar5TransformPlan.Create(profile, null, inputs, [], additions, Ct)
            : ArchiveSevenZipTransformPlan.Create(profile, null, inputs, [], additions, Ct)).Expected;
        string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        var native = new ArchiveFileObservation(new(files.GetFileIdentity(path)!, new FileInfo(path).Length, digest), "parent", Path.GetFileName(path), new('C', 64),
            ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier), new(1700000000, 0), 0x180);
        var target = new ArchiveTargetReview(path, profile, native, new(manifest, digest, native.Version.Length, digest));
        try
        {
            using var view = provider.OpenReadView(root, password, Ct);
            var sink = new Sink(); await provider.EnumerateAsync(root, sink, Ct);
            var item = provider.GetItemRef(root, Assert.Single(sink.Entries));
            Assert.Equal("notes.txt", provider.ReviewedMemberPath(target, item, password, Ct));
            using (var content = provider.OpenReviewedMember(target, "notes.txt", password, Ct))
            {
                byte[] actual = new byte[expected.Length]; Assert.Equal(actual.Length, content.Read(0, actual)); Assert.Equal(expected, actual);
            }
            var wrongIdentity = new ArchiveFileObservation(new("wrong-native-identity", native.Version.Length, digest), native.ParentIdentity,
                native.LeafName, native.MetadataDigest, native.Origin, native.MemberModified, native.MemberMode);
            var wrong = new ArchiveTargetReview(path, profile, wrongIdentity, target.Assessment);
            Assert.Throws<IOException>(() => provider.OpenReviewedMember(wrong, "notes.txt", password, Ct));
            Assert.Throws<IOException>(() => provider.OpenReviewedMember(target, "missing.txt", password, Ct));
        }
        finally { provider.Release(path); }
    }

    [Theory, MemberData(nameof(Profiles))]
    public async Task Password_view_reads_actual_bytes_keeps_protected_flags_and_forgets_after_last_view(bool rar, bool hidden)
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, rar ? "owned.rar" : "owned.7z");
        var expected = ArchivePasswordReadFixture.Create(path, rar, hidden);
        var provider = Provider(temp.Path); var root = ArchiveProvider.ForFile(path);
        try
        {
            Assert.Throws<ArchiveReadPasswordRequiredException>(() => provider.OpenReadView(root, null, Ct));
            using (var wrong = new ArchiveJobPassword("PUBLIC-WRONG"))
                Assert.ThrowsAny<InvalidDataException>(() => provider.OpenReadView(root, wrong, Ct));
            Assert.Equal(0, provider.RetainedIndexes);
            using var password = new ArchiveJobPassword(ArchivePasswordReadFixture.Password);
            using (var view = provider.OpenReadView(root, password, Ct))
            {
                password.Dispose(); // The reader owns encoded bytes, never this borrowed job owner.
                using var other = provider.OpenReadView(root.WithPath("folder"), null, Ct);
                var sink = new Sink(); await provider.EnumerateAsync(other.Location, sink, Ct);
                var entry = Assert.Single(sink.Entries); Assert.True(entry.Has(EntryFlags.Protected)); Assert.Equal("private-界.bin", entry.Name);
                view.Dispose(); Assert.Equal(1, provider.RetainedIndexes);
                using var content = provider.OpenContent(provider.GetItemRef(other.Location, entry)); Assert.NotNull(content);
                byte[] actual = new byte[expected.Length]; Assert.Equal(actual.Length, content.Read(0, actual)); Assert.Equal(expected, actual);
            }
            Assert.Equal(0, provider.RetainedIndexes);
            Assert.Throws<ArchiveReadPasswordRequiredException>(() => provider.OpenReadView(root, null, Ct));
        }
        finally { provider.Release(path); }
    }

    [Theory, MemberData(nameof(Profiles))]
    public async Task Changed_archive_never_inherits_password_even_when_path_length_and_mtime_match(bool rar, bool hidden)
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, rar ? "owned.rar" : "owned.7z");
        ArchivePasswordReadFixture.Create(path, rar, hidden); var provider = Provider(temp.Path); var root = ArchiveProvider.ForFile(path);
        using var password = new ArchiveJobPassword(ArchivePasswordReadFixture.Password);
        using var view = provider.OpenReadView(root, password, Ct);
        try
        {
            // Rewrite the pinned source while the tab remains alive, restoring its visible length and mtime.
            var mtime = File.GetLastWriteTimeUtc(path); var archive = File.ReadAllBytes(path);
            File.WriteAllBytes(path, archive); File.SetLastWriteTimeUtc(path, mtime);
            Assert.Throws<ArchiveReadPasswordRequiredException>(() => provider.OpenReadView(root, null, Ct));
            var sink = new Sink();
            if (!hidden)
            {
                await provider.EnumerateAsync(root.WithPath("folder"), sink, Ct);
                Assert.Null(provider.OpenContent(provider.GetItemRef(root.WithPath("folder"), Assert.Single(sink.Entries))));
            }
        }
        finally { view.Dispose(); provider.Release(path); }
    }

    [Theory, InlineData(false), InlineData(true)]
    public void Encrypted_empty_header_requires_password_and_cancellation_does_not_retain_a_reader(bool rar)
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, rar ? "owned.rar" : "owned.7z");
        ArchivePasswordReadFixture.Create(path, rar, true, empty: true); var provider = Provider(temp.Path); var root = ArchiveProvider.ForFile(path);
        try
        {
            Assert.Throws<ArchiveReadPasswordRequiredException>(() => provider.OpenReadView(root, null, Ct));
            using var password = new ArchiveJobPassword(ArchivePasswordReadFixture.Password);
            using (var stop = new CancellationTokenSource())
            {
                stop.Cancel(); Assert.ThrowsAny<OperationCanceledException>(() => provider.OpenReadView(root, password, stop.Token));
                Assert.Equal(0, provider.RetainedIndexes);
            }
            using (provider.OpenReadView(root, password, Ct)) Assert.Equal(1, provider.RetainedIndexes);
            Assert.Equal(0, provider.RetainedIndexes);
        }
        finally { provider.Release(path); }
    }

    private static ArchiveProvider Provider(string directory)
    {
        var files = new IdentityAwareTestFiles();
        return new(directory, new ProviderRegistry()) { GetSourceIdentity = files.GetFileIdentity, GetOpenedSourceIdentity = files.GetHandleIdentity };
    }

    [Theory, InlineData(false), InlineData(true)]
    public void Borrowed_progressive_content_finishes_after_the_last_tab_closes(bool rar)
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, rar ? "owned.rar" : "owned.7z");
        byte[] expected = ArchivePasswordReadFixture.Create(path, rar, true, (int)ArchiveProvider.MaxMemberInMemory + 4097);
        var provider = Provider(temp.Path); var root = ArchiveProvider.ForFile(path);
        using var password = new ArchiveJobPassword(ArchivePasswordReadFixture.Password);
        using var view = provider.OpenReadView(root, password, Ct);
        using var content = provider.OpenContent(new(root.WithPath("folder"), "private-界.bin", EntryKind.File)); Assert.NotNull(content);
        view.Dispose(); Assert.Equal(0, provider.RetainedIndexes);
        byte[] actual = new byte[65536]; long offset = 0;
        for (int read; (read = content.Read(offset, actual)) != 0; offset += read)
        { Ct.ThrowIfCancellationRequested(); Assert.Equal(expected.AsSpan((int)offset, read).ToArray(), actual.AsSpan(0, read).ToArray()); }
        Assert.Equal(expected.Length, offset);
        content.Dispose();
        Assert.Throws<ArchiveReadPasswordRequiredException>(() => provider.OpenReadView(root, null, Ct));
        provider.Release(path);
    }

    [Theory, InlineData("Rar.encrypted_filesOnly.rar"), InlineData("Rar.encrypted_filesAndHeader.rar")]
    public void Unsupported_older_RAR_passwords_report_a_limitation_instead_of_an_endless_wrong_password_retry(string name)
    {
        using var temp = new TempDir(); string path = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", name);
        var provider = Provider(temp.Path); var root = ArchiveProvider.ForFile(path);
        try
        {
            using var password = new ArchiveJobPassword("PUBLIC-UNSUPPORTED-LEGACY");
            Assert.Contains("not implemented", Assert.Throws<NotSupportedException>(() => provider.OpenReadView(root, password, Ct)).Message);
            Assert.Equal(0, provider.RetainedIndexes);
        }
        finally { provider.Release(path); }
    }

    [Theory, MemberData(nameof(Profiles))]
    public async Task Queued_extraction_retains_password_after_tab_close_and_releases_it_after_completion_or_cancel(bool rar, bool hidden)
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, rar ? "owned.rar" : "owned.7z");
        var expected = ArchivePasswordReadFixture.Create(path, rar, hidden); var provider = Provider(temp.Path);
        var registry = new ProviderRegistry(); registry.Register(new LocalFileSystemProvider()); registry.Register(provider);
        var manager = new JobManager(new IdentityAwareTestFiles(), registry, Path.Combine(temp.Path, "journals")) { MaxConcurrent = 0 };
        var root = ArchiveProvider.ForFile(path); var destination = Location.FileSystem(Directory.CreateDirectory(Path.Combine(temp.Path, "output")).FullName);
        var request = new JobRequest { Kind = JobKind.Copy, Sources = [new(root, "folder", EntryKind.Directory)], Destination = destination, Mode = QueueMode.Queue };
        using var password = new ArchiveJobPassword(ArchivePasswordReadFixture.Password);
        using (var view = provider.OpenReadView(root, password, Ct))
        {
            var job = manager.Submit(request); view.Dispose(); Assert.Equal(1, provider.RetainedIndexes);
            manager.MaxConcurrent = 1; manager.Schedule();
            for (int i = 0; i < 1000 && !job.State.IsFinished(); i++) await Task.Delay(10, Ct);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(expected, File.ReadAllBytes(Path.Combine(destination.Path, "folder", "private-界.bin")));
            Assert.Equal(0, provider.RetainedIndexes);
        }
        manager.MaxConcurrent = 0;
        using (var view = provider.OpenReadView(root, password, Ct))
        {
            var canceled = manager.Submit(request); view.Dispose(); Assert.Equal(1, provider.RetainedIndexes);
            canceled.Cancel(); Assert.Equal(JobState.Canceled, canceled.State); Assert.Equal(0, provider.RetainedIndexes);
        }
        using (var view = provider.OpenReadView(root, password, Ct))
        {
            manager.JobAdded += _ => throw new IOException("Public failed admission fixture");
            Assert.Throws<IOException>(() => manager.Submit(request)); view.Dispose(); Assert.Equal(0, provider.RetainedIndexes);
        }
        provider.Release(path);
    }
    private sealed class Sink : IEnumerationSink
    {
        internal List<EntryData> Entries { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) { foreach (var entry in entries) Entries.Add(entry); }
        public void ReportIssue(string message) => Assert.Fail(message);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Queued_extraction_from_an_inner_archive_retains_its_encrypted_outer_source(bool rar)
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, rar ? "owned.rar" : "owned.7z");
        using var data = new MemoryStream();
        using (var writer = new System.Formats.Tar.TarWriter(data, System.Formats.Tar.TarEntryFormat.Ustar, leaveOpen: true))
            writer.WriteEntry(new System.Formats.Tar.UstarTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "text.bin")
                { DataStream = new MemoryStream("nested queued content"u8.ToArray()) });
        ArchivePasswordReadFixture.Create(path, rar, true, payload: data.ToArray(), name: "nested.tar");
        var registry = new ProviderRegistry(); registry.Register(new LocalFileSystemProvider()); var files = new IdentityAwareTestFiles();
        var provider = new ArchiveProvider(Path.Combine(temp.Path, "scratch"), registry)
            { GetSourceIdentity = files.GetFileIdentity, GetOpenedSourceIdentity = files.GetHandleIdentity }; registry.Register(provider);
        var root = ArchiveProvider.ForFile(path); var nested = new Location(Schemes.Archive, "", root.WithPath("nested.tar"));
        using var password = new ArchiveJobPassword(ArchivePasswordReadFixture.Password); using var view = provider.OpenReadView(root, password, Ct);
        var sink = new Sink(); await provider.EnumerateAsync(nested, sink, Ct);
        var destination = Location.FileSystem(Directory.CreateDirectory(Path.Combine(temp.Path, "output")).FullName);
        var manager = new JobManager(files, registry, Path.Combine(temp.Path, "journals")) { MaxConcurrent = 0 };
        var job = manager.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [provider.GetItemRef(nested, Assert.Single(sink.Entries))], Destination = destination });
        view.Dispose(); Assert.Equal(2, provider.RetainedIndexes); // Both the queued member and its outer crypto reader still exist.
        manager.MaxConcurrent = 1; manager.Schedule();
        for (int i = 0; i < 1000 && !job.State.IsFinished(); i++) await Task.Delay(10, Ct);
        Assert.Equal(JobState.Completed, job.State); Assert.Equal("nested queued content", File.ReadAllText(Path.Combine(destination.Path, "text.bin")));
        Assert.Equal(1, provider.RetainedIndexes); provider.Release(path);
    }
}
