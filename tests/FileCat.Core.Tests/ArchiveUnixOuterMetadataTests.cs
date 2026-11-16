using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;
using static FileCat.Core.Archives.ArchiveUnixOuterMetadata;

namespace FileCat.Core.Tests;

public sealed partial class ArchiveUnixOuterMetadataTests
{
    private static Facts Sample => new(NativeFormat.Linux, 0x1234, 42, 7, 1000, 100, 0x1a0, 2,
        new(123, 456), new(789, 123456789), new Timestamp(-42, 999999999), 0x40, 0x800, 0x1800);

    [Fact]
    public void Frozen_ACL_bytes_and_canonical_digest_are_repeatable_and_have_no_content_authority()
    {
        byte[] acl = [2, 0, 1, 0, 0xff]; var origin = ArchiveOriginSnapshot.Present(ArchiveOriginFormat.LinuxOriginUrl, [0xff, 0, 0x80]);
        var snapshot = new ArchiveUnixOuterMetadata(Sample, true, acl, origin); string digest = snapshot.Digest;
        acl[0] = 99;
        Assert.Equal(new byte[] { 2, 0, 1, 0, 0xff }, snapshot.AclBytes.ToArray());
        Assert.Equal(digest, new ArchiveUnixOuterMetadata(Sample, true, [2, 0, 1, 0, 0xff], origin).Digest);
        Assert.Equal(new FileIdentity(0x1234, "000000000000002A0000000000000000"), snapshot.Identity);
        // Independently encoded little-endian schema/field fixture, including binary ACL/origin and pre-epoch birth.
        Assert.Equal("CE2E321DBE5D7671B2470F4D973194B54ECD23AAD845B44C690318ED3F12344B", digest);
    }

    [Theory]
    [InlineData("device")]
    [InlineData("inode")]
    [InlineData("length")]
    [InlineData("owner")]
    [InlineData("group")]
    [InlineData("mode")]
    [InlineData("links")]
    [InlineData("mtimeSeconds")]
    [InlineData("mtimeNanos")]
    [InlineData("ctime")]
    [InlineData("birthAbsent")]
    [InlineData("birthSeconds")]
    [InlineData("birthNanos")]
    [InlineData("flags")]
    [InlineData("attributes")]
    [InlineData("attributeMask")]
    [InlineData("aclAbsent")]
    [InlineData("aclBytes")]
    [InlineData("originAbsent")]
    [InlineData("originBytes")]
    [InlineData("platform")]
    public void Every_named_observation_and_presence_marker_participates_in_the_digest(string field)
    {
        var facts = Sample; bool present = true; byte[] acl = [2, 0, 0, 0];
        var origin = ArchiveOriginSnapshot.Present(ArchiveOriginFormat.LinuxOriginUrl, "url"u8);
        var baseline = new ArchiveUnixOuterMetadata(facts, present, acl, origin);
        facts = field switch
        {
            "device" => facts with { Device = facts.Device + 1 }, "inode" => facts with { Inode = facts.Inode + 1 },
            "length" => facts with { Length = 8 }, "owner" => facts with { Owner = 1001 }, "group" => facts with { Group = 101 },
            "mode" => facts with { Mode = 0x180 }, "links" => facts with { LinkCount = 1 },
            "mtimeSeconds" => facts with { Modified = facts.Modified with { Seconds = 124 } },
            "mtimeNanos" => facts with { Modified = facts.Modified with { Nanoseconds = 457 } },
            "ctime" => facts with { Changed = new(789, 123456790) }, "birthAbsent" => facts with { Created = null },
            "birthSeconds" => facts with { Created = new(-43, 999999999) }, "birthNanos" => facts with { Created = new(-42, 999999998) },
            "flags" => facts with { Flags = 0x41 }, "attributes" => facts with { Attributes = 0 },
            "attributeMask" => facts with { AttributeMask = 0 }, "platform" => facts with { Format = NativeFormat.Mac }, _ => facts,
        };
        if (field == "aclAbsent") { present = false; acl = []; }
        if (field == "aclBytes") acl = [2, 0, 0, 1];
        if (field == "originAbsent") origin = ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.LinuxOriginUrl);
        if (field == "originBytes") origin = ArchiveOriginSnapshot.Present(ArchiveOriginFormat.LinuxOriginUrl, "urL"u8);
        if (field == "platform") origin = ArchiveOriginSnapshot.Present(ArchiveOriginFormat.MacQuarantine, "url"u8);
        Assert.NotEqual(baseline.Digest, new ArchiveUnixOuterMetadata(facts, present, acl, origin).Digest);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("size")]
    [InlineData("mode")]
    [InlineData("time")]
    [InlineData("birth")]
    [InlineData("format")]
    [InlineData("aclEmpty")]
    [InlineData("aclAbsent")]
    [InlineData("aclLarge")]
    [InlineData("macOriginFormat")]
    [InlineData("originUnavailable")]
    [InlineData("originFormat")]
    public void Incomplete_or_unbounded_observations_cannot_become_a_snapshot(string defect)
    {
        var facts = Sample; bool present = true; byte[] acl = [1]; var origin = ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.LinuxOriginUrl);
        facts = defect switch
        {
            "length" => facts with { Length = -1 }, "size" => facts with { Length = ArchiveWritePolicy.MaxPhysicalBytes + 1 },
            "mode" => facts with { Mode = 0x1000 }, "time" => facts with { Modified = new(0, 1_000_000_000) },
            "birth" => facts with { Created = new(0, -1) }, "format" => facts with { Format = (NativeFormat)88 },
            "macOriginFormat" => facts with { Format = NativeFormat.Mac }, _ => facts,
        };
        if (defect == "aclEmpty") acl = [];
        if (defect == "aclAbsent") present = false;
        if (defect == "aclLarge") acl = new byte[MaxAclBytes + 1];
        if (defect == "originUnavailable") origin = ArchiveOriginSnapshot.Unavailable(ArchiveOriginFormat.LinuxOriginUrl, ArchiveOriginFailure.AccessDenied);
        if (defect == "originFormat") origin = ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier);
        Assert.Throws<ArgumentException>(() => new ArchiveUnixOuterMetadata(facts, present, acl, origin));
    }

    [Fact]
    public void Native_observations_match_the_held_object_and_preserve_precision_without_touching_it()
    {
        RequireUnix(); using var root = new TempDir(); string path = root.File("owned", "payload"); using var held = Open(path);
        var before = UnixFiles.Stat(held)!.Value; var snapshot = Capture(held, TestContext.Current.CancellationToken);
        Assert.Equal(before.Identity, snapshot.Identity); Assert.Equal(before.Owner, snapshot.Native.Owner); Assert.Equal(before.Mode, snapshot.Native.Mode);
        Assert.Equal(before.Size, snapshot.Native.Length); Assert.Equal(before.ChangeSeconds, snapshot.Native.Changed.Seconds); Assert.Equal(before.ChangeNanoseconds, snapshot.Native.Changed.Nanoseconds);
        Assert.Equal(File.GetLastWriteTimeUtc(path), Time(snapshot.Native.Modified));
        Assert.Equal((ulong)1, snapshot.Native.LinkCount); Assert.Equal(ArchiveOriginState.Absent, snapshot.Origin.State);
        // Linux's managed creation-time API can fall back to ctime/mtime. Compare actual birth time with the C
        // statx observer below, rather than replacing a precise native field with that managed fallback.
        if (OperatingSystem.IsMacOS() && snapshot.Native.Created is { } birth) Assert.Equal(File.GetCreationTimeUtc(path), Time(birth));
        Assert.Equal(snapshot.Digest, Capture(held, TestContext.Current.CancellationToken).Digest);
        Assert.Equal(before, UnixFiles.Stat(held)); Assert.Equal("payload", File.ReadAllText(path));
    }

    [Fact]
    public async Task Native_C_header_observer_independently_agrees_on_every_stat_and_flag_field()
    {
        RequireUnix(); string? observer = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_UNIX_METADATA_OBSERVER");
        if (string.IsNullOrEmpty(observer)) Assert.Skip("Explicit independently compiled native C observer required.");
        Assert.True(Path.IsPathFullyQualified(observer) && File.Exists(observer));
        using var root = new TempDir(); string path = root.File("owned", "payload"); using var held = Open(path);
        var before = Read(held); var start = new ProcessStartInfo(observer) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(path); using var native = Process.Start(start)!;
        try
        {
            string output = await native.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await native.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(0, native.ExitCode); Assert.InRange(output.Length, 1, 2048);
            Assert.Equal(before.Digest, Read(held).Digest); using var json = JsonDocument.Parse(output); var fact = json.RootElement;
            Assert.Equal(before.Native.Device, fact.GetProperty("device").GetUInt64()); Assert.Equal(before.Native.Inode, fact.GetProperty("inode").GetUInt64());
            Assert.Equal(before.Native.LinkCount, fact.GetProperty("links").GetUInt64()); Assert.Equal(before.Native.Length, fact.GetProperty("size").GetInt64());
            Assert.Equal(before.Native.Owner, fact.GetProperty("owner").GetUInt32()); Assert.Equal(before.Native.Group, fact.GetProperty("group").GetUInt32());
            Assert.Equal(before.Native.Mode, fact.GetProperty("mode").GetUInt32()); Assert.Equal(before.Native.Flags, fact.GetProperty("flags").GetUInt32());
            Assert.Equal(before.Native.Modified, new Timestamp(fact.GetProperty("mtime").GetInt64(), fact.GetProperty("mtimeNs").GetInt64()));
            Assert.Equal(before.Native.Changed, new Timestamp(fact.GetProperty("ctime").GetInt64(), fact.GetProperty("ctimeNs").GetInt64()));
            Assert.Equal(before.Native.Created.HasValue, fact.GetProperty("hasBirth").GetInt32() == 1);
            if (before.Native.Created is { } birth) Assert.Equal(birth, new Timestamp(fact.GetProperty("birth").GetInt64(), fact.GetProperty("birthNs").GetInt64()));
            Assert.Equal(before.Native.Attributes, fact.GetProperty("attributes").GetUInt64()); Assert.Equal(before.Native.AttributeMask, fact.GetProperty("attributeMask").GetUInt64());
        }
        finally { if (!native.HasExited) { native.Kill(); await native.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); } }
    }

    [Fact]
    public void Held_rename_replacement_symlink_and_unlink_never_retarget_metadata_capture()
    {
        RequireUnix(); using var root = new TempDir(); string path = root.File("named", "original"), moved = Path.Combine(root.Path, "moved"); using var held = Open(path);
        var identity = UnixFiles.Stat(held)!.Value.Identity;
        File.Move(path, moved); File.WriteAllText(path, "different replacement");
        Assert.Equal(identity, Read(held).Identity); Assert.Equal(8, Read(held).Native.Length);
        File.Delete(path); File.CreateSymbolicLink(path, root.File("other", "other bytes"));
        Assert.Equal(identity, Read(held).Identity);
        File.Delete(moved); var unlinked = Read(held);
        Assert.Equal(identity, unlinked.Identity); Assert.Equal((ulong)0, unlinked.Native.LinkCount); Assert.Equal(8, unlinked.Native.Length);
    }

    [Fact]
    public void Content_unchanged_mode_mtime_and_raw_origin_changes_produce_new_observations()
    {
        RequireUnix(); using var root = new TempDir(); string path = root.File("owned", "payload"); using var held = Open(path);
        var first = Read(held); var mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, mode ^ UnixFileMode.GroupRead); var permissions = Read(held);
        Assert.NotEqual(first.Digest, permissions.Digest); Assert.NotEqual(first.Native.Mode, permissions.Native.Mode);
        File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddSeconds(12345).AddTicks(1234567)); var modified = Read(held);
        Assert.NotEqual(permissions.Digest, modified.Digest); Assert.Equal(File.GetLastWriteTimeUtc(path), Time(modified.Native.Modified));
        SetOrigin(held, [0xff, 0, 0x80]); var marked = Read(held);
        Assert.Equal(new byte[] { 0xff, 0, 0x80 }, marked.Origin.Bytes.ToArray()); Assert.NotEqual(modified.Digest, marked.Digest);
        Assert.Equal(modified.Native.Modified, marked.Native.Modified); Assert.Equal("payload", File.ReadAllText(path));
    }

    [Fact]
    public void Native_hardlink_counts_observe_both_names_without_modifying_content()
    {
        RequireUnix(); using var root = new TempDir(); string path = root.File("owned", "payload"), alias = Path.Combine(root.Path, "alias"); using var held = Open(path);
        var first = Read(held); Assert.Equal(0, Link(path, alias));
        var linked = Read(held); Assert.Equal((ulong)2, linked.Native.LinkCount); Assert.Equal(first.Identity, linked.Identity); Assert.NotEqual(first.Digest, linked.Digest);
        File.Delete(alias); Assert.Equal((ulong)1, Read(held).Native.LinkCount); Assert.Equal("payload", File.ReadAllText(path));
    }

    [Fact]
    public void Native_Linux_POSIX_access_ACL_bytes_are_frozen_and_not_replaced_with_mode_bits()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Native Linux POSIX ACL fixture.");
        using var root = new TempDir(); using var held = Open(root.File("owned", "payload"));
        Assert.False(Read(held).AclPresent); byte[] acl = PosixAcl(4);
        Assert.Equal(0, SetAcl(Fd(held), "system.posix_acl_access", acl, acl.Length, 0)); var first = Read(held);
        Assert.True(first.AclPresent); Assert.Equal(acl, first.AclBytes.ToArray());
        byte[] changed = PosixAcl(0); Assert.Equal(0, SetAcl(Fd(held), "system.posix_acl_access", changed, changed.Length, 0));
        var second = Read(held); Assert.Equal(first.Native.Mode, second.Native.Mode); Assert.Equal(first.Native.Modified, second.Native.Modified);
        Assert.NotEqual(first.Digest, second.Digest); Assert.Equal(acl, first.AclBytes.ToArray()); Assert.Equal(changed, second.AclBytes.ToArray());
    }

    [Fact]
    public void Native_Mac_extended_ACL_is_captured_including_changes_not_expressed_by_mode()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("Native macOS extended ACL fixture.");
        using var root = new TempDir(); string path = root.File("owned", "payload"); using var held = Open(path); var first = Read(held);
        try
        {
            Chmod("+a", "everyone allow read", path); var second = Read(held);
            Assert.False(first.AclPresent); Assert.True(second.AclPresent); Assert.NotEmpty(second.AclBytes.ToArray());
            Assert.Equal(first.Native.Mode, second.Native.Mode); Assert.Equal(first.Native.Modified, second.Native.Modified);
            Assert.NotEqual(first.AclBytes.ToArray(), second.AclBytes.ToArray()); Assert.NotEqual(first.Digest, second.Digest);
            Assert.Equal(second.Digest, Read(held).Digest);
        }
        finally { Chmod("-N", path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Native_permission_denials_are_unavailable_for_absent_and_present_origin_marks(bool marked)
    {
        RequireUnix(); if (GetEuid() == 0) Assert.Skip("Ordinary-user permission denial required.");
        using var root = new TempDir(); string path = root.File("owned", "payload"); using var held = Open(path);
        if (marked) SetOrigin(held, "origin"u8.ToArray()); var mode = File.GetUnixFileMode(path);
        try
        {
            if (OperatingSystem.IsMacOS()) Chmod("+a", "everyone deny readsecurity", path);
            else File.SetUnixFileMode(path, UnixFileMode.None);
            Assert.Equal(ArchiveWriteReason.PublicationUnavailable, Assert.Throws<ArchiveWriteException>(() => Read(held)).Reason);
        }
        finally { if (OperatingSystem.IsMacOS()) Chmod("-N", path); else File.SetUnixFileMode(path, mode); }
    }

    [Fact]
    public unsafe void Native_nodump_flags_participate_in_observations_and_are_never_silently_cleared()
    {
        RequireUnix(); using var root = new TempDir(); using var held = Open(root.File("owned", "payload")); var first = Read(held);
        uint bit = OperatingSystem.IsMacOS() ? 1u : 0x40u;
        try
        {
            SetFlags(first.Native.Flags | bit); var second = Read(held);
            Assert.Equal(first.Native.Flags | bit, second.Native.Flags); Assert.NotEqual(first.Digest, second.Digest);
            Assert.Equal(first.Native.Mode, second.Native.Mode); Assert.Equal(first.Native.Modified, second.Native.Modified);
        }
        finally { SetFlags(first.Native.Flags); }
        void SetFlags(uint flags)
        {
            ulong value = flags;
            int result = OperatingSystem.IsMacOS() ? Fchflags(Fd(held), flags) : Ioctl(Fd(held), 0x40086602, &value);
            Assert.Equal(0, result);
        }
    }

    [Fact]
    public void Non_regular_devices_directories_cancelled_and_disposed_handles_are_refused()
    {
        RequireUnix(); using var root = new TempDir();
        foreach (string path in new[] { root.Path, "/dev/null" })
        {
            int fd = OpenNative(path, OperatingSystem.IsMacOS() ? 4 | 0x1000000 : 0x800 | 0x80000);
            Assert.True(fd >= 0); using var held = new SafeFileHandle((nint)fd, ownsHandle: true);
            Assert.Equal(ArchiveWriteReason.PublicationUnavailable, Assert.Throws<ArchiveWriteException>(() => Read(held)).Reason);
        }
        using var file = Open(root.File("owned", "payload")); using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => Capture(file, stop.Token));
        file.Dispose(); Assert.Throws<ObjectDisposedException>(() => Read(file));
    }

    [Fact]
    public void Unsupported_Windows_dispatch_does_not_treat_a_handle_as_Unix_metadata()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows unsupported-platform guard.");
        using var invalid = new SafeFileHandle((nint)(-1), ownsHandle: false);
        Assert.Throws<PlatformNotSupportedException>(() => Read(invalid));
    }

    private static ArchiveUnixOuterMetadata Read(SafeFileHandle file) => Capture(file, TestContext.Current.CancellationToken);
    private static DateTime Time(Timestamp value) => DateTime.UnixEpoch.AddSeconds(value.Seconds).AddTicks(value.Nanoseconds / 100);
    private static void RequireUnix() { if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Native Unix metadata fixture."); }
    private static SafeFileHandle Open(string path) => File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
    private static int Fd(SafeFileHandle handle) => checked((int)handle.DangerousGetHandle());
    private static void SetOrigin(SafeFileHandle handle, byte[] value) => Assert.Equal(0, OperatingSystem.IsMacOS()
        ? MacSet(Fd(handle), "com.apple.quarantine", value, value.Length, 0, 0)
        : SetAcl(Fd(handle), "user.xdg.origin.url", value, value.Length, 0));
    private static byte[] PosixAcl(ushort user)
    {
        byte[] bytes = new byte[44]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, 2);
        var entries = new (ushort Tag, ushort Permission, uint Id)[] { (1, 6, uint.MaxValue), (2, user, 50001), (4, 4, uint.MaxValue), (16, 4, uint.MaxValue), (32, 0, uint.MaxValue) };
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i]; var target = bytes.AsSpan(4 + 8 * i);
            BinaryPrimitives.WriteUInt16LittleEndian(target, entry.Tag); BinaryPrimitives.WriteUInt16LittleEndian(target[2..], entry.Permission); BinaryPrimitives.WriteUInt32LittleEndian(target[4..], entry.Id);
        }
        return bytes;
    }
    private static void Chmod(params string[] args)
    {
        var start = new ProcessStartInfo("/bin/chmod") { UseShellExecute = false, RedirectStandardError = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        if (!child.WaitForExit(10_000)) { child.Kill(); child.WaitForExit(); Assert.Fail("The owned chmod fixture did not exit."); }
        Assert.True(child.ExitCode == 0, child.StandardError.ReadToEnd());
    }
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenNative(string path, int flags);
    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEuid();
    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Link(string path, string alias);
    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int SetAcl(int fd, string name, byte[] value, nint size, int flags);
    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacSet(int fd, string name, byte[] value, nint size, uint position, int flags);
    [LibraryImport("libc", EntryPoint = "fchflags", SetLastError = true)]
    private static partial int Fchflags(int fd, uint flags);
    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static unsafe partial int Ioctl(int fd, nuint request, ulong* flags);
}
