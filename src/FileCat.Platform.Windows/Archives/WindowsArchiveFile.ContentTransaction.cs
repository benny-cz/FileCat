using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Archives;

internal sealed partial class WindowsArchiveFile : IArchiveContentUpdateSource, IArchiveContentUpdateStage
{
    ArchiveContentUpdateRecipe IArchiveContentUpdateSource.Recipe => ArchiveContentUpdateRecipe.WindowsNtfsTransactionalData;
    ArchiveArtifactReservation IArchiveContentUpdateSource.ReserveContentSnapshot(Guid nonce, ArchiveSpaceEstimate prepared, CancellationToken ct)
        => ReserveContentSnapshot(nonce, prepared, ct);
    IArchiveContentTransaction IArchiveContentUpdateSource.BeginContentTransaction(ArchiveFileObservation reviewed, Guid nonce, TimeSpan timeout, CancellationToken ct)
        => BeginContentTransaction(reviewed, nonce, timeout, ct);
    string IArchiveContentUpdateStage.ReviewContentSnapshotCreation(ArchiveArtifactReservation reservation, CancellationToken ct)
        => ReviewContentSnapshotCreation(reservation, ct);
    void IArchiveContentUpdateStage.ReviewOwnedCleanup(ArchiveContentCleanupArtifact expected, string parentIdentity, CancellationToken ct)
        => ReviewOwnedCleanup(expected, parentIdentity, ct);
    void IArchiveContentUpdateStage.DeleteOwnedCleanup(ArchiveContentCleanupArtifact expected, string parentIdentity, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            ReviewOwnedCleanup(expected, parentIdentity, ct);
            var metadata = CaptureOuterMetadata(ct);
            if (metadata.Digest != expected.MetadataSha256) throw ChangedRetained();
            DeleteReviewed(expected.Version, metadata, ct);
        }
    }
    private void ReviewOwnedCleanup(ArchiveContentCleanupArtifact expected, string parentIdentity, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            EnsureLive(); ct.ThrowIfCancellationRequested();
            if (!artifact || !createdWritable || writable || namespaceChanged || !mayRename || !(rebuildClaimed || evidenceClaimed) ||
                createdKind != (WindowsArchiveArtifactKind)expected.Kind || parent.Identity.ToString() != parentIdentity ||
                !string.Equals(DisplayPath, expected.Path, StringComparison.OrdinalIgnoreCase))
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "Cleanup requires this live creator-owned private artifact and its exact scope.");
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity); parent.CheckPrivate(handle);
            var actual = ObserveRetained(ct);
            if (actual.Version != expected.Version || actual.MetadataDigest != expected.MetadataSha256 || actual.Origin.Digest != expected.OriginSha256)
                throw ChangedRetained();
            WindowsArchiveNative.RequireCreationStreams(handle, actual.Version.Length, actual.Origin);
        }
    }
    internal void ReviewRecoveredCleanup(ArchiveContentCleanupArtifact expected, string parentIdentity, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            EnsureLive(); ct.ThrowIfCancellationRequested();
            if (!artifact || createdWritable || writable || namespaceChanged || !mayRename || createdKind != (WindowsArchiveArtifactKind)expected.Kind ||
                parent.Identity.ToString() != parentIdentity || !string.Equals(DisplayPath, expected.Path, StringComparison.OrdinalIgnoreCase))
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "Recovery requires the exact catalog-bound native artifact.");
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity); parent.CheckPrivate(handle);
            var actual = ObserveRetained(ct);
            if (actual.Version != expected.Version || actual.MetadataDigest != expected.MetadataSha256 || actual.Origin.Digest != expected.OriginSha256) throw ChangedRetained();
            WindowsArchiveNative.RequireCreationStreams(handle, actual.Version.Length, actual.Origin);
        }
    }
    /// <summary>The recovery coordinator must durably record intent under its exclusive retained journal first.</summary>
    /// <summary>
    /// Discard of an unpublished transaction's own artifact, reopened by its recorded identity in the held archive folder:
    /// private security, the same name and (for a sealed sidecar) the recorded version, then deletion of the exact held
    /// object through its handle. A partial stage has no recorded version; its identity and private security bind it.
    /// </summary>
    internal void DeleteDiscarded(ArchiveObjectVersion? expected, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            EnsureLive(); ct.ThrowIfCancellationRequested();
            if (!artifact || createdWritable || writable || namespaceChanged || !mayRename)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "Discard requires the exact recorded private artifact.");
            parent.VerifyNamespace(); parent.RequireName(KnownName, Identity); parent.CheckPrivate(handle);
            if (expected is not null && ObserveRetained(ct).Version != expected) throw ChangedRetained();
            WindowsArchiveNative.DeleteExact(handle);
            // POSIX disposition unlinks when this deleting handle closes.
            removed = true; handle.Dispose();
        }
    }
    internal bool IsRemoved { get { lock (parent.Gate) return removed; } }
    internal void DeleteRecoveredCleanup(ArchiveContentCleanupArtifact expected, string parentIdentity, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            ReviewRecoveredCleanup(expected, parentIdentity, ct); var metadata = CaptureOuterMetadata(ct);
            if (metadata.Digest != expected.MetadataSha256) throw ChangedRetained();
            DeleteReviewed(expected.Version, metadata, ct);
        }
    }
    internal WindowsArchiveFile CreateSiblingArtifact(ArchiveArtifactReservation reservation)
    {
        lock (parent.Gate) { EnsureLive(); return parent.CreateArtifact(reservation); }
    }

    internal string ReviewContentSnapshotCreation(ArchiveArtifactReservation reservation, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            // ReviewCreation returns an object identity, not a metadata digest. Capture the actual queried native
            // security state separately, while rereviewing the issuer-bound fresh empty private artifact.
            if (((IArchiveRetainedArtifact)this).ReviewCreation(reservation, ct) != Identity.ToString()) throw ChangedRetained();
            var first = WindowsArchiveNative.ReadOuterSecurity(handle);
            ((IArchiveRetainedArtifact)this).ReviewCreation(reservation, ct);
            var second = WindowsArchiveNative.ReadOuterSecurity(handle);
            if (!first.SequenceEqual(second)) throw ChangedRetained();
            return Convert.ToHexString(SHA256.HashData(second));
        }
    }

    internal ArchiveArtifactReservation ReserveContentSnapshot(Guid nonce, ArchiveSpaceEstimate prepared, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            EnsureLive(); ct.ThrowIfCancellationRequested();
            ArchiveOuterEfs.RequireClear(handle);
            if (artifact || writable || namespaceChanged || RandomAccess.GetLength(handle) != prepared.OriginalBytes)
                throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The original no longer matches the native update space review.");
            var estimate = new ArchiveSpaceEstimate(prepared.OriginalBytes, prepared.StageBytes, prepared.EvidenceBytes, prepared.KnownSnapshotBytes,
                retainedOriginalCopyBytes: prepared.OriginalBytes, publicationScratchBytes: Math.Max(prepared.OriginalBytes, prepared.StageBytes));
            ((IArchiveDestinationSpace)parent).ReviewSpace(parent.Identity.ToString(), estimate, ct).RequireAvailable();
            return parent.ReserveArtifact(nonce, WindowsArchiveArtifactKind.Stage, prepared.OriginalBytes);
        }
    }

    /// <summary>Consumes this source lease. Caller must separately journal the backup, transaction and commit intent.</summary>
    internal ContentTransaction BeginContentTransaction(ArchiveFileObservation reviewed, Guid nonce, TimeSpan timeout, CancellationToken ct)
    {
        lock (parent.Gate)
        {
            EnsureLive(); ct.ThrowIfCancellationRequested();
            ArchiveOuterEfs.RequireClear(handle);
            if (artifact || writable || namespaceChanged || nonce == Guid.Empty || timeout.TotalMilliseconds is < 1 or > int.MaxValue)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A live original source and finite transaction scope are required.");
            ((IArchiveRetainedFile)this).Review(reviewed, ct);
            if ((parent.Volume.Flags & 0x00200000 /* FILE_SUPPORTS_TRANSACTIONS */) == 0)
                throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "The held NTFS volume does not advertise transaction support.");
            return new ContentTransaction(this, reviewed, nonce, (uint)timeout.TotalMilliseconds, ct);
        }
    }

    internal void RequireClearUpdateSource()
    {
        lock (parent.Gate)
        {
            EnsureLive();
            ArchiveOuterEfs.RequireClear(handle);
        }
    }

    /// <summary>
    /// Unregistered same-object data primitive. Private changes require an exact retained original snapshot and a
    /// sealed verified output. No codec, journal, deletion, arbitrary security copying or generic overwrite fallback.
    /// </summary>
    internal sealed partial class ContentTransaction : IArchiveContentTransaction
    {
        internal sealed class Review : ArchiveContentUpdateReview
        {
            internal readonly ContentTransaction Issuer;
            internal Guid TransactionId => Issuer.TransactionId;
            internal Review(ContentTransaction issuer, ArchiveFileObservation output, ArchiveFileObservation retained)
                : base(output, retained) { Issuer = issuer; }
        }
        Guid IArchiveContentTransaction.TransactionId => TransactionId;
        ArchiveFileObservation IArchiveContentTransaction.RetainOriginal(IArchiveContentUpdateStage destination, CancellationToken ct)
            => RetainOriginal(NativeStage(destination), ct);
        ArchiveContentUpdateReview IArchiveContentTransaction.PrepareOutput(IArchiveContentUpdateStage output, ArchiveVerifiedNativeStage verified,
            ArchiveOriginSnapshot requiredOrigin, CancellationToken ct) => PrepareOutput(NativeStage(output), verified, requiredOrigin, ct);
        ArchiveFileObservation IArchiveContentTransaction.Commit(ArchiveContentUpdateReview review, CancellationToken ct)
            => Commit(NativeReview(review), ct);
        ArchiveFileObservation IArchiveContentTransaction.ReviewPublished(ArchiveContentUpdateReview review, CancellationToken ct)
            => ReviewPublished(NativeReview(review), ct);
        void IArchiveContentTransaction.Rollback() => Rollback();
        private static WindowsArchiveFile NativeStage(IArchiveContentUpdateStage file) => file as WindowsArchiveFile ?? throw Changed();
        private static Review NativeReview(ArchiveContentUpdateReview review) => review as Review ?? throw Changed();
        private enum Phase { Open, Retaining, BackedUp, Preparing, Ready, Committing, Committed, RolledBack, Uncertain, Disposed }
        private readonly WindowsArchiveDirectory parent;
        private readonly string leaf;
        private readonly FileIdentity identity;
        private readonly ArchiveFileObservation original;
        private readonly WindowsArchiveOuterMetadata originalMetadata;
        private readonly SafeFileHandle transaction;
        private SafeFileHandle? data, pin;
        private WindowsArchiveFile? published, backup, output;
        private ArchiveFileObservation? backupState, outputState;
        private bool ownsParentChild;
        private Phase phase;
        private Review? issued;
        internal Guid Nonce { get; }
        internal Guid TransactionId { get; }

        internal ContentTransaction(WindowsArchiveFile source, ArchiveFileObservation reviewed, Guid nonce, uint timeout, CancellationToken ct)
        {
            parent = source.parent; leaf = source.KnownName; identity = source.Identity; original = reviewed; Nonce = nonce;
            originalMetadata = source.CaptureOuterMetadata(ct);
            transaction = CreateTransaction(0, 0, 0, 0, 0, timeout, "FileCat private archive content update");
            if (transaction.IsInvalid) { int error = Marshal.GetLastPInvokeError(); transaction.Dispose(); throw Native(error, "Create native archive transaction"); }
            try
            {
                if (!GetTransactionId(transaction, out var id)) throw Native(Marshal.GetLastPInvokeError(), "Identify native archive transaction");
                TransactionId = id;
                // Transfer the existing child lifetime, without opening a seventeenth retained file or relaxing
                // the normal source sharing contract. The old object is deliberately unusable after this point.
                source.handle.Dispose(); source.disposed = true; ownsParentChild = true;
                data = Open(leaf, 0xc0000000, 7, 3);
                if (ProtectedHexFile.Identity(data) != identity) throw Changed();
                ArchiveOuterEfs.RequireClear(data);
                if (!Observe(data, ct).Matches(original)) throw Changed();
            }
            catch { Dispose(); throw; }
        }

        internal ArchiveFileObservation RetainOriginal(WindowsArchiveFile destination, CancellationToken ct)
        {
            lock (parent.Gate)
            {
                Require(Phase.Open); ct.ThrowIfCancellationRequested();
                ArchiveOuterEfs.RequireClear(data!);
                RequireStage(destination, original.Version.Length);
                var oldOrigin = ArchiveWindowsOriginZone.Parse(original.Origin);
                phase = Phase.Retaining; // A partially claimed snapshot cannot be replaced by a retry.
                var stream = ((IArchiveRetainedStage)destination).BeginRebuild(original.Version.Length, ct);
                string copied = Copy(data!, original.Version.Length, stream, ct);
                var retained = ((IArchiveRetainedStage)destination).SealForVerification(ct);
                // A separately retained byte copy must not shed the original's declared zone. Raw URL evidence
                // remains in the scoped provenance sidecar; the private snapshot receives the canonical zone.
                if (oldOrigin.DeclaredZone is { } zone)
                    retained = ((IArchiveCreatePublicationStage)destination).ApplyCreationOrigin(retained, ArchiveWindowsOriginZone.ZoneOnly(zone), ct);
                WindowsArchiveNative.RequireCreationStreams(destination.handle, retained.Version.Length, retained.Origin);
                if (copied != original.Version.Sha256 || retained.Version.Length != original.Version.Length || retained.Version.Sha256 != copied ||
                    !Observe(data!, ct).Matches(original)) throw Changed();
                backup = destination; backupState = retained; phase = Phase.BackedUp; return retained;
            }
        }

        internal Review PrepareOutput(WindowsArchiveFile verifiedFile, ArchiveVerifiedNativeStage verified,
            ArchiveOriginSnapshot requiredOrigin, CancellationToken ct)
        {
            lock (parent.Gate)
            {
                Require(Phase.BackedUp); ct.ThrowIfCancellationRequested();
                if (verified.Original is null || !verified.Original.Matches(original)) throw Changed();
                RequireStage(verifiedFile, verifiedFile.maximumLength);
                if (ReferenceEquals(verifiedFile, backup) || verifiedFile.Identity == backup!.Identity || !verifiedFile.rebuildClaimed || verifiedFile.writable)
                    throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A separate sealed rebuilt output is required.");
                ((IArchiveRetainedFile)verifiedFile).Review(verified.Stage, ct);
                ((IArchiveRetainedFile)backup).Review(backupState!, ct);
                RequireOrigin(requiredOrigin);
                if (!Observe(data!, ct).Matches(original)) throw Changed();
                phase = Phase.Preparing; // Failure cannot retry a partially modified private view.
                using (var source = ((IArchiveRetainedFile)verifiedFile).OpenReadView(verified.Stage, ct))
                {
                    RandomAccess.SetLength(data!, 0);
                    string copied = CopyToHandle(source, data!, verified.Stage.Version.Length, ct);
                    if (copied != verified.Stage.Version.Sha256) throw Changed();
                }
                if (requiredOrigin.Digest != original.Origin.Digest) WriteOrigin(requiredOrigin, ct);
                WindowsArchiveNative.Flush(data!);
                var ready = Observe(data!, ct);
                if (ready.Version.Sha256 != verified.Stage.Version.Sha256 || ready.Version.Length != verified.Stage.Version.Length ||
                    ready.Origin.Digest != requiredOrigin.Digest) throw Changed();
                RequirePreserved(CaptureMetadata(data!, ct));
                ((IArchiveRetainedFile)verifiedFile).Review(verified.Stage, ct);
                ((IArchiveRetainedFile)backup).Review(backupState!, ct);
                data!.Dispose(); data = null;
                // A normal deny-write read open is incompatible with a pending TxF writer. This same-transaction
                // read pin is measured to retain its sharing denial through commit and normal-reader adoption.
                pin = Open(leaf, 0x80000000, 1, 3);
                var pinned = Observe(pin, ct); if (!pinned.Matches(ready)) throw Changed();
                output = verifiedFile; outputState = verified.Stage; phase = Phase.Ready;
                return issued = new Review(this, pinned, backupState!);
            }
        }

        internal ArchiveFileObservation Commit(Review review, CancellationToken ct)
        {
            lock (parent.Gate)
            {
                Require(Phase.Ready);
                if (!ReferenceEquals(review, issued) || !ReferenceEquals(review.Issuer, this)) throw Changed();
                ct.ThrowIfCancellationRequested();
                ((IArchiveRetainedFile)backup!).Review(backupState!, ct);
                ((IArchiveRetainedFile)output!).Review(outputState!, ct);
                if (!Observe(pin!, ct).Matches(review.PrivateOutput)) throw Changed();
                ct.ThrowIfCancellationRequested(); phase = Phase.Committing;
                if (!CommitTransaction(transaction)) { phase = Phase.Uncertain; throw Native(Marshal.GetLastPInvokeError(), "Commit archive content", uncertain: true); }
                phase = Phase.Committed;
                // No cancellation after a possible public effect. Preserve the pin until the normal deny-write
                // reader owns the transferred parent lifetime; failure retains all objects and is NeedsReview.
                try
                {
                    var handle = NtFile.OpenOrThrow(parent.Handle, leaf, NtFile.GenericRead | NtFile.Synchronize | 0x20000,
                        NtFile.ShareRead, NtFile.Open, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint, "Pin committed archive content");
                    try
                    {
                        WindowsArchiveNative.Regular(handle, ArchiveWritePolicy.MaxPhysicalBytes, artifact: false, mayRename: false);
                        if (ProtectedHexFile.Identity(handle) != identity) throw Changed();
                        published = new WindowsArchiveFile(parent, handle, leaf, identity, ArchiveWritePolicy.MaxPhysicalBytes, false, false, false);
                        ownsParentChild = false; // The published reader now owns the already-counted source child.
                    }
                    catch { handle.Dispose(); throw; }
                    var actual = ((IArchiveRetainedFile)published).Observe(CancellationToken.None);
                    RequirePublished(actual, review);
                    pin!.Dispose(); pin = null; return actual;
                }
                catch (Exception error)
                {
                    phase = Phase.Uncertain;
                    throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview,
                        "Archive content may have been committed; retain the archive, snapshot and staged output for review.", error);
                }
            }
        }

        internal ArchiveFileObservation ReviewPublished(Review review, CancellationToken ct)
        {
            lock (parent.Gate)
            {
                Require(Phase.Committed);
                if (!ReferenceEquals(review, issued) || published is null) throw Changed();
                var actual = ((IArchiveRetainedFile)published).Observe(ct); RequirePublished(actual, review); return actual;
            }
        }
        internal void Rollback()
        {
            lock (parent.Gate)
            {
                if (phase is Phase.Committing or Phase.Committed or Phase.Uncertain or Phase.Disposed) throw Changed();
                if (phase == Phase.RolledBack) return;
                if (!RollbackTransaction(transaction)) { phase = Phase.Uncertain; throw Native(Marshal.GetLastPInvokeError(), "Rollback private archive content", true); }
                phase = Phase.RolledBack;
            }
        }
        public void Dispose()
        {
            lock (parent.Gate)
            {
                if (phase == Phase.Disposed) return;
                try { published?.Dispose(); pin?.Dispose(); data?.Dispose(); transaction.Dispose(); }
                finally { if (ownsParentChild) { ownsParentChild = false; parent.ReleaseChild(); } phase = Phase.Disposed; }
            }
        }

        private ArchiveFileObservation Observe(SafeFileHandle file, CancellationToken ct)
        {
            parent.VerifyNamespace(); parent.RequireName(leaf, identity);
            var before = CaptureMetadata(file, ct); long length = RandomAccess.GetLength(file);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[128 * 1024];
            for (long offset = 0; offset < length;)
            {
                ct.ThrowIfCancellationRequested(); int read = RandomAccess.Read(file, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - offset)), offset);
                if (read == 0) throw Changed(); digest.AppendData(buffer, 0, read); offset += read;
            }
            var after = CaptureMetadata(file, ct); if (after.Digest != before.Digest || RandomAccess.GetLength(file) != length) throw Changed();
            return new(new(identity.ToString(), length, Convert.ToHexString(digest.GetHashAndReset())), parent.Identity.ToString(), leaf,
                after.Digest, after.Origin, new DateTimeOffset(DateTime.FromFileTimeUtc(after.LastWriteTime), TimeSpan.Zero), 0x1a4);
        }
        private WindowsArchiveOuterMetadata CaptureMetadata(SafeFileHandle file, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (ProtectedHexFile.Identity(file) != identity || WindowsArchiveNative.Regular(file, ArchiveWritePolicy.MaxPhysicalBytes, false, true) != 1) throw Changed();
            return new(identity, NtFile.GetBasic(file), 1, WindowsArchiveNative.ReadOuterSecurity(file), ReadOrigin(ct));
        }
        private ArchiveOriginSnapshot ReadOrigin(CancellationToken ct)
        {
            using var mark = CreateFileTransacted(Path.Combine(parent.DisplayPath, leaf) + ":Zone.Identifier", 0x80000000, 7, 0, 3, 0x00200000, 0, transaction, 0, 0);
            if (mark.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == 2) return ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier);
                throw Native(error, "Read transactional archive origin");
            }
            WindowsArchiveNative.Regular(mark, ArchiveOriginSnapshot.MaxBytes, false, false);
            if (ProtectedHexFile.Identity(mark) != identity) throw Changed();
            long length = RandomAccess.GetLength(mark); byte[] bytes = new byte[(int)length];
            for (int offset = 0; offset < bytes.Length;)
            { ct.ThrowIfCancellationRequested(); int read = RandomAccess.Read(mark, bytes.AsSpan(offset), offset); if (read == 0) throw Changed(); offset += read; }
            if (RandomAccess.GetLength(mark) != length) throw Changed();
            return ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, bytes);
        }
        private void WriteOrigin(ArchiveOriginSnapshot required, CancellationToken ct)
        {
            if (ReadOrigin(ct).Digest != original.Origin.Digest || required.State != ArchiveOriginState.Present) throw Changed();
            using var mark = Open(leaf + ":Zone.Identifier", 0xc0000000, 7, original.Origin.State == ArchiveOriginState.Absent ? 1u : 3u);
            ct.ThrowIfCancellationRequested(); RandomAccess.Write(mark, required.Bytes, 0); RandomAccess.SetLength(mark, required.Bytes.Length);
            WindowsArchiveNative.Flush(mark);
            if (ReadOrigin(CancellationToken.None).Digest != required.Digest) throw Changed();
            ct.ThrowIfCancellationRequested();
        }
        private void RequireOrigin(ArchiveOriginSnapshot required)
        {
            var old = ArchiveWindowsOriginZone.Parse(original.Origin); var next = ArchiveWindowsOriginZone.Parse(required);
            if (old.IsAbsent && next.IsAbsent) return;
            if (next.DeclaredZone is not { } zone || old.DeclaredZone is { } previous && zone < previous ||
                (old.IsAbsent ? ArchiveWindowsOriginZone.ZoneOnly(zone) : old.RaiseTo(zone)).Digest != required.Digest) throw Changed();
        }
        private void RequirePreserved(WindowsArchiveOuterMetadata actual)
        {
            if (actual.Identity != originalMetadata.Identity || actual.CreationTime != originalMetadata.CreationTime || actual.LinkCount != 1 ||
                actual.Attributes != originalMetadata.Attributes && actual.Attributes != (originalMetadata.Attributes | (uint)FileAttributes.Archive) ||
                !actual.SecurityDescriptor.SequenceEqual(originalMetadata.SecurityDescriptor)) throw Changed();
        }
        private void RequirePublished(ArchiveFileObservation actual, Review review)
        {
            if (actual.Version != review.PrivateOutput.Version || actual.Origin.Digest != review.PrivateOutput.Origin.Digest ||
                actual.MetadataDigest != review.PrivateOutput.MetadataDigest) throw Changed();
            RequirePreserved(published!.CaptureOuterMetadata(CancellationToken.None));
        }
        private void RequireStage(WindowsArchiveFile file, long bound)
        {
            file.EnsureLive();
            if (!ReferenceEquals(file.parent, parent) || !file.artifact || !file.createdWritable || file.createdKind != WindowsArchiveArtifactKind.Stage ||
                file.namespaceChanged || file.Identity == identity || file.maximumLength != bound) throw Changed();
        }
        private void Require(Phase wanted) { if (phase != wanted) throw new InvalidOperationException("The native content transaction is not in the required phase."); }
        private SafeFileHandle Open(string name, uint access, uint sharing, uint disposition)
        {
            var result = CreateFileTransacted(Path.Combine(parent.DisplayPath, name), access, sharing, 0, disposition, 0x00200000, 0, transaction, 0, 0);
            if (!result.IsInvalid) return result;
            int error = Marshal.GetLastPInvokeError(); result.Dispose(); throw Native(error, "Open native transactional archive view");
        }
        private static string Copy(SafeFileHandle source, long length, Stream destination, CancellationToken ct)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[128 * 1024];
            for (long offset = 0; offset < length;)
            { ct.ThrowIfCancellationRequested(); int read = RandomAccess.Read(source, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - offset)), offset); if (read == 0) throw Changed(); destination.Write(buffer, 0, read); hash.AppendData(buffer, 0, read); offset += read; }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        private static string CopyToHandle(Stream source, SafeFileHandle destination, long length, CancellationToken ct)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[128 * 1024];
            for (long offset = 0; offset < length;)
            { ct.ThrowIfCancellationRequested(); int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, length - offset)); if (read == 0) throw Changed(); RandomAccess.Write(destination, buffer.AsSpan(0, read), offset); hash.AppendData(buffer, 0, read); offset += read; }
            if (source.ReadByte() != -1 || RandomAccess.GetLength(destination) != length) throw Changed();
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        private static ArchiveWriteException Changed() => new(ArchiveWriteReason.NeedsReview, "The transactional archive objects, bytes, origin or security state changed; retain every artifact.");
        private static ArchiveWriteException Native(int error, string action, bool uncertain = false) =>
            new(uncertain ? ArchiveWriteReason.NeedsReview : ArchiveWriteReason.PublicationUnavailable, action + " failed; retain the archive and artifacts.", new Win32Exception(error));

        [LibraryImport("ktmw32.dll", EntryPoint = "CreateTransaction", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        private static partial SafeFileHandle CreateTransaction(nint security, nint unitOfWork, uint options, uint isolation, uint isolationFlags, uint timeout, string description);
        [LibraryImport("ktmw32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetTransactionId(SafeFileHandle transaction, out Guid id);
        [LibraryImport("kernel32.dll", EntryPoint = "CreateFileTransactedW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        private static partial SafeFileHandle CreateFileTransacted(string path, uint access, uint sharing, nint security, uint disposition, uint flags, nint template, SafeFileHandle transaction, nint miniVersion, nint extended);
        [LibraryImport("ktmw32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CommitTransaction(SafeFileHandle transaction);
        [LibraryImport("ktmw32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool RollbackTransaction(SafeFileHandle transaction);
    }
}
