using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using FileCat.Core.State;
using FileCat.Platform.Windows;

namespace FileCat.App.Services;

[SupportedOSPlatform("windows")]
internal sealed class AdministratorTransferPreferences(AdministratorTransferRegistry registry, string scope)
{
    private string AppearanceName => scope + ".appearance";
    private string WorkspaceName => scope + ".workspace";

    internal static AdministratorTransferPreferences? ForProfile(AppPaths paths, AdministratorTransferRegistry? registry = null)
    {
        // --data reserves all persistent writes for the chosen recovery destination.
        if (!OperatingSystem.IsWindows() || !WindowsTokenAuthority.IsHighIntegrity || paths.DataRoot is not null)
            return null;
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is not { } user) return null;
        return new(registry ?? new AdministratorTransferRegistry(), ProfileScope(user.Value, paths.SettingsDirectory));
    }

    internal static string ProfileScope(string sid, string settingsDirectory) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(sid + '\0' + Path.GetFullPath(settingsDirectory).ToUpperInvariant())));

    internal void LoadAppearance(AppSettings settings)
    {
        string? text = registry.Read(AppearanceName, AppearanceTransferV1.MaxCharacters);
        if (text is null) return;
        if (!AppearanceTransferV1.TryParse(text, ThemeManager.Names, out var value) || value is null)
            throw new IOException("The remembered appearance uses an invalid or unsupported format.");
        value.ApplyTo(settings);
    }

    internal WorkspaceTransfer? LoadWorkspace()
    {
        string? text = registry.Read(WorkspaceName, WorkspaceTransfer.MaxCharacters);
        return text is null ? null : WorkspaceTransfer.Decode(text)
            ?? throw new IOException("The remembered workspace uses an invalid or unsupported format.");
    }

    internal void RememberAppearance(AppearanceTransferV1 value) =>
        registry.Save(AppearanceName, value.Encode(), AppearanceTransferV1.MaxCharacters);

    internal void RememberWorkspace(WorkspaceTransfer value) =>
        registry.Save(WorkspaceName, value.Encode(), WorkspaceTransfer.MaxCharacters);

    internal void Forget()
    {
        registry.Forget(AppearanceName);
        registry.Forget(WorkspaceName);
    }

    internal static bool IsStoreFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException or Win32Exception;
}
