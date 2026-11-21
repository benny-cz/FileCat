using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// N-ID.C cloud breadth: a temporary Cloud Files sync root of this test's own provider (which serves no data and counts
/// every request) beside an ordinary sibling. Storage changes refuse the selected root and its pinned placeholder,
/// skip the root in a recursive job over the parent, and never cause a data request.
/// </summary>
public sealed partial class WindowsFileRecordsTests
{
    [Fact]
    public unsafe void Storage_changes_never_enter_a_registered_sync_root_or_ask_its_provider_for_data()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Cloud Files are Windows'.");
        if (!Opted("FILECAT_TEST_CLOUD_FILES")) Assert.Skip("Set FILECAT_TEST_CLOUD_FILES=1 to register a temporary Cloud Files sync root in a test folder.");
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        Assert.SkipWhen(!WindowsStorageTransforms.SupportsHost(arm64), "Qualified Windows client NTFS fixture.");
        Assert.SkipWhen(WindowsTokenAuthority.IsHighIntegrity, "Storage jobs run in the normal user process.");
        var ct = TestContext.Current.CancellationToken;
        string parent = NewFolder(), root = Path.Join(parent, "sync"), sibling = Path.Join(parent, "sibling.txt");
        string file = Path.Join(root, "cloud.txt"), evidence = NewFolder(); // outside the recursive scope
        Directory.CreateDirectory(root); File.WriteAllText(sibling, "ordinary sibling bytes");
        bool registered = false, connected = false;
        long connection = 0;
        var table = (CfCallbackRegistration*)NativeMemory.AllocZeroed((nuint)(2 * sizeof(CfCallbackRegistration)));
        int fetchesBefore = Volatile.Read(ref _fetches);
        void RequireNoFetch(string phase) =>
            Assert.True(Volatile.Read(ref _fetches) == fetchesBefore, $"{phase}: the provider received a data request.");
        try
        {
            if (!OnNtfs(root)) Assert.Skip("The test folder is not on NTFS.");
            fixed (char* name = "FileCat storage test")
            fixed (char* version = "1.0")
            fixed (byte* rootIdentity = "filecat-storage-root"u8)
            {
                var registration = new CfSyncRegistration
                {
                    StructSize = (uint)sizeof(CfSyncRegistration), ProviderName = (nint)name, ProviderVersion = (nint)version,
                    SyncRootIdentity = (nint)rootIdentity, SyncRootIdentityLength = 20, ProviderId = Guid.NewGuid(),
                };
                var policies = new CfSyncPolicies { StructSize = (uint)sizeof(CfSyncPolicies), HydrationPrimary = 2, PopulationPrimary = 3 };
                int registeredResult = CfRegisterSyncRoot(root, &registration, &policies, 0x2 | 0x4);
                Assert.True(registeredResult >= 0, $"CfRegisterSyncRoot: 0x{registeredResult:X8}");
                registered = true;
            }
            table[0] = new CfCallbackRegistration { Type = 0, Callback = (nint)(delegate* unmanaged[Stdcall]<nint, nint, void>)&Fetched };
            table[1] = new CfCallbackRegistration { Type = -1 };
            int connect = CfConnectSyncRoot(root, table, 0, 0, out connection);
            Assert.True(connect >= 0, $"CfConnectSyncRoot: 0x{connect:X8}");
            connected = true;
            long now = DateTime.UtcNow.ToFileTimeUtc();
            var identity = "filecat-storage-identity"u8.ToArray();
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
            RequireNoFetch("Fixture creation and pinning");

            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Directory.CreateDirectory(Path.Join(evidence, "journals")).FullName,
                ArchiveJobBackends.Empty, new WindowsStorageTransforms(qualifyNativeArm64: arm64),
                arm64 ? WindowsStorageReviewEvidence.CreateArm64QualificationJournal : WindowsStorageReviewEvidence.CreateJournal,
                arm64 ? WindowsStorageReviewEvidence.Arm64QualificationBackend : WindowsStorageReviewEvidence.Backend);
            foreach (var action in new[] { StorageAction.Compress, StorageAction.RemoveCompression })
            {
                using (var selected = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(root, EntryKind.Directory)], action, ct))
                    Assert.Equal("reparse-entry", Assert.Single(selected.Roots).Observation.Ineligible?.Code);
                Assert.Equal("ancestor-excluded", Assert.Throws<StorageInspectionException>(() =>
                    manager.PrepareStorageReview([ItemRef.ForFileSystemPath(file, EntryKind.File)], action, ct)).Problem.Code);
            }
            RequireNoFetch("Selected root and pinned placeholder reviews");

            using (var review = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(parent, EntryKind.Directory)], StorageAction.Compress, ct))
            {
                Assert.Null(Assert.Single(review.Roots).Observation.Ineligible);
                using var finished = new ManualResetEventSlim();
                manager.JobFinished += _ => finished.Set();
                var job = manager.SubmitStorage(review, review.Approve(StorageAction.Compress, StorageScope.ExistingDescendants), _ => { });
                Assert.True(finished.Wait(TimeSpan.FromSeconds(60), ct));
                var result = job.StorageResult!;
                TestContext.Current.TestOutputHelper?.WriteLine($"Parent job: {job.State}; changed {result.Counts.Changed}, skipped {result.Counts.Skipped}; " +
                    $"reasons {string.Join(',', result.Reasons.Select(r => r.Problem.Code + "×" + r.Count))}");
                Assert.Equal(JobState.CompletedWithIssues, job.State);
                Assert.Equal((2L, 1L), (result.Counts.Changed, result.Counts.Skipped));
                Assert.Contains(result.Reasons, r => r.Problem.Code == "reparse-entry");
            }
            RequireNoFetch("Recursive job over the sync root's parent");
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
            RequireNoFetch("Final placeholder checks");
        }
        finally
        {
            try { if (File.Exists(file)) File.Delete(file); }
            finally
            {
                if (connected) CfDisconnectSyncRoot(connection);
                if (registered) CfUnregisterSyncRoot(root);
                NativeMemory.Free(table);
                Directory.Delete(parent, recursive: true);
                Directory.Delete(evidence, recursive: true);
            }
        }
    }

    [LibraryImport("cldapi.dll")]
    private static partial int CfSetPinState(Microsoft.Win32.SafeHandles.SafeFileHandle file, int pinState, uint flags, nint overlapped);
}
