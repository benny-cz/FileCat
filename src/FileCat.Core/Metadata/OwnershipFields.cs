using System.Globalization;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Metadata;

/// <summary>Instance fields capture their source and resolver; the static catalogue owns neither.</summary>
public static class OwnershipFields
{
    public static MetadataField CreateOwner(IOwnership source, PrincipalNames names) => Create(source, names, false);
    public static MetadataField CreateGroup(IOwnership source, PrincipalNames names) => Create(source, names, true);

    private static MetadataField Create(IOwnership source, PrincipalNames names, bool group) => new(
        group ? "group" : "owner", group ? "Group" : "Owner", MetadataCost.Cheap, _ => true,
        (path, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var read = group ? source.ReadGroup(path) : source.ReadOwner(path);
            ct.ThrowIfCancellationRequested();
            if (read.Status == OwnerReadStatus.Ok && read.Principal is { } principal)
            {
                names.Request(principal);
                return new MetadataValue(MetadataState.Available, principal, read.Reason);
            }
            return new MetadataValue(read.Status switch
            {
                OwnerReadStatus.Denied => MetadataState.Denied,
                OwnerReadStatus.Unsupported => MetadataState.Unsupported,
                OwnerReadStatus.Gone => MetadataState.Absent,
                _ => MetadataState.Failed,
            }, Detail: read.Reason, Observation: read.Status == OwnerReadStatus.Gone ? MetadataObservation.Gone : MetadataObservation.Current);
        },
        value => value is Principal p ? Format(p, names.Peek(p)) : string.Empty,
        SortKey: value => value is Principal p ? new PrincipalOrderKey(p, names.Peek(p), CultureInfo.CurrentCulture.CompareInfo) : null,
        Folders: true, RefreshAfter: TimeSpan.FromSeconds(5),
        Detail: value => value is Principal p ? Detail(p, names.Peek(p)) : string.Empty,
        NeedsExplanation: value => value is Principal p && names.Peek(p).Status != NameStatus.Resolved);

    public static string Format(Principal principal, PrincipalName name) =>
        name.Status == NameStatus.Resolved && !string.IsNullOrEmpty(name.Name) ? name.Name : principal.DisplayId;

    public static string Detail(Principal principal, PrincipalName name)
    {
        string id = principal.Kind switch
        {
            PrincipalKind.UnixUser => "UID " + principal.Id,
            PrincipalKind.UnixGroup => "GID " + principal.Id,
            _ => principal.Id,
        };
        if (name.Status == NameStatus.Resolved) return string.IsNullOrEmpty(name.Type) ? id : name.Type + " · " + id;
        string reason = name.Status switch
        {
            NameStatus.Pending => "Name lookup in progress.",
            NameStatus.NotFound => principal.Kind switch
            {
                PrincipalKind.UnixUser => $"This system has no user name for {id}.",
                PrincipalKind.UnixGroup => $"This system has no group name for {id}.",
                _ => "This computer has no name for this ID. It may belong to another computer or domain, or to an account that no longer exists.",
            },
            NameStatus.Unavailable => "The name is unavailable just now. It becomes eligible for retry after 30 seconds, when ownership is next read.",
            _ => "No name is cached. The next ownership read can request it.",
        };
        return id + " · " + reason;
    }
}
