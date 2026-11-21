using System.ComponentModel;
using System.Globalization;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Analysis;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Permissions;

internal sealed partial class WindowsPermissionService
{
    private const uint AccessSystemSecurity = 0x01000000, BackupSecurityInformation = 0x00010000, SaclInformation = 8;

    internal static bool CanBindAuthorizedInspection(PermissionSnapshot original) =>
        original.Binding is { FileSystem: "NTFS", LinkCount: > 0 } binding &&
        PermissionSnapshots.IsLocal(original.Subject) && original.Subject.LinkPolicy == PermissionLinkPolicy.NoFollow &&
        original.BindingProblem is null && NtfsMetadataVolume.CanonicalGuid(binding.MountRoot) is not null;

    internal static async Task<PermissionInspection> InspectWithAuthorizationAsync(PermissionInspection original,
        PermissionSections sections, bool portable, string exchangeRoot, nint owner, CancellationToken cancellation)
    {
        if (!ElevationBroker.IsNativeFileSecurityHost || portable || !CanBindAuthorizedInspection(original.Snapshot))
            throw new NotSupportedException("A bound local Windows object in installed FileCat is required.");
        var pin = original.TransferLease<WindowsInspectionPin>() ??
            throw new IOException("The Windows object inspection has ended. Inspect it again.");
        try
        {
            var snapshot = original.Snapshot;
            CheckPinnedSecurityTarget(snapshot, pin, requireOriginalStamp: true, cancellation);
            var plan = CreateFileSecurityPlan(snapshot, pin, sections, change: null);
            var reply = await ElevationBroker.RequestFileSecurityAsync(plan, portable, exchangeRoot, owner, cancellation).ConfigureAwait(false);
            if (reply.Outcome == FileSecurityOutcome.Declined)
                throw new OperationCanceledException("The Windows file security inspection was declined.");
            if (reply.Inspection is not { } read)
                throw new IOException("The installed Windows helper could not corroborate the reviewed object.");
            CheckPinnedSecurityTarget(snapshot, pin, requireOriginalStamp: true, cancellation);
            return new(DecodeAuthorizedInspection(snapshot, sections, read), pin);
        }
        catch
        {
            pin.Dispose();
            throw;
        }
    }

    private static ElevationPlan CreateFileSecurityPlan(PermissionSnapshot snapshot, WindowsInspectionPin pin,
        PermissionSections sections, ElevatedFileSecurityChange? change)
    {
        var binding = snapshot.Binding!;
        string volume = NtfsMetadataVolume.CanonicalGuid(binding.MountRoot)!;
        using var requester = NtfsMetadataPeer.HoldSelf(elevated: false);
        using var image = ElevationBroker.OpenNtfsMetadataImage(requester.Image);
        var scope = new ElevatedFileSecurityScope(binding.Identity.Object.VolumeSerial.ToString("X16", CultureInfo.InvariantCulture),
            pin.Chain.Select(entry => entry.Identity.Object.FileId).ToArray(), binding.Identity.Kind, binding.LinkCount!.Value, requester.Creation,
            requester.Session, requester.LogonSid.Value, ElevationBroker.ImageId(image), sections, PermissionScope.Objects, change);
        var step = new ElevatedStep(change is null ? ElevatedVerb.InspectFileSecurity : ElevatedVerb.ChangeFileSecurity)
        {
            Path = volume + string.Join('\\', pin.Names),
            FileSecurity = scope,
        };
        return new ElevationPlan
        {
            Version = ElevationPlan.FileSecurityVersion, Nonce = NtfsMetadataProtocol.NewNonce(), CreatedUtc = DateTime.UtcNow,
            UserSid = requester.UserSid.Value, RequesterProcessId = Environment.ProcessId,
            Title = change is null ? ElevationPlanCodec.FileSecurityInspectTitle : ElevationPlanCodec.FileSecurityChangeTitle, Steps = [step],
        };
    }

    private static void CheckPinnedSecurityTarget(PermissionSnapshot snapshot, WindowsInspectionPin pin, bool requireOriginalStamp,
        CancellationToken cancellation)
    {
        try
        {
            var authority = snapshot.Subject.Authority;
            using var current = Walk(pin.Root, pin.Names, authority, cancellation,
                out var parent, out var chain, out var observed);
            using (parent)
            {
                if (observed != snapshot.Binding || !SameChain(pin.Chain, chain) ||
                    (requireOriginalStamp && chain[^1] != pin.Chain[^1]) ||
                    Screen(pin.File, observed.Identity.MountInstance, pin.Names.Length == 0, authority, cancellation) != chain[^1])
                    throw new PermissionBindingConflictException("The reviewed object, its namespace or its metadata changed.");
            }
        }
        catch (ReadFailure error) { throw ReadError(error); }
        catch (Win32Exception error) { throw new IOException("The Windows permission binding could not be verified.", error); }
    }

    private static PermissionSnapshot DecodeAuthorizedInspection(PermissionSnapshot original, PermissionSections sections,
        FileSecurityInspection read)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var snapshot = PermissionSnapshots.Create(original.Subject, PermissionModel.WindowsDescriptor, original.Binding, sections, now,
            Source, read.Discretionary.Status, "The helper could not read the requested Windows security section.", Error(read.Discretionary));
        if (read.Discretionary.Descriptor is { } access)
            snapshot = Decode(snapshot, sections & DiscretionarySections, access, now);
        if ((sections & PermissionSections.Audit) != 0)
        {
            if (read.Audit.Descriptor is { } bytes)
            {
                try
                {
                    var audit = WindowsPermissionDescriptor.Sacl(bytes, snapshot.Subject.Authority.Id);
                    snapshot = snapshot with { Audit = new(audit.Present ? PermissionReadStatus.Present : PermissionReadStatus.Absent,
                        Source, now, new PermissionAclValue(audit)) };
                }
                catch (Exception error) when (error is InvalidDataException or OverflowException)
                {
                    snapshot = snapshot with { Audit = new(error is OverflowException ? PermissionReadStatus.Incomplete :
                        PermissionReadStatus.Failed, Source, now, Reason: error.Message) };
                }
            }
            else
                snapshot = snapshot with { Audit = new(read.Audit.Status, Source, now,
                    Reason: "The helper could not read the requested Windows audit ACL.", NativeError: Error(read.Audit)) };
        }
        return PermissionSnapshots.WithCapabilities(snapshot, sections);

        static int? Error(FileSecurityDescriptorRead value) => value.NativeError == 0 ? null : value.NativeError;
    }

    internal static SafeFileHandle HoldAuthorizedTarget(ElevatedStep step, CancellationToken cancellation)
    {
        var reviewed = step.FileSecurity!;
        var (root, names) = SplitLocalPath(step.Path!);
        var authority = new PermissionAuthority(PermissionAuthorityKind.Local, "local", 0);
        try
        {
            try { return Hold(backup: false); }
            catch (ReadFailure error) when (error.Status == PermissionReadStatus.Denied)
            {
                return WindowsFileSecurityPrivilege.Read(audit: false, () => Hold(backup: true));
            }
        }
        catch (ReadFailure error) { throw ReadError(error); }

        SafeFileHandle Hold(bool backup)
        {
            var file = Walk(root, names, authority, cancellation, out var parent, out var chain, out var binding, backupIntent: backup);
            using (parent)
            {
                try { RequireReviewedTarget(reviewed, chain, binding); return file; }
                catch { file.Dispose(); throw; }
            }
        }
    }

    internal static FileSecurityInspection ReadAuthorized(ElevatedStep step, CancellationToken cancellation)
    {
        if (!ElevationBroker.IsNativeFileSecurityHost || step is not
            { Verb: ElevatedVerb.InspectFileSecurity, Path: { } path, FileSecurity: { } reviewed })
            throw new NotSupportedException("The Windows security helper requires one reviewed inspection.");

        var (root, names) = SplitLocalPath(path);
        var authority = new PermissionAuthority(PermissionAuthorityKind.Local, "local", 0);
        PermissionSections discretionary = reviewed.Sections & DiscretionarySections;
        var access = ReadSection(discretionary != 0, audit: false, out FileStamp? accessStamp);
        var audit = ReadSection((reviewed.Sections & PermissionSections.Audit) != 0, audit: true, out FileStamp? auditStamp);
        if (accessStamp is { } first && auditStamp is { } second && first != second)
            return new(new(PermissionReadStatus.Stale), new(PermissionReadStatus.Stale));
        return new(access, audit);

        FileSecurityDescriptorRead ReadSection(bool requested, bool audit, out FileStamp? observed)
        {
            observed = null;
            if (!requested) return new(PermissionReadStatus.Unloaded);
            try
            {
                var (bytes, stamp) = WindowsFileSecurityPrivilege.Read(audit, () =>
                {
                    using var file = Walk(root, names, authority, cancellation, out var parent, out var chain, out var binding,
                        leafReadAccess: ReadControl | (audit ? AccessSystemSecurity : 0), backupIntent: true);
                    using (parent)
                    {
                        RequireReviewedTarget(reviewed, chain, binding);
                        // Backup information includes the complete SACL, including labels that a narrow audit query can omit.
                        // The authorized audit reply carries only the SACL.
                        byte[] descriptor = ReadSecurityDescriptor(file, audit ? BackupSecurityInformation : DiscretionaryFlags(discretionary),
                            cancellation, bytes => audit ? WindowsPermissionDescriptor.AuditOnly(bytes, authority.Id) : bytes.ToArray());
                        using var current = Walk(root, names, authority, cancellation, out var currentParent, out var latest, out var currentBinding,
                            backupIntent: true);
                        using (currentParent)
                        {
                            RequireReviewedTarget(reviewed, latest, currentBinding);
                            if (chain[^1] != latest[^1] || Screen(file, binding.Identity.MountInstance,
                                    reviewed.Kind == PermissionObjectKind.VolumeRoot, authority, cancellation) != chain[^1])
                                throw new ReadFailure(PermissionReadStatus.Stale, "The reviewed Windows security object changed during inspection.");
                        }
                        return (descriptor, chain[^1]);
                    }
                });
                observed = stamp;
                return new(PermissionReadStatus.Present, bytes);
            }
            catch (ReadFailure error) { return new(error.Status, NativeError: error.Error ?? 0); }
            catch (UnauthorizedAccessException) { return new(PermissionReadStatus.Denied); }
            catch (InvalidDataException) { return new(PermissionReadStatus.Failed); }
            catch (OverflowException) { return new(PermissionReadStatus.Incomplete); }
            catch (Win32Exception error) { return new(error.NativeErrorCode == 1300 ? PermissionReadStatus.Denied : ErrorStatus(error.NativeErrorCode),
                NativeError: error.NativeErrorCode); }
            catch (IOException) { return new(PermissionReadStatus.Failed); }
        }
    }

    private static void RequireReviewedTarget(ElevatedFileSecurityScope reviewed, FileStamp[] chain, PermissionBinding current)
    {
        if (reviewed.Kind != current.Identity.Kind || reviewed.LinkCount != current.LinkCount ||
            reviewed.VolumeSerial != current.Identity.Object.VolumeSerial.ToString("X16", CultureInfo.InvariantCulture) ||
            !reviewed.FileIds.SequenceEqual(chain.Select(entry => entry.Identity.Object.FileId), StringComparer.Ordinal))
            throw new ReadFailure(PermissionReadStatus.Stale, "The held Windows object or its ancestors no longer match the reviewed target.");
    }
}
