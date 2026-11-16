using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Edit;

internal sealed partial class UnixEditDirectoryLease
{
    internal override EditStoredFileSize MeasureStoredFile(string name,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            using var file=UnixArchiveNative.Child(directory,name);
            if(!isPrivate||UnixFiles.Stat(file) is not {IsRegular:true,Owner:var owner}||owner!=user)
                throw new IOException("The stored edit is not a creator-owned ordinary file.");
            var metadata=ArchiveUnixOuterMetadata.Capture(file,ct);
            using var named=UnixArchiveNative.Child(directory,name);
            if(UnixFiles.Stat(named)?.Identity!=metadata.Identity||ArchiveUnixOuterMetadata.Capture(file,ct).Digest!=metadata.Digest)
                throw new IOException("The stored edit changed during size review.");
            ReviewCore(ct);return new(metadata.Identity.ToString(),metadata.Native.Length,metadata.Digest,MetadataCharge(metadata));
        }
    }
    internal override EditStoredDirectorySize MeasureStoredDirectory(CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();ReviewCore(ct);
            var metadata=ArchiveUnixOuterMetadata.CaptureDirectory(directory,ct);ReviewCore(ct);
            if(metadata.Identity.ToString()!=Identity)throw new IOException("The stored edit directory changed during size review.");
            return new(Identity,metadata.Digest,MetadataCharge(metadata));
        }
    }
    private static long MetadataCharge(ArchiveUnixOuterMetadata metadata)
    {
        long bytes=checked(128L+metadata.AclBytes.Length+metadata.Origin.Bytes.Length);
        if(metadata.ExtendedAttributes is not { } attributes)throw new IOException("The stored edit attributes cannot be accounted completely.");
        foreach(var attribute in attributes.Attributes)
            bytes=checked(bytes+8+ArchiveWritePolicy.Utf8.GetByteCount(attribute.Name)+attribute.Value.Length);
        return bytes;
    }
}
