using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsEfsKeys
{
    internal enum BackupState { WindowsReportedComplete, Canceled, Unavailable }
    internal sealed record BackupResult(BackupState State, int? NativeError, StorageProblem? Problem, StorageEfsBackupCompletion? Completion = null);
    private readonly record struct NativeBackup(BackupState State, int Error);
    internal delegate BackupResult InvokeBackupWizard(Func<BackupResult> show);

    [StructLayout(LayoutKind.Sequential)]
    private struct ExportInfo
    {
        public uint Size;
        public nint Filename;
        public uint SubjectChoice;
        public nint Certificate;
        public uint StoreCount;
        public nint Stores;
    }

    internal BackupResult BackUpAssociatedCertificate(StorageEfsAssociationSnapshot association, string certificateSha1,
        nint ownerWindow, bool explicitlyRequested, CancellationToken ct, InvokeBackupWizard? invokeWizard = null,
        Action<Action>? invokeProviderOwner = null)
    {
        if (!explicitlyRequested)
            return Refused("efs-backup-choice", "Request backup explicitly for the selected file-associated EFS key.");
        ArgumentNullException.ThrowIfNull(association);
        ValidateRequest(association.InvokerSid, certificateSha1);
        ct.ThrowIfCancellationRequested();
        if (!WindowsStorageTransforms.SupportsHost(qualifyNativeArm64))
            return Refused("efs-backup-host", "The EFS backup wizard requires its qualified Windows host.");
        if (association.Users.IsDefaultOrEmpty || !association.Users.Any(user => user.Sid == association.InvokerSid && user.CertificateSha1 == certificateSha1))
            return Refused("efs-backup-association", "The selected certificate is outside the invoking user's observed EFS associations.");
        if (OwnedWindowThread(ownerWindow) == 0)
            return Refused("efs-backup-owner", "Open the EFS backup wizard from a visible FileCat window.");

        if (invokeProviderOwner is not null && WindowsEfsCapiWorker.Available && !association.CurrentUserKeys.IsDefaultOrEmpty &&
            association.CurrentUserKeys.Any(key => key.InvokerSid == association.InvokerSid && key.CertificateSha1 == certificateSha1 &&
                key is { Provider: StorageEfsKeyProvider.Capi, CertificatePresent: true, ProviderAssociationPresent: true,
                    State: StorageEfsKeyState.InteractionRequired or StorageEfsKeyState.MatchedAvailable }))
            return BackUpCapiKey(association, certificateSha1, ownerWindow, ct);

        var interaction = invokeProviderOwner is not null && !association.CurrentUserKeys.IsDefaultOrEmpty &&
            association.CurrentUserKeys.Any(key => key.InvokerSid == association.InvokerSid &&
                key.CertificateSha1 == certificateSha1 && NeedsParentedCngAccess(key))
            ? new CngInteraction(ownerWindow, invokeProviderOwner) : null;
        BackupResult? result = null;
        var evidence = InspectAssociatedCertificate(association.InvokerSid, certificateSha1, ct, (fresh, backupProblem) =>
        {
            if (backupProblem is not null)
            {
                result = new(BackupState.Unavailable, null, backupProblem);
                return;
            }
            // Keep the certificate alive on the worker until the synchronous, owner-thread modal call returns.
            using var ownerTokenCharge = StorageNativeAllowance.RequireCurrent().ClaimScratch();
            var lifetime = new object();
            bool active = true, entered = false;
            try
            {
                if (invokeWizard is null) ShowOnce();
                else invokeWizard(ShowOnce);
            }
            finally { lock (lifetime) active = false; }
            result ??= Refused("efs-backup-owner", "The FileCat window did not run the requested backup wizard.");

            BackupResult ShowOnce()
            {
                lock (lifetime)
                {
                    if (!active || entered)
                        return Refused("efs-backup-owner", "The requested backup wizard is no longer active.");
                    entered = true;
                    return result = Show();
                }
            }

            BackupResult Show()
            {
                ct.ThrowIfCancellationRequested();
                if (OwnedWindowThread(ownerWindow) != GetCurrentThreadId())
                    return Refused("efs-backup-owner", "Open the EFS backup wizard from a live FileCat window on its UI thread.");
                CheckInvoker(association.InvokerSid, ct);

                return BackupOutcome(association, certificateSha1, ExportPrivateKey(fresh, ownerWindow));
            }
        }, interaction);
        return result ?? new(BackupState.Unavailable, evidence.NativeError, evidence.Problem);
    }

    private static NativeBackup ExportPrivateKey(nint certificate, nint owner)
    {
        var export = new ExportInfo { Size = (uint)sizeof(ExportInfo), SubjectChoice = 1 /* CERT_CONTEXT */, Certificate = certificate };
        // Windows collects the destination and password. The private key is mandatory and cannot be deleted.
        const uint exportPrivateKey = 0x0100, noDeletePrivateKey = 0x0200;
        if (CryptUIWizExport(exportPrivateKey | noDeletePrivateKey, owner, "Back up EFS key", &export, 0))
            return new(BackupState.WindowsReportedComplete, 0);
        int error = Marshal.GetLastPInvokeError();
        return new(error == 1223 /* ERROR_CANCELLED */ ? BackupState.Canceled : BackupState.Unavailable, error);
    }

    private static BackupResult BackupOutcome(StorageEfsAssociationSnapshot association, string hash, NativeBackup backup) =>
        backup.State switch
        {
            BackupState.WindowsReportedComplete => new(backup.State, null, null, new(association.Identity, association.InvokerSid, hash)),
            BackupState.Canceled => new(backup.State, backup.Error, null),
            _ => Refused("efs-backup-wizard", "Windows did not report completion of the EFS private-key export.", backup.Error),
        };

    private static uint OwnedWindowThread(nint window)
    {
        if (window == 0 || !IsWindowVisible(window)) return 0;
        uint thread = GetWindowThreadProcessId(window, out uint process);
        return process == Environment.ProcessId ? thread : 0;
    }

    private static BackupResult Refused(string code, string message, int? error = null) =>
        new(BackupState.Unavailable, error, new(StorageReasonKind.PermissionOrPolicy, code, message, true));

    [LibraryImport("cryptui.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUIWizExport(uint flags, nint parent, string title, ExportInfo* info, nint extra);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
