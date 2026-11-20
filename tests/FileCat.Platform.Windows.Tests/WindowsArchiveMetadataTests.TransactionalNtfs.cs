using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Platform.Windows.Archives;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveMetadataTests
{
    private static void RequireTransactionalProbe()
    {
        RequireWindows();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1")
            Assert.Skip("Explicit owned-file TxF feasibility probe; no production archive route uses TxF.");
    }

    [Theory]
    [InlineData("commit")] [InlineData("rollback")] [InlineData("abandon")]
    public void Txf_probe_isolates_data_and_preserves_the_same_object_security_creation_time_attributes_and_origin(string outcome)
    {
        RequireTransactionalProbe(); using var fixture = new Fixture(); string path = fixture.PathOf("archive.bin");
        File.WriteAllBytes(path, [1, 2, 3]); File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddSeconds(1700000000));
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.NotContentIndexed); Mark(path); SetResourceClaim(path, "original");
        using var root = WindowsArchiveDirectory.Open(fixture.Root);
        WindowsArchiveOuterMetadata before;
        using (var source = root.OpenSource("archive.bin")) before = source.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        using (var transaction = NewTransaction())
        {
            using var file = new FileStream(OpenTransactionFile(path, transaction), FileAccess.ReadWrite, 4096, isAsync: false);
            Assert.Equal(new byte[] { 1, 2, 3 }, ReadShared(path));
            file.SetLength(0); file.Write([4, 5, 6, 7]); file.Flush(flushToDisk: true);
            Assert.Equal(new byte[] { 1, 2, 3 }, ReadShared(path));
            file.Position = 0; byte[] pending = new byte[4]; file.ReadExactly(pending); Assert.Equal(new byte[] { 4, 5, 6, 7 }, pending);
            if (outcome == "commit") Check(CommitTransaction(transaction), "CommitTransaction");
            if (outcome == "rollback") Check(RollbackTransaction(transaction), "RollbackTransaction");
        }
        Assert.Equal(outcome == "commit" ? new byte[] { 4, 5, 6, 7 } : new byte[] { 1, 2, 3 }, ReadShared(path));
        using var afterFile = root.OpenSource("archive.bin"); var after = afterFile.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        Assert.Equal(before.Identity, after.Identity); Assert.Equal(before.CreationTime, after.CreationTime);
        Assert.Equal(before.Attributes, after.Attributes); Assert.Equal(before.Origin.Digest, after.Origin.Digest);
        Assert.Equal(before.SecurityDescriptor.ToArray(), after.SecurityDescriptor.ToArray());
        if (outcome != "commit") Assert.Equal(before.LastWriteTime, after.LastWriteTime);
    }

    [Fact]
    public void Txf_probe_rejects_an_external_named_stream_writer_and_hard_link_creation_while_the_file_is_locked()
    {
        RequireTransactionalProbe(); using var fixture = new Fixture(); string path = fixture.PathOf("archive.bin"), alias = fixture.PathOf("alias.bin");
        File.WriteAllBytes(path, [1, 2, 3]); Mark(path);
        Check(CreateHardLink(alias, path, 0), "CreateHardLink baseline"); File.Delete(alias);
        using var root = WindowsArchiveDirectory.Open(fixture.Root); // Admit the actual local NTFS volume before the probe.
        using (var transaction = NewTransaction())
        {
            using var file = new FileStream(OpenTransactionFile(path, transaction), FileAccess.ReadWrite, 4096, isAsync: false);
            file.WriteByte(9); file.Flush(flushToDisk: true);
            Assert.ThrowsAny<IOException>(() => { using var writer = new FileStream(path + ":Zone.Identifier", FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); });
            Assert.False(CreateHardLink(alias, path, 0));
            Assert.Contains(Marshal.GetLastPInvokeError(), new[] { 32, 6800 /* ERROR_TRANSACTIONAL_CONFLICT */ });
            Check(RollbackTransaction(transaction), "RollbackTransaction");
        }
        Assert.False(File.Exists(alias)); Assert.Equal(new byte[] { 1, 2, 3 }, ReadShared(path));
    }

    [Fact]
    public void Txf_probe_refuses_a_preexisting_nontransactional_writer_instead_of_relying_only_on_sharing_flags()
    {
        RequireTransactionalProbe(); using var fixture = new Fixture(); string path = fixture.PathOf("archive.bin");
        File.WriteAllBytes(path, [1, 2, 3]); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var transaction = NewTransaction();
        using var candidate = CreateFileTransacted(path, 0xc0000000, 7, 0, 3, 0x00200000, 0, transaction, 0, 0);
        int error = Marshal.GetLastPInvokeError();
        Assert.True(candidate.IsInvalid); Assert.Contains(error, new[] { 32, 6800 });
        Check(RollbackTransaction(transaction), "RollbackTransaction");
    }

    [Fact]
    public void Txf_probe_refuses_the_normal_deny_write_pin_until_the_pending_transaction_ends()
    {
        RequireTransactionalProbe(); using var fixture = new Fixture(); string path = fixture.PathOf("archive.bin"); File.WriteAllBytes(path, [1, 2, 3]);
        using var root = WindowsArchiveDirectory.Open(fixture.Root); using var transaction = NewTransaction();
        using (var file = new FileStream(OpenTransactionFile(path, transaction), FileAccess.ReadWrite, 4096, isAsync: false))
        { file.SetLength(0); file.Write([4, 5, 6, 7]); file.Flush(flushToDisk: true); }
        Assert.ThrowsAny<IOException>(() => { using var blocked = root.OpenSource("archive.bin"); });
        Check(CommitTransaction(transaction), "CommitTransaction before a normal read-only pin");
        using var frozen = root.OpenSource("archive.bin");
        Assert.Equal(4, frozen.CaptureVersion(TestContext.Current.CancellationToken).Length);
        Assert.Equal(new byte[] { 4, 5, 6, 7 }, ReadShared(path));
        Assert.ThrowsAny<IOException>(() => { using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); });
    }

    [Fact]
    public void Txf_probe_a_transaction_bound_read_pin_keeps_writers_denied_across_commit()
    {
        RequireTransactionalProbe(); using var fixture = new Fixture(); string path = fixture.PathOf("archive.bin"); File.WriteAllBytes(path, [1, 2, 3]);
        using var root = WindowsArchiveDirectory.Open(fixture.Root); using var transaction = NewTransaction();
        using (var file = new FileStream(OpenTransactionFile(path, transaction), FileAccess.ReadWrite, 4096, isAsync: false))
        { file.SetLength(0); file.Write([4, 5, 6, 7]); file.Flush(flushToDisk: true); }
        using var pin = CreateFileTransacted(path, 0x80000000 /* GENERIC_READ */, 1 /* SHARE_READ */, 0, 3, 0x00200000, 0, transaction, 0, 0);
        if (pin.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Open transaction-bound read pin failed.");
        Check(CommitTransaction(transaction), "Commit with a transaction-bound read pin");
        using var frozen = root.OpenSource("archive.bin");
        Assert.Equal(4, frozen.CaptureVersion(TestContext.Current.CancellationToken).Length);
        Assert.ThrowsAny<IOException>(() => { using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Txf_probe_changes_base_data_and_the_origin_stream_as_one_visible_result(bool commit)
    {
        RequireTransactionalProbe(); using var fixture = new Fixture(); string path = fixture.PathOf("archive.bin"), zone = path + ":Zone.Identifier";
        File.WriteAllBytes(path, [1, 2, 3]);
        byte[] before = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/owned\r\n"u8.ToArray(); File.WriteAllBytes(zone, before);
        byte[] required = "[ZoneTransfer]\r\nZoneId=4\r\nHostUrl=https://example.test/owned\r\n"u8.ToArray();
        using var root = WindowsArchiveDirectory.Open(fixture.Root); using var transaction = NewTransaction();
        using (var file = new FileStream(OpenTransactionFile(path, transaction), FileAccess.ReadWrite, 4096, isAsync: false))
        { file.SetLength(0); file.Write([4, 5, 6, 7]); file.Flush(flushToDisk: true); }
        using (var mark = new FileStream(OpenTransactionFile(zone, transaction), FileAccess.ReadWrite, 4096, isAsync: false))
        { mark.SetLength(0); mark.Write(required); mark.Flush(flushToDisk: true); }
        Assert.Equal(new byte[] { 1, 2, 3 }, ReadShared(path)); Assert.Equal(before, ReadSharedBounded(zone, 1024));
        if (commit) Check(CommitTransaction(transaction), "CommitTransaction data/origin");
        else Check(RollbackTransaction(transaction), "RollbackTransaction data/origin");
        Assert.Equal(commit ? new byte[] { 4, 5, 6, 7 } : new byte[] { 1, 2, 3 }, ReadShared(path));
        Assert.Equal(commit ? required : before, ReadSharedBounded(zone, 1024));
    }

    [Theory]
    [InlineData("commit")] [InlineData("rollback")] [InlineData("abandon")]
    public void Txf_probe_updates_owned_audited_CAP_fixtures_without_requesting_audit_security_rights(string outcome)
    {
        RequireTransactionalProbe(); string? fixtures = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_FIXTURES");
        if (fixtures is null) Assert.Skip("Prepare fresh audited TxF fixtures with make-windows-resource-policy-fixtures.ps1 -ForTxf.");
        Assert.True(Path.IsPathFullyQualified(fixtures!)); string leaf = "txf-" + outcome + ".bin", path = Path.Combine(fixtures!, leaf);
        Assert.Equal(new byte[] { 1, 2, 3 }, ReadShared(path));
        using var root = WindowsArchiveDirectory.Open(fixtures!);
        WindowsArchiveOuterMetadata before;
        using (var source = root.OpenSource(leaf)) before = source.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        using (var transaction = NewTransaction())
        {
            using (var file = new FileStream(OpenTransactionFile(path, transaction), FileAccess.ReadWrite, 4096, isAsync: false))
            { file.SetLength(0); file.Write([4, 5, 6, 7]); file.Flush(flushToDisk: true); }
            Assert.Equal(new byte[] { 1, 2, 3 }, ReadShared(path));
            if (outcome == "commit") Check(CommitTransaction(transaction), "Commit audited data");
            if (outcome == "rollback") Check(RollbackTransaction(transaction), "Rollback audited data");
        }
        Assert.Equal(outcome == "commit" ? new byte[] { 4, 5, 6, 7 } : new byte[] { 1, 2, 3 }, ReadShared(path));
        using var afterFile = root.OpenSource(leaf); var after = afterFile.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        Assert.Equal(before.Identity, after.Identity); Assert.Equal(before.CreationTime, after.CreationTime);
        Assert.Equal(before.SecurityDescriptor.ToArray(), after.SecurityDescriptor.ToArray());
        // The elevated external observer checks the seeded security descriptor, including audit ACEs, before/after.
        // This test requests neither ACCESS_SYSTEM_SECURITY nor a privilege on its transactional data handle.
    }

    private static SafeFileHandle NewTransaction()
    {
        var result = CreateTransaction(0, 0, 0, 0, 0, 15000, "FileCat owned archive validation");
        if (!result.IsInvalid) return result;
        int error = Marshal.GetLastPInvokeError(); result.Dispose(); throw new Win32Exception(error, "CreateTransaction failed.");
    }
    private static SafeFileHandle OpenTransactionFile(string path, SafeFileHandle transaction)
    {
        var result = CreateFileTransacted(path, 0xc0000000 /* GENERIC_READ | GENERIC_WRITE */, 7, 0, 3 /* OPEN_EXISTING */,
            0x00200000 /* OPEN_REPARSE_POINT */, 0, transaction, 0, 0);
        if (!result.IsInvalid) return result;
        int error = Marshal.GetLastPInvokeError(); result.Dispose(); throw new Win32Exception(error, "CreateFileTransacted failed.");
    }
    private static byte[] ReadShared(string path) => ReadSharedBounded(path, 16);
    private static byte[] ReadSharedBounded(string path, int maximum)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Assert.InRange(file.Length, 0, maximum); byte[] bytes = new byte[(int)file.Length]; file.ReadExactly(bytes); return bytes;
    }
    private static void Check(bool result, string operation)
    { if (!result) throw new Win32Exception(Marshal.GetLastPInvokeError(), operation + " failed."); }

    [LibraryImport("ktmw32.dll", EntryPoint = "CreateTransaction", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateTransaction(nint security, nint unitOfWork, uint options, uint isolation, uint isolationFlags, uint timeout, string description);
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileTransactedW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileTransacted(string path, uint access, uint sharing, nint security, uint disposition, uint flags, nint template, SafeFileHandle transaction, nint miniVersion, nint extended);
    [LibraryImport("ktmw32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CommitTransaction(SafeFileHandle transaction);
    [LibraryImport("ktmw32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RollbackTransaction(SafeFileHandle transaction);
    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string newName, string existingName, nint security);
}
