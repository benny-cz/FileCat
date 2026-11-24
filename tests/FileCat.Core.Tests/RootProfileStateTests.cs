using System.Formats.Tar;
using System.IO.Compression;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.Diagnostics;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;
using FileCat.Core.Platform;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Verification;
using static FileCat.Tests.ProcessAuthorityScopes;

namespace FileCat.Core.Tests;

/// <summary>
/// I655: FileCat running as root keeps out of the saved state of the profile it finds, as an administrator window does:
/// a root FileCat started with the caller's HOME (sudo -E, or sudo where its configuration keeps HOME) read the caller's
/// settings and journals, acted on them as root, and left files there only root could use. Each case runs with the
/// decision assumed (every platform) and, in a test host started as root or elevated, with the process's own decision.
/// The controls take the process to be an ordinary one.
/// </summary>
public sealed class RootProfileStateTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Saved_state_loads_as_the_defaults_and_nothing_is_saved(bool assumed)
    {
        string path = Path.Combine(_dir.Path, "settings.json");
        AppSettings Load(out StateLoadStatus status) =>
            JsonFileStore.Load(path, StateJsonContext.Default.AppSettings, AppSettings.CurrentSchema, () => new AppSettings(), out status);
        using (Ordinary())
        {
            JsonFileStore.Save(path, new AppSettings { DateFormat = "yyyy" }, StateJsonContext.Default.AppSettings);
            Assert.Equal("yyyy", Load(out var loaded).DateFormat);
            Assert.Equal(StateLoadStatus.Loaded, loaded);
        }
        byte[] saved = File.ReadAllBytes(path);
        string fresh = Path.Combine(_dir.Path, "fresh", "history.json");
        using (ElevatedOrRoot(assumed))
        {
            var settings = Load(out var status);
            Assert.Equal(StateLoadStatus.UnavailableReadOnly, status);
            Assert.Equal(new AppSettings().DateFormat, settings.DateFormat);
            string refusal = $"FileCat does not save user-profile state while running {RunningAs}. {Advice} to save changes.";
            settings.DateFormat = "dd.MM.yyyy";
            Assert.Equal(refusal, Assert.Throws<IOException>(() => JsonFileStore.Save(path, settings, StateJsonContext.Default.AppSettings)).Message);
            Assert.Equal(refusal, Assert.Throws<IOException>(() =>
                JsonFileStore.SaveNew(fresh, new HistoryState(), StateJsonContext.Default.HistoryState)).Message);
        }
        Assert.Equal(saved, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
        Assert.False(Directory.Exists(Path.GetDirectoryName(fresh)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Edit_sessions_are_neither_listed_nor_written(bool assumed)
    {
        string root = Path.Combine(_dir.Path, "edit-sessions");
        string id = Guid.NewGuid().ToString("N");
        var store = new EditSessionStore(root, new PortableFileOperations());
        using (Ordinary())
        {
            Directory.CreateDirectory(Path.Combine(root, id));
            store.Save(new EditSessionRecord { Id = id, CreatedUtc = DateTime.UtcNow });
            Assert.Single(store.LoadAll());
        }
        string record = Path.Combine(root, id, "session.json");
        byte[] saved = File.ReadAllBytes(record);
        using (ElevatedOrRoot(assumed))
        {
            Assert.Empty(store.LoadAll());
            var error = Assert.Throws<IOException>(() => store.Save(new EditSessionRecord { Id = id, CreatedUtc = DateTime.UtcNow.AddDays(1) }));
            Assert.Equal($"FileCat does not read or write user-profile edit sessions while running {RunningAs}. {Advice}.", error.Message);
        }
        Assert.Equal(saved, File.ReadAllBytes(record));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Job_journals_are_neither_reviewed_nor_pruned(bool assumed)
    {
        string journals = _dir.Dir("journal");
        string source = _dir.File("source.txt", "source");
        var job = new Job(new JobRequest
        {
            Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = Location.FileSystem(_dir.Dir("destination")),
        }, "Copy", "local", [], []);
        string path;
        // A journal without its end record: an operation that was interrupted.
        using (Ordinary())
        using (var journal = JobJournal.Create(journals, job))
            path = journal.Path;
        byte[] written = File.ReadAllBytes(path);
        using (ElevatedOrRoot(assumed))
            Assert.Empty(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        Assert.Equal(written, File.ReadAllBytes(path));
        using (Ordinary())
            Assert.Equal(path, Assert.Single(JournalRecovery.Scan(journals)).JournalPath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_interrupted_rename_is_neither_reviewed_nor_finished(bool assumed)
    {
        string via = _dir.File("via", "reviewed");
        string original = Path.Combine(_dir.Path, "original"), target = Path.Combine(_dir.Path, "target");
        var intent = new PendingIntent(1, JobJournal.RenameViaOp, original, target, null) { Via = via };
        var files = new PortableFileOperations();
        var provider = new LocalFileSystemProvider();
        JournalRecovery.RenameReview review;
        using (Ordinary())
            review = Assert.IsType<JournalRecovery.RenameReview>(JournalRecovery.ReviewRename(intent, files, provider));
        using (ElevatedOrRoot(assumed))
        {
            Assert.Null(JournalRecovery.ReviewRename(intent, files, provider));
            var result = JournalRecovery.FinishReviewedRename(review, files, provider);
            Assert.False(result.Resolved);
            Assert.Equal(0, result.Finished);
            Assert.Equal([$"FileCat does not apply user-profile rename recovery while running {RunningAs}."], result.Report);
        }
        Assert.Equal("reviewed", File.ReadAllText(via));
        Assert.False(File.Exists(original));
        Assert.False(File.Exists(target));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Storage_history_is_not_read_accounted_or_exported(bool assumed)
    {
        const string reviewed = "Storage history must be reviewed in the normal user process.";
        var id = Guid.NewGuid();
        string journal = Path.Combine(_dir.Path, $"job-storage-{DateTime.UtcNow:yyyyMMddHHmmss}-{id:N}.fcj");
        var prefix = new StorageJournalPrefix(id, 1, 1, new string('A', 64));
        using var retained = new OwnedRetainedEvidence();
        using (ElevatedOrRoot(assumed))
        {
            Assert.Equal(reviewed, Assert.Throws<IOException>(() => JournalRecovery.ReadStorageHistory(journal, ct: Ct)).Message);
            Assert.Equal(reviewed, Assert.Throws<IOException>(() => new StorageHistoryDiscovery(_dir.Path, Ct)).Message);
            Assert.Equal("Storage evidence must be read in the normal user process.",
                Assert.Throws<IOException>(() => JournalRecovery.ReadStorageCalls(journal, prefix, ct: Ct)).Message);
            Assert.Equal("Storage evidence must be exported in the normal user process.",
                Assert.Throws<IOException>(() => JournalRecovery.CopyStorageCalls(journal, prefix, () => new MemoryStream(), Ct)).Message);
            Assert.Equal("Storage history must be accounted in the normal user process.",
                Assert.Throws<IOException>(() => JournalRecovery.AccountStorageHistory(_dir.Path, Ct)).Message);
            Assert.Equal(reviewed, Assert.Throws<IOException>(() =>
                JournalRecovery.ReadRetainedStorageReview(retained, new StorageReviewArtifactInfo("storage-unnamed", "identity", 1), Ct)).Message);
        }
        using (Ordinary())
            Assert.Null(JournalRecovery.ReadRetainedStorageReview(retained, new StorageReviewArtifactInfo("storage-unnamed", "identity", 1), Ct));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_folder_is_not_moved_back_by_undo(bool assumed)
    {
        string moved = _dir.Dir("moved");
        File.WriteAllText(Path.Combine(moved, "child.txt"), "child");
        string original = Path.Combine(_dir.Path, "original");
        var job = new Job(new JobRequest { Kind = JobKind.Move }, "Original operation", "local", [], []);
        job.AddUndo(new UndoStep(UndoKind.MoveBack, moved, original, 0, 0));
        job.SetState(JobState.Completed);
        using var claim = Assert.IsType<UndoReservation>(job.TryReserveUndo());
        UndoReport report;
        using (ElevatedOrRoot(assumed))
        {
            var runner = new UndoRunner(claim.Plan, new PortableFileOperations(), () => { });
            runner.Run();
            report = runner.Report("not finished");
        }
        var outcome = Assert.Single(report.Outcomes);
        Assert.Equal(UndoOutcomeKind.Kept, outcome.Outcome);
        Assert.Equal(UndoRunner.ElevatedDirectoryUndoRefusal(OperatingSystem.IsWindows()), outcome.Reason);
        Assert.Equal("child", File.ReadAllText(Path.Combine(moved, "child.txt")));
        Assert.False(Directory.Exists(original));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Nothing_is_staged_for_dragging_out_of_an_archive(bool assumed)
    {
        string archive = Path.Combine(_dir.Path, "drag.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("inside.txt").Open())) writer.Write("inside");
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var zips = new ZipProvider(Path.Combine(_dir.Path, "zip-temp"));
        providers.Register(zips);
        var member = new ItemRef(zips.GetContainerLocation(archive)!, "inside.txt", EntryKind.File, size: 6);
        string temp = Path.Combine(_dir.Path, "temp");
        using (ElevatedOrRoot(assumed))
        {
            var (paths, refusal) = DragStaging.Stage([member], providers, new PortableFileOperations(), temp, Ct);
            Assert.Null(paths);
            Assert.Equal(DragStaging.ElevatedRefusal(OperatingSystem.IsWindows()), refusal);
        }
        Assert.False(Directory.Exists(temp));
        using (Ordinary())
        {
            var (paths, refusal) = DragStaging.Stage([member], providers, new PortableFileOperations(), temp, Ct);
            Assert.Null(refusal);
            Assert.Equal("inside", File.ReadAllText(Assert.Single(paths!)));
        }
        zips.Release(archive);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Verification_records_are_neither_read_nor_kept(bool assumed)
    {
        string file = Path.Combine(_dir.Path, "cache", "verification.jsonl");
        var result = new VerificationResult(VerificationState.Matches, "matches", []);
        using (Ordinary())
        {
            new VerificationCache(file).Put("kept", result);
            Assert.NotNull(new VerificationCache(file).Get("kept"));
        }
        byte[] kept = File.ReadAllBytes(file);
        using (ElevatedOrRoot(assumed))
        {
            var cache = new VerificationCache(file);
            Assert.Null(cache.Get("kept"));
            cache.Put("new", result);
            Assert.NotNull(cache.Get("new"));
        }
        Assert.Equal(kept, File.ReadAllBytes(file));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Files_beside_checksum_files_are_not_checked_by_themselves(bool assumed)
    {
        string folder = _dir.Dir("checked");
        string file = Path.Combine(folder, "a.txt");
        File.WriteAllText(file, "alpha");
        File.WriteAllText(Path.Combine(folder, "checks.sfv"), "a.txt 00000000\n");
        static VerificationService Service() => new(new VerificationCache(null), () => 1 << 20, () => []);
        using (ElevatedOrRoot(assumed))
        {
            var service = Service();
            int changed = 0;
            service.SidecarsChanged += _ => changed++;
            service.FolderChanged(folder, Ct);
            Assert.Equal(0, changed);
            Assert.Null(service.Automatic(file, Ct));
            // An explicit scan still reads the checksum file; the status line's summary still says nothing.
            Assert.Single(service.SidecarsOf(folder, Ct).Checksums("a.txt"));
            Assert.Equal(default, service.Summary(folder, ["a.txt"]));
        }
        using (Ordinary())
        {
            var service = Service();
            int changed = 0;
            service.SidecarsChanged += _ => changed++;
            service.FolderChanged(folder, Ct);
            Assert.Equal(1, changed);
            Assert.NotNull(service.Automatic(file, Ct));
            Assert.NotEqual(default, service.Summary(folder, ["a.txt"]));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpenPGP_signatures_are_not_checked_with_the_profile_configuration(bool assumed)
    {
        string signed = _dir.File("signed.txt", "signed");
        string signature = _dir.File("signed.txt.asc", "-----BEGIN PGP SIGNATURE-----\n");
        string advice = char.ToLowerInvariant(Advice[0]) + Advice[1..];
        string expected = $"OpenPGP verification is unavailable while FileCat runs {RunningAs}; {advice} to avoid running user-profile GnuPG configuration";
        using (ElevatedOrRoot(assumed))
        {
            var onDisk = OpenPgp.Verify(signature, signed, Ct);
            Assert.Equal(VerificationState.SignatureUnchecked, onDisk.State);
            Assert.Equal(expected, onDisk.Text);
            var inMemory = OpenPgp.Verify(signature, "signed"u8.ToArray(), Ct);
            Assert.Equal(VerificationState.SignatureUnchecked, inMemory.State);
            Assert.Equal(expected, inMemory.Text);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_viewed_archive_member_is_kept_in_memory_not_spooled(bool assumed)
    {
        string spool = Path.Combine(_dir.Path, "member-spool");
        byte[] payload = new byte[64 * 1024];
        new Random(655).NextBytes(payload);
        var limits = new MemberLimits(payload.Length, payload.Length, payload.Length + 1L, 100, Crc32.HashToUInt32(payload));
        int diskChecks = 0;
        ProgressiveContent Member() => new("I655 member", new object(), () => new MemoryStream(payload, writable: false), limits,
            1L << 20, spool, null, null, () => diskChecks++);
        var buffer = new byte[payload.Length];
        using (ElevatedOrRoot(assumed))
        using (var member = Member())
        {
            Assert.Equal(payload.Length, member.Read(0, buffer));
            Assert.Equal(payload, buffer);
            Assert.False(Directory.Exists(spool));
        }
        Assert.Equal(0, diskChecks);
        using (Ordinary())
        using (var member = Member())
        {
            Assert.Equal(payload.Length, member.Read(0, buffer));
            Assert.Single(Directory.GetFiles(spool, "member-*.tmp"));
        }
        Assert.NotEqual(0, diskChecks);
    }

    [Fact]
    public void A_member_kept_in_memory_stops_at_64_MiB()
    {
        const long limit = 64L * 1024 * 1024;
        var payload = new byte[limit + 1024 * 1024];
        payload.AsSpan().Fill((byte)'A');
        var limits = new MemberLimits(payload.Length, payload.Length, payload.Length + 1L, 100, Crc32.HashToUInt32(payload));
        string spool = Path.Combine(_dir.Path, "large-member-spool");
        using (ElevatedOrRoot(assumed: true))
        using (var member = new ProgressiveContent("I655 large member", new object(), () => new MemoryStream(payload, writable: false), limits,
                   2 * limit, spool))
        {
            var buffer = new byte[1024 * 1024];
            for (long at = 0; at < limit; at += buffer.Length) Assert.Equal(buffer.Length, member.Read(at, buffer));
            Assert.Contains("64 MiB", Assert.Throws<InvalidDataException>(() => member.Read(limit, buffer)).Message);
        }
        Assert.False(Directory.Exists(spool));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_nested_ZIP_is_not_spooled_to_be_opened(bool assumed)
    {
        string outer = Path.Combine(_dir.Path, "outer.zip");
        using (var innerBytes = new MemoryStream())
        {
            using (var archive = new ZipArchive(innerBytes, ZipArchiveMode.Create, leaveOpen: true))
            using (var stream = archive.CreateEntry("inside.txt").Open()) stream.WriteByte(42);
            using var zip = ZipFile.Open(outer, ZipArchiveMode.Create);
            using var entry = zip.CreateEntry("inner.zip").Open();
            entry.Write(innerBytes.ToArray());
        }
        string spools = Path.Combine(_dir.Path, "nested-spools");
        var provider = new ZipProvider(spools);
        var outerLocation = provider.GetContainerLocation(outer)!;
        var rows = new Rows();
        await provider.EnumerateAsync(outerLocation, rows, Ct);
        var inner = provider.GetChildLocation(outerLocation, Assert.Single(rows.Entries))!;
        using (ElevatedOrRoot(assumed))
        {
            var error = await Assert.ThrowsAnyAsync<IOException>(() => provider.EnumerateAsync(inner, new Rows(), Ct));
            Assert.Equal($"Nested archives cannot be opened while FileCat runs {RunningAs} because they require a user-local spool.", error.Message);
        }
        Assert.False(Directory.Exists(spools));
        using (Ordinary())
        {
            var listed = new Rows();
            await provider.EnumerateAsync(inner, listed, Ct);
            Assert.Equal("inside.txt", Assert.Single(listed.Entries).Name);
        }
        provider.Release(outer);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Other_archives_spool_no_nested_archive_and_show_no_member_of_unknown_size(bool assumed)
    {
        string outer = Path.Combine(_dir.Path, "outer-with-tar.zip");
        using (var tar = new MemoryStream())
        {
            using (var writer = new TarWriter(tar, TarEntryFormat.Ustar, leaveOpen: true))
            using (var data = new MemoryStream("inside"u8.ToArray()))
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "inside.txt") { DataStream = data, ModificationTime = DateTimeOffset.UnixEpoch });
            using var zip = ZipFile.Open(outer, ZipArchiveMode.Create);
            using var entry = zip.CreateEntry("nested.tar", CompressionLevel.NoCompression).Open();
            entry.Write(tar.ToArray());
        }
        string gz = Path.Combine(_dir.Path, "notes.txt.gz");
        using (var file = File.Create(gz))
        using (var compressed = new GZipStream(file, CompressionLevel.Fastest))
            compressed.Write("compressed notes"u8);
        string temp = Path.Combine(_dir.Path, "archive-spools");
        var registry = new ProviderRegistry();
        registry.Register(new LocalFileSystemProvider());
        var zips = new ZipProvider(temp);
        var archives = new ArchiveProvider(temp, registry);
        registry.Register(zips);
        registry.Register(archives);
        zips.OtherArchives = archives;
        zips.SpoolForeignMember = archives.Spool;
        var nested = ZipProvider.ForFile(outer).WithPath("nested.tar");
        var compressedFile = ArchiveProvider.ForFile(gz);
        var rows = new Rows();
        await archives.EnumerateAsync(compressedFile, rows, Ct);
        var notes = new ItemRef(compressedFile, Assert.Single(rows.Entries).Name, EntryKind.File);
        using (ElevatedOrRoot(assumed))
        {
            Assert.Equal($"Nested archives cannot be opened while FileCat runs {RunningAs} because they require a user-local spool.",
                Assert.Throws<IOException>(() => archives.OpenSpool(nested)).Message);
            Assert.Equal($"This archive member has no declared size and cannot be viewed safely while FileCat runs {RunningAs}.",
                Assert.ThrowsAny<IOException>(() => archives.OpenContent(notes)).Message);
        }
        Assert.Empty(Directory.Exists(temp) ? Directory.GetFileSystemEntries(temp) : []);
        using (Ordinary())
        {
            using (var lease = archives.OpenSpool(nested)) Assert.True(File.Exists(lease.Path));
            using var content = archives.OpenContent(notes)!;
            var bytes = new byte[64];
            Assert.Equal("compressed notes", System.Text.Encoding.ASCII.GetString(bytes, 0, content.Read(0, bytes)));
        }
        archives.Release(gz);
        zips.Release(outer);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Root_starts_no_program_that_the_profile_chooses(bool assumed)
    {
        var shell = new PortableShellServices();
        string file = _dir.File("plain.txt", "plain");
        void Refused(string what, Action action) =>
            Assert.Equal($"{what} are unavailable while FileCat runs {RunningAs}. {Advice}.", Assert.Throws<IOException>(action).Message);
        using (ElevatedOrRoot(assumed))
        {
            Assert.True(shell.IsElevated);
            Refused("System file associations", () => shell.Open(file));
            // macOS reveals an item in Finder, which no association chooses.
            if (!OperatingSystem.IsMacOS()) Refused("System file associations", () => shell.Reveal(file));
            Refused("Terminals", () => shell.OpenTerminal(_dir.Path, "/bin/sh"));
            Refused("Terminals", () => shell.RunInTerminal(_dir.Path, "true", "/bin/sh"));
        }
        using (Ordinary()) Assert.False(shell.IsElevated);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Root_keeps_what_it_writes_in_a_private_session_folder_outside_every_profile(bool assumed)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(),
            "An administrator window keeps its profile's folders, where Windows gives the account rights to what it makes.");
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        using (ElevatedOrRoot(assumed))
        {
            Assert.True(AppPaths.UsesRootSession);
            var usual = AppPaths.Resolve();
            string session = Assert.IsType<string>(usual.DataRoot);
            Assert.StartsWith(OperatingSystem.IsMacOS() ? "/private/tmp/filecat-root-" : "/tmp/filecat-root-", session, StringComparison.Ordinal);
            var made = Assert.IsType<UnixStat>(UnixFiles.Stat(session));
            Assert.True(made.IsDirectory);
            Assert.False(made.IsLink);
            Assert.Equal(0u, made.Mode & 0x3F);
            Assert.Equal(UnixPermissions.CurrentUserId, made.Owner);
            var overridden = AppPaths.Resolve(overrideRoot: _dir.Dir("override"));
            string[] written = [.. usual.WriteFolders.Select(f => f.Folder), overridden.ListingScratchDirectory, overridden.HexRecoveryDirectory];
            foreach (string folder in written)
            {
                Assert.True(folder == session || folder.StartsWith(session + "/", StringComparison.Ordinal), folder);
                Assert.False(folder.StartsWith(home + "/", StringComparison.Ordinal), folder);
            }
            // One folder for the process, whichever profile and however often FileCat asks.
            Assert.Equal(session, AppPaths.Resolve(profile: "other").DataRoot);
        }
        using (Ordinary()) Assert.False(AppPaths.UsesRootSession);
    }

    private sealed class Rows : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var entry in entries) Entries.Add(entry);
        }
        public void ReportIssue(string message) { }
    }

    /// <summary>Owned retained storage evidence with nothing in it: what the refusal comes before.</summary>
    private sealed class OwnedRetainedEvidence : StorageRetainedReviewEvidence
    {
        internal override string DirectoryIdentity => "I655 owned directory";
        internal override bool IsOwned => true;
        internal override IReadOnlyList<StorageReviewArtifactInfo> Artifacts(Guid association, int maximum, CancellationToken ct) => [];
        internal override StorageReviewArtifact OpenArtifact(StorageReviewArtifactInfo expected, CancellationToken ct) =>
            throw new FileNotFoundException("No artifact is kept here.");
        internal override void DeleteArtifact(StorageReviewArtifact artifact, StorageEvidenceVersion expected, CancellationToken ct) =>
            throw new InvalidOperationException("Nothing is deleted here.");
        internal override bool JournalExists(string historicalLeaf, Guid association, CancellationToken ct) => false;
        internal override void DeleteUnreferenced(StorageReviewArtifact artifact, StorageEvidenceVersion expected, string historicalLeaf,
            Guid association, CancellationToken ct) => throw new InvalidOperationException("Nothing is deleted here.");
        public override void Dispose() { }
    }
}

[CollectionDefinition(nameof(RootDiagnosticsCollection), DisableParallelization = true)]
public sealed class RootDiagnosticsCollection;

/// <summary>I655: the log and crash reports; the log file is process-wide, so these cases run on their own.</summary>
[Collection(nameof(RootDiagnosticsCollection))]
public sealed class RootDiagnosticsTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose()
    {
        // The log is the process's: leave it pointing nowhere rather than into a removed folder.
        using (ElevatedOrRoot(assumed: true)) AppLog.Initialize(_dir.Path);
        _dir.Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Neither_the_log_nor_a_crash_report_is_kept(bool assumed)
    {
        string logs = _dir.Dir("diagnostics");
        using (ElevatedOrRoot(assumed))
        {
            AppLog.Initialize(logs);
            Assert.Equal(string.Empty, AppLog.CurrentLogFile);
            AppLog.Info("I655 root log line");
            AppLog.AppendCrash(logs, "I655 root crash report");
        }
        Assert.Empty(Directory.GetFileSystemEntries(logs));
        using (Ordinary())
        {
            AppLog.Initialize(logs);
            AppLog.Info("I655 ordinary log line");
            AppLog.AppendCrash(logs, "I655 ordinary crash report");
        }
        Assert.Contains("I655 ordinary log line", File.ReadAllText(Path.Combine(logs, "filecat.log")));
        Assert.Contains("I655 ordinary crash report", File.ReadAllText(Path.Combine(logs, "crash.log")));
    }
}
