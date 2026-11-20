using FileCat.Core.Analysis;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    public string? ContinuityNotice { get; private set; }

    private void SetContinuityNotice(AnalysisHostProfileIdentity? identity)
    {
        ContinuityNotice = identity is null
            ? "Private profile continuity could not be loaded or created. Existing state was preserved. Metadata analysis can continue with recorded-path comparison."
            : identity.HasHostContext ? null
            : "Host context is unavailable. Comparison can use recorded paths; host/profile identity continuity is unknown.";
    }
}
