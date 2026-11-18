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
    private string TransfersName => scope + ".transfers.v1";

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

    /// <summary>Reads the authoritative record once. Older installations stored the values separately.</summary>
    internal TransferPackets? ReadSnapshot()
    {
        string? text = registry.Read(TransfersName, TransferPackets.MaxCharacters);
        return text is null ? null : TransferPackets.Decode(text)
            ?? throw new IOException("The remembered transfers use an invalid or unsupported format.");
    }

    private string? ReadAppearance(TransferPackets? snapshot) => snapshot is not null
        ? snapshot.Appearance : registry.Read(AppearanceName, AppearanceTransferV1.MaxCharacters);

    private string? ReadWorkspace(TransferPackets? snapshot) => snapshot is not null
        ? snapshot.Workspace : registry.Read(WorkspaceName, WorkspaceTransfer.MaxCharacters);

    internal void LoadAppearance(AppSettings settings, TransferPackets? snapshot)
    {
        string? text = ReadAppearance(snapshot);
        if (text is null) return;
        if (!AppearanceTransferV1.TryParse(text, ThemeManager.Names, out var value) || value is null)
            throw new IOException("The remembered appearance uses an invalid or unsupported format.");
        value.ApplyTo(settings);
    }

    internal WorkspaceTransfer? LoadWorkspace(TransferPackets? snapshot)
    {
        string? text = ReadWorkspace(snapshot);
        return text is null ? null : WorkspaceTransfer.Decode(text)
            ?? throw new IOException("The remembered workspace uses an invalid or unsupported format.");
    }

    internal WorkspaceTransfer? LoadWorkspace() => LoadWorkspace(ReadSnapshot());

    internal void RememberAppearance(AppearanceTransferV1 value) => WithWriterLock(() =>
        Save(new TransferPackets(value.Encode(), ReadWorkspace(ReadSnapshot()))));

    internal void RememberWorkspace(WorkspaceTransfer value) => WithWriterLock(() =>
        Save(new TransferPackets(ReadAppearance(ReadSnapshot()), value.Encode())));

    internal void RememberBoth(AppearanceTransferV1 appearance, WorkspaceTransfer workspace) => WithWriterLock(() =>
        Save(new TransferPackets(appearance.Encode(), workspace.Encode())));

    private void Save(TransferPackets packets) => registry.Save(TransfersName, packets.Encode(), TransferPackets.MaxCharacters);

    internal void Forget() => WithWriterLock(() =>
    {
        // Remove the legacy values before removing the record that supersedes them.
        registry.Forget(AppearanceName);
        registry.Forget(WorkspaceName);
        registry.Forget(TransfersName);
    });

    private void WithWriterLock(Action action)
    {
        using var mutex = new Mutex(false, @"Global\FileCat.AdministratorTransfers." + scope);
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(2)))
                throw new IOException("Another administrator window is saving these preferences. Try again.");
        }
        catch (AbandonedMutexException) { }
        try { action(); }
        finally { mutex.ReleaseMutex(); }
    }

    internal static bool IsStoreFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException or Win32Exception or WaitHandleCannotBeOpenedException;
}
