using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Tests;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

// Store/adapter contracts over real private files; these cases do not qualify native publication.
public sealed class ArchiveEditSessionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public static IEnumerable<object[]> Profiles() => ArchiveNativeJobCases.EditProfiles();

    [Theory,MemberData(nameof(Profiles))]
    public void Native_storage_schema_survives_verified_archive_acknowledgement(ArchiveWriteFormat format,int mode)
    {
        if(OperatingSystem.IsWindows())Assert.Skip("Unix native store adapter case; Windows uses its platform lane.");
        using var f=new Fixture(ArchiveNativeJobCases.Profile(format,mode),native:true);
        var record=f.Store.CreateArchive(f.Source,"notes.txt",Ct);
        Assert.Equal(EditSessionRecord.NativeStorageVersion,record.Version);Assert.Equal(EditState.Unchanged,f.Store.StateOf(record));
        string frozen=Path.Combine(Directory.CreateDirectory(Path.Combine(f.Temp.Path,"commit-fixture")).FullName,"notes.txt");
        File.WriteAllBytes(frozen,f.Before);
        using var copy=new EditCommitCopy(frozen,record.BaseSha256,Crc32.Append(0,f.Before),f.Before.Length);
        var published=f.Published(f.Before);
        var updated=f.Store.CommittedArchive(record,published,copy,Receipt(published.Target),Ct);
        Assert.Equal(EditSessionRecord.NativeStorageVersion,updated.Version);Assert.Equal(1,updated.StorageRevision);
        Assert.Equal(updated,Assert.Single(f.Store.LoadAll()));Assert.Equal(EditState.Unchanged,f.Store.StateOf(updated));
    }

    [Theory, MemberData(nameof(Profiles))]
    public void Explicit_profile_survives_restart_and_only_verified_published_bytes_advance_the_baseline(ArchiveWriteFormat format, int mode)
    {
        using var f = new Fixture(ArchiveNativeJobCases.Profile(format, mode));
        var record = f.Store.CreateArchive(f.Source, "notes.txt", Ct);
        Assert.True(record.IsSupported); Assert.True(record.IsNativeArchive); Assert.False(record.IsLegacyZip);
        Assert.Equal(record, Assert.Single(f.Store.LoadAll())); Assert.Equal(f.Profile, record.ArchiveProfile);
        Assert.Equal(f.Before, File.ReadAllBytes(record.WorkingPath)); Assert.Equal(1, f.Source.Origins);
        Assert.Equal(record, f.Store.FindArchive(record.ArchivePath, format, record.ArchiveIdentity, record.MemberPath));
        Assert.Null(f.Store.Find(record.ArchivePath, record.MemberPath));
        Assert.Equal(CommitCheck.Ready, EditSessionStore.ReviewArchive(record, f.Source.Target));
        File.WriteAllText(record.WorkingPath, "authorized first edit");
        using var copy = f.Store.PrepareCommit(record, f.Temp.Path, Ct);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(0, (int)File.GetUnixFileMode(f.Store.SessionDirectory(record)) & 0x3f);
            Assert.Equal(0, (int)File.GetUnixFileMode(record.WorkingPath) & 0x3f);
            Assert.Equal(0, (int)File.GetUnixFileMode(Path.GetDirectoryName(copy.Path)!) & 0x3f);
            Assert.Equal(0, (int)File.GetUnixFileMode(copy.Path) & 0x3f);
        }
        var plan = EditSessionStore.CommitArchivePlan(record, f.Source.Target, copy, false);
        var change = Assert.Single(plan.Changes); Assert.Equal(ArchiveChangeKind.Replace, change.Kind);
        Assert.Equal("notes.txt", change.MemberPath); Assert.Equal(0, change.Ordinal); Assert.Equal(copy.Path, change.SourcePath);
        Assert.Equal(f.Source.Target.Native, plan.NativeBaseline); Assert.Equal(f.Profile, plan.Profile);
        File.WriteAllText(record.WorkingPath, "a later editor save must remain modified");
        var actual = f.Published(File.ReadAllBytes(copy.Path));
        var receipt = Receipt(actual.Target);
        var updated = f.Store.CommittedArchive(record, actual, copy, receipt, Ct);
        Assert.Equal(copy.Sha256, updated.BaseSha256); Assert.Equal(actual.Target.Baseline, updated.Baseline);
        Assert.NotNull(updated.LastCommitUtc); Assert.Equal(updated, Assert.Single(f.Store.LoadAll()));
        Assert.Equal(EditState.Modified, f.Store.StateOf(updated)); Assert.Equal(1, actual.Closes);
        Assert.Equal("a later editor save must remain modified", File.ReadAllText(record.WorkingPath));
        // Neither passwords nor native authority are persisted in a record.
        string json = File.ReadAllText(Path.Combine(f.Store.SessionDirectory(updated), "session.json"));
        Assert.DoesNotContain(ArchiveNativeJobCases.Password, json); Assert.DoesNotContain("SafeFileHandle", json);
    }

    // AW7: a compressed file's one member has the synthetic raw name in the record and every plan, and is shown by the name
    // it had when the edit started; a commit is exactly one replacement of that member.
    [Theory, InlineData(ArchiveWriteFormat.Gzip), InlineData(ArchiveWriteFormat.Bzip2), InlineData(ArchiveWriteFormat.Zstd)]
    public void A_compressed_file_session_edits_the_synthetic_member_and_shows_the_name_it_started_with(ArchiveWriteFormat format)
    {
        using var f = new Fixture(new(format));
        var record = f.Store.CreateArchive(f.Source, "report.txt", Ct);
        Assert.True(record.IsSupported); Assert.True(record.IsNativeArchive); Assert.Equal(ArchiveStandalone.MemberName, record.MemberPath);
        Assert.Equal("report.txt", record.MemberDisplay); Assert.Equal("report.txt", record.DisplayName);
        Assert.Equal("report.txt in " + record.ArchivePath, record.DisplayContainer);
        var loaded = Assert.Single(f.Store.LoadAll()); Assert.Equal(record, loaded); Assert.Equal("report.txt", loaded.MemberDisplay);
        Assert.Equal(CommitCheck.Ready, EditSessionStore.ReviewArchive(record, f.Source.Target));
        File.WriteAllText(record.WorkingPath, "edited compressed contents");
        using var copy = f.Store.PrepareCommit(record, f.Temp.Path, Ct);
        var change = Assert.Single(EditSessionStore.CommitArchivePlan(record, f.Source.Target, copy, false).Changes);
        Assert.Equal((ArchiveChangeKind.Replace, ArchiveStandalone.MemberName, (int?)0, copy.Path), (change.Kind, change.MemberPath, change.Ordinal, change.SourcePath));
    }

    [Theory, InlineData("outer-bytes"), InlineData("outer-identity"), InlineData("parent"), InlineData("outer-metadata"),
        InlineData("member-bytes"), InlineData("member-metadata"), InlineData("missing"), InlineData("duplicate")]
    public void Changed_archive_requires_explicit_rebase_and_changed_member_never_becomes_an_add(string change)
    {
        using var f = new Fixture(new(ArchiveWriteFormat.Tar)); var record = f.Store.CreateArchive(f.Source, "notes.txt", Ct);
        using var copy = f.Store.PrepareCommit(record, f.Temp.Path, Ct);
        var original = f.Source.Target; var member = Assert.Single(original.Assessment.Manifest.Members);
        bool changedMember = change.StartsWith("member", StringComparison.Ordinal) || change is "missing" or "duplicate";
        var members = change switch
        {
            "member-bytes" => new[] { new ArchiveManifestMember(member.Name, member.Type, 0, member.Length, new('B', 64), member.Metadata) },
            "member-metadata" => MakeManifest(f.Profile, f.Before, new(1700000100, 0)).Members.ToArray(),
            "missing" => [],
            "duplicate" => new[] { member, new ArchiveManifestMember(member.Name, member.Type, 1, member.Length, member.Sha256, member.Metadata) },
            _ => original.Assessment.Manifest.Members.ToArray(),
        };
        var manifest = new ArchiveWriteManifest(f.Profile, members, original.Assessment.Manifest.Features);
        var native = new ArchiveFileObservation(new(change == "outer-identity" ? "other" : original.Native.Version.Identity, 2048,
            change == "outer-bytes" ? new('E', 64) : original.Native.Version.Sha256), change == "parent" ? "other-parent" : original.Native.ParentIdentity,
            original.Native.LeafName, change == "outer-metadata" ? new('D', 64) : original.Native.MetadataDigest,
            original.Native.Origin, original.Native.MemberModified, original.Native.MemberMode);
        var target = new ArchiveTargetReview(original.Path, f.Profile, native, new(manifest, new('F', 64), 2048, native.Version.Sha256));
        Assert.Equal(changedMember ? CommitCheck.MemberChanged : CommitCheck.ArchiveChanged, EditSessionStore.ReviewArchive(record, target));
        Assert.Throws<IOException>(() => EditSessionStore.CommitArchivePlan(record, target, copy, false));
        if (changedMember) Assert.Throws<IOException>(() => EditSessionStore.CommitArchivePlan(record, target, copy, true));
        else Assert.Equal(target.Native, EditSessionStore.CommitArchivePlan(record, target, copy, true).NativeBaseline);
        Assert.Equal(record, Assert.Single(f.Store.LoadAll()));
    }

    [Theory, InlineData("wrong-copy"), InlineData("wrong-readback"), InlineData("changed-source"), InlineData("close-failure"),
        InlineData("wrong-manifest"), InlineData("wrong-identity"), InlineData("canceled"), InlineData("partial"), InlineData("caveat"),
        InlineData("changed-revision"), InlineData("changed-after-close")]
    public void Uncertain_publication_or_readback_keeps_previous_durable_evidence_and_working_bytes(string failure)
    {
        using var f = new Fixture(new(ArchiveWriteFormat.Tar)); var record = f.Store.CreateArchive(f.Source, "notes.txt", Ct);
        File.WriteAllText(record.WorkingPath, "authorized first edit"); using var copy = f.Store.PrepareCommit(record, f.Temp.Path, Ct);
        var actual = f.Published(failure == "wrong-copy" ? "different bytes"u8.ToArray() : File.ReadAllBytes(copy.Path));
        if (failure == "wrong-readback") File.WriteAllText(actual.ContentPath, new string('x', (int)copy.Length));
        actual.FailReview = failure == "changed-source"; actual.FailClose = failure == "close-failure";
        actual.FailPartial = failure == "partial"; actual.FailCaveat = failure == "caveat";
        actual.FailRevision = failure == "changed-revision"; actual.FailAfterCloseReview = failure == "changed-after-close";
        var receipt = Receipt(actual.Target);
        if (failure == "wrong-manifest") receipt = receipt with { ManifestDigest = new('E', 64) };
        if (failure == "wrong-identity") receipt = receipt with { Native = f.Source.Target.Native };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct); if (failure == "canceled") stop.Cancel();
        byte[] metadata = File.ReadAllBytes(Path.Combine(f.Store.SessionDirectory(record), "session.json"));
        var error = Record.Exception(() => f.Store.CommittedArchive(record, actual, copy, receipt, stop.Token));
        Assert.True(error is IOException or OperationCanceledException, error?.ToString());
        Assert.Equal(metadata, File.ReadAllBytes(Path.Combine(f.Store.SessionDirectory(record), "session.json")));
        Assert.Equal(record, Assert.Single(f.Store.LoadAll())); Assert.Equal("authorized first edit", File.ReadAllText(record.WorkingPath));
    }

    [Theory, InlineData("changed-source"), InlineData("wrong-bytes"), InlineData("close-failure"), InlineData("origin-failure"), InlineData("canceled")]
    public void Failed_extraction_does_not_publish_or_leave_an_unowned_session(string failure)
    {
        using var f = new Fixture(new(ArchiveWriteFormat.Tar)); f.Source.FailReview = failure == "changed-source";
        f.Source.FailClose = failure == "close-failure"; f.Source.FailOrigin = failure == "origin-failure";
        if (failure == "wrong-bytes") File.WriteAllText(f.Source.ContentPath, "wrong extracted bytes");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct); if (failure == "canceled") stop.Cancel();
        var error = Record.Exception(() => f.Store.CreateArchive(f.Source, "notes.txt", stop.Token));
        Assert.True(error is IOException or OperationCanceledException, error?.ToString()); Assert.Empty(f.Store.LoadAll());
        if (Directory.Exists(f.Store.Root)) Assert.Empty(Directory.EnumerateFileSystemEntries(f.Store.Root));
    }

    private static ArchiveEditPublication Receipt(ArchiveTargetReview target) => new(target.Path, target.Profile, target.Native, target.Assessment.Manifest.Digest);
    private static ArchiveWriteManifest MakeManifest(ArchiveWriteProfile profile, byte[] bytes, ArchiveTimestamp time)
    {
        ArchiveTransformInput[] inputs = [new(1, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), time)];
        ArchiveTransformAddition[] additions = [new(ArchiveStandalone.IsStandalone(profile.Format) ? ArchiveStandalone.MemberName : "notes.txt", ArchiveMemberType.File, 1)];
        return (profile.Format switch
        {
            ArchiveWriteFormat.Gzip or ArchiveWriteFormat.Bzip2 or ArchiveWriteFormat.Zstd => ArchiveStandaloneTransformPlan.Create(profile, null, inputs, [], additions, Ct),
            ArchiveWriteFormat.Rar5 => (ArchiveContainerTransformPlan)ArchiveRar5TransformPlan.Create(profile, null, inputs, [], additions, Ct),
            ArchiveWriteFormat.SevenZip => ArchiveSevenZipTransformPlan.Create(profile, null, inputs, [], additions, Ct),
            ArchiveWriteFormat.Zip => ArchiveZipTransformPlan.Create(profile, null, inputs, [], additions, Ct),
            _ => ArchiveTarTransformPlan.Create(profile, null, inputs, [], additions, Ct),
        }).Expected;
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly TempDir Temp = new(); internal readonly IdentityAwareTestFiles Files = new();
        internal readonly ArchiveWriteProfile Profile; internal readonly EditSessionStore Store; internal readonly Source Source;
        internal readonly byte[] Before = "complete original editable bytes"u8.ToArray();
        internal Fixture(ArchiveWriteProfile profile,bool native=false)
        {
            string root=Path.Combine(Temp.Path,"sessions");
            if(native&&OperatingSystem.IsMacOS()&&root.StartsWith("/var/",StringComparison.Ordinal))root="/private"+root;
            Profile = profile; Store = new(root,Files,native?UnixEditDirectoryLease.OpenRoot:null); Source = Published(Before, original: true);
        }
        internal Source Published(byte[] bytes, bool original = false)
        {
            string content = Path.Combine(Temp.Path, Guid.NewGuid().ToString("N")); File.WriteAllBytes(content, bytes);
            var manifest = MakeManifest(Profile, bytes, new(1700000000, 0)); string revision = new(original ? 'A' : 'B', 64);
            var native = new ArchiveFileObservation(new(original ? "original" : "published", 2048, revision), "parent", "owned.archive", new('C', 64),
                ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier), new(1700000000, 0), 0x180);
            return new(new(Path.Combine(Temp.Path, "owned.archive"), Profile, native, new(manifest, revision, 2048, revision)), content, Files);
        }
        public void Dispose() => Temp.Dispose();
    }
    private sealed class Source(ArchiveTargetReview target, string path, IdentityAwareTestFiles files) : IArchiveEditSource
    {
        public ArchiveTargetReview Target { get; } = target;
        public ArchiveManifestMember Member => Target.Assessment.Manifest.Members.Single();
        internal string ContentPath => path;
        internal bool FailReview, FailClose, FailOrigin, FailPartial, FailCaveat, FailRevision, FailAfterCloseReview; internal int Closes, Origins;
        public IContentSource OpenContent(CancellationToken ct) => new Content(this,
            new LocalFileSystemProvider().OpenContent(ItemRef.ForFileSystemPath(path, EntryKind.File))!);
        public void ReviewSource(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if (FailReview || FailAfterCloseReview && Closes != 0) throw new IOException("Public changed source"); }
        public string WorkingIdentity(SafeFileHandle handle) => files.GetHandleIdentity(handle)!;
        public void PreserveOrigin(FileStream workingFile,EditDirectoryLease? files,string workingPath,CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Origins++; if (FailOrigin) throw new IOException("Public origin failure"); }
        private sealed class Content(Source owner, IContentSource inner) : IContentSource, IPartialContent
        {
            private bool read; private int revisionReads;
            public string DisplayName => inner.DisplayName; public long Length => inner.Length; public bool CanSeek => inner.CanSeek;
            public string? LocalPath => inner.LocalPath;
            public IReadOnlyList<(long Offset, long Length)> MissingRanges => owner.FailPartial && read ? [(0, 1)] : [];
            public string? Caveat => owner.FailCaveat && read ? "Public incomplete readback caveat" : null;
            public ContentRevision? GetRevision()
            {
                var revision = inner.GetRevision();
                return owner.FailRevision && ++revisionReads > 1 && revision is { } r ? r with { ModifiedTicks = r.ModifiedTicks + 1 } : revision;
            }
            public int Read(long offset, Span<byte> buffer) { int count = inner.Read(offset, buffer); read = true; return count; }
            public void Dispose() { inner.Dispose(); owner.Closes++; if (owner.FailClose) throw new IOException("Public edit close failure"); }
        }
    }
}
