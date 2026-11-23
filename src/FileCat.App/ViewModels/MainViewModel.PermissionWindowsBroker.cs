using System.ComponentModel;
using Avalonia.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private nint WindowsPermissionOwner() =>
        View.TopLevel?.TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: not 0 } handle ? handle.Handle : 0;

    // The installed x64 FileCat's administrator helper (G-PRIV, G-PKG): present, in Program Files, administrator-protected.
    // Asked once: the availability checks run while the inspector is open, and each request verifies the helper again.
    private static readonly Lazy<bool> InstalledWindowsHelper = new(() => ElevationBroker.Locate(portable: false, out _) is not null);

    private bool CanInspectWithWindowsAuthorization(InspectedPermission item) =>
        Services.Permissions is WindowsPermissionService && !Services.Paths.IsPortable &&
        ElevationBroker.IsNativeFileSecurityHost && InstalledWindowsHelper.Value && WindowsPermissionOwner() != 0 &&
        item.Source?.FileSystemPath == item.Snapshot.Subject.RawLocator &&
        WindowsPermissionService.CanBindAuthorizedInspection(item.Snapshot);

    private async Task ShowAuthorizedWindowsPermissionsAsync(IDialogService dialogs, InspectedPermission item,
        Func<string, Task>? copy, PermissionSelection? origin)
    {
        if (!CanInspectWithWindowsAuthorization(item) || Services.Io.IsStopped || origin is { IsCurrent: false }) return;
        nint owner = WindowsPermissionOwner();
        using var cancellation = new CancellationTokenSource();
        var closer = new DialogCloser();
        var waiting = ShowAuthorizationWaitAsync(dialogs, Formatters.SafeName(item.Snapshot.Subject.DisplayPath),
            "Windows asks for administrator approval to read this item's security metadata.", closer);
        var inspect = Task.Run(() => WindowsPermissionService.InspectWithAuthorizationAsync(item.Inspection,
            WindowsPermissionService.DiscretionarySections | PermissionSections.Audit, Services.Paths.IsPortable,
            Services.Paths.ElevationExchangeDirectory, owner, cancellation.Token));
        try
        {
            if (await Task.WhenAny(waiting, inspect) == waiting) cancellation.Cancel();
            else closer.Close("done");
            await waiting;
            using var observation = await inspect;
            if (cancellation.IsCancellationRequested || Services.Io.IsStopped || origin is { IsCurrent: false } ||
                owner != WindowsPermissionOwner()) return;

            var snapshot = observation.Snapshot;
            bool volumeRoot = Services.Permissions is WindowsPermissionService windows && windows.CanReviewVolumeRoot(snapshot);
            string? refusal = WindowsPermissionService.EditRefusal(snapshot);
            var actions = new List<PermissionInspectorAction>();
            if (refusal is null)
            {
                actions.Add(new("Change owner…", "owner", PermissionInspectorArea.Ownership, _ => true));
                if (snapshot.Access.Value?.Acl is WindowsDacl { Form: WindowsDaclForm.Entries, Revision: 2 or 4 })
                    actions.Add(new("Edit access ACL…", "acl", PermissionInspectorArea.Access, _ => true));
                if (snapshot.Audit.Value?.Acl is WindowsSacl { Revision: null or 2 or 4 })
                    actions.Add(new("Edit audit rules…", "audit", PermissionInspectorArea.Auditing, _ => true));
            }
            if (volumeRoot) actions.Add(new("Edit volume root access rules…", "acl", PermissionInspectorArea.Access, _ => true));
            var inspectorCloser = new DialogCloser();
            var view = new PermissionInspectorView([(item.Name, snapshot)], copy, [volumeRoot ? null : refusal], Services.PermissionPrincipalNames,
                actions, inspectorCloser);
            var choice = await dialogs.ShowCustomAsync("Authorized permissions and ownership", view, [new("Close", "close", IsDefault: true, IsCancel: true)],
                closer: inspectorCloser, resizable: true,
                behavior: new DialogBehavior { DismissOnBackdrop = false, PreferredSize = new Avalonia.Size(860, 640), SizeKey = "permissions.authorized" });
            if (choice is string command && command is "owner" or "acl" or "audit" && actions.Any(action => action.Result == command) &&
                !Services.Io.IsStopped && owner == WindowsPermissionOwner() && origin is not { IsCurrent: false })
                await ShowBoundPermissionEditorAsync(dialogs, item with { Inspection = observation }, copy, command, origin,
                    windowsAuthorizationOwner: owner);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or
            InvalidDataException or Win32Exception or TimeoutException)
        {
            if (!Services.Io.IsStopped && !cancellation.IsCancellationRequested)
                await dialogs.AlertAsync("Authorized inspection unavailable", Formatters.SafeName(error.Message));
        }
    }
}
