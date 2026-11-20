using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Fact]
    public void Update_metadata_reproduces_permissions_group_acl_flags_and_birth_time_before_the_exchange()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string targetPath = Path.Combine(root, "original.tar"); File.WriteAllText(targetPath, "old archive");
        File.SetUnixFileMode(targetPath, (UnixFileMode)0x1a0); // 0640
        uint? group = SupplementaryGroup(targetPath);
        if (group is { } g) Assert.Equal(0, Chown(targetPath, uint.MaxValue, g));
        if (OperatingSystem.IsLinux())
        {
            using var held = File.OpenHandle(targetPath, FileMode.Open, FileAccess.Read);
            byte[] acl = PosixAccessAcl(4 /* group r */, 4 /* mask r */, 0 /* other */);
            Assert.Equal(0, SetAcl((int)held.DangerousGetHandle(), "system.posix_acl_access", acl, acl.Length, 0));
            ulong flags = 0; Assert.Equal(0, IoctlFlags((int)held.DangerousGetHandle(), 0x80086601, ref flags));
            flags |= ArchiveUnixUpdateMetadata.LinuxNoDump;
            if (IoctlFlags((int)held.DangerousGetHandle(), 0x40086602, ref flags) != 0) flags = 0; // Some filesystems refuse; still exercised on ext4.
        }
        else
        {
            Chmod("+a", "everyone allow read", targetPath);
            Run("/usr/bin/chflags", "nodump", targetPath);
        }
        try { UpdateMetadataExchange(root, targetPath, ct); }
        finally { if (OperatingSystem.IsMacOS()) foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) Chmod("-N", file); }
    }

    private void UpdateMetadataExchange(string root, string targetPath, CancellationToken ct)
    {
        using var target = UnixArchiveDirectory.Open(root);
        using var original = target.OpenSource("original.tar", 4096);
        var old = ((IArchiveRetainedFile)original).Observe(ct); var oldMetadata = original.CaptureOuterMetadata(ct);
        using var privateParent = target.CreatePrivateStageDirectory(Guid.NewGuid(), ct);
        using var stage = Stage(privateParent); string stageName = stage.KnownName;
        ((IArchiveRetainedStage)stage).BeginRebuild(4096, ct).Write("new archive"u8);
        var sealedStage = ((IArchiveRetainedStage)stage).SealForVerification(ct);
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(Path.Combine(privateParent.DisplayPath, stageName)));

        var applied = stage.ApplyUpdateMetadata(sealedStage, original, old, old.Origin, ct);

        var stageMetadata = Metadata(stage);
        Assert.Equal(sealedStage.Version, applied.Version);
        Assert.Equal(oldMetadata.Native.Mode, stageMetadata.Native.Mode);
        Assert.Equal(oldMetadata.Native.Group, stageMetadata.Native.Group);
        Assert.Equal(oldMetadata.AclPresent, stageMetadata.AclPresent);
        Assert.True(oldMetadata.AclBytes.SequenceEqual(stageMetadata.AclBytes));
        uint keep = ArchiveUnixUpdateMetadata.Preservable(oldMetadata.Native.Format);
        Assert.Equal(oldMetadata.Native.Flags & keep, stageMetadata.Native.Flags & keep);
        if (OperatingSystem.IsMacOS()) Assert.Equal(oldMetadata.Native.Created, stageMetadata.Native.Created);
        Assert.Equal(ArchiveUnixUpdateMetadata.PromisedDigest(oldMetadata, old.Origin), ArchiveUnixUpdateMetadata.PromisedDigest(stageMetadata, applied.Origin));

        var receipt = stage.ExchangeRetainedAcrossParents(original, applied, old, ct);

        Assert.Equal(applied.Version, receipt.Published);
        Assert.Equal("new archive", File.ReadAllText(targetPath));
        Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(targetPath));
        using var reopened = target.OpenSource("original.tar", 4096);
        Assert.Equal(ArchiveUnixUpdateMetadata.PromisedDigest(oldMetadata, old.Origin),
            ArchiveUnixUpdateMetadata.PromisedDigest(reopened.CaptureOuterMetadata(ct), old.Origin));
        Assert.Equal("old archive", File.ReadAllText(Path.Combine(privateParent.DisplayPath, stageName)));
    }

    [Fact]
    public void Exchange_refused_by_the_file_system_leaves_both_names_unchanged_and_usable()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string targetPath = Path.Combine(root, "original.tar"); File.WriteAllText(targetPath, "old archive");
        using var target = UnixArchiveDirectory.Open(root);
        using var original = target.OpenSource("original.tar", 4096);
        var old = ((IArchiveRetainedFile)original).Observe(ct);
        using var privateParent = target.CreatePrivateStageDirectory(Guid.NewGuid(), ct);
        using var stage = Stage(privateParent); string stageName = stage.KnownName;
        ((IArchiveRetainedStage)stage).BeginRebuild(4096, ct).Write("new archive"u8);
        var sealedStage = ((IArchiveRetainedStage)stage).SealForVerification(ct);
        var applied = stage.ApplyUpdateMetadata(sealedStage, original, old, old.Origin, ct);
        File.SetUnixFileMode(root, (UnixFileMode)0x140); // 0500: the public folder no longer allows renames.
        try
        {
            var refused = Assert.Throws<UnixArchiveExchangeNotPerformedException>(() => stage.ExchangeRetainedAcrossParents(original, applied, old, ct));
            Assert.NotEqual(0, refused.Errno);
            Assert.Equal("old archive", File.ReadAllText(targetPath));
            Assert.Equal("new archive", File.ReadAllText(Path.Combine(privateParent.DisplayPath, stageName)));
            Assert.True(stage.CanRead); Assert.True(original.CanRead);
            ((IArchiveRetainedFile)original).Review(old, ct); ((IArchiveRetainedFile)stage).Review(applied, ct);
        }
        finally { File.SetUnixFileMode(root, (UnixFileMode)0x1c0); }
    }

    [Fact]
    public void Update_admission_refuses_a_Mac_ACL_with_a_deny_entry()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("Darwin ACL deny entries.");
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string targetPath = Path.Combine(root, "original.tar"); File.WriteAllText(targetPath, "old archive");
        Chmod("+a", "everyone deny delete", targetPath);
        try
        {
            using var target = UnixArchiveDirectory.Open(root);
            using var original = target.OpenSource("original.tar", 4096);
            Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() => original.RequireUpdateAdmissible(ct)).Reason);
        }
        finally { Chmod("-N", targetPath); }
    }

    [Fact]
    public void Update_metadata_refuses_special_permission_bits_and_keeps_the_stage_private()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string targetPath = Path.Combine(root, "original.tar"); File.WriteAllText(targetPath, "old archive");
        File.SetUnixFileMode(targetPath, (UnixFileMode)0x9a4); // 04644: setuid set by the owner on a data file
        using var target = UnixArchiveDirectory.Open(root);
        using var original = target.OpenSource("original.tar", 4096);
        var old = ((IArchiveRetainedFile)original).Observe(ct);
        using var privateParent = target.CreatePrivateStageDirectory(Guid.NewGuid(), ct);
        using var stage = Stage(privateParent); string stageName = stage.KnownName;
        ((IArchiveRetainedStage)stage).BeginRebuild(4096, ct).Write("new archive"u8);
        var sealedStage = ((IArchiveRetainedStage)stage).SealForVerification(ct);

        Assert.Equal(ArchiveWriteReason.UnsupportedFeature,
            Assert.Throws<ArchiveWriteException>(() => stage.ApplyUpdateMetadata(sealedStage, original, old, old.Origin, ct)).Reason);
        Assert.Equal(ArchiveWriteReason.InvalidRequest,
            Assert.Throws<ArchiveWriteException>(() => stage.ExchangeRetainedAcrossParents(original, sealedStage, old, ct)).Reason);
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(Path.Combine(privateParent.DisplayPath, stageName)));
        Assert.Equal("old archive", File.ReadAllText(targetPath));
        Assert.True(stage.CanRead); Assert.True(original.CanRead);
    }

    [Fact]
    public void Update_metadata_requires_the_private_stage_directory_of_the_original()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        File.WriteAllText(Path.Combine(root, "original.tar"), "old archive");
        using var target = UnixArchiveDirectory.Open(root);
        using var original = target.OpenSource("original.tar", 4096);
        var old = ((IArchiveRetainedFile)original).Observe(ct);
        using var stage = Stage(target); // Same public directory: never receives non-private permissions.
        ((IArchiveRetainedStage)stage).BeginRebuild(4096, ct).Write("new archive"u8);
        var sealedStage = ((IArchiveRetainedStage)stage).SealForVerification(ct);
        Assert.Equal(ArchiveWriteReason.InvalidRequest,
            Assert.Throws<ArchiveWriteException>(() => stage.ApplyUpdateMetadata(sealedStage, original, old, old.Origin, ct)).Reason);
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(stage.DisplayPath));
    }

    private static uint? SupplementaryGroup(string path)
    {
        var start = new ProcessStartInfo("/usr/bin/id") { UseShellExecute = false, RedirectStandardOutput = true }; start.ArgumentList.Add("-G");
        using var child = Process.Start(start)!; string output = child.StandardOutput.ReadToEnd(); child.WaitForExit(10_000);
        using var held = File.OpenHandle(path); uint current = ArchiveUnixOuterMetadata.Capture(held).Native.Group;
        foreach (string text in output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (uint.TryParse(text, out uint gid) && gid != current) return gid;
        return null;
    }

    private static byte[] PosixAccessAcl(ushort groupPerm, ushort maskPerm, ushort otherPerm)
    {
        var entries = new (ushort Tag, ushort Perm, uint Id)[] { (1, 6, uint.MaxValue), (2, 4, 50002), (4, groupPerm, uint.MaxValue), (16, maskPerm, uint.MaxValue), (32, otherPerm, uint.MaxValue) };
        byte[] acl = new byte[4 + 8 * entries.Length]; BinaryPrimitives.WriteUInt32LittleEndian(acl, 2);
        for (int i = 0; i < entries.Length; i++)
        {
            var span = acl.AsSpan(4 + 8 * i);
            BinaryPrimitives.WriteUInt16LittleEndian(span, entries[i].Tag); BinaryPrimitives.WriteUInt16LittleEndian(span[2..], entries[i].Perm);
            BinaryPrimitives.WriteUInt32LittleEndian(span[4..], entries[i].Id);
        }
        return acl;
    }

    private static void Run(string program, params string[] args)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardError = true }; foreach (string arg in args) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!; if (!child.WaitForExit(10_000)) { child.Kill(); child.WaitForExit(); Assert.Fail(program + " fixture timed out."); }
        Assert.True(child.ExitCode == 0, child.StandardError.ReadToEnd());
    }

    [LibraryImport("libc", EntryPoint = "chown", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int Chown(string path, uint owner, uint group);
    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int IoctlFlags(int fd, nuint request, ref ulong value);
}
