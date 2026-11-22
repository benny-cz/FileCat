using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Analysis;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// J1 on Windows: the protected permissions journal. Its storage is private to the profile owner; a held, aliased or
/// widened journal supplies no evidence and is never pruned; complete history is pruned only beyond retention; and a
/// process killed around a real DACL change leaves exactly the recorded prefix for recovery.
/// </summary>
public sealed partial class WindowsPermissionJournalTests
{
    private const string AuthorityId = "local";
    private const string Interactive = "S-1-5-4";
    private const string ChildRoot = "FILECAT_TEST_PERMISSION_JOURNAL_CHILD_ROOT";
    private const string ChildBoundary = "FILECAT_TEST_PERMISSION_JOURNAL_CHILD_BOUNDARY";

    [Fact]
    public void Journal_storage_is_private_and_a_held_aliased_or_widened_journal_supplies_no_evidence()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs an ordinary Windows process.");
        var ct = TestContext.Current.CancellationToken;
        var fixture = Directory.CreateTempSubdirectory("filecat-acl-journal-");
        try
        {
            string me = CurrentSid(), folder = Path.Combine(fixture.FullName, PermissionJournalNames.Directory);
            string leaf = PermissionJournalNames.Name(DateTime.UtcNow, Guid.NewGuid());
            var tracked = WindowsAnalysisPrivateDirectory.CreatePermissionJournal(fixture.FullName, leaf, ct);
            using (var directory = new WindowsAnalysisPrivateDirectory(fixture.FullName, PermissionJournalNames.Directory))
            {
                using (tracked.Owner)
                using (tracked.Stream)
                {
                    Assert.Equal(Path.Combine(folder, leaf), tracked.Path);
                    // Independent reads: the profile owner and a protected DACL for SYSTEM and that owner only.
                    Assert.Equal($"O:{me}D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;{me})", Stored(folder));
                    Assert.Equal($"O:{me}D:P(A;;FA;;;SY)(A;;FA;;;{me})", Stored(tracked.Path));
                    tracked.Verify();
                    // While its writer holds it, the journal can be neither read nor pruned, and its name cannot move.
                    Assert.Throws<IOException>(() => directory.OpenPermissionJournal(leaf));
                    Assert.Throws<IOException>(() => directory.OpenPermissionJournalMaintenance(leaf));
                    Assert.Throws<IOException>(() => File.Move(tracked.Path, tracked.Path + ".moved"));
                    Assert.Empty(PermissionJournalRecovery.Scan(directory, folder, ct, keepFinished: 0));
                }
                // Released without a header, it is retained as damaged history rather than pruned.
                var damaged = Assert.Single(PermissionJournalRecovery.Scan(directory, folder, ct, keepFinished: 0));
                Assert.True(damaged.PermissionReview!.Damaged);
                Assert.Equal(tracked.Path, damaged.JournalPath);
                using (var reader = directory.OpenPermissionJournal(leaf)) reader.Verify();

                // A second name makes it unreadable.
                string alias = Path.Combine(fixture.FullName, "alias");
                Assert.True(HardLink(alias, tracked.Path, 0), $"CreateHardLinkW failed: {Marshal.GetLastPInvokeError()}");
                Assert.Throws<IOException>(() => directory.OpenPermissionJournal(leaf));
                Assert.Empty(PermissionJournalRecovery.Scan(directory, folder, ct, keepFinished: 0));
                File.Delete(alias);

                // So does a grant beyond SYSTEM and the owner, and such a journal is not pruned either.
                var info = new FileInfo(tracked.Path);
                var security = info.GetAccessControl();
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    FileSystemRights.Read, AccessControlType.Allow));
                info.SetAccessControl(security);
                Assert.Throws<IOException>(() => directory.OpenPermissionJournal(leaf));
                Assert.Throws<IOException>(() => directory.OpenPermissionJournalMaintenance(leaf));
                Assert.Empty(PermissionJournalRecovery.Scan(directory, folder, ct, keepFinished: 0));
                Assert.True(File.Exists(tracked.Path));
            }

            // A journal parent reached through a junction is refused before anything is created behind it.
            string elsewhere = Directory.CreateDirectory(Path.Combine(fixture.FullName, "elsewhere")).FullName;
            string redirect = Path.Combine(fixture.FullName, "redirect");
            Junction.Create(redirect, elsewhere);
            try
            {
                Assert.Throws<IOException>(() => WindowsAnalysisPrivateDirectory.CreatePermissionJournal(redirect,
                    PermissionJournalNames.Name(DateTime.UtcNow, Guid.NewGuid()), ct));
                Assert.Empty(Directory.GetFileSystemEntries(elsewhere));
            }
            finally { Assert.True(RemoveDirectory(redirect), $"RemoveDirectoryW failed: {Marshal.GetLastPInvokeError()}"); }
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(fixture.FullName); }
    }

    [Fact]
    public async Task A_verified_DACL_change_leaves_complete_private_history_that_only_retention_prunes()
    {
        RequireOrdinaryNtfs();
        var ct = TestContext.Current.CancellationToken;
        var fixture = Directory.CreateTempSubdirectory("filecat-acl-journal-job-");
        try
        {
            string me = CurrentSid();
            string path = CreateDocument(fixture.FullName, me);
            string before = StoredDacl(path);
            using (var change = Change.Prepare(path, ct))
            {
                var jobs = Jobs(fixture.FullName, (leaf, cancellation) =>
                    WindowsAnalysisPrivateDirectory.CreatePermissionJournal(fixture.FullName, leaf, cancellation));
                var finished = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
                jobs.JobFinished += job => finished.TrySetResult(job);
                var job = jobs.SubmitPermissions(change.Review);
                Assert.Same(job, await finished.Task.WaitAsync(TimeSpan.FromSeconds(30), ct));
                var result = Assert.Single(job.PermissionResults);
                Assert.True(result.Verdict == PermissionVerdict.Verified, $"{result.Verdict}: {result.Detail}");
            }
            Assert.Equal(before + "(A;;FR;;;IU)", StoredDacl(path));

            string folder = Path.Combine(fixture.FullName, PermissionJournalNames.Directory);
            string journal = Assert.Single(Directory.GetFiles(folder));
            Assert.True(PermissionJournalNames.TryParse(Path.GetFileName(journal), out _, out _));
            Assert.Equal($"O:{me}D:P(A;;FA;;;SY)(A;;FA;;;{me})", Stored(journal));
            using var directory = new WindowsAnalysisPrivateDirectory(fixture.FullName, PermissionJournalNames.Directory);
            // Complete, verified history needs no review. It stays within the retention count and is pruned beyond it.
            Assert.Empty(PermissionJournalRecovery.Scan(directory, folder, ct));
            Assert.True(File.Exists(journal));
            Assert.Empty(PermissionJournalRecovery.Scan(directory, folder, ct, keepFinished: 0));
            Assert.False(File.Exists(journal));
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(fixture.FullName); }
    }

    [Theory]
    [InlineData("begin", false, false, false)]
    [InlineData("intent", false, true, false)]
    [InlineData("native-return", true, true, false)]
    [InlineData("outcome", true, true, true)]
    public async Task A_process_killed_around_a_DACL_change_leaves_the_recorded_prefix(string boundary, bool changed, bool intent, bool outcome)
    {
        RequireOrdinaryNtfs();
        var ct = TestContext.Current.CancellationToken;
        if (Environment.GetEnvironmentVariable(ChildRoot) is { Length: > 0 } childRoot)
        {
            // The child half: hold the job at the requested journal boundary until the parent kills this process.
            if (Environment.GetEnvironmentVariable(ChildBoundary) == boundary) await HoldAtBoundary(childRoot, boundary, ct);
            return;
        }

        var fixture = Directory.CreateTempSubdirectory("filecat-acl-journal-crash-");
        try
        {
            string me = CurrentSid();
            string path = CreateDocument(fixture.FullName, me);
            string before = StoredDacl(path);
            string host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
            var start = new ProcessStartInfo(host)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string argument in new[] { typeof(WindowsPermissionJournalTests).Assembly.Location, "-method",
                         typeof(WindowsPermissionJournalTests).FullName + "." + nameof(A_process_killed_around_a_DACL_change_leaves_the_recorded_prefix),
                         "-noAutoReporters", "-noColor", "-noLogo" })
                start.ArgumentList.Add(argument);
            start.Environment[ChildRoot] = fixture.FullName;
            start.Environment[ChildBoundary] = boundary;
            start.Environment["DOTNET_DbgEnableMiniDump"] = "0";
            string ready = Path.Combine(fixture.FullName, "ready");
            using (var child = Process.Start(start) ?? throw new IOException("The journal child did not start."))
            {
                var stdout = child.StandardOutput.ReadToEndAsync(ct);
                var stderr = child.StandardError.ReadToEndAsync(ct);
                try
                {
                    var deadline = DateTime.UtcNow.AddSeconds(60);
                    while (!File.Exists(ready))
                    {
                        if (child.HasExited)
                            Assert.Fail($"The child exited with {child.ExitCode} before {boundary}. {await stdout} {await stderr}");
                        Assert.True(DateTime.UtcNow < deadline, $"The child did not reach {boundary}.");
                        await Task.Delay(25, ct);
                    }
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(20), ct);
                }
                finally
                {
                    if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(10_000); }
                }
            }

            Assert.Equal(changed ? before + "(A;;FR;;;IU)" : before, StoredDacl(path));
            string folder = Path.Combine(fixture.FullName, PermissionJournalNames.Directory);
            using var directory = new WindowsAnalysisPrivateDirectory(fixture.FullName, PermissionJournalNames.Directory);
            var interrupted = Assert.Single(PermissionJournalRecovery.Scan(directory, folder, ct, keepFinished: 0));
            Assert.Equal(nameof(JobKind.Permissions), interrupted.Kind);
            Assert.False(interrupted.CanContinueTransfer);
            Assert.Equal([path], interrupted.Sources);
            var history = Assert.IsType<InterruptedPermissionReview>(interrupted.PermissionReview);
            Assert.False(history.Damaged);
            Assert.Equal(PermissionScope.Objects, history.Scope);
            if (intent)
            {
                var call = Assert.Single(history.Calls);
                Assert.Equal(PermissionSections.Access, call.Section);
                Assert.Equal(outcome ? PermissionVerdict.Verified : (PermissionVerdict?)null, call.Outcome);
            }
            else Assert.Empty(history.Calls);
            Assert.Empty(history.ObjectVerdicts);
            Assert.True(File.Exists(interrupted.JournalPath));
            Assert.Equal($"O:{me}D:P(A;;FA;;;SY)(A;;FA;;;{me})", Stored(interrupted.JournalPath));
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(fixture.FullName); }
    }

    // Abrupt device loss (N1, G-MOUNT): the NTFS volume holding the reviewed object is hot-removed after the call's intent
    // is durable and before dispatch. The recheck on the held handle fails, so no write is attempted, the object reports
    // a failure, and the history on the system volume is complete. A host harness owns the disk: the test signals it from
    // the control folder and waits until the volume is gone (.tmp/acl-validation/hotremove).
    [Fact]
    public async Task A_volume_removed_after_the_intent_receives_no_write_and_leaves_complete_history()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_HOTREMOVE_ROOT");
        string? control = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_HOTREMOVE_CONTROL");
        if (!OperatingSystem.IsWindows() || root is null || control is null || ReviewedPermissionOperation.ProcessRefusal() is not null ||
            AnalysisAdmission.Refusal() is not null)
            Assert.Skip("Requires the hot-removable NTFS volume harness.");
        var ct = TestContext.Current.CancellationToken;
        var (removable, wait) = RemovalHarness(root);
        string path = CreateDocument(root, CurrentSid());
        File.WriteAllText(Path.Combine(control, "before.sddl"), StoredDacl(path));
        using var change = Change.Prepare(path, ct, removable);
        var jobs = Jobs(control, (leaf, cancellation) =>
        {
            var journal = WindowsAnalysisPrivateDirectory.CreatePermissionJournal(control, leaf, cancellation);
            int checks = 0;
            return journal with
            {
                Verify = () =>
                {
                    journal.Verify();
                    if (++checks != 4) return; // after the intent flush, before the recheck and dispatch
                    File.WriteAllText(Path.Combine(control, "ready"), "intent");
                    var deadline = DateTime.UtcNow + wait;
                    while (Directory.Exists(root))
                    {
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("The harness did not remove the volume.");
                        Thread.Sleep(100);
                    }
                }
            };
        });
        var finished = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.JobFinished += job => finished.TrySetResult(job);
        var submitted = jobs.SubmitPermissions(change.Review);
        Assert.Same(submitted, await finished.Task.WaitAsync(wait + TimeSpan.FromMinutes(2), ct));
        Assert.False(Directory.Exists(root));
        var result = Assert.Single(submitted.PermissionResults);
        var component = Assert.Single(result.Components);
        File.WriteAllText(Path.Combine(control, "result.txt"), $"{result.Verdict}|{component.Verdict}|{component.NativeCall}|{component.Detail}");
        Assert.Contains(result.Verdict, new[] { PermissionVerdict.FailedBeforeChange, PermissionVerdict.Conflict });
        Assert.Null(component.NativeCall);
        Assert.Null(component.NativeError);

        string folder = Path.Combine(control, PermissionJournalNames.Directory);
        var records = JobJournal.ReadAll(Assert.Single(Directory.GetFiles(folder)));
        Assert.Single(records, record => record.Type == "permission-intent");
        Assert.Equal(result.Verdict.ToString(), Assert.Single(records, record => record.Type == "permission-outcome").Get("verdict"));
        using var directory = new WindowsAnalysisPrivateDirectory(control, PermissionJournalNames.Directory);
        Assert.Empty(PermissionJournalRecovery.Scan(directory, folder, ct));
    }

    // G-RESULT device loss between the call and the flush: the NTFS volume holding the reviewed object is hot-removed after
    // the native call changed its DACL and before the file system is asked to write its pending changes. The host harness
    // flushes the volume cache once the call's intent is durable, so the document and its original DACL are on the device,
    // and removes the disk without another flush while the test waits in the flush hook. The flush on the held folder, or
    // the read-back after it, fails, so the job does not report the change as verified (.tmp/acl-validation/gresult-windows-flush).
    [Fact]
    public async Task A_volume_removed_between_the_call_and_the_flush_is_not_verified()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_FLUSHLOSS_ROOT");
        string? control = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_FLUSHLOSS_CONTROL");
        if (!OperatingSystem.IsWindows() || root is null || control is null || ReviewedPermissionOperation.ProcessRefusal() is not null ||
            AnalysisAdmission.Refusal() is not null)
            Assert.Skip("Requires the hot-removable NTFS volume harness.");
        var ct = TestContext.Current.CancellationToken;
        var (removable, wait) = RemovalHarness(root);
        string path = CreateDocument(root, CurrentSid());
        File.WriteAllText(Path.Combine(control, "before.sddl"), StoredDacl(path));
        using var change = Change.Prepare(path, ct, removable);
        int flushes = 0;
        var jobs = Jobs(control, (leaf, cancellation) =>
        {
            var journal = WindowsAnalysisPrivateDirectory.CreatePermissionJournal(control, leaf, cancellation);
            int checks = 0;
            return journal with
            {
                Verify = () =>
                {
                    journal.Verify();
                    if (++checks != 4) return; // the call's intent is durable: the harness persists the volume's state
                    Signal("intent", "the volume cache flush", () => File.Exists(Path.Combine(control, "cache-flushed")));
                }
            };
        });
        WindowsPermissionService.FlushFaultForTests = folder =>
        {
            if (!string.Equals(folder, root, StringComparison.OrdinalIgnoreCase) || Interlocked.Increment(ref flushes) != 1) return 0;
            Signal("ready", "the volume removal", () => !Directory.Exists(root));
            return 0; // the real FlushFileBuffers now runs on the held folder of the removed volume
        };
        try
        {
            var finished = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            jobs.JobFinished += job => finished.TrySetResult(job);
            var submitted = jobs.SubmitPermissions(change.Review);
            Assert.Same(submitted, await finished.Task.WaitAsync(2 * wait + TimeSpan.FromMinutes(2), ct));
            var result = Assert.Single(submitted.PermissionResults);
            var component = Assert.Single(result.Components);
            File.WriteAllText(Path.Combine(control, "result.txt"), $"{result.Verdict}|{component.Verdict}|{component.Verification}|" +
                $"{component.NativeCall}|{component.NativeError}|flushes={flushes}|{component.Detail}");
            Assert.False(Directory.Exists(root));
            Assert.InRange(flushes, 1, 3);
            Assert.NotNull(component.NativeCall);
            Assert.NotEqual(PermissionVerdict.Verified, result.Verdict);
            Assert.Equal(PermissionVerification.Unavailable, component.Verification);
            string folder = Path.Combine(control, PermissionJournalNames.Directory);
            var records = JobJournal.ReadAll(Assert.Single(Directory.GetFiles(folder)));
            Assert.Single(records, record => record.Type == "permission-intent");
            Assert.Equal(result.Verdict.ToString(), Assert.Single(records, record => record.Type == "permission-outcome").Get("verdict"));
        }
        finally { WindowsPermissionService.FlushFaultForTests = null; }

        void Signal(string name, string awaited, Func<bool> done)
        {
            File.WriteAllText(Path.Combine(control!, name), name);
            var deadline = DateTime.UtcNow + wait;
            while (!done())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException($"The harness did not complete {awaited}.");
                Thread.Sleep(100);
            }
        }
    }

    // The child half of the kill test: the real Windows writer and journal, stopped after the given journal check.
    // Each record is verified before and after its flush: begin (checks 1-2), the call intent (3-4, before dispatch)
    // and its outcome (5-6). Check 5 comes after the native call and read-back have returned.
    private static async Task HoldAtBoundary(string root, string boundary, CancellationToken ct)
    {
        int stopAt = boundary switch { "begin" => 2, "intent" => 4, "native-return" => 5, "outcome" => 6, _ => 0 };
        Assert.NotEqual(0, stopAt);
        using var change = Change.Prepare(Path.Combine(root, "document"), ct);
        var jobs = Jobs(root, (leaf, cancellation) =>
        {
            var journal = WindowsAnalysisPrivateDirectory.CreatePermissionJournal(root, leaf, cancellation);
            int checks = 0;
            return journal with
            {
                Verify = () =>
                {
                    journal.Verify();
                    if (++checks != stopAt) return;
                    File.WriteAllText(Path.Combine(root, "ready"), boundary);
                    Thread.Sleep(Timeout.Infinite);
                }
            };
        });
        jobs.SubmitPermissions(change.Review);
        await Task.Delay(TimeSpan.FromSeconds(60), ct);
        Assert.Fail($"The parent did not end the child at {boundary}.");
    }

    /// <summary>One reviewed grant of read access to INTERACTIVE, with the inspection that binds it.</summary>
    private sealed class Change : IDisposable
    {
        private readonly PermissionInspection _inspection;
        internal ReviewedPermissionOperation Review { get; }

        private Change(PermissionInspection inspection, ReviewedPermissionOperation review) { _inspection = inspection; Review = review; }

        internal static Change Prepare(string path, CancellationToken ct, bool removable = false)
        {
            var source = ItemRef.ForFileSystemPath(path, EntryKind.File);
            var subject = new PermissionSubject(source.Parent, path, path, new(PermissionAuthorityKind.Local, AuthorityId, 0),
                PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            var inspection = new WindowsPermissionService(qualifyRemovableReads: removable, qualifyRemovableWrites: removable)
                .Inspect(subject, PermissionSections.Ordinary, ct);
            try
            {
                var grant = WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Allow, new(PrincipalKind.WindowsSid, Interactive, AuthorityId),
                    (uint)(FileSystemRights.Read | FileSystemRights.Synchronize));
                var prepared = WindowsPermissionService.Prepare(inspection, source, new(PermissionScope.Objects, [new AddWindowsAce(grant)]), ct,
                    removableWrites: removable);
                return new(inspection, prepared.Review);
            }
            catch { inspection.Dispose(); throw; }
        }

        public void Dispose() { try { Review.Dispose(); } finally { _inspection.Dispose(); } }
    }

    // FILECAT_TEST_PERMISSIONS_REMOVAL_REMOVABLE=1: the volume is removable media (a USB stick a person pulls), written
    // through the qualified removable writer; FILECAT_TEST_PERMISSIONS_REMOVAL_MINUTES gives the person time to act.
    private static (bool Removable, TimeSpan Wait) RemovalHarness(string root)
    {
        bool removable = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_REMOVAL_REMOVABLE") == "1";
        double minutes = double.TryParse(Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_REMOVAL_MINUTES"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) &&
            value is > 0 and <= 60 ? value : 3;
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        Assert.Equal(removable ? DriveType.Removable : DriveType.Fixed, drive.DriveType);
        Assert.Equal("NTFS", drive.DriveFormat);
        return (removable, TimeSpan.FromMinutes(minutes));
    }

    private static JobManager Jobs(string root, Func<string, CancellationToken, PermissionJournalFile> journal)
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        return new JobManager(new PortableFileOperations(), providers, root, ArchiveJobBackends.Empty, new NoStorageTransforms(),
            createPermissionJournal: journal);
    }

    // A protected DACL set the ordinary way (SetSecurityInfo): the owner with full control and Users with read access.
    private static string CreateDocument(string folder, string me)
    {
        string path = Path.Combine(folder, "document");
        File.WriteAllText(path, "fixture");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(me), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
        return path;
    }

    private static string CurrentSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!.Value;
    }

    // Owner and DACL as stored, read independently of FileCat, keeping only the protection flag.
    private static string Stored(string path)
    {
        var raw = new RawSecurityDescriptor(FileSecurityBytes(path, 1 | 4), 0);
        var kept = raw.ControlFlags & (ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected);
        return new RawSecurityDescriptor(kept, raw.Owner, null, null, raw.DiscretionaryAcl)
            .GetSddlForm(AccessControlSections.Owner | AccessControlSections.Access);
    }

    private static string StoredDacl(string path) =>
        new RawSecurityDescriptor(FileSecurityBytes(path, 4), 0).GetSddlForm(AccessControlSections.Access);

    private static byte[] FileSecurityBytes(string path, uint information)
    {
        _ = GetFileSecurity(path, information, null, 0, out uint length);
        byte[] descriptor = new byte[length];
        Assert.True(GetFileSecurity(path, information, descriptor, length, out _), $"GetFileSecurityW failed: {Marshal.GetLastPInvokeError()}");
        return descriptor;
    }

    private static void RequireOrdinaryNtfs()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_NTFS") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null || AnalysisAdmission.Refusal() is not null)
            Assert.Skip("Requires an explicitly enabled ordinary Windows x64 NTFS test volume.");
    }

    [LibraryImport("advapi32.dll", EntryPoint = "GetFileSecurityW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileSecurity(string path, uint information, byte[]? descriptor, uint length, out uint needed);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HardLink(string link, string existing, nint attributes);

    [LibraryImport("kernel32.dll", EntryPoint = "RemoveDirectoryW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveDirectory(string path);
}
