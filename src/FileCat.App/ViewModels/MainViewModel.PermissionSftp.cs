using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Core.Threading;
using FileCat.Remote.Sftp;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task ShowSftpPermissionEditorAsync(IDialogService dialogs, InspectedPermission item, Func<string, Task>? copy,
        PermissionSelection? origin)
    {
        var source = item.Source!;
        string device = Services.SftpProvider.GetDeviceKey(source.Parent);
        try
        {
            using var preparation = origin is null ? null : new PreparationScope(origin.Tab, Services.Io);
            if (origin is { IsCurrent: false }) throw new OperationCanceledException();
            using var edit = await Services.Io.RunOwned(device, IoPriority.Interactive,
                ct => Services.SftpProvider.BeginSftpPermissionEdit(item.Snapshot, source, ct), preparation?.Stop.Token ?? default);
            preparation?.Check();
            if (origin is { IsCurrent: false } || Services.Io.IsStopped) throw new OperationCanceledException();
            preparation?.Detach();
            await ShowBoundPermissionEditorAsync(dialogs, item, copy, "sftp", origin, edit);
        }
        catch (OperationCanceledException)
        {
            if (!Services.Io.IsStopped) Notify("The selection changed while opening SFTP permissions.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (!Services.Io.IsStopped) await dialogs.AlertAsync("SFTP permissions unavailable", Formatters.SafeName(error.Message));
        }
    }

    private static string[] DescribeSftpPlan(SftpPermissionEdit edit, SftpPermissionPlan plan)
    {
        var rows = new List<string>();
        foreach (var stage in plan.Stages)
        {
            if (stage.Before == stage.After) continue;
            if (stage.Change is AssignPermissionPrincipal)
            {
                uint owner = stage.Before.UserId!.Value;
                uint paired = edit.PairedUser(owner);
                rows.Add($"Change server group ID {stage.Before.GroupId} → {stage.After.GroupId}");
                rows.Add(paired == uint.MaxValue ? $"Paired UID · {paired} (keep server owner ID {owner})"
                    : $"Paired UID · {paired} (current server owner ID)");
                rows.Add("A group change may clear server-side file execution capabilities that SFTP cannot show.");
            }
            else
            {
                string before = UnixPermissions.Octal((UnixFileMode)(stage.Before.Permissions!.Value & 0xfff));
                string after = UnixPermissions.Octal((UnixFileMode)(stage.After.Permissions!.Value & 0xfff));
                rows.Add($"Mode {before} → {after}");
            }
        }
        if (plan.NeedsSetIdConsent)
            rows.Add("The server may clear set-ID bits. Review the mode under the resulting owner and group before applying.");
        return rows.ToArray();
    }
}
