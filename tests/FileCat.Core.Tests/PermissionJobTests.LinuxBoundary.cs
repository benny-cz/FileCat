using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;

namespace FileCat.Core.Tests;

// G-LINUX native boundaries. The Ubuntu harness runs each case under the condition it names: a seccomp policy that denies
// the ACL read or the writer's calls, statx results without the fields a binding requires, procfs from another PID
// namespace or a namespace's own procfs, and the libc exports the Linux route binds to.
public sealed partial class PermissionJobTests
{
    private const uint StatxType = 0x1, StatxMode = 0x2, StatxUid = 0x8, StatxGid = 0x10, StatxInode = 0x100, StatxMountId = 0x1000;

    private static void RequireLinuxBoundary(string variable, string value)
    {
        RequireExt4Writer();
        if (Environment.GetEnvironmentVariable(variable) != value)
            Assert.Skip($"Requires the native boundary harness ({variable}={value}).");
    }

    private static PermissionDraft ModeDraft(ushort mode) => new(PermissionScope.Objects, [new ChangePermissionMode(mode, 0xfff)]);

    // A seccomp policy fails getxattr with EPERM. The ACL section reports the denial with the kernel's error, the mode is
    // still read, and neither an ACL nor a mode change can be reviewed without the ACL.
    [Fact]
    public void Ext4_seccomp_denied_acl_read_is_reported_and_admits_no_review()
    {
        RequireLinuxBoundary("FILECAT_TEST_PERMISSIONS_SECCOMP", "acl-read");
        using var fixture = new Fixture();
        var (item, subject) = Ext4Subject(fixture);
        var ct = TestContext.Current.CancellationToken;
        var service = new LinuxPermissionService(qualifyDefaultWrites: true);
        using var inspection = service.Inspect(subject, PermissionSections.Ordinary, ct);
        var snapshot = inspection.Snapshot;
        Assert.NotNull(snapshot.Binding);
        Assert.Equal((ushort)0x180, snapshot.Mode.Value);
        Assert.Equal(PermissionReadStatus.Denied, snapshot.Access.Status);
        Assert.Equal(1, snapshot.Access.NativeError);
        Assert.False(service.CanEditAccessAcl(snapshot));
        Assert.Equal("This change depends on the access ACL, and FileCat could not read it.", LinuxPermissionService.ModeReviewRefusal(snapshot));
        var acl = new PermissionDraft(PermissionScope.Objects, [new ChangePosixAcl(false,
            [new(PosixAclTag.Owner, 6, null), new(PosixAclTag.OwningGroup, 4, null), new(PosixAclTag.Other, 0, null)])]);
        Assert.ThrowsAny<Exception>(() => LinuxPermissionService.Prepare(inspection, item, ModeDraft(0x1a0), false, ct));
        Assert.ThrowsAny<Exception>(() => LinuxPermissionService.Prepare(inspection, item, acl, false, ct));
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(item.FileSystemPath!));
    }

    // A seccomp policy fails fchmodat2 with ENOSYS, as a container whose policy predates the call does, and setxattr with
    // EPERM. Each job fails before change with the kernel's error and leaves the object as it was: no other route is tried.
    [Theory]
    [InlineData("mode", 38)]
    [InlineData("acl", 1)]
    public async Task Ext4_seccomp_denied_writes_fail_before_change_without_another_route(string change, int error)
    {
        RequireLinuxBoundary("FILECAT_TEST_PERMISSIONS_SECCOMP", "writes");
        using var fixture = new Fixture(nativeJournal: true);
        var (item, subject) = Ext4Subject(fixture);
        string path = item.FileSystemPath!;
        string aclBefore = RunAclTool("getfacl", "-ncp", path);
        var ct = TestContext.Current.CancellationToken;
        using var inspection = new LinuxPermissionService().Inspect(subject, PermissionSections.Ordinary, ct);
        var draft = change == "mode" ? ModeDraft(0x1a0) : new PermissionDraft(PermissionScope.Objects, [new ChangePosixAcl(false,
            [new(PosixAclTag.Owner, 6, null), new(PosixAclTag.NamedUser, 4, new Principal(PrincipalKind.UnixUser, "12345", subject.Authority.Id)),
             new(PosixAclTag.OwningGroup, 0, null), new(PosixAclTag.Mask, 4, null), new(PosixAclTag.Other, 0, null)])]);
        using var review = LinuxPermissionService.Prepare(inspection, item, draft, false, ct).Review;
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        var result = Assert.Single(job.PermissionResults);
        var component = Assert.Single(result.Components);
        Assert.True(result.Verdict == PermissionVerdict.FailedBeforeChange, $"{result.Verdict}: {component.Detail}");
        Assert.Equal(error, component.NativeError);
        Assert.Equal(PermissionVerification.SameObject, component.Verification);
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(path));
        Assert.Equal(aclBefore, RunAclTool("getfacl", "-ncp", path));
    }

    // A seccomp supervisor answers FileCat's statx calls for the subject with result-mask bits removed and their fields
    // zeroed (a reader that ignored the mask would see root as the owner), or with an error. Missing permission bits or
    // principals are unknown and admit no review; a missing type, inode or mount identity admits no binding; a job whose
    // recheck loses the mode or the owner makes no call.
    [Fact]
    public async Task Ext4_statx_results_without_required_fields_are_unknown()
    {
        RequireExt4Writer();
        string control = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_STATX_CONTROL") ?? string.Empty;
        if (control.Length == 0) Assert.Skip("Requires the statx supervisor harness.");
        using var fixture = new Fixture();
        var (item, subject) = Ext4Subject(fixture);
        string path = item.FileSystemPath!;
        var ct = TestContext.Current.CancellationToken;
        var service = new LinuxPermissionService();
        try
        {
            using (var whole = Inspect(0))
            {
                Assert.Equal((ushort)0x180, whole.Snapshot.Mode.Value);
                Assert.Equal(PermissionReadStatus.Present, whole.Snapshot.Owner.Status);
                Assert.Null(LinuxPermissionService.ModeReviewRefusal(whole.Snapshot));
            }
            using (var modeless = Inspect(StatxMode))
            {
                Assert.Equal(PermissionReadStatus.Incomplete, modeless.Snapshot.Mode.Status);
                Assert.Null(modeless.Snapshot.Mode.Value);
                Assert.Equal(PermissionReadStatus.Present, modeless.Snapshot.Owner.Status);
                Assert.NotNull(LinuxPermissionService.ModeReviewRefusal(modeless.Snapshot));
                Assert.ThrowsAny<Exception>(() => LinuxPermissionService.Prepare(modeless, item, ModeDraft(0x1a0), false, ct));
            }
            using (var ownerless = Inspect(StatxUid | StatxGid))
            {
                Assert.Equal(PermissionReadStatus.Incomplete, ownerless.Snapshot.Owner.Status);
                Assert.Equal(PermissionReadStatus.Incomplete, ownerless.Snapshot.Group.Status);
                Assert.Null(ownerless.Snapshot.Owner.Value);
                Assert.Null(ownerless.Snapshot.Group.Value);
                Assert.Equal("FileCat could not read the original object's owner.", LinuxPermissionService.ModeReviewRefusal(ownerless.Snapshot));
                Assert.ThrowsAny<Exception>(() => LinuxPermissionService.Prepare(ownerless, item, ModeDraft(0x1a0), false, ct));
            }
            foreach (uint identity in new[] { StatxType, StatxInode, StatxMountId })
            {
                using var unbound = Inspect(identity);
                Assert.Null(unbound.Snapshot.Binding);
                Assert.Equal(PermissionReadStatus.Incomplete, unbound.Snapshot.Mode.Status);
                Assert.Equal("statx did not establish the type, inode and mount identity.", unbound.Snapshot.BindingProblem);
            }
            using (var denied = Inspect(0, error: 1))
            {
                Assert.Null(denied.Snapshot.Binding);
                Assert.Equal(PermissionReadStatus.Denied, denied.Snapshot.Mode.Status);
                Assert.Equal(1, denied.Snapshot.Mode.NativeError);
            }

            // Reviewed with every field, rechecked without the mode or the owner: the job makes no call.
            foreach (uint lost in new[] { StatxMode, StatxUid })
            {
                using var jobFixture = new Fixture();
                using var inspection = Inspect(0);
                using var review = LinuxPermissionService.Prepare(inspection, item, ModeDraft(0x1a0), false, ct).Review;
                Answer(lost);
                var job = jobFixture.Manager.SubmitPermissions(review);
                await jobFixture.Finish(job);
                var result = Assert.Single(job.PermissionResults);
                Assert.NotEqual(PermissionVerdict.Verified, result.Verdict);
                Assert.All(result.Components, component => Assert.Null(component.NativeCall));
                Answer(0);
                Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(path));
            }
        }
        finally { Answer(0); }

        void Answer(uint clear, int error = 0)
        {
            File.WriteAllText(control + ".next", string.Create(CultureInfo.InvariantCulture, $"{path}\n{clear:x}\n{error}\n"));
            File.Move(control + ".next", control, overwrite: true);
        }

        PermissionInspection Inspect(uint clear, int error = 0)
        {
            Answer(clear, error);
            return service.Inspect(subject, PermissionSections.Ordinary, ct);
        }
    }

    // This process runs in a new PID namespace while /proc still belongs to the parent namespace, so procfs cannot show
    // this process's own descriptors and mount table. The reader refuses the binding and admits no review.
    [Fact]
    public void Ext4_reader_refuses_procfs_from_another_pid_namespace()
    {
        RequireLinuxBoundary("FILECAT_TEST_PERMISSIONS_PROCFS", "foreign");
        Assert.NotEqual(Environment.ProcessId.ToString(CultureInfo.InvariantCulture), new FileInfo("/proc/self").LinkTarget);
        using var fixture = new Fixture();
        var (item, subject) = Ext4Subject(fixture);
        var ct = TestContext.Current.CancellationToken;
        using var inspection = new LinuxPermissionService().Inspect(subject, PermissionSections.Ordinary, ct);
        Assert.Null(inspection.Snapshot.Binding);
        Assert.Equal(PermissionReadStatus.Unsupported, inspection.Snapshot.Mode.Status);
        Assert.Equal("Procfs does not represent this process's PID namespace.", inspection.Snapshot.BindingProblem);
        Assert.ThrowsAny<Exception>(() => LinuxPermissionService.Prepare(inspection, item, ModeDraft(0x1a0), false, ct));
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(item.FileSystemPath!));
    }

    // In a new PID and mount namespace with its own procfs, as in a container, the reader binds through that procfs and
    // reviewed mode and access ACL jobs verify.
    [Theory]
    [InlineData("mode")]
    [InlineData("acl")]
    public async Task Ext4_writer_verifies_in_a_pid_namespace_with_its_own_procfs(string change)
    {
        RequireLinuxBoundary("FILECAT_TEST_PERMISSIONS_PROCFS", "own");
        string? parent = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_PARENT_PIDNS");
        Assert.False(string.IsNullOrEmpty(parent));
        Assert.NotEqual(parent, new FileInfo("/proc/self/ns/pid").LinkTarget);
        Assert.Equal(Environment.ProcessId.ToString(CultureInfo.InvariantCulture), new FileInfo("/proc/self").LinkTarget);
        using var fixture = new Fixture(nativeJournal: true);
        var (item, subject) = Ext4Subject(fixture);
        string path = item.FileSystemPath!;
        var ct = TestContext.Current.CancellationToken;
        using var inspection = new LinuxPermissionService().Inspect(subject, PermissionSections.Ordinary, ct);
        Assert.Equal(PermissionReadStatus.Absent, inspection.Snapshot.Access.Status);
        var draft = change == "mode" ? ModeDraft(0x1a0) : new PermissionDraft(PermissionScope.Objects, [new ChangePosixAcl(false,
            [new(PosixAclTag.Owner, 6, null), new(PosixAclTag.NamedUser, 4, new Principal(PrincipalKind.UnixUser, "12345", subject.Authority.Id)),
             new(PosixAclTag.OwningGroup, 0, null), new(PosixAclTag.Mask, 4, null), new(PosixAclTag.Other, 0, null)])]);
        using var review = LinuxPermissionService.Prepare(inspection, item, draft, false, ct).Review;
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(PermissionVerdict.Verified, Assert.Single(job.PermissionResults).Verdict);
        Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(path));
        Assert.Equal(change == "acl", RunAclTool("getfacl", "-ncp", path).Contains("user:12345:r--", StringComparison.Ordinal));
    }

    // Every libc entry point that the Linux permission route, its descriptors and its journal bind to resolves in this
    // libc. The journal type also declares macOS entry points, which are listed and not expected here.
    [Fact]
    public void Linux_permission_route_resolves_every_libc_export()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Requires Linux.");
        string[] macOnly = ["acl_free", "acl_get_entry", "acl_valid", "fclonefileat", "filesec_free", "filesec_get_property",
            "filesec_init", "filesec_query_property", "fstatfs$INODE64", "fstatx_np", "fstatx_np$INODE64", "readdir$INODE64", "renameatx_np"];
        Type[] roots = [typeof(LinuxPermissionService), typeof(LinuxPinnedDescriptors), typeof(LinuxPosixAcl), typeof(UnixAnalysisPrivateDirectory)];
        string[] entries = roots.SelectMany(WithNested)
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Select(method => (method.Name, Import: method.GetCustomAttribute<LibraryImportAttribute>()))
            .Where(pair => pair.Import?.LibraryName == "libc")
            .Select(pair => pair.Import!.EntryPoint ?? pair.Name)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.Contains("statx", entries);
        Assert.Contains("getxattr", entries);
        Assert.Contains("syncfs", entries);
        Assert.Subset(entries.ToHashSet(StringComparer.Ordinal), macOnly.ToHashSet(StringComparer.Ordinal)); // no stale exclusions
        Assert.True(NativeLibrary.TryLoad("libc", typeof(LinuxPermissionService).Assembly, null, out nint libc), "libc did not load.");
        string libcVersion = Marshal.PtrToStringUTF8(GnuLibcVersion()) ?? "unknown";
        string[] linux = entries.Except(macOnly, StringComparer.Ordinal).ToArray();
        string[] missing = linux.Where(name => !NativeLibrary.TryGetExport(libc, name, out _)).ToArray();
        TestContext.Current.TestOutputHelper?.WriteLine($"glibc {libcVersion}: {linux.Length} exports resolved: {string.Join(' ', linux)}");
        Assert.Empty(missing);

        static IEnumerable<Type> WithNested(Type type) => type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(WithNested).Prepend(type);
    }

    [DllImport("libc", EntryPoint = "gnu_get_libc_version")]
    private static extern nint GnuLibcVersion();
}
