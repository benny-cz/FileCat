using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Operations;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.App.Services;

internal sealed record AdministratorLinkDraft(string Destination, bool RelativeTarget, AdministratorLinkDraft.Target[] Targets,
    Dictionary<string, string> Volumes)
{
    internal const int MaxLinks = 10_000;
    internal sealed record Target(string Path, bool IsDirectory);
    public LinkKind Kind { get; init; } = LinkKind.Symbolic;

    // Only the exact previewed input crosses the boundary. The elevated window still asks the user to create it.
    internal static AdministratorLinkDraft Capture(IReadOnlyList<LinkPreview> rows, bool relative, LinkKind kind = LinkKind.Symbolic)
    {
        string destination = rows.Count == 1 ? rows[0].LinkPath : Path.GetDirectoryName(rows[0].LinkPath)!;
        return Capture(rows.Select(row => row.Item), destination, relative, kind);
    }

    internal static AdministratorLinkDraft Capture(IEnumerable<ItemRef> items, string destination, bool relative, LinkKind kind)
    {
        Target[] targets = items.Select(item => new Target(item.FileSystemPath ??
            throw new NotSupportedException("Select local files or folders for administrator link review."), item.IsContainer)).ToArray();
        return new(destination, relative, targets, AdministratorWindow.CaptureVolumes(
            targets.Select(target => target.Path).Prepend(destination))) { Kind = kind };
    }

    internal ElevationExchange Stage(string root) =>
        ElevationExchange.StageReadOnlyRequest(root, JsonSerializer.SerializeToUtf8Bytes(this));

    internal static AdministratorLinkDraft Read(string path, string hash)
    {
        using var exchange = BrokerExchange.Open(path);
        byte[] bytes = exchange.ReadPlan();
        if (!string.Equals(ElevationPlanCodec.Hash(bytes), hash, StringComparison.Ordinal))
            throw new InvalidDataException("The symbolic-link draft changed. Open it again from the ordinary window.");
        var draft = JsonSerializer.Deserialize<AdministratorLinkDraft>(bytes);
        if (draft is null || !Enum.IsDefined(draft.Kind) || draft.Targets is not { Length: > 0 and <= MaxLinks } || draft.Volumes is null ||
            draft.Targets.Any(target => target is null || string.IsNullOrEmpty(target.Path)) || string.IsNullOrEmpty(draft.Destination))
            throw new InvalidDataException("The symbolic-link draft is incomplete.");

        AdministratorWindow.VerifyVolumes(draft.Targets.Select(target => target.Path).Prepend(draft.Destination), draft.Volumes);
        return draft;
    }
}
