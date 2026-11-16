using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveOriginWriteTests
{
    private static void Native()
    { if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Native Unix origin writing."); UnixArchiveNative.RequireOrdinaryIdentity(); }
    private static ArchiveOriginFormat Format => OperatingSystem.IsMacOS() ? ArchiveOriginFormat.MacQuarantine : ArchiveOriginFormat.LinuxOriginUrl;
    private static string Attribute => OperatingSystem.IsMacOS() ? "com.apple.quarantine" : "user.xdg.origin.url";
    private static UnixArchiveDirectory DirectoryOf(TempDir root) => UnixArchiveDirectory.Open(UnixFiles.RealPath(root.Path)!);
    private static UnixArchiveFile Stage(UnixArchiveDirectory root) => root.CreateArtifact(Guid.NewGuid(), UnixArchiveArtifactKind.Stage, 1 << 20);
    private static ArchiveObjectVersion Seal(UnixArchiveFile file) => file.Seal(TestContext.Current.CancellationToken);
    private static ArchiveObjectVersion Version(UnixArchiveFile file) => file.CaptureVersion(TestContext.Current.CancellationToken);
    private static ArchiveUnixOuterMetadata Metadata(UnixArchiveFile file) => file.CaptureOuterMetadata(TestContext.Current.CancellationToken);
    private static ArchiveOriginSnapshot Origin(UnixArchiveFile file) => file.ReadOrigin(TestContext.Current.CancellationToken);
    private static void Copy(UnixArchiveFile file, ArchiveOriginSnapshot origin) => file.CopyOriginNoReplace(origin, TestContext.Current.CancellationToken);

    public static IEnumerable<object[]> Marks() => new byte[][]
    {
        [], "https://example.test/download?q=é"u8.ToArray(), "0081;12345678;FileCat;"u8.ToArray(), [0xff, 0, 0x80, 1], new byte[1024],
    }.Select(bytes => new object[] { bytes });
    [Theory]
    [MemberData(nameof(Marks))]
    public void Native_create_only_copy_preserves_exact_empty_text_and_binary_bytes_and_all_other_observed_fields(byte[] bytes)
    {
        Native(); using var temp = new TempDir(); using var root = DirectoryOf(temp); using var file = Stage(root);
        file.Write([1, 2, 3]); var version = Seal(file); var before = Metadata(file);
        var origin = ArchiveOriginSnapshot.Present(Format, bytes); Copy(file, origin);
        Assert.Equal(origin.Digest, Origin(file).Digest); Assert.Equal(bytes, Origin(file).Bytes.ToArray()); Assert.Equal(version, Version(file));
        var after = Metadata(file); Assert.Equal(before.Native, after.Native with { Changed = before.Native.Changed });
        Assert.Equal(before.AclPresent, after.AclPresent); Assert.Equal(before.AclBytes.ToArray(), after.AclBytes.ToArray());
        file.RenameNoReplace("marked.tar", version, after, TestContext.Current.CancellationToken);
        Assert.Equal(origin.Digest, Origin(file).Digest); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(file.DisplayPath));
        foreach (var request in new[] { ArchiveOriginSnapshot.Absent(Format), origin })
            Assert.Throws<InvalidOperationException>(() => Copy(file, request));
        Assert.Equal(origin.Digest, Origin(file).Digest);
    }
    [Fact]
    public void Native_absence_is_not_materialized_as_an_empty_attribute()
    {
        Native(); using var temp = new TempDir(); using var root = DirectoryOf(temp); using var file = Stage(root); var version = Seal(file); var before = Metadata(file);
        Copy(file, ArchiveOriginSnapshot.Absent(Format)); Assert.Equal(ArchiveOriginState.Absent, Origin(file).State);
        Assert.Equal(before.Digest, Metadata(file).Digest); Assert.Equal(version, Version(file));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_existing_empty_or_present_origin_is_never_overwritten_or_erased(bool empty)
    {
        Native(); using var temp = new TempDir(); using var root = DirectoryOf(temp); using var file = Stage(root); var version = Seal(file);
        byte[] actual = empty ? [] : "late evidence"u8.ToArray(); Put(file.DisplayPath, actual);
        foreach (var origin in new[] { ArchiveOriginSnapshot.Absent(Format), ArchiveOriginSnapshot.Present(Format, "wanted"u8) })
            Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() => Copy(file, origin)).Reason);
        Assert.Equal(ArchiveOriginState.Present, Origin(file).State); Assert.Equal(actual, Origin(file).Bytes.ToArray()); Assert.Equal(version, Version(file));
    }
    [Fact]
    public void Unsealed_source_reopened_renamed_and_disposed_leases_cannot_gain_origin_write_authority()
    {
        Native(); using var temp = new TempDir(); using var root = DirectoryOf(temp); var absent = ArchiveOriginSnapshot.Absent(Format);
        using (var unsealed = Stage(root)) Assert.Throws<InvalidOperationException>(() => Copy(unsealed, absent));
        string leaf; FileIdentity identity;
        using (var created = Stage(root)) { leaf = created.KnownName; identity = created.Identity; Seal(created); }
        using (var recovery = root.OpenRecoveryArtifact(leaf, identity, UnixArchiveArtifactKind.Stage, 1 << 20))
            Assert.Throws<InvalidOperationException>(() => Copy(recovery, absent));
        using (var source = root.OpenSource(leaf)) Assert.Throws<InvalidOperationException>(() => Copy(source, absent));
        using var stage = Stage(root); var version = Seal(stage);
        stage.RenameNoReplace("published.tar", version, Metadata(stage), TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => Copy(stage, absent)); Assert.Equal(ArchiveOriginState.Absent, Origin(stage).State);
        stage.Dispose(); Assert.Throws<ObjectDisposedException>(() => Copy(stage, absent));
    }
    [Fact]
    public void Foreign_unavailable_and_cancelled_requests_do_not_modify_the_artifact()
    {
        Native(); using var temp = new TempDir(); using var root = DirectoryOf(temp); using var file = Stage(root); Seal(file); var before = Metadata(file);
        foreach (var origin in new[] { ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, [1]),
                     ArchiveOriginSnapshot.Unavailable(Format, ArchiveOriginFailure.AccessDenied),
                     ArchiveOriginSnapshot.Absent(OperatingSystem.IsMacOS() ? ArchiveOriginFormat.LinuxOriginUrl : ArchiveOriginFormat.MacQuarantine) })
            Assert.Equal(ArchiveWriteReason.PublicationUnavailable, Assert.Throws<ArchiveWriteException>(() => Copy(file, origin)).Reason);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => file.CopyOriginNoReplace(ArchiveOriginSnapshot.Present(Format, [1]), stop.Token));
        Assert.Equal(before.Digest, Metadata(file).Digest); Assert.Equal(ArchiveOriginState.Absent, Origin(file).State);
    }
    [Fact]
    public void A_stale_name_and_changed_permissions_do_not_retarget_or_authorize_attribute_writes()
    {
        Native(); using var temp = new TempDir(); using var root = DirectoryOf(temp); using var file = Stage(root); Seal(file);
        string path = file.DisplayPath, moved = path + ".held"; var wanted = ArchiveOriginSnapshot.Present(Format, "owned evidence"u8);
        File.SetUnixFileMode(path, (UnixFileMode)0x1a4);
        try { Assert.Throws<ArchiveWriteException>(() => Copy(file, wanted)); }
        finally { File.SetUnixFileMode(path, (UnixFileMode)0x180); }
        File.Move(path, moved); File.WriteAllText(path, "different occupant");
        Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() => Copy(file, wanted)).Reason);
        Assert.Equal(ArchiveOriginState.Absent, Origin(file).State);
        using var replacement = File.OpenHandle(path); Assert.Equal(ArchiveOriginState.Absent, ArchiveUnixOrigin.Read(replacement, TestContext.Current.CancellationToken).State);
        Assert.Equal("different occupant", File.ReadAllText(path));
    }
    [Fact]
    public void A_Linux_permission_revocation_cannot_turn_unknown_provenance_into_absence()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Native Linux attribute-read denial.");
        Native(); using var temp = new TempDir(); using var root = DirectoryOf(temp); using var file = Stage(root); Seal(file); string path = file.DisplayPath;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.None); var observed = Origin(file);
            Assert.Equal(ArchiveOriginState.Unavailable, observed.State); Assert.Equal(ArchiveOriginFailure.AccessDenied, observed.Failure);
            Assert.Throws<ArchiveWriteException>(() => Copy(file, ArchiveOriginSnapshot.Absent(Format)));
        }
        finally { File.SetUnixFileMode(path, (UnixFileMode)0x180); }
    }
    [Fact]
    public void Mac_maximum_bounded_raw_quarantine_bytes_copy_without_text_conversion()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("Native APFS large quarantine fixture.");
        Native(); using var temp = new TempDir(); using var root = DirectoryOf(temp); using var file = Stage(root); Seal(file);
        byte[] bytes = new byte[ArchiveOriginSnapshot.MaxBytes]; new Random(1042).NextBytes(bytes); Copy(file, ArchiveOriginSnapshot.Present(Format, bytes));
        Assert.Equal(bytes, Origin(file).Bytes.ToArray());
    }
    private static void Put(string path, byte[] bytes)
    {
        using var held = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        int fd = checked((int)held.DangerousGetHandle()); int result = OperatingSystem.IsMacOS() ? MacSet(fd, Attribute, bytes, bytes.Length, 0, 0) : LinuxSet(fd, Attribute, bytes, bytes.Length, 0);
        Assert.Equal(0, result);
    }
    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxSet(int fd, string name, byte[] value, nint size, int flags);
    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacSet(int fd, string name, byte[] value, nint size, uint position, int options);
}
