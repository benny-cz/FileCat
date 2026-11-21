using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// N-ID.C cloud breadth from another process. FileCat is never the provider of a sync root, and Windows may disguise
/// another provider's placeholders as ordinary files for it. So a child copy of the test assembly reviews and compresses
/// while this process serves the sync root:
/// <list type="bullet">
/// <item>the selected root and its pinned placeholder are refused for both actions;</item>
/// <item>a recursive job over the parent changes only the parent and its ordinary sibling;</item>
/// <item>the provider never receives a data request.</item>
/// </list>
/// </summary>
public sealed partial class WindowsFileRecordsTests
{
    private const string CloudStorageParent = "FILECAT_TEST_CLOUD_STORAGE_PARENT";
    private const string CloudStorageEvidence = "FILECAT_TEST_CLOUD_STORAGE_EVIDENCE";

    [Fact]
    public void Cloud_storage_child_reviews_and_compresses_as_its_parent_asks()
    {
        string? parent = Environment.GetEnvironmentVariable(CloudStorageParent), evidence = Environment.GetEnvironmentVariable(CloudStorageEvidence);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(evidence)) Assert.Skip("Started only by the cross-process Cloud Files storage fixture.");
        var ct = TestContext.Current.CancellationToken;
        string root = Path.Join(parent, "sync"), file = Path.Join(root, "cloud.txt");
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; // the parent's opt-in reaches the child through its environment
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Directory.CreateDirectory(Path.Join(evidence, "journals")).FullName,
            ArchiveJobBackends.Empty, new WindowsStorageTransforms(qualifyNativeArm64: arm64),
            arm64 ? WindowsStorageReviewEvidence.CreateArm64QualificationJournal : WindowsStorageReviewEvidence.CreateJournal,
            arm64 ? WindowsStorageReviewEvidence.Arm64QualificationBackend : WindowsStorageReviewEvidence.Backend);
        string Refusal(string path, EntryKind kind, StorageAction action)
        {
            try
            {
                using var review = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(path, kind)], action, ct);
                return Assert.Single(review.Roots).Observation.Ineligible?.Code ?? "admitted";
            }
            catch (StorageInspectionException ex) { return ex.Problem.Code; }
        }
        foreach (var action in new[] { StorageAction.Compress, StorageAction.RemoveCompression })
        {
            string rootCode = Refusal(root, EntryKind.Directory, action), fileCode = Refusal(file, EntryKind.File, action);
            TestContext.Current.TestOutputHelper?.WriteLine($"Child: {action} refused the sync root as {rootCode} and the placeholder as {fileCode}");
            Assert.NotEqual("admitted", rootCode);
            Assert.NotEqual("admitted", fileCode);
        }
        using var parentReview = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(parent, EntryKind.Directory)], StorageAction.Compress, ct);
        Assert.Null(Assert.Single(parentReview.Roots).Observation.Ineligible);
        using var finished = new ManualResetEventSlim();
        manager.JobFinished += _ => finished.Set();
        var job = manager.SubmitStorage(parentReview, parentReview.Approve(StorageAction.Compress, StorageScope.ExistingDescendants), _ => { });
        Assert.True(finished.Wait(TimeSpan.FromSeconds(60), ct));
        var result = job.StorageResult!;
        TestContext.Current.TestOutputHelper?.WriteLine($"Child: recursive job {job.State}; changed {result.Counts.Changed}, skipped {result.Counts.Skipped}; " +
            $"reasons {string.Join(',', result.Reasons.Select(r => r.Problem.Code + "x" + r.Count))}");
        Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Equal((2L, 1L), (result.Counts.Changed, result.Counts.Skipped));
    }

    [Fact]
    public async Task Storage_changes_from_another_process_refuse_the_sync_root_and_its_placeholder()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Cloud Files are Windows'.");
        if (!Opted("FILECAT_TEST_CLOUD_FILES")) Assert.Skip("Set FILECAT_TEST_CLOUD_FILES=1 to register a temporary Cloud Files sync root in a test folder.");
        Assert.SkipWhen(!WindowsStorageTransforms.SupportsHost(WindowsStorageAbiTests.QualifyNativeArm64Jobs),
            "A qualified compression host: a Windows 11 x64 client, or native ARM64 when opted in.");
        Assert.SkipWhen(WindowsTokenAuthority.IsHighIntegrity, "Storage jobs run in the normal user process.");
        string parent = NewFolder(), root = Path.Join(parent, "sync"), sibling = Path.Join(parent, "sibling.txt");
        string file = Path.Join(root, "cloud.txt"), evidence = NewFolder(); // outside the recursive scope
        Directory.CreateDirectory(root); File.WriteAllText(sibling, "ordinary sibling bytes");
        bool registered = false, connected = false;
        long connection = 0;
        nint table = 0;
        int fetchesBefore = Volatile.Read(ref _fetches);
        try
        {
            if (!OnNtfs(root)) Assert.Skip("The test folder is not on NTFS.");
            (registered, connected, connection, table) = ServePinnedPlaceholder(root, file);
            string exe = Path.ChangeExtension(typeof(WindowsFileRecordsTests).Assembly.Location, ".exe");
            await RunCloudChild(exe, nameof(Cloud_storage_child_reviews_and_compresses_as_its_parent_asks),
                new() { [CloudStorageParent] = parent, [CloudStorageEvidence] = evidence }, "storage");
            Assert.True(Volatile.Read(ref _fetches) == fetchesBefore, "The provider received a data request.");
            Assert.True((File.GetAttributes(sibling) & FileAttributes.Compressed) != 0);
            Assert.Equal("ordinary sibling bytes", File.ReadAllText(sibling));
            Assert.Equal((FileAttributes)0, File.GetAttributes(root) & FileAttributes.Compressed);
            using (var handle = CreateFile(file, 0x0010_0080, 7, 0, 3, 0x0220_0000, 0))
            {
                var problems = new List<string>();
                var after = WindowsFileRecords.PlaceholderInfo(handle, problems, out string? failed);
                Assert.True(after is not null, failed);
                Assert.Equal((0L, 1), (after.OnDiskDataSize, after.PinState));
            }
            Assert.Equal((FileAttributes)0, File.GetAttributes(file) & FileAttributes.Compressed);
            Assert.True(Volatile.Read(ref _fetches) == fetchesBefore, "The final checks caused a data request.");
        }
        finally
        {
            try { if (File.Exists(file)) File.Delete(file); }
            finally
            {
                if (connected) CfDisconnectSyncRoot(connection);
                if (registered) CfUnregisterSyncRoot(root);
                if (table != 0) FreeTable(table);
                Directory.Delete(parent, recursive: true);
                Directory.Delete(evidence, recursive: true);
            }
        }
    }

    /// <summary>Registers <paramref name="root"/> for a provider that serves no data and creates one pinned, cloud-only placeholder.</summary>
    private static unsafe (bool Registered, bool Connected, long Connection, nint Table) ServePinnedPlaceholder(string root, string file)
    {
        fixed (char* name = "FileCat storage process test")
        fixed (char* version = "1.0")
        fixed (byte* rootIdentity = "filecat-storage-process"u8)
        {
            var registration = new CfSyncRegistration
            {
                StructSize = (uint)sizeof(CfSyncRegistration), ProviderName = (nint)name, ProviderVersion = (nint)version,
                SyncRootIdentity = (nint)rootIdentity, SyncRootIdentityLength = 23, ProviderId = Guid.NewGuid(),
            };
            var policies = new CfSyncPolicies { StructSize = (uint)sizeof(CfSyncPolicies), HydrationPrimary = 2, PopulationPrimary = 3 };
            int registeredResult = CfRegisterSyncRoot(root, &registration, &policies, 0x2 | 0x4);
            Assert.True(registeredResult >= 0, $"CfRegisterSyncRoot: 0x{registeredResult:X8}");
        }
        var table = (CfCallbackRegistration*)NativeMemory.AllocZeroed((nuint)(2 * sizeof(CfCallbackRegistration)));
        table[0] = new CfCallbackRegistration { Type = 0 /* FETCH_DATA */, Callback = (nint)(delegate* unmanaged[Stdcall]<nint, nint, void>)&Fetched };
        table[1] = new CfCallbackRegistration { Type = -1 };
        long connection = 0;
        bool connected = false;
        try
        {
            int connect = CfConnectSyncRoot(root, table, 0, 0, out connection);
            Assert.True(connect >= 0, $"CfConnectSyncRoot: 0x{connect:X8}");
            connected = true;
            long now = DateTime.UtcNow.ToFileTimeUtc();
            var identity = "filecat-storage-process-identity"u8.ToArray();
            fixed (char* relative = "cloud.txt")
            fixed (byte* id = identity)
            {
                var create = new CfPlaceholderCreateInfo
                {
                    RelativeFileName = (nint)relative, CreationTime = now, LastAccessTime = now, LastWriteTime = now, ChangeTime = now,
                    FileAttributes = 0x80, FileSize = 5000, FileIdentity = (nint)id, FileIdentityLength = (uint)identity.Length, Flags = 0x2,
                };
                int made = CfCreatePlaceholders(root, &create, 1, 0, out uint processed);
                Assert.True(made >= 0 && processed == 1 && create.Result >= 0, $"CfCreatePlaceholders: 0x{made:X8}");
            }
            using (var placeholder = CreateFile(file, 0x0010_0180 /* SYNCHRONIZE | FILE_WRITE_ATTRIBUTES | FILE_READ_ATTRIBUTES */, 7, 0, 3,
                0x0220_0000 /* OPEN_REPARSE_POINT | BACKUP_SEMANTICS */, 0))
            {
                Assert.False(placeholder.IsInvalid, "Open the placeholder: " + Marshal.GetLastPInvokeError());
                int pinned = CfSetPinState(placeholder, 1 /* CF_PIN_STATE_PINNED */, 0, 0);
                Assert.True(pinned >= 0, $"CfSetPinState: 0x{pinned:X8}");
            }
        }
        catch
        {
            // Undo this helper's own registration: the caller never receives it when a step fails.
            if (connected) CfDisconnectSyncRoot(connection);
            CfUnregisterSyncRoot(root);
            NativeMemory.Free(table);
            throw;
        }
        return (true, true, connection, (nint)table);
    }
}
