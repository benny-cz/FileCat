using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed partial class UnixArchiveStorageTests
{
    private static void Native()
    { if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Native Unix archive storage."); UnixArchiveNative.RequireOrdinaryIdentity(); }
    private static string Canonical(string path) => UnixFiles.RealPath(path) ?? throw new IOException("Owned fixture path unavailable.");
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static UnixArchiveFile Stage(UnixArchiveDirectory directory, long maximum = 4096) => directory.CreateArtifact(Guid.NewGuid(), UnixArchiveArtifactKind.Stage, maximum);
    private static ArchiveObjectVersion Seal(UnixArchiveFile file) => file.Seal(TestContext.Current.CancellationToken);
    private static ArchiveObjectVersion Version(UnixArchiveFile file) => file.CaptureVersion(TestContext.Current.CancellationToken);
    private static ArchiveUnixOuterMetadata Metadata(UnixArchiveFile file) => file.CaptureOuterMetadata(TestContext.Current.CancellationToken);

    [Fact]
    public void Unsupported_platform_cannot_acquire_storage_authority()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Unsupported platform guard.");
        Assert.Throws<PlatformNotSupportedException>(() => UnixArchiveDirectory.Open(Path.GetTempPath()));
    }
    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a\0b")]
    [InlineData("a\nb")]
    [InlineData("\uFFFD")]
    public void Unsafe_single_names_are_refused_before_native_opens(string name) => Assert.Throws<ArgumentException>(() => UnixArchiveNative.Leaf(name));
    [Fact]
    public void Filename_limits_are_strict_UTF8_byte_limits()
    {
        UnixArchiveNative.Leaf(new string('x', 255)); UnixArchiveNative.Leaf(new string('é', 127));
        foreach (string name in new[] { new string('x', 256), new string('é', 128), "\ud800", "\udc00" })
            Assert.Throws<ArgumentException>(() => UnixArchiveNative.Leaf(name));
    }
    [Theory]
    [InlineData("ext4")]
    [InlineData("ext3")]
    [InlineData("ext2")]
    [InlineData("xfs")]
    public void Linux_magic_family_is_not_substituted_for_the_exact_mount_type(string type) =>
        Assert.Equal(type, UnixArchiveVolume.LinuxFileSystem($"42 1 8:1 / /owned\\040path rw,relatime shared:5 - {type} /dev/test rw\n", 42));
    [Theory]
    [InlineData("")]
    [InlineData("42 1 8:1 / / ro - ext4 /dev/test rw\n")]
    [InlineData("42 1 8:1 / / rw - ext4\n")]
    [InlineData("42 1 8:1 / / rw - ext4 /dev/test rw\n42 1 8:1 / / rw - ext4 /dev/test rw\n")]
    [InlineData("43 1 8:1 / / rw - ext4 /dev/test rw\n")]
    [InlineData("bad row\n")]
    public void Missing_readonly_duplicate_or_malformed_mount_evidence_cannot_admit_a_route(string text) =>
        Assert.Equal(ArchiveWriteReason.PublicationUnavailable, Assert.Throws<ArchiveWriteException>(() => UnixArchiveVolume.LinuxFileSystem(text, 42)).Reason);

    [Fact]
    public void Held_volume_and_private_at_creation_artifact_survive_disposal_and_reopen_by_identity()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); using var directory = UnixArchiveDirectory.Open(root);
        Assert.Equal(OperatingSystem.IsMacOS() ? "apfs" : "ext4", directory.Volume.FileSystem);
        using (var again = UnixArchiveDirectory.Open(root)) Assert.Equal(directory.Identity, again.Identity);
        string path; FileIdentity identity; ArchiveObjectVersion version;
        using (var file = Stage(directory))
        {
            path = file.DisplayPath; identity = file.Identity;
            Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(path)); Assert.Equal(UnixArchiveNative.GetEuid(), UnixFiles.Stat(path)!.Value.Owner);
            file.Write("payload"u8); version = Seal(file);
            Assert.Equal(new(identity.ToString(), 7, Hash("payload"u8)), version);
            Assert.Equal((ulong)1, Metadata(file).Native.LinkCount); Assert.False(file.CanWrite); Assert.Throws<NotSupportedException>(() => file.WriteByte(1));
        }
        Assert.Equal("payload", File.ReadAllText(path));
        using var recovery = directory.OpenRecoveryArtifact(Path.GetFileName(path), identity, UnixArchiveArtifactKind.Stage, 4096);
        Assert.Equal(version, Version(recovery));
        Assert.Equal(ArchiveWriteReason.PublicationUnavailable, Assert.Throws<ArchiveWriteException>(() => recovery.DeleteReviewed(version, Metadata(recovery))).Reason);
        Assert.True(File.Exists(path)); // No pathname unlink is disguised as exact-object deletion.
    }
    [Fact]
    public void Bounds_are_checked_before_writes_length_or_position_changes_and_failed_seal_stays_readonly()
    {
        Native(); using var temp = new TempDir(); using var directory = UnixArchiveDirectory.Open(Canonical(temp.Path)); using var file = Stage(directory, 3);
        file.Write([1, 2, 3]);
        foreach (Action action in new Action[] { () => file.WriteByte(4), () => file.SetLength(4), () => file.SetLength(-1), () => file.Position = 4,
                     () => file.Seek(-1, SeekOrigin.Begin), () => file.Seek(long.MaxValue, SeekOrigin.Current) })
        { Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(action).Reason); Assert.Equal(3, file.Length); Assert.Equal(3, file.Position); }
        file.Position = 0; byte[] bytes = new byte[8]; Assert.Equal(3, file.Read(bytes)); Assert.Equal(new byte[] { 1, 2, 3 }, bytes[..3]); Assert.Equal(0, file.Read(bytes));
        using var stop = new CancellationTokenSource(); stop.Cancel(); Assert.Throws<OperationCanceledException>(() => file.Seal(stop.Token));
        Assert.False(file.CanWrite); Assert.Equal(Hash([1, 2, 3]), Version(file).Sha256);
    }
    [Fact]
    public void Bounded_manifest_and_origin_sidecars_roundtrip_through_actual_private_files()
    {
        Native(); using var temp = new TempDir(); using var directory = UnixArchiveDirectory.Open(Canonical(temp.Path));
        using var sidecar = directory.CreateArtifact(Guid.NewGuid(), UnixArchiveArtifactKind.Provenance, 1 << 20);
        var scope = new ArchiveOriginEvidenceScope(Guid.NewGuid(), new(ArchiveWriteFormat.Tar), ArchiveOriginOperation.Create, 1);
        var source = new ArchiveObjectVersion("owned-source", 1, Hash([1])); ArchiveOriginEvidenceInfo info;
        using (var writer = new ArchiveOriginEvidenceWriter(sidecar, scope, TestContext.Current.CancellationToken))
        { writer.Append(new(ArchiveOriginSourceKind.File, source, ArchiveOriginSnapshot.Absent(OperatingSystem.IsMacOS() ? ArchiveOriginFormat.MacQuarantine : ArchiveOriginFormat.LinuxOriginUrl))); info = writer.Complete(); }
        var version = Seal(sidecar); Assert.Equal(info.Length, version.Length); sidecar.Position = 0;
        using var reader = new ArchiveOriginEvidenceReader(sidecar, info, scope, TestContext.Current.CancellationToken);
        Assert.Equal(source, reader.ReadNext()!.Source); Assert.Null(reader.ReadNext()); reader.Complete();
        using var catalog = directory.CreateArtifact(Guid.NewGuid(), UnixArchiveArtifactKind.Manifest, 1 << 20);
        var manifest = new ArchiveWriteManifest(new(ArchiveWriteFormat.Tar), [new("owned", ArchiveMemberType.File, 0, 1, Hash([1]), [])], []);
        Guid nonce = Guid.NewGuid(); var catalogInfo = ArchiveManifestSidecar.Write(catalog, nonce, ArchiveManifestPurpose.ExpectedOutput, manifest, TestContext.Current.CancellationToken);
        var catalogVersion = Seal(catalog); Assert.Equal(catalogInfo.Sha256, catalogVersion.Sha256); catalog.Position = 0;
        using var review = ArchiveManifestSidecar.OpenRead(catalog, catalogInfo, nonce, ArchiveManifestPurpose.ExpectedOutput, manifest.Profile, TestContext.Current.CancellationToken);
        Assert.Equal("owned", review.ReadNext()!.Name); Assert.Null(review.ReadNext()); review.Complete(); Assert.Equal(manifest.Digest, review.VerifiedManifestDigest);
    }
    [Fact]
    public void No_replace_publication_retains_a_late_existing_file_and_then_claims_only_an_absent_name()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); using var directory = UnixArchiveDirectory.Open(root); using var file = Stage(directory);
        file.Write("verified"u8); var version = Seal(file); var metadata = Metadata(file); string old = file.DisplayPath, final = Path.Combine(root, "result.tar");
        File.WriteAllText(final, "late occupant");
        // The no-replace claim changed nothing and says so plainly (a typed refusal, not a raw errno).
        Assert.Equal(ArchiveWriteReason.PublicationUnavailable, Assert.Throws<ArchiveWriteException>(() => file.RenameNoReplace("result.tar", version, metadata, TestContext.Current.CancellationToken)).Reason);
        Assert.Equal(old, file.DisplayPath); Assert.Equal("late occupant", File.ReadAllText(final)); Assert.True(File.Exists(old));
        File.Delete(final); file.RenameNoReplace("result.tar", version, metadata, TestContext.Current.CancellationToken);
        Assert.False(File.Exists(old)); Assert.Equal("verified", File.ReadAllText(final)); Assert.Equal(version, Version(file));
        Assert.Equal(metadata.Origin.Digest, Metadata(file).Origin.Digest);
    }
    [Fact]
    public void A_reopened_exact_private_artifact_can_be_flushed_and_renamed_without_exposing_data_writes()
    {
        Native(); using var temp = new TempDir(); using var directory = UnixArchiveDirectory.Open(Canonical(temp.Path));
        FileIdentity identity; string leaf; ArchiveObjectVersion version;
        using (var created = Stage(directory)) { created.Write("reopened"u8); version = Seal(created); identity = created.Identity; leaf = created.KnownName; }
        using var file = directory.OpenRecoveryArtifact(leaf, identity, UnixArchiveArtifactKind.Stage, 4096);
        Assert.False(file.CanWrite); Assert.Throws<NotSupportedException>(() => file.WriteByte(1));
        file.RenameNoReplace("reopened.tar", version, Metadata(file), TestContext.Current.CancellationToken);
        Assert.Equal(version, Version(file)); Assert.Equal("reopened", File.ReadAllText(file.DisplayPath));
    }
    [Fact]
    public void Stale_content_permissions_or_namespace_prevent_a_rename_without_losing_any_object()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); using var directory = UnixArchiveDirectory.Open(root); using var file = Stage(directory);
        file.Write("stage"u8); var version = Seal(file); var metadata = Metadata(file); string path = file.DisplayPath;
        File.WriteAllText(path, "other"); Assert.Throws<ArchiveWriteException>(() => file.RenameNoReplace("final", version, metadata, TestContext.Current.CancellationToken));
        var current = Version(file); var beforeMode = Metadata(file); File.SetUnixFileMode(path, (UnixFileMode)0x1a4);
        try { Assert.Throws<ArchiveWriteException>(() => file.RenameNoReplace("final", current, beforeMode, TestContext.Current.CancellationToken)); }
        finally { File.SetUnixFileMode(path, (UnixFileMode)0x180); }
        File.Move(path, path + ".held"); File.WriteAllText(path, "substitute");
        Assert.Throws<ArchiveWriteException>(() => file.RenameNoReplace("final", Version(file), Metadata(file), TestContext.Current.CancellationToken));
        Assert.Equal("other", File.ReadAllText(path + ".held")); Assert.Equal("substitute", File.ReadAllText(path)); Assert.False(File.Exists(Path.Combine(root, "final")));
    }
    [Fact]
    public void Parent_owner_disposal_keeps_children_alive_and_a_replaced_parent_name_has_no_publication_authority()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Dir("parent")), moved = root + "-moved";
        var directory = UnixArchiveDirectory.Open(root); using var file = Stage(directory); directory.Dispose(); file.Write("owned"u8); var version = Seal(file); var metadata = Metadata(file);
        Assert.Throws<ObjectDisposedException>(() => Stage(directory)); Directory.Move(root, moved); Directory.CreateDirectory(root);
        Assert.Throws<ArchiveWriteException>(() => file.RenameNoReplace("final", version, metadata, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root)); Assert.Equal("owned", File.ReadAllText(Path.Combine(moved, file.KnownName)));
    }
    [Fact]
    public void Sources_are_nonblocking_regular_held_inodes_with_no_Unix_sharing_lock_claim()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); string path = temp.File("source", "original"); using var directory = UnixArchiveDirectory.Open(root);
        using var source = directory.OpenSource("source", 8); Assert.False(source.CanWrite); Assert.Equal(Hash("original"u8), Version(source).Sha256);
        File.Move(path, path + ".moved"); File.WriteAllText(path, "replacement"); Assert.Equal(Hash("original"u8), Version(source).Sha256);
        File.WriteAllText(path + ".moved", "changed!"); Assert.Equal(Hash("changed!"u8), Version(source).Sha256);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => directory.OpenSource("source", 2)).Reason);
        Directory.CreateDirectory(Path.Combine(root, "folder")); Assert.Throws<ArchiveWriteException>(() => directory.OpenSource("folder"));
        File.CreateSymbolicLink(Path.Combine(root, "link"), path); Assert.Throws<IOException>(() => directory.OpenSource("link"));
        string fifo = Path.Combine(root, "fifo"); Assert.Equal(0, MkFifo(fifo, 0x180)); var time = Stopwatch.StartNew();
        Assert.Throws<ArchiveWriteException>(() => directory.OpenSource("fifo")); Assert.True(time.Elapsed < TimeSpan.FromSeconds(2));
    }
    [Fact]
    public void Root_and_ancestor_symlinks_are_refused_and_private_artifact_aliases_stop_further_writes()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); Directory.CreateSymbolicLink(Path.Combine(root, "alias"), root);
        Assert.Throws<IOException>(() => UnixArchiveDirectory.Open(Path.Combine(root, "alias")));
        Directory.CreateDirectory(Path.Combine(root, "real")); Assert.Throws<IOException>(() => UnixArchiveDirectory.Open(Path.Combine(root, "alias", "real")));
        using var directory = UnixArchiveDirectory.Open(root); using var file = Stage(directory); Assert.Equal(0, Link(file.DisplayPath, Path.Combine(root, "hardlink")));
        Assert.Throws<ArchiveWriteException>(() => file.WriteByte(1)); Assert.Equal(0, file.Length); Assert.True(File.Exists(file.DisplayPath));
    }
    [Fact]
    public void Per_directory_capacity_and_scope_bounds_precede_artifact_creation()
    {
        Native(); using var temp = new TempDir(); using var directory = UnixArchiveDirectory.Open(Canonical(temp.Path));
        Assert.Throws<ArgumentException>(() => directory.CreateArtifact(Guid.Empty, UnixArchiveArtifactKind.Stage, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => directory.CreateArtifact(Guid.NewGuid(), UnixArchiveArtifactKind.Manifest, ArchiveWritePolicy.MaxMetadataBytes + 1));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path)); var leases = new List<UnixArchiveFile>();
        try
        { for (int i = 0; i < 16; i++) leases.Add(Stage(directory)); Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => Stage(directory)).Reason); Assert.Equal(16, Directory.GetFiles(temp.Path).Length); }
        finally { foreach (var lease in leases) lease.Dispose(); }
        using var last = Stage(directory); Assert.Equal(17, Directory.GetFiles(temp.Path).Length);
    }
    [Fact]
    public void Mac_inheritable_allow_ACL_does_not_open_a_private_at_create_permission_window()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("Native Darwin inherited ACL.");
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        try
        {
            Chmod("+a", "everyone allow read,readsecurity,file_inherit,directory_inherit", root);
            string control = Path.Combine(root, "control"); File.WriteAllText(control, "control"); using var controlHeld = File.OpenHandle(control);
            Assert.True(ArchiveUnixOuterMetadata.Capture(controlHeld, TestContext.Current.CancellationToken).AclPresent);
            using var directory = UnixArchiveDirectory.Open(root); using var file = Stage(directory); file.Write("private"u8); Seal(file);
            Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(file.DisplayPath)); // Product calls no post-create chmod/ACL repair.
            using var snapshot = File.OpenHandle(file.DisplayPath); UnixArchiveNative.RequirePrivate(snapshot, UnixArchiveNative.GetEuid());
        }
        finally { Chmod("-RN", root); }
    }
    [Fact]
    public void Linux_default_ACL_is_masked_at_create_and_unknown_procfs_is_not_admitted_as_local_ext4()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Native Linux default ACL/mount.");
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        using var fd = UnixArchiveNative.PinDirectory(root); byte[] acl = new byte[44]; BinaryPrimitives.WriteUInt32LittleEndian(acl, 2);
        var entries = new (ushort Tag, ushort Mode, uint Id)[] { (1, 7, uint.MaxValue), (2, 7, 50001), (4, 7, uint.MaxValue), (16, 7, uint.MaxValue), (32, 7, uint.MaxValue) };
        for (int i = 0; i < entries.Length; i++) { var span = acl.AsSpan(4 + 8 * i); BinaryPrimitives.WriteUInt16LittleEndian(span, entries[i].Tag); BinaryPrimitives.WriteUInt16LittleEndian(span[2..], entries[i].Mode); BinaryPrimitives.WriteUInt32LittleEndian(span[4..], entries[i].Id); }
        Assert.Equal(0, SetAcl(UnixArchiveNative.Fd(fd), "system.posix_acl_default", acl, acl.Length, 0));
        using var directory = UnixArchiveDirectory.Open(root); using var file = Stage(directory); Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(file.DisplayPath));
        file.Write("private"u8); Seal(file); Assert.True(Metadata(file).AclPresent);
        Assert.Throws<ArchiveWriteException>(() => UnixArchiveDirectory.Open("/proc"));
    }
    private static void Chmod(params string[] args)
    {
        var start = new ProcessStartInfo("/bin/chmod") { UseShellExecute = false, RedirectStandardError = true }; foreach (string arg in args) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!; if (!child.WaitForExit(10_000)) { child.Kill(); child.WaitForExit(); Assert.Fail("Owned chmod fixture timed out."); }
        Assert.True(child.ExitCode == 0, child.StandardError.ReadToEnd());
    }
    [LibraryImport("libc", EntryPoint = "mkfifo", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MkFifo(string path, uint mode);
    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Link(string source, string destination);
    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int SetAcl(int fd, string name, byte[] value, nint length, int flags);
}
