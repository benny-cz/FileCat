using System.ComponentModel;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Archives;

internal sealed partial class WindowsArchiveFile
{
    public WindowsArchiveOuterMetadata CaptureOuterMetadata(CancellationToken cancellationToken = default)
    {
        lock (parent.Gate)
        {
            EnsureLive(); cancellationToken.ThrowIfCancellationRequested();
            if (writable) throw new InvalidOperationException("Seal the owned artifact before capturing its outer metadata.");
            try
            {
                WindowsArchiveOuterMetadata Capture()
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    uint links = Validate();
                    var basic = NtFile.GetBasic(handle);
                    byte[] security = WindowsArchiveNative.ReadOuterSecurity(handle);
                    var origin = ReadOrigin(cancellationToken);
                    if (origin.State == ArchiveOriginState.Unavailable)
                        throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "The archive origin mark could not be captured with its permissions.");
                    return new(Identity, basic, links, security, origin);
                }
                // An observed change during capture is refused. Two matching observations do not establish atomicity
                // against equivalent-privilege ABA races; retirement/publication must acquire fresh observations.
                var first = Capture(); var second = Capture();
                if (first.Digest != second.Digest)
                    throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The archive outer metadata changed while being captured.");
                return second;
            }
            catch (ArchiveWriteException) { throw; }
            catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable,
                    "The archive outer permissions, label or metadata could not be captured; publication requires review.");
            }
        }
    }
}
