using System.Runtime.InteropServices;

namespace FileCat.Core.Archives.Unix;

internal sealed partial class UnixArchiveFile
{
    internal ArchiveOriginSnapshot ReadOrigin(CancellationToken ct = default)
    {
        lock (parent.Gate) { Live(); ct.ThrowIfCancellationRequested(); Validate(); return ArchiveUnixOrigin.Read(held, ct); }
    }

    /// <summary>
    /// Copy opaque evidence only to this transaction's newly created sealed private artifact, before any successful
    /// rename. No erase/replacement/pathname mutation is available. This does not derive a strongest aggregate mark;
    /// the trusted coordinator must separately establish representability and provenance over every captured source.
    /// </summary>
    internal void CopyOriginNoReplace(ArchiveOriginSnapshot origin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        lock (parent.Gate)
        {
            Live(); ct.ThrowIfCancellationRequested();
            if (!artifact || !createdWritable || writable || namespaceChanged)
                throw new InvalidOperationException("Origin copying requires this transaction's sealed new private artifact before publication.");
            var format = UnixArchiveNative.Mac ? ArchiveOriginFormat.MacQuarantine : ArchiveOriginFormat.LinuxOriginUrl;
            if (origin.Format != format || origin.State == ArchiveOriginState.Unavailable)
                throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "The required Unix archive origin cannot be represented or read.");
            try
            {
                parent.VerifyNamespace(); parent.RequireName(KnownName, Identity); Validate(); parent.CheckPrivate(held);
                if (ReadOrigin(ct).State != ArchiveOriginState.Absent) throw OriginReview();
                if (origin.State == ArchiveOriginState.Absent)
                {
                    if (ReadOrigin(ct).State != ArchiveOriginState.Absent) throw OriginReview();
                    return;
                }
                var before = CaptureOuterMetadata(ct); var version = CaptureVersion(ct);
                if (CaptureOuterMetadata(ct).Digest != before.Digest) throw OriginReview();
                parent.RequireName(KnownName, Identity); ct.ThrowIfCancellationRequested();
                byte[] bytes = origin.Bytes.ToArray();
                int result = UnixArchiveNative.Mac
                    ? MacCreateOrigin(UnixArchiveNative.Fd(held), "com.apple.quarantine", bytes, bytes.Length, 0, 2 /* XATTR_CREATE */)
                    : LinuxCreateOrigin(UnixArchiveNative.Fd(held), "user.xdg.origin.url", bytes, bytes.Length, 1 /* XATTR_CREATE */);
                if (result != 0)
                {
                    if (Marshal.GetLastPInvokeError() == 17 /* EEXIST */) throw OriginReview();
                    throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "The Unix archive origin could not be created; retain the artifact without publication.");
                }
                // The native attribute operation has happened. Finish its flush/readback even if cancellation arrived;
                // cancellation is observed afterwards and never authorizes removing or overwriting this mark.
                UnixArchiveNative.SyncFile(held); parent.CheckPrivate(held);
                parent.VerifyNamespace(); parent.RequireName(KnownName, Identity);
                var after = CaptureOuterMetadata(CancellationToken.None);
                if (after.Origin.Digest != origin.Digest || after.Native with { Changed = before.Native.Changed } != before.Native ||
                    after.AclPresent != before.AclPresent || !after.AclBytes.SequenceEqual(before.AclBytes) ||
                    CaptureVersion(CancellationToken.None) != version) throw OriginReview();
            }
            catch (ArchiveWriteException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException)
            { throw OriginReview(); }
            ct.ThrowIfCancellationRequested();
        }
    }
    private static ArchiveWriteException OriginReview() => new(ArchiveWriteReason.NeedsReview,
        "The Unix archive artifact's name, origin or other evidence changed; retain it without replacing or erasing any mark.");
    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxCreateOrigin(int fd, string name, byte[] bytes, nint length, int flags);
    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacCreateOrigin(int fd, string name, byte[] bytes, nint length, uint position, int options);
}
