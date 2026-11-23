using FileCat.Core.Permissions;
using FileCat.Core.State;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Permissions;

internal sealed partial class WindowsPermissionService
{
    /// <summary>Whether this administrator window can read the inspected object's complete security with its own token.</summary>
    internal static bool CanInspectElevated(PermissionSnapshot snapshot) => ElevatedInspectionRefusal(snapshot) is null;

    /// <summary>
    /// The authorized inspection run in this high-integrity FileCat process instead of the UAC-approved helper, for a
    /// read-only administrator window: the helper's reviewed walk and backup-intent reads (<see cref="ReadReviewedSecurity"/>),
    /// with backup privilege, and system security privilege for audit rules, enabled on a duplicate token for each read alone.
    /// Nothing is written. The original inspection's binding is checked before and after the read and moves to the result.
    /// Sections keep the authorized route's <see cref="Source"/>, the native call that read them; the caller knows it runs
    /// in an administrator window, and failures name this window's token.
    /// </summary>
    internal static PermissionInspection InspectElevated(PermissionInspection original, PermissionSections sections,
        CancellationToken cancellation)
    {
        if (ElevatedInspectionRefusal(original.Snapshot) is { } refusal) throw new NotSupportedException(refusal);
        if (sections == 0 || (sections & ~(DiscretionarySections | PermissionSections.Audit)) != 0)
            throw new NotSupportedException("Request only Windows owner, group, access, inheritance or audit sections.");
        var pin = original.TransferLease<WindowsInspectionPin>() ??
            throw new IOException("The Windows object inspection has ended. Inspect it again.");
        try
        {
            var snapshot = original.Snapshot;
            CheckPinnedSecurityTarget(snapshot, pin, requireOriginalStamp: true, cancellation);
            var read = ReadReviewedSecurity(ReviewedPath(snapshot, pin), ReviewedTarget.Of(snapshot.Binding!, pin), sections, cancellation);
            CheckPinnedSecurityTarget(snapshot, pin, requireOriginalStamp: true, cancellation);
            return new(DecodeAuthorizedInspection(snapshot, sections, read, administratorWindow: true), pin);
        }
        catch
        {
            pin.Dispose();
            throw;
        }
    }

    private static string? ElevatedInspectionRefusal(PermissionSnapshot snapshot) =>
        !WindowsTokenAuthority.IsHighIntegrity
            ? "Only an administrator FileCat window reads Windows security with its own privileges."
            : !ElevationBroker.IsNativeFileSecurityHost
                ? "Reading Windows security in an administrator window is qualified only for native x64 FileCat on Windows 11, not on Windows on Arm or in an emulated process."
                : !CanBindAuthorizedInspection(snapshot) ? "A bound local Windows object on a fixed NTFS volume is required." : null;
}
