using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.ViewModels;

/// <summary>
/// ZIP creation and updates (plan §15, P5). Every change is one job that rebuilds the archive beside itself and
/// replaces it only when complete, verified, and still the version the user saw; the dialogs say that a change
/// rewrites the whole archive.
/// </summary>
public sealed partial class MainViewModel
{
    private const int MaxArchiveBatch = 100_000;

    /// <summary>The Shell's Send to compressed-folder handler can outlive our isolated context host.</summary>
    internal void PackFromWindowsSendTo(IReadOnlyList<string?> paths, TabViewModel origin)
    {
        if (!OperatingSystem.IsWindows() || !WindowsContextMenu.CanShow(paths) || Services.Io.IsStopped) return;
        var items = new List<ItemRef>(paths.Count);
        try
        {
            foreach (string path in paths.Select(p => p!))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0 && ReparseLinks.IsLink(path))
                {
                    Notify("Linked selections are not packed. Select the link's target explicitly.", true);
                    return;
                }
                items.Add(ItemRef.ForFileSystemPath(path,
                    (attributes & FileAttributes.Directory) != 0 ? EntryKind.Directory : EntryKind.File));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify("Cannot pack this selection: " + ex.Message, true);
            return;
        }
        string folder = Path.GetDirectoryName(paths[0]!)!;
        string stem = items.Count == 1 && items[0].IsContainer ? items[0].Name : Path.GetFileNameWithoutExtension(items[0].Name);
        if (stem.Length == 0) stem = "archive";
        string archive = Path.Combine(folder, stem + ".zip");
        for (int suffix = 2; File.Exists(archive) || Directory.Exists(archive); suffix++)
        {
            if (suffix > 10_000) { Notify("No available ZIP name was found in this folder.", true); return; }
            archive = Path.Combine(folder, stem + " (" + suffix + ").zip");
        }
        var changes = items.Select(i => new ArchiveChange(i.IsContainer ? ArchiveChangeKind.AddFolder : ArchiveChangeKind.AddFile,
            i.Name, i.FileSystemPath)).ToList();
        SubmitArchive(new ArchivePlan(archive, null, changes, CompressionLevel.Optimal), items, Location.FileSystem(folder),
            $"Pack {Formatters.Plural(items.Count, "item", "items")} into \"{Path.GetFileName(archive)}\"",
            Path.GetFileName(archive), origin);
    }

    private static string ArchiveFile(Location zip) => zip.Container?.Path ?? throw new InvalidOperationException("Not an archive location.");

    private static string MemberPath(ItemRef item) => item.Parent.Path.Length == 0 ? item.Name : item.Parent.Path + "/" + item.Name;

    private static string InArchive(Location folder, string name) => folder.Path.Length == 0 ? name : folder.Path + "/" + name;

    private bool CheckWritable(Location zip)
    {
        if (Services.Providers.For(zip) is ZipProvider provider && provider.WhyReadOnly(zip) is { } reason)
        {
            Notify(reason, true);
            return false;
        }
        return true;
    }

    private void SubmitArchive(ArchivePlan plan, IReadOnlyList<ItemRef> sources, Location? refreshed, string description, string? focus = null, TabViewModel? origin = null,
        QueueMode mode = QueueMode.Start, Location? originLocation = null)
    {
        if (Services.Io.IsStopped) return;
        var tab = origin ?? ActiveTab;
        var context = originLocation ?? tab?.Location;
        Services.Jobs.SubmitWithRegistration(new JobRequest
        {
            Kind = JobKind.ArchiveUpdate,
            Archive = plan,
            Sources = sources.ToList(),
            Destination = refreshed,
            Description = description,
            Mode = mode,
        }, job =>
        {
            if (tab is not null && context is not null) _jobOrigins[job] = (tab, context);
            if (focus is not null) _focusAfter[job] = focus;
        });
    }

    /// <summary>F5 into an archive folder: files and folders from disk are added; existing names ask once.</summary>
    private async Task AddToArchiveAsync(JobKind kind, IReadOnlyList<ItemRef> items, Location destination, TransferOptions options)
        => await AddToArchiveTransferAsync(kind, items, destination, options, null, QueueMode.Start, null);

    private async Task AddToArchiveTransferAsync(JobKind kind, IReadOnlyList<ItemRef> items, Location destination, TransferOptions options,
        string? newName, QueueMode mode, TabViewModel? sourceTab, Location? sourceContext = null)
    {
        if (items.Count == 0) return;
        if (newName is not null && (items.Count != 1 || ArchivePaths.Problem(newName) is not null))
        { Notify("Choose one valid member name for a single item in the archive.", true); return; }
        if (items.Any(i => i.FileSystemPath is null))
        {
            Notify("Only files and folders from disk can be added to an archive. Extract items from other archives first.", true);
            return;
        }
        if (options.Filter is not null)
        {
            // Adding would take every file of the folders, not only the matching ones.
            Notify("\"Only files matching\" is not available when adding to an archive; nothing was added. Mark the files to add instead.", true);
            return;
        }
        if (items.Count > MaxArchiveBatch) { Notify($"Add at most {MaxArchiveBatch:N0} items at a time.", true); return; }
        if (!CheckWritable(destination)) return;
        string archive = ArchiveFile(destination);
        var names = items.Select(i => InArchive(destination, newName ?? i.Name)).ToList();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
        {
            Notify("Two selected items have the same name; an archive folder holds each name once.", true);
            return;
        }
        var origin = sourceTab ?? ActiveTab;
        var prepared = await PrepareArchiveAsync(destination, names: true);
        if (prepared is null) return;
        var baseline = prepared.Baseline; var existing = prepared.Names;
        bool replace = options.Conflicts == ConflictPolicy.Replace;
        int clashes = names.Count(existing.Contains);
        if (clashes > 0 && options.Conflicts is ConflictPolicy.Ask or ConflictPolicy.ReplaceIfNewer or ConflictPolicy.KeepBothRenameExisting or ConflictPolicy.KeepBothRenameIncoming)
        {
            var answer = await Dialogs.ShowCustomAsync("Names already in the archive",
                new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 620,
                    Text = $"{Formatters.Plural(clashes, "item has a name that already exists", "items have names that already exist")} in {Path.GetFileName(archive)}. Replace the members with the new files, or keep the members and skip those files?",
                },
                [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Skip existing", "skip", IsDefault: true), new DialogButton("Replace", "replace", IsDanger: true)]);
            if (answer is not ("skip" or "replace")) return;
            replace = answer as string == "replace";
        }
        string cost = prepared.Cost;
        string verb = kind == JobKind.Move ? "Move" : "Add";
        if (cost.Length > 0 && !await Dialogs.ConfirmAsync("Update archive", $"{verb} {Formatters.Plural(items.Count, "item", "items")} to {Path.GetFileName(archive)}?{cost}", verb))
            return;
        var changes = items.Select(i => new ArchiveChange(i.IsContainer ? ArchiveChangeKind.AddFolder : ArchiveChangeKind.AddFile,
            InArchive(destination, newName ?? i.Name), i.FileSystemPath)).ToList();
        SubmitArchive(new ArchivePlan(archive, baseline, changes, ReplaceExisting: replace) { MoveSources = kind == JobKind.Move }, items, destination,
            $"{verb} {Formatters.Plural(items.Count, "item", "items")} to \"{Path.GetFileName(archive)}\"", origin: origin, mode: mode, originLocation: sourceContext);
    }

    private async Task DeleteArchiveMembersAsync(Location folder, IReadOnlyList<ItemRef> items)
    {
        if (!CheckWritable(folder)) return;
        if (items.Count > MaxArchiveBatch) { Notify($"Delete at most {MaxArchiveBatch:N0} members at a time.", true); return; }
        string archive = ArchiveFile(folder);
        var origin = ActiveTab;
        using var scope = origin is null ? null : new PreparationScope(origin, Services.Io);
        var prepared = await PrepareArchiveAsync(folder, scope: scope);
        if (prepared is null) return;
        int folders = items.Count(i => i.IsContainer), files = items.Count - folders;
        var parts = new List<string>();
        if (files > 0) parts.Add(Formatters.Plural(files, "member", "members"));
        if (folders > 0) parts.Add(Formatters.Plural(folders, "folder", "folders") + " with everything in them");
        bool duplicates = items.Any(i => i.Ordinal > 0);
        if (!await Dialogs.ConfirmAsync("Delete from archive",
                $"Delete {string.Join(" and ", parts)} from {Path.GetFileName(archive)}? This cannot be undone; archive members do not go to {RecycleText.Destination}." +
                (duplicates ? " Duplicate names are deleted one copy at a time, exactly as selected." : "") + prepared.Cost,
                "Delete", danger: true))
            return;
        var changes = items.Select(i => new ArchiveChange(ArchiveChangeKind.Delete, MemberPath(i),
            Ordinal: i.IsContainer || prepared.Copies.GetValueOrDefault(MemberPath(i)) <= 1 ? null : i.Ordinal)).ToList();
        SubmitArchive(new ArchivePlan(archive, prepared.Baseline, changes), items, folder,
            $"Delete {Formatters.Plural(items.Count, "item", "items")} from \"{Path.GetFileName(archive)}\"", origin: origin);
    }

    // External edit sessions still use the provider's catalog; their separate admission audit remains outstanding.
    private int Copies(ItemRef item) => Services.Providers.For(item.Parent) is ZipProvider zip ? zip.CopiesOf(item.Parent, item.Name) : 1;

    private async Task CreateArchiveFolderAsync(TabViewModel tab, Location folder)
    {
        if (!CheckWritable(folder)) return;
        string archive = ArchiveFile(folder);
        using var scope = new PreparationScope(tab, Services.Io);
        var prepared = await PrepareArchiveAsync(folder, scope: scope);
        if (prepared is null) return;
        var r = await Dialogs.PromptAsync(new PromptOptions("Create folder in archive", $"Folder name in {Path.GetFileName(archive)} (use / for nested folders):")
        {
            Validate = n => ArchivePaths.Problem(InArchive(folder, n.Trim('/'))),
            ConfirmText = "Create",
        });
        if (r is null) return;
        string member = InArchive(folder, r.Text.Trim('/'));
        SubmitArchive(new ArchivePlan(archive, prepared.Baseline, [new ArchiveChange(ArchiveChangeKind.CreateFolderEntry, member)]), [], folder,
            $"Create folder \"{r.Text.Trim('/')}\" in \"{Path.GetFileName(archive)}\"", r.Text.Trim('/').Split('/')[0], tab);
    }

    private async Task RenameArchiveMemberAsync(TabViewModel tab, Location folder)
    {
        if (!tab.Listing.TryGetFocused(out var row) || row.Kind == EntryKind.Parent) return;
        if (!CheckWritable(folder)) return;
        var item = tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        using var scope = new PreparationScope(tab, Services.Io);
        var prepared = await PrepareArchiveAsync(folder, scope: scope);
        if (prepared is null) return;
        if (prepared.Copies.GetValueOrDefault(MemberPath(item)) > 1)
        {
            Notify("This name appears more than once in the archive; rename is not offered for duplicated names. Delete the copy you do not want (F8).", true);
            return;
        }
        string archive = ArchiveFile(folder);
        var newName = await View.RenameInlineAsync(new PromptOptions("Rename in archive", $"New name for \"{Formatters.SafeName(item.Name)}\":")
        {
            Text = item.Name,
            SelectStem = !item.IsContainer,
            Validate = n => n == item.Name ? "The name is unchanged." : n.Contains('/') ? "Rename within the same folder." : ArchivePaths.Problem(InArchive(folder, n)),
            ConfirmText = "Rename",
        });
        if (newName is null) return;
        string cost = prepared.Cost;
        if (cost.Length > 0 && !await Dialogs.ConfirmAsync("Rename in archive", $"Rename \"{item.Name}\" to \"{newName}\"?{cost}", "Rename")) return;
        SubmitArchive(new ArchivePlan(archive, prepared.Baseline, [new ArchiveChange(ArchiveChangeKind.Rename, MemberPath(item), NewMemberPath: InArchive(folder, newName))]),
            [item], folder, $"Rename \"{item.Name}\" to \"{newName}\" in \"{Path.GetFileName(archive)}\"", newName, tab);
    }

    /// <summary>
    /// Alt+F5 (Salamander, Total Commander): pack the selection into a new ZIP, or add it to an existing one. Folders
    /// keep their structure; links are not followed; paths are relative to each item's folder.
    /// </summary>
    private async Task PackAsync()
    {
        var selected = SourceSelection();
        if (selected is null) return;
        var origin = selected.Value.Tab; var originLocation = origin.Location;
        List<ItemRef> items;
        try { items = selected.Value.Items.ToList(); } finally { ItemSources.Release(selected.Value.Items); }
        if (items.Count > MaxArchiveBatch) { Notify($"Pack at most {MaxArchiveBatch:N0} items at a time.", true); return; }
        if (items.Count == 0) return;
        if (items.Any(item => item.FileSystemPath is null)) { Notify("Pack works on files and folders on disk.", true); return; }
        if (items.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != items.Count)
        { Notify("Two selected items have the same name; pack them from their own folders.", true); return; }
        string targetFolder = Workspace.ActiveTarget?.ActiveTab?.Location is { IsFileSystem: true } target
            ? target.Path : Path.GetDirectoryName(items[0].FileSystemPath!)!;
        string stem = items.Count == 1 ? items[0].IsContainer ? items[0].Name : Path.GetFileNameWithoutExtension(items[0].Name)
            : Path.GetFileName(Path.GetDirectoryName(items[0].FileSystemPath!)!.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(stem)) stem = "archive";
        string suggestion = Path.Combine(targetFolder, stem + ".zip");
        var formats = ArchiveCreationFormat.All.Where(format => format.DefaultProfile is not { } profile ||
            Services.Jobs.ArchiveAvailability(new(suggestion, null, [], Profile: profile)).Available).ToArray();
        using var dialog = new ArchiveCreationDialog(suggestion, selected.Value.Summary, formats, Services.Jobs.ArchiveAvailability);
        while (!Services.Io.IsStopped)
        {
            // A refused destination keeps the form and password draft. Each overlay owns a fresh wrapper;
            // detach the reusable form before the next overlay takes ownership of it.
            var body = new Border { Child = dialog.Content };
            object? answer;
            try
            {
                answer = await Dialogs.ShowCustomAsync("Create archive", body,
                    [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Pack", "pack", IsDefault: true)],
                    dialog.InitialFocus, canConfirm: () => dialog.CanConfirm);
            }
            finally { body.Child = null; }
            if (answer as string != "pack" || dialog.Selection is not { } choice || Services.Io.IsStopped) return;
            FileSystemItemInfo? info;
            try
            {
                info = await MutationIoAsync(choice.Path, () => Services.Platform.FileOperations.TryGetInfo(choice.Path),
                    () => { if (Services.Io.IsStopped) throw new OperationCanceledException(); });
            }
            catch (OperationCanceledException) { return; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { if (!Services.Io.IsStopped) dialog.ShowProblem("Cannot read this archive path: " + Formatters.SafeName(error.Message)); continue; }
            if (info?.IsDirectory == true) { dialog.ShowProblem("A folder with that name exists; choose an archive filename."); continue; }
            if (items.Any(item => PathUtil.IsSameOrUnder(choice.Path, item.FileSystemPath!)))
            { dialog.ShowProblem("The archive cannot be created inside a folder that is being packed."); continue; }
            if (info is not null)
            {
                if (choice.Format.Format != ArchiveWriteFormat.Zip)
                { dialog.ShowProblem("That filename already exists. Choose a new filename; the existing file will be kept."); continue; }
                await AddToArchiveTransferAsync(JobKind.Copy, items, ZipProvider.ForFile(choice.Path), new TransferOptions(),
                    null, QueueMode.Start, origin, originLocation);
                return;
            }
            string? focus = originLocation is { IsFileSystem: true } &&
                string.Equals(originLocation.Path, Path.GetDirectoryName(choice.Path), StringComparison.Ordinal) ? Path.GetFileName(choice.Path) : null;
            string description = $"Create {choice.Format.Name} archive “{Path.GetFileName(choice.Path)}”";
            var changes = items.Select(item => new ArchiveChange(item.IsContainer ? ArchiveChangeKind.AddFolder : ArchiveChangeKind.AddFile,
                item.Name, item.FileSystemPath)).ToArray();
            if (choice.Profile is null)
            {
                SubmitArchive(new(choice.Path, null, changes, choice.LegacyLevel), items, Location.FileSystem(Path.GetDirectoryName(choice.Path)!),
                    description, focus, origin, originLocation: originLocation);
                return;
            }
            var plan = new ArchivePlan(choice.Path, null, changes, Profile: choice.Profile);
            if (!Services.Jobs.ArchiveAvailability(plan).Available)
            { dialog.ShowProblem("Writing this format is unavailable here. Choose another format or location."); continue; }
            ArchiveJobPassword? password = null;
            try
            {
                password = dialog.TakePassword();
                Services.Jobs.SubmitArchive(plan, password, description: description, register: job =>
                {
                    if (originLocation is not null) _jobOrigins[job] = (origin, originLocation);
                    if (focus is not null) _focusAfter[job] = focus;
                });
                password = null; // The manager now owns the secret through queueing, execution and cleanup.
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            { Notify("Cannot create this archive: " + Formatters.SafeName(error.Message), true); return; }
            finally { password?.Dispose(); }
        }
    }

    /// <summary>Test archive: decompress every member of the selected ZIPs (or the open archive) and compare checksums.</summary>
    private void TestArchives()
    {
        var tab = ActiveTab;
        if (tab?.Location is { Scheme: Schemes.Zip, Container.IsFileSystem: true } inside)
        {
            var archive = ArchiveFile(inside);
            SubmitTest([ItemRef.ForFileSystemPath(archive, EntryKind.File)]);
            return;
        }
        var sel = SourceSelection();
        if (sel is null) return;
        List<ItemRef> zips;
        try { zips = sel.Value.Items.Where(i => i.FileSystemPath is { } p && !i.IsContainer &&
                                              Services.Providers.TryGet(Schemes.Zip, out var z) && z is ZipProvider zp && zp.IsContainer(i.Name)).ToList(); } finally { ItemSources.Release(sel.Value.Items); }
        if (zips.Count == 0)
        {
            Notify("Select ZIP archives (or open one) to test them.", true);
            return;
        }
        SubmitTest(zips);
    }

    private void SubmitTest(IReadOnlyList<ItemRef> archives)
    {
        var job = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.ArchiveTest,
            Sources = archives,
            Description = archives.Count == 1 ? $"Test \"{archives[0].Name}\"" : $"Test {archives.Count:N0} archives",
        });
        if (ActiveTab is { } tab) Track(job, tab);
        Notify("Testing… the result shows here when it is done (details: Ctrl+J).");
    }
}
