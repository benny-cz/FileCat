using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

internal sealed record SevenZipLogicalMember(SevenZipRawMember Physical, string Sha256, IReadOnlyList<SevenZipPackedWitness> Packed);
internal sealed record SevenZipLogicalCatalog(SevenZipPhysicalCatalog Physical, IReadOnlyList<SevenZipLogicalMember> Members,
    long RetainedHeapBytes, long PeakDecoderHeapBytes);

/// <summary>Complete source observation. No tolerant reader, pathname, expected-output hashes or publication authority.</summary>
internal static class StrictSevenZipCatalog
{
    private const string EmptySha256 = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
    internal static SevenZipLogicalCatalog Read(Stream source, CancellationToken ct, SevenZipReadPasswordSession? password = null)
    {
        var physical = StrictSevenZipFraming.Read(source, ct, password);
        if (!physical.EncodedHeaderEndVerified) throw Invalid();
        if (physical.EncodedHeader is { } header && header.Folders.Any(folder => folder.Encrypted) &&
            header.Folders[0].Crc32 is null && header.MemberCrc32[0] is null) throw Unsupported();
        var budget = new ArchiveWriteBudget();
        budget.AddMetadata(0, checked(physical.RetainedHeapBytes + physical.Files.Members.Count * 8L + ArchiveIoAuthority.MaximumReadBytes));
        long work = 0;
        foreach (var folder in physical.Streams.Folders)
            foreach (long length in folder.UnpackedSizes)
            { ArchiveWriteBudget.Reserve(work, length, 4 * ArchiveWritePolicy.MaxLogicalBytes); work += length; }
        var members = new List<SevenZipLogicalMember>(physical.Files.Members.Count); long peak = 0;
        byte[] buffer = new byte[ArchiveIoAuthority.MaximumReadBytes];
        try
        {
            foreach (var member in physical.Files.Members)
            {
                ct.ThrowIfCancellationRequested(); budget.AddMetadata(0, 256);
                if (member.Folder is not { } index) { members.Add(new(member, EmptySha256, [])); continue; }
                var folder = physical.Streams.Folders[index];
                if (folder.Encrypted && password is null) throw ArchiveWriteException.PasswordRequired();
                if (folder.Encrypted && folder.Crc32 is null && member.Crc32 is null) throw Unsupported();
                budget.AddMetadata(0, checked(folder.PackedInputs.Count * 256L));
                // Each member releases its decoder graph before the next member. Persistent catalog observations
                // accumulate; transient decoder reservations are simultaneous per graph, never cumulative per file.
                var live = new ArchiveWriteBudget(); live.AddMetadata(0, budget.RetainedHeapBytes);
                using var decoder = new StrictSevenZipFolderDecoder(source, folder, physical.Streams.Packed, password, live, ct);
                peak = Math.Max(peak, decoder.ReservedDecoderHeapBytes);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var crc = new ArchiveCrc32(); long left = member.Length;
                while (left > 0)
                {
                    ct.ThrowIfCancellationRequested(); int n = decoder.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, left)));
                    if (n <= 0 || n > left) throw Invalid(); hash.AppendData(buffer, 0, n); crc.Append(buffer.AsSpan(0, n)); left -= n;
                }
                if (decoder.ReadByte() != -1 || !decoder.Complete || member.Crc32 is { } expected && crc.Value != expected ||
                    folder.Crc32 is { } folderCrc && crc.Value != folderCrc) throw Invalid();
                members.Add(new(member, Convert.ToHexString(hash.GetHashAndReset()), decoder.Witnesses));
            }
            if (StrictSevenZipFraming.WholeHash(source, physical.Length, ct) != physical.Revision) throw Changed();
            return new(physical, members.AsReadOnly(), budget.RetainedHeapBytes, peak);
        }
        catch (ArchiveWriteException) { throw; }
        catch (InvalidDataException ex) when (ex.InnerException is ArchiveWriteException) { throw (ArchiveWriteException)ex.InnerException; }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or SharpCompress.Common.SharpCompressException or ArgumentException)
        { throw new ArchiveWriteException(ArchiveWriteReason.InvalidArchive, "The 7z member graph, checksum, password or complete stream ending is invalid.", ex); }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidArchive, "The complete 7z member size, checksum or stream graph does not match.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The complete 7z source changed during payload verification.");
    private static ArchiveWriteException Unsupported() => new(ArchiveWriteReason.UnsupportedFeature, "Encrypted 7z data needs its job password and a plaintext checksum to establish the password.");
}
