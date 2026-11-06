using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveMetadataTests
{
    private static void RequireWindows()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows native archive metadata required.");
        Assert.SkipWhen(WindowsTokenAuthority.IsHighIntegrity, "Run native archive metadata tests from an unelevated process.");
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("filecat-archive-metadata-").FullName;
        public string PathOf(string name) => Path.Combine(Root, name);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
    private static void Mark(string path)
    {
        using var stream = new FileStream(path + ":Zone.Identifier", FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete);
        stream.Write("[ZoneTransfer]\r\nZoneId=4\r\n"u8);
    }

    [Fact]
    public void Snapshot_captures_the_held_identity_native_times_attributes_owner_group_permissions_and_origin()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        string path = fixture.PathOf("original.bin"); File.WriteAllBytes(path, [1, 2, 3]); Mark(path);
        using var source = root.OpenSource("original.bin");
        var snapshot = source.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        Assert.Equal(source.Identity, snapshot.Identity); Assert.Equal(1u, snapshot.LinkCount);
        Assert.Equal(File.GetCreationTimeUtc(path).ToFileTimeUtc(), snapshot.CreationTime);
        Assert.Equal(File.GetLastWriteTimeUtc(path).ToFileTimeUtc(), snapshot.LastWriteTime);
        Assert.Equal((uint)File.GetAttributes(path), snapshot.Attributes);
        var security = new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);
        Assert.Equal(Assert.IsType<SecurityIdentifier>(security.GetOwner(typeof(SecurityIdentifier))).Value, snapshot.OwnerSid);
        Assert.Equal(Assert.IsType<SecurityIdentifier>(security.GetGroup(typeof(SecurityIdentifier))).Value, snapshot.GroupSid);
        Assert.Equal(security.AreAccessRulesProtected, snapshot.DaclProtected);
        Assert.Equal(ArchiveOriginState.Present, snapshot.Origin.State);
        Assert.Equal(source.ReadOrigin(TestContext.Current.CancellationToken).Digest, snapshot.Origin.Digest);
        Assert.Equal(snapshot.Digest, source.CaptureOuterMetadata(TestContext.Current.CancellationToken).Digest);
        Assert.NotEmpty(snapshot.SecurityDescriptor.ToArray());
        var decoded = new RawSecurityDescriptor(snapshot.SecurityDescriptor.ToArray(), 0);
        Assert.Equal(snapshot.OwnerSid, decoded.Owner!.Value); Assert.Equal(snapshot.GroupSid, decoded.Group!.Value);
    }

    [Fact]
    public void Same_payload_does_not_hide_changed_times_attributes_acl_or_a_new_mark()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        string path = fixture.PathOf("original.bin"); File.WriteAllBytes(path, [1, 2, 3]);
        using var source = root.OpenSource("original.bin"); var payload = source.CaptureVersion(TestContext.Current.CancellationToken);
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        void CaptureChanged()
        {
            Assert.True(distinct.Add(source.CaptureOuterMetadata(TestContext.Current.CancellationToken).Digest));
            Assert.Equal(payload, source.CaptureVersion(TestContext.Current.CancellationToken));
        }
        CaptureChanged();
        File.SetLastWriteTimeUtc(path, new DateTime(2021, 2, 3, 4, 5, 6, DateTimeKind.Utc)); CaptureChanged();
        File.SetCreationTimeUtc(path, new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc)); CaptureChanged();
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.NotContentIndexed); CaptureChanged();
        var security = new FileInfo(path).GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security); CaptureChanged();
        DateTime stamp = File.GetLastWriteTimeUtc(path); Mark(path); File.SetLastWriteTimeUtc(path, stamp); CaptureChanged();
        Assert.Equal(6, distinct.Count);
    }

    [Fact]
    public void Unreadable_origin_refuses_the_whole_metadata_capture_instead_of_defaulting_it_to_absent()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        string path = fixture.PathOf("original.bin"); File.WriteAllBytes(path, [1]); Mark(path);
        using var source = root.OpenSource("original.bin");
        using (var writer = new FileStream(path + ":Zone.Identifier", FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            Assert.Equal(ArchiveWriteReason.PublicationUnavailable, Assert.Throws<ArchiveWriteException>(() => source.CaptureOuterMetadata(TestContext.Current.CancellationToken)).Reason);
        Assert.Equal(ArchiveOriginState.Present, source.CaptureOuterMetadata(TestContext.Current.CancellationToken).Origin.State);
    }

    [Fact]
    public void Only_sealed_artifacts_are_captured_and_owned_rename_keeps_the_object_metadata_observation()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        using var stage = root.CreateArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Stage, 4096); stage.Write([1, 2, 3]);
        Assert.Throws<InvalidOperationException>(() => stage.CaptureOuterMetadata(TestContext.Current.CancellationToken));
        var version = stage.Seal(TestContext.Current.CancellationToken);
        var snapshot = stage.CaptureOuterMetadata(TestContext.Current.CancellationToken); Assert.True(snapshot.DaclProtected);
        stage.CopyOriginNoReplace(ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, "[ZoneTransfer]\r\nZoneId=3\r\n"u8), TestContext.Current.CancellationToken);
        var marked = stage.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        Assert.NotEqual(snapshot.Digest, marked.Digest); Assert.Equal(snapshot.LastWriteTime, marked.LastWriteTime);
        Assert.Equal(version, stage.CaptureVersion(TestContext.Current.CancellationToken));
        // Provenance preparation finishes before any namespace mutation. A rename then preserves that prepared
        // snapshot and closes this lease's origin-write capability, including an otherwise harmless absent request.
        stage.RenameNoReplace("retained.stage", version, marked, TestContext.Current.CancellationToken);
        Assert.Equal(marked.Digest, stage.CaptureOuterMetadata(TestContext.Current.CancellationToken).Digest);
        Assert.Throws<InvalidOperationException>(() => stage.CopyOriginNoReplace(ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Readonly_inputs_can_be_observed_and_cancellation_cannot_return_partial_metadata()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        string path = fixture.PathOf("original.bin"); File.WriteAllBytes(path, [1]); File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            using var source = root.OpenSource("original.bin");
            Assert.NotEqual(0u, source.CaptureOuterMetadata(TestContext.Current.CancellationToken).Attributes & (uint)FileAttributes.ReadOnly);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => source.CaptureOuterMetadata(canceled.Token));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    [Theory]
    [InlineData("mtime")]
    [InlineData("mark")]
    [InlineData("acl")]
    public void Changed_metadata_with_identical_bytes_refuses_both_rename_and_cleanup(string change)
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        string path = fixture.PathOf("original.bin"); File.WriteAllBytes(path, [1, 2, 3]);
        using var source = root.OpenReviewedTarget("original.bin");
        var version = source.CaptureVersion(TestContext.Current.CancellationToken);
        var reviewed = source.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        if (change == "mtime") File.SetLastWriteTimeUtc(path, new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        else if (change == "mark") { Mark(path); File.SetLastWriteTimeUtc(path, DateTime.FromFileTimeUtc(reviewed.LastWriteTime)); }
        else
        {
            var security = new FileInfo(path).GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        Assert.Equal(version, source.CaptureVersion(TestContext.Current.CancellationToken));
        Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() => source.RenameNoReplace("unwanted.bin", version, reviewed, TestContext.Current.CancellationToken)).Reason);
        Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() => source.DeleteReviewed(version, reviewed, TestContext.Current.CancellationToken)).Reason);
        Assert.True(File.Exists(path)); Assert.False(File.Exists(fixture.PathOf("unwanted.bin")));
        if (change == "mark") Assert.Equal(ArchiveOriginState.Present, source.ReadOrigin(TestContext.Current.CancellationToken).State);
    }

    [Fact]
    public void Metadata_of_another_object_cannot_authorize_rename_or_cleanup()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        File.WriteAllBytes(fixture.PathOf("a.bin"), [1]); File.WriteAllBytes(fixture.PathOf("b.bin"), [1]);
        using var source = root.OpenReviewedTarget("a.bin"); using var other = root.OpenSource("b.bin");
        var version = source.CaptureVersion(TestContext.Current.CancellationToken);
        var wrong = other.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        Assert.ThrowsAny<IOException>(() => source.RenameNoReplace("unwanted.bin", version, wrong, TestContext.Current.CancellationToken));
        Assert.ThrowsAny<IOException>(() => source.DeleteReviewed(version, wrong, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(fixture.PathOf("a.bin"))); Assert.True(File.Exists(fixture.PathOf("b.bin")));
    }

    [Fact]
    public void Sealed_metadata_remains_stable_when_the_writing_lease_closes_and_recovery_reopens_it()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        WindowsArchiveOuterMetadata reviewed; ArchiveObjectVersion version; string name;
        using (var stage = root.CreateArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Stage, 4096))
        {
            stage.Write([1, 2, 3]); version = stage.Seal(TestContext.Current.CancellationToken); name = stage.KnownName;
            reviewed = stage.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        }
        using var reopened = root.OpenRecoveryArtifact(name, reviewed.Identity, WindowsArchiveArtifactKind.Stage, 4096);
        Assert.Equal(version, reopened.CaptureVersion(TestContext.Current.CancellationToken));
        Assert.Equal(reviewed.Digest, reopened.CaptureOuterMetadata(TestContext.Current.CancellationToken).Digest);
    }
}
