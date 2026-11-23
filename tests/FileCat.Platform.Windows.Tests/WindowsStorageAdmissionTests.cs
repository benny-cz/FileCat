using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Volume admission rules for storage changes: the drive's attachment and Cloud Files membership when no filter
/// instance answers on the volume.
/// </summary>
[Collection("Windows storage job evidence")]
public sealed class WindowsStorageAdmissionTests
{
    private static CancellationToken Stop => TestContext.Current.CancellationToken;
    private const int NoFilter = unchecked((int)0x80070001), OutsideSyncRoot = unchecked((int)0x80070186);

    [Theory]
    [InlineData(7, false, "HotPlug")] // USB, even when the disk reports fixed media
    [InlineData(4, false, "HotPlug")] // IEEE 1394
    [InlineData(0xC, false, "HotPlug")] // SD
    [InlineData(0xD, false, "HotPlug")] // MMC
    [InlineData(0xB, true, "HotPlug")] // removable medium on SATA
    [InlineData(1, false, "Fixed")] // SCSI
    [InlineData(0xB, false, "Fixed")] // SATA
    [InlineData(0xF, false, "Fixed")] // file-backed virtual disk (VHD/VHDX)
    [InlineData(0x10, false, "Fixed")] // Storage Spaces
    [InlineData(0x11, false, "Fixed")] // NVMe
    [InlineData(0x14, false, "Fixed")] // NVMe over Fabrics
    [InlineData(0, false, "Unknown")]
    [InlineData(0x15, false, "Unknown")]
    [InlineData(0x7F, false, "Unknown")]
    [InlineData(-1, false, "Unknown")]
    public void Bus_types_classify_hot_plug_fixed_and_unknown_attachments(int bus, bool removable, string expected) =>
        Assert.Equal(expected, WindowsStorageDevice.Classify(bus, removable).ToString());

    [Fact]
    public void Hot_plug_and_unknown_attachments_are_refused_with_distinct_reasons()
    {
        Assert.Null(WindowsStorageTransforms.AttachmentProblem(new(WindowsStorageDevice.Attachment.Fixed, 0x11, false)));
        var hotPlug = WindowsStorageTransforms.AttachmentProblem(new(WindowsStorageDevice.Attachment.HotPlug, 7, false));
        Assert.Equal(("hot-plug-unqualified", StorageReasonKind.FileCatBoundary, false), (hotPlug?.Code, hotPlug!.Kind, hotPlug.IsError));
        Assert.Contains("USB", hotPlug.Message, StringComparison.Ordinal);
        var unknown = WindowsStorageTransforms.AttachmentProblem(new(WindowsStorageDevice.Attachment.Unknown, -1, false));
        Assert.Equal(("device-attachment-unknown", StorageReasonKind.Unknown, true), (unknown?.Code, unknown!.Kind, unknown.IsError));
    }

    [Fact]
    public void Unanswered_cloud_queries_admit_only_held_walks_without_a_registered_root_on_the_volume()
    {
        const string volume = "5e4c6f7a-1b2c-4d3e-8f90-a1b2c3d4e5f6", other = "0d1e2f30-4152-4637-8899-aabbccddeeff";
        int reads = 0;
        IReadOnlySet<string>? Roots(params string[] volumes) { reads++; return new HashSet<string>(volumes, StringComparer.OrdinalIgnoreCase); }

        // The filter's explicit answers decide without consulting registrations.
        Assert.Equal("cloud-sync-root", WindowsStorageTransforms.SyncRootProblem(0, volume, () => Roots(), Stop)?.Code);
        Assert.Null(WindowsStorageTransforms.SyncRootProblem(OutsideSyncRoot, volume, () => Roots(volume), Stop));
        Assert.Equal(0, reads);
        // Other failures, and an unanswered query without a held walk, keep refusing.
        Assert.Equal("cloud-query-80070005", WindowsStorageTransforms.SyncRootProblem(unchecked((int)0x80070005), volume, () => Roots(), Stop)?.Code);
        Assert.Equal("cloud-query-80070001", WindowsStorageTransforms.SyncRootProblem(NoFilter, null, () => Roots(), Stop)?.Code);
        Assert.Equal("cloud-query-80070001", WindowsStorageTransforms.SyncRootProblem(NoFilter, volume, null, Stop)?.Code);
        Assert.Equal(0, reads);

        // No filter answered: the registrations decide.
        Assert.Null(WindowsStorageTransforms.SyncRootProblem(NoFilter, volume, () => Roots(), Stop));
        Assert.Null(WindowsStorageTransforms.SyncRootProblem(NoFilter, volume, () => Roots(other), Stop));
        var registered = WindowsStorageTransforms.SyncRootProblem(NoFilter, volume, () => Roots(other, volume.ToUpperInvariant()), Stop);
        Assert.Equal(("cloud-sync-root-volume", StorageReasonKind.Unknown), (registered?.Code, registered!.Kind));
        var unreadable = WindowsStorageTransforms.SyncRootProblem(NoFilter, volume, () => null, Stop);
        Assert.Equal(("cloud-registrations-unknown", StorageReasonKind.Unknown), (unreadable?.Code, unreadable!.Kind));
        Assert.Equal(3, reads);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => WindowsStorageTransforms.SyncRootProblem(NoFilter, volume, () => Roots(), canceled.Token));
        Assert.Equal(3, reads);
    }

    [Fact]
    public void The_release_composition_is_compression_only_and_needs_the_qualified_normal_process()
    {
        var release = WindowsStorageTransforms.CreateRelease();
        Assert.False(release is IStorageEfsJobAdapter);
        Assert.False(release is IStorageEfsFileBackup);
        if (WindowsStorageReviewEvidence.IsSupportedHost)
        {
            Assert.IsType<WindowsStorageTransforms>(release);
            Assert.Null(release.Unavailable(StorageAction.Compress));
            Assert.Null(release.Unavailable(StorageAction.RemoveCompression));
            Assert.Equal("efs-unqualified", release.Unavailable(StorageAction.EncryptEfs)?.Code);
            Assert.Equal("efs-unqualified", release.Unavailable(StorageAction.DecryptEfs)?.Code);
            return;
        }
        Assert.IsType<NoStorageTransforms>(release);
        var reason = WindowsStorageTransforms.ReleaseUnavailable();
        Assert.Contains(reason.Code, new[] { "elevated-process", "arm64-emulated", "windows-version", "target-unqualified" });
        Assert.Equal(WindowsTokenAuthority.IsHighIntegrity, reason.Code == "elevated-process");
        // Native Arm64 is a release host; only an emulated copy on Windows on Arm gets the Arm64 reason.
        bool emulatedOnArm = RuntimeInformation.OSArchitecture == Architecture.Arm64 && RuntimeInformation.ProcessArchitecture != Architecture.Arm64;
        Assert.Equal(emulatedOnArm && !WindowsTokenAuthority.IsHighIntegrity, reason.Code == "arm64-emulated");
        foreach (var action in Enum.GetValues<StorageAction>()) Assert.Equal(reason, release.Unavailable(action));
        Assert.Throws<NotSupportedException>(() => release.InspectRoot(default!, StorageAction.Compress, Stop));
    }

    [Fact]
    public void The_App_release_composition_adds_EFS_only_on_a_qualified_edition()
    {
        var release = WindowsStorageTransforms.CreateReleaseWithEfs(Path.GetTempPath, static show => show(), static compare => compare());
        if (!WindowsStorageReviewEvidence.IsSupportedHost)
        {
            Assert.IsType<NoStorageTransforms>(release);
            foreach (var action in Enum.GetValues<StorageAction>()) Assert.Equal(WindowsStorageTransforms.ReleaseUnavailable(), release.Unavailable(action));
            return;
        }
        Assert.IsAssignableFrom<IStorageEfsJobAdapter>(release);
        Assert.IsAssignableFrom<IStorageEfsFileBackup>(release);
        Assert.Null(release.Unavailable(StorageAction.Compress));
        Assert.Null(release.Unavailable(StorageAction.RemoveCompression));
        foreach (var action in new[] { StorageAction.EncryptEfs, StorageAction.DecryptEfs })
        {
            var problem = release.Unavailable(action);
            if (WindowsStorageTransforms.IsEfsEdition) Assert.Null(problem);
            else Assert.Equal(("efs-build", WindowsStorageTransforms.EfsEditionReason), (problem?.Code, problem?.Message));
        }
    }

    /// <summary>Runs on a released Windows 11 host whose edition EFS changes are not qualified on, such as Home.</summary>
    [Fact]
    public void Without_a_qualified_EFS_edition_EFS_review_is_refused_before_any_object_is_opened()
    {
        Assert.SkipWhen(!WindowsStorageReviewEvidence.IsSupportedHost || WindowsStorageTransforms.IsEfsEdition,
            "Needs a released Windows 11 host whose edition EFS changes are not qualified on, such as Home.");
        string root = Directory.CreateTempSubdirectory("filecat-efs-edition-").FullName;
        try
        {
            string file = Path.Join(root, "plain.txt");
            File.WriteAllText(file, "plain");
            var release = WindowsStorageTransforms.CreateReleaseWithEfs(() => root, static show => show(), static compare => compare());
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), release, Path.Join(root, "journal"));
            foreach (var action in new[] { StorageAction.EncryptEfs, StorageAction.DecryptEfs })
            {
                var refused = Assert.Throws<NotSupportedException>(() => manager.PrepareStorageReview(
                    [ItemRef.ForFileSystemPath(file, EntryKind.File)], action, Stop));
                Assert.Equal(WindowsStorageTransforms.EfsEditionReason, refused.Message);
            }
            Assert.Empty(manager.Jobs);
            Assert.False(Directory.Exists(Path.Join(root, "journal")));
            Assert.Empty(Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Where(p => p != file));
            // Compression stays available on the same host, and the file stays clear.
            Assert.Null(release.Unavailable(StorageAction.Compress));
            Assert.False(File.GetAttributes(file).HasFlag(FileAttributes.Encrypted));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Each_physical_refusal_has_its_own_reason_kind_and_precedence()
    {
        var held = new StorageObjectIdentity(7, "held") { VolumeBinding = "v" };
        var root = new StorageObjectIdentity(5, "root") { VolumeBinding = "v" };
        var windows = new StorageObjectIdentity(6, "windows") { VolumeBinding = "v" };
        var ordinary = new WindowsStorageTransforms.VolumeFacts("v", 0x10 | 0x20000, 4096);
        WindowsStorageTransforms.NodeFacts File(uint attributes = 0x20, uint links = 1, bool directory = false, bool deleting = false,
            StorageObjectIdentity? identity = null) => new(identity ?? held, attributes, links, 1, directory, deleting, 0);
        (string?, StorageReasonKind?) Check(WindowsStorageTransforms.NodeFacts facts, WindowsStorageTransforms.VolumeFacts? volume = null,
            bool compression = true, StorageObjectIdentity[]? ancestry = null, StorageObjectIdentity? expected = null)
        {
            var problem = WindowsStorageTransforms.PhysicalProblem(expected ?? held, ancestry ?? [root, held], root, volume ?? ordinary, facts, windows, compression);
            return (problem?.Code, problem?.Kind);
        }

        Assert.Equal((null, null), Check(File()));
        Assert.Equal((null, null), Check(File(0x10, 2, directory: true)));
        Assert.Equal(("entry-changed", StorageReasonKind.IdentityChanged), Check(File(identity: root with { ObjectId = "other" })));
        Assert.Equal(("entry-changed", StorageReasonKind.IdentityChanged), Check(File(deleting: true)));
        Assert.Equal(("volume-root", StorageReasonKind.FileCatBoundary), Check(File(identity: root), ancestry: [root], expected: root));
        Assert.Equal(("windows-tree", StorageReasonKind.FileCatBoundary), Check(File(), ancestry: [root, windows, held]));
        Assert.Equal(("system-entry", StorageReasonKind.FileCatBoundary), Check(File(0x24)));
        Assert.Equal(("reparse-entry", StorageReasonKind.FileCatBoundary), Check(File(0x400)));
        foreach (uint managed in new[] { 0x1000u, 0x80000u, 0x100000u, 0x400000u })
            Assert.Equal(("cloud-entry", StorageReasonKind.FileCatBoundary), Check(File(0x20 | managed)));
        Assert.Equal((null, null), Check(File(0x20 | 0x40000))); // queried FILE_BASIC_INFO 0x40000 is the EA indication
        Assert.Equal(("multiple-names", StorageReasonKind.FileCatBoundary), Check(File(links: 2)));
        Assert.Equal(("multiple-names", StorageReasonKind.FileCatBoundary), Check(File(links: 0)));
        Assert.Equal(("sparse-entry", StorageReasonKind.FileCatBoundary), Check(File(0x10 | 0x200, directory: true)));
        Assert.Equal((null, null), Check(File(0x20 | 0x200)));
        Assert.Equal(("read-only-volume", StorageReasonKind.PermissionOrPolicy), Check(File(), ordinary with { Flags = ordinary.Flags | 0x80000 }));
        Assert.Equal(("allocation-unit-unknown", StorageReasonKind.Unknown), Check(File(), ordinary with { AllocationUnit = 0 }));
        Assert.Equal(("allocation-unit", StorageReasonKind.FileCatBoundary), Check(File(), ordinary with { AllocationUnit = 8192 }));
        Assert.Equal((null, null), Check(File(), ordinary with { AllocationUnit = 8192 }, compression: false));
        Assert.Equal(("volume-compression", StorageReasonKind.WindowsLimitation), Check(File(), ordinary with { Flags = 0x20000 }));
        Assert.Equal(("volume-efs", StorageReasonKind.WindowsLimitation), Check(File(), ordinary with { Flags = 0x10 }, compression: false));
        // Precedence: identity before policy, policy before volume capability.
        Assert.Equal("entry-changed", Check(File(0x404, deleting: true), ordinary with { AllocationUnit = 0 }).Item1);
        Assert.Equal("system-entry", Check(File(0x404), ordinary with { Flags = 0x80000 }).Item1);

        Assert.Equal(("compression-unknown", StorageReasonKind.Unknown),
            (WindowsStorageTransforms.StateProblem(StorageObservationState.Unknown, StorageObservationState.Disabled)?.Code,
             WindowsStorageTransforms.StateProblem(StorageObservationState.Unknown, StorageObservationState.Disabled)?.Kind));
        Assert.Equal("inconsistent-storage", WindowsStorageTransforms.StateProblem(StorageObservationState.Enabled, StorageObservationState.Enabled)?.Code);
        Assert.Null(WindowsStorageTransforms.StateProblem(StorageObservationState.Enabled, StorageObservationState.Disabled));
        Assert.Null(WindowsStorageTransforms.StateProblem(StorageObservationState.Disabled, StorageObservationState.Enabled));
    }

    [Fact]
    public void The_system_volume_resolves_to_its_held_binding_and_reports_a_fixed_attachment()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        Assert.SkipWhen(!WindowsStorageTransforms.SupportsHost(arm64), "Qualified Windows 11 client fixture (x64, or native ARM64 when opted in).");
        string system = Environment.SystemDirectory;
        using (var held = File.OpenHandle(system, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            FileOptions.None | (FileOptions)0x02000000 /* FILE_FLAG_BACKUP_SEMANTICS */))
        {
            string binding = WindowsStorageTransforms.ActualVolumeBinding(held, Stop).Identity;
            Assert.Equal(binding, WindowsStorageSyncRoots.VolumeOf(system));
            Assert.Equal(binding, WindowsStorageSyncRoots.VolumeOf(Path.GetPathRoot(system)!));
            var device = WindowsStorageDevice.Describe(binding);
            Assert.Equal(WindowsStorageDevice.Attachment.Fixed, device.Attachment);
            Assert.False(device.RemovableMedia);
        }

        char? unused = null;
        for (char letter = 'Z'; letter >= 'H' && unused is null; letter--)
            if (!Directory.Exists(letter + @":\")) unused = letter;
        if (unused is { } missing) Assert.Null(WindowsStorageSyncRoots.VolumeOf(missing + @":\FileCat sync root"));
        Assert.Null(WindowsStorageSyncRoots.VolumeOf(@"\\localhost\c$\FileCat sync root"));
        Assert.Null(WindowsStorageSyncRoots.VolumeOf(@"relative\sync root"));
        Assert.Equal(WindowsStorageDevice.Attachment.Unknown, WindowsStorageDevice.Describe("not-a-volume").Attachment);
        Assert.NotNull(WindowsStorageSyncRoots.RegisteredRootPaths());
        Assert.All(WindowsStorageSyncRoots.RegisteredRootVolumes()!, v => Assert.True(Guid.TryParseExact(v, "D", out _)));
    }

    /// <summary>
    /// FILECAT_STORAGE_NATIVE_HOTPLUG_TARGET names an existing file or folder on a USB, FireWire or card-reader disk that
    /// Windows reports as fixed media. Review only holds metadata handles; the refusal comes before any job exists.
    /// </summary>
    [Fact]
    public void A_hot_plug_disk_that_reports_fixed_media_is_refused_at_review()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        Assert.SkipWhen(!WindowsStorageTransforms.SupportsHost(arm64), "Qualified Windows 11 client fixture (x64, or native ARM64 when opted in).");
        string? target = Environment.GetEnvironmentVariable("FILECAT_STORAGE_NATIVE_HOTPLUG_TARGET");
        if (string.IsNullOrWhiteSpace(target)) Assert.Skip("Set FILECAT_STORAGE_NATIVE_HOTPLUG_TARGET to an existing item on a hot-plug fixed disk.");
        bool folder = Directory.Exists(target);
        Assert.True(folder || File.Exists(target), "The hot-plug target must exist.");
        var drive = new DriveInfo(Path.GetPathRoot(target)!);
        Assert.Equal((DriveType.Fixed, "NTFS"), (drive.DriveType, drive.DriveFormat));
        string binding = WindowsStorageSyncRoots.VolumeOf(target) ?? throw new InvalidOperationException("The target volume did not resolve.");
        var device = WindowsStorageDevice.Describe(binding);
        Assert.Equal(WindowsStorageDevice.Attachment.HotPlug, device.Attachment);
        Assert.False(device.RemovableMedia);
        string evidence = Directory.CreateTempSubdirectory("filecat-storage-hotplug-evidence-").FullName;
        try
        {
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), new WindowsStorageTransforms(qualifyNativeArm64: arm64),
                Path.Join(evidence, "journal"));
            foreach (var action in new[] { StorageAction.Compress, StorageAction.RemoveCompression })
            {
                var refused = Assert.Throws<StorageInspectionException>(() => manager.PrepareStorageReview(
                    [ItemRef.ForFileSystemPath(target, folder ? EntryKind.Directory : EntryKind.File)], action, Stop));
                Assert.Equal("hot-plug-unqualified", refused.Problem.Code);
            }
            Assert.Empty(manager.Jobs);
            Assert.False(Directory.Exists(Path.Join(evidence, "journal")));
        }
        finally { Directory.Delete(evidence, recursive: true); }
    }

    /// <summary>
    /// FILECAT_STORAGE_NATIVE_NOFILTER_ROOT names an owned directory on a freshly attached fixed NTFS volume with 4 KiB
    /// clusters and no Cloud Files filter instance. A reviewed Compress and Remove compression job must run there.
    /// </summary>
    [Fact]
    public void Reviewed_jobs_run_on_an_owned_volume_where_no_Cloud_Files_filter_answers()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        Assert.SkipWhen(!WindowsStorageTransforms.SupportsHost(arm64), "Qualified Windows 11 client fixture (x64, or native ARM64 when opted in).");
        string? owned = Environment.GetEnvironmentVariable("FILECAT_STORAGE_NATIVE_NOFILTER_ROOT");
        if (string.IsNullOrWhiteSpace(owned)) Assert.Skip("Set FILECAT_STORAGE_NATIVE_NOFILTER_ROOT to an owned directory on a prepared volume.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity, "Storage jobs must run in the normal user process.");
        string root = Directory.CreateDirectory(Path.Join(owned, "nofilter-" + Guid.NewGuid().ToString("N"))).FullName;
        string evidence = Directory.CreateTempSubdirectory("filecat-storage-nofilter-evidence-").FullName;
        try
        {
            string file = Path.Join(root, "dense payload.bin");
            byte[] payload = new byte[1 << 20];
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i % 7 == 0 ? i : 0x41);
            File.WriteAllBytes(file, payload);
            using (var held = File.OpenHandle(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                Assert.Equal(NoFilter, WindowsStorageCloudQuery.Query(held, Stop));
                string binding = WindowsStorageTransforms.ActualVolumeBinding(held, Stop).Identity;
                Assert.DoesNotContain(binding, WindowsStorageSyncRoots.RegisteredRootVolumes()!);
                Assert.Equal(WindowsStorageDevice.Attachment.Fixed, WindowsStorageDevice.Describe(binding).Attachment);
            }

            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(),
                Directory.CreateDirectory(Path.Join(evidence, "journals")).FullName, ArchiveJobBackends.Empty,
                new WindowsStorageTransforms(qualifyNativeArm64: arm64), arm64 ? WindowsStorageReviewEvidence.CreateArm64QualificationJournal : WindowsStorageReviewEvidence.CreateJournal,
                arm64 ? WindowsStorageReviewEvidence.Arm64QualificationBackend : WindowsStorageReviewEvidence.Backend);
            var source = ItemRef.ForFileSystemPath(file, EntryKind.File);
            var compressed = Run(manager, StorageAction.Compress, source);
            Assert.Equal(JobState.Completed, compressed.State);
            Assert.Equal(1, compressed.StorageResult!.Counts.Changed);
            Assert.True((File.GetAttributes(file) & FileAttributes.Compressed) != 0);
            Assert.Equal(payload, File.ReadAllBytes(file));
            var removed = Run(manager, StorageAction.RemoveCompression, source);
            Assert.Equal(JobState.Completed, removed.State);
            Assert.Equal(1, removed.StorageResult!.Counts.Changed);
            Assert.False((File.GetAttributes(file) & FileAttributes.Compressed) != 0);
            Assert.Equal(payload, File.ReadAllBytes(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(evidence, recursive: true);
        }

        static Job Run(JobManager manager, StorageAction action, ItemRef source)
        {
            using var review = manager.PrepareStorageReview([source], action, Stop);
            var observation = Assert.Single(review.Roots).Observation;
            Assert.True(observation.Ineligible is null, observation.Ineligible?.Code + " " + observation.Ineligible?.Message);
            using var finished = new ManualResetEventSlim();
            void Finished(Job _) => finished.Set();
            manager.JobFinished += Finished;
            try
            {
                var job = manager.SubmitStorage(review, review.Approve(action, StorageScope.SelectedItems), _ => { });
                Assert.True(finished.Wait(TimeSpan.FromSeconds(60), Stop));
                return job;
            }
            finally { manager.JobFinished -= Finished; }
        }
    }
}
