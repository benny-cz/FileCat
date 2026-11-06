using FileCat.Core.FileSystem;

namespace FileCat.Core.Archives.Unix;

internal sealed partial class UnixArchiveDirectory
{
    internal void RequireSelectionRoot() { lock (Gate) { RequireCapacity(); VerifyNamespace(); } }
    internal void CaptureSourceDirectory(ArchiveCapturedInputs inputs, int id, string leaf, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inputs); UnixArchiveNative.Leaf(leaf);
        inputs.CaptureDirectory(id, token => ObserveSourceDirectory(leaf, token), ct);
    }

    internal ArchiveDirectoryObservation ObserveSelectionDirectory(string leaf, CancellationToken ct) => ObserveSourceDirectory(leaf, ct);
    private ArchiveDirectoryObservation ObserveSourceDirectory(string leaf, CancellationToken ct)
    {
        lock (Gate)
        {
            ct.ThrowIfCancellationRequested(); RequireCapacity(); VerifyNamespace();
            using var held = UnixArchiveNative.ChildDirectory(directory, leaf);
            var metadata = ArchiveUnixOuterMetadata.CaptureDirectory(held, ct);
            VerifyNamespace();
            using var named = UnixArchiveNative.ChildDirectory(directory, leaf);
            if (UnixFiles.Stat(named) is not { IsDirectory: true } namedFacts || namedFacts.Identity != metadata.Identity)
                throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The selected folder now identifies a different object.");
            ct.ThrowIfCancellationRequested();
            return new(metadata.Identity.ToString(), Identity.ToString(), leaf, metadata.Digest, metadata.Origin,
                new(metadata.Native.Modified.Seconds, checked((int)metadata.Native.Modified.Nanoseconds)), (int)(metadata.Native.Mode & 0x1ff));
        }
    }
}
