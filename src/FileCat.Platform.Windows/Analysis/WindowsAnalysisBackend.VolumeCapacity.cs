using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;

namespace FileCat.Platform.Windows.Analysis;

public sealed partial class WindowsAnalysisBackend
{
    AnalysisVolumeCapacity? IAnalysisVolumeBackend.ReadVolumeCapacity(CancellationToken cancellation)
    {
        Guard(); cancellation.ThrowIfCancellationRequested();
        if (Root.Assurance != IdentityAssurance.Verified || RootEntry.Cloud == true) return null;
        var space = FileSystemCapacity.QueryHeldWindowsVolume(_root.Value);
        cancellation.ThrowIfCancellationRequested();
        if (space is not { } known) return null;
        var observedUtc = DateTimeOffset.UtcNow;
        var volume = WindowsStorageTransforms.ActualVolumeBinding(_root.Value, cancellation);
        return new(volume.Identity, observedUtc, known.Total, known.Used, known.Free, known.Available,
            RootEntry.Kind == AnalysisKind.Directory && volume.WholeVolume, known.MayShareCapacity);
    }
}
