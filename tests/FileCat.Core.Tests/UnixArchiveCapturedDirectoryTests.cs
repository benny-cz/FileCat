using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Tests;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Fact]
    public void Directory_capture_preserves_and_rereviews_every_native_timestamp_nanosecond()
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temporary = new TempDir(); string root = Canonical(temporary.Path);
        string path = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
        ArchiveNativeTimestampFixture.SetModified(path, -1, 999999999);
        using var directory = UnixArchiveDirectory.Open(root); using var captured = new ArchiveCapturedInputs();
        directory.CaptureSourceDirectory(captured, 1, "folder", ct); captured.Seal(ct); var binding = captured.Directories[1];
        Assert.Equal(new ArchiveTimestamp(-1, 999999999), binding.Reviewed.Modified); binding.Review(ct);
        ArchiveNativeTimestampFixture.SetModified(path, -1, 999999998);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => binding.Review(ct)).Reason);
    }

    [Fact]
    public void Directory_capture_keeps_native_identity_time_ordinary_mode_and_no_per_folder_handle()
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temporary = new TempDir(); string root = Canonical(temporary.Path);
        var modified = DateTime.UnixEpoch.AddSeconds(1700000000).AddTicks(1234567);
        using var directory = UnixArchiveDirectory.Open(root); using var captured = new ArchiveCapturedInputs();
        for (int id = 1; id <= 256; id++)
        {
            string leaf = "folder-" + id; string path = Directory.CreateDirectory(Path.Combine(root, leaf)).FullName;
            File.SetUnixFileMode(path, (UnixFileMode)0x3d9); Directory.SetLastWriteTimeUtc(path, modified);
            directory.CaptureSourceDirectory(captured, id, leaf, ct);
        }
        captured.Seal(ct); Assert.Equal(256, captured.Directories.Count); Assert.Empty(captured.Bindings);
        foreach (var binding in captured.Directories.Values)
        {
            Assert.Equal(0x1d9, binding.Reviewed.Mode); Assert.Equal((ArchiveTimestamp)new DateTimeOffset(modified), binding.Reviewed.Modified);
            Assert.Equal(ArchiveOriginState.Absent, binding.Reviewed.Origin.State); binding.Review(ct);
        }
        Payload(Path.Combine(root, "file"), [1]); var files = new List<UnixArchiveFile>();
        try { for (int id = 1; id <= 16; id++) files.Add(directory.OpenSource("file")); }
        finally { foreach (var file in files) file.Dispose(); }
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("time")]
    [InlineData("replacement")]
    [InlineData("file")]
    [InlineData("symlink")]
    [InlineData("parent")]
    [InlineData("child")]
    [InlineData("origin")]
    public void Directory_capture_review_refuses_native_metadata_identity_type_and_namespace_changes(string change)
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temporary = new TempDir(); string root = Canonical(temporary.Path);
        string path = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
        using var directory = UnixArchiveDirectory.Open(root); using var captured = new ArchiveCapturedInputs();
        directory.CaptureSourceDirectory(captured, 1, "folder", ct); captured.Seal(ct); var binding = captured.Directories[1]; binding.Review(ct);
        if (change == "mode") File.SetUnixFileMode(path, (UnixFileMode)0x1c0);
        if (change == "time") Directory.SetLastWriteTimeUtc(path, DateTime.UnixEpoch);
        if (change is "replacement" or "file" or "symlink")
        {
            Directory.Move(path, Path.Combine(root, "retained-folder"));
            if (change == "replacement") Directory.CreateDirectory(path);
            else if (change == "file") File.WriteAllBytes(path, [1]);
            else Directory.CreateSymbolicLink(path, Path.Combine(root, "retained-folder"));
        }
        if (change == "child")
        { var modified = Directory.GetLastWriteTimeUtc(path); File.WriteAllBytes(Path.Combine(path, "new-child"), [1]); Directory.SetLastWriteTimeUtc(path, modified); }
        if (change == "origin") SetDirectoryOrigin(path, [0xff, 0, 0x80]);
        string moved = root + "-moved"; if (change == "parent") Directory.Move(root, moved);
        try { Assert.ThrowsAny<IOException>(() => binding.Review(ct)); }
        finally { if (change == "parent") Directory.Move(moved, root); }
    }

    [Fact]
    public void Directory_capture_keeps_opaque_native_origin_and_checks_the_native_object_kind()
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temporary = new TempDir(); string root = Canonical(temporary.Path);
        string path = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName; SetDirectoryOrigin(path, [0xff, 0, 0x80]);
        Payload(Path.Combine(root, "file"), [1]); Directory.CreateSymbolicLink(Path.Combine(root, "link"), path);
        using var directory = UnixArchiveDirectory.Open(root); using var captured = new ArchiveCapturedInputs();
        directory.CaptureSourceDirectory(captured, 1, "folder", ct); captured.Seal(ct);
        var binding = captured.Directories[1]; Assert.Equal(ArchiveOriginState.Present, binding.Reviewed.Origin.State);
        Assert.Equal(new byte[] { 0xff, 0, 0x80 }, binding.Reviewed.Origin.Bytes.ToArray()); binding.Review(ct);
        foreach (string leaf in new[] { "file", "link" })
        { using var bad = new ArchiveCapturedInputs(); Assert.ThrowsAny<IOException>(() => directory.CaptureSourceDirectory(bad, 1, leaf, ct)); }
        using var held = UnixArchiveNative.ChildDirectory(directory.Handle, "folder");
        Assert.Throws<ArchiveWriteException>(() => ArchiveUnixOuterMetadata.Capture(held, ct));
        using var file = UnixArchiveNative.Child(directory.Handle, "file");
        Assert.Throws<ArchiveWriteException>(() => ArchiveUnixOuterMetadata.CaptureDirectory(file, ct));
    }

    [Fact]
    public void Directory_capture_does_not_turn_an_unreadable_source_into_an_unmarked_folder()
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temporary = new TempDir(); string root = Canonical(temporary.Path);
        string path = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
        using var directory = UnixArchiveDirectory.Open(root); using var captured = new ArchiveCapturedInputs();
        File.SetUnixFileMode(path, 0);
        try
        {
            Assert.ThrowsAny<IOException>(() => directory.CaptureSourceDirectory(captured, 1, "folder", ct));
            Assert.Throws<ArchiveWriteException>(() => captured.Seal(ct));
        }
        finally { File.SetUnixFileMode(path, (UnixFileMode)0x1c0); }
    }

    [Fact]
    public void Directory_capture_refuses_oversized_native_Mac_quarantine_before_copying_its_value()
    {
        Native(); if (!OperatingSystem.IsMacOS()) Assert.Skip("APFS permits origin attributes beyond the 64 KiB capture bound.");
        var ct = TestContext.Current.CancellationToken; using var temporary = new TempDir(); string root = Canonical(temporary.Path);
        string path = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
        SetDirectoryOrigin(path, new byte[ArchiveOriginSnapshot.MaxBytes + 1]);
        using var directory = UnixArchiveDirectory.Open(root); using var captured = new ArchiveCapturedInputs();
        Assert.Throws<ArchiveWriteException>(() => directory.CaptureSourceDirectory(captured, 1, "folder", ct));
        Assert.Throws<ArchiveWriteException>(() => captured.Seal(ct));
    }

    private static void SetDirectoryOrigin(string path, byte[] bytes)
    {
        int result = OperatingSystem.IsMacOS() ? MacSetDirectoryOrigin(path, "com.apple.quarantine", bytes, bytes.Length, 0, 0)
            : LinuxSetDirectoryOrigin(path, "user.xdg.origin.url", bytes, bytes.Length, 0);
        Assert.Equal(0, result);
    }
    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxSetDirectoryOrigin(string path, string name, byte[] bytes, nint length, int flags);
    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacSetDirectoryOrigin(string path, string name, byte[] bytes, nint length, uint position, int flags);
}
