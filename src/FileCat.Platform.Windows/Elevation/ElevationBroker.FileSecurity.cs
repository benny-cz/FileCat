using System.Runtime.InteropServices;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Analysis;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Elevation;

public static partial class ElevationBroker
{
    internal static bool IsNativeFileSecurityHost => RuntimeInformation.OSArchitecture == Architecture.X64 &&
        WindowsStorageTransforms.SupportsHost(qualifyNativeArm64: false);

    /// <summary>Connect to the actual UAC-launched helper and verify its current terminal reply.</summary>
    internal static async Task<FileSecurityReply> RequestFileSecurityAsync(ElevationPlan plan, bool portable, string exchangeRoot,
        nint owner, CancellationToken cancellation, Action? sendingClaim = null)
    {
        if (!IsNativeFileSecurityHost)
            throw new NotSupportedException("This installed FileCat cannot request Windows security operations.");
        plan = ElevationPlanCodec.Parse(ElevationPlanCodec.Serialize(plan));
        if (!ElevationPlanCodec.IsFileSecurityPlan(plan) || ElevationPlanCodec.Validate(plan, DateTime.UtcNow).Count != 0)
            throw new NotSupportedException("This installed FileCat cannot request that Windows security operation.");
        string helperImage = Locate(portable, out string? reason) ?? throw new NotSupportedException(reason);
        string requesterImage = Path.Combine(Path.GetDirectoryName(helperImage)!, RequesterExecutableName);
        var scope = plan.Steps[0].FileSecurity!;
        using var requester = NtfsMetadataPeer.HoldSelf(elevated: false);
        requester.RequireIdentity(requesterImage, scope.RequesterSession, elevated: false);
        if (Environment.ProcessId != plan.RequesterProcessId || requester.Creation != scope.RequesterCreatedFileTime ||
            requester.LogonSid.Value != scope.RequesterLogonSid || requester.UserSid.Value != plan.UserSid ||
            !HasTrustedExecutable(requesterImage))
            throw new UnauthorizedAccessException("This FileCat process does not own the security request.");

        using var requesterFile = OpenNtfsMetadataImage(requesterImage);
        using var helperFile = OpenNtfsMetadataImage(helperImage);
        if (ImageId(requesterFile) != scope.RequesterImageId)
            throw new UnauthorizedAccessException("The installed security requester has been replaced.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(SecurityTimeRemaining(plan, DateTime.UtcNow));
        deadline.Token.ThrowIfCancellationRequested();
        using var exchange = ElevationExchange.Create(exchangeRoot, plan);
        using var launched = LaunchFileSecurity(helperImage, exchange, owner);
        if (launched is null) return new(FileSecurityOutcome.Declined);
        using var helper = launched.HoldMetadataServer(helperImage, requester.Session);
        using var pipe = await NtfsMetadataPipe.ConnectFileSecurityAsync(plan.Nonce, helper, deadline.Token).ConfigureAwait(false);
        var protocol = new FileSecurityPipeProtocol(plan.Nonce, exchange.Hash, InspectionSections(plan.Steps[0]), scope.Change);
        deadline.Token.ThrowIfCancellationRequested();
        sendingClaim?.Invoke();
        var reply = await protocol.RequestAsync(pipe, deadline.Token).ConfigureAwait(false);
        _ = SecurityTimeRemaining(plan, DateTime.UtcNow);
        return reply;
    }

    private static ElevatedProcess? LaunchFileSecurity(string image, ElevationExchange exchange, nint owner)
    {
        try { return Launch(image, exchange.VolumePlanPath, exchange.Hash, owner); }
        catch (OperationCanceledException) { return null; }
    }

    internal static FileSecurityOutcome? ServeFileSecurity(ElevationPlan plan, string digest, string brokerDirectory,
        Func<string, DateTime, bool> claimNonce, Func<DateTime> utcNow,
        Func<string, string, IReadOnlyList<string>, string, Func<bool>, bool> ask)
    {
        DateTime now = utcNow();
        if (!IsNativeFileSecurityHost ||
            !ElevationPlanCodec.IsFileSecurityPlan(plan) || ElevationPlanCodec.Validate(plan, now).Count != 0)
            throw new NotSupportedException("This installed FileCat cannot serve that Windows security request.");

        ElevatedStep step = plan.Steps[0];
        var scope = step.FileSecurity!;
        string helperImage = Path.Combine(brokerDirectory, ExecutableName);
        string requesterImage = Path.Combine(brokerDirectory, RequesterExecutableName);
        using var helper = NtfsMetadataPeer.HoldSelf(elevated: true);
        using var requester = NtfsMetadataPeer.Hold((uint)plan.RequesterProcessId);
        helper.RequireIdentity(helperImage, scope.RequesterSession, elevated: true);
        requester.RequireIdentity(requesterImage, scope.RequesterSession, elevated: false);
        if (requester.Creation != scope.RequesterCreatedFileTime || requester.LogonSid.Value != scope.RequesterLogonSid ||
            requester.UserSid.Value != plan.UserSid || !HasTrustedExecutable(helperImage) || !HasTrustedExecutable(requesterImage))
            throw new UnauthorizedAccessException("The installed security requester could not be verified.");

        using var requesterFile = OpenNtfsMetadataImage(requesterImage);
        using var helperFile = OpenNtfsMetadataImage(helperImage);
        if (ImageId(requesterFile) != scope.RequesterImageId)
            throw new UnauthorizedAccessException("The installed security requester has been replaced.");

        DateTime expires = plan.CreatedUtc + ElevationPlanCodec.FileSecurityMaxAge;
        using var deadline = new CancellationTokenSource(SecurityTimeRemaining(plan, utcNow()));
        using var server = NtfsMetadataPipe.CreateFileSecurityServer(plan.Nonce, requester.LogonSid);
        NtfsMetadataPipe.AcceptConnectionAsync(server, requester, deadline.Token).GetAwaiter().GetResult();
        PermissionSections inspection = InspectionSections(step);
        var protocol = new FileSecurityPipeProtocol(plan.Nonce, digest, inspection, scope.Change);
        FileSecurityOutcome? outcome = null;
        bool sent = protocol.Serve(server, (connected, cancellation) =>
        {
            var reply = Dispatch(connected, cancellation);
            outcome = reply?.Outcome;
            return reply;
        }, deadline.Token);
        return sent ? outcome : null;

        bool Verify()
        {
            NtfsMetadataPipe.VerifyPeer(server.SafePipeHandle, requester, serverEnd: true);
            helper.RequireIdentity(helperImage, scope.RequesterSession, elevated: true);
            DateTime checkedAt = utcNow();
            return checkedAt >= plan.CreatedUtc && checkedAt < expires;
        }

        FileSecurityReply? Dispatch(Func<bool> connected, CancellationToken cancellation)
        {
            if (!Verify() || !connected() || !claimNonce(plan.Nonce, utcNow()) || !Verify() || !connected()) return null;
            if (inspection == 0 && !WindowsPermissionService.SupportsAuthorizedChange(step))
                return new(FileSecurityOutcome.Unavailable);

            bool Current()
            {
                DateTime checkedAt = utcNow();
                return connected() && requester.IsAlive && checkedAt >= plan.CreatedUtc && checkedAt < expires;
            }

            // Keep the reviewed object alive across consent. A write review needs its own independently read and projected descriptor.
            bool mayHaveDispatched = false;
            try
            {
                // An administrator whose token lacks backup, restore or audit privilege is refused before the target is
                // held or consent is asked, with a reply that names the missing privilege rather than a generic failure.
                if (WindowsPermissionService.RefuseWithoutPrivileges(step, WindowsFileSecurityPrivilege.MissingFromProcess) is { } refused)
                    return Current() && Verify() ? refused : null;
                using var target = HoldTarget(step, cancellation);
                if (!Current()) return null;
                string who = $"Requester {ElevationPlanCodec.DisplayName(requester.UserSid.Value)} ({requester.UserSid.Value}).";
                string item = ElevationConsent.DisplayText(step.Path!);
                if (inspection == 0)
                {
                    var review = Task.Run(() => WindowsPermissionService.DescribeAuthorizedChange(step, requester, helper, cancellation),
                        CancellationToken.None).WaitAsync(cancellation).GetAwaiter().GetResult();
                    if (!Current() || !Verify()) return null;
                    bool noRestore = WindowsFileSecurityPrivilege.MissingFromProcess("SeRestorePrivilege").Count != 0;
                    if (review.Owner is { CanUseWriteOwner: false } && noRestore)
                        return new(FileSecurityOutcome.ChangeReported,
                            Apply: new(FileSecurityChangeStatus.FailedBeforeChange, WindowsFileSecurityPrivilege.PrivilegeNotHeld));
                    bool volumeRoot = scope.Kind == PermissionObjectKind.VolumeRoot;
                    string scopeText = volumeRoot
                        ? "This write targets the selected volume root. Existing child ACLs stay in place. New children can inherit these rules."
                        : $"Hard-link count {scope.LinkCount}. Other names for this file are unknown.";
                    string impact = review.Owner switch
                    {
                        { CanUseWriteOwner: true } when noRestore => "The helper uses the administrator's right to write the owner. If Windows denies that access, the change fails: this administrator's token lacks restore privilege.",
                        { CanUseWriteOwner: true } => "The helper uses the administrator's right to write the owner. If Windows denies that access, approval allows restore privilege for this same owner assignment.",
                        not null => "The helper uses restore privilege to assign the reviewed owner from the requesting Windows token.",
                        _ when scope.Sections == PermissionSections.Audit => "The helper uses the system security privilege for this change. Windows audit policy determines whether events are recorded.",
                        _ when volumeRoot => "The helper overrides discretionary permissions for this single volume-root access change.",
                        _ => "The helper overrides the file's discretionary permissions for this one reviewed change."
                    };
                    string actor = review.Owner is not null && helper.UserSid != requester.UserSid
                        ? $" Approving administrator {helper.UserSid.Value}." : "";
                    bool approved = ask(ElevationPlanCodec.FileSecurityChangeTitle, who + actor,
                        [$"Change {item}\n{review.Description}\n{scopeText}"],
                        impact, Current);
                    if (!Current() || !Verify()) return null;
                    if (!approved) return new(FileSecurityOutcome.Declined);
                    mayHaveDispatched = true;
                    var applied = Task.Run(() => WindowsPermissionService.ApplyAuthorizedChange(step, requester, helper, review.Owner,
                        Current, cancellation), CancellationToken.None).GetAwaiter().GetResult();
                    return Current() && Verify() ? new(FileSecurityOutcome.ChangeReported, Apply: applied) : null;
                }

                string sections = string.Join(", ", Enum.GetValues<PermissionSections>()
                    .Where(section => section != 0 && ((int)section & ((int)section - 1)) == 0 && (inspection & section) == section)
                    .Select(section => section == PermissionSections.Access ? "discretionary ACL (DACL)" :
                        section == PermissionSections.Audit ? "system audit ACL (SACL)" : section.ToString().ToLowerInvariant()));
                string kind = scope.Kind == PermissionObjectKind.VolumeRoot ? "volume root" : scope.Kind.ToString().ToLowerInvariant();
                bool accepted = ask(ElevationPlanCodec.FileSecurityInspectTitle, who,
                    [$"Inspect {item}\nRead {sections} on this {kind}. Hard-link count {scope.LinkCount}."],
                    "This approval covers the listed security metadata for one object.", Current);
                if (!Current() || !Verify()) return null;
                if (!accepted) return new(FileSecurityOutcome.Declined);
                var result = Task.Run(() => WindowsPermissionService.ReadAuthorized(step, cancellation), CancellationToken.None)
                    .WaitAsync(cancellation).GetAwaiter().GetResult();
                return Current() && Verify() ? new(FileSecurityOutcome.Inspected, result) : null;
            }
            catch (Exception ex) when (WindowsPermissionService.IsAuthorizedChangeFailure(ex))
            {
                if (mayHaveDispatched || !Current()) return null;
                return inspection == 0 && WindowsPermissionService.IsAuthorizedBindingConflict(ex)
                    ? new(FileSecurityOutcome.ChangeReported, Apply: new(FileSecurityChangeStatus.Conflict))
                    : new(FileSecurityOutcome.Unavailable);
            }
        }
    }

    private static Microsoft.Win32.SafeHandles.SafeFileHandle HoldTarget(ElevatedStep step, CancellationToken cancellation)
    {
        var opening = Task.Run(() => WindowsPermissionService.HoldAuthorizedTarget(step, cancellation), CancellationToken.None);
        try { return opening.WaitAsync(cancellation).GetAwaiter().GetResult(); }
        catch (OperationCanceledException)
        {
            _ = opening.ContinueWith(static pending =>
            {
                if (pending.IsCompletedSuccessfully) pending.Result.Dispose();
                else _ = pending.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }

    private static PermissionSections InspectionSections(ElevatedStep step) =>
        step.Verb == ElevatedVerb.InspectFileSecurity ? step.FileSecurity!.Sections : PermissionSections.None;

    private static TimeSpan SecurityTimeRemaining(ElevationPlan plan, DateTime now)
    {
        TimeSpan age = now - plan.CreatedUtc;
        if (age < TimeSpan.Zero || age >= ElevationPlanCodec.FileSecurityMaxAge)
            throw new TimeoutException("The Windows security request has expired.");
        return ElevationPlanCodec.FileSecurityMaxAge - age;
    }
}
