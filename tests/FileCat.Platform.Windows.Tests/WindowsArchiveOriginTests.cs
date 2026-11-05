using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsArchiveOriginTests
{
    private static void RequireWindows()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows native archive provenance required.");
        Assert.SkipWhen(WindowsTokenAuthority.IsHighIntegrity, "Run native archive provenance tests from an unelevated process.");
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("filecat-archive-origin-").FullName;
        public string PathOf(string name) => Path.Combine(Root, name);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
    private static WindowsArchiveFile Stage(WindowsArchiveDirectory root) => root.CreateArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Stage, 4096);
    private static void PutMark(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(path + ":Zone.Identifier", FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete);
        stream.Write(bytes);
    }
    private static byte[] ReadMark(string path)
    {
        using var stream = new FileStream(path + ":Zone.Identifier", FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var data = new MemoryStream(); stream.CopyTo(data); return data.ToArray();
    }

    [Fact]
    public void Native_missing_empty_and_unreadable_streams_remain_three_distinct_states()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        string path = fixture.PathOf("input.bin"); File.WriteAllBytes(path, [1, 2, 3]);
        using var input = root.OpenSource("input.bin");
        var absent = input.ReadOrigin(TestContext.Current.CancellationToken); Assert.Equal(ArchiveOriginState.Absent, absent.State);
        PutMark(path, []);
        var empty = input.ReadOrigin(TestContext.Current.CancellationToken); Assert.Equal(ArchiveOriginState.Present, empty.State); Assert.Empty(empty.Bytes.ToArray());
        using var writer = new FileStream(path + ":Zone.Identifier", FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var locked = input.ReadOrigin(TestContext.Current.CancellationToken);
        Assert.Equal(ArchiveOriginState.Unavailable, locked.State); Assert.Equal(ArchiveOriginFailure.InUse, locked.Failure);
        Assert.Equal(3, new[] { absent.Digest, empty.Digest, locked.Digest }.Distinct().Count());
    }

    [Fact]
    public void Oversized_origin_is_unavailable_before_materialization_and_cancellation_propagates()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        string path = fixture.PathOf("input.bin"); File.WriteAllBytes(path, [1]);
        using (var mark = File.Create(path + ":Zone.Identifier")) mark.SetLength(ArchiveOriginSnapshot.MaxBytes + 1);
        using var input = root.OpenSource("input.bin");
        var tooLarge = input.ReadOrigin(TestContext.Current.CancellationToken);
        Assert.Equal(ArchiveOriginState.Unavailable, tooLarge.State); Assert.Equal(ArchiveOriginFailure.TooLarge, tooLarge.Failure);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => input.ReadOrigin(canceled.Token));
    }

    [Fact]
    public void An_access_denied_mark_is_unavailable_and_never_becomes_absent()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        string path = fixture.PathOf("input.bin"); File.WriteAllBytes(path, [1]); PutMark(path, "[ZoneTransfer]\r\nZoneId=4\r\n"u8);
        using var input = root.OpenSource("input.bin");
        var original = new FileInfo(path).GetAccessControl(); var changed = new FileInfo(path).GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        changed.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ReadData, AccessControlType.Deny));
        try
        {
            new FileInfo(path).SetAccessControl(changed);
            var refused = input.ReadOrigin(TestContext.Current.CancellationToken);
            Assert.Equal(ArchiveOriginState.Unavailable, refused.State); Assert.Equal(ArchiveOriginFailure.AccessDenied, refused.Failure);
        }
        finally
        {
            // SetAccessControl persists modified sections; an untouched captured descriptor is otherwise a no-op.
            original.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            new FileInfo(path).SetAccessControl(original);
        }
        Assert.Equal(ArchiveOriginState.Present, input.ReadOrigin(TestContext.Current.CancellationToken).State);
    }

    public static IEnumerable<object[]> Marks() => new byte[][]
    {
        [], "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/download\r\n"u8.ToArray(),
        Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("[ZoneTransfer]\r\nZoneId=4\r\n")).ToArray(),
        [0xff, 0, 0x80, 1], new byte[ArchiveOriginSnapshot.MaxBytes],
    }.Select(bytes => new object[] { bytes });

    [Theory]
    [MemberData(nameof(Marks))]
    public void Exact_opaque_marks_copy_to_the_owned_stage_without_changing_payload_or_mtime_and_survive_rename(byte[] mark)
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        using var stage = Stage(root); stage.Write([1, 2, 3]); var version = stage.Seal(TestContext.Current.CancellationToken);
        var modified = File.GetLastWriteTimeUtc(stage.DisplayPath);
        var origin = ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, mark);
        stage.CopyOriginNoReplace(origin, TestContext.Current.CancellationToken);
        Assert.Equal(origin.Digest, stage.ReadOrigin(TestContext.Current.CancellationToken).Digest);
        Assert.Equal(version, stage.CaptureVersion(TestContext.Current.CancellationToken));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(stage.DisplayPath));
        stage.RenameNoReplace("retained.tar", version, TestContext.Current.CancellationToken);
        File.WriteAllBytes(fixture.PathOf("unrelated.bin"), [9]);
        Assert.Equal(origin.Digest, stage.ReadOrigin(TestContext.Current.CancellationToken).Digest);
        Assert.Equal(mark, ReadMark(stage.DisplayPath));
    }

    [Fact]
    public void A_new_mark_on_the_stage_is_never_erased_or_overwritten_even_by_a_previously_unmarked_result()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        using var stage = Stage(root); stage.Write([1]); stage.Seal(TestContext.Current.CancellationToken);
        byte[] actual = "[ZoneTransfer]\r\nZoneId=4\r\n"u8.ToArray();
        PutMark(stage.DisplayPath, actual);
        foreach (var origin in new[] { ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier),
                     ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, "[ZoneTransfer]\r\nZoneId=3\r\n"u8) })
            Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() => stage.CopyOriginNoReplace(origin, TestContext.Current.CancellationToken)).Reason);
        Assert.Equal(actual, stage.ReadOrigin(TestContext.Current.CancellationToken).Bytes.ToArray());
    }

    [Fact]
    public void Unavailable_foreign_and_canceled_origins_do_not_create_a_mark_and_recovery_leases_cannot_write_origins()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        string name; FileCat.Core.FileSystem.FileIdentity identity;
        using (var stage = Stage(root))
        {
            Assert.Throws<InvalidOperationException>(() => stage.CopyOriginNoReplace(ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier), TestContext.Current.CancellationToken));
            name = stage.KnownName; identity = stage.Identity; stage.Seal(TestContext.Current.CancellationToken);
            foreach (var origin in new[] { ArchiveOriginSnapshot.Unavailable(ArchiveOriginFormat.WindowsZoneIdentifier, ArchiveOriginFailure.AccessDenied),
                         ArchiveOriginSnapshot.Present(ArchiveOriginFormat.MacQuarantine, [1]) })
                Assert.Equal(ArchiveWriteReason.PublicationUnavailable, Assert.Throws<ArchiveWriteException>(() => stage.CopyOriginNoReplace(origin, TestContext.Current.CancellationToken)).Reason);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => stage.CopyOriginNoReplace(ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, [1]), canceled.Token));
            Assert.Equal(ArchiveOriginState.Absent, stage.ReadOrigin(TestContext.Current.CancellationToken).State);
        }
        using var recovery = root.OpenRecoveryArtifact(name, identity, WindowsArchiveArtifactKind.Stage, 4096);
        Assert.Throws<InvalidOperationException>(() => recovery.CopyOriginNoReplace(ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_source_origin_is_read_again_after_its_held_object_moves_and_a_new_input_mark_is_not_hidden_by_unchanged_bytes()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        string path = fixture.PathOf("input.bin"); File.WriteAllBytes(path, [1, 2, 3]);
        using var input = root.OpenReviewedTarget("input.bin"); var version = input.CaptureVersion(TestContext.Current.CancellationToken);
        var before = input.ReadOrigin(TestContext.Current.CancellationToken); Assert.Equal(ArchiveOriginState.Absent, before.State);
        input.RenameNoReplace("retired.bin", version, TestContext.Current.CancellationToken);
        File.WriteAllBytes(path, [9]);
        PutMark(fixture.PathOf("retired.bin"), "[ZoneTransfer]\r\nZoneId=4\r\n"u8);
        Assert.Equal(version, input.CaptureVersion(TestContext.Current.CancellationToken));
        Assert.Equal(ArchiveOriginState.Present, input.ReadOrigin(TestContext.Current.CancellationToken).State);
        Assert.NotEqual(before.Digest, input.ReadOrigin(TestContext.Current.CancellationToken).Digest);
        Assert.False(File.Exists(path + ":Zone.Identifier"));
    }
}
