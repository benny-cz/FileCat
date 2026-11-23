using Avalonia.Input.Platform;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Listing;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Threading;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private const int PermissionSelectionLimit = 128;
    private static readonly PermissionAuthority LocalPermissionAuthority = new(PermissionAuthorityKind.Local, "local", 0);
    private static PermissionSections PermissionReadSections => PermissionSections.Ordinary |
        (OperatingSystem.IsWindows() ? PermissionSections.Audit :
            OperatingSystem.IsMacOS() ? PermissionSections.Flags | PermissionSections.Times | PermissionSections.VolumePolicy :
            OperatingSystem.IsLinux() ? PermissionSections.Flags : PermissionSections.None);
    private bool _preparingPermissionInspection;

    internal sealed record PermissionTarget(string Name, PermissionSubject Subject, bool Hidden, string? Refusal = null, ItemRef? Source = null);
    internal sealed record PermissionSelection(TabViewModel Tab, Location Location, long Navigation, int ListingGeneration, long Marks,
        PermissionTarget[] Targets, string? Refusal = null)
    {
        internal bool SameNavigation => !Tab.Listing.IsDisposed && Tab.Location == Location && Tab.NavigationGeneration == Navigation;
        internal bool HasOriginalMarks => SameNavigation && Tab.Listing.MarkRevision == Marks;
        internal bool IsCurrent => HasOriginalMarks && Tab.Listing.Generation == ListingGeneration;
    }

    /// <summary>
    /// Why this FileCat process changes no permissions (null: it may). An administrator window, or FileCat running as root,
    /// acts with another token, may run user-writable code and has no per-plan consent (plan §6): it inspects read-only,
    /// with every change action absent, and points to the usual window for changes.
    /// </summary>
    internal static string? PermissionChangeRefusal()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!WindowsTokenAuthority.IsHighIntegrity) return null;
            return WindowsTokenAuthority.HasDefaultElevationType
                ? "Read-only: this sign-in has no standard-user token. Change permissions after signing in with a standard token."
                : "Read-only in an administrator window. Change permissions from your usual FileCat window.";
        }
        return Environment.IsPrivilegedProcess ? "Read-only while FileCat runs as root. Change permissions from an ordinary FileCat window." : null;
    }

    /// <summary>
    /// One local object's permissions for display. In an administrator window on Windows x64 the window's own backup and
    /// security privileges complete the ordinary read (owner, DACL and audit rules even where its DACL denies the
    /// Administrators group), read-only (G-PRIV). Where that read is refused or fails, the ordinary read stays, and the
    /// audit section says why.
    /// </summary>
    private PermissionInspection InspectLocalPermissions(PermissionSubject subject, PermissionSections sections, CancellationToken ct)
    {
        var inspection = Services.Permissions.Inspect(subject, sections, ct);
        if (Services.Permissions is not WindowsPermissionService || !WindowsPermissionService.CanInspectElevated(inspection.Snapshot))
            return inspection;
        try
        {
            return WindowsPermissionService.InspectElevated(inspection, WindowsPermissionService.DiscretionarySections | PermissionSections.Audit, ct);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or
            System.ComponentModel.Win32Exception)
        {
            ct.ThrowIfCancellationRequested();
            // The binding was given up with the failed read; this window changes nothing, so the observation alone stays.
            var snapshot = inspection.Snapshot;
            inspection.Dispose();
            return new(snapshot with
            {
                Audit = snapshot.Audit with
                {
                    Reason = "This administrator window could not complete its privileged read: " + Formatters.SafeName(error.Message)
                }
            });
        }
    }

    internal static PermissionSelection CapturePermissionsSelection(TabViewModel tab)
    {
        var listing = tab.Listing;
        var selection = new PermissionSelection(tab, tab.Location!, tab.NavigationGeneration, listing.Generation, listing.MarkRevision, []);
        int limit = OperatingSystem.IsMacOS() ? PermissionSelectionLimit / 2 : PermissionSelectionLimit;
        if (listing.MarkedCount > limit)
            return selection with { Refusal = $"Select at most {limit} objects for one permissions review." };
        Span<int> indices = stackalloc int[PermissionSelectionLimit + 1];
        int count = listing.HasMarks ? listing.CopyMarkedStoreIndices(-1, indices) : 0;
        if (!listing.HasMarks && listing.TryGetFocused(out var focused) && focused.Kind != EntryKind.Parent)
        {
            indices[0] = listing.FocusedStoreIndex;
            count = 1;
        }
        if (count == 0 || count > limit)
            return selection with { Refusal = "The selection has changed. Focus or mark the objects again." };

        var targets = new PermissionTarget[count];
        for (int n = 0; n < count; n++)
        {
            int index = indices[n];
            var entry = listing.Store[index];
            var original = listing.GetItemRef(index);
            bool hidden = listing.HasMarks && listing.GetVisibleIndex(index) < 0;
            if (entry.Tag is Core.FileSystem.DriveTag drive)
            {
                var subject = new PermissionSubject(Location.FileSystem(drive.RootPath), drive.RootPath, drive.RootPath,
                    LocalPermissionAuthority, PermissionObjectKind.VolumeRoot, PermissionLinkPolicy.NoFollow);
                targets[n] = new(entry.Name, subject, hidden, drive.Ready
                    ? "The drive listing has no captured mount identity. Open the mounted folder and use its background menu."
                    : "This drive is offline or has no mounted filesystem.");
                continue;
            }
            string locator = original.FileSystemPath ?? original.Name;
            var kind = original.Flags.HasFlag(EntryFlags.Link) ? PermissionObjectKind.Link : original.Kind switch
            {
                EntryKind.File => PermissionObjectKind.File,
                EntryKind.Directory => PermissionObjectKind.Directory,
                _ => PermissionObjectKind.Virtual
            };
            var authority = original.Parent.Scheme is Schemes.Sftp or Schemes.Ftp
                ? new PermissionAuthority(PermissionAuthorityKind.RemoteSession, original.Parent.Session ?? original.Parent.Scheme, 0) : LocalPermissionAuthority;
            targets[n] = new(original.Name, new(original.Parent, locator, locator, authority, kind, PermissionLinkPolicy.NoFollow,
                OriginalOrdinal: original.Ordinal, ListedSize: kind == PermissionObjectKind.File && original.Size >= 0 ? original.Size : null,
                ListedModifiedUtcTicks: kind == PermissionObjectKind.File && original.Modified > 0 ? original.Modified : null), hidden, Source: original);
        }
        return selection with { Targets = targets };
    }

    internal Task InspectFolderPermissionsAsync(TabViewModel tab, Location folder, int generation)
    {
        var subject = new PermissionSubject(folder, folder.Path, folder.Path, LocalPermissionAuthority, PermissionObjectKind.Unknown,
            PermissionLinkPolicy.NoFollow);
        var selection = new PermissionSelection(tab, folder, tab.NavigationGeneration, generation, tab.Listing.MarkRevision,
            [new(folder.Path, subject, false, Source: folder.IsFileSystem ? ItemRef.ForFileSystemPath(folder.Path, EntryKind.Directory) : null)]);
        return InspectPermissionsAsync(selection);
    }

    /// <summary>
    /// A place has no listing of its own to bind a review to. It opens in a new tab at exactly its location (a drive at
    /// its root, not the folder another panel shows on that drive), and that folder's permissions open as from its
    /// background menu, bound to the new tab.
    /// </summary>
    internal async Task InspectPlacePermissionsAsync(PanelViewModel panel, Place place)
    {
        if (!place.OpensInPanel || place.Location is not { IsFileSystem: true } location)
        {
            Notify("Only file-system places have permissions to inspect here.");
            return;
        }
        var tab = panel.OpenTab(location);
        Workspace.Activate(panel);
        if (!await ListingSettledAsync(tab, TimeSpan.FromSeconds(30)))
        {
            Notify($"{place.Title} is still opening. Inspect its permissions from its background menu.");
            return;
        }
        if (!panel.Tabs.Contains(tab) || tab.Location != location)
        {
            Notify("The place's tab changed before its permissions opened.");
            return;
        }
        await InspectFolderPermissionsAsync(tab, location, tab.Listing.Generation);
    }

    private static async Task<bool> ListingSettledAsync(TabViewModel tab, TimeSpan timeout)
    {
        bool Settled() => tab.Listing.State != ListingState.Loading && !tab.Listing.IsRefreshing;
        if (Settled()) return true;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, ListingChange change) { if (Settled()) settled.TrySetResult(); }
        tab.Listing.Changed += Changed;
        try { return await Task.WhenAny(settled.Task, Task.Delay(timeout)) == settled.Task || Settled(); }
        finally { tab.Listing.Changed -= Changed; }
    }

    internal async Task InspectPermissionsAsync(PermissionSelection selection)
    {
        if (selection.Refusal is { } refusal) { Notify(refusal); return; }
        if (_preparingPermissionInspection) { Notify("A permissions inspection is already opening."); return; }
        if (!selection.IsCurrent) { Notify("The original selection changed. Open its permissions again."); return; }
        var dialogs = Dialogs;
        var clipboard = View.Clipboard;
        _preparingPermissionInspection = true;
        try
        {
            using var preparation = new PreparationScope(selection.Tab, Services.Io);
            var targets = selection.Targets;
            int hidden = targets.Count(t => t.Hidden);
            if (hidden != 0)
            {
                var choice = await dialogs.ChooseAsync(new ChoiceOptions("Inspect selected permissions",
                    [new($"Include all {targets.Length} selected objects", $"{hidden} hidden marks"), new($"Visible items only ({targets.Length - hidden})") ]));
                if (choice.Index < 0) return;
                if (choice.Index == 1) targets = targets.Where(t => !t.Hidden).ToArray();
                if (targets.Length == 0) { Notify("There are no visible selected objects."); return; }
            }

            var inspections = new List<(PermissionTarget Target, PermissionInspection Inspection)>(targets.Length);
            try
            {
                var remote = new Dictionary<PermissionTarget, PermissionSnapshot>();
                foreach (var group in targets.Where(CanObserveServer).GroupBy(target => target.Subject.OriginalLocation))
                {
                    if (!selection.IsCurrent) throw new OperationCanceledException();
                    preparation.Check();
                    var members = group.ToArray();
                    var subjects = members.Select(target => target.Subject).ToArray();
                    string device = Services.SftpProvider.GetDeviceKey(group.Key);
                    bool ftp = Services.Sftp.Profile(group.Key.Session!)?.IsFtp == true;
                    var snapshots = await Services.Io.RunOwned(device, IoPriority.Interactive,
                        ct => ftp ? Services.SftpProvider.InspectFtpPermissions(group.Key, subjects, ct)
                            : Services.SftpProvider.InspectSftpPermissions(group.Key, subjects, ct), preparation.Stop.Token);
                    for (int i = 0; i < members.Length; i++) remote[members[i]] = snapshots[i];
                }

                foreach (var target in targets)
                {
                    if (!selection.IsCurrent) throw new OperationCanceledException();
                    preparation.Check();
                    var sections = PermissionReadSections;
                    PermissionInspection inspection;
                    if (target.Refusal is { } reason)
                        inspection = PermissionSnapshots.Unavailable(target.Subject, sections, PermissionReadStatus.Unsupported, reason);
                    else if (remote.TryGetValue(target, out var observed))
                        inspection = new(observed);
                    else if (!target.Subject.OriginalLocation.IsFileSystem || target.Subject.Kind is PermissionObjectKind.Virtual or PermissionObjectKind.Link)
                        inspection = PermissionSnapshots.Unavailable(target.Subject, sections, PermissionReadStatus.Unsupported,
                            target.Subject.Kind == PermissionObjectKind.Link ? "This is a link. Select its target separately to inspect that object."
                            : "This provider has no permissions reader for the selected object.");
                    else
                    {
                        var location = target.Subject.OriginalLocation;
                        string device = Services.Providers.For(location).GetDeviceKey(location);
                        inspection = await Services.Io.RunOwned(device, IoPriority.Interactive,
                            ct => InspectLocalPermissions(target.Subject, sections, ct), preparation.Stop.Token);
                    }
                    inspections.Add((target, inspection));
                }
                if (!selection.IsCurrent) throw new OperationCanceledException();
                preparation.Check();
                preparation.Detach();
                await ShowPermissionInspectorAsync(dialogs, inspections.Select(item =>
                    new InspectedPermission(item.Target.Name, item.Target.Source, item.Inspection)).ToArray(),
                    clipboard is null ? null : clipboard.SetTextAsync, selection);
            }
            finally { foreach (var item in inspections) item.Inspection.Dispose(); }
        }
        catch (OperationCanceledException) { if (!Services.Io.IsStopped) Notify("The selection changed while opening permissions."); }
        finally { _preparingPermissionInspection = false; }

        bool CanObserveServer(PermissionTarget target) => target.Refusal is null &&
            target.Subject.OriginalLocation.Scheme is Schemes.Sftp or Schemes.Ftp &&
            target.Subject.OriginalLocation.Session is { } id && Services.Sftp.Profile(id) is { } profile &&
            (target.Subject.Kind is PermissionObjectKind.File or PermissionObjectKind.Directory ||
                profile.Protocol == RemoteProtocols.Sftp && target.Subject.Kind == PermissionObjectKind.Link);
    }

    private async Task<string?> InspectRecoveredPermissionsAsync(string path, string identityDigest, CancellationToken cancellation)
    {
        if (PermissionChangeRefusal() is { } refusal) return refusal;
        if (!Path.IsPathFullyQualified(path)) return "The retained locator has no admitted local filesystem path.";
        if (_view?.TopLevel is not Avalonia.Controls.Window { IsVisible: true } main || Services.Io.IsStopped || _dialogs.IsOpen)
            return "Open the owning workspace and close any other dialog before inspecting permissions.";
        var location = Location.FileSystem(path);
        var subject = new PermissionSubject(location, path, path, LocalPermissionAuthority, PermissionObjectKind.Unknown, PermissionLinkPolicy.NoFollow);
        string device = Services.Providers.For(location).GetDeviceKey(location);
        using var inspection = await Services.Io.RunOwned(device, IoPriority.Interactive,
            token => Services.Permissions.Inspect(subject, PermissionReadSections, token), cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (!main.IsVisible || _view.TopLevel != main || _dialogs.IsOpen) return "The owning workspace changed during inspection.";
        if (inspection.Snapshot.Binding?.Identity is not { } current || PermissionJournalNames.IdentityDigest(current) != identityDigest)
            return "The retained object and mount could not be corroborated. Select the current file or folder in a panel to start a fresh review.";
        var clipboard = _view.Clipboard;
        main.Activate();
        await _dialogs.ShowCustomAsync("Current permissions and ownership", new PermissionInspectorView([(path, inspection.Snapshot)],
            clipboard is null ? null : clipboard.SetTextAsync, principalNames: Services.PermissionPrincipalNames),
            [new("Close", "close", IsDefault: true, IsCancel: true)], resizable: true,
            behavior: new DialogBehavior { DismissOnBackdrop = false, PreferredSize = new Avalonia.Size(860, 640), SizeKey = "permissions.current" });
        return null;
    }
}
