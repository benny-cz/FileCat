using FileCat.Core.Archives;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
{
    [Fact]
    public void Directory_capture_keeps_native_identity_time_mode_and_no_per_folder_handle()
    {
        RequireWindows(); var ct = TestContext.Current.CancellationToken; using var fixture = new Fixture();
        var modified = DateTime.UnixEpoch.AddSeconds(1700000000).AddTicks(1234567);
        using var directory = WindowsArchiveDirectory.Open(fixture.Root); using var captured = new ArchiveCapturedInputs();
        for (int id = 1; id <= 256; id++)
        {
            string leaf = "folder-" + id; string path = Directory.CreateDirectory(fixture.PathOf(leaf)).FullName;
            Directory.SetLastWriteTimeUtc(path, modified); directory.CaptureSourceDirectory(captured, id, leaf, ct);
        }
        captured.Seal(ct); Assert.Equal(256, captured.Directories.Count); Assert.Empty(captured.Bindings);
        foreach (var binding in captured.Directories.Values)
        {
            Assert.Equal(0x1ed, binding.Reviewed.Mode); Assert.Equal((ArchiveTimestamp)new DateTimeOffset(modified), binding.Reviewed.Modified);
            Assert.Equal(ArchiveOriginState.Absent, binding.Reviewed.Origin.State); binding.Review(ct);
        }
        File.WriteAllBytes(fixture.PathOf("file"), [1]); var files = new List<WindowsArchiveFile>();
        try { for (int id = 1; id <= 16; id++) files.Add(directory.OpenSource("file")); }
        finally { foreach (var file in files) file.Dispose(); }
    }

    [Theory]
    [InlineData("attributes")]
    [InlineData("time")]
    [InlineData("replacement")]
    [InlineData("file")]
    [InlineData("symlink")]
    [InlineData("child")]
    [InlineData("origin")]
    public void Directory_capture_review_refuses_native_metadata_identity_type_and_namespace_changes(string change)
    {
        RequireWindows(); var ct = TestContext.Current.CancellationToken; using var fixture = new Fixture();
        string path = Directory.CreateDirectory(fixture.PathOf("folder")).FullName;
        using var directory = WindowsArchiveDirectory.Open(fixture.Root); using var captured = new ArchiveCapturedInputs();
        directory.CaptureSourceDirectory(captured, 1, "folder", ct); captured.Seal(ct); var binding = captured.Directories[1]; binding.Review(ct);
        if (change == "attributes") File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        if (change == "time") Directory.SetLastWriteTimeUtc(path, DateTime.UnixEpoch);
        if (change is "replacement" or "file" or "symlink")
        {
            Directory.Move(path, fixture.PathOf("retained-folder"));
            if (change == "replacement") Directory.CreateDirectory(path);
            else if (change == "file") File.WriteAllBytes(path, [1]);
            else Directory.CreateSymbolicLink(path, fixture.PathOf("retained-folder"));
        }
        if (change == "child")
        { var modified = Directory.GetLastWriteTimeUtc(path); File.WriteAllBytes(Path.Combine(path, "new-child"), [1]); Directory.SetLastWriteTimeUtc(path, modified); }
        if (change == "origin") File.WriteAllBytes(path + ":Zone.Identifier", [0xff, 0, 0x80]);
        try { Assert.ThrowsAny<IOException>(() => binding.Review(ct)); }
        finally
        {
            if (change == "attributes") File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }

    [Fact]
    public void Directory_capture_pins_the_parent_namespace_and_stops_review_after_owner_disposal()
    {
        RequireWindows(); var ct = TestContext.Current.CancellationToken; using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.PathOf("folder"));
        using var directory = WindowsArchiveDirectory.Open(fixture.Root); using var captured = new ArchiveCapturedInputs();
        directory.CaptureSourceDirectory(captured, 1, "folder", ct); captured.Seal(ct); var binding = captured.Directories[1];
        string moved = fixture.Root + "-moved";
        Assert.ThrowsAny<IOException>(() => Directory.Move(fixture.Root, moved));
        Assert.True(Directory.Exists(fixture.Root)); Assert.False(Directory.Exists(moved)); binding.Review(ct);
        directory.Dispose(); Directory.Move(fixture.Root, moved);
        try { Assert.Throws<ObjectDisposedException>(() => binding.Review(ct)); }
        finally { Directory.Move(moved, fixture.Root); }
    }

    [Fact]
    public void Directory_capture_keeps_opaque_folder_ADS_origin_and_rejects_regular_files_and_links()
    {
        RequireWindows(); var ct = TestContext.Current.CancellationToken; using var fixture = new Fixture();
        string path = Directory.CreateDirectory(fixture.PathOf("folder")).FullName;
        File.WriteAllBytes(path + ":Zone.Identifier", [0xff, 0, 0x80]);
        File.WriteAllBytes(fixture.PathOf("file"), [1]); Directory.CreateSymbolicLink(fixture.PathOf("link"), path);
        using var directory = WindowsArchiveDirectory.Open(fixture.Root); using var captured = new ArchiveCapturedInputs();
        directory.CaptureSourceDirectory(captured, 1, "folder", ct); captured.Seal(ct); var binding = captured.Directories[1];
        Assert.Equal(ArchiveOriginState.Present, binding.Reviewed.Origin.State);
        Assert.Equal(new byte[] { 0xff, 0, 0x80 }, binding.Reviewed.Origin.Bytes.ToArray()); binding.Review(ct);
        foreach (string leaf in new[] { "file", "link" })
        { using var bad = new ArchiveCapturedInputs(); Assert.ThrowsAny<IOException>(() => directory.CaptureSourceDirectory(bad, 1, leaf, ct)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Directory_capture_never_treats_locked_or_oversized_origin_as_absent(bool oversized)
    {
        RequireWindows(); var ct = TestContext.Current.CancellationToken; using var fixture = new Fixture();
        string path = Directory.CreateDirectory(fixture.PathOf("folder")).FullName;
        File.WriteAllBytes(path + ":Zone.Identifier", [1]);
        using var directory = WindowsArchiveDirectory.Open(fixture.Root); using var captured = new ArchiveCapturedInputs();
        using (var stream = new FileStream(path + ":Zone.Identifier", FileMode.Open, FileAccess.ReadWrite,
            oversized ? FileShare.ReadWrite | FileShare.Delete : FileShare.None))
        {
            if (oversized) stream.SetLength(ArchiveOriginSnapshot.MaxBytes + 1L);
            Assert.ThrowsAny<IOException>(() => directory.CaptureSourceDirectory(captured, 1, "folder", ct));
            Assert.Throws<ArchiveWriteException>(() => captured.Seal(ct));
        }
        File.WriteAllBytes(path + ":Zone.Identifier", [1]); using var retry = new ArchiveCapturedInputs();
        directory.CaptureSourceDirectory(retry, 1, "folder", ct); retry.Seal(ct); retry.Directories[1].Review(ct);
        Assert.Equal(ArchiveOriginState.Present, retry.Directories[1].Reviewed.Origin.State);
    }

    [Fact]
    public void Directory_capture_reports_unrepresentable_native_time_as_a_typed_refusal()
    {
        RequireWindows(); var ct = TestContext.Current.CancellationToken; using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.PathOf("folder"));
        using var directory = WindowsArchiveDirectory.Open(fixture.Root); using var captured = new ArchiveCapturedInputs();
        using var held = FileCat.Platform.Windows.Elevation.NtFile.OpenOrThrow(directory.Handle, "folder",
            0x80 | 0x100 | 0x100000, 7, 1, 1 | 0x200000, "Set owned folder timestamp fixture");
        var original = FileCat.Platform.Windows.Elevation.NtFile.GetBasic(held); var changed = original;
        changed.LastWriteTime = long.MaxValue;
        try
        {
            FileCat.Platform.Windows.Elevation.NtFile.SetBasic(held, changed);
            Assert.Equal(long.MaxValue, FileCat.Platform.Windows.Elevation.NtFile.GetBasic(held).LastWriteTime);
            var failure = Assert.Throws<ArchiveWriteException>(() => directory.CaptureSourceDirectory(captured, 1, "folder", ct));
            Assert.Equal(ArchiveWriteReason.UnsupportedFeature, failure.Reason);
        }
        finally { FileCat.Platform.Windows.Elevation.NtFile.SetBasic(held, original); }
    }
}
