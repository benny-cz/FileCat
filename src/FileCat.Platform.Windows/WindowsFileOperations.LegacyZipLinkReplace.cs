using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.HiddenData;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows;

public partial class WindowsFileOperations
{
    private const FileAttributes ZipLinkAttributes = FileAttributes.Normal | FileAttributes.Archive | FileAttributes.Hidden |
        FileAttributes.NotContentIndexed | FileAttributes.System;
    private const uint ZipLinkFileOptions = NonDirectoryFile | OpenReparsePoint;

    private static LegacyZipInPlaceResult? PublishLegacyZipLinkReplacement(SafeFileHandle directory, string archive, string stage, string backup,
        LegacyZipRollbackFile original, SafeFileHandle originalPin, LegacyZipRollbackFile rebuilt, SafeFileHandle stagePin,
        Action checkpoint, Action? beforeCommit, Action? afterCommit)
    {
        bool prepared = false, commitAttempted = false;
        try
        {
            using var transaction = new WindowsFileTransaction("FileCat hard-linked ZIP publication");
            using var source = transaction.OpenHeldChild(directory, archive, GenericRead | Delete | Synchronize);
            RequireZipLinkIdentity(source, originalPin);
            uint links = WindowsArchiveNative.Regular(source, original.Version.Length, artifact: false, mayRename: true);
            if (links < 2 || !OrdinaryZipLinkFile(source) || !HasNoZipObjectId(source) ||
                !WindowsArchiveNative.HasNoDistinctShortName(source, archive)) return null;
            var originalMark = VerifyTransactedPublicationInput(source, transaction, original, checkpoint);
            var metadata = GetBasic(source);
            byte[] security = WindowsArchiveNative.ReadOuterSecurity(source);
            var descriptor = new RawSecurityDescriptor(security, 0);
            const ControlFlags excluded = ControlFlags.SystemAclPresent | ControlFlags.SystemAclProtected |
                ControlFlags.DiscretionaryAclProtected;
            if ((descriptor.ControlFlags & excluded) != 0) return null;

            // The transacted source holds its exact selected name against writers, EFS and removal.
            // NTFS returns EAs through the ordinary handles, which remain bound to the same objects.
            using var sourceMetadata = OpenOrThrow(directory, archive, GenericRead | Synchronize, ShareAll, Open, ZipLinkFileOptions, "Read the original ZIP extended attributes");
            RequireZipLinkIdentity(sourceMetadata, source);
            byte[]? eas = WindowsHiddenData.ReadArchiveCopyEas(sourceMetadata);
            if (eas is null) return null;
            var inventory = WindowsHiddenData.ReadStreamInventory(source, CancellationToken.None);
            if (inventory.State != HiddenInventoryState.Complete || inventory.Items.Any(item => item.Role is not (HiddenMetadataRole.Ordinary or HiddenMetadataRole.DownloadMark)))
                return null;
            string sourceName = WindowsFileTransaction.HeldPath(source);
            if (originalMark.State == ArchiveOriginState.Present)
            {
                using var zone = transaction.OpenExisting(sourceName + ZoneStream, GenericRead | Synchronize, ShareRead | ShareDelete, "Inspect the held ZIP download mark");
                RequireZipLinkIdentity(zone, source);
                if (!OrdinaryZipLinkFile(zone)) return null;
            }
            var streams = new List<ZipLinkStream>();
            foreach (var item in inventory.Items.Where(item => item.Role != HiddenMetadataRole.DownloadMark))
            {
                checkpoint();
                string name = ":" + item.Name + ":$DATA";
                using var input = transaction.OpenExisting(sourceName + name, GenericRead | Synchronize, ShareRead | ShareDelete, "Inspect a held ZIP stream");
                RequireZipLinkIdentity(input, source);
                if (!OrdinaryZipLinkFile(input) || RandomAccess.GetLength(input) != item.Size) return null;
                streams.Add(new(name, item.Size, DigestZipLinkStream(input, item.Size, checkpoint)));
            }

            using var stageMetadata = OpenOrThrow(directory, stage, GenericRead | Synchronize, ShareAll, Open, ZipLinkFileOptions, "Hold the rebuild metadata through publication");
            RequireZipLinkIdentity(stageMetadata, stagePin);
            ArchiveOriginSnapshot publishedMark;
            using (var working = OpenOrThrow(directory, stage, GenericRead | GenericWrite | Synchronize, ShareRead, Open, ZipLinkFileOptions,
                "Prepare the verified ZIP rebuild for namespace replacement"))
            {
                RequireZipLinkIdentity(working, stagePin);
                WindowsArchiveNative.Regular(working, rebuilt.Version.Length, artifact: true, mayRename: true);
                if (!OrdinaryZipLinkFile(working) || !HasNoZipObjectId(working) || !WindowsArchiveNative.HasNoExtendedAttributes(working) ||
                    !WindowsArchiveNative.ReadOuterSecurity(working).AsSpan().SequenceEqual(security)) return null;
                var rebuiltMark = VerifyLegacyZipRollbackFile(working, rebuilt, allowMissingOrigin: false, checkpoint: checkpoint);
                WindowsArchiveNative.RequireSnapshotStreams(working, rebuilt.Version.Length, rebuiltMark);
                publishedMark = string.Equals(original.Origin, rebuilt.Origin, StringComparison.Ordinal) ? originalMark : rebuiltMark;
                prepared = true;
                if (publishedMark.Digest != rebuiltMark.Digest)
                {
                    using var zone = OpenOrThrow(working, ZoneStream, GenericWrite | ReadAttributes | Synchronize, ShareAll, Open, ZipLinkFileOptions,
                        "Keep the exact original ZIP download mark");
                    WriteZipPublicationOrigin(working, zone, publishedMark);
                }
                foreach (var stream in streams)
                {
                    checkpoint();
                    using var input = transaction.OpenExisting(sourceName + stream.Name, GenericRead | Synchronize, ShareRead | ShareDelete, "Open the held ZIP stream");
                    RequireZipLinkIdentity(input, source);
                    CheckLegacyZipRollbackProtection(working, encrypted: false);
                    using var output = OpenOrThrow(working, stream.Name, GenericRead | GenericWrite | Synchronize, ShareAll, Create, ZipLinkFileOptions,
                        "Preserve a stream on the held ZIP rebuild");
                    RequireZipLinkIdentity(output, working);
                    CopyLegacyZipPublicationBytes(input, output, stream.Length, checkpoint);
                    WindowsArchiveNative.Flush(output);
                }
                checkpoint();
                CheckLegacyZipRollbackProtection(working, encrypted: false);
                WindowsHiddenData.WriteArchiveCopyEas(working, eas);
                SetBasic(working, new BasicInfo { CreationTime = metadata.CreationTime, FileAttributes = metadata.FileAttributes });
                WindowsArchiveNative.Flush(working);
            }

            using (var output = transaction.OpenHeldChild(directory, stage, GenericRead | GenericWrite | Delete | Synchronize))
            {
                RequireZipLinkIdentity(output, stagePin);
                VerifyZipLinkMetadata(output, stageMetadata, transaction, rebuilt, publishedMark, metadata, security, eas, streams, checkpoint);
                Rename(source, directory, backup, replace: false);
                Rename(output, directory, archive, replace: false);
                if (!WindowsArchiveNative.HasNoDistinctShortName(output, archive))
                    throw new IOException("The rebuilt ZIP acquired a different short name during publication.");
                beforeCommit?.Invoke();
                checkpoint();
            }
            source.Dispose();
            commitAttempted = true;
            transaction.Commit("Windows could not confirm publication of the hard-linked ZIP update.");
            afterCommit?.Invoke();

            using var adopted = OpenOrThrow(directory, archive, GenericRead | Synchronize, ShareRead, Open, ZipLinkFileOptions, "Verify the published ZIP rebuild");
            VerifyZipLinkMetadata(adopted, adopted, null, rebuilt, publishedMark, metadata, security, eas, streams, static () => { });
            using var retained = OpenLegacyZipRollbackFile(directory, backup, allowDelete: false);
            VerifyLegacyZipRollbackFile(retained, original, allowMissingOrigin: false);
            if (WindowsArchiveNative.Regular(retained, original.Version.Length, artifact: false, mayRename: true) != links)
                throw new IOException("FileCat could not confirm the original ZIP's link count after publication.");
            return new(true, "FileCat retained the original ZIP at the backup path because other names still link to it.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or OperationCanceledException or NotSupportedException)
        {
            string? review = commitAttempted ? "FileCat could not verify the hard-linked ZIP publication. Check the selected archive and its backup."
                : prepared ? "The ZIP transaction stopped before commit. The prepared rebuild requires review." : null;
            return new(false, review, error is Win32Exception ? new IOException(error.Message, error) : error);
        }
    }

    private sealed record ZipLinkStream(string Name, long Length, string Sha256);

    private static void VerifyZipLinkMetadata(SafeFileHandle file, SafeFileHandle metadataReader, WindowsFileTransaction? transaction,
        LegacyZipRollbackFile expected, ArchiveOriginSnapshot expectedMark, BasicInfo original, byte[] security, byte[] eas,
        IReadOnlyList<ZipLinkStream> streams, Action checkpoint)
    {
        RequireZipLinkIdentity(metadataReader, file);
        var mark = transaction is null ? VerifyLegacyZipRollbackFile(file, expected, allowMissingOrigin: false, checkpoint: checkpoint)
            : VerifyTransactedPublicationInput(file, transaction, expected, checkpoint);
        if (mark.Digest != expectedMark.Digest || !HasNoZipObjectId(file))
            throw new IOException("The exact ZIP download mark or object ID changed during publication.");
        RequirePreservedLegacyZipMetadata(file, expected.Version.Length, original, security);
        var inventory = WindowsHiddenData.ReadStreamInventory(file, CancellationToken.None);
        if (inventory.State != HiddenInventoryState.Complete || inventory.Items.Length != streams.Count + (mark.State == ArchiveOriginState.Present ? 1 : 0) ||
            WindowsHiddenData.ReadArchiveCopyEas(metadataReader) is not { } actualEas || !actualEas.AsSpan().SequenceEqual(eas))
            throw new IOException("The ZIP rebuild's streams or extended attributes changed during publication.");
        foreach (var stream in streams)
        {
            checkpoint();
            using var input = transaction is null
                ? OpenOrThrow(file, stream.Name, GenericRead | Synchronize, ShareAll, Open, ZipLinkFileOptions, "Verify a published ZIP stream")
                : transaction.OpenExisting(WindowsFileTransaction.HeldPath(file) + stream.Name, GenericRead | Synchronize, ShareAll, "Verify a transacted ZIP stream");
            RequireZipLinkIdentity(input, file);
            if (!OrdinaryZipLinkFile(input) || !DigestMatches(input, stream.Sha256, stream.Length, CancellationToken.None, () =>
                {
                    checkpoint();
                    CheckLegacyZipRollbackProtection(file, encrypted: false);
                    CheckLegacyZipRollbackProtection(input, encrypted: false);
                }))
                throw new IOException("A preserved ZIP stream changed during publication.");
        }
    }

    private static void RequireZipLinkIdentity(SafeFileHandle file, SafeFileHandle expected)
    {
        CheckLegacyZipRollbackProtection(file, encrypted: false);
        if (ProtectedHexFile.Identity(file) != ProtectedHexFile.Identity(expected))
            throw new IOException("The held ZIP file or stream no longer identifies the reviewed object.");
    }

    private static bool OrdinaryZipLinkFile(SafeFileHandle file) =>
        ((FileAttributes)GetBasic(file).FileAttributes & ~ZipLinkAttributes) == 0 && WindowsCompression.Query(file).Native == WindowsCompression.None;

    private static string DigestZipLinkStream(SafeFileHandle file, long length, Action checkpoint)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024];
        for (long offset = 0; offset < length;)
        {
            checkpoint();
            int read = RandomAccess.Read(file, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - offset)), offset);
            if (read == 0) throw new IOException("The reviewed ZIP stream changed length.");
            digest.AppendData(buffer.AsSpan(0, read));
            offset += read;
        }
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    private static unsafe bool HasNoZipObjectId(SafeFileHandle file)
    {
        byte* data = stackalloc byte[64];
        return !ReadZipObjectId(file, 0x0009009C /* FSCTL_GET_OBJECT_ID */, 0, 0, data, 64, out _, 0) && Marshal.GetLastPInvokeError() == 2;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ReadZipObjectId(SafeFileHandle file, uint operation, nint input, uint inputBytes,
        byte* output, uint outputBytes, out uint written, nint overlapped);
}
