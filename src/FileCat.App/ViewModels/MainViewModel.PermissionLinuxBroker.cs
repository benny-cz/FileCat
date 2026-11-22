using Avalonia.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Recovery.Unix;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool CanInspectWithLinuxAuthorization(InspectedPermission item) =>
        Services.LinuxBroker is not null && Services.Permissions is LinuxPermissionService &&
        item.Snapshot.Binding is { FileSystem: "ext4", Identity.Kind: PermissionObjectKind.File or PermissionObjectKind.Directory } &&
        item.Source?.FileSystemPath == item.Snapshot.Subject.RawLocator;

    private async Task ShowAuthorizedLinuxPermissionsAsync(IDialogService dialogs, InspectedPermission item,
        Func<string, Task>? copy, PermissionSelection? origin)
    {
        if (!CanInspectWithLinuxAuthorization(item) || Services.Io.IsStopped || origin is { IsCurrent: false }) return;
        var broker = Services.LinuxBroker!;
        using var cancellation = new CancellationTokenSource();
        var closer = new DialogCloser();
        var waiting = ShowAuthorizationWaitAsync(dialogs, Formatters.SafeName(item.Snapshot.Subject.DisplayPath),
            "The system asks for administrator approval to read this item's security metadata.", closer);

        // An administrator may take time to answer. Keep this off the filesystem I/O queue.
        var inspect = Task.Run(() => LinuxAuthorizedPermissionEdit.Inspect(broker, item.Inspection, cancellation.Token));
        try
        {
            if (await Task.WhenAny(waiting, inspect) == waiting) cancellation.Cancel();
            else closer.Close("done");
            await waiting;
            using var authorized = await inspect;
            if (cancellation.IsCancellationRequested || Services.Io.IsStopped || origin is { IsCurrent: false }) return;
            var view = new PermissionInspectorView([(item.Name, authorized.Snapshot)], copy,
                principalNames: Services.PermissionPrincipalNames);
            var buttons = new List<DialogButton> { new("Close", "close", IsDefault: true, IsCancel: true) };
            foreach (var (command, label) in new (string, string)[]
            {
                ("mode", "Edit mode…"), ("acl", "Edit access ACL…"), ("defaults", "Edit default ACL…"),
                ("owner", "Change owner…"), ("group", "Change group…"), ("protection", "Edit file protection…")
            })
                if (authorized.CanEdit(command)) buttons.Insert(0, new(label, command));
            var choice = await dialogs.ShowCustomAsync("Authorized permissions and ownership", view, buttons, resizable: true,
                behavior: new DialogBehavior { DismissOnBackdrop = false, PreferredSize = new Avalonia.Size(740, 550) });
            if (choice is string commandChoice && authorized.CanEdit(commandChoice) && !Services.Io.IsStopped &&
                origin is not { IsCurrent: false })
                await ShowBoundPermissionEditorAsync(dialogs, item, copy, commandChoice, origin, authorizedLinux: authorized);
        }
        catch (OperationCanceledException) { }
        catch (DBusException refusal) when (refusal.Name == "System.Error.ECANCELED") { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or System.Net.Sockets.SocketException)
        {
            if (!Services.Io.IsStopped && !cancellation.IsCancellationRequested)
                await dialogs.AlertAsync("Authorized inspection unavailable",
                    Formatters.SafeName(error is DBusException refusal ? refusal.PermissionsRefusal : error.Message));
        }
    }

    private static string[] DescribeAuthorizedLinuxChange(PermissionSnapshot before, PermissionSnapshot after, PermissionDelta change)
    {
        if (change is ChangePermissionFlags) return DescribeProtectionDiff(before, after);
        var diff = DescribePermissionDiff(before, after, change is ChangePosixAcl { IsDefault: true });
        if (change is not AssignPermissionPrincipal principal) return diff;
        var oldPrincipal = principal.IsOwner ? before.Owner.Value : before.Group.Value;
        var lines = new List<string>
        {
            $"{(principal.IsOwner ? "Owner UID" : "Group GID")} {oldPrincipal?.Id} → {principal.Principal.Id}"
        };
        lines.AddRange(diff);
        if (before.FileCapabilities?.Status == PermissionReadStatus.Present && after.FileCapabilities?.Status == PermissionReadStatus.Absent)
            lines.Add("Clear file capabilities");
        return lines.ToArray();
    }
}
