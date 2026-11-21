using Avalonia.Input.Platform;
using FileCat.App.Views;
using FileCat.Core.Listing;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Threading;

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

    internal static string? PermissionProcessRefusal()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!WindowsTokenAuthority.IsHighIntegrity) return null;
            return WindowsTokenAuthority.HasDefaultElevationType
                ? "This sign-in has no standard-user token. Sign in with a standard token to inspect permissions."
                : "Open your usual FileCat window to inspect permissions.";
        }
        return Environment.IsPrivilegedProcess ? "Open an ordinary FileCat window to inspect permissions." : null;
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

    internal async Task InspectPermissionsAsync(PermissionSelection selection)
    {
        if ((PermissionProcessRefusal() ?? selection.Refusal) is { } refusal) { Notify(refusal); return; }
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
                            ct => Services.Permissions.Inspect(target.Subject, sections, ct), preparation.Stop.Token);
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
        if (PermissionProcessRefusal() is { } refusal) return refusal;
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
            behavior: new DialogBehavior { DismissOnBackdrop = false });
        return null;
    }
}
