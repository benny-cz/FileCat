namespace FileCat.Core.HiddenData;

public static class HiddenSearch
{
    public static string ScopeId => OperatingSystem.IsWindows() ? "windows-ads-v1" : OperatingSystem.IsMacOS() ? "darwin-ordinary-xattrs-v1" : "linux-visible-xattrs-v1";
    public static string Label => OperatingSystem.IsWindows() ? "Carries alternate data streams besides its download mark"
        : OperatingSystem.IsMacOS() ? "Carries ordinary-view attributes or resource forks besides its download mark"
        : "Carries attributes visible to this user besides its download mark";
    public static string Help => OperatingSystem.IsWindows() ? "Examines named alternate data streams only. Extended attributes (EAs) are excluded; no EA-value query is issued. Incomplete inventories are reported as unexamined."
        : "Examines bounded names and lengths visible to this user, without reading values. Incomplete inventories are reported as unexamined. macOS uses the ordinary API view; compression backing is not inventoried.";
    public static bool Includes(HiddenKind kind) => OperatingSystem.IsWindows() ? kind == HiddenKind.Stream
        : OperatingSystem.IsMacOS() ? kind is HiddenKind.Attribute or HiddenKind.ResourceFork : kind == HiddenKind.Attribute;
}
