using System.ComponentModel;
using FileCat.Core.Resources;
using Microsoft.Win32;

namespace FileCat.Platform.Windows;

/// <summary>A concrete key that an alias view (HKCR, HKCC) shows, and whether it exists yet.</summary>
/// <param name="Existing">The deepest existing key on the way (the target itself when it exists).</param>
public sealed record RegistryWriteTarget(string Label, string Note, Location Target, bool Exists, Location Existing);

/// <summary>
/// HKCR merges per-user and machine class registrations and HKCC aliases the current hardware profile (plan §12.3).
/// Both are browsed read-only; edits go to an explicit underlying key that the user chooses and sees.
/// </summary>
public static class RegistryAliases
{
    private const int MaxLinkHops = 8;

    public static bool IsAliasPath(string path) => path is "HKCR" or "HKCC" ||
        path.StartsWith("HKCR\\", StringComparison.Ordinal) || path.StartsWith("HKCC\\", StringComparison.Ordinal);

    public const string ReadOnlyReason =
        "HKCR and HKCC are merged or alias views and are read-only here. Use File → Open writable Registry location… to edit the key they show.";

    public static IReadOnlyList<RegistryWriteTarget> WritableTargets(Location alias)
    {
        if (alias.Scheme != Schemes.Registry || !IsAliasPath(alias.Path)) return [];
        string rest = alias.Path.Length > 4 ? alias.Path[4..] : "";
        // HKCU\Software\Classes and HKCC's path are themselves Registry links; links are never followed implicitly,
        // so each target is resolved to the real key it names before it is offered.
        var candidates = alias.Path.StartsWith("HKCR", StringComparison.Ordinal)
            ? new[]
            {
                ("Current user", "Per-user registration; it takes precedence over the machine entry.", @"HKCU\Software\Classes" + rest),
                ("All users", "Machine registration; changing it usually needs administrator rights.", @"HKLM\SOFTWARE\Classes" + rest),
            }
            : [("Current hardware profile", "HKCC aliases this key through Registry links.", @"HKLM\SYSTEM\CurrentControlSet\Hardware Profiles\Current" + rest)];
        var result = new List<RegistryWriteTarget>();
        foreach (var (label, note, path) in candidates)
            if (ResolveLinks(alias.WithPath(path)) is { } resolved) result.Add(Target(label, note, resolved));
        return result;
    }

    private static RegistryWriteTarget Target(string label, string note, Location target)
    {
        var existing = target;
        while (true)
        {
            try
            {
                using (WindowsRegistryProvider.Open(existing, writable: false)) { }
                return new RegistryWriteTarget(label, note, target, ReferenceEquals(existing, target), existing);
            }
            catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException or System.Security.SecurityException)
            {
                int slash = existing.Path.LastIndexOf('\\');
                if (slash < 0) return new RegistryWriteTarget(label, note, target, false, existing);
                existing = existing.WithPath(existing.Path[..slash]);
            }
        }
    }

    /// <summary>
    /// Replaces each Registry link on the path with its target (HKLM/HKU only), so the result names the real key.
    /// Returns null for a link loop or a target outside the local machine and user hives.
    /// </summary>
    public static Location? ResolveLinks(Location location)
    {
        var parts = new List<string>(location.Path.Split('\\'));
        for (int hops = 0; hops <= MaxLinkHops; hops++)
        {
            bool restarted = false;
            using var root = WindowsRegistryProvider.Open(location.WithPath(parts[0]), writable: false);
            RegistryKey current = root;
            var opened = new List<RegistryKey>();
            try
            {
                for (int i = 1; i < parts.Count; i++)
                {
                    string? link;
                    try { link = RegistryRaw.LinkTarget(current, parts[i]); }
                    catch (Win32Exception) { return location.WithPath(string.Join('\\', parts)); } // missing: nothing more to resolve
                    if (link is not null)
                    {
                        var mapped = MapNativePath(link);
                        if (mapped is null) return null;
                        parts = [.. mapped.Split('\\'), .. parts.Skip(i + 1)];
                        restarted = true;
                        break;
                    }
                    var child = RegistryRaw.OpenNoLink(current, parts[i], current.View, writable: false);
                    opened.Add(child);
                    current = child;
                }
            }
            finally
            {
                foreach (var key in opened) key.Dispose();
            }
            if (!restarted) return location.WithPath(string.Join('\\', parts));
        }
        return null;
    }

    /// <summary>Kernel Registry paths (\REGISTRY\MACHINE\…, \REGISTRY\USER\…) as HKLM/HKU paths.</summary>
    public static string? MapNativePath(string target)
    {
        const string machine = @"\Registry\Machine", user = @"\Registry\User";
        string trimmed = target.TrimEnd('\\');
        if (trimmed.StartsWith(machine, StringComparison.OrdinalIgnoreCase) && (trimmed.Length == machine.Length || trimmed[machine.Length] == '\\'))
            return "HKLM" + trimmed[machine.Length..];
        if (trimmed.StartsWith(user, StringComparison.OrdinalIgnoreCase) && (trimmed.Length == user.Length || trimmed[user.Length] == '\\'))
            return "HKU" + trimmed[user.Length..];
        return null;
    }
}
