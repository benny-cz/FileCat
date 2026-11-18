using System.Formats.Tar;
using System.IO.Compression;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ArchiveEfsSpoolTests
{
    private const string FixtureRequirement = "Requires a disposable Windows NTFS fixture with EFS enabled.";

    [Fact]
    public void The_opened_contributor_limit_counts_distinct_held_identities_and_refuses_an_overflow()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires actual Windows file identities.");
        using var directory = new TempDir();
        var protection = new OutputEfsObservation();
        for (int i = 0; i < OutputEfsObservation.MaxContributors; i++)
        {
            string path = directory.File($"input-{i:D4}.bin");
            using (var handle = File.OpenHandle(path)) protection.ObserveOpened(handle);
            if (i == 0)
            {
                using var reopened = File.OpenHandle(path);
                protection.ObserveOpened(reopened);
            }
        }
        Assert.Equal(OutputEfsObservation.State.Clear, protection.Status);
        ArchiveOuterEfs.RequireClearOutput(protection, ContentOutputKind.DiskCache);
        var receipt = protection.Freeze();
        Assert.Equal(OutputEfsObservation.MaxContributors, receipt.Contributors.Length);
        Assert.False(receipt.HasUnknownInput);
        var derived = new OutputEfsObservation();
        derived.Include(receipt);
        using (var overflow = File.OpenHandle(directory.File("overflow.bin"))) derived.ObserveOpened(overflow);
        Assert.Equal(OutputEfsObservation.State.Unknown, derived.Status);
        Assert.True(derived.Freeze().HasUnknownInput);
        Assert.Equal(OutputEfsObservation.State.Clear, receipt.Protection);
        Assert.Equal(ArchiveOuterEfs.SpoolUnknown, Assert.Throws<ArchiveWriteException>(() =>
            ArchiveOuterEfs.RequireClearOutput(derived, ContentOutputKind.DiskCache)).Message);
    }

    [Fact]
    public void Over_budget_origin_metadata_cannot_authorize_persistence()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires an actual Windows file identity.");
        using var directory = new TempDir();
        using var handle = File.OpenHandle(directory.File("input.bin"));
        var protection = new OutputEfsObservation();
        protection.ObserveOpened(handle, new string('A', OutputEfsObservation.MaxContributorBytes / sizeof(char)));
        Assert.Equal(OutputEfsObservation.State.Unknown, protection.Status);
        protection.ObserveOpened(handle);
        Assert.Equal(OutputEfsObservation.State.Unknown, protection.Status);
    }

    [Fact]
    public void An_encrypted_observation_preserves_unknown_input_across_a_retained_receipt()
    {
        if (!IsEfsFixture) Assert.Skip(FixtureRequirement);
        using var directory = new TempDir();
        string path = directory.File("protected.bin");
        File.Encrypt(path);
        var observation = new OutputEfsObservation();
        using (var handle = File.OpenHandle(path))
        {
            observation.ObserveOpened(handle, new string('A', OutputEfsObservation.MaxContributorBytes / sizeof(char)));
            observation.ObserveOpened(handle);
        }
        var receipt = observation.Freeze();
        Assert.Equal(OutputEfsObservation.State.Encrypted, receipt.Protection);
        Assert.Single(receipt.Contributors);
        Assert.True(receipt.HasUnknownInput);
        var derived = new OutputEfsObservation();
        derived.Include(receipt);
        var retained = derived.Freeze();
        Assert.Equal(receipt.Contributors.ToArray(), retained.Contributors.ToArray());
        Assert.Equal(OutputEfsObservation.State.Encrypted, retained.Protection);
        Assert.True(retained.HasUnknownInput);
    }

    [Fact]
    public void Protected_tar_members_can_be_read_forward_without_creating_a_disk_cache()
    {
        if (!IsEfsFixture) Assert.Skip(FixtureRequirement);

        using var directory = new TempDir();
        string archivePath = Path.Join(directory.Path, "protected.tar.gz");
        string cache = Path.Join(directory.Path, "cache");
        var archives = new ArchiveProvider(cache, new ProviderRegistry());
        byte[] payload = new byte[33 * 1024 * 1024];
        payload.AsSpan().Fill(42);
        try
        {
            using (var file = File.Create(archivePath))
            using (var compressed = new GZipStream(file, CompressionLevel.Fastest))
            using (var writer = new TarWriter(compressed))
            using (var content = new MemoryStream(payload))
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "payload.bin") { DataStream = content });

            File.Encrypt(archivePath);
            var item = new ItemRef(ArchiveProvider.ForFile(archivePath), "payload.bin", EntryKind.File);
            using (var viewer = Assert.IsType<ProgressiveContent>(archives.OpenContent(item)))
            {
                var refusal = Assert.Throws<ArchiveWriteException>(() => viewer.Read(0, new byte[256]));
                Assert.Equal(ArchiveOuterEfs.SpoolUnavailable, refusal.Message);
            }

            using (var forward = ProgressiveContent.Sequential(Assert.IsType<ProgressiveContent>(archives.OpenContent(item)))!)
            {
                byte[] actual = new byte[256];
                Assert.Equal(actual.Length, forward.Read(0, actual));
                Assert.Equal(actual.Length, forward.Read(actual.Length, actual));
                Assert.All(actual, value => Assert.Equal(42, value));
            }
            Assert.False(Directory.Exists(cache));
        }
        finally { archives.Release(archivePath); }
    }

    [Fact]
    public void An_encrypted_later_rar_volume_refuses_a_nested_disk_spool()
    {
        if (!IsEfsFixture) Assert.Skip(FixtureRequirement);
        using var directory = new TempDir();
        for (int part = 1; part <= 6; part++)
        {
            string name = $"Rar5.multi.part{part:00}.rar";
            File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", name), Path.Join(directory.Path, name));
        }
        string first = Path.Join(directory.Path, "Rar5.multi.part01.rar"), later = Path.Join(directory.Path, "Rar5.multi.part03.rar");
        File.Encrypt(later);
        Assert.False(File.GetAttributes(first).HasFlag(FileAttributes.Encrypted));
        Assert.True(File.GetAttributes(later).HasFlag(FileAttributes.Encrypted));
        string cache = Path.Join(directory.Path, "cache");
        var providers = new ProviderRegistry();
        var archives = new ArchiveProvider(cache, providers);
        providers.Register(archives);
        try
        {
            var archive = ArchiveProvider.ForFile(first);
            var member = new ItemRef(archive.WithPath("exe"), "test.exe", EntryKind.File);
            using var content = Assert.IsType<MemoryContentSource>(archives.OpenContent(member));
            Assert.Equal(45056, content.Length);
            var refusal = Assert.Throws<ArchiveWriteException>(() => archives.OpenSpool(archive.WithPath("exe/test.exe")));
            Assert.Equal(ArchiveOuterEfs.SpoolUnavailable, refusal.Message);
            Assert.False(Directory.Exists(cache));
        }
        finally { archives.Release(first); }
    }

    [Fact]
    public void A_zip_member_cannot_bypass_the_efs_disk_check_through_the_other_provider()
    {
        if (!IsEfsFixture) Assert.Skip(FixtureRequirement);
        using var directory = new TempDir();
        string archive = Path.Join(directory.Path, "protected.zip"), cache = Path.Join(directory.Path, "cache");
        var providers = new ProviderRegistry();
        var zip = new ZipProvider(cache);
        var archives = new ArchiveProvider(cache, providers);
        providers.Register(zip);
        try
        {
            using (var file = ZipFile.Open(archive, ZipArchiveMode.Create))
            using (var member = file.CreateEntry("payload.bin").Open()) member.Write([7, 8, 9]);
            File.Encrypt(archive);
            var location = ZipProvider.ForFile(archive).WithPath("payload.bin");
            Assert.Equal(ArchiveOuterEfs.SpoolUnavailable, Assert.Throws<ArchiveWriteException>(() => archives.OpenSpool(location)).Message);
            Assert.False(Directory.Exists(cache));
        }
        finally { zip.Release(archive); }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Protected_memory_and_forward_only_members_cannot_publish_an_extraction_or_drag_stage(bool legacyZip, bool large)
    {
        if (!IsEfsFixture) Assert.Skip(FixtureRequirement);
        using var directory = new TempDir();
        string path = Path.Join(directory.Path, legacyZip ? "source.zip" : "source.tar.gz");
        string cache = Path.Join(directory.Path, "cache"), drag = Path.Join(directory.Path, "drag-temp");
        string destination = directory.Dir("destination");
        byte[] payload = new byte[large ? 33 * 1024 * 1024 : 4];
        payload[0] = 50;
        if (legacyZip)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            using var member = archive.CreateEntry("payload.bin").Open();
            member.Write(payload);
        }
        else
        {
            using var file = File.Create(path);
            using var compressed = new GZipStream(file, CompressionLevel.Fastest);
            using var writer = new TarWriter(compressed);
            using var content = new MemoryStream(payload);
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "payload.bin") { DataStream = content });
        }

        File.Encrypt(path);
        var providers = new ProviderRegistry();
        var files = new IdentityAwareTestFiles();
        var zip = new ZipProvider(cache) { GetSourceIdentity = files.GetFileIdentity, GetOpenedSourceIdentity = files.GetHandleIdentity };
        var archives = new ArchiveProvider(cache, providers) { GetSourceIdentity = files.GetFileIdentity, GetOpenedSourceIdentity = files.GetHandleIdentity };
        providers.Register(zip);
        providers.Register(archives);
        providers.Register(new LocalFileSystemProvider());
        var location = legacyZip ? ZipProvider.ForFile(path) : ArchiveProvider.ForFile(path);
        var item = new ItemRef(location, "payload.bin", EntryKind.File, payload.Length);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            using (var read = ProgressiveContent.Sequential(providers.For(location).OpenContent(item))!)
            {
                byte[] first = new byte[1];
                Assert.Equal(1, read.Read(0, first));
                Assert.Equal(payload[0], first[0]);
            }

            var staged = DragStaging.Stage([item], providers, files, drag, ct);
            Assert.Null(staged.Paths);
            Assert.Contains(ArchiveOuterEfs.OutputUnavailable(ContentOutputKind.Drag), staged.Refusal);
            if (Directory.Exists(drag)) Assert.Empty(Directory.EnumerateFiles(drag, "*", SearchOption.AllDirectories));

            var manager = new JobManager(files, providers, Path.Join(directory.Path, "journal"));
            var retired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.JobFinished += _ => retired.TrySetResult();
            var job = manager.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [item],
                Destination = Location.FileSystem(destination),
            });
            try { await retired.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); }
            finally { if (!job.State.IsFinished()) job.Cancel(); }
            Assert.Contains(job.Issues, issue => issue.Message.Contains(ArchiveOuterEfs.OutputUnavailable(ContentOutputKind.Extraction), StringComparison.Ordinal));
            Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
            Assert.False(Directory.Exists(cache));
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Encrypted));
        }
        finally
        {
            zip.Release(path);
            archives.Release(path);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatformGuard("windows")]
    private static bool IsEfsFixture => OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1";
}
