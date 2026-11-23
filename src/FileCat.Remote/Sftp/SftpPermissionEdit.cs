using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Remote.Sftp;

/// <summary>Retains the original inspected server path until the draft is canceled or transferred to a job.</summary>
internal sealed class SftpPermissionEdit(SftpPermissionPathReview review, PermissionSubject subject, ItemRef source, string device)
    : IDisposable
{
    private SftpPermissionPathReview? _review = review;
    private SftpPermissionPathReview Active => _review ?? throw new InvalidOperationException("Open a fresh SFTP permissions review.");

    internal uint PairedUser(uint user) => Active.UnchangedUser(user);
    internal bool ModeWritesRefused => Active.ModeWritesRefused;
    internal SftpPermissionPlan Preview(PermissionDraft draft) => SftpPermissionMutation.Preview(Active, subject.Authority, draft);

    internal ReviewedPermissionOperation Prepare(PermissionDraft draft, SftpPermissionPlan accepted, bool acceptSetId, CancellationToken cancellation)
    {
        var owned = Interlocked.Exchange(ref _review, null) ?? throw new InvalidOperationException("Open a fresh SFTP permissions review.");
        try
        {
            cancellation.ThrowIfCancellationRequested();
            owned.Revalidate(cancellation);
            var plan = SftpPermissionMutation.Preview(owned, subject.Authority, draft);
            if (plan.NeedsSetIdConsent != accepted.NeedsSetIdConsent || !plan.Stages.SequenceEqual(accepted.Stages))
                throw new SftpPermissionPathConflictException("The expected server changes differ from the review.");
            if (!acceptSetId && plan.NeedsSetIdConsent)
                throw new RefusedOperationException("Accept the reviewed set-ID effects before applying the SFTP changes.");
            return SftpPermissionMutation.Prepare(owned, subject, source, plan, device);
        }
        catch { owned.Dispose(); throw; }
    }

    public void Dispose() => Interlocked.Exchange(ref _review, null)?.Dispose();
}
