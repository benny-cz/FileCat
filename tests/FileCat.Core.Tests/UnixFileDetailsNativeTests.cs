using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

public sealed partial class UnixFileDetailsNativeTests
{
    [Fact]
    public void Native_summary_describes_files_directories_and_links_without_legacy_value_reads()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) { Assert.Skip("Unix native metadata fixture."); return; }
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-details-").FullName;
        try
        {
            string file = Path.Combine(root, "file.txt"), link = Path.Combine(root, "link");
            File.WriteAllText(file, "payload-sentinel-must-not-appear-in-report");
            File.CreateSymbolicLink(link, "file.txt");
            var reader = new UnixFileRecords();
            var regular = reader.ReadSummary(file, ct);
            Assert.Equal(FileObjectKind.File, regular.Kind);
            Assert.Equal(FactState.Known, regular.Identity.State);
            Assert.Equal(FactState.Known, regular.Allocated.State);
            Assert.Equal(FactState.Known, regular.LinkCount.State);
            Assert.Equal(1u, regular.LinkCount.Value);
            Assert.Equal(FileObjectKind.Directory, reader.ReadSummary(root, ct).Kind);
            var symbolic = reader.ReadSummary(link, ct);
            Assert.Equal(FileObjectKind.SymbolicLink, symbolic.Kind);
            Assert.NotEqual(regular.Identity.Value, symbolic.Identity.Value);
            Assert.Equal(FactState.Known, symbolic.Target.State);
            Assert.Equal(new StoredLinkTarget("file.txt", "", true), symbolic.Target.Value);
            var target = Assert.Single(symbolic.ToReport().Sections, section => section.Title == "Target");
            Assert.Contains(("Observation", symbolic.Target.Source), target.Fields);
            Assert.Contains(("Stored path", "file.txt"), target.Fields);
            Assert.Contains(("Calculated path", file), target.Fields);
            Assert.Contains(("Target status", "Not checked"), target.Fields);
            File.Delete(file);
            Assert.Equal(symbolic.Target, reader.ReadSummary(link, ct).Target);
            File.WriteAllText(file, "payload-sentinel-must-not-appear-in-report");
            var full = reader.ReadFull(file, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            Assert.DoesNotContain("payload-sentinel", full.ToText());
            Assert.Contains("Not acquired:", full.ToText());
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("../wanted.txt", FileObjectKind.File, false)]
    [InlineData("jump/../wanted.txt", FileObjectKind.File, false)]
    [InlineData("../wanted.txt/", FileObjectKind.File, true)]
    [InlineData("../leaf-alias/", FileObjectKind.SymbolicLink, true)]
    [InlineData("..", FileObjectKind.Directory, true)]
    public void Explicit_relative_target_navigation_resolves_parent_aliases_but_preserves_the_final_object(
        string stored, FileObjectKind expectedKind, bool requiresDirectory)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix native link fixture.");
        using var root = new TempDir();
        string real = Path.Combine(root.Path, "real"), visible = Path.Combine(root.Path, "visible");
        string sub = Path.Combine(real, "sub"), sibling = Path.Combine(real, "sibling");
        Directory.CreateDirectory(sub);
        Directory.CreateDirectory(sibling);
        Directory.CreateDirectory(visible);
        string alias = Path.Combine(visible, "alias");
        Directory.CreateSymbolicLink(alias, sub);
        Directory.CreateSymbolicLink(Path.Combine(sub, "jump"), sibling);
        Directory.CreateSymbolicLink(Path.Combine(real, "leaf-alias"), sibling);
        File.WriteAllText(Path.Combine(real, "wanted.txt"), "correct");
        File.WriteAllText(Path.Combine(sub, "wanted.txt"), "intermediate alias ignored");
        File.WriteAllText(Path.Combine(visible, "wanted.txt"), "parent alias ignored");
        File.CreateSymbolicLink(Path.Combine(sub, "link"), stored);

        var ct = TestContext.Current.CancellationToken;
        var reader = new UnixFileRecords();
        var link = reader.ReadSummary(Path.Combine(alias, "link"), ct);
        Assert.True(FileDetailsTargets.TryResolve(link, windows: false, out string candidate, out _));
        Assert.Equal(Path.Combine(alias, stored), candidate);
        var resolved = FileDetailsTargets.ResolveUnixNavigationPath(candidate, ct);
        string canonical = Assert.IsType<string>(UnixFiles.RealPath(real));
        string expected = expectedKind switch
        {
            FileObjectKind.SymbolicLink => Path.Combine(canonical, "leaf-alias"),
            FileObjectKind.File => Path.Combine(canonical, "wanted.txt"),
            _ => canonical,
        };
        Assert.Equal((expected, requiresDirectory), resolved);
        Assert.Equal(expectedKind, reader.ReadSummary(resolved.Path, ct).Kind);
    }

    [Fact]
    public unsafe void Linux_retains_link_metadata_with_an_unrepresentable_stored_target()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires a filesystem that stores non-UTF-8 symlink bytes.");
        using var root = new TempDir();
        string path = Path.Combine(root.Path, "link");
        byte* invalidUtf8 = stackalloc byte[] { 0xff, 0 };
        Assert.Equal(0, MakeSymlink(invalidUtf8, path));
        var link = new UnixFileRecords().ReadSummary(path, TestContext.Current.CancellationToken);
        Assert.Equal(FileObjectKind.SymbolicLink, link.Kind);
        Assert.Equal(FactState.Known, link.Identity.State);
        Assert.Equal(FactState.Unsupported, link.Target.State);
        Assert.Null(link.Target.Value);
        Assert.False(FileDetailsTargets.TryResolve(link, windows: false, out _, out _));
    }

    [Fact]
    public void Linux_full_report_observes_casefold_on_the_held_ext4_directory()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Requires a qualified Linux x64 ext4 fixture.");
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_EXT4_CASEFOLD_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Skip("Requires a disposable ext4 mount with both directory policies prepared.");

        string ordinary = Path.Join(root, "ordinary"), folded = Path.Join(root, "folded");
        const string composed = "caf\u00E9.txt", decomposed = "cafe\u0301.txt";
        Assert.False(File.Exists(Path.Join(ordinary, "mixed.txt")));
        Assert.False(File.Exists(Path.Join(ordinary, decomposed)));
        Assert.True(File.Exists(Path.Join(folded, "mixed.txt")));
        Assert.True(File.Exists(Path.Join(folded, decomposed)));
        Assert.Contains(composed, Directory.EnumerateFiles(folded).Select(Path.GetFileName));

        var reader = new UnixFileRecords();
        var ct = TestContext.Current.CancellationToken;
        const string title = "ext4 directory namespace";
        foreach (var (directory, enabled) in new[] { (ordinary, false), (folded, true), (Path.Join(root, "parent-alias", "folded"), true) })
        {
            var summary = reader.ReadSummary(directory, ct);
            Assert.Equal("ext4", summary.FileSystem.Value);
            Assert.DoesNotContain(title, summary.ToReport().ToText());
            var full = reader.ReadFull(directory, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(summary.Identity.Value, full.Summary.Identity.Value);
            var section = Assert.Single(full.Technical.Sections, entry => entry.Title == title);
            Assert.Contains(("Scope", "Name lookup in this directory"), section.Fields);
            Assert.Contains(("Case folding", enabled ? "Enabled" : "Disabled"), section.Fields);
            Assert.Equal(enabled, section.Lines.Any(line => line.Contains("Unicode normalization", StringComparison.Ordinal)));
        }

        foreach (string leaf in new[] { Path.Join(folded, composed), Path.Join(root, "folded-link") })
            Assert.DoesNotContain(reader.ReadFull(leaf, FileDetailsPolicy.MetadataOnly, ct).Technical.Sections, section => section.Title == title);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mac_namespace_requires_matching_volume_and_individually_valid_capabilities(bool apfs)
    {
        const uint device = 42;
        var fields = new byte[76];
        BinaryPrimitives.WriteUInt32LittleEndian(fields, 76);
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(4), 0x80000002);
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(8), apfs ? 0x00120000u : 0x00320000u);
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(24), device);
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(28), 0x100);
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(44), 0x100);
        int offset = apfs ? 8 : 12;
        var type = apfs ? "apfs\0"u8 : "hfs\0"u8;
        BinaryPrimitives.WriteInt32LittleEndian(fields.AsSpan(60), offset);
        BinaryPrimitives.WriteUInt32LittleEndian(fields.AsSpan(64), (uint)type.Length);
        type.CopyTo(fields.AsSpan(60 + offset));

        var (sensitive, preserving, volumeVerified) = UnixFileRecords.ParseMacVolumeNamespace(fields, device, apfs);
        Assert.True(volumeVerified);
        Assert.Equal(FactState.Known, sensitive.State);
        Assert.Equal(FactScope.Volume, sensitive.Scope);
        Assert.True(sensitive.Value);
        Assert.Equal(FactState.Unsupported, preserving.State);
        Assert.Equal(FactState.Partial, UnixFileRecords.ParseMacVolumeNamespace(fields, device + 1, apfs).Sensitive.State);
        Assert.False(UnixFileRecords.ParseMacVolumeNamespace(fields, device + 1, apfs).VolumeVerified);
        Assert.False(UnixFileRecords.ParseMacVolumeNamespace(fields, device, !apfs).VolumeVerified);
        Assert.False(UnixFileRecords.ParseMacVolumeNamespace(fields.AsSpan(0, 70), device, apfs).VolumeVerified);
        BinaryPrimitives.WriteInt32LittleEndian(fields.AsSpan(60), int.MaxValue);
        Assert.False(UnixFileRecords.ParseMacVolumeNamespace(fields, device, apfs).VolumeVerified);
    }

    [Fact]
    public void Mac_full_report_describes_APFS_case_and_normalization_on_the_observed_volume()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Requires Darwin volume attributes.");
        string? sensitive = Environment.GetEnvironmentVariable("FILECAT_TEST_APFS_SENSITIVE");
        string? insensitive = Environment.GetEnvironmentVariable("FILECAT_TEST_APFS_INSENSITIVE");
        if (string.IsNullOrEmpty(sensitive) || string.IsNullOrEmpty(insensitive))
            Assert.Skip("Requires two disposable, prepared APFS variants.");

        var ct = TestContext.Current.CancellationToken;
        var reader = new UnixFileRecords();
        Assert.False(File.Exists(Path.Join(sensitive, "mixed")));
        Assert.True(File.Exists(Path.Join(insensitive, "mixed")));
        foreach (var (path, caseSensitive) in new[]
        {
            (sensitive, true), (Path.Join(sensitive, "MiXeD"), true),
            (insensitive, false), (Path.Join(insensitive, "MiXeD"), false),
            (Path.Join(insensitive, "link-to-sensitive"), false),
            (Path.Join(insensitive, "dir-alias", "MiXeD"), true),
        })
        {
            Assert.DoesNotContain("APFS volume namespace", reader.ReadSummary(path, ct).ToReport().ToText());
            var report = reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
            var section = Assert.Single(report.Technical.Sections, section => section.Title == "APFS volume namespace");
            Assert.Contains(("Case sensitive", caseSensitive ? "Yes" : "No"), section.Fields);
            Assert.Contains(("Case preserving", "Yes"), section.Fields);
            Assert.Contains(section.Fields, field => field.Name == "Unicode normalization" &&
                field.Value.Contains("Canonically equivalent names match", StringComparison.Ordinal));
            Assert.Equal(ReportContentKind.MetadataOnly, report.ContentKind);
        }

        const string composed = "caf\u00e9";
        const string decomposed = "cafe\u0301";
        foreach (string root in new[] { sensitive, insensitive })
        {
            var stored = Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).ToArray();
            foreach (var (first, equivalent) in new[] { ("nfc-" + composed, "nfc-" + decomposed), ("nfd-" + decomposed, "nfd-" + composed) })
            {
                Assert.Contains(first, stored);
                Assert.DoesNotContain(equivalent, stored);
                var original = reader.ReadSummary(Path.Join(root, first), ct).Identity;
                var alias = reader.ReadFull(Path.Join(root, equivalent), FileDetailsPolicy.MetadataOnly, ct);
                Assert.Equal(FactState.Known, original.State);
                Assert.Equal(FactState.Known, alias.Summary.Identity.State);
                Assert.Equal(original.Value, alias.Summary.Identity.Value);
                Assert.Contains(alias.Technical.Sections, section => section.Title == "APFS volume namespace");
            }
        }
    }

    [Theory]
    [InlineData(0, 12, FactState.Known)]
    [InlineData(2, 24, FactState.Known)]
    [InlineData(2, 12, FactState.Failed)]
    [InlineData(2, 25, FactState.Failed)]
    [InlineData(3, 24, FactState.Unsupported)]
    public void Linux_fscrypt_policy_checks_returned_version_and_length(byte version, int length, FactState expected)
    {
        var reply = new byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(reply, (ulong)length);
        reply[8] = version;
        reply[9] = 1;
        reply[10] = 4;
        reply[11] = 3;
        var policy = UnixFileRecords.ParseLinuxFscryptPolicy(reply);
        Assert.Equal(expected, policy.State);
        if (expected == FactState.Known)
            Assert.Equal(new UnixFileRecords.LinuxFscryptPolicy(version == 0 ? 1 : 2, 1, 4, 3), policy.Value);
        else Assert.Null(policy.Value);
    }

    [Theory]
    [InlineData(128, 1, FactState.Known)]
    [InlineData(128, 2, FactState.Known)]
    [InlineData(128, 3, FactState.Known)]
    [InlineData(128, 0, FactState.Unsupported)]
    [InlineData(128, 4, FactState.Unsupported)]
    [InlineData(127, 2, FactState.Failed)]
    public void Linux_fscrypt_key_status_requires_a_complete_recognized_reply(int length, int status, FactState expected)
    {
        var reply = new byte[length];
        BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(64), status);
        var observed = UnixFileRecords.ParseLinuxFscryptKeyStatus(reply);
        Assert.Equal(expected, observed.State);
        Assert.Equal(FactScope.Volume, observed.Scope);
        if (expected == FactState.Known) Assert.Equal((UnixFileRecords.LinuxFscryptKeyStatus)status, observed.Value);
    }

    [Fact]
    public void Linux_full_report_distinguishes_real_ext4_encryption_policies_without_showing_key_or_payload()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Requires a qualified Linux x64 ext4 fixture.");
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_FSCRYPT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Skip("Requires the disposable ext4 directory with prepared v1 and v2 policies.");
        var reader = new UnixFileRecords();
        var ct = TestContext.Current.CancellationToken;
        foreach (var (leaf, version, scope) in new[]
        {
            ("v1", "1", "Directory policy"), ("v2", "2", "Directory policy"), ("v2/covered.dat", "2", "Regular file"),
        })
        {
            string path = Path.Join(root, leaf);
            var summary = reader.ReadSummary(path, ct);
            Assert.Equal("ext4", summary.FileSystem.Value);
            Assert.Equal(FactState.Known, summary.FileEncryption.State);
            Assert.True(summary.FileEncryption.Value);
            Assert.DoesNotContain("fscrypt policy", summary.ToReport().ToText());
            var full = reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(summary.Identity.Value, full.Summary.Identity.Value);
            var policy = Assert.Single(full.Technical.Sections, section => section.Title == "fscrypt policy");
            Assert.Contains(("Status", "Policy present"), policy.Fields);
            Assert.Contains(("Scope", scope), policy.Fields);
            Assert.Contains(("Policy version", version), policy.Fields);
            Assert.Contains(("Content mode", "AES-256-XTS"), policy.Fields);
            Assert.Contains(("Filename mode", "AES-256-CTS"), policy.Fields);
            Assert.Contains(("Filesystem keyring", version == "2" ? "Policy key present"
                : "Not inspected for version 1 policies, which can use process keyrings"), policy.Fields);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            Assert.DoesNotContain("fscrypt-payload-sentinel", full.ToText());
        }

        var clear = reader.ReadFull(Path.Join(root, "plain"), FileDetailsPolicy.MetadataOnly, ct);
        var absent = Assert.Single(clear.Technical.Sections, section => section.Title == "fscrypt policy");
        Assert.Contains(("Status", "No policy on this object"), absent.Fields);
        Assert.DoesNotContain(absent.Fields, field => field.Name == "Policy version");
        Assert.DoesNotContain(absent.Fields, field => field.Name == "Filesystem keyring");
    }

    [Fact]
    public void Linux_full_report_distinguishes_absent_and_partially_removed_ext4_policy_keys()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Requires a qualified Linux x64 ext4 fixture.");
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_FSCRYPT_KEY_STATES");
        if (string.IsNullOrWhiteSpace(root)) Assert.Skip("Requires disposable ext4 directories with absent and incompletely removed keys.");
        var reader = new UnixFileRecords();
        var ct = TestContext.Current.CancellationToken;
        foreach (var (leaf, status) in new[] { ("absent", "Policy key absent"), ("removing", "Policy key removal incomplete") })
        {
            var full = reader.ReadFull(Path.Join(root, leaf), FileDetailsPolicy.MetadataOnly, ct);
            Assert.True(full.Summary.FileEncryption.Value);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            var policy = Assert.Single(full.Technical.Sections, section => section.Title == "fscrypt policy");
            Assert.Contains(("Policy version", "2"), policy.Fields);
            Assert.Contains(("Filesystem keyring", status), policy.Fields);
            Assert.Contains(("Key status source", "FS_IOC_GET_ENCRYPTION_KEY_STATUS on the same ext4 descriptor"), policy.Fields);
        }
    }

    [Fact]
    public void Linux_gets_the_mount_type_of_the_held_item_through_a_parent_symlink()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux statx and procfs.");
        using var root = new TempDir();
        string link = Path.Join(root.Path, "proc-alias");
        Directory.CreateSymbolicLink(link, "/proc/self");
        var reader = new UnixFileRecords();
        var ct = TestContext.Current.CancellationToken;
        var entry = reader.ReadSummary(link, ct);
        Assert.Equal(FileObjectKind.SymbolicLink, entry.Kind);
        Assert.Equal(FactState.Known, entry.FileSystem.State);
        Assert.Equal(reader.ReadSummary(root.Path, ct).FileSystem.Value, entry.FileSystem.Value);
        var proc = reader.ReadSummary(Path.Join(link, "stat"), ct);
        Assert.Equal(FactState.Known, proc.FileSystem.State);
        Assert.Equal(FactScope.Volume, proc.FileSystem.Scope);
        Assert.Equal("proc", proc.FileSystem.Value);
    }

    [Fact]
    public void Linux_summary_of_FIFO_does_not_open_it_for_data_or_wait_for_a_writer()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Skip("Linux O_PATH fixture."); return; }
        string root = Directory.CreateTempSubdirectory("filecat-fifo-details-").FullName;
        try
        {
            string fifo = Path.Combine(root, "fifo");
            Assert.Equal(0, MakeFifo(fifo, 0x180));
            var snapshot = new UnixFileRecords().ReadSummary(fifo, TestContext.Current.CancellationToken);
            Assert.Equal(FileObjectKind.Fifo, snapshot.Kind);
            Assert.Equal(FactState.Known, snapshot.Identity.State);
            var full = new UnixFileRecords().ReadFull(fifo, FileDetailsPolicy.MetadataOnly, TestContext.Current.CancellationToken);
            Assert.DoesNotContain(full.Technical.Sections, section => section.Title == "Main-stream extent records");
            Assert.DoesNotContain(full.Technical.Sections, section => section.Title == "fscrypt policy");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Linux_FIEMAP_reopens_the_pinned_inode_even_after_its_path_becomes_a_FIFO()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_FIEMAP") == "1",
            "Requires a disposable Linux filesystem with native FIEMAP support.");
        string root = Directory.CreateTempSubdirectory("filecat-fiemap-details-").FullName;
        var ct = TestContext.Current.CancellationToken;
        try
        {
            string path = Path.Join(root, "source"), retired = Path.Join(root, "renamed");
            const int ranges = 520, stride = 32 * 1024;
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                file.SetLength((ranges + 1L) * stride);
                for (int i = 1; i <= ranges; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    file.Position = (long)i * stride;
                    file.WriteByte(42);
                }
                file.Flush(flushToDisk: true);
            }
            var reader = new UnixFileRecords();
            var summary = reader.ReadSummary(path, ct);
            Assert.DoesNotContain("Main-stream extent records", summary.ToReport().ToText());
            var full = reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
            var extents = Assert.Single(full.Technical.Sections, section => section.Title == "Main-stream extent records");
            Assert.Contains(("Inventory", "Complete"), extents.Fields);
            Assert.NotEmpty(extents.Table!.Rows);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            int pin = OpenPath(path, 0x200000 | 0x80000); // O_PATH | O_CLOEXEC
            Assert.True(pin >= 0);
            try
            {
                File.Move(path, retired);
                Assert.Equal(0, MakeFifo(path, 0x180));
                var map = UnixFileRecords.ReadLinuxPinnedExtents(pin, summary.Identity.Value!, ct);
                Assert.Equal(FactState.Known, map.State);
                Assert.True(map.Value!.Count >= ranges);
                Assert.DoesNotContain(reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct).Technical.Sections,
                    section => section.Title == "Main-stream extent records");
            }
            finally { CloseFile(pin); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [LibraryImport("libc", EntryPoint = "mkfifo", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MakeFifo(string path, uint mode);

    [LibraryImport("libc", EntryPoint = "symlink", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static unsafe partial int MakeSymlink(byte* stored, string path);

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenPath(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int CloseFile(int fd);
}
