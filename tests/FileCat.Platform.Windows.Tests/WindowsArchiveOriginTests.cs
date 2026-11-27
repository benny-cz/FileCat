using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
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
        stage.RenameNoReplace("retained.tar", version, stage.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
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
    public void A_successfully_renamed_new_artifact_cannot_receive_late_origin_writes()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root); using var stage = Stage(root);
        stage.Write([1, 2, 3]); var version = stage.Seal(TestContext.Current.CancellationToken);
        stage.RenameNoReplace("published.tar", version, stage.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        foreach (var origin in new[] { ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier),
                     ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, "[ZoneTransfer]\r\nZoneId=3\r\n"u8) })
            Assert.Throws<InvalidOperationException>(() => stage.CopyOriginNoReplace(origin, TestContext.Current.CancellationToken));
        Assert.Equal(ArchiveOriginState.Absent, stage.ReadOrigin(TestContext.Current.CancellationToken).State);
        Assert.Equal(version, stage.CaptureVersion(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_source_origin_is_read_again_after_its_held_object_moves_and_a_new_input_mark_is_not_hidden_by_unchanged_bytes()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        string path = fixture.PathOf("input.bin"); File.WriteAllBytes(path, [1, 2, 3]);
        using var input = root.OpenReviewedTarget("input.bin"); var version = input.CaptureVersion(TestContext.Current.CancellationToken);
        var before = input.ReadOrigin(TestContext.Current.CancellationToken); Assert.Equal(ArchiveOriginState.Absent, before.State);
        input.RenameNoReplace("retired.bin", version, input.CaptureOuterMetadata(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        File.WriteAllBytes(path, [9]);
        PutMark(fixture.PathOf("retired.bin"), "[ZoneTransfer]\r\nZoneId=4\r\n"u8);
        Assert.Equal(version, input.CaptureVersion(TestContext.Current.CancellationToken));
        Assert.Equal(ArchiveOriginState.Present, input.ReadOrigin(TestContext.Current.CancellationToken).State);
        Assert.NotEqual(before.Digest, input.ReadOrigin(TestContext.Current.CancellationToken).Digest);
        Assert.False(File.Exists(path + ":Zone.Identifier"));
    }

    [Fact]
    public void A_raised_declared_zone_retains_url_evidence_when_copied_to_the_native_owned_stage()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        string path = fixture.PathOf("source.bin"); File.WriteAllBytes(path, [1, 2, 3]);
        const string original = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/original\r\n";
        PutMark(path, Encoding.UTF8.GetBytes(original));
        using var source = root.OpenSource("source.bin");
        var raised = ArchiveWindowsOriginZone.Parse(source.ReadOrigin(TestContext.Current.CancellationToken)).RaiseTo(4);
        using var stage = Stage(root); source.CopyTo(stage); var version = stage.Seal(TestContext.Current.CancellationToken);
        stage.CopyOriginNoReplace(raised, TestContext.Current.CancellationToken);
        Assert.Equal(original.Replace("ZoneId=3", "ZoneId=4"), Encoding.UTF8.GetString(stage.ReadOrigin(TestContext.Current.CancellationToken).Bytes));
        Assert.Equal(3, ArchiveWindowsOriginZone.Parse(source.ReadOrigin(TestContext.Current.CancellationToken)).DeclaredZone);
        Assert.Equal(source.CaptureVersion(TestContext.Current.CancellationToken).Sha256, version.Sha256);
    }

    [Theory]
    [InlineData("utf8")][InlineData("utf8-bom")][InlineData("utf16-le")][InlineData("utf16-be")]
    public void Windows_system_zone_identifier_is_an_independent_encoding_oracle(string format)
    {
        RequireWindows(); using var fixture = new Fixture();
        string path = fixture.PathOf("oracle.bin"); File.WriteAllBytes(path, [1]);
        Encoding encoding = format switch { "utf8" => new UTF8Encoding(false, true), "utf8-bom" => new UTF8Encoding(true, true),
            "utf16-le" => new UnicodeEncoding(false, true, true), _ => new UnicodeEncoding(true, true, true) };
        byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\n")).ToArray(); PutMark(path, bytes);
        // Test-only oracle, not a production COM/parser dependency. IID is from SDK urlmon.h; CLSID is the installed
        // Windows PersistentZoneIdentifier. No policy is changed, no URL is mapped and no Save/SetId is called.
        uint ReadNative()
        {
            object identifier = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("0968e258-16c7-4dba-aa86-462dd61e31a3"), throwOnError: true)!)!;
            try
            {
                ((IPersistFile)identifier).Load(path, 0 /* STGM_READ */);
                int result = ((IZoneIdentifier)identifier).GetId(out uint zone);
                Assert.True(result >= 0, $"{format}: GetId HRESULT 0x{result:X8}"); return zone;
            }
            finally { Marshal.FinalReleaseComObject(identifier); }
        }
        if (format is "utf8-bom" or "utf16-be")
        {
            Assert.Throws<UnauthorizedAccessException>(() => ReadNative()); // Observed Windows 11 native refusal, not absence.
            Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() => ArchiveWindowsOriginZone.Parse(
                ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, bytes))).Reason);
            return;
        }
        Assert.Equal(3u, ReadNative());
        var parsed = ArchiveWindowsOriginZone.Parse(ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, bytes));
        PutMark(path, parsed.RaiseTo(4).Bytes);
        Assert.Equal(4u, ReadNative()); // Fresh COM object; never reuse a cached original declaration.
    }

    // I688: browsers end the mark with a NUL. Every Chromium download on the release session's host had the first shape,
    // and FileCat refused it, so no archive could be made of downloaded files. Windows' own reader takes all three as zone 3.
    [Theory]
    [InlineData("browser")][InlineData("crlf-nul")][InlineData("utf16-le-nul")]
    public void Windows_reads_marks_that_browsers_end_with_a_NUL_and_FileCat_raises_them_exactly(string shape)
    {
        RequireWindows(); using var fixture = new Fixture();
        string path = fixture.PathOf("download.bin"); File.WriteAllBytes(path, [1]);
        var utf16 = new UnicodeEncoding(false, true, true);
        byte[] bytes = shape switch
        {
            "browser" => Encoding.UTF8.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.test/r\r\nHostUrl=https://example.test/h\0"),
            "crlf-nul" => Encoding.UTF8.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/h\r\n\0"),
            _ => utf16.GetPreamble().Concat(utf16.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\n\0")).ToArray(),
        };
        PutMark(path, bytes);
        Assert.Equal(3u, ReadNativeZone(path));
        var parsed = ArchiveWindowsOriginZone.Parse(ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, bytes));
        Assert.Equal(3, parsed.DeclaredZone);
        PutMark(path, parsed.RaiseTo(4).Bytes);
        Assert.Equal(4u, ReadNativeZone(path)); // A fresh COM object reads the raised mark, NUL and URLs kept.
        Assert.Equal(bytes.Length, ReadMark(path).Length);
    }

    /// <summary>Test-only oracle: the installed Windows PersistentZoneIdentifier, as in the encoding oracle above.</summary>
    private static uint ReadNativeZone(string path)
    {
        object identifier = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("0968e258-16c7-4dba-aa86-462dd61e31a3"), throwOnError: true)!)!;
        try
        {
            ((IPersistFile)identifier).Load(path, 0 /* STGM_READ */);
            int result = ((IZoneIdentifier)identifier).GetId(out uint zone);
            Assert.True(result >= 0, $"GetId HRESULT 0x{result:X8}"); return zone;
        }
        finally { Marshal.FinalReleaseComObject(identifier); }
    }

    [ComImport, Guid("cd45f185-1b21-48e2-967b-ead743a8914e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IZoneIdentifier
    {
        [PreserveSig] int GetId(out uint zone);
        [PreserveSig] int SetId(uint zone);
        [PreserveSig] int Remove();
    }
}
