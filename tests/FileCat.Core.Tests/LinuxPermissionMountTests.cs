using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class LinuxPermissionMountTests
{
    [Fact]
    public void The_mount_model_preserves_server_scope_without_displaying_connection_secrets()
    {
        var cifs = LinuxPermissionService.ObserveMountProjection("cifs", "//test/share", "/folder",
            "rw,vers=3.1.1,noperm,username=benny,password=secret");
        Assert.NotNull(cifs);
        Assert.Equal(PermissionMountPresentation.Translated, cifs.Presentation);
        Assert.True(cifs.IsServerProjection);
        Assert.Equal("//test/share", cifs.Source);
        Assert.Equal("/folder", cifs.Root);
        Assert.Equal("SMB 3.1.1", cifs.Protocol);
        Assert.Contains("server still checks access", cifs.ClientPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", cifs.ToString(), StringComparison.Ordinal);
        var privateShare = LinuxPermissionService.ObserveMountProjection("smb3", "//benny:secret@test/share", "/", "rw");
        Assert.Null(privateShare!.Source);

        var nfs = LinuxPermissionService.ObserveMountProjection("nfs4", "[2001:db8::1]:/export", "/", "rw,vers=4.2,sec=krb5p");
        Assert.Equal("NFS 4.2", nfs!.Protocol);
        Assert.Equal("[2001:db8::1]:/export", nfs.Source);
        Assert.True(nfs.IsServerProjection);
        Assert.Contains("Kerberos authentication and privacy protection", nfs.ClientPolicy, StringComparison.Ordinal);
        Assert.Contains("NFSv4 ACL", nfs.ChangeRefusal, StringComparison.Ordinal);
        var nfs3 = LinuxPermissionService.ObserveMountProjection("nfs", "nas:/export", "/", "ro,vers=3,sec=sys");
        Assert.Equal("NFS 3", nfs3!.Protocol);
        Assert.Contains("AUTH_SYS", nfs3.ClientPolicy, StringComparison.Ordinal);
        var unknownNfs = LinuxPermissionService.ObserveMountProjection("nfs", "nas:/export", "/", "ro,vers=4..1,sec=token");
        Assert.Equal("NFS", unknownNfs!.Protocol);
        Assert.DoesNotContain("token", unknownNfs.ToString(), StringComparison.Ordinal);
        var gvfs = LinuxPermissionService.ObserveMountProjection("fuse.gvfsd-fuse", "gvfsd-fuse", "/", "rw");
        Assert.Null(gvfs!.Source);
        Assert.True(gvfs.IsServerProjection);
        var share = LinuxPermissionService.ProjectionForPath(gvfs, "fuse.gvfsd-fuse", "/runtime/gvfs",
            "/runtime/gvfs/smb-share:server=nas,share=Public%20files,user=benny/witness");
        Assert.Equal("//nas/Public files", share.Source);
        Assert.Equal("SMB via GVfs", share.Protocol);
        var alias = LinuxPermissionService.ProjectionForPath(gvfs with { Root = "/smb-share:server=nas,share=Public%20files,user=benny" },
            "fuse.gvfsd-fuse", "/alias", "/alias/witness");
        Assert.Equal(share.Source, alias.Source);
        Assert.Equal(share.ConnectionKey, alias.ConnectionKey);
        var otherAccount = LinuxPermissionService.ProjectionForPath(gvfs, "fuse.gvfsd-fuse", "/runtime/gvfs",
            "/runtime/gvfs/smb-share:server=nas,share=Public%20files,user=other/witness");
        Assert.Equal(share.Source, otherAccount.Source);
        Assert.NotEqual(share.ConnectionKey, otherAccount.ConnectionKey);
        Assert.DoesNotContain("benny", share.ConnectionKey, StringComparison.Ordinal);
        Assert.Null(LinuxPermissionService.ProjectionForPath(gvfs, "fuse.gvfsd-fuse", "/runtime/gvfs",
            "/runtime/gvfs/smb-share:server=account%40nas,share=public").Source);
        var zfs = LinuxPermissionService.ObserveMountProjection("zfs", "pool/dataset", "/", "rw");
        Assert.Equal(PermissionMountPresentation.Unidentified, zfs!.Presentation);
        Assert.Contains("ACL type", zfs.ClientPolicy, StringComparison.Ordinal);
        var conflictingZfs = LinuxPermissionService.ObserveMountProjection("zfs", "pool/dataset", "/", "rw,posixacl,noacl");
        Assert.Equal(PermissionMountPresentation.Unidentified, conflictingZfs!.Presentation);
        Assert.Null(LinuxPermissionService.ObserveMountProjection("autofs", "systemd", "/", "rw"));
        Assert.Null(LinuxPermissionService.ObserveMountProjection("ext4", "/dev/root", "/", "rw"));
    }

    [Theory]
    [InlineData("fat", "vfat", false)]
    [InlineData("fat-ro", "vfat", true)]
    [InlineData("exfat", "exfat", false)]
    [InlineData("fuse", "fuse", false)]
    [InlineData("smb", "cifs", true)]
    [InlineData("gvfs", "fuse.gvfsd-fuse", false)]
    [InlineData("nfs", "nfs4", true)]
    [InlineData("zfs-posix", "zfs", false)]
    [InlineData("zfs-off", "zfs", false)]
    [InlineData("zfs-nfsv4", "zfs", false)]
    public void A_real_mount_reports_observed_metadata_and_never_admits_the_projected_object_to_a_writer(
        string fixture, string fileSystem, bool readOnly)
    {
        string? fixtureRoot = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_MOUNT_ROOT");
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(fixtureRoot) || Environment.IsPrivilegedProcess)
            Assert.Skip("Requires disposable native mounts and an ordinary Linux process.");
        string? folder = fixture == "gvfs" ? Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_GVFS_ROOT") :
            Path.Combine(fixtureRoot, fixture);
        if (string.IsNullOrEmpty(folder)) Assert.Skip("This native mount fixture is unavailable.");
        string path = Path.Combine(folder, "witness.txt");
        if (!File.Exists(path)) Assert.Skip("This native mount fixture is unavailable.");
        var source = ItemRef.ForFileSystemPath(path, EntryKind.File);
        var subject = new PermissionSubject(source.Parent, path, path, new(PermissionAuthorityKind.Local, "local", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        var service = new LinuxPermissionService(qualifyAccessChecks: true, qualifyMountProjections: true);
        var cancellation = TestContext.Current.CancellationToken;
        using var inspection = service.Inspect(subject, PermissionSections.Ordinary, cancellation);
        var snapshot = inspection.Snapshot;
        Assert.Equal(PermissionModel.Projected, snapshot.Model);
        Assert.NotNull(snapshot.Binding);
        Assert.Equal(fileSystem, snapshot.Binding.FileSystem);
        Assert.Equal(readOnly, snapshot.Binding.ReadOnly);
        Assert.Equal(fixture == "gvfs" ? Path.GetDirectoryName(folder) : folder, snapshot.Binding.MountRoot);
        Assert.Equal(PermissionReadStatus.Present, snapshot.Owner.Status);
        Assert.Equal(PermissionReadStatus.Present, snapshot.Group.Status);
        Assert.Equal(PermissionReadStatus.Present, snapshot.Mode.Status);
        Assert.Equal(snapshot.Mode.ObservedAt, snapshot.Subject.LastMountObservation);
        Assert.Equal((ushort)((uint)File.GetUnixFileMode(path) & 0xfff), snapshot.Mode.Value);
        Assert.Equal(PermissionReadStatus.Unsupported, snapshot.Access.Status);
        Assert.NotNull(snapshot.Mount);
        Assert.Equal(fixture is "smb" or "gvfs" or "nfs" ? PermissionAuthorityKind.MountedServer : PermissionAuthorityKind.Local, snapshot.Subject.Authority.Kind);
        Assert.Equal(snapshot.Subject.Authority, snapshot.Binding.Identity.Authority);
        if (snapshot.Subject.Authority.Kind == PermissionAuthorityKind.MountedServer)
        {
            Assert.Contains("client cache allowed", snapshot.Mode.Source, StringComparison.Ordinal);
            Assert.Equal(fixture == "nfs" ? "127.0.0.1:/permissions" : "//127.0.0.1/permissions", snapshot.Mount.Source);
            using var inspectedAgain = service.Inspect(snapshot.Subject with { ExpectedIdentity = snapshot.Binding.Identity }, PermissionSections.Mode, cancellation);
            Assert.Equal(snapshot.Binding.Identity, inspectedAgain.Snapshot.Binding?.Identity);
        }
        if (fileSystem == "cifs")
        {
            Assert.Equal("SMB 3.0", snapshot.Mount.Protocol);
            Assert.Contains("server still checks access", snapshot.Mount.ClientPolicy, StringComparison.Ordinal);
        }
        if (fixture == "gvfs") Assert.Equal("SMB via GVfs", snapshot.Mount.Protocol);
        if (fixture == "nfs")
        {
            Assert.Equal("NFS 4.1", snapshot.Mount.Protocol);
            Assert.Contains("AUTH_SYS", snapshot.Mount.ClientPolicy, StringComparison.Ordinal);
            Assert.Contains("NFSv4 ACL", snapshot.Mount.ChangeRefusal, StringComparison.Ordinal);
        }
        if (fileSystem == "zfs")
        {
            Assert.Equal(PermissionMountPresentation.Native, snapshot.Mount.Presentation);
            Assert.EndsWith("/" + fixture[4..], snapshot.Mount.Source, StringComparison.Ordinal);
            Assert.Contains(fixture == "zfs-posix" ? "POSIX ACLs" : "ACLs disabled", snapshot.Mount.ClientPolicy, StringComparison.Ordinal);
            Assert.Contains("inheritance policies are unverified", snapshot.Mount.ClientPolicy, StringComparison.Ordinal);
        }
        Assert.False(service.CanCheckAccess(snapshot));
        Assert.False(service.CanEditAccessAcl(snapshot));
        Assert.NotNull(LinuxPermissionService.ModeReviewRefusal(snapshot));
        var unchanged = File.GetUnixFileMode(path);
        Assert.Throws<NotSupportedException>(() => LinuxPermissionService.Prepare(inspection, source,
            new(PermissionScope.Objects, [new ChangePermissionMode(0x180, 0xfff)]), acceptSetIdClear: false, cancellation));
        Assert.Equal(unchanged, File.GetUnixFileMode(path));
    }

    [Fact]
    public async Task A_removed_medium_preserves_the_observation_date_and_cannot_rebind_to_an_alias()
    {
        string? controls = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_MEDIA_CONTROL");
        string? mounts = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_MOUNT_ROOT");
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(controls) || string.IsNullOrEmpty(mounts) || Environment.IsPrivilegedProcess)
            Assert.Skip("Requires a disposable removable medium with a mount controller.");

        string folder = Path.Combine(mounts, "fat-ro");
        string path = Path.Combine(folder, "witness.txt");
        var subject = new PermissionSubject(Location.FileSystem(folder), path, path, new(PermissionAuthorityKind.Local, "local", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        var service = new LinuxPermissionService(qualifyMountProjections: true);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var cancellation = timeout.Token;

        using var initial = service.Inspect(subject, PermissionSections.Ordinary, cancellation);
        var binding = Assert.IsType<PermissionBinding>(initial.Snapshot.Binding);
        Assert.Equal("vfat", binding.FileSystem);
        Assert.True(binding.ReadOnly);
        var captured = initial.Snapshot.Subject with { ExpectedIdentity = binding.Identity };
        Assert.Equal(initial.Snapshot.Mode.ObservedAt, captured.LastMountObservation);

        await AdvanceAsync("captured", "removed");
        AssertUnavailable(PermissionReadStatus.Gone);
        await AdvanceAsync("absence-observed", "substitute");
        AssertUnavailable(PermissionReadStatus.Stale);
        await AdvanceAsync("substitute-observed", "reattached");
        AssertUnavailable(PermissionReadStatus.Stale);
        using var replacement = service.Inspect(subject, PermissionSections.Mode, cancellation);
        Assert.Equal(PermissionReadStatus.Present, replacement.Snapshot.Mode.Status);
        Assert.NotEqual(binding.Identity.MountInstance, replacement.Snapshot.Binding?.Identity.MountInstance);

        async Task AdvanceAsync(string signal, string ready)
        {
            File.WriteAllText(Path.Combine(controls, signal), "");
            while (!File.Exists(Path.Combine(controls, ready))) await Task.Delay(50, cancellation);
        }

        void AssertUnavailable(PermissionReadStatus expected)
        {
            using var observed = service.Inspect(captured, PermissionSections.Mode, cancellation);
            Assert.Null(observed.Snapshot.Binding);
            Assert.True(expected == observed.Snapshot.Mode.Status, observed.Snapshot.BindingProblem);
            Assert.Equal(captured.LastMountObservation, observed.Snapshot.Subject.LastMountObservation);
            Assert.NotNull(LinuxPermissionService.ModeReviewRefusal(observed.Snapshot));
        }
    }

    [Fact]
    public async Task A_disconnected_Nfs_client_retains_the_observation_date_when_paths_are_unavailable()
    {
        string? controls = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_NFS_OUTAGE_CONTROL");
        string? mounts = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_MOUNT_ROOT");
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(controls) || string.IsNullOrEmpty(mounts) || Environment.IsPrivilegedProcess)
            Assert.Skip("Requires a disposable NFS export with an outage controller.");

        string folder = Path.Combine(mounts, "nfs");
        PermissionSubject Subject(string name)
        {
            string path = Path.Combine(folder, name);
            return new(Location.FileSystem(folder), path, path, new(PermissionAuthorityKind.Local, "local", 0),
                PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        }

        var service = new LinuxPermissionService(qualifyMountProjections: true);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var cancellation = timeout.Token;
        using var live = service.Inspect(Subject("witness.txt"), PermissionSections.Ordinary, cancellation);
        Assert.Equal(PermissionReadStatus.Present, live.Snapshot.Mode.Status);
        var binding = Assert.IsType<PermissionBinding>(live.Snapshot.Binding);
        var captured = live.Snapshot.Subject with { ExpectedIdentity = binding.Identity };
        File.WriteAllText(Path.Combine(controls, "ready"), "");
        while (!File.Exists(Path.Combine(controls, "disconnected"))) await Task.Delay(50, cancellation);

        using var observed = service.Inspect(captured, PermissionSections.Mode, cancellation);
        Assert.Null(observed.Snapshot.Binding);
        Assert.True(observed.Snapshot.Mode.Status == PermissionReadStatus.Failed, observed.Snapshot.BindingProblem);
        Assert.Contains("mounted client could not retrieve permission metadata", observed.Snapshot.BindingProblem, StringComparison.Ordinal);
        Assert.Equal(live.Snapshot.Mode.ObservedAt, observed.Snapshot.Subject.LastMountObservation);
        Assert.False(service.CanEditAccessAcl(observed.Snapshot));

        using var cold = service.Inspect(Subject("cold.txt"), PermissionSections.Mode, cancellation);
        Assert.Null(cold.Snapshot.Binding);
        Assert.Equal(PermissionReadStatus.Failed, cold.Snapshot.Mode.Status);
        Assert.Equal(5, cold.Snapshot.Mode.NativeError);
        Assert.Contains("mounted client could not retrieve permission metadata", cold.Snapshot.BindingProblem, StringComparison.Ordinal);
        Assert.Null(cold.Snapshot.Subject.LastMountObservation);
    }

    // G-MOUNT disconnects on the other claimed families: a kernel CIFS share whose server stops, a bindfs FUSE mount whose
    // daemon ends and a GVfs SMB share whose FUSE daemon ends. The harness reports the disconnect once an independent read
    // fails. A subject bound while the mount answered is then unavailable with the client's error, keeps its observation
    // date and admits no review.
    [Theory]
    [InlineData("smb")]
    [InlineData("fuse")]
    [InlineData("gvfs")]
    public async Task A_disconnected_mount_reports_the_client_error_and_keeps_the_observation_date(string fixture)
    {
        string? controls = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_DISCONNECT_CONTROL");
        string? mounts = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_MOUNT_ROOT");
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(controls) || string.IsNullOrEmpty(mounts) || Environment.IsPrivilegedProcess)
            Assert.Skip("Requires disposable mounts with a disconnect controller.");
        string? folder = fixture == "gvfs" ? Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_GVFS_ROOT") : Path.Combine(mounts, fixture);
        if (string.IsNullOrEmpty(folder) || !File.Exists(Path.Combine(folder, "witness.txt")))
            Assert.Skip("This native mount fixture is unavailable.");
        string path = Path.Combine(folder, "witness.txt");
        var subject = new PermissionSubject(Location.FileSystem(folder), path, path, new(PermissionAuthorityKind.Local, "local", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        var service = new LinuxPermissionService(qualifyMountProjections: true);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(150));
        var cancellation = timeout.Token;

        using var live = service.Inspect(subject, PermissionSections.Ordinary, cancellation);
        Assert.Equal(PermissionReadStatus.Present, live.Snapshot.Mode.Status);
        var binding = Assert.IsType<PermissionBinding>(live.Snapshot.Binding);
        var captured = live.Snapshot.Subject with { ExpectedIdentity = binding.Identity };
        File.WriteAllText(Path.Combine(controls, fixture + ".disconnect"), "");
        while (!File.Exists(Path.Combine(controls, fixture + ".disconnected"))) await Task.Delay(50, cancellation);

        using var observed = service.Inspect(captured, PermissionSections.Mode, cancellation);
        var mode = observed.Snapshot.Mode;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{fixture} ({binding.FileSystem}): {mode.Status} errno {mode.NativeError}: {observed.Snapshot.BindingProblem}");
        Assert.Null(observed.Snapshot.Binding);
        Assert.Equal(PermissionReadStatus.Failed, mode.Status);
        Assert.Contains(mode.NativeError, new int?[] { 5, 103, 104, 107, 110, 111, 112, 113 });
        if (live.Snapshot.Subject.Authority.Kind == PermissionAuthorityKind.MountedServer)
            Assert.Contains("mounted client could not retrieve permission metadata", observed.Snapshot.BindingProblem, StringComparison.Ordinal);
        Assert.Equal(live.Snapshot.Mode.ObservedAt, observed.Snapshot.Subject.LastMountObservation);
        Assert.NotNull(LinuxPermissionService.ModeReviewRefusal(observed.Snapshot));
    }

    [Fact]
    public async Task A_removed_and_remounted_share_does_not_reuse_the_old_permission_observation()
    {
        string? controls = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_SMB_REMOUNT");
        string? mounts = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_MOUNT_ROOT");
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(controls) || string.IsNullOrEmpty(mounts) || Environment.IsPrivilegedProcess)
            Assert.Skip("Requires a disposable CIFS share and its mount fixture.");
        var folder = Path.Combine(mounts, "smb");
        string path = Path.Combine(folder, "witness.txt");
        var subject = new PermissionSubject(Location.FileSystem(folder), path, path, new(PermissionAuthorityKind.Local, "local", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        var service = new LinuxPermissionService(qualifyMountProjections: true);
        var cancellation = TestContext.Current.CancellationToken;
        using var initial = service.Inspect(subject, PermissionSections.Ordinary, cancellation);
        var original = Assert.IsType<PermissionBinding>(initial.Snapshot.Binding);
        var captured = initial.Snapshot.Subject with { ExpectedIdentity = original.Identity };

        File.WriteAllText(Path.Combine(controls, "captured"), "");
        await WaitForAsync("detached");
        using var detached = service.Inspect(captured, PermissionSections.Mode, cancellation);
        Assert.Null(detached.Snapshot.Binding);
        Assert.Equal(PermissionReadStatus.Gone, detached.Snapshot.Mode.Status);
        Assert.Equal(initial.Snapshot.Mode.ObservedAt, detached.Snapshot.Subject.LastMountObservation);
        File.WriteAllText(Path.Combine(controls, "absence-observed"), "");
        await WaitForAsync("remounted");

        using var stale = service.Inspect(captured, PermissionSections.Mode, cancellation);
        Assert.Null(stale.Snapshot.Binding);
        Assert.Equal(PermissionReadStatus.Stale, stale.Snapshot.Mode.Status);
        using var current = service.Inspect(subject, PermissionSections.Ordinary, cancellation);
        var replacement = Assert.IsType<PermissionBinding>(current.Snapshot.Binding);
        Assert.Equal(initial.Snapshot.Mount!.Source, current.Snapshot.Mount!.Source);
        Assert.NotEqual(original.Identity.MountInstance, replacement.Identity.MountInstance);

        async Task WaitForAsync(string filename)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(controls, filename)) && timeout.Elapsed < TimeSpan.FromSeconds(20))
                await Task.Delay(50, cancellation);
            Assert.True(File.Exists(Path.Combine(controls, filename)), $"The fixture did not report {filename}.");
        }
    }
}
