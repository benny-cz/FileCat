using FileCat.Core.Analysis;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    public string? ContinuityNotice { get; private set; }

    private async Task<string> ResolveHostProfileAsync(CancellationToken cancellation)
    {
        ContinuityNotice = null;
        Guid? privateProfile = null;
        try
        {
            string path = _services!.Paths.AnalysisSnapshotsDirectory;
            Guid profile = await RunPrivateIoAsync(path, token =>
            {
                using var store = OpenPrivateStore(path);
                return new AnalysisPrivateProfileState(store).LoadOrCreate(token);
            }, cancellation);
            privateProfile = profile;
            // Host metadata has its own bounded I/O turn. Do not retain private-directory
            // handles or classify a blocked OS host lookup as activity on a portable data drive.
            _preparationDevice = "analysis-host-context";
            var identity = await AnalysisIo.Run(_services.Io, _preparationDevice, _ => AnalysisNativeHostProfile.Read(profile), cancellation);
            if (identity.HasHostContext) return identity.ToString();
            ContinuityNotice = "Host context is unavailable. Comparison can use recorded paths; host/profile identity continuity is unknown.";
            return identity.ToString();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { ContinuityNotice = "Private profile storage is not responding. Metadata analysis can continue; identity continuity across analyses is unknown."; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            ContinuityNotice = privateProfile.HasValue
                ? "Host context could not be established. The private profile identity is retained; comparison can use recorded paths."
                : "Private profile continuity could not be loaded or created. Existing state was preserved. Metadata analysis can continue with recorded-path comparison.";
        }
        cancellation.ThrowIfCancellationRequested();
        return privateProfile is { } known ? AnalysisHostProfileIdentity.Unverified(known).ToString() : "ephemeral:" + Guid.NewGuid().ToString("N");
    }
}
