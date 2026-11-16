using System.Runtime.InteropServices;

namespace FileCat.Core.Archives.Unix;

internal sealed partial class UnixArchiveFile
{
    private bool updateMetadataApplied;

    /// <summary>
    /// Refuse, before any stage exists, an original whose promised outer metadata this profile cannot reproduce or whose
    /// macOS ACL denies access: a deny entry can block the name exchange or the exact cleanup after it.
    /// </summary>
    internal void RequireUpdateAdmissible(CancellationToken ct)
    {
        lock (parent.Gate)
        {
            Live(); ct.ThrowIfCancellationRequested();
            var metadata = CaptureOuterMetadata(ct);
            ArchiveUnixUpdateMetadata.RequireAdmissible(metadata, UnixArchiveNative.GetEuid());
            // Known before anything is rebuilt; otherwise found only when the replacement's metadata is applied.
            if (!parent.KeepsGroup(metadata.Native.Group)) throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, GroupNotKept);
            if (UnixArchiveNative.Mac && MacAclHasDeny(UnixArchiveNative.Fd(held)))
                throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, MacDenyAcl);
        }
    }

    internal const string GroupNotKept = "This archive's group cannot be kept by the current user, so it is read-only here.";

    internal const string MacDenyAcl =
        "This archive has a macOS access-control entry that denies some access. FileCat cannot replace it safely, so it is read-only here.";

    internal static unsafe bool MacAclHasDeny(int fd)
    {
        nint acl = MacGetAcl(fd, 0x100 /* ACL_TYPE_EXTENDED */);
        if (acl == 0)
        {
            if (Marshal.GetLastPInvokeError() == 2 /* ENOENT: no extended ACL */) return false;
            throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "The archive's macOS ACL could not be read; retain it for review.");
        }
        try
        {
            nint entry = 0;
            for (int id = 0 /* ACL_FIRST_ENTRY */, count = 0; MacAclEntry(acl, id, &entry) == 0; id = -1 /* ACL_NEXT_ENTRY */)
            {
                if (++count > 4096) return true;
                int tag = 0;
                if (MacAclTag(entry, &tag) != 0 || tag != 1 /* ACL_EXTENDED_ALLOW */) return true;
            }
            return false;
        }
        finally { MacAclFree(acl); }
    }

    /// <summary>
    /// Give this transaction's sealed, freshly verified stage the reviewed original's promised outer metadata, while the
    /// stage is still reachable only through its creator-private stage directory. Only held-descriptor calls are used.
    /// Content and the already applied origin mark are rechecked; any mismatch retains the stage for review. Returns the
    /// stage's new observation, which the exchange must use. Nothing is published or removed here.
    /// </summary>
    internal ArchiveFileObservation ApplyUpdateMetadata(ArchiveFileObservation verifiedStage, UnixArchiveFile original,
        ArchiveFileObservation reviewedOriginal, ArchiveOriginSnapshot requiredOrigin, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(verifiedStage); ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(reviewedOriginal); ArgumentNullException.ThrowIfNull(requiredOrigin);
        if (ReferenceEquals(this, original) || ReferenceEquals(parent, original.parent))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Update metadata requires a stage in the private stage directory.");
        lock (CrossDirectoryExchangeGate)
        lock (original.parent.Gate)
        lock (parent.Gate)
        {
            Live(); original.Live(); ct.ThrowIfCancellationRequested();
            if (!artifact || !createdWritable || createdKind != UnixArchiveArtifactKind.Stage || !rebuildClaimed || !verificationSealed ||
                evidenceClaimed || writable || namespaceChanged || updateMetadataApplied || original.artifact || original.writable || original.namespaceChanged)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Update metadata requires the sealed verified stage and the unchanged held original.");
            parent.RequirePrivateStageOf(original.parent);
            parent.RequireName(KnownName, Identity); parent.CheckPrivate(held);
            ((IArchiveRetainedFile)this).Review(verifiedStage, ct);
            ((IArchiveRetainedFile)original).Review(reviewedOriginal, ct);
            var source = original.CaptureOuterMetadata(ct);
            if (source.Digest != reviewedOriginal.MetadataDigest) throw Changed();
            ArchiveUnixUpdateMetadata.RequireAdmissible(source, UnixArchiveNative.GetEuid());
            if (UnixArchiveNative.Mac && MacAclHasDeny(UnixArchiveNative.Fd(original.held)))
                throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature,
                    "This archive has a macOS access-control entry that denies some access. FileCat cannot replace it safely, so it is read-only here.");
            var before = CaptureOuterMetadata(ct); var version = CaptureVersion(ct);
            if (before.Origin.Digest != requiredOrigin.Digest || before.Native.Owner != source.Native.Owner)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The stage's origin or owner differs from the reviewed update; retain it.");
            before.RequireSupportedExtendedAttributes();
            int fd = UnixArchiveNative.Fd(held); var target = source.Native;
            try
            {
                ct.ThrowIfCancellationRequested();
                if (before.Native.Group != target.Group && Fchown(fd, uint.MaxValue, target.Group) != 0)
                    throw Unsupported("This archive's group cannot be kept by the current user, so it is read-only here.", Marshal.GetLastPInvokeError());
                ApplyAcl(fd, source, before);
                if (Fchmod(fd, target.Mode & 0x1ff) != 0) throw Failed("permissions");
                uint keep = ArchiveUnixUpdateMetadata.Preservable(target.Format);
                var current = CaptureOuterMetadata(CancellationToken.None).Native;
                uint flags = (current.Flags & ~keep) | (target.Flags & keep);
                if (flags != current.Flags)
                {
                    if (UnixArchiveNative.Mac) { if (Fchflags(fd, flags) != 0) throw Failed("file flags"); }
                    else { ulong value = flags; unsafe { if (LinuxSetFlags(fd, 0x40086602 /* FS_IOC_SETFLAGS */, &value) != 0) throw Failed("file flags"); } }
                }
                if (UnixArchiveNative.Mac && target.Created is { } created && CaptureOuterMetadata(CancellationToken.None).Native.Created != created)
                    SetMacBirthTime(fd, created);
                UnixArchiveNative.SyncFile(held);
            }
            catch (ArchiveWriteException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException)
            { throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The stage's outer metadata could not be completed; retain it without publication."); }
            // Some native calls may already have happened. Finish the readback without cancellation.
            updateMetadataApplied = true;
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity);
            var after = CaptureOuterMetadata(CancellationToken.None);
            ArchiveUnixUpdateMetadata.RequirePromisedMatch(source, after, requiredOrigin);
            if (CaptureVersion(CancellationToken.None) != version || after.Native.Length != before.Native.Length || after.Native.Owner != before.Native.Owner)
                throw Changed();
            var observed = ObserveRetained(CancellationToken.None);
            if (observed.Version != verifiedStage.Version || observed.LeafName != verifiedStage.LeafName ||
                observed.ParentIdentity != verifiedStage.ParentIdentity || observed.Origin.Digest != requiredOrigin.Digest) throw Changed();
            return observed;
        }

        void ApplyAcl(int fd, ArchiveUnixOuterMetadata source, ArchiveUnixOuterMetadata stage)
        {
            if (UnixArchiveNative.Mac)
            {
                if (!source.AclPresent)
                {
                    if (stage.AclPresent) throw Unsupported("The private stage unexpectedly carries an extended ACL; retain it.", 0);
                    return;
                }
                byte[] bytes = source.AclBytes.ToArray(); nint acl = MacAclFromExternal(bytes);
                if (acl == 0) throw Failed("ACL");
                try { if (MacSetAcl(fd, acl, 0x100 /* ACL_TYPE_EXTENDED */) != 0) throw Failed("ACL"); }
                finally { MacAclFree(acl); }
                return;
            }
            if (source.AclPresent)
            {
                byte[] bytes = source.AclBytes.ToArray();
                if (LinuxSetXattr(fd, "system.posix_acl_access", bytes, bytes.Length, 0) != 0) throw Failed("ACL");
            }
            else if (stage.AclPresent && LinuxRemoveXattr(fd, "system.posix_acl_access") != 0 && Marshal.GetLastPInvokeError() != 61 /* ENODATA */)
                throw Failed("ACL");
        }
    }

    private static unsafe void SetMacBirthTime(int fd, ArchiveUnixOuterMetadata.Timestamp created)
    {
        // struct attrlist { u_short bitmapcount; u_int16_t reserved; attrgroup_t common, volume, directory, file, fork; }
        byte* list = stackalloc byte[24]; new Span<byte>(list, 24).Clear();
        *(ushort*)list = 5 /* ATTR_BIT_MAP_COUNT */; *(uint*)(list + 4) = 0x200 /* ATTR_CMN_CRTIME */;
        long* value = stackalloc long[2]; value[0] = created.Seconds; value[1] = created.Nanoseconds;
        if (MacSetAttributes(fd, list, value, 16, 0) != 0) throw Failed("creation time");
    }

    private static ArchiveWriteException Failed(string what) => new(ArchiveWriteReason.NeedsReview,
        "The stage's " + what + " could not be set to the reviewed original's value (errno " + Marshal.GetLastPInvokeError() + "); retain it without publication.");
    private static ArchiveWriteException Unsupported(string message, int errno) => new(ArchiveWriteReason.UnsupportedFeature,
        errno == 0 ? message : message + " (errno " + errno + ")");

    [LibraryImport("libc", EntryPoint = "fchown", SetLastError = true)]
    private static partial int Fchown(int fd, uint owner, uint group);
    [LibraryImport("libc", EntryPoint = "fchmod", SetLastError = true)]
    private static partial int Fchmod(int fd, uint mode);
    [LibraryImport("libc", EntryPoint = "fchflags", SetLastError = true)]
    private static partial int Fchflags(int fd, uint flags);
    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static unsafe partial int LinuxSetFlags(int fd, nuint request, ulong* value);
    [LibraryImport("libc", EntryPoint = "fsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxSetXattr(int fd, string name, byte[] value, nint size, int flags);
    [LibraryImport("libc", EntryPoint = "fremovexattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxRemoveXattr(int fd, string name);
    [LibraryImport("libc", EntryPoint = "acl_copy_int", SetLastError = true)]
    private static partial nint MacAclFromExternal(byte[] external);
    [LibraryImport("libc", EntryPoint = "acl_set_fd_np", SetLastError = true)]
    private static partial int MacSetAcl(int fd, nint acl, int type);
    [LibraryImport("libc", EntryPoint = "acl_free")]
    private static partial int MacAclFree(nint acl);
    [LibraryImport("libc", EntryPoint = "acl_get_fd_np", SetLastError = true)]
    private static partial nint MacGetAcl(int fd, int type);
    [LibraryImport("libc", EntryPoint = "acl_get_entry", SetLastError = true)]
    private static unsafe partial int MacAclEntry(nint acl, int entryId, nint* entry);
    [LibraryImport("libc", EntryPoint = "acl_get_tag_type", SetLastError = true)]
    private static unsafe partial int MacAclTag(nint entry, int* tag);
    [LibraryImport("libc", EntryPoint = "fsetattrlist", SetLastError = true)]
    private static unsafe partial int MacSetAttributes(int fd, void* list, void* buffer, nuint size, uint options);
}
