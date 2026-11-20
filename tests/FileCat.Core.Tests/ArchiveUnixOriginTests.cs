using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

public sealed partial class ArchiveUnixOriginTests
{
    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void Private_editor_file_receives_exact_present_origin_without_rewriting_its_data(int kind)
    {
        RequireUnix(); using var root = new TempDir(); using var handle = Open(root.File("private-editor-file", "editor bytes"));
        byte[] value = kind switch { 0 => [], 1 => "https://example.test/é?q=a\0b"u8.ToArray(), _ => [0xff, 0, 0x80, 0xfe] };
        var expected = ArchiveOriginSnapshot.Present(Format, value); var before = UnixFiles.Stat(handle)!.Value;
        ArchiveUnixOrigin.CopyToNewWorkingFile(handle, expected, TestContext.Current.CancellationToken);
        Assert.Equal(expected.Digest, ArchiveUnixOrigin.Read(handle, TestContext.Current.CancellationToken).Digest);
        Assert.Equal(before.Identity, UnixFiles.Stat(handle)!.Value.Identity);
        Assert.Equal("editor bytes", File.ReadAllText(Path.Combine(root.Path, "private-editor-file")));
        ArchiveUnixOrigin.CopyToNewWorkingFile(handle, expected, TestContext.Current.CancellationToken); // Identical readback is idempotent.
    }

    [Fact]
    public void Private_editor_origin_does_not_overwrite_a_different_existing_mark()
    {
        RequireUnix(); using var root = new TempDir(); using var handle = Open(root.File("owned", "editor bytes"));
        Set(handle, "other origin"u8.ToArray());
        Assert.Throws<IOException>(() => ArchiveUnixOrigin.CopyToNewWorkingFile(handle,
            ArchiveOriginSnapshot.Present(Format, "wanted origin"u8), TestContext.Current.CancellationToken));
        Assert.Equal("other origin"u8.ToArray(), ArchiveUnixOrigin.Read(handle, TestContext.Current.CancellationToken).Bytes.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Native_present_values_preserve_empty_binary_and_unmodified_text_bytes(int kind)
    {
        RequireUnix(); using var root = new TempDir();
        using var handle = Open(root.File("owned", "payload"));
        byte[] value = kind switch { 0 => [], 1 => "https://example.test/é?q=a\0b"u8.ToArray(), _ => [0xff, 0, 0x80, 0xfe] };
        Set(handle, value);
        var before = UnixFiles.Stat(handle);
        var captured = ArchiveUnixOrigin.Read(handle, TestContext.Current.CancellationToken);
        Assert.Equal(ArchiveOriginState.Present, captured.State); Assert.Equal(Format, captured.Format);
        Assert.Equal(ArchiveOriginFailure.None, captured.Failure); Assert.Equal(value, captured.Bytes.ToArray());
        Assert.Equal(before, UnixFiles.Stat(handle));
        Assert.Equal(ArchiveOriginSnapshot.Present(Format, value).Digest, captured.Digest);
        if (value.Length > 0) { byte previous = value[0]; value[0] ^= 1; Assert.Equal(previous, captured.Bytes[0]); }
    }

    [Fact]
    public void Native_absence_is_distinct_from_present_empty_and_a_later_mark()
    {
        RequireUnix(); using var root = new TempDir();
        using var handle = Open(root.File("owned", "payload"));
        Assert.Equal(ArchiveOriginState.Absent, ArchiveUnixOrigin.Read(handle, TestContext.Current.CancellationToken).State);
        Set(handle, []);
        var empty = ArchiveUnixOrigin.Read(handle, TestContext.Current.CancellationToken); Assert.Equal(ArchiveOriginState.Present, empty.State); Assert.Empty(empty.Bytes.ToArray());
        Set(handle, "0081;12345678;FileCat;"u8.ToArray());
        var marked = ArchiveUnixOrigin.Read(handle, TestContext.Current.CancellationToken); Assert.Equal(ArchiveOriginState.Present, marked.State);
        Assert.NotEqual(empty.Digest, marked.Digest);
        Remove(handle); Assert.Equal(ArchiveOriginState.Absent, ArchiveUnixOrigin.Read(handle, TestContext.Current.CancellationToken).State);
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("symlink")]
    [InlineData("unlink")]
    public void Renamed_replaced_unlinked_or_redirected_names_cannot_retarget_the_held_origin(string route)
    {
        RequireUnix(); using var root = new TempDir();
        string path = root.File("selected", "original bytes"), other = root.File("other", "different bytes");
        using var held = Open(path); using var replacement = Open(other);
        byte[] originalMark = "original native mark"u8.ToArray();
        Set(held, originalMark); Set(replacement, "replacement native mark"u8.ToArray());
        var identity = UnixFiles.Stat(held)!.Value.Identity;
        File.Move(path, Path.Combine(root.Path, "retained"));
        if (route == "replace") File.Move(other, path);
        if (route == "symlink") File.CreateSymbolicLink(path, other);
        if (route == "unlink") File.Delete(Path.Combine(root.Path, "retained"));
        var observed = ArchiveUnixOrigin.Read(held, TestContext.Current.CancellationToken);
        Assert.Equal(ArchiveOriginState.Present, observed.State); Assert.Equal(originalMark, observed.Bytes.ToArray());
        Assert.Equal(identity, UnixFiles.Stat(held)!.Value.Identity);
        Assert.Equal("replacement native mark"u8.ToArray(), ArchiveUnixOrigin.Read(replacement, TestContext.Current.CancellationToken).Bytes.ToArray());
    }

    [Fact]
    public void Explicit_input_directories_have_their_own_native_origin_evidence()
    {
        RequireUnix(); using var root = new TempDir();
        int flags = OperatingSystem.IsMacOS() ? 0x01000000 | 0x00100000 | 0x100 | 4 : 0x80000 | 0x10000 | 0x20000 | 0x800;
        int fd = OpenNative(root.Path, flags); Assert.True(fd >= 0, "Open owned fixture directory");
        using var handle = new SafeFileHandle((nint)fd, ownsHandle: true);
        Assert.Equal(ArchiveOriginState.Absent, ArchiveUnixOrigin.Read(handle, TestContext.Current.CancellationToken).State);
        Set(handle, "directory provenance"u8.ToArray());
        var observed = ArchiveUnixOrigin.Read(handle, TestContext.Current.CancellationToken);
        Assert.Equal(ArchiveOriginState.Present, observed.State); Assert.Equal("directory provenance"u8.ToArray(), observed.Bytes.ToArray());
    }

    [Fact]
    public void Devices_do_not_become_apparently_unmarked_regular_inputs()
    {
        RequireUnix(); using var handle = File.OpenHandle("/dev/null", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var observed = ArchiveUnixOrigin.Read(handle, TestContext.Current.CancellationToken);
        Assert.Equal(ArchiveOriginState.Unavailable, observed.State); Assert.Equal(ArchiveOriginFailure.Unsupported, observed.Failure);
    }

    [Fact]
    public void Cancellation_and_closed_handles_never_produce_absence()
    {
        RequireUnix(); using var root = new TempDir();
        using var handle = Open(root.File("owned", "payload"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ArchiveUnixOrigin.Read(handle, cancellation.Token));
        handle.Dispose(); Assert.Throws<ObjectDisposedException>(() => ArchiveUnixOrigin.Read(handle, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Revoked_Linux_attribute_read_permission_is_unknown_even_when_the_mark_was_absent(bool marked)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Native Linux xattr permission semantics.");
        if (GetEuid() == 0) Assert.Skip("This case requires an ordinary Unix user.");
        using var root = new TempDir(); string path = root.File("owned", "payload");
        using var held = Open(path);
        if (marked) Set(held, "https://example.test/download"u8.ToArray());
        var mode = File.GetUnixFileMode(path);
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.None);
            var observed = ArchiveUnixOrigin.Read(held, TestContext.Current.CancellationToken);
            Assert.Equal(ArchiveOriginState.Unavailable, observed.State); Assert.Equal(ArchiveOriginFailure.AccessDenied, observed.Failure);
        }
        finally { File.SetUnixFileMode(path, mode); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_Mac_ACL_denial_of_attribute_reads_is_unknown_even_when_the_mark_was_absent(bool marked)
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("Native macOS readextattr ACL semantics.");
        using var root = new TempDir(); string path = root.File("owned", "payload");
        using var held = Open(path);
        if (marked) Set(held, "0081;12345678;FileCat;"u8.ToArray());
        try
        {
            Chmod("+a", "everyone deny readextattr", path);
            var observed = ArchiveUnixOrigin.Read(held, TestContext.Current.CancellationToken);
            Assert.Equal(ArchiveOriginState.Unavailable, observed.State); Assert.Equal(ArchiveOriginFailure.AccessDenied, observed.Failure);
        }
        finally { Chmod("-N", path); }
    }

    [Theory]
    [InlineData(ArchiveOriginSnapshot.MaxBytes)]
    [InlineData(ArchiveOriginSnapshot.MaxBytes + 1)]
    public void Native_APFS_quarantine_size_is_checked_before_copying_an_excessive_value(int length)
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("Native macOS large-xattr fixture.");
        using var root = new TempDir(); using var held = Open(root.File("owned", "payload"));
        byte[] mark = new byte[length]; Array.Fill(mark, (byte)'x'); Set(held, mark);
        var observed = ArchiveUnixOrigin.Read(held, TestContext.Current.CancellationToken);
        if (length == ArchiveOriginSnapshot.MaxBytes)
        { Assert.Equal(ArchiveOriginState.Present, observed.State); Assert.Equal(mark, observed.Bytes.ToArray()); }
        else
        { Assert.Equal(ArchiveOriginState.Unavailable, observed.State); Assert.Equal(ArchiveOriginFailure.TooLarge, observed.Failure); Assert.Empty(observed.Bytes.ToArray()); }
    }

    [Fact]
    public void Native_evidence_round_trips_in_the_complete_source_version_sidecar_without_text_conversion()
    {
        RequireUnix(); using var root = new TempDir(); byte[] content = "owned payload"u8.ToArray();
        string path = Path.Combine(root.Path, "owned"); File.WriteAllBytes(path, content);
        using var held = Open(path); byte[] mark = [0xff, 0, 0xfe, 0x80]; Set(held, mark);
        var version = new ArchiveObjectVersion(UnixFiles.Stat(held)!.Value.Identity.ToString(), content.Length, Convert.ToHexString(SHA256.HashData(content)));
        var scope = new ArchiveOriginEvidenceScope(Guid.NewGuid(), new(ArchiveWriteFormat.Tar), ArchiveOriginOperation.Create, 1);
        using var bytes = new MemoryStream(); ArchiveOriginEvidenceInfo info;
        using (var writer = new ArchiveOriginEvidenceWriter(bytes, scope, TestContext.Current.CancellationToken))
        {
            writer.Append(new(ArchiveOriginSourceKind.File, version, ArchiveUnixOrigin.Read(held, TestContext.Current.CancellationToken)));
            info = writer.Complete();
        }
        bytes.Position = 0;
        using var reader = new ArchiveOriginEvidenceReader(bytes, info, scope, TestContext.Current.CancellationToken);
        var record = Assert.IsType<ArchiveOriginEvidenceRecord>(reader.ReadNext());
        Assert.Equal(version, record.Source); Assert.Equal(Format, record.Origin.Format); Assert.Equal(mark, record.Origin.Bytes.ToArray());
        Assert.Null(reader.ReadNext()); reader.Complete();
        Assert.Equal(content, File.ReadAllBytes(path));
    }

    [Fact]
    public void Windows_cannot_accidentally_dispatch_a_Unix_origin_reader()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows platform dispatch refusal.");
        using var invalid = new SafeFileHandle((nint)(-1), ownsHandle: false);
        Assert.Throws<PlatformNotSupportedException>(() => ArchiveUnixOrigin.Read(invalid, TestContext.Current.CancellationToken));
    }

    private static ArchiveOriginFormat Format => OperatingSystem.IsMacOS() ? ArchiveOriginFormat.MacQuarantine : ArchiveOriginFormat.LinuxOriginUrl;
    private static string Attribute => OperatingSystem.IsMacOS() ? UnixFileOperations.QuarantineAttribute : UnixFileOperations.OriginAttribute;
    private static void RequireUnix() { if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Native Unix origin attributes."); }
    private static SafeFileHandle Open(string path) => File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
    private static void Set(SafeFileHandle handle, byte[] bytes)
    {
        int fd = checked((int)handle.DangerousGetHandle());
        int result = OperatingSystem.IsMacOS() ? MacSet(fd, Attribute, bytes, bytes.Length, 0, 0) : LinuxSet(fd, Attribute, bytes, bytes.Length, 0);
        Assert.True(result == 0, "Set owned fixture xattr; errno " + Marshal.GetLastPInvokeError());
    }
    private static void Remove(SafeFileHandle handle)
    {
        int fd = checked((int)handle.DangerousGetHandle());
        int result = OperatingSystem.IsMacOS() ? MacRemove(fd, Attribute, 0) : LinuxRemove(fd, Attribute);
        Assert.True(result == 0, "Remove owned fixture xattr; errno " + Marshal.GetLastPInvokeError());
    }

    private static void Chmod(params string[] arguments)
    {
        var start = new ProcessStartInfo("/bin/chmod") { UseShellExecute = false, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        if (!process.WaitForExit(10_000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); Assert.Fail("The owned chmod fixture did not exit."); }
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenNative(string path, int flags);
    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEuid();
    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxSet(int fd, string name, byte[] bytes, nint size, int flags);
    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacSet(int fd, string name, byte[] bytes, nint size, uint position, int flags);
    [LibraryImport("libc", EntryPoint = "fremovexattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxRemove(int fd, string name);
    [LibraryImport("libc", EntryPoint = "fremovexattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacRemove(int fd, string name, int options);
}
