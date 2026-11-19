using System.Security.Principal;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Recovery;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    internal bool CanRequestAdministratorTask => OperatingSystem.IsWindows() && !Services.Shell.IsElevated;
    internal static bool AdministratorAccessDenied(Exception error) => error switch
    {
        System.ComponentModel.Win32Exception native => native.NativeErrorCode == 5,
        System.Security.SecurityException => true,
        _ => ErrorText.Classify(error) == "access",
    };

    internal async Task<bool> RequestAdministratorTaskAsync(AdministratorTask task, string title, string description,
        IDialogService? dialogs = null)
    {
        if (!CanRequestAdministratorTask) return false;
        if (!await (dialogs ?? Dialogs).ConfirmAsync(title, description +
            "\n\nWindows will ask for approval to open a separate FileCat window with administrator rights.",
            "Open as administrator", danger: true)) return false;
        return await OpenAdministratorWindowAsync(task: task);
    }

    internal async Task ReviewAdministratorTaskAsync(AdministratorTask task, (string Path, string Hash)? exchange = null)
    {
        if (!OperatingSystem.IsWindows() || !Services.Shell.IsElevated || Workspace.ActivePanel is not { } panel) return;
        try
        {
            switch (task.Kind)
            {
                case AdministratorTaskKind.View:
                    var file = ItemRef.ForFileSystemPath(task.Paths[0], EntryKind.File);
                    await ViewItemAsync(file, file.Name, task.Alternate, panel.OpenTab(file.Parent, file.Name));
                    break;
                case AdministratorTaskKind.Checksums:
                    await OperationDialogs.ShowChecksumsAsync(this, task.Paths, task.Detail, task.Context);
                    break;
                case AdministratorTaskKind.Search:
                    FindWindow.OpenAdministratorRetry(this, task.Search!);
                    break;
                case AdministratorTaskKind.Registry:
                    var key = new Location(Schemes.Registry, task.Detail!, session: task.Context);
                    panel.OpenTab(key);
                    break;
                case AdministratorTaskKind.RegistryExport:
                    await ReviewAdministratorRegistryExportAsync(task);
                    break;
                case AdministratorTaskKind.RecoveryVolume:
                case AdministratorTaskKind.RecoveryDisk:
                    await ReviewAdministratorRecoveryAsync(panel, task);
                    break;
                case AdministratorTaskKind.FileRetry:
                    await ReviewAdministratorFileRetryAsync(panel, task, exchange);
                    break;
                case AdministratorTaskKind.Rename:
                case AdministratorTaskKind.CreateFile:
                case AdministratorTaskKind.Attributes:
                    await ReviewAdministratorMutationAsync(panel, task);
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                                   NotSupportedException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            await Dialogs.AlertAsync("Administrator request", Formatters.SafeName(ex.Message));
        }
    }

    private async Task ReviewAdministratorFileRetryAsync(PanelViewModel panel, AdministratorTask task, (string Path, string Hash)? exchange)
    {
        var retry = task.FileRetry!;
        var options = retry.Options();
        var keyPoints = new List<string> { "Only the displayed portion of the original operation will be retried." };
        if (retry.Kind is JobKind.Copy or JobKind.Move)
        {
            string conflict = options.Conflicts switch
            {
                ConflictPolicy.Skip => "Skip existing items",
                ConflictPolicy.Replace => "Replace when the operation supports it",
                ConflictPolicy.ReplaceIfNewer => "Replace only when the source is newer",
                ConflictPolicy.KeepBothRenameIncoming => "Keep both by renaming the incoming item",
                ConflictPolicy.KeepBothRenameExisting => "Keep both by renaming the existing item",
                _ => "Ask about conflicts",
            };
            keyPoints.Add(conflict + ".");
            if (options.Filter is { } filter) keyPoints.Add($"Original filter: {Formatters.SafeName(filter.ResolvedDescription)}.");
            if (options.Verify == VerifyMode.ReadBack) keyPoints.Add("Verify copied content by reading it back.");
        }
        var review = string.Join('\n', retry.ReviewText.Split('\n').Select(Formatters.SafeName));
        bool approved = await Dialogs.ConfirmReviewAsync(retry.Title, review, keyPoints, "Retry as administrator", danger: true);
        if (exchange is { } requestExchange)
            await Task.Run(() => AdministratorFileRetryReview.Report(requestExchange, approved));
        if (!approved) return;
        var request = await Task.Run(() =>
        {
            AdministratorWindow.VerifyVolumes(task.Paths, task.Volumes);
            return retry.ForExecution(Services.Platform.FileOperations);
        });
        var job = Services.Jobs.Submit(request);
        if (panel.ActiveTab is { } tab) Track(job, tab);
        Operations.IsOpen = true;
    }

    internal static Location AdministratorRegistryKey(Location original)
    {
        string key = original.Path;
        if (key.Equals("HKCU", StringComparison.OrdinalIgnoreCase) || key.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase))
        {
            using var identity = WindowsIdentity.GetCurrent();
            string sid = identity.User?.Value ?? throw new IOException("The original Registry user could not be identified.");
            key = @"HKU\" + sid + key[4..];
        }
        return original.WithPath(key);
    }

    internal Task<bool> RequestAdministratorRegistryAsync(Location original)
    {
        var key = AdministratorRegistryKey(original);
        if (!AdministratorTask.IsRegistryKey(key.Path))
        {
            Notify("Choose an explicit HKLM or HKU key for administrator Registry access.", true);
            return Task.FromResult(false);
        }
        return RequestAdministratorTaskAsync(new(AdministratorTaskKind.Registry)
        {
            Detail = key.Path, Context = key.Session,
        }, "Reopen Registry key as administrator",
            $"Open {Formatters.SafeName(Services.Providers.Display(key))} in its selected Registry view.");
    }

    internal static AdministratorRegistryExport BindAdministratorRegistryExport(Location key, string? value)
    {
        var bound = AdministratorRegistryKey(key);
        return new(bound.Path, value, !string.Equals(bound.Path, key.Path, StringComparison.Ordinal));
    }

    private async Task RequestAdministratorRegistryExportAsync(IReadOnlyList<(Location Key, string? Value)> selection, string suggested)
    {
        var entries = selection.Select(item => BindAdministratorRegistryExport(item.Key, item.Value)).ToArray();
        if (entries.Any(entry => !AdministratorTask.IsRegistryKey(entry.Key)))
        {
            Notify("Choose explicit Registry keys for administrator export.", true);
            return;
        }
        await RequestAdministratorTaskAsync(new(AdministratorTaskKind.RegistryExport)
        {
            Detail = suggested, Context = selection[0].Key.Session, RegistryExport = entries,
        }, "Export Registry as administrator",
            $"Review {Formatters.Plural(entries.Length, "selected Registry item", "selected Registry items")} in the selected view, then choose where to save the export.");
    }

    private async Task ReviewAdministratorRegistryExportAsync(AdministratorTask task)
    {
        var items = task.RegistryExport!.Select(entry => entry.ForView(task.Context)).ToArray();
        string review = string.Join('\n', items.Select(item => Formatters.SafeName(Services.Providers.Display(item.Key)) +
            (item.Value is { } value ? $" · {Formatters.SafeName(value.Length == 0 ? "(Default)" : value)}" : " · key and subtree")));
        if (!await Dialogs.ConfirmReviewAsync("Review Registry export as administrator", review,
            ["Choose a .reg destination after reviewing the selected keys and values."], "Choose destination")) return;
        var path = await ChooseRegistryExportPathAsync(task.Detail!);
        if (path is null || !await ConfirmRegistryExportDestinationAsync(path)) return;
        Notify("Exporting Registry selection…");
        await Task.Run(() => RegistryInterchange.ExportSelections(items, path));
        Notify($"Exported to {path}. Import must use the intended Registry view explicitly.");
        RefreshTabsShowing(Path.GetDirectoryName(path)!);
    }

    private async Task ReviewAdministratorMutationAsync(PanelViewModel panel, AdministratorTask task)
    {
        string title;
        string[] summary;
        if (task.Kind == AdministratorTaskKind.Rename)
        {
            title = "Review bulk rename as administrator";
            summary = task.Paths.Zip(task.Names, (path, name) => $"{Formatters.SafeName(path)} → {Formatters.SafeName(name)}").ToArray();
        }
        else if (task.Kind == AdministratorTaskKind.CreateFile)
        {
            title = "Create an empty file as administrator";
            summary = [Formatters.SafeName(Path.Combine(task.Paths[0], task.Names[0]))];
        }
        else
        {
            title = "Review attribute and timestamp changes as administrator";
            var change = task.Attributes!;
            summary = [
                $"Set {change.Set}. Clear {change.Clear}.",
                .. change.CreatedUtc is { } created ? new[] { $"Creation time {created.ToLocalTime():G}" } : [],
                .. change.ModifiedUtc is { } modified ? new[] { $"Modified time {modified.ToLocalTime():G}" } : [],
                change.Recursive ? "Include contents of the selected folders." : "Apply to the selected items.",
                .. task.Paths.Select(Formatters.SafeName),
            ];
        }
        if (!await Dialogs.ConfirmReviewAsync(title, string.Join('\n', summary),
            [$"Apply this request to {Formatters.Plural(task.Paths.Length, "selected path", "selected paths")}."],
            "Apply as administrator", danger: true)) return;

        var jobRequest = await Task.Run(() => PrepareAdministratorMutation(task));
        var job = Services.Jobs.Submit(jobRequest);
        if (panel.ActiveTab is { } tab) Track(job, tab);
        Operations.IsOpen = true;
    }

    internal static JobRequest PrepareAdministratorMutation(AdministratorTask task)
    {
        // Administrative writes accept ordinary local paths. Refuse a reparse point at any level of a reviewed path.
        foreach (string path in task.Paths)
        {
            string? component = Path.TrimEndingDirectorySeparator(path);
            while (component is not null)
            {
                if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Choose the actual location of {path}. An administrator write cannot go through a link or mount point.");
                component = Path.GetDirectoryName(component);
            }
        }
        if (task.Kind == AdministratorTaskKind.CreateFile)
            return new() { Kind = JobKind.CreateFile, Destination = Location.FileSystem(task.Paths[0]), NewName = task.Names[0] };
        var sources = task.Paths.Select(path => ItemRef.ForFileSystemPath(path,
            (File.GetAttributes(path) & FileAttributes.Directory) != 0 ? EntryKind.Directory : EntryKind.File)).ToArray();
        return task.Kind == AdministratorTaskKind.Rename
            ? new() { Kind = JobKind.Rename, Sources = sources, NewNames = task.Names }
            : new() { Kind = JobKind.Attributes, Sources = sources, Attributes = task.Attributes };
    }

    private async Task ReviewAdministratorRecoveryAsync(PanelViewModel panel, AdministratorTask task)
    {
        if (!await Dialogs.ConfirmAsync("Review recovery source",
            "Close the original FileCat window and any other FileCat instances before continuing. The scan will check the disk, this window's data folders, and other running processes before reading.",
            "Check and review scan", danger: true)) return;
        if (task.Kind == AdministratorTaskKind.RecoveryVolume)
        {
            var drive = (await RecoverableDrivesAsync()).FirstOrDefault(candidate =>
                string.Equals(candidate.RootPath, task.Paths[0], StringComparison.OrdinalIgnoreCase));
            if (drive is null) throw new IOException("The selected volume is no longer available.");
            await FindDeletedOnDriveAsync(panel, drive, task.Context, expectedIdentity: task.Detail, expectedDevice: task.Device);
        }
        else
        {
            var disk = (await Task.Run(DeviceTopology.Disks)).FirstOrDefault(candidate =>
                string.Equals(candidate.InterfacePath, task.Device, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(SelectedDiskInterfaceSource.WholeDiskSelectionIdentity(candidate), task.Detail, StringComparison.Ordinal));
            if (disk is null) throw new IOException("The selected disk is no longer available.");
            await FindDeletedOnDiskAsync(panel, disk);
        }
    }
}
