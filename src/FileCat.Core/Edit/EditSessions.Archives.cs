using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Content;

namespace FileCat.Core.Edit;

public sealed partial class EditSessionStore
{
    internal EditSessionRecord? FindArchive(string archivePath, ArchiveWriteFormat format, string identity, string rawPath) =>
        LoadAll().FirstOrDefault(record => record.IsSupported && record.IsNativeArchive && record.ArchiveFormat == format &&
            record.MemberPath == rawPath && (record.ArchivePath == Path.GetFullPath(archivePath) || record.ArchiveIdentity == identity));

    internal EditSessionRecord CreateArchive(IArchiveEditSource source, string displayName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source); ct.ThrowIfCancellationRequested();
        var target = source.Target; var member = ArchiveEditEvidence.UniqueMember(target, source.Member.Name);
        if (!ReferenceEquals(member, source.Member)) throw new IOException("The edit member does not belong to its complete archive assessment.");
        source.ReviewSource(ct); string workingIdentity = "";
        return CreateSession(displayName, working =>
        {
            var content = ProgressiveContent.Sequential(source.OpenContent(ct)) ?? throw new IOException("The archive member cannot be opened for editing.");
            return ReadOwned(content, () =>
            {
                var copy = WriteWorkingCopy(content, working, ct, finalize: output =>
                {
                    workingIdentity = source.WorkingIdentity(output.SafeFileHandle);
                    if (string.IsNullOrEmpty(workingIdentity)) throw new IOException("The private edit file cannot be identified safely.");
                    source.PreserveOrigin(output, ct);
                });
                if (copy.Length != member.Length || copy.Sha256 != member.Sha256)
                    throw new IOException("The extracted member does not match its complete assessed bytes; no edit session was created.");
                source.ReviewSource(ct); return copy;
            });
        }, (working, copy) =>
        {
            string? directoryIdentity = fs.GetFileIdentity(Path.GetDirectoryName(working)!);
            if (string.IsNullOrEmpty(directoryIdentity) || fs.GetFileIdentity(working) != workingIdentity)
                throw new IOException("The private edit copy or its directory changed before its record could be published.");
            source.ReviewSource(ct);
            return NativeRecord(new EditSessionRecord { WorkingPath = working, WorkingIdentity = workingIdentity,
                WorkingDirectoryIdentity = directoryIdentity }, target, member, copy.Sha256);
        }, ct);
    }

    internal static CommitCheck ReviewArchive(EditSessionRecord record, ArchiveTargetReview target)
    {
        if (!record.IsSupported || !record.IsNativeArchive || record.ArchiveFormat != target.Profile.Format)
            throw new NotSupportedException("This session does not describe this archive edit profile.");
        ArchiveManifestMember member;
        try { member = ArchiveEditEvidence.UniqueMember(target, record.MemberPath); }
        catch (IOException) { return CommitCheck.MemberChanged; }
        if (member.Length != record.MemberLength || member.Sha256 != record.BaseSha256 ||
            ArchiveEditEvidence.Metadata(member) != record.MemberMetadataSha256) return CommitCheck.MemberChanged;
        return record.Baseline == target.Baseline && record.ArchiveIdentity == target.Native.Version.Identity &&
            record.ArchiveParentIdentity == target.Native.ParentIdentity && record.ArchiveMetadataDigest == target.Native.MetadataDigest
                ? CommitCheck.Ready : CommitCheck.ArchiveChanged;
    }

    internal static ArchivePlan CommitArchivePlan(EditSessionRecord record, ArchiveTargetReview target, EditCommitCopy copy, bool acceptRebase)
    {
        var check = ReviewArchive(record, target);
        if (check == CommitCheck.MemberChanged || check == CommitCheck.ArchiveChanged && !acceptRebase)
            throw new IOException("The archive edit needs a fresh explicit conflict/rebase decision. Your working copy is kept.");
        var member = ArchiveEditEvidence.UniqueMember(target, record.MemberPath);
        return target.CreatePlan([new(ArchiveChangeKind.Replace, member.Name, copy.Path, Ordinal: member.Occurrence)]);
    }

    internal EditSessionRecord CommittedArchive(EditSessionRecord record, IArchiveEditSource actual, EditCommitCopy copy,
        ArchiveEditPublication publication, CancellationToken ct)
    {
        if (!record.IsSupported || !record.IsNativeArchive || Path.GetFullPath(record.ArchivePath) != publication.Path ||
            actual.Target.Profile != publication.Profile || actual.Target.Assessment.Manifest.Digest != publication.ManifestDigest ||
            !actual.Target.Native.Matches(publication.Native))
            throw new IOException("The published archive no longer matches this completed edit. The previous session baseline is kept.");
        var member = ArchiveEditEvidence.UniqueMember(actual.Target, record.MemberPath);
        if (member.Length != copy.Length || member.Sha256 != copy.Sha256)
            throw new IOException("The actual published member does not match the authorized commit copy.");
        var content = ProgressiveContent.Sequential(actual.OpenContent(ct)) ?? throw new IOException("The published member cannot be read back.");
        ReadOwned(content, () =>
        {
            CheckComplete(content);
            if (content.Length != copy.Length) throw new IOException("The published member length changed before acknowledgement.");
            var revision = content.GetRevision();
            if (revision is { } before && before.Length != copy.Length) throw new IOException("The published member revision has a different length.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[1024 * 1024]; long offset = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested(); CheckComplete(content); int want = (int)Math.Min(buffer.Length, copy.Length - offset + 1);
                int read = content.Read(offset, buffer.AsSpan(0, want));
                ct.ThrowIfCancellationRequested(); CheckComplete(content);
                if (read < 0 || read > want || read > copy.Length - offset) throw new IOException("The published member changed during readback.");
                if (read == 0) { if (offset != copy.Length) throw new IOException("The published member ended before complete readback."); break; }
                hash.AppendData(buffer, 0, read); offset += read;
            }
            if (content.Length != copy.Length || content.GetRevision() != revision)
                throw new IOException("The published member changed during complete readback.");
            CheckComplete(content);
            if (Convert.ToHexString(hash.GetHashAndReset()) != copy.Sha256) throw new IOException("The published member readback does not match the commit copy.");
            actual.ReviewSource(ct); return new WorkingCopy(copy.Sha256, copy.Crc32, copy.Length);
        });
        // Content close can fail or expose a source change too; no durable acknowledgement precedes it.
        actual.ReviewSource(ct);
        ct.ThrowIfCancellationRequested();
        var updated = NativeRecord(record, actual.Target, member, copy.Sha256) with { LastCommitUtc = DateTime.UtcNow };
        Save(updated); return updated;
    }

    private static EditSessionRecord NativeRecord(EditSessionRecord record, ArchiveTargetReview target, ArchiveManifestMember member, string sha256) =>
        record with
        {
            Version = EditSessionRecord.CurrentVersion, ArchivePath = target.Path, ArchiveFormat = target.Profile.Format, ArchiveProfile = target.Profile,
            ArchiveIdentity = target.Native.Version.Identity, ArchiveParentIdentity = target.Native.ParentIdentity, ArchiveMetadataDigest = target.Native.MetadataDigest,
            Baseline = target.Baseline, MemberPath = member.Name, MemberOccurrence = member.Occurrence, MemberCatalogRevision = target.Assessment.CatalogRevision,
            MemberLength = member.Length, MemberMetadataSha256 = ArchiveEditEvidence.Metadata(member), BaseSha256 = sha256, MemberCrc32 = 0,
        };
}
