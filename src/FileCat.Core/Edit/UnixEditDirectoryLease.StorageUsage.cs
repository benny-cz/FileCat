using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

internal sealed partial class UnixEditDirectoryLease
{
    internal override EditStoredFileSize MeasureStoredFile(string name,CancellationToken ct)
    {
        lock(Gate)
        {
            using var file=OpenWorkingFile(name,ct);var before=ReviewWorkingFile(file,name,ct);
            var metadata=ArchiveUnixOuterMetadata.Capture(file.SafeFileHandle,ct);
            if(ReviewWorkingFile(file,name,ct)!=before||metadata.Native.Length!=before.Length)throw new IOException("The stored edit changed during size review.");
            return new(before,MetadataCharge(metadata));
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
