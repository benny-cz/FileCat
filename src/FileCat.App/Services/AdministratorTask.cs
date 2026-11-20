using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.App.Services;

internal enum AdministratorTaskKind { View, Checksums, Search, Rename, CreateFile, Attributes, Registry, RegistryExport, RecoveryVolume, RecoveryDisk, FileRetry }

/// <summary>The selected operation carried into a separate administrator window.</summary>
internal sealed record AdministratorTask(AdministratorTaskKind Kind)
{
    public string[] Paths { get; init; } = [];
    public string[] Names { get; init; } = [];
    public Dictionary<string, string> Volumes { get; init; } = [];
    public bool Alternate { get; init; }
    public string? Detail { get; init; }
    public string? Context { get; init; }
    public string? Device { get; init; }
    public AttributeChangeSet? Attributes { get; init; }
    public AdministratorSearchDraft? Search { get; init; }
    public AdministratorRegistryExport[]? RegistryExport { get; init; }
    public AdministratorFileRetry? FileRetry { get; init; }

    internal ElevationExchange Stage(string root)
    {
        var draft = this with { Volumes = AdministratorWindow.CaptureVolumes(Paths) };
        draft.Validate();
        return ElevationExchange.StageReadOnlyRequest(root, JsonSerializer.SerializeToUtf8Bytes(draft));
    }

    internal static AdministratorTask Read(string path, string hash)
    {
        using var exchange = BrokerExchange.Open(path);
        byte[] bytes = exchange.ReadPlan();
        if (!string.Equals(hash, ElevationPlanCodec.Hash(bytes), StringComparison.Ordinal))
            throw new InvalidDataException("The administrator request changed. Reopen it from the ordinary window.");
        var task = JsonSerializer.Deserialize<AdministratorTask>(bytes)
            ?? throw new InvalidDataException("The administrator request is empty.");
        task.Validate();
        AdministratorWindow.VerifyVolumes(task.Paths, task.Volumes);
        return task;
    }

    private void Validate()
    {
        if (Paths is null || Paths.Length > 50_000 || Paths.Any(string.IsNullOrEmpty) || Names is null || Volumes is null)
            throw new InvalidDataException("The administrator request has an invalid selection.");
        bool valid = Kind switch
        {
            AdministratorTaskKind.View => Paths.Length == 1,
            AdministratorTaskKind.Checksums => Paths.Length > 0,
            AdministratorTaskKind.Search => Search is { Criteria: not null, IgnoredFolders: not null, Folders.Length: > 0 } &&
                Search.Folders.All(folder => folder is not null && !string.IsNullOrEmpty(folder.Root) &&
                    !string.IsNullOrEmpty(folder.Folder) && folder.Depth >= 0) &&
                Paths.SequenceEqual(Search.Folders.Select(folder => folder.Folder)),
            AdministratorTaskKind.Rename => Paths.Length > 0 && Paths.Length == Names.Length &&
                Names.All(name => name is not null && PathUtil.ValidateNewName(name) is null),
            AdministratorTaskKind.CreateFile => Paths.Length == 1 && Names is [var name] &&
                name is not null && PathUtil.ValidateNewName(name) is null,
            AdministratorTaskKind.Attributes => Paths.Length > 0 && Attributes is { ChangesPermissions: false },
            AdministratorTaskKind.Registry => Paths.Length == 0 && IsRegistryKey(Detail) && IsRegistryView(Context),
            AdministratorTaskKind.RegistryExport => Paths.Length == 0 && Detail is { Length: > 0 } && IsRegistryView(Context) &&
                RegistryExport is { Length: > 0 } && RegistryExport.All(item => item is not null && IsRegistryKey(item.Key)),
            AdministratorTaskKind.RecoveryVolume => Paths.Length == 1 && Device is { Length: > 0 } && Detail is { Length: > 0 },
            AdministratorTaskKind.RecoveryDisk => Paths.Length == 0 && Device is { Length: > 0 } && Detail is { Length: > 0 },
            AdministratorTaskKind.FileRetry => FileRetry is { Sources: not null } retry && Paths.SequenceEqual(retry.Paths),
            _ => false,
        };
        if (!valid) throw new InvalidDataException("The administrator request has an invalid operation or scope.");
        if (Kind == AdministratorTaskKind.FileRetry) FileRetry!.Validate();
    }

    internal static bool IsRegistryKey(string? path) => path?.Split('\\', 2)[0] is "HKLM" or "HKU" or "HKCC";

    private static bool IsRegistryView(string? view) => view is null or "default" or "32" or "64";
}

internal sealed record AdministratorRegistryExport(string Key, string? Value, bool CurrentUser)
{
    internal RegistryInterchange.Selection ForView(string? view) =>
        new(new Location(Schemes.Registry, Key, session: view), Value, CurrentUser ? "HKCU" + RegistrySubpath(Key) : null);

    private static string RegistrySubpath(string key)
    {
        if (!key.StartsWith(@"HKU\S-1-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The requesting user's Registry hive could not be identified.");
        int end = key.IndexOf('\\', 4);
        return end < 0 ? string.Empty : key[end..];
    }
}
