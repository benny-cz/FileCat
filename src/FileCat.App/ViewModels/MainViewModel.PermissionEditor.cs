using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Core.Threading;
using FileCat.Platform.Windows.Permissions;
using FileCat.Recovery.Unix;
using FileCat.Remote.Sftp;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private sealed record InspectedPermission(string Name, ItemRef? Source, PermissionInspection Inspection)
    {
        internal PermissionSnapshot Snapshot => Inspection.Snapshot;
    }

    private async Task ShowPermissionInspectorAsync(IDialogService dialogs, InspectedPermission[] inspected, Func<string, Task>? copy,
        PermissionSelection? origin = null)
    {
        var refusals = inspected.Select(item => item.Source?.FileSystemPath == item.Snapshot.Subject.RawLocator ? Services.Permissions switch
        {
            LinuxPermissionService => LinuxPermissionService.ModeReviewRefusal(item.Snapshot),
            DarwinPermissionService => DarwinPermissionService.ModeRefusal(item.Snapshot),
            WindowsPermissionService => WindowsPermissionService.EditRefusal(item.Snapshot),
            _ => "Editing is unavailable for this object."
        } : "Editing is unavailable for this object.").ToArray();
        var view = new PermissionInspectorView(inspected.Select(item => (item.Name, item.Snapshot)).ToArray(), copy,
            refusals.Select((refusal, index) => WindowsFolderOnlyAvailable(index, volumeRoot: false) ||
                WindowsFolderOnlyAvailable(index, volumeRoot: true) || WindowsResetAvailable(index) ? null : refusal).ToArray(),
            Services.PermissionPrincipalNames);
        var buttons = new List<DialogButton> { new("Close", "close", IsDefault: true, IsCancel: true) };
        bool AclAvailable(int index) => refusals[index] is null && (Services.Permissions is LinuxPermissionService linux && linux.CanEditAccessAcl(inspected[index].Snapshot) ||
            Services.Permissions is DarwinPermissionService && DarwinPermissionService.CanEditAcl(inspected[index].Snapshot) ||
            Services.Permissions is WindowsPermissionService &&
            inspected[index].Snapshot.Access.Value?.Acl is WindowsDacl { Form: WindowsDaclForm.Entries, Revision: 2 or 4 });
        bool DefaultsAvailable(int index) => refusals[index] is null && Services.Permissions is LinuxPermissionService linux &&
            linux.CanEditDefaults(inspected[index].Snapshot);
        bool GroupAvailable(int index) => refusals[index] is null && (Services.Permissions is LinuxPermissionService linux &&
            linux.CanEditGroup(inspected[index].Snapshot) || Services.Permissions is DarwinPermissionService &&
            DarwinPermissionService.CanEditGroup(inspected[index].Snapshot));
        bool ModeTreeAvailable(int index) => refusals[index] is null && inspected[index].Source is not null && CanReviewModeTree(inspected[index].Snapshot);
        bool DarwinAclTreeAvailable(int index) => refusals[index] is null && inspected[index].Source is not null &&
            Services.Permissions is DarwinPermissionService darwin && darwin.CanEditAclTree(inspected[index].Snapshot);
        bool ResetTreeAvailable(int index) => DarwinAclTreeAvailable(index) || refusals[index] is null && inspected[index].Source is not null &&
            Services.Permissions is LinuxPermissionService linux && linux.CanResetAclTree(inspected[index].Snapshot);
        bool AccessCheckAvailable(int index) => inspected[index].Source is not null && (Services.Permissions switch
        {
            LinuxPermissionService linux => linux.CanCheckAccess(inspected[index].Snapshot),
            DarwinPermissionService darwin => darwin.CanCheckAccess(inspected[index].Snapshot),
            _ => false
        });
        bool SftpReviewAvailable(int index) => inspected[index].Source is not null &&
            inspected[index].Snapshot is { Sftp.ChangeRefusal: null, ServerState: not null };
        bool WindowsEstimateAvailable(int index) => inspected[index].Source is not null &&
            Services.Permissions is WindowsPermissionService windows && windows.CanEstimateAccess(inspected[index].Snapshot);
        bool WindowsPropagationAvailable(int index) => inspected[index].Source is { Kind: EntryKind.Directory } source &&
            source.FileSystemPath == inspected[index].Snapshot.Subject.RawLocator &&
            Services.Permissions is WindowsPermissionService windows && windows.CanReviewNativePropagation(inspected[index].Snapshot);
        bool WindowsAccessTreeAvailable(int index, PermissionObjectKind kind) => inspected[index].Source is { Kind: EntryKind.Directory } source &&
            source.FileSystemPath == inspected[index].Snapshot.Subject.RawLocator &&
            Services.Permissions is WindowsPermissionService windows && windows.CanReviewAccessTree(inspected[index].Snapshot, kind);
        bool WindowsFolderOnlyAvailable(int index, bool volumeRoot) => inspected[index].Source is { Kind: EntryKind.Directory } source &&
            source.FileSystemPath == inspected[index].Snapshot.Subject.RawLocator &&
            Services.Permissions is WindowsPermissionService windows &&
            (volumeRoot ? windows.CanReviewVolumeRoot(inspected[index].Snapshot) : windows.CanReviewFolderOnly(inspected[index].Snapshot));
        bool WindowsResetAvailable(int index) => inspected[index].Source is { Kind: EntryKind.Directory } source &&
            source.FileSystemPath == inspected[index].Snapshot.Subject.RawLocator &&
            Services.Permissions is WindowsPermissionService windows && windows.CanReviewAclReset(inspected[index].Snapshot);
        bool ProtectionAvailable(int index) => refusals[index] is null && Services.Permissions switch
        {
            DarwinPermissionService => DarwinPermissionService.CanEditProtection(inspected[index].Snapshot),
            LinuxPermissionService => LinuxPermissionService.CanEditProtection(inspected[index].Snapshot),
            _ => false
        };
        var estimates = inspected.Select((item, index) => Services.Permissions is LinuxPermissionService && AccessCheckAvailable(index)
            ? PosixAclEvaluation.FromSnapshot(item.Snapshot) : null).ToArray();
        if (refusals.Any(refusal => refusal is null))
        {
            if (Services.Permissions is LinuxPermissionService or DarwinPermissionService)
                buttons.Insert(0, new("Edit mode…", "mode") { IsAvailable = () => refusals[view.SelectedSubject] is null });
            if (Enumerable.Range(0, inspected.Length).Any(ProtectionAvailable))
                buttons.Insert(0, new("Edit file protection…", "protection") { IsAvailable = () => ProtectionAvailable(view.SelectedSubject) });
            if (Enumerable.Range(0, inspected.Length).Any(ModeTreeAvailable))
                buttons.Insert(0, new("Review folder tree mode…", "mode-tree") { IsAvailable = () => ModeTreeAvailable(view.SelectedSubject) });
            if (Enumerable.Range(0, inspected.Length).Any(ResetTreeAvailable))
                buttons.Insert(0, new("Reset descendant access ACLs…", "reset-tree") { IsAvailable = () => ResetTreeAvailable(view.SelectedSubject) });
            if (Enumerable.Range(0, inspected.Length).Any(DarwinAclTreeAvailable))
                buttons.Insert(0, new("Refresh inherited access rules…", "inherit-tree") { IsAvailable = () => DarwinAclTreeAvailable(view.SelectedSubject) });
            if (Services.Permissions is WindowsPermissionService)
                buttons.Insert(0, new("Change owner…", "owner") { IsAvailable = () => refusals[view.SelectedSubject] is null });
            if (Enumerable.Range(0, inspected.Length).Any(AclAvailable))
                buttons.Insert(0, new("Edit access ACL…", "acl") { IsAvailable = () => AclAvailable(view.SelectedSubject) });
            if (Enumerable.Range(0, inspected.Length).Any(DefaultsAvailable))
                buttons.Insert(0, new("Edit default ACL…", "defaults") { IsAvailable = () => DefaultsAvailable(view.SelectedSubject) });
            if (Enumerable.Range(0, inspected.Length).Any(GroupAvailable))
                buttons.Insert(0, new("Change group…", "group") { IsAvailable = () => GroupAvailable(view.SelectedSubject) });
        }
        if (Enumerable.Range(0, inspected.Length).Any(AccessCheckAvailable))
            buttons.Insert(0, new("Check current process access…", "current-access") { IsAvailable = () => AccessCheckAvailable(view.SelectedSubject) });
        if (estimates.Any(estimate => estimate is not null))
            buttons.Insert(0, new("Estimate POSIX ACL access…", "estimate-access") { IsAvailable = () => estimates[view.SelectedSubject] is not null });
        if (Enumerable.Range(0, inspected.Length).Any(WindowsEstimateAvailable))
            buttons.Insert(0, new("Estimate Windows ACL access…", "windows-estimate") { IsAvailable = () => WindowsEstimateAvailable(view.SelectedSubject) });
        if (Enumerable.Range(0, inspected.Length).Any(WindowsPropagationAvailable))
            buttons.Insert(0, new("Review Windows inheritance…", "windows-propagation")
                { IsAvailable = () => WindowsPropagationAvailable(view.SelectedSubject) });
        if (Enumerable.Range(0, inspected.Length).Any(index => WindowsAccessTreeAvailable(index, PermissionObjectKind.File)))
            buttons.Insert(0, new("Add access rule to descendant files…", "windows-file-tree")
                { IsAvailable = () => WindowsAccessTreeAvailable(view.SelectedSubject, PermissionObjectKind.File) });
        if (Enumerable.Range(0, inspected.Length).Any(index => WindowsAccessTreeAvailable(index, PermissionObjectKind.Directory)))
            buttons.Insert(0, new("Add access rule to descendant folders…", "windows-folder-tree")
                { IsAvailable = () => WindowsAccessTreeAvailable(view.SelectedSubject, PermissionObjectKind.Directory) });
        if (Enumerable.Range(0, inspected.Length).Any(index => WindowsFolderOnlyAvailable(index, volumeRoot: false)))
            buttons.Insert(0, new("Edit selected folder access rules…", "windows-folder-only")
                { IsAvailable = () => WindowsFolderOnlyAvailable(view.SelectedSubject, volumeRoot: false) });
        if (Enumerable.Range(0, inspected.Length).Any(index => WindowsFolderOnlyAvailable(index, volumeRoot: true)))
            buttons.Insert(0, new("Edit volume root access rules…", "windows-volume-root")
                { IsAvailable = () => WindowsFolderOnlyAvailable(view.SelectedSubject, volumeRoot: true) });
        if (Enumerable.Range(0, inspected.Length).Any(WindowsResetAvailable))
            buttons.Insert(0, new("Reset Windows descendant ACLs…", "windows-reset-tree")
                { IsAvailable = () => WindowsResetAvailable(view.SelectedSubject) });
        if (Enumerable.Range(0, inspected.Length).Any(SftpReviewAvailable))
            buttons.Insert(0, new("Review SFTP permissions…", "sftp") { IsAvailable = () => SftpReviewAvailable(view.SelectedSubject) });
        if (inspected.Any(CanInspectWithLinuxAuthorization))
            buttons.Insert(0, new("Inspect with authorization…", "authorized-linux")
                { IsAvailable = () => CanInspectWithLinuxAuthorization(inspected[view.SelectedSubject]) });
        if (inspected.Any(CanInspectWithWindowsAuthorization))
            buttons.Insert(0, new("Inspect with authorization…", "authorized-windows")
                { IsAvailable = () => CanInspectWithWindowsAuthorization(inspected[view.SelectedSubject]) });

        var answer = await dialogs.ShowCustomAsync("Permissions and ownership", view, buttons, resizable: true,
            behavior: new DialogBehavior { DismissOnBackdrop = false, PreferredSize = new Avalonia.Size(740, 550) });
        if (answer is "authorized-linux")
            await ShowAuthorizedLinuxPermissionsAsync(dialogs, inspected[view.SelectedSubject], copy, origin);
        else if (answer is "authorized-windows")
            await ShowAuthorizedWindowsPermissionsAsync(dialogs, inspected[view.SelectedSubject], copy, origin);
        else if (answer is "sftp" && SftpReviewAvailable(view.SelectedSubject) && !Services.Io.IsStopped)
            await ShowSftpPermissionEditorAsync(dialogs, inspected[view.SelectedSubject], copy, origin);
        else if (answer is "current-access" && AccessCheckAvailable(view.SelectedSubject) && !Services.Io.IsStopped)
            await ShowCurrentAccessReportAsync(dialogs, inspected[view.SelectedSubject]);
        else if (answer is "windows-estimate" && WindowsEstimateAvailable(view.SelectedSubject) && !Services.Io.IsStopped &&
            Services.Permissions is WindowsPermissionService windows)
            await ShowWindowsAclEstimateAsync(dialogs, inspected[view.SelectedSubject], windows);
        else if (answer is "windows-propagation" && WindowsPropagationAvailable(view.SelectedSubject) && !Services.Io.IsStopped)
            await ShowWindowsPropagationEditorAsync(dialogs, inspected[view.SelectedSubject], copy, origin);
        else if (answer is "windows-file-tree" or "windows-folder-tree" && !Services.Io.IsStopped &&
            Services.Permissions is WindowsPermissionService windowsTree)
        {
            var kind = answer is "windows-file-tree" ? PermissionObjectKind.File : PermissionObjectKind.Directory;
            if (WindowsAccessTreeAvailable(view.SelectedSubject, kind))
                await ShowWindowsAccessTreeAsync(dialogs, inspected[view.SelectedSubject], windowsTree, kind, origin);
        }
        else if (answer is "windows-folder-only" or "windows-volume-root" && !Services.Io.IsStopped)
        {
            if (WindowsFolderOnlyAvailable(view.SelectedSubject, volumeRoot: answer is "windows-volume-root"))
                await ShowBoundPermissionEditorAsync(dialogs, inspected[view.SelectedSubject], copy, "acl", origin, windowsFolderOnly: true);
        }
        else if (answer is "windows-reset-tree" && WindowsResetAvailable(view.SelectedSubject) && !Services.Io.IsStopped &&
            Services.Permissions is WindowsPermissionService windowsReset)
            await ShowWindowsAclResetAsync(dialogs, inspected[view.SelectedSubject], windowsReset, origin);
        else if ((answer is "mode-tree" && ModeTreeAvailable(view.SelectedSubject) || answer is "reset-tree" && ResetTreeAvailable(view.SelectedSubject) ||
            answer is "inherit-tree" && DarwinAclTreeAvailable(view.SelectedSubject)) &&
            !Services.Io.IsStopped)
        {
            var selected = inspected[view.SelectedSubject];
            foreach (var item in inspected)
                if (!ReferenceEquals(item, selected)) item.Inspection.Dispose();
            if (answer is "reset-tree" && Services.Permissions is LinuxPermissionService linux)
                await ShowReviewedAclResetAsync(dialogs, selected, linux, origin);
            else if (answer is "reset-tree" or "inherit-tree" && Services.Permissions is DarwinPermissionService darwin)
                await ShowReviewedDarwinAclTreeAsync(dialogs, selected, darwin, reset: answer is "reset-tree", origin);
            else await ShowReviewedModeTreeAsync(dialogs, selected, origin);
        }
        else if (answer is "estimate-access" && estimates[view.SelectedSubject] is { } estimate)
            await dialogs.ShowCustomAsync("Estimate POSIX ACL access", new PosixAccessEstimateView(inspected[view.SelectedSubject].Snapshot, estimate),
                [new("Close", "close", IsDefault: true, IsCancel: true)], resizable: true,
                behavior: new DialogBehavior { DismissOnBackdrop = false, PreferredSize = new Avalonia.Size(700, 590) });
        else if (answer is string command && command is "mode" or "acl" or "owner" or "defaults" or "group" or "protection" &&
            refusals[view.SelectedSubject] is null && (command != "defaults" || DefaultsAvailable(view.SelectedSubject)) &&
            (command != "acl" || AclAvailable(view.SelectedSubject)) &&
            (command != "protection" || ProtectionAvailable(view.SelectedSubject)) &&
            (command != "group" || GroupAvailable(view.SelectedSubject)) && !Services.Io.IsStopped)
            await ShowBoundPermissionEditorAsync(dialogs, inspected[view.SelectedSubject], copy, command, origin);
    }

    private async Task ShowWindowsPropagationEditorAsync(IDialogService dialogs, InspectedPermission item, Func<string, Task>? copy,
        PermissionSelection? origin)
    {
        try
        {
            var device = Services.Providers.For(item.Source!.Parent).GetDeviceKey(item.Source.Parent);
            var boundary = await Services.Io.RunOwned(device, IoPriority.Interactive,
                ct => WindowsPermissionService.CapturePropagationBoundary(item.Snapshot, ct));
            WindowsInheritanceOptions? options = null;
            if (Services.Permissions is WindowsPermissionService service && service.CanReviewInheritanceChange(item.Snapshot))
            {
                options = new(null, null);
                if (item.Snapshot.Inheritance.Value?.IsProtected == true)
                {
                    try
                    {
                        var parent = await Services.Io.RunOwned(device, IoPriority.Interactive,
                            ct => WindowsPermissionService.CaptureInheritanceParent(item.Snapshot, boundary, ct));
                        options = new(parent, null);
                    }
                    catch (Exception error) when (error is IOException or NotSupportedException or UnauthorizedAccessException)
                    {
                        options = new(null, Formatters.SafeName(error.Message));
                    }
                }
            }
            if (!Services.Io.IsStopped)
                await ShowBoundPermissionEditorAsync(dialogs, item, copy, "acl", origin, windowsPropagation: boundary, windowsInheritance: options);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (!Services.Io.IsStopped)
                await dialogs.AlertAsync("Windows inheritance unavailable", Formatters.SafeName(error.Message));
        }
    }

    private sealed record WindowsInheritanceOptions(WindowsPermissionService.InheritanceParent? Parent, string? Reason);

    private async Task ShowWindowsAclEstimateAsync(IDialogService dialogs, InspectedPermission item, WindowsPermissionService windows)
    {
        var snapshot = item.Snapshot;
        var subject = snapshot.Subject with { ExpectedIdentity = snapshot.Binding!.Identity, ListedSize = null, ListedModifiedUtcTicks = null };
        var content = new StackPanel { Spacing = 7 };
        content.Children.Add(Wrapped(Formatters.SafeName(subject.DisplayPath)));
        try
        {
            var source = item.Source!;
            var device = Services.Providers.For(source.Parent).GetDeviceKey(source.Parent);
            var report = await Services.Io.RunOwned(device, IoPriority.Interactive, ct => windows.EstimateAccess(subject, ct));
            if (Services.Io.IsStopped) return;
            content.Children.Add(Wrapped($"Estimated {report.CheckedAt.ToLocalTime():G} for {report.UserSid}"));
            string restrictions = report.Restricted ? "restricted SIDs present" : "no restricted SIDs";
            content.Children.Add(Wrapped($"Windows process token · {report.EnabledGroups} enabled groups · {restrictions}"));
            if (report.GrantedMask is { } mask)
                content.Children.Add(Wrapped($"Discretionary grant mask · 0x{mask:X8}"));
            if (report.Reason is { } reason) content.Children.Add(Wrapped(reason));
            if (report.NativeError is { } code) content.Children.Add(Wrapped($"Windows Authz status · {code}"));
            foreach (var check in report.Checks)
            {
                string status = check.Status switch
                {
                    PermissionReadStatus.Present => "Granted by the discretionary ACL calculation",
                    PermissionReadStatus.Denied => "No discretionary grant",
                    _ => "Unknown"
                };
                content.Children.Add(Wrapped($"{check.Right} · {status}"));
            }
            content.Children.Add(Wrapped("Actual access can also depend on mandatory integrity, privileges, central policy, file sharing and the parent directory."));
        }
        catch (OperationCanceledException) { return; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            content.Children.Add(Wrapped(Formatters.SafeName(error.Message)));
        }
        await dialogs.ShowCustomAsync("Windows ACL access estimate", content, [new("Close", "close", IsDefault: true, IsCancel: true)],
            resizable: true, behavior: new DialogBehavior { DismissOnBackdrop = false, PreferredSize = new Avalonia.Size(700, 535) });
    }

    private async Task ShowCurrentAccessReportAsync(IDialogService dialogs, InspectedPermission item)
    {
        var source = item.Source!;
        var snapshot = item.Snapshot;
        var subject = snapshot.Subject with { ExpectedIdentity = snapshot.Binding!.Identity, ListedSize = null, ListedModifiedUtcTicks = null };
        try
        {
            var device = Services.Providers.For(source.Parent).GetDeviceKey(source.Parent);
            var content = new StackPanel { Spacing = 7 };
            content.Children.Add(Wrapped(Formatters.SafeName(subject.DisplayPath)));
            ImmutableArray<PermissionAccessCheck> checks;
            if (Services.Permissions is DarwinPermissionService darwin)
            {
                var report = await Services.Io.RunOwned(device, IoPriority.Interactive, ct => darwin.CheckEffectiveAccess(subject, ct));
                if (Services.Io.IsStopped) return;
                content.Children.Add(Wrapped($"Checked {report.CheckedAt.ToLocalTime():G} on mount {report.Identity.MountInstance}"));
                content.Children.Add(Wrapped($"Real UID {report.RealUid} · GID {report.RealGid} · effective UID {report.EffectiveUid} · GID {report.EffectiveGid}"));
                AddGroups("macOS credential groups", report.Groups.Select(group => group.ToString(System.Globalization.CultureInfo.InvariantCulture)), report.Groups.Length);
                AddMountRestrictions(report.ReadOnly, report.NoExecute, report.NoSetId);
                if (!report.HonorsOwnership) content.Children.Add(Wrapped("This mount ignores stored ownership."));
                if ((report.FileFlags & DarwinPermissionService.UserLocked) != 0) content.Children.Add(Wrapped("Finder Locked is set."));
                if ((report.FileFlags & DarwinPermissionService.UserAppendOnly) != 0) content.Children.Add(Wrapped("User append-only is set."));
                if ((report.FileFlags & DarwinPermissionService.SystemImmutable) != 0) content.Children.Add(Wrapped("System immutable is set."));
                if ((report.FileFlags & DarwinPermissionService.SystemAppendOnly) != 0) content.Children.Add(Wrapped("System append-only is set."));
                checks = report.Checks;
            }
            else if (Services.Permissions is LinuxPermissionService linux)
            {
                var report = await Services.Io.RunOwned(device, IoPriority.Interactive, ct => linux.CheckEffectiveAccess(subject, ct));
                if (Services.Io.IsStopped) return;
                content.Children.Add(Wrapped($"Checked {report.CheckedAt.ToLocalTime():G} on mount {report.Identity.MountInstance}"));
                content.Children.Add(Wrapped($"Effective UID {report.EffectiveUid} · GID {report.EffectiveGid} · capabilities 0x{report.EffectiveCapabilities}"));
                if (report.FileSystemUid != report.EffectiveUid || report.FileSystemGid != report.EffectiveGid)
                    content.Children.Add(Wrapped($"Filesystem UID {report.FileSystemUid} · GID {report.FileSystemGid}"));
                AddGroups("Supplementary groups", report.SupplementaryGroups, report.SupplementaryGroups.Length);
                AddMountRestrictions(report.ReadOnly, report.NoExecute, report.NoSetId);
                checks = report.Checks;
            }
            else return;
            foreach (var check in checks)
            {
                string status = check.Status switch
                {
                    PermissionReadStatus.Present => "Allowed by the current access check",
                    PermissionReadStatus.Denied => "Denied by the current access check",
                    _ => "Unknown"
                };
                string error = check.NativeError is { } code ? $" (errno {code})" : "";
                content.Children.Add(Wrapped($"{check.Right} · {status}{error}"));
            }
            content.Children.Add(Wrapped("Results apply to this object at the time shown. Creating, replacing, renaming or removing an entry also depends on its parent, sticky bits and other policy. Executing a file can depend on its interpreter and other policy."));
            await dialogs.ShowCustomAsync("Current process access", content, [new("Close", "close", IsDefault: true, IsCancel: true)], resizable: true,
                behavior: new DialogBehavior { DismissOnBackdrop = false, PreferredSize = new Avalonia.Size(700, 535) });

            void AddGroups(string label, IEnumerable<string> ids, int count)
            {
                string groups = count == 0 ? "none" : string.Join(", ", ids.Take(64));
                if (count > 64) groups += $" and {count - 64} more";
                content.Children.Add(Wrapped(label + " · " + groups));
            }

            void AddMountRestrictions(bool readOnly, bool noExecute, bool noSetId)
            {
                if (readOnly) content.Children.Add(Wrapped("The mount is read-only."));
                if (noExecute) content.Children.Add(Wrapped("The mount disallows executing regular files."));
                if (noSetId) content.Children.Add(Wrapped("The mount does not honor set-ID bits at execution."));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (!Services.Io.IsStopped) await dialogs.AlertAsync("Access check unavailable", Formatters.SafeName(error.Message));
        }
    }

    private async Task ShowBoundPermissionEditorAsync(IDialogService dialogs, InspectedPermission item, Func<string, Task>? copy,
        string command, PermissionSelection? origin, SftpPermissionEdit? sftpEdit = null, LinuxAuthorizedPermissionEdit? authorizedLinux = null,
        nint windowsAuthorizationOwner = 0, WindowsPermissionService.PropagationBoundary? windowsPropagation = null,
        WindowsInheritanceOptions? windowsInheritance = null, bool windowsFolderOnly = false)
    {
        var snapshot = authorizedLinux?.Snapshot ?? item.Snapshot;
        var source = item.Source ?? throw new InvalidOperationException("The reviewed object lacks its original source.");
        bool windows = snapshot.Model == PermissionModel.WindowsDescriptor;
        bool authorizedWindows = windowsAuthorizationOwner != 0;
        bool windowsVolumeRoot = (windowsFolderOnly || authorizedWindows) && snapshot.Binding?.Identity.Kind == PermissionObjectKind.VolumeRoot;
        bool darwin = snapshot.Model == PermissionModel.DarwinAcl;
        bool linux = Services.Permissions is LinuxPermissionService && sftpEdit is null;
        bool defaults = command == "defaults";
        bool changeGroup = command == "group";
        bool changeProtection = command == "protection";
        bool editDarwinAcl = darwin && command == "acl";
        var effect = darwin && (changeGroup || editDarwinAcl) ? DarwinPermissionService.AccessChangeSections :
            changeGroup ? LinuxPermissionService.GroupChangeSections(snapshot) :
            defaults ? PermissionSections.Defaults : windows ? WindowsPermissionService.AuthorizedRevisionSections(
                command == "audit" ? PermissionSections.Audit : PermissionSections.Access) :
            snapshot.Model == PermissionModel.UnixMode ? PermissionSections.Mode :
                PermissionSections.Mode | PermissionSections.Access | (changeProtection ? PermissionSections.Flags : PermissionSections.None);
        ushort originalMode = snapshot.Mode.Value ?? 0;
        var originalRevision = PermissionRevision.Read(snapshot, effect);
        var device = Services.Providers.For(source.Parent).GetDeviceKey(source.Parent);
        var acl = !windows && !darwin && (command is "acl" or "defaults") ? new PosixAccessEditor(snapshot, copy, defaults) : null;
        var darwinAcl = editDarwinAcl ? new DarwinAccessEditor(snapshot, copy) : null;
        var mode = editDarwinAcl || command is "mode" or "sftp" || (authorizedLinux is null && command is "group" or "protection")
            ? new UnixPermissionModeEditor(bit => (originalMode & (ushort)bit) != 0,
            snapshot.Access.Value?.Acl is PosixAcl original && original.Entries.Any(row => row.Tag == PosixAclTag.Mask)) : null;
        var windowsAcl = windows && command == "acl" && snapshot.Access.Value?.Acl is WindowsDacl dacl
            ? new WindowsAccessEditor(dacl, snapshot.Subject.Authority.Id, copy, directory: windowsFolderOnly || windowsVolumeRoot || windowsPropagation is not null) : null;
        (string Label, WindowsInheritanceAction? Action)[] inheritanceChoices = windowsInheritance is null ? [] :
            snapshot.Inheritance.Value?.IsProtected == true
            ? [("Edit access rules", null), ("Enable inheritance from parent", WindowsInheritanceAction.Enable)]
            : [("Edit access rules", null), ("Protect and copy inherited rules", WindowsInheritanceAction.ProtectCopy),
                ("Protect and remove inherited rules", WindowsInheritanceAction.ProtectRemove)];
        var inheritanceChoice = windowsInheritance is not null && windowsAcl is not null
            ? new ComboBox { ItemsSource = inheritanceChoices.Select(choice => choice.Label).ToArray(), SelectedIndex = 0 }
            : null;
        if (inheritanceChoice is not null) AutomationProperties.SetName(inheritanceChoice, "Windows DACL inheritance action");
        var windowsAudit = authorizedWindows && command == "audit" && snapshot.Audit.Value?.Acl is WindowsSacl sacl
            ? new WindowsAuditEditor(sacl, snapshot.Subject.Authority.Id) : null;
        ComboBox? owner = null;
        IReadOnlyList<Principal>? ownerChoices = null;
        ComboBox? group = null;
        Principal[] groupChoices = [];
        LinuxGroupAuthority? groupAuthority = null;
        DarwinGroupAuthority? darwinGroupAuthority = null;
        if (windows && command == "owner")
        {
            try
            {
                ownerChoices = await Services.Io.RunOwned(device, IoPriority.Interactive,
                    ct =>
                    {
                        ct.ThrowIfCancellationRequested();
                        return WindowsPermissionService.OwnerChoices(snapshot.Subject.Authority);
                    });
                if (Services.Io.IsStopped) return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
            {
                await dialogs.AlertAsync("Owner choices unavailable", Formatters.SafeName(error.Message));
                return;
            }
            owner = new ComboBox { ItemsSource = ownerChoices.Select(choice => Formatters.SafeName(choice.Id)).ToArray(),
                SelectedIndex = ownerChoices.ToList().FindIndex(choice => choice == snapshot.Owner.Value) };
            AutomationProperties.SetName(owner, "Assignable owner SID");
        }
        StackPanel? groupFields = null;
        string? optionalGroupProblem = null;
        if (authorizedLinux is null && (changeGroup || editDarwinAcl && DarwinPermissionService.CanEditGroup(snapshot)))
        {
            try
            {
                if (darwin)
                {
                    darwinGroupAuthority = await Services.Io.RunOwned(device, IoPriority.Interactive, DarwinPermissionService.ReadGroupAuthority);
                    groupChoices = darwinGroupAuthority.Choices(snapshot.Subject.Authority);
                }
                else
                {
                    groupAuthority = await Services.Io.RunOwned(device, IoPriority.Interactive, LinuxPermissionService.ReadGroupAuthority);
                    groupChoices = groupAuthority.Choices(snapshot.Subject.Authority);
                }
                if (Services.Io.IsStopped) return;
                groupChoices = groupChoices.Where(choice => choice != snapshot.Group.Value).ToArray();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
            {
                if (changeGroup)
                {
                    await dialogs.AlertAsync("Group choices unavailable", Formatters.SafeName(error.Message));
                    return;
                }
                optionalGroupProblem = Formatters.SafeName(error.Message);
            }

            if (changeGroup && groupChoices.Length == 0)
            {
                await dialogs.AlertAsync("Group choices unavailable", "FileCat's ordinary credential has no other assignable group.");
                return;
            }
            if (groupChoices.Length > 0)
            {
                var ids = groupChoices.Select(choice => choice.Id);
                group = new ComboBox { ItemsSource = (editDarwinAcl ? ids.Prepend("Keep current group") : ids).ToArray(),
                    SelectedIndex = editDarwinAcl ? 0 : -1 };
                AutomationProperties.SetName(group, "Assignable group ID");
                groupFields = new StackPanel { Spacing = 8, Children =
                    { Wrapped($"Current group ID {snapshot.Group.Value?.Id}"), new Label { Content = "New group ID", Target = group }, group } };
                if (!editDarwinAcl) groupFields.Children.Add(mode!);
            }
        }
        TextBox? authorizedPrincipal = null;
        if (authorizedLinux is not null && command is "owner" or "group")
        {
            bool changingOwner = command == "owner";
            string newId = changingOwner ? "New owner UID" : "New group GID";
            var currentId = changingOwner ? snapshot.Owner.Value?.Id : snapshot.Group.Value?.Id;
            authorizedPrincipal = new TextBox { MaxLength = 10, Text = currentId };
            AutomationProperties.SetName(authorizedPrincipal, newId);
            groupFields = new StackPanel { Spacing = 8, Children =
                { Wrapped($"Current {(changingOwner ? "owner UID" : "group GID")} {currentId}"),
                    new Label { Content = newId, Target = authorizedPrincipal }, authorizedPrincipal } };
        }
        TextBox? serverGroup = null;
        if (sftpEdit is not null)
        {
            serverGroup = new TextBox { MaxLength = 32, PlaceholderText = "Keep current group" };
            AutomationProperties.SetName(serverGroup, "New server group ID");
            mode!.IsEnabled = !sftpEdit.ModeWritesRefused;
            var remoteFields = new StackPanel { Spacing = 8, Children =
            {
                Wrapped($"Server owner ID {snapshot.Owner.Value?.Id} · Current group ID {snapshot.Group.Value?.Id}"),
                new Label { Content = "New server group ID", Target = serverGroup }, serverGroup,
                Wrapped("Enter a numeric group ID. The server checks whether this account may assign it."), mode
            } };
            if (!mode.IsEnabled) remoteFields.Children.Add(Wrapped("The server has refused mode changes in this session."));
            groupFields = remoteFields;
        }
        uint lockBit = darwin ? DarwinPermissionService.UserLocked : LinuxPermissionService.ImmutableInode;
        uint appendBit = darwin ? DarwinPermissionService.UserAppendOnly : LinuxPermissionService.AppendOnlyInode;
        CheckBox? lockedFlag = null, appendOnly = null;
        if (changeProtection || authorizedLinux is null && ((changeGroup || editDarwinAcl) && darwin && DarwinPermissionService.CanEditProtection(snapshot) ||
            linux && (changeGroup || acl is not null) && LinuxPermissionService.CanEditProtection(snapshot)))
        {
            ulong originalFlags = snapshot.Flags.Value!.Value.Bits;
            lockedFlag = new CheckBox { Content = darwin ? "Finder Locked" : "Immutable", IsChecked = (originalFlags & lockBit) != 0 };
            appendOnly = new CheckBox { Content = "Append only", IsChecked = (originalFlags & appendBit) != 0 };
            if (!editDarwinAcl)
            {
                if (groupFields is null)
                {
                    groupFields = new StackPanel { Spacing = 8, Children = { lockedFlag, appendOnly } };
                    if (((Control?)acl ?? mode) is { } accessFields) groupFields.Children.Add(accessFields);
                }
                else
                {
                    groupFields.Children.Insert(groupFields.Children.Count - 1, lockedFlag);
                    groupFields.Children.Insert(groupFields.Children.Count - 1, appendOnly);
                }
            }
        }
        if (darwinAcl is not null)
        {
            var optional = groupFields ?? new StackPanel { Spacing = 8 };
            if (optionalGroupProblem is not null) optional.Children.Add(Wrapped(optionalGroupProblem));
            optional.Children.Add(mode!);
            if (lockedFlag is not null) optional.Children.Add(lockedFlag);
            if (appendOnly is not null) optional.Children.Add(appendOnly);
            bool locked = (snapshot.Flags.Value.GetValueOrDefault().Bits & DarwinPermissionService.UserProtection) != 0;
            groupFields = new StackPanel { Spacing = 8, Children = { darwinAcl,
                new Expander { Header = "BSD mode, group and file protection", Content = optional, IsExpanded = locked } } };
        }
        Control fields = groupFields ?? (Control?)acl ?? (Control?)mode ?? (Control?)windowsAcl ?? (Control?)windowsAudit ?? owner
            ?? throw new NotSupportedException("This file has no supported permissions editor.");
        if (inheritanceChoice is not null)
            fields = new StackPanel { Spacing = 8, Children =
                { new Label { Content = "Access rule or inheritance change", Target = inheritanceChoice }, inheritanceChoice, fields } };
        var problem = PermissionNotice();
        var result = PermissionNotice();
        var preview = new ListBox { MaxHeight = 180, IsVisible = false, ItemTemplate =
            new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }) };
        AutomationProperties.SetName(preview, "Reviewed changes");
        var accept = new CheckBox
        {
            Content = windowsPropagation is not null ? "Allow Windows to update inheriting descendants" :
                windowsVolumeRoot ? "Approve this volume root access change" :
                changeGroup || editDarwinAcl || sftpEdit is not null ? "Accept the reviewed set-ID effects" :
                "Accept clearing the reviewed set-ID bits",
            IsVisible = false
        };
        var stop = new Button { Content = "Stop operation", IsVisible = false };
        var current = new Button { Content = "Review current state…", IsVisible = false };
        var footer = new DialogButtonPresentation(new("Cancel"));
        var close = new DialogButtonPresentation(new("Close permissions editor"));
        var closer = new DialogCloser();
        var layout = new StackPanel { Spacing = 8, Children =
        {
            Wrapped(Formatters.SafeName(snapshot.Subject.DisplayPath)),
            Wrapped(windowsPropagation is not null ? "Scope · Selected folder and Windows inheritance" :
                windowsVolumeRoot ? "Scope · Selected NTFS volume root" :
                windowsFolderOnly ? "Scope · Selected folder" :
                defaults ? "Scope · Defaults for new children of the selected directory" : "Scope · Selected object"),
            new TextBlock { Text = "Owner choices come from FileCat's ordinary Windows token.", IsVisible = owner is not null },
            fields, preview, accept, problem, result, stop, current
        } };
        if (darwin) layout.Children.Insert(2, Wrapped(snapshot.Binding?.DarwinRoute == DarwinBindingRoute.Descriptor
            ? DarwinPermissionService.HeldDescriptorReviewNotice : DarwinPermissionService.ParentRelativeReviewNotice));
        if (authorizedLinux is not null || authorizedWindows)
            layout.Children.Insert(2, Wrapped("Applying this reviewed change requires a separate administrator approval."));
        if (windowsAudit is not null)
            layout.Children.Insert(3, Wrapped("Windows audit policy determines whether successful or failed access creates an event."));
        if (windowsFolderOnly || windowsVolumeRoot)
        {
            layout.Children.Insert(2, Wrapped($"Volume · {Formatters.SafeName(snapshot.Binding!.MountRoot)}"));
            layout.Children.Insert(3, Wrapped(windowsVolumeRoot
                ? "The write targets the volume root. It can change who can list the root and create entries, and which rules immediate new files and folders inherit."
                : "The write targets the selected folder's DACL. Windows can inherit these rules when it creates new children."));
        }
        if (windowsPropagation is { } boundary)
        {
            layout.Children.Insert(2, Wrapped($"Volume · {Formatters.SafeName(boundary.Root.MountRoot)}"));
            layout.Children.Insert(3, Wrapped($"Inspected {Formatters.Plural(boundary.Members.Length, "path", "paths")} including the selected folder."));
            layout.Children.Insert(4, Wrapped("Windows may update eligible existing descendants and inheritance for future children. " +
                "FileCat verifies the selected folder. Descendant effects remain unverified."));
            if (windowsInheritance?.Parent is { } parent)
                layout.Children.Insert(5, Wrapped("Reviewed inheritance parent · " + Formatters.SafeName(parent.Locator)));
        }
        if (sftpEdit is not null)
        {
            layout.Children.Insert(2, Wrapped("Connection · " + Formatters.SafeName(snapshot.Sftp!.Connection ?? source.Parent.Session ?? "")));
            layout.Children.Insert(3, Wrapped("SFTP identifies this item by its server path. Server administrators, processes using this account, and accounts with ancestor write rights through server ACLs can still redirect a request."));
        }

        bool active = false, consumed = false, confirmingDiscard = false, dirty = false, canApply = false;
        Task? applying = null;
        Job? submitted = null;
        using var lifetime = new CancellationTokenSource();
        CancellationToken cancellation = lifetime.Token;
        stop.Click += (_, _) => { submitted?.Cancel(); stop.IsEnabled = false; };
        current.Click += async (_, _) =>
        {
            if (!current.IsEnabled || cancellation.IsCancellationRequested) return;
            current.IsEnabled = false;
            try
            {
                using var observation = await Services.Io.RunOwned(device, IoPriority.Interactive, ct =>
                {
                    var subject = snapshot.Subject with { ExpectedIdentity = snapshot.Binding?.Identity, ListedSize = null, ListedModifiedUtcTicks = null };
                    if (sftpEdit is null) return Services.Permissions.Inspect(subject, PermissionReadSections, ct);
                    var observed = Services.SftpProvider.InspectSftpPermissions(source.Parent, [subject], ct)[0];
                    if (observed.ServerState?.Target != snapshot.ServerState!.Target)
                        throw new PermissionBindingConflictException("The original SFTP server path and connection could not be corroborated. Inspect the item again from its panel.");
                    return new PermissionInspection(observed);
                }, cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (authorizedLinux is not null && observation.Snapshot.Binding is not null)
                {
                    await ShowAuthorizedLinuxPermissionsAsync(dialogs, new(item.Name, source, observation), copy, origin: null);
                    return;
                }
                if (authorizedWindows && CanInspectWithWindowsAuthorization(new(item.Name, source, observation)))
                {
                    await ShowAuthorizedWindowsPermissionsAsync(dialogs, new(item.Name, source, observation), copy, origin: null);
                    return;
                }
                await dialogs.ShowCustomAsync("Current permissions and ownership", new PermissionInspectorView([(item.Name, observation.Snapshot)], copy,
                    principalNames: Services.PermissionPrincipalNames),
                    [new("Close", "close", IsDefault: true, IsCancel: true)], resizable: true,
                    behavior: new DialogBehavior { DismissOnBackdrop = false });
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                if (!cancellation.IsCancellationRequested) ShowResult(Formatters.SafeName(error.Message));
            }
            finally { if (!cancellation.IsCancellationRequested) current.IsEnabled = true; }
        };
        if (acl is not null) acl.Changed += DraftChanged;
        if (darwinAcl is not null) darwinAcl.Changed += DraftChanged;
        if (mode is not null) mode.Changed += DraftChanged;
        if (windowsAcl is not null) windowsAcl.Changed += DraftChanged;
        if (inheritanceChoice is not null)
            inheritanceChoice.SelectionChanged += (_, _) =>
            {
                windowsAcl!.IsVisible = inheritanceChoice.SelectedIndex == 0;
                DraftChanged();
            };
        if (windowsAudit is not null) windowsAudit.Changed += DraftChanged;
        if (owner is not null) owner.SelectionChanged += (_, _) => DraftChanged();
        if (group is not null) group.SelectionChanged += (_, _) => DraftChanged();
        if (serverGroup is not null) serverGroup.TextChanged += (_, _) => DraftChanged();
        if (authorizedPrincipal is not null) authorizedPrincipal.TextChanged += (_, _) => DraftChanged();
        if (lockedFlag is not null) lockedFlag.IsCheckedChanged += (_, _) => DraftChanged();
        if (appendOnly is not null) appendOnly.IsCheckedChanged += (_, _) => DraftChanged();
        accept.IsCheckedChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        try
        {
            await dialogs.ShowCustomAsync(windowsPropagation is not null ? "Review Windows inheritance" : command switch
                { "acl" => "Edit access ACL", "audit" => "Edit audit rules", "defaults" => "Edit default ACL", "owner" => "Change owner", "group" => "Change group and mode",
                    "sftp" => "Review SFTP permissions", "protection" => "Edit file protection", _ => "Edit mode" }, layout,
                [new("Cancel", "close", IsCancel: true) { Presentation = footer, Validate = CanClose },
                 new(authorizedLinux is not null || authorizedWindows ? "Apply with authorization" : "Apply", "apply", IsDefault: true)
                     { IsAvailable = () => canApply, InvokeAsync = () => applying = ApplyAsync() }],
                initialFocus: FirstField(fields), resizable: true, closer: closer, behavior: new DialogBehavior
                {
                    DismissOnBackdrop = false, KeepOperationsUsableDuringAction = true, ConfirmOnOptionEnter = false, ConfirmOnListItemEnter = false,
                    ClosePresentation = close, PreferredSize = new Avalonia.Size(760, 665),
                    OnImplicitDismiss = () => { if (CanClose()) closer.Close("close"); }
                });
        }
        finally
        {
            lifetime.Cancel();
            if (applying is not null) await applying;
        }

        void DraftChanged() { accept.IsChecked = false; UpdatePreview(); }

        bool CanClose()
        {
            if (!dirty || consumed) return true;
            if (!confirmingDiscard) _ = ConfirmDiscardAsync();
            return false;
        }

        async Task ConfirmDiscardAsync()
        {
            confirmingDiscard = true;
            try
            {
                if (await dialogs.ConfirmAsync("Discard changes?", "Discard the unsent permission changes?", "Discard", cancelText: "Keep editing"))
                    closer.Close("close");
            }
            finally { confirmingDiscard = false; }
        }

        void UpdatePreview()
        {
            canApply = false;
            if (active || consumed) return;
            var draft = ReadDraft(out var projected, out var changePlan, out var sftpPlan, out string? error);
            var rows = sftpPlan is not null ? DescribeSftpPlan(sftpEdit!, sftpPlan) :
                projected is null ? Array.Empty<string>() : changePlan is not null ? DescribePermissionPlan(snapshot, changePlan) :
                authorizedLinux is not null ? DescribeAuthorizedLinuxChange(snapshot, projected, draft!.Changes[0]) :
                windows ? DescribeWindowsPermissionDiff(snapshot, projected, draft!.Changes[0]) :
                changeProtection && darwin ? DescribeProtectionDiff(snapshot, projected) : DescribePermissionDiff(snapshot, projected, defaults);
            preview.ItemsSource = rows;
            preview.IsVisible = rows.Length > 0;
            accept.IsVisible = windowsPropagation is not null || windowsVolumeRoot ? draft is not null :
                sftpPlan?.NeedsSetIdConsent ?? changePlan?.NeedsSetIdConsent ??
                (projected?.Mode.Value is { } newMode && (originalMode & 0xc00 & ~newMode) != 0);
            if (accept.IsVisible && accept.IsChecked != true)
                error ??= windowsPropagation is not null ? "Approve Windows inheritance before applying." :
                    windowsVolumeRoot ? "Approve the reviewed volume root change before applying." :
                    changeGroup || editDarwinAcl || sftpEdit is not null ? "Accept the reviewed set-ID effects before applying." :
                    "Accept the reviewed set-ID clearance before applying.";
            problem.Text = error;
            problem.IsVisible = error is not null;
            dirty = draft is not null || error is not null || changeGroup && mode?.HasChanges == true;
            canApply = draft is not null && error is null;
        }

        ChangePermissionFlags? ReadProtectionFlags()
        {
            if (lockedFlag is null || appendOnly is null) return null;
            ulong originalFlags = snapshot.Flags.Value!.Value.Bits & (lockBit | appendBit);
            ulong requestedFlags = (lockedFlag.IsChecked == true ? lockBit : 0u) |
                (appendOnly.IsChecked == true ? appendBit : 0u);
            ulong touched = originalFlags ^ requestedFlags;
            return touched == 0 ? null : new(requestedFlags & touched, touched);
        }

        PermissionDraft? ReadDraft(out PermissionSnapshot? projected, out PermissionChangePlan? changePlan, out SftpPermissionPlan? sftpPlan, out string? error)
        {
            projected = null;
            changePlan = null;
            sftpPlan = null;
            error = null;
            if (authorizedLinux is not null) return ReadAuthorizedDraft(out projected, out error);
            if (changeProtection)
            {
                if (!mode!.TryGetChange(out var modeChange))
                {
                    error = "Correct the octal value before applying.";
                    return null;
                }
                var changes = ImmutableArray.CreateBuilder<PermissionDelta>(2);
                if (ReadProtectionFlags() is { } flags) changes.Add(flags);
                if (((originalMode ^ modeChange.Bits) & modeChange.TouchedBits) != 0) changes.Add(modeChange);
                if (changes.Count == 0) return null;
                var draft = new PermissionDraft(PermissionScope.Objects, changes.ToImmutable());
                try
                {
                    if (darwin) projected = DarwinPermissionService.PreviewProtection(snapshot, draft.Changes);
                    else if (changes.Any(change => change is ChangePermissionFlags))
                    {
                        changePlan = LinuxPermissionService.PreviewProtection(snapshot, draft, null);
                        projected = changePlan.Expected;
                    }
                    else
                    {
                        LinuxPermissionService.RequireUnprotected(snapshot);
                        projected = LinuxPermissionService.PreviewAccessChange(snapshot, modeChange);
                    }
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
                {
                    error = Formatters.SafeName(failure.Message);
                    return null;
                }
                return draft;
            }
            if (sftpEdit is not null)
            {
                if (!mode!.TryGetChange(out var modeChange))
                {
                    error = "Correct the octal value before applying.";
                    return null;
                }
                var changes = ImmutableArray.CreateBuilder<PermissionDelta>(2);
                if (serverGroup!.Text is { Length: > 0 } id)
                {
                    if (!SftpPermissionMutation.TryGroupId(id, out _))
                    {
                        error = SftpPermissionMutation.GroupIdRefusal;
                        return null;
                    }
                    changes.Add(new AssignPermissionPrincipal(false, new Principal(PrincipalKind.UnixGroup, id, snapshot.Subject.Authority.Id)));
                }
                if (modeChange.TouchedBits != 0) changes.Add(modeChange);
                if (changes.Count == 0) return null;
                var draft = new PermissionDraft(PermissionScope.Objects, changes.ToImmutable());
                try
                {
                    var plan = sftpEdit.Preview(draft);
                    if (plan.Stages.All(stage => stage.Before == stage.After)) return null;
                    sftpPlan = plan;
                    return draft;
                }
                catch (RefusedOperationException refusal)
                {
                    error = refusal.Message;
                    return null;
                }
            }
            if (changeGroup || darwinAcl is not null)
            {
                if (!mode!.TryGetChange(out var modeChange))
                {
                    error = "Correct the octal value before applying.";
                    return null;
                }
                var changes = ImmutableArray.CreateBuilder<PermissionDelta>();
                if (changeGroup && group!.SelectedIndex < 0) return null;
                int selectedGroup = (group?.SelectedIndex ?? -1) - (editDarwinAcl ? 1 : 0);
                if (selectedGroup >= 0) changes.Add(new AssignPermissionPrincipal(false, groupChoices[selectedGroup]));
                if (darwinAcl is not null)
                {
                    var (aclChange, aclError) = darwinAcl.ReadChange();
                    if (aclError is not null) { error = aclError; return null; }
                    if (aclChange is not null) changes.Add(aclChange);
                }
                if (modeChange.TouchedBits != 0) changes.Add(modeChange);
                if (ReadProtectionFlags() is { } flags) changes.Add(flags);
                if (changes.Count == 0) return null;
                var draft = new PermissionDraft(PermissionScope.Objects, changes.ToImmutable());
                try
                {
                    changePlan = darwin ? DarwinPermissionService.PreviewAccessChanges(snapshot, draft, darwinGroupAuthority)
                        : draft.Changes.Any(change => change is ChangePermissionFlags)
                            ? LinuxPermissionService.PreviewProtection(snapshot, draft, groupAuthority)
                            : LinuxPermissionService.PreviewGroupChange(snapshot, draft, groupAuthority!);
                    if (changePlan.Stages.IsEmpty) return null;
                    projected = changePlan.Expected;
                    return draft;
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
                {
                    error = Formatters.SafeName(failure.Message);
                    return null;
                }
            }
            if (acl?.PendingEntry == true)
            {
                error = "Add the named entry or clear its account ID before applying.";
                return null;
            }
            PermissionDelta? change;
            if (owner is not null)
            {
                if (owner.SelectedIndex < 0) return null;
                change = new AssignPermissionPrincipal(true, ownerChoices![owner.SelectedIndex]);
            }
            else if (windowsAcl is not null)
            {
                int inheritanceIndex = inheritanceChoice?.SelectedIndex ?? 0;
                if (inheritanceIndex > 0 && inheritanceChoices[inheritanceIndex].Action is { } action)
                {
                    if (action == WindowsInheritanceAction.Enable && windowsInheritance?.Parent is null)
                    {
                        error = windowsInheritance?.Reason ?? "A verified parent DACL is required.";
                        return null;
                    }
                    change = new ChangeWindowsInheritance(action);
                }
                else (change, error) = windowsAcl.ReadChange();
                if (change is null) return null;
            }
            else if (windowsAudit is not null)
            {
                (change, error) = windowsAudit.ReadChange();
                if (change is null) return null;
            }
            else if (acl is not null) change = acl.Change;
            else
            {
                if (!mode!.TryGetChange(out var modeChange))
                {
                    error = "Correct the octal value before applying.";
                    return null;
                }
                change = modeChange;
            }
            try
            {
                var expected = snapshot.Model switch
                {
                    PermissionModel.WindowsDescriptor when authorizedWindows => WindowsPermissionService.PreviewAuthorizedChange(snapshot, change),
                    PermissionModel.WindowsDescriptor when windowsPropagation is not null =>
                        WindowsPermissionService.PreviewPropagation(snapshot, change, windowsInheritance?.Parent),
                    PermissionModel.WindowsDescriptor when windowsFolderOnly => WindowsPermissionService.PreviewFolderOnly(snapshot, change),
                    PermissionModel.WindowsDescriptor => WindowsPermissionService.PreviewChange(snapshot, change),
                    PermissionModel.DarwinAcl when change is ChangePermissionMode changeMode => DarwinPermissionService.PreviewMode(snapshot, changeMode),
                    _ => LinuxPermissionService.PreviewAccessChange(snapshot, change)
                };
                bool changesAccess = PermissionRevision.Read(expected, effect) != originalRevision;
                if (linux && ReadProtectionFlags() is { } flags)
                {
                    var draft = changesAccess
                        ? new PermissionDraft(defaults ? PermissionScope.DefaultsForNewChildren : PermissionScope.Objects, [flags, change])
                        : new PermissionDraft(PermissionScope.Objects, [flags]);
                    changePlan = LinuxPermissionService.PreviewProtection(snapshot, draft, null);
                    projected = changePlan.Expected;
                    return draft;
                }
                if (!changesAccess) return null;
                if (linux) LinuxPermissionService.RequireUnprotected(snapshot);
                projected = expected;
                return new(windowsPropagation is not null ? PermissionScope.NativePropagation :
                    defaults ? PermissionScope.DefaultsForNewChildren : PermissionScope.Objects, [change]);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException or OverflowException or ArgumentException)
            {
                error = Formatters.SafeName(failure.Message);
                return null;
            }
        }

        PermissionDraft? ReadAuthorizedDraft(out PermissionSnapshot? projected, out string? error)
        {
            projected = null;
            error = null;
            PermissionDelta? change;
            if (authorizedPrincipal is not null)
            {
                if (!uint.TryParse(authorizedPrincipal.Text, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out uint id) || id == uint.MaxValue)
                {
                    error = "Enter a local numerical account ID from 0 to 4294967294.";
                    return null;
                }
                bool isOwner = command == "owner";
                change = new AssignPermissionPrincipal(isOwner, new Principal(isOwner ? PrincipalKind.UnixUser : PrincipalKind.UnixGroup,
                    id.ToString(System.Globalization.CultureInfo.InvariantCulture), snapshot.Subject.Authority.Id));
            }
            else if (changeProtection) change = ReadProtectionFlags();
            else if (acl is not null)
            {
                if (acl.PendingEntry)
                {
                    error = "Add the named entry or clear its account ID before applying.";
                    return null;
                }
                change = acl.Change;
            }
            else
            {
                if (!mode!.TryGetChange(out var modeChange))
                {
                    error = "Correct the octal value before applying.";
                    return null;
                }
                change = modeChange;
            }
            if (change is null) return null;
            try
            {
                var edit = authorizedLinux!;
                var expected = edit.Preview(change);
                var sections = edit.SectionsFor(change);
                var before = PermissionRevision.Read(snapshot, sections);
                var after = PermissionRevision.Read(expected, sections);
                if (before is null || after is null)
                {
                    error = "The requested change still depends on unread security metadata.";
                    return null;
                }
                if (after == before) return null;
                projected = expected;
                return new(defaults ? PermissionScope.DefaultsForNewChildren : PermissionScope.Objects, [change]);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException or ArgumentException)
            {
                error = Formatters.SafeName(failure.Message);
                return null;
            }
        }

        void CheckWindowsAuthorizationOrigin()
        {
            if (authorizedWindows && (windowsAuthorizationOwner != WindowsPermissionOwner() || origin is { IsCurrent: false }))
                throw new PermissionBindingConflictException("The original selection or workspace changed. Inspect the item again with authorization.");
        }

        async Task ApplyAsync()
        {
            if (!canApply || cancellation.IsCancellationRequested) return;
            var draft = ReadDraft(out var projected, out var acceptedPlan, out var sftpPlan, out _);
            if (draft is null || (projected is null && sftpPlan is null) || (accept.IsVisible && accept.IsChecked != true)) return;
            bool linuxProtection = linux && draft.Changes.Any(change => change is ChangePermissionFlags);
            var acceptedSections = authorizedLinux?.SectionsFor(draft.Changes[0]) ??
                (linuxProtection ? LinuxPermissionService.ProtectionChangeSections(snapshot, draft) : effect);
            var acceptedRevision = projected is null ? null : PermissionRevision.Read(projected, acceptedSections);
            bool acceptClear = accept.IsChecked == true;
            active = true;
            canApply = false;
            fields.IsEnabled = false;
            accept.IsEnabled = false;
            var completion = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Finished(Job job) { if (ReferenceEquals(Volatile.Read(ref submitted), job)) completion.TrySetResult(job); }
            try
            {
                ShowResult("Checking the reviewed object…");
                if (sftpEdit is not null && origin is { IsCurrent: false })
                    throw new PermissionBindingConflictException("The original selection changed. Open its SFTP permissions again.");
                if (authorizedLinux is not null && origin is { IsCurrent: false })
                    throw new PermissionBindingConflictException("The original selection changed. Inspect it again with authorization.");
                CheckWindowsAuthorizationOrigin();
                using var review = await Services.Io.RunOwned(device, IoPriority.Interactive, ct =>
                {
                    if (authorizedLinux is not null) return authorizedLinux.Prepare(source, draft, acceptClear, acceptedRevision!.Value, ct);
                    if (authorizedWindows) return WindowsPermissionService.PrepareAuthorizedChange(item.Inspection, source, draft, acceptedRevision!.Value,
                        Services.Paths.IsPortable, Services.Paths.ElevationExchangeDirectory, windowsAuthorizationOwner, ct,
                        approveVolumeRoot: windowsVolumeRoot && acceptClear);
                    if (sftpEdit is not null) return sftpEdit.Prepare(draft, sftpPlan!, acceptClear, ct);
                    var prepared = snapshot.Model switch
                    {
                        PermissionModel.WindowsDescriptor when windowsPropagation is not null && Services.Permissions is WindowsPermissionService windowsService
                            => windowsService.PrepareNativePropagation(item.Inspection, source, draft, windowsPropagation, ct, windowsInheritance?.Parent),
                        PermissionModel.WindowsDescriptor when windowsFolderOnly && Services.Permissions is WindowsPermissionService folderService
                            => folderService.PrepareFolderOnly(item.Inspection, source, draft, ct, approveVolumeRoot: windowsVolumeRoot && acceptClear),
                        PermissionModel.WindowsDescriptor => WindowsPermissionService.Prepare(item.Inspection, source, draft, ct),
                        PermissionModel.DarwinAcl => DarwinPermissionService.Prepare(item.Inspection, source, draft, acceptClear, ct, darwinGroupAuthority),
                        _ => LinuxPermissionService.Prepare(item.Inspection, source, draft, acceptClear, ct, groupAuthority)
                    };
                    var steps = prepared.Review.Targets[0].Components;
                    bool verifyStages = linuxProtection || darwin && draft.Changes.Any(change => change is DarwinAclDelta or AssignPermissionPrincipal);
                    bool sequenceMatches = !verifyStages || acceptedPlan is null ||
                        steps.Length == acceptedPlan.Stages.Length && steps.Zip(acceptedPlan.Stages).All(pair =>
                            pair.First.Change.Section == pair.Second.Change.Section && pair.First.NativeCall == pair.Second.NativeCall &&
                            pair.First.Expected == PermissionRevision.Read(pair.Second.Expected, acceptedSections));
                    if (sequenceMatches && PermissionRevision.Read(prepared.Expected, acceptedSections) == acceptedRevision) return prepared.Review;
                    prepared.Review.Dispose();
                    throw new PermissionBindingConflictException("The expected permissions changed after the review.");
                }, cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (sftpEdit is not null && origin is { IsCurrent: false })
                    throw new PermissionBindingConflictException("The original selection changed. Open its SFTP permissions again.");
                if (authorizedLinux is not null && origin is { IsCurrent: false })
                    throw new PermissionBindingConflictException("The original selection changed. Inspect it again with authorization.");
                CheckWindowsAuthorizationOrigin();
                Services.Jobs.JobFinished += Finished;
                try
                {
                    Services.Jobs.SubmitPermissions(review, job =>
                    {
                        Volatile.Write(ref submitted, job);
                        if (origin is not null && sftpEdit is null) _permissionOrigins[job] = origin;
                    });
                }
                catch
                {
                    if (submitted is not null) _permissionOrigins.Remove(submitted);
                    throw;
                }
                ShowResult(authorizedLinux is not null || authorizedWindows
                    ? "Waiting for administrator approval. Follow progress in Operations." : "Applying changes. Follow progress in Operations.");
                stop.IsVisible = true;
                var finished = await completion.Task.WaitAsync(cancellation);
                ShowResult(DescribePermissionJobResult(finished));
                consumed = true;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or InvalidDataException)
            {
                consumed = true;
                if (!cancellation.IsCancellationRequested) ShowResult(Formatters.SafeName(failure.Message));
            }
            finally
            {
                Services.Jobs.JobFinished -= Finished;
                active = false;
                if (!cancellation.IsCancellationRequested)
                {
                    stop.IsVisible = false;
                    current.IsVisible = consumed;
                    footer.Update(new("Close"));
                }
            }
        }

        void ShowResult(string text) { result.Text = text; result.IsVisible = true; }
    }

    private static string[] DescribeWindowsPermissionDiff(PermissionSnapshot original, PermissionSnapshot expected, PermissionDelta change) => change switch
    {
        ChangeWindowsInheritance inheritance => DescribeWindowsInheritance(original, expected, inheritance.Action),
        AssignPermissionPrincipal owner => [$"Owner · {Formatters.SafeName(original.Owner.Value?.Id ?? "unavailable")} → {Formatters.SafeName(owner.Principal.Id)}"],
        ChangeWindowsAce { Replacement: null } removed when expected.Access.Value?.Acl is WindowsDacl { Entries.IsEmpty: true } =>
            [$"Remove entry {removed.Occurrence + 1} · {WindowsAccessEditor.Describe(removed.Expected)}", "The resulting empty DACL grants no discretionary access."],
        ChangeWindowsAce { Replacement: null } removed => [$"Remove entry {removed.Occurrence + 1} · {WindowsAccessEditor.Describe(removed.Expected)}"],
        ChangeWindowsAce edited => [$"Entry {edited.Occurrence + 1} · {WindowsAccessEditor.Describe(edited.Expected)} → {WindowsAccessEditor.Describe(edited.Replacement!)}"],
        AddWindowsAce added => [$"Add explicit {WindowsAccessEditor.Describe(added.Entry)}", "The new entry is placed at the end of its explicit allow or deny block."],
        ChangeWindowsAuditAce { Occurrence: int index, Replacement: null } removed =>
            [$"Remove audit entry {index + 1} · {WindowsAuditEditor.Describe(removed.Expected)}"],
        ChangeWindowsAuditAce { Occurrence: int index } edited =>
            [$"Audit entry {index + 1} · {WindowsAuditEditor.Describe(edited.Expected)} → {WindowsAuditEditor.Describe(edited.Replacement)}"],
        ChangeWindowsAuditAce added => [$"Add audit entry · {WindowsAuditEditor.Describe(added.Replacement)}",
            "The new entry is placed after the explicit audit entries, before inherited, label and policy entries."],
        _ => []
    };

    private static string[] DescribeWindowsInheritance(PermissionSnapshot original, PermissionSnapshot expected, WindowsInheritanceAction action)
    {
        var before = (WindowsDacl)original.Access.Value!.Value.Acl;
        var after = (WindowsDacl)expected.Access.Value!.Value.Acl;
        var rows = new List<string> { action == WindowsInheritanceAction.Enable ? "Enable DACL inheritance from the reviewed parent" : "Protect the DACL" };
        for (int index = 0; index < before.Entries.Length; index++)
        {
            var entry = before.Entries[index];
            if ((entry.Flags & 0x10) == 0) continue;
            rows.Add(action == WindowsInheritanceAction.ProtectCopy
                ? $"Rule {index + 1} becomes explicit · {PermissionInspectorView.WindowsRule(after.Entries[index])}"
                : $"Remove inherited rule {index + 1} · {PermissionInspectorView.WindowsRule(entry)}");
        }
        if (action == WindowsInheritanceAction.Enable)
            for (int index = 0; index < after.Entries.Length; index++)
                if ((after.Entries[index].Flags & 0x10) != 0)
                    rows.Add($"Inherited from reviewed parent · {PermissionInspectorView.WindowsRule(after.Entries[index])}");
        if (after.Entries.IsEmpty) rows.Add("The resulting empty DACL grants no discretionary access.");
        return rows.ToArray();
    }

    private static string[] DescribeProtectionDiff(PermissionSnapshot original, PermissionSnapshot expected)
    {
        ulong before = original.Flags.Value!.Value.Bits, after = expected.Flags.Value!.Value.Bits;
        bool darwin = original.Model == PermissionModel.DarwinAcl;
        uint locked = darwin ? DarwinPermissionService.UserLocked : LinuxPermissionService.ImmutableInode;
        uint append = darwin ? DarwinPermissionService.UserAppendOnly : LinuxPermissionService.AppendOnlyInode;
        string name = darwin ? "Finder Locked" : "immutable protection";
        var rows = new List<string>();
        if (((before & ~after) & locked) != 0) rows.Add($"Clear {name}");
        if (((before & ~after) & append) != 0) rows.Add("Clear append-only protection");
        if (darwin) rows.AddRange(DescribePermissionDiff(original.Mode.Value!.Value, expected.Mode.Value!.Value, [], []));
        if (((after & ~before) & locked) != 0) rows.Add($"Set {name}");
        if (((after & ~before) & append) != 0) rows.Add("Set append-only protection");
        return rows.ToArray();
    }

    private static string[] DescribeDarwinAclEdit(PermissionSnapshot original, PermissionSnapshot expected, DarwinAclDelta change)
    {
        bool directory = original.Binding!.Identity.Kind is PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot;
        string Describe(DarwinAce entry) => PermissionInspectorView.DarwinRule(entry, directory);
        var after = expected.Access.Value?.Acl as DarwinAcl;
        return change switch
        {
            AddDarwinAce insert => [$"Insert entry {insert.Index + 1} · {Describe(after!.Entries[insert.Index])}"],
            ChangeDarwinAce { Replacement: null } last when after is { Entries.IsEmpty: true } =>
                [$"Remove entry {last.Index + 1} · {Describe(last.Expected)}", "The extended ACL will have zero entries and retain its ACL flags."],
            ChangeDarwinAce { Replacement: null } remove => [$"Remove entry {remove.Index + 1} · {Describe(remove.Expected)}"],
            ChangeDarwinAce edit => [$"Entry {edit.Index + 1} · {Describe(edit.Expected)} → {Describe(after!.Entries[edit.Index])}"],
            RemoveDarwinAcl => ["Remove the entire extended ACL. BSD mode continues to apply."],
            _ => []
        };
    }

    private static string[] DescribePermissionPlan(PermissionSnapshot original, PermissionChangePlan plan)
    {
        var rows = new List<string>();
        var before = original;
        bool assignedGroup = false;
        bool changesGroup = plan.Stages.Any(stage => stage.Change is AssignPermissionPrincipal);
        foreach (var step in plan.Stages)
        {
            var after = step.Expected;
            if (step.Change is ChangePermissionFlags flags)
            {
                rows.AddRange(DescribeProtectionDiff(before, after));
                if (plan.Stages.Length > 1 && flags.Bits == 0)
                    rows.Add("Clearing protection first can temporarily allow writes or removal under the current permissions.");
                before = after;
                continue;
            }
            if (step.Change is DarwinAclDelta acl)
            {
                rows.AddRange(DescribeDarwinAclEdit(before, after, acl));
                before = after;
                continue;
            }
            if (step.Change is ChangePosixAcl posix)
            {
                rows.AddRange(DescribePermissionDiff(before, after, posix.IsDefault));
                before = after;
                continue;
            }
            if (step.Change is AssignPermissionPrincipal)
            {
                rows.Add($"Change group ID {before.Group.Value?.Id} → {after.Group.Value?.Id}");
                if (before.FileCapabilities?.Status == PermissionReadStatus.Present && after.FileCapabilities?.Status == PermissionReadStatus.Absent)
                    rows.Add("The group assignment clears the file's execution capabilities.");
                rows.AddRange(DescribeMode(before, after));
                assignedGroup = true;
            }
            else
            {
                string phase = changesGroup ? assignedGroup ? "After changing group" : "Before changing group" :
                    original.Model == PermissionModel.DarwinAcl ? "BSD mode" : "Mode";
                rows.AddRange(DescribeMode(before, after).Select(row => $"{phase} · {row}"));
            }
            if (original.Model == PermissionModel.DarwinAcl)
            {
                int cleared = before.Mode.Value!.Value & ~after.Mode.Value!.Value;
                if ((cleared & 0x800) != 0) rows.Add("Clear set-user-ID");
                if ((cleared & 0x400) != 0) rows.Add("Clear set-group-ID");
            }
            before = after;
        }
        if (plan.NeedsSetIdConsent && (plan.Expected.Mode.Value.GetValueOrDefault() & 0xc00) != 0)
            rows.Add("Review the set-ID bits under the resulting owner and group.");
        return rows.ToArray();

        string[] DescribeMode(PermissionSnapshot beforeState, PermissionSnapshot afterState) =>
            original.Model == PermissionModel.DarwinAcl
                ? DescribePermissionDiff(beforeState.Mode.Value!.Value, afterState.Mode.Value!.Value, [], [])
                : DescribePermissionDiff(beforeState, afterState, defaults: false);
    }

    private static string[] DescribePermissionDiff(PermissionSnapshot before, PermissionSnapshot after, bool defaults)
        => DescribePermissionDiff(before.Mode.Value!.Value, after.Mode.Value!.Value,
            defaults ? PosixAccessDraft.DefaultRows(before) : LinuxPermissionAcl.AccessRows(before),
            defaults ? PosixAccessDraft.DefaultRows(after) : LinuxPermissionAcl.AccessRows(after));

    private static string[] DescribePermissionDiff(ushort oldMode, ushort newMode,
        ImmutableArray<PosixAclEntry> oldRows, ImmutableArray<PosixAclEntry> newRows)
    {
        var lines = new List<string>();
        if (oldMode != newMode)
            lines.Add($"Mode {UnixPermissions.Octal((UnixFileMode)oldMode)} → {UnixPermissions.Octal((UnixFileMode)newMode)}");
        byte oldMask = PosixAccessDraft.Mask(oldRows), newMask = PosixAccessDraft.Mask(newRows);
        var previous = oldRows.ToDictionary(row => (row.Tag, row.Principal));
        foreach (var row in newRows)
        {
            string label = PosixAccessEditor.PrincipalLabel(row);
            string updated = PosixAccessEditor.DescribeRights(row, newMask);
            if (!previous.Remove((row.Tag, row.Principal), out var prior)) lines.Add($"Add {label} · {updated}");
            else if (row.Rights != prior.Rights || PosixAccessDraft.EffectiveRights(row, newMask) != PosixAccessDraft.EffectiveRights(prior, oldMask))
                lines.Add($"{label} · {PosixAccessEditor.DescribeRights(prior, oldMask)} → {updated}");
        }
        foreach (var row in previous.Values)
            lines.Add($"Remove {PosixAccessEditor.PrincipalLabel(row)} · {PosixAccessEditor.DescribeRights(row, oldMask)}");
        return lines.ToArray();
    }
}
