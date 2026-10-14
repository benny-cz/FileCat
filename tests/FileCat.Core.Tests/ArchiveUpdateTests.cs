using System.IO.Compression;
using System.Text;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ArchiveUpdateTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly MarkingFileOperations _operations = new();
    private readonly JobManager _jobs;

    private sealed class MarkingFileOperations : PortableFileOperations
    {
        public readonly Dictionary<string, string> Marks = new(StringComparer.Ordinal);
        public bool RefuseTemporaryMark;
        public bool LoseReplacementMarkOnce;
        public int TemporaryMarkAttempts;
        private int _archiveReads;
        private string? _lastArchivePath;

        public override string? ReadOriginMark(string path)
        {
            if (!File.Exists(path)) return null;
            // File.Replace moves the original file, including its metadata, to the backup. This
            // synthetic adapter stores marks by path, so model that native move on backup reads.
            if (Path.GetExtension(path).Equals(".bak", StringComparison.Ordinal) && _lastArchivePath is { } original)
                return Marks.GetValueOrDefault(original);
            if (Path.GetExtension(path).Equals(".zip", StringComparison.Ordinal)) _lastArchivePath = path;
            if (LoseReplacementMarkOnce && Path.GetFileName(path) == "marked.zip" && ++_archiveReads == 2)
                return null;
            return Marks.GetValueOrDefault(path);
        }

        public override bool WriteOriginMark(string path, string mark)
        {
            if (Path.GetFileName(path).StartsWith(".filecat-zip-", StringComparison.Ordinal))
            {
                TemporaryMarkAttempts++;
                if (RefuseTemporaryMark) return false;
            }
            Marks[path] = mark;
            return true;
        }
    }

    private sealed class MarkAfterFirstReadUnixFileOperations(string archive) : UnixFileOperations
    {
        public bool Injected { get; private set; }
        public string? InjectedMark { get; private set; }

        public override string? ReadOriginMark(string path)
        {
            var mark = base.ReadOriginMark(path);
            if (!Injected && string.Equals(path, archive, StringComparison.Ordinal))
            {
                if (mark is not null) throw new IOException("The owned archive was marked before the controlled update.");
                const string newlyDownloaded = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://owned.invalid/during-update.zip\r\n";
                if (!base.WriteOriginMark(path, newlyDownloaded)) throw new IOException("The controlled origin mark could not be set.");
                InjectedMark = base.ReadOriginMark(path) ?? throw new IOException("The controlled origin mark could not be read back.");
                Injected = true;
            }
            return mark;
        }
    }

    private sealed class SwapAfterFirstMarkReadUnixFileOperations(string archive, string replacement) : UnixFileOperations
    {
        public bool Swapped { get; private set; }

        public override string? ReadOriginMark(string path)
        {
            var mark = base.ReadOriginMark(path);
            if (!Swapped && string.Equals(path, archive, StringComparison.Ordinal))
            {
                // The final baseline check has already run, but the opened source is still held.
                // Unix rename replaces the pathname while that old descriptor remains readable.
                File.Move(replacement, archive, overwrite: true);
                Swapped = true;
            }
            return mark;
        }
    }

    private sealed class SwapAgainDuringBackupMarkReadUnixFileOperations(
        string archive, string firstReplacement, string thirdPartyReplacement) : UnixFileOperations
    {
        public bool SwappedSource { get; private set; }
        public bool SwappedPublication { get; private set; }

        public override string? ReadOriginMark(string path)
        {
            var mark = base.ReadOriginMark(path);
            if (!SwappedSource && string.Equals(path, archive, StringComparison.Ordinal))
            {
                File.Move(firstReplacement, archive, overwrite: true);
                SwappedSource = true;
            }
            else if (SwappedSource && !SwappedPublication &&
                     Path.GetExtension(path).Equals(".bak", StringComparison.Ordinal))
            {
                File.Move(thirdPartyReplacement, archive, overwrite: true);
                SwappedPublication = true;
            }
            return mark;
        }
    }

    private sealed class SwapSameBytesDuringBackupMarkReadUnixFileOperations(
        string archive, string firstReplacement) : UnixFileOperations
    {
        public const string ThirdPartyMark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://owned.invalid/same-bytes.zip\r\n";
        public bool SwappedSource { get; private set; }
        public bool SwappedPublication { get; private set; }
        public byte[]? PublishedBytes { get; private set; }
        public string? WrittenMark { get; private set; }

        public override string? ReadOriginMark(string path)
        {
            var mark = base.ReadOriginMark(path);
            if (!SwappedSource && string.Equals(path, archive, StringComparison.Ordinal))
            {
                File.Move(firstReplacement, archive, overwrite: true);
                SwappedSource = true;
            }
            else if (SwappedSource && !SwappedPublication &&
                     Path.GetExtension(path).Equals(".bak", StringComparison.Ordinal))
            {
                PublishedBytes = File.ReadAllBytes(archive);
                string third = Path.Combine(Path.GetDirectoryName(archive)!, "same-bytes-third.zip");
                File.Copy(archive, third);
                if (!base.WriteOriginMark(third, ThirdPartyMark))
                    throw new IOException("The controlled third-party origin mark could not be written.");
                WrittenMark = base.ReadOriginMark(third) ??
                    throw new IOException("The controlled third-party origin mark could not be read back.");
                File.Move(third, archive, overwrite: true);
                SwappedPublication = true;
            }
            return mark;
        }
    }

    private sealed class MarkPublishedArchiveDuringBackupReadUnixFileOperations(
        string archive, string firstReplacement) : UnixFileOperations
    {
        public bool SwappedSource { get; private set; }
        public bool MarkedPublication { get; private set; }
        public byte[]? PublishedBytes { get; private set; }
        public string? InjectedMark { get; private set; }

        public override string? ReadOriginMark(string path)
        {
            var mark = base.ReadOriginMark(path);
            if (!SwappedSource && string.Equals(path, archive, StringComparison.Ordinal))
            {
                File.Move(firstReplacement, archive, overwrite: true);
                SwappedSource = true;
            }
            else if (SwappedSource && !MarkedPublication &&
                     Path.GetExtension(path).Equals(".bak", StringComparison.Ordinal))
            {
                PublishedBytes = File.ReadAllBytes(archive);
                const string addedMark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://owned.invalid/in-place.zip\r\n";
                if (!base.WriteOriginMark(archive, addedMark))
                    throw new IOException("The controlled in-place mark could not be written.");
                InjectedMark = base.ReadOriginMark(archive) ??
                    throw new IOException("The controlled in-place mark could not be read back.");
                MarkedPublication = true;
            }
            return mark;
        }
    }

    private sealed class ChangePublishedMarkDuringBackupReadUnixFileOperations(
        string archive, string firstReplacement) : UnixFileOperations
    {
        public const string NewMark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://owned.invalid/new-mark.zip\r\nQuarantine=0081;12345678;FileCat;new-mark\r\n";
        public bool SwappedSource { get; private set; }
        public bool ChangedPublication { get; private set; }
        public byte[]? PublishedBytes { get; private set; }
        public string? InjectedMark { get; private set; }

        public override string? ReadOriginMark(string path)
        {
            var mark = base.ReadOriginMark(path);
            if (!SwappedSource && string.Equals(path, archive, StringComparison.Ordinal))
            {
                if (mark is null) throw new IOException("The controlled source had no original mark.");
                File.Move(firstReplacement, archive, overwrite: true);
                SwappedSource = true;
            }
            else if (SwappedSource && !ChangedPublication &&
                     Path.GetExtension(path).Equals(".bak", StringComparison.Ordinal))
            {
                PublishedBytes = File.ReadAllBytes(archive);
                if (!base.WriteOriginMark(archive, NewMark))
                    throw new IOException("The controlled replacement mark could not be written.");
                InjectedMark = base.ReadOriginMark(archive) ??
                    throw new IOException("The controlled replacement mark could not be read back.");
                ChangedPublication = true;
            }
            return mark;
        }
    }

    private sealed class ChangeBackupDuringMarkReadFileOperations(string replacement, bool replacePath)
        : PortableFileOperations
    {
        public string? BackupPath { get; private set; }
        public string? PreservedOriginal { get; private set; }

        public override string? ReadOriginMark(string path)
        {
            var mark = base.ReadOriginMark(path);
            if (BackupPath is null && Path.GetExtension(path).Equals(".bak", StringComparison.Ordinal))
            {
                BackupPath = path;
                if (replacePath)
                {
                    PreservedOriginal = path + ".held-original";
                    File.Move(path, PreservedOriginal);
                    File.Move(replacement, path);
                }
                else File.WriteAllBytes(path, File.ReadAllBytes(replacement));
            }
            return mark;
        }
    }

    private sealed class MarkBackupDuringMarkReadUnixFileOperations : UnixFileOperations
    {
        public string? BackupPath { get; private set; }
        public string? InjectedMark { get; private set; }

        public override string? ReadOriginMark(string path)
        {
            var mark = base.ReadOriginMark(path);
            if (BackupPath is null && Path.GetExtension(path).Equals(".bak", StringComparison.Ordinal))
            {
                BackupPath = path;
                const string addedMark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://owned.invalid/backup-new-mark.zip\r\nQuarantine=0081;12345678;FileCat;backup-new-mark\r\n";
                if (!base.WriteOriginMark(path, addedMark))
                    throw new IOException("The controlled backup mark could not be written.");
                InjectedMark = base.ReadOriginMark(path) ??
                    throw new IOException("The controlled backup mark could not be read back.");
            }
            return mark;
        }
    }

    public ArchiveUpdateTests()
    {
        _providers.Register(new LocalFileSystemProvider());
        _jobs = new JobManager(_operations, _providers, Path.Combine(_dir.Path, "journal"));
    }

    public void Dispose() => _dir.Dispose();

    private async Task<Job> RunAsync(ArchivePlan plan, IReadOnlyList<ItemRef>? sources = null,
        IFileSystemOperations? operations = null)
    {
        var manager = operations is null ? _jobs : new JobManager(operations, _providers, Path.Combine(_dir.Path, "journal-native"));
        var job = manager.Submit(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan, Sources = sources ?? [] });
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!job.State.IsFinished())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Job still {job.State}");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    private static Dictionary<string, string> Read(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        // Duplicate names are legal in ZIP; the last one wins here.
        return archive.Entries.GroupBy(e => e.FullName).Select(g => g.Last()).ToDictionary(e => e.FullName, e =>
        {
            if (e.FullName.EndsWith('/')) return "<dir>";
            using var reader = new StreamReader(e.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        });
    }

    private string MakeZip(string name, params (string Member, string Text)[] members)
    {
        string zip = Path.Combine(_dir.Path, name);
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        archive.Comment = "archive comment";
        foreach (var (member, text) in members)
        {
            var entry = archive.CreateEntry(member);
            if (member.EndsWith('/')) continue;
            entry.Comment = "about " + member;
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
        return zip;
    }

    [Fact]
    public async Task A_marked_archive_is_not_replaced_when_its_rebuilt_copy_cannot_be_marked()
    {
        string zip = MakeZip("marked.zip", ("original.txt", "original"));
        byte[] before = File.ReadAllBytes(zip);
        const string mark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://owned.invalid/marked.zip\r\n";
        _operations.Marks[zip] = mark;
        _operations.RefuseTemporaryMark = true;

        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
            [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "new-folder")]));

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(before, File.ReadAllBytes(zip));
        Assert.Equal(mark, _operations.ReadOriginMark(zip));
        Assert.True(_operations.TemporaryMarkAttempts > 0);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*"));
    }

    [Fact]
    public async Task A_marked_archive_is_restored_if_replacement_loses_its_mark()
    {
        string zip = MakeZip("marked.zip", ("original.txt", "original"));
        byte[] before = File.ReadAllBytes(zip);
        const string mark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://owned.invalid/marked.zip\r\n";
        _operations.Marks[zip] = mark;
        _operations.LoseReplacementMarkOnce = true;

        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
            [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "new-folder")]));

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(before, File.ReadAllBytes(zip));
        Assert.Equal(mark, _operations.ReadOriginMark(zip));
        Assert.True(_operations.TemporaryMarkAttempts > 0);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*"));
    }

    [Fact]
    public async Task A_marked_archive_update_checks_its_rebuilt_mark_before_completion()
    {
        string zip = MakeZip("marked.zip", ("original.txt", "original"));
        const string mark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://owned.invalid/marked.zip\r\n";
        _operations.Marks[zip] = mark;

        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
            [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "new-folder")]));

        Assert.Equal(JobState.Completed, job.State);
        Assert.Contains("new-folder/", Read(zip).Keys);
        Assert.Equal(mark, _operations.ReadOriginMark(zip));
        Assert.True(_operations.TemporaryMarkAttempts > 0);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*"));
    }

    [Fact]
    public async Task A_native_unix_archive_update_keeps_its_download_attribute()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Skip("Extended-attribute marks are for Linux and macOS.");
        string zip = MakeZip("native-marked.zip", ("original.txt", "original"));
        var operations = new UnixFileOperations();
        Assert.True(operations.WriteOriginMark(zip,
            "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://owned.invalid/native-marked.zip\r\n"));
        string mark = Assert.IsType<string>(operations.ReadOriginMark(zip));

        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
            [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "new-folder")]), operations: operations);

        Assert.Equal(JobState.Completed, job.State);
        Assert.Contains("new-folder/", Read(zip).Keys);
        Assert.Equal(mark, operations.ReadOriginMark(zip));
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*"));
    }

    [Fact]
    public async Task A_download_mark_added_during_an_unmarked_update_is_not_discarded()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Skip("The controlled mark is a Unix extended attribute.");
        string zip = MakeZip("during-update.zip", ("original.txt", "original"));
        byte[] before = File.ReadAllBytes(zip);
        var operations = new MarkAfterFirstReadUnixFileOperations(zip);

        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
            [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "new-folder")]), operations: operations);

        Assert.True(operations.Injected);
        Assert.Equal(operations.InjectedMark, operations.ReadOriginMark(zip));
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(before, File.ReadAllBytes(zip));
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*"));
    }

    [Fact]
    public async Task A_source_replaced_after_the_final_baseline_check_is_not_overwritten()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Skip("The controlled replacement uses Unix pathname/descriptor semantics.");
        string zip = MakeZip("source-before.zip", ("original.txt", "original"));
        string replacement = MakeZip("source-after.zip", ("original.txt", "concurrent"));
        byte[] concurrentBytes = File.ReadAllBytes(replacement);
        var operations = new SwapAfterFirstMarkReadUnixFileOperations(zip, replacement);

        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
            [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "new-folder")]), operations: operations);

        Assert.True(operations.Swapped);
        Assert.Equal(concurrentBytes, File.ReadAllBytes(zip));
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("concurrent", Read(zip)["original.txt"]);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*"));
    }

    [Fact]
    public async Task A_same_metadata_replacement_before_open_is_not_modified()
    {
        string zip = MakeZip("planned.zip", ("record.txt", "alpha"));
        var baseline = ArchiveBaseline.Of(zip);
        string replacement = MakeZip("concurrent.zip", ("record.txt", "bravo"));
        byte[] concurrentBytes = File.ReadAllBytes(replacement);
        Assert.Equal(baseline.Length, concurrentBytes.LongLength);
        File.SetLastWriteTimeUtc(replacement, new DateTime(baseline.LastWriteUtcTicks, DateTimeKind.Utc));
        File.Move(replacement, zip, overwrite: true);
        Assert.Equal(baseline.Length, new FileInfo(zip).Length);
        Assert.Equal(baseline.LastWriteUtcTicks, File.GetLastWriteTimeUtc(zip).Ticks);

        var job = await RunAsync(new ArchivePlan(zip, baseline,
            [new ArchiveChange(ArchiveChangeKind.Delete, "record.txt")]));

        Assert.Equal(concurrentBytes, File.ReadAllBytes(zip));
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("bravo", Read(zip)["record.txt"]);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*"));
    }

    [Fact]
    public async Task A_third_party_replacement_during_rollback_is_not_overwritten()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Skip("The controlled replacements use Unix pathname/descriptor semantics.");
        string zip = MakeZip("rollback-source.zip", ("record.txt", "original"));
        string first = MakeZip("first-concurrent.zip", ("record.txt", "first change"));
        string third = MakeZip("third-concurrent.zip", ("record.txt", "third change"));
        byte[] firstBytes = File.ReadAllBytes(first);
        byte[] thirdBytes = File.ReadAllBytes(third);
        var operations = new SwapAgainDuringBackupMarkReadUnixFileOperations(zip, first, third);

        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
            [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "new-folder")]), operations: operations);

        Assert.True(operations.SwappedSource);
        Assert.True(operations.SwappedPublication);
        Assert.Equal(thirdBytes, File.ReadAllBytes(zip));
        Assert.Equal("third change", Read(zip)["record.txt"]);
        string preserved = Assert.Single(Directory.GetFiles(_dir.Path, ".filecat-zip-*.bak"));
        Assert.Equal(firstBytes, File.ReadAllBytes(preserved));
        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Uncertain);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*.tmp"));
    }

    [Fact]
    public async Task A_same_byte_third_party_replacement_during_rollback_keeps_its_origin_mark()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Skip("The controlled same-byte replacement and origin mark use Unix semantics.");
        string zip = MakeZip("same-byte-source.zip", ("record.txt", "original"));
        string first = MakeZip("same-byte-first.zip", ("record.txt", "first change"));
        byte[] firstBytes = File.ReadAllBytes(first);
        var operations = new SwapSameBytesDuringBackupMarkReadUnixFileOperations(zip, first);

        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
            [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "new-folder")]), operations: operations);

        Assert.True(operations.SwappedSource);
        Assert.True(operations.SwappedPublication);
        Assert.Equal(operations.PublishedBytes, File.ReadAllBytes(zip));
        Assert.Equal(operations.WrittenMark, operations.ReadOriginMark(zip));
        string preserved = Assert.Single(Directory.GetFiles(_dir.Path, ".filecat-zip-*.bak"));
        Assert.Equal(firstBytes, File.ReadAllBytes(preserved));
        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Uncertain);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*.tmp"));
    }

    [Fact]
    public async Task A_download_mark_added_to_publication_during_rollback_is_not_discarded()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Skip("The controlled in-place origin mark uses Unix extended attributes.");
        string zip = MakeZip("in-place-mark-source.zip", ("record.txt", "original"));
        string first = MakeZip("in-place-mark-first.zip", ("record.txt", "first change"));
        byte[] firstBytes = File.ReadAllBytes(first);
        var operations = new MarkPublishedArchiveDuringBackupReadUnixFileOperations(zip, first);

        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
            [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "new-folder")]), operations: operations);

        Assert.True(operations.SwappedSource);
        Assert.True(operations.MarkedPublication);
        Assert.Equal(operations.PublishedBytes, File.ReadAllBytes(zip));
        Assert.Equal(operations.InjectedMark, operations.ReadOriginMark(zip));
        string preserved = Assert.Single(Directory.GetFiles(_dir.Path, ".filecat-zip-*.bak"));
        Assert.Equal(firstBytes, File.ReadAllBytes(preserved));
        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Uncertain);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*.tmp"));
    }

    [Fact]
    public async Task A_changed_download_mark_on_publication_during_rollback_is_not_discarded()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Skip("The controlled changed origin mark uses Unix extended attributes.");
        string zip = MakeZip("changed-mark-source.zip", ("record.txt", "original"));
        string first = MakeZip("changed-mark-first.zip", ("record.txt", "first change"));
        byte[] firstBytes = File.ReadAllBytes(first);
        const string oldMark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://owned.invalid/old-mark.zip\r\nQuarantine=0081;12345678;FileCat;old-mark\r\n";
        var native = new UnixFileOperations();
        Assert.True(native.WriteOriginMark(zip, oldMark));
        Assert.True(native.WriteOriginMark(first, oldMark));
        string normalizedOldMark = Assert.IsType<string>(native.ReadOriginMark(zip));
        var operations = new ChangePublishedMarkDuringBackupReadUnixFileOperations(zip, first);

        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
            [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "new-folder")]), operations: operations);

        Assert.True(operations.SwappedSource);
        Assert.True(operations.ChangedPublication);
        Assert.NotEqual(normalizedOldMark, operations.InjectedMark);
        Assert.Equal(operations.InjectedMark, operations.ReadOriginMark(zip));
        Assert.Equal(operations.PublishedBytes, File.ReadAllBytes(zip));
        string preserved = Assert.Single(Directory.GetFiles(_dir.Path, ".filecat-zip-*.bak"));
        Assert.Equal(firstBytes, File.ReadAllBytes(preserved));
        Assert.Equal(normalizedOldMark, native.ReadOriginMark(preserved));
        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Uncertain);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*.tmp"));
    }

    [Fact]
    public async Task A_backup_replaced_before_success_cleanup_is_not_deleted()
    {
        string zip = MakeZip("cleanup-replacement-source.zip", ("record.txt", "original"));
        byte[] originalBytes = File.ReadAllBytes(zip);
        string third = MakeZip("cleanup-replacement-third.zip", ("record.txt", "third-party data"));
        byte[] thirdBytes = File.ReadAllBytes(third);
        var operations = new ChangeBackupDuringMarkReadFileOperations(third, replacePath: true);

        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
            [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "new-folder")]), operations: operations);

        Assert.Contains("new-folder/", Read(zip).Keys);
        string backup = Assert.IsType<string>(operations.BackupPath);
        Assert.Equal(thirdBytes, File.ReadAllBytes(backup));
        Assert.Equal(originalBytes, File.ReadAllBytes(Assert.IsType<string>(operations.PreservedOriginal)));
        Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Committed);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*.tmp"));
    }

    [Fact]
    public async Task A_backup_rewritten_in_place_before_success_cleanup_is_not_deleted()
    {
        string zip = MakeZip("cleanup-rewrite-source.zip", ("record.txt", "original"));
        string third = MakeZip("cleanup-rewrite-third.zip", ("record.txt", "third-party rewrite"));
        byte[] thirdBytes = File.ReadAllBytes(third);
        var operations = new ChangeBackupDuringMarkReadFileOperations(third, replacePath: false);

        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
            [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "new-folder")]), operations: operations);

        Assert.Contains("new-folder/", Read(zip).Keys);
        Assert.Equal(thirdBytes, File.ReadAllBytes(Assert.IsType<string>(operations.BackupPath)));
        Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Committed);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*.tmp"));
    }

    [Fact]
    public async Task A_backup_marked_in_place_before_success_cleanup_is_not_deleted()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Skip("The controlled backup mark uses Unix extended attributes.");
        string zip = MakeZip("cleanup-mark-source.zip", ("record.txt", "original"));
        byte[] originalBytes = File.ReadAllBytes(zip);
        var operations = new MarkBackupDuringMarkReadUnixFileOperations();

        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
            [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "new-folder")]), operations: operations);

        Assert.Contains("new-folder/", Read(zip).Keys);
        string backup = Assert.IsType<string>(operations.BackupPath);
        Assert.Equal(originalBytes, File.ReadAllBytes(backup));
        Assert.Equal(operations.InjectedMark, operations.ReadOriginMark(backup));
        Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Committed);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*.tmp"));
    }

    [Fact]
    public async Task Pack_creates_an_archive_of_files_and_folders_with_empty_folders_kept()
    {
        string src = _dir.Dir("pack");
        _dir.File("pack/a.txt", "alpha");
        _dir.File("pack/sub/b.txt", "beta");
        _dir.Dir("pack/empty");
        string single = _dir.File("single.txt", "one");
        string zip = Path.Combine(_dir.Path, "new.zip");
        var job = await RunAsync(new ArchivePlan(zip, null,
        [
            new ArchiveChange(ArchiveChangeKind.AddFolder, "pack", src),
            new ArchiveChange(ArchiveChangeKind.AddFile, "single.txt", single),
        ]));
        Assert.Equal(JobState.Completed, job.State);
        var content = Read(zip);
        Assert.Equal("alpha", content["pack/a.txt"]);
        Assert.Equal("beta", content["pack/sub/b.txt"]);
        Assert.Equal("<dir>", content["pack/empty/"]);
        Assert.Equal("one", content["single.txt"]);
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*"));

        var again = await RunAsync(new ArchivePlan(zip, null, [new ArchiveChange(ArchiveChangeKind.AddFile, "x.txt", single)]));
        Assert.Equal(JobState.Failed, again.State); // creating never overwrites an existing file
    }

    [Fact]
    public async Task Update_adds_replaces_deletes_renames_and_keeps_everything_else_exactly()
    {
        string zip = MakeZip("update.zip", ("keep.txt", "kept"), ("dup.txt", "first"), ("dup.txt", "second"),
            ("old/one.txt", "1"), ("old/two.txt", "2"), ("gone.txt", "bye"), ("folder/inner.txt", "in"), ("existing.txt", "old"));
        string added = _dir.File("added.txt", "new file");
        string replacement = _dir.File("replacement.txt", "replaced");
        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip),
        [
            new ArchiveChange(ArchiveChangeKind.AddFile, "folder/added.txt", added),
            new ArchiveChange(ArchiveChangeKind.AddFile, "existing.txt", replacement),
            new ArchiveChange(ArchiveChangeKind.Delete, "gone.txt"),
            new ArchiveChange(ArchiveChangeKind.Rename, "old", NewMemberPath: "renamed"),
            new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "made"),
        ]));
        Assert.Equal(JobState.CompletedWithIssues, job.State); // existing.txt was kept: replacing was not chosen
        using (var archive = ZipFile.OpenRead(zip))
        {
            Assert.Equal("archive comment", archive.Comment);
            Assert.Equal(["keep.txt", "dup.txt", "dup.txt", "renamed/one.txt", "renamed/two.txt", "folder/inner.txt", "existing.txt", "folder/added.txt", "made/"],
                archive.Entries.Select(e => e.FullName));
            Assert.Equal("about keep.txt", archive.Entries[0].Comment);
            string Text(int i) { using var r = new StreamReader(archive.Entries[i].Open()); return r.ReadToEnd(); }
            Assert.Equal("first", Text(1));
            Assert.Equal("second", Text(2));
            Assert.Equal("old", Text(6));
            Assert.Equal("new file", Text(7));
        }

        var replace = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip), [new ArchiveChange(ArchiveChangeKind.AddFile, "existing.txt", replacement)],
            ReplaceExisting: true));
        Assert.Equal(JobState.Completed, replace.State);
        Assert.Equal("replaced", Read(zip)["existing.txt"]);
        var editCommit = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip), [new ArchiveChange(ArchiveChangeKind.Replace, "keep.txt", added)]));
        Assert.Equal(JobState.Completed, editCommit.State);
        Assert.Equal("new file", Read(zip)["keep.txt"]);
    }

    [Fact]
    public async Task A_changed_archive_or_a_name_collision_is_refused_and_the_original_is_untouched()
    {
        string zip = MakeZip("guard.zip", ("a.txt", "a"), ("b.txt", "b"));
        var baseline = ArchiveBaseline.Of(zip);
        File.SetLastWriteTimeUtc(zip, DateTime.UtcNow.AddMinutes(5)); // another program touched it
        var bytes = File.ReadAllBytes(zip);
        var stale = await RunAsync(new ArchivePlan(zip, baseline, [new ArchiveChange(ArchiveChangeKind.Delete, "a.txt")]));
        Assert.Equal(JobState.Failed, stale.State);
        Assert.Contains(stale.Issues, i => i.Message.Contains("changed after it was opened", StringComparison.Ordinal));
        Assert.Equal(bytes, File.ReadAllBytes(zip));

        var collision = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip), [new ArchiveChange(ArchiveChangeKind.Rename, "a.txt", NewMemberPath: "b.txt")]));
        Assert.Equal(JobState.Failed, collision.State);
        Assert.Equal(bytes, File.ReadAllBytes(zip));
        Assert.Empty(Directory.GetFiles(_dir.Path, ".filecat-zip-*"));

        var traversal = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip), [new ArchiveChange(ArchiveChangeKind.Rename, "a.txt", NewMemberPath: "../evil.txt")]));
        Assert.Equal(JobState.Failed, traversal.State);
        Assert.Equal(bytes, File.ReadAllBytes(zip));
    }

    [Fact]
    public async Task Damaged_or_encrypted_members_stop_an_update_and_the_test_command_names_them()
    {
        string zip = MakeZip("damaged.zip", ("good.txt", "good"), ("bad.txt", new string('x', 4000)));
        // Flip bytes inside bad.txt's compressed data (after its local header and name).
        var bytes = File.ReadAllBytes(zip);
        int local = IndexOf(bytes, "bad.txt"u8.ToArray());
        bytes[local + 7 + 5] ^= 0x55;
        bytes[local + 7 + 6] ^= 0x55;
        File.WriteAllBytes(zip, bytes);
        var update = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip), [new ArchiveChange(ArchiveChangeKind.Delete, "good.txt")]));
        Assert.Equal(JobState.Failed, update.State);
        Assert.Equal(bytes, File.ReadAllBytes(zip));

        var test = _jobs.Submit(new JobRequest { Kind = JobKind.ArchiveTest, Sources = [ItemRef.ForFileSystemPath(zip, EntryKind.File)] });
        while (!test.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Contains(test.Issues, i => i.Path.Contains("bad.txt", StringComparison.Ordinal) && i.Severity == IssueSeverity.Error);
        Assert.Contains(test.Issues, i => i.Message.StartsWith("1 members intact", StringComparison.Ordinal));
        Assert.Equal("1 members intact, 1 damaged", test.Summary);
        string intact = MakeZip("intact.zip", ("a.txt", "a"), ("b.txt", "b"));
        var clean = _jobs.Submit(new JobRequest { Kind = JobKind.ArchiveTest, Sources = [ItemRef.ForFileSystemPath(intact, EntryKind.File)] });
        while (!clean.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(JobState.Completed, clean.State);
        Assert.Equal("all 2 members intact", clean.Summary);

        string locked = MakeZip("encrypted.zip", ("secret.txt", "s"));
        var raw = File.ReadAllBytes(locked);
        int central = IndexOf(raw, [0x50, 0x4B, 0x01, 0x02]);
        raw[central + 8] |= 1; // general-purpose flag bit 0: encrypted
        File.WriteAllBytes(locked, raw);
        var refused = await RunAsync(new ArchivePlan(locked, ArchiveBaseline.Of(locked), [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, "x")]));
        Assert.Equal(JobState.Failed, refused.State);
        Assert.Contains(refused.Issues, i => i.Message.Contains("encrypted", StringComparison.Ordinal));
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        throw new InvalidOperationException("Pattern not found.");
    }

    [Fact]
    public async Task Large_archives_plan_in_linear_time()
    {
        string zip = Path.Combine(_dir.Path, "large.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            for (int i = 0; i < 20_000; i++)
            {
                using var writer = new StreamWriter(archive.CreateEntry($"d{i % 100}/f{i}.txt", CompressionLevel.NoCompression).Open());
                writer.Write(i);
            }
        var changes = Enumerable.Range(50, 50).Select(d => new ArchiveChange(ArchiveChangeKind.Delete, $"d{d}"))
            .Append(new ArchiveChange(ArchiveChangeKind.Rename, "d1", NewMemberPath: "renamed")).ToList();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var job = await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip), changes, CompressionLevel.Fastest));
        Assert.Equal(JobState.Completed, job.State);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"took {clock.Elapsed}");
        using var result = ZipFile.OpenRead(zip);
        Assert.Equal(10_000, result.Entries.Count);
        Assert.Equal(200, result.Entries.Count(e => e.FullName.StartsWith("renamed/", StringComparison.Ordinal)));
        Assert.DoesNotContain(result.Entries, e => e.FullName.StartsWith("d1/", StringComparison.Ordinal) || e.FullName.StartsWith("d77/", StringComparison.Ordinal));
    }

    [Fact]
    public void Member_paths_are_checked()
    {
        Assert.Null(ArchivePaths.Problem("a/b.txt"));
        Assert.NotNull(ArchivePaths.Problem("/abs.txt"));
        Assert.NotNull(ArchivePaths.Problem("a/../b"));
        Assert.NotNull(ArchivePaths.Problem(@"a\b"));
        Assert.NotNull(ArchivePaths.Problem("C:/x"));
        Assert.NotNull(ArchivePaths.Problem("a//b"));
        Assert.NotNull(ArchivePaths.Problem("bad|name"));
    }
}
