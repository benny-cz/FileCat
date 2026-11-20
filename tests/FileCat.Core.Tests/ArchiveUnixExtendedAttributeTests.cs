using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using static FileCat.Core.Archives.ArchiveUnixExtendedAttributes;
using NativeAttribute = FileCat.Core.Archives.ArchiveUnixExtendedAttributes.Attribute;

namespace FileCat.Core.Tests;

public sealed partial class ArchiveUnixExtendedAttributeTests
{
    [Fact]
    public void Canonical_inventory_is_ordered_frozen_and_binary_safe()
    {
        byte[] value = [0, 0xff, 7]; var items = new[] { new NativeAttribute("a", []), new NativeAttribute("é", value) };
        var snapshot = new ArchiveUnixExtendedAttributes(items); string digest = snapshot.Digest; value[1] = 0; items[0] = new("different", [1]);
        Assert.Equal(new[] { "a", "é" }, snapshot.Attributes.Select(a => a.Name)); Assert.Empty(snapshot.Attributes[0].Value.ToArray());
        Assert.Equal(new byte[] { 0, 0xff, 7 }, snapshot.Attributes[1].Value.ToArray());
        Assert.Equal(digest, new ArchiveUnixExtendedAttributes([new("a", []), new("é", [0, 0xff, 7])]).Digest);
        Assert.NotEqual(digest, new ArchiveUnixExtendedAttributes([new("a", [0]), new("é", [0, 0xff, 7])]).Digest);
        Assert.Throws<ArchiveWriteException>(() => new ArchiveUnixExtendedAttributes([new("b", []), new("a", [])]));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveUnixExtendedAttributes([new("a", []), new("a", [])]));
    }

    [Theory]
    [InlineData("unterminated")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("utf8")]
    [InlineData("name-length")]
    [InlineData("list-length")]
    [InlineData("count")]
    public void Native_name_lists_refuse_bad_framing_or_counts_before_large_object_allocation(string kind)
    {
        byte[] bytes = kind switch
        {
            "unterminated" => "a"u8.ToArray(), "empty" => [0], "duplicate" => "a\0a\0"u8.ToArray(), "utf8" => [0xff, 0],
            "name-length" => [.. Enumerable.Repeat((byte)'a', 256), 0], "list-length" => new byte[MaxNamesBytes + 1],
            _ => Enumerable.Range(0, MaxAttributes + 1).SelectMany(i => Encoding.ASCII.GetBytes("a" + i + "\0")).ToArray(),
        };
        long before = GC.GetAllocatedBytesForCurrentThread(); Assert.Throws<ArchiveWriteException>(() => ParseNames(bytes));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 256 << 10);
    }

    [Fact]
    public void Unordered_native_names_are_canonicalized_without_normalization()
    {
        Assert.Equal(new[] { "a", "e\u0301", "z", "é" }, ParseNames(Encoding.UTF8.GetBytes("é\0z\0e\u0301\0a\0")));
        Assert.Empty(ParseNames([]));
    }

    [Fact]
    public void Complete_encoded_inventory_bound_applies_across_values_and_name_records()
    {
        byte[] value = new byte[MaxBytes / 2];
        Assert.Throws<ArchiveWriteException>(() => new ArchiveUnixExtendedAttributes([new("a", value), new("b", value)]));
        Assert.Throws<ArchiveWriteException>(() => new NativeAttribute("a", new byte[MaxBytes + 1]));
        Assert.Throws<ArchiveWriteException>(() => new NativeAttribute("bad\0name", []));
        Assert.Throws<ArchiveWriteException>(() => new NativeAttribute("\ud800", []));
    }

    [Fact]
    public void Missing_inventory_is_not_an_observed_empty_inventory_and_origin_must_agree()
    {
        var facts = new ArchiveUnixOuterMetadata.Facts(ArchiveUnixOuterMetadata.NativeFormat.Linux, 1, 2, 0, 1000, 1000, 0x180, 1, default, default, null, 0, 0, 0);
        var absent = ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.LinuxOriginUrl);
        var missing = new ArchiveUnixOuterMetadata(facts, false, [], absent);
        var empty = new ArchiveUnixOuterMetadata(facts, false, [], absent, new([])); Assert.NotEqual(missing.Digest, empty.Digest);
        Assert.Null(missing.ExtendedAttributes); Assert.Empty(empty.ExtendedAttributes!.Attributes);
        Assert.Throws<ArchiveWriteException>(missing.RequireSupportedExtendedAttributes); empty.RequireSupportedExtendedAttributes();
        var custom = new ArchiveUnixOuterMetadata(facts, false, [], absent, new([new("user.custom", [1])]));
        var changed = new ArchiveUnixOuterMetadata(facts, false, [], absent, new([new("user.custom", [2])]));
        Assert.NotEqual(custom.Digest, changed.Digest);
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(custom.RequireSupportedExtendedAttributes).Reason);
        var mark = new ArchiveUnixExtendedAttributes([new("user.xdg.origin.url", [])]);
        Assert.True(mark.MatchesOrigin(ArchiveOriginSnapshot.Present(ArchiveOriginFormat.LinuxOriginUrl, []))); Assert.False(mark.MatchesOrigin(absent));
        Assert.Throws<ArgumentException>(() => new ArchiveUnixOuterMetadata(facts, false, [], absent, mark));
        Assert.Throws<ArgumentException>(() => new ArchiveUnixOuterMetadata(facts, true, [1], absent, new([])));
        Assert.Throws<ArgumentException>(() => new ArchiveUnixOuterMetadata(facts, false, [], absent, new([new("system.posix_acl_access", [1])])));
    }

    [Fact]
    public void Origin_exclusion_does_not_hide_another_changed_attribute_or_its_presence()
    {
        var before = new ArchiveUnixExtendedAttributes([new("a", [1]), new("origin", [1])]);
        Assert.True(before.MatchesExcept(new([new("a", [1]), new("origin", [9])]), "origin"));
        Assert.False(before.MatchesExcept(new([new("a", [2]), new("origin", [9])]), "origin"));
        Assert.False(before.MatchesExcept(new([new("origin", [9])]), "origin"));
    }

    [Fact]
    public async Task Held_native_binary_empty_and_Unicode_attributes_enter_outer_review_and_survive_origin_copy_and_rename()
    {
        Native(); string? observer = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_XATTR_OBSERVER");
        if (string.IsNullOrEmpty(observer)) Assert.Skip("Explicit independently compiled C xattr observer required.");
        using var root = new TempDir(); string directoryPath = UnixFiles.RealPath(root.Path)!;
        using var directory = UnixArchiveDirectory.Open(directoryPath); using var file = directory.CreateArtifact(Guid.NewGuid(), UnixArchiveArtifactKind.Stage, 1024);
        file.Write("owned"u8); var version = file.Seal(TestContext.Current.CancellationToken);
        string prefix = OperatingSystem.IsLinux() ? "user.filecat-archive-" : "filecat-archive-";
        Set(file.DisplayPath, prefix + "binary", [0, 0xff, 1]); Set(file.DisplayPath, prefix + "empty", []); Set(file.DisplayPath, prefix + "é", [7]);
        var before = file.CaptureOuterMetadata(TestContext.Current.CancellationToken); Assert.NotNull(before.ExtendedAttributes);
        Assert.Equal(new byte[] { 0, 0xff, 1 }, Assert.Single(before.ExtendedAttributes.Attributes, a => a.Name == prefix + "binary").Value.ToArray());
        Assert.Empty(Assert.Single(before.ExtendedAttributes.Attributes, a => a.Name == prefix + "empty").Value.ToArray());
        await ObserveIndependently(before, file.DisplayPath, observer!);
        var format = OperatingSystem.IsLinux() ? ArchiveOriginFormat.LinuxOriginUrl : ArchiveOriginFormat.MacQuarantine;
        var origin = ArchiveOriginSnapshot.Present(format, "raw origin"u8); file.CopyOriginNoReplace(origin, TestContext.Current.CancellationToken);
        var marked = file.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        Assert.True(before.ExtendedAttributes.MatchesExcept(marked.ExtendedAttributes!, OperatingSystem.IsLinux() ? "user.xdg.origin.url" : "com.apple.quarantine"));
        Assert.True(marked.ExtendedAttributes!.MatchesOrigin(origin)); file.RenameNoReplace("published.tar", version, marked, TestContext.Current.CancellationToken);
        var published = file.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        Assert.Equal(marked.ExtendedAttributes.Digest, published.ExtendedAttributes!.Digest); await ObserveIndependently(published, file.DisplayPath, observer!);
    }

    private static async Task ObserveIndependently(ArchiveUnixOuterMetadata expected, string path, string executable)
    {
        Assert.True(Path.IsPathFullyQualified(executable));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }; start.ArgumentList.Add(path);
        using var process = Process.Start(start)!;
        try
        {
            string output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            string error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken); await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(process.ExitCode == 0, error); Assert.InRange(output.Length, 1, 3 * MaxBytes);
            string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("FCXOBS1 " + expected.Native.Device.ToString("X16") + " " + expected.Native.Inode.ToString("X16"), lines[0]);
            var attributes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string line in lines.Skip(1))
            { string[] fields = line.Split(':'); Assert.Equal(2, fields.Length); attributes.Add(new UTF8Encoding(false, true).GetString(Convert.FromHexString(fields[0])), Convert.FromHexString(fields[1])); }
            Assert.Equal(expected.ExtendedAttributes!.Attributes.Count, attributes.Count);
            foreach (var attribute in expected.ExtendedAttributes.Attributes) Assert.Equal(attribute.Value.ToArray(), attributes[attribute.Name]);
            TestContext.Current.TestOutputHelper?.WriteLine(output.Trim());
        }
        finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); } }
    }

    [Fact]
    public void Changed_or_removed_unknown_attributes_invalidate_retained_input_review()
    {
        Native(); using var root = new TempDir(); string directoryPath = UnixFiles.RealPath(root.Path)!, path = Path.Combine(directoryPath, "input"); File.WriteAllBytes(path, [1]);
        string name = OperatingSystem.IsLinux() ? "user.filecat-archive-review" : "filecat-archive-review"; Set(path, name, [1]);
        using var directory = UnixArchiveDirectory.Open(directoryPath); using var file = directory.OpenSource("input"); var state = ((IArchiveRetainedFile)file).Observe(TestContext.Current.CancellationToken);
        Set(path, name, [2]); var changed = ((IArchiveRetainedFile)file).Observe(TestContext.Current.CancellationToken);
        Assert.NotEqual(state.MetadataDigest, changed.MetadataDigest); Assert.Equal(state.Version, changed.Version);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => ((IArchiveRetainedFile)file).Review(state, TestContext.Current.CancellationToken)).Reason);
        Remove(path, name); Assert.NotEqual(changed.MetadataDigest, ((IArchiveRetainedFile)file).Observe(TestContext.Current.CancellationToken).MetadataDigest);
    }

    [Fact]
    public void Mac_oversize_native_value_refuses_before_allocating_the_value_buffer()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("APFS supports the large native xattr fixture.");
        using var root = new TempDir(); string directoryPath = UnixFiles.RealPath(root.Path)!, path = Path.Combine(directoryPath, "input"); File.WriteAllBytes(path, [1]);
        Set(path, "filecat-archive-oversize", new byte[MaxBytes]); using var directory = UnixArchiveDirectory.Open(directoryPath); using var file = directory.OpenSource("input");
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => file.CaptureOuterMetadata(TestContext.Current.CancellationToken)).Reason);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 512 << 10);
    }

    [Fact]
    public void Linux_invalid_native_UTF8_name_is_refused_without_replacement_decoding()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux raw native xattr-name fixture.");
        using var root = new TempDir(); string directoryPath = UnixFiles.RealPath(root.Path)!, path = Path.Combine(directoryPath, "input"); File.WriteAllBytes(path, [1]);
        Assert.Equal(0, LinuxSetRaw(path, [.. "user.filecat-archive-"u8.ToArray(), 0xff, 0], [1], 1, 0));
        using var directory = UnixArchiveDirectory.Open(directoryPath); using var file = directory.OpenSource("input");
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() => file.CaptureOuterMetadata(TestContext.Current.CancellationToken)).Reason);
    }

    private static void Native() { if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Native Unix xattrs."); }
    private static void Set(string path, string name, byte[] value)
    { Assert.Equal(0, OperatingSystem.IsMacOS() ? MacSet(path, name, value, value.Length, 0, 0) : LinuxSet(path, name, value, value.Length, 0)); }
    private static void Remove(string path, string name)
    { Assert.Equal(0, OperatingSystem.IsMacOS() ? MacRemove(path, name, 0) : LinuxRemove(path, name)); }
    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxSet(string path, string name, byte[] value, nint size, int flags);
    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxSetRaw(string path, byte[] name, byte[] value, nint size, int flags);
    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacSet(string path, string name, byte[] value, nint size, uint position, int options);
    [LibraryImport("libc", EntryPoint = "removexattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxRemove(string path, string name);
    [LibraryImport("libc", EntryPoint = "removexattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacRemove(string path, string name, int options);
}
