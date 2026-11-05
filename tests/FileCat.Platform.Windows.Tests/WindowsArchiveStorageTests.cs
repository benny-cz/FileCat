using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
{
    private static void RequireWindows()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows native archive storage required.");
        Assert.SkipWhen(WindowsTokenAuthority.IsHighIntegrity, "Run native archive storage tests from an unelevated process.");
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("filecat-archive-storage-").FullName;
        public string PathOf(string leaf) => Path.Combine(Root, leaf);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] Bytes(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bytes = new MemoryStream(); file.CopyTo(bytes); return bytes.ToArray();
    }
    private static WindowsArchiveFile Artifact(WindowsArchiveDirectory root, long limit = 4096) =>
        root.CreateArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Stage, limit);

    [Fact]
    public void Unsupported_platform_or_elevation_has_no_storage_authority()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() && !WindowsTokenAuthority.IsHighIntegrity,
            "This case covers the unsupported-platform and elevated refusal lanes.");
        using var fixture = new Fixture();
        if (!OperatingSystem.IsWindows()) Assert.Throws<PlatformNotSupportedException>(() => WindowsArchiveDirectory.Open(fixture.Root));
        else Assert.Throws<UnauthorizedAccessException>(() => WindowsArchiveDirectory.Open(fixture.Root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Root));
    }

    [Fact]
    public void Held_volume_is_observed_and_artifact_is_private_at_creation_and_never_deleted_on_dispose()
    {
        RequireWindows(); using var fixture = new Fixture();
        using var root = WindowsArchiveDirectory.Open(fixture.Root);
        Assert.Equal("NTFS", root.Volume.FileSystem); Assert.Equal(7u, root.Volume.DeviceType);
        Assert.Equal(0u, root.Volume.DeviceCharacteristics & 0x10);
        using (var sameDirectory = WindowsArchiveDirectory.Open(fixture.Root)) Assert.Equal(sameDirectory.Identity, root.Identity);
        string path; FileIdentity identity;
        using (var file = Artifact(root))
        {
            path = file.DisplayPath; identity = file.Identity;
            var security = new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            Assert.True(security.AreAccessRulesProtected);
            using var caller = WindowsIdentity.GetCurrent();
            Assert.Equal(caller.User, security.GetOwner(typeof(SecurityIdentifier)));
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
            Assert.Equal(2, rules.Length);
            Assert.All(rules, rule =>
            {
                Assert.False(rule.IsInherited); Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
                Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
                Assert.Contains(rule.IdentityReference.Value, new[] { caller.User!.Value, "S-1-5-18" });
            });
            Assert.Equal(1u, file.LinkCount); Assert.Equal(0, file.Length);
            file.Write("payload"u8); var version = file.Seal(TestContext.Current.CancellationToken);
            Assert.Equal(new(identity.ToString(), 7, Digest("payload"u8)), version);
            Assert.False(file.CanWrite); Assert.Throws<NotSupportedException>(() => file.WriteByte(1));
        }
        Assert.True(File.Exists(path)); Assert.Equal("payload"u8.ToArray(), File.ReadAllBytes(path));
        using var recovery = root.OpenRecoveryArtifact(Path.GetFileName(path), identity, WindowsArchiveArtifactKind.Stage, 4096);
        recovery.DeleteReviewed(recovery.CaptureVersion(TestContext.Current.CancellationToken), recovery.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Boundaries_refuse_before_any_write_length_or_position_change_and_failed_seal_stays_read_only()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        using var file = Artifact(root, 3);
        file.Write([1, 2, 3]);
        foreach (Action forbidden in new Action[] { () => file.WriteByte(4), () => file.SetLength(4),
                     () => file.SetLength(-1), () => file.Position = 4, () => file.Seek(long.MaxValue, SeekOrigin.Current),
                     () => file.Seek(-1, SeekOrigin.Begin) })
        {
            Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(forbidden).Reason);
            Assert.Equal(3, file.Length); Assert.Equal(3, file.Position);
        }
        file.Position = 0; byte[] read = new byte[9]; Assert.Equal(3, file.Read(read)); Assert.Equal(new byte[] { 1, 2, 3 }, read[..3]);
        Assert.Equal(0, file.Read(read));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => file.Seal(canceled.Token));
        Assert.False(file.CanWrite); Assert.Throws<NotSupportedException>(() => file.SetLength(1));
        Assert.Equal(Digest([1, 2, 3]), file.CaptureVersion(TestContext.Current.CancellationToken).Sha256);
    }

    [Fact]
    public void Source_open_excludes_existing_and_future_writers_and_refuses_links_directories_and_large_files()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        string path = fixture.PathOf("source.bin"); File.WriteAllBytes(path, [1, 2, 3]);
        using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            Assert.ThrowsAny<IOException>(() => root.OpenSource("source.bin"));
        using (var source = root.OpenSource("source.bin", 3))
        {
            Assert.ThrowsAny<IOException>(() => File.OpenWrite(path).Dispose());
            Assert.ThrowsAny<IOException>(() => File.Move(path, fixture.PathOf("moved.bin")));
            Assert.ThrowsAny<IOException>(() => File.Delete(path));
            Assert.Equal(Digest([1, 2, 3]), source.CaptureVersion(TestContext.Current.CancellationToken).Sha256);
            Assert.Throws<InvalidOperationException>(() => source.DeleteReviewed(source.CaptureVersion(TestContext.Current.CancellationToken), source.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
        }
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => root.OpenSource("source.bin", 2)).Reason);
        Directory.CreateDirectory(fixture.PathOf("folder")); Assert.ThrowsAny<Exception>(() => root.OpenSource("folder"));
        Directory.CreateSymbolicLink(fixture.PathOf("redirect"), fixture.Root);
        Assert.ThrowsAny<IOException>(() => WindowsArchiveDirectory.Open(fixture.PathOf("redirect")));
        File.CreateSymbolicLink(fixture.PathOf("link.bin"), path);
        Assert.ThrowsAny<IOException>(() => root.OpenSource("link.bin"));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void Reviewed_target_never_bypasses_read_only_and_reports_hard_links_without_modifying_the_other_name()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        string path = fixture.PathOf("original.bin"); File.WriteAllBytes(path, [4, 5, 6]);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.ThrowsAny<UnauthorizedAccessException>(() => root.OpenReviewedTarget("original.bin"));
            using var input = root.OpenSource("original.bin"); Assert.Equal(3, input.Length);
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        Assert.True(CreateHardLink(fixture.PathOf("alias.bin"), path, 0));
        using var original = root.OpenReviewedTarget("original.bin");
        Assert.Equal(2u, original.LinkCount);
        var version = original.CaptureVersion(TestContext.Current.CancellationToken);
        original.RenameNoReplace("retained.bin", version, original.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.False(File.Exists(path)); Assert.True(File.Exists(fixture.PathOf("alias.bin")));
        File.WriteAllBytes(path, [9]);
        original.DeleteReviewed(version, original.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.False(File.Exists(fixture.PathOf("retained.bin")));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(path));
        Assert.Equal(new byte[] { 4, 5, 6 }, Bytes(fixture.PathOf("alias.bin")));
    }

    [Fact]
    public void Late_file_directory_and_link_occupants_survive_no_replace_and_stage_remains_owned()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        using var file = Artifact(root); file.Write([1, 2, 3]); var version = file.Seal(TestContext.Current.CancellationToken);
        string original = file.DisplayPath;
        File.WriteAllBytes(fixture.PathOf("late.bin"), [9]);
        Directory.CreateDirectory(fixture.PathOf("late-directory"));
        File.CreateSymbolicLink(fixture.PathOf("late-link"), fixture.PathOf("late.bin"));
        foreach (string occupant in new[] { "late.bin", "late-directory", "late-link" })
        {
            Assert.ThrowsAny<Exception>(() => file.RenameNoReplace(occupant, version, file.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
            Assert.Equal(original, file.DisplayPath); Assert.Equal(version, file.CaptureVersion(TestContext.Current.CancellationToken));
        }
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(fixture.PathOf("late.bin")));
        Assert.True(Directory.Exists(fixture.PathOf("late-directory")));
        Assert.NotNull(new FileInfo(fixture.PathOf("late-link")).LinkTarget);
        file.RenameNoReplace("published.tar", version, file.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.False(File.Exists(original)); Assert.Equal(version, file.CaptureVersion(TestContext.Current.CancellationToken));
        // Handle-bound cleanup must affect this object alone, even with a new unrelated occupant at the old name.
        File.WriteAllBytes(original, [8]); file.DeleteReviewed(version, file.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(original));
        Assert.False(File.Exists(fixture.PathOf("published.tar")));
    }

    [Fact]
    public void Parent_disposal_defers_handle_release_until_children_finish_and_never_deletes_them()
    {
        RequireWindows(); using var fixture = new Fixture(); var root = WindowsArchiveDirectory.Open(fixture.Root);
        var file = Artifact(root); file.Write([1]); var version = file.Seal(TestContext.Current.CancellationToken);
        root.Dispose(); root.Dispose(); Assert.Throws<ObjectDisposedException>(() => Artifact(root));
        Assert.ThrowsAny<IOException>(() => Directory.Move(fixture.Root, fixture.Root + "-moved"));
        Assert.Equal(version, file.CaptureVersion(TestContext.Current.CancellationToken)); file.RenameNoReplace("retained.stage", version, file.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        file.Dispose(); file.Dispose(); Assert.False(file.CanRead); Assert.False(file.CanSeek); Assert.False(file.CanWrite);
        Assert.Throws<ObjectDisposedException>(() => file.ReadByte());
        Assert.True(File.Exists(fixture.PathOf("retained.stage")));
        string moved = fixture.Root + "-moved"; Directory.Move(fixture.Root, moved); Directory.Move(moved, fixture.Root);
    }

    [Fact]
    public void Stale_identity_bytes_length_and_cancellation_never_authorize_rename_or_delete()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        using var file = Artifact(root); file.Write([1, 2, 3]); var version = file.Seal(TestContext.Current.CancellationToken); string path = file.DisplayPath;
        foreach (var stale in new[] { new ArchiveObjectVersion("another-object", 3, version.Sha256),
                     new ArchiveObjectVersion(version.Identity, 2, version.Sha256), new ArchiveObjectVersion(version.Identity, 3, Digest([3, 2, 1])) })
        {
            Assert.ThrowsAny<IOException>(() => file.DeleteReviewed(stale, file.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
            Assert.ThrowsAny<IOException>(() => file.RenameNoReplace("unwanted.bin", stale, file.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
            Assert.Equal(path, file.DisplayPath); Assert.Equal(version, file.CaptureVersion(TestContext.Current.CancellationToken));
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => file.RenameNoReplace("unwanted.bin", version, file.CaptureOuterMetadata(TestContext.Current.CancellationToken), canceled.Token));
        Assert.Throws<OperationCanceledException>(() => file.DeleteReviewed(version, file.CaptureOuterMetadata(TestContext.Current.CancellationToken), canceled.Token));
        Assert.True(File.Exists(path)); Assert.False(File.Exists(fixture.PathOf("unwanted.bin")));
    }

    [Fact]
    public void Recovery_refuses_same_bytes_at_a_different_identity_and_permissive_or_changed_permissions()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        FileIdentity old; string path;
        using (var file = Artifact(root)) { file.Write([1, 2, 3]); file.Seal(TestContext.Current.CancellationToken); old = file.Identity; path = file.DisplayPath; }
        File.Move(path, fixture.PathOf("real-original.stage")); File.WriteAllBytes(path, [1, 2, 3]);
        Assert.ThrowsAny<IOException>(() => root.OpenRecoveryArtifact(Path.GetFileName(path), old, WindowsArchiveArtifactKind.Stage, 4096));
        var substitute = ProtectedHexFile.PathIdentity(path)!.Value;
        Assert.Throws<UnauthorizedAccessException>(() => root.OpenRecoveryArtifact(Path.GetFileName(path), substitute, WindowsArchiveArtifactKind.Stage, 4096));
        string exact = fixture.PathOf("real-original.stage");
        var changed = new FileInfo(exact).GetAccessControl();
        changed.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(exact).SetAccessControl(changed);
        Assert.Throws<UnauthorizedAccessException>(() => root.OpenRecoveryArtifact("real-original.stage", old, WindowsArchiveArtifactKind.Stage, 4096));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(exact)); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void Live_permission_and_read_only_changes_refuse_mutation_and_retain_the_held_artifact()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        using var file = Artifact(root); file.Write([1, 2, 3]); var version = file.Seal(TestContext.Current.CancellationToken);
        string path = file.DisplayPath; var reviewedMetadata = file.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        var originalSecurity = new FileInfo(path).GetAccessControl();
        var changed = new FileInfo(path).GetAccessControl();
        changed.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(changed);
        Assert.Throws<UnauthorizedAccessException>(() => file.RenameNoReplace("unwanted.bin", version, reviewedMetadata, TestContext.Current.CancellationToken));
        Assert.Throws<UnauthorizedAccessException>(() => file.DeleteReviewed(version, reviewedMetadata, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(path));
        originalSecurity.SetSecurityDescriptorBinaryForm(originalSecurity.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
        new FileInfo(path).SetAccessControl(originalSecurity);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => file.DeleteReviewed(version, reviewedMetadata, TestContext.Current.CancellationToken));
            Assert.True(File.Exists(path));
        }
        finally { File.SetAttributes(path, (FileAttributes)reviewedMetadata.Attributes); }
        Assert.Equal(version, file.CaptureVersion(TestContext.Current.CancellationToken));
        // A deliberate ACL edit can change native descriptor state even when its visible ACEs are restored. Review
        // that intentional edit afresh; the native rename must still independently require the private DACL.
        var afterRestore = file.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        Assert.Equal(reviewedMetadata.CreationTime, afterRestore.CreationTime);
        Assert.Equal(reviewedMetadata.LastWriteTime, afterRestore.LastWriteTime);
        Assert.Equal(reviewedMetadata.Attributes, afterRestore.Attributes);
        Assert.Equal(reviewedMetadata.LinkCount, afterRestore.LinkCount);
        Assert.Equal(reviewedMetadata.OwnerSid, afterRestore.OwnerSid); Assert.Equal(reviewedMetadata.GroupSid, afterRestore.GroupSid);
        Assert.Equal(reviewedMetadata.Origin.Digest, afterRestore.Origin.Digest);
        file.RenameNoReplace("restored-private.stage", version, afterRestore, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Exact_cleanup_unlinks_the_reviewed_name_while_an_independent_reader_keeps_the_bytes()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        using var file = Artifact(root); file.Write([1, 2, 3]); var version = file.Seal(TestContext.Current.CancellationToken);
        string path = file.DisplayPath;
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        file.DeleteReviewed(version, file.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.False(File.Exists(path)); File.WriteAllBytes(path, [9]);
        Assert.Equal(1, reader.ReadByte()); Assert.Equal(2, reader.ReadByte()); Assert.Equal(3, reader.ReadByte());
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void Manifest_codec_runs_on_the_private_native_file_and_fresh_recovery_checks_its_complete_version()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        var manifest = new ArchiveWriteManifest(new(ArchiveWriteFormat.Tar),
            [new("file.txt", ArchiveMemberType.File, 0, 3, Digest([1, 2, 3]), [7])], [1]);
        Guid nonce = Guid.NewGuid(); FileIdentity identity; string name; ArchiveObjectVersion version; ArchiveManifestSidecarInfo info;
        using (var sidecar = root.CreateArtifact(nonce, WindowsArchiveArtifactKind.Manifest, ArchiveWritePolicy.MaxMetadataBytes))
        {
            identity = sidecar.Identity; name = sidecar.KnownName;
            info = ArchiveManifestSidecar.Write(sidecar, nonce, ArchiveManifestPurpose.ExpectedOutput, manifest, TestContext.Current.CancellationToken);
            version = sidecar.Seal(TestContext.Current.CancellationToken);
            Assert.Equal(info.Length, version.Length); Assert.Equal(info.Sha256, version.Sha256);
        }
        using var fresh = root.OpenRecoveryArtifact(name, identity, WindowsArchiveArtifactKind.Manifest, ArchiveWritePolicy.MaxMetadataBytes);
        Assert.Equal(version, fresh.CaptureVersion(TestContext.Current.CancellationToken));
        using (var review = ArchiveManifestSidecar.OpenRead(fresh, info, nonce, ArchiveManifestPurpose.ExpectedOutput, manifest.Profile, TestContext.Current.CancellationToken))
        { Assert.Equal("file.txt", review.ReadNext()!.Name); Assert.Null(review.ReadNext()); Assert.Equal(manifest.Digest, review.VerifiedManifestDigest); }
        fresh.DeleteReviewed(version, fresh.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken); Assert.False(File.Exists(fixture.PathOf(name)));
    }

    [Theory]
    [InlineData("")][InlineData(".")][InlineData("..")][InlineData("C:\\outside")][InlineData("\\rooted")]
    [InlineData("a/b")][InlineData("a\\b")][InlineData("a:stream")][InlineData("trailing.")][InlineData("trailing ")]
    [InlineData("CON")][InlineData("NUL.txt")][InlineData("LPT¹.zip")][InlineData("COM9")][InlineData("CONOUT$")]
    [InlineData("a\0b")][InlineData("a\nb")][InlineData("*.tar")]
    public void Relative_native_names_reject_redirection_devices_ambiguity_and_invalid_unicode(string leaf) =>
        Assert.Throws<ArgumentException>(() => WindowsArchiveDirectory.ValidateLeaf(leaf));

    [Theory]
    [InlineData("ordinary.tar")][InlineData("café.tar")][InlineData("name😀.tar")][InlineData("com10.tar")]
    public void Relative_native_names_accept_ordinary_unicode_components(string leaf) => WindowsArchiveDirectory.ValidateLeaf(leaf);

    [Fact]
    public void Relative_names_refuse_unpaired_surrogates_and_excessive_length()
    {
        foreach (string name in new[] { "bad" + (char)0xd800, "bad" + (char)0xdc00, "a" + (char)0xd800 + "b", new string('a', 256) })
            Assert.Throws<ArgumentException>(() => WindowsArchiveDirectory.ValidateLeaf(name));
    }

    [Fact]
    public void Open_lease_count_is_bounded_and_disposal_returns_the_reservation()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        var held = new List<WindowsArchiveFile>();
        try
        {
            for (int i = 0; i < 16; i++) held.Add(Artifact(root));
            Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => Artifact(root)).Reason);
            Assert.Equal(16, Directory.EnumerateFiles(fixture.Root).Count());
            held[0].Dispose(); using var another = Artifact(root); Assert.Equal(17, Directory.EnumerateFiles(fixture.Root).Count());
        }
        finally { foreach (var item in held) item.Dispose(); }
    }

    [Fact]
    public void Artifact_scope_and_limits_are_checked_without_creating_any_file()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        Assert.Throws<ArgumentException>(() => root.CreateArtifact(Guid.Empty, WindowsArchiveArtifactKind.Stage, 1));
        Assert.Throws<ArgumentException>(() => root.CreateArtifact(Guid.NewGuid(), (WindowsArchiveArtifactKind)77, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Artifact(root, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Artifact(root, ArchiveWritePolicy.MaxPhysicalBytes + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => root.CreateArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Manifest, ArchiveWritePolicy.MaxMetadataBytes + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => root.CreateArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Provenance, ArchiveWritePolicy.MaxMetadataBytes + 1));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Root));
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string name, string existing, nint security);
}
