using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsEfsKeys
{
    internal enum BackupState { WindowsReportedComplete, Canceled, Unavailable }
    internal sealed record BackupResult(BackupState State, int? NativeError, StorageProblem? Problem, StorageEfsBackupCompletion? Completion = null);

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
        nint ownerWindow, bool explicitlyRequested, CancellationToken ct)
    {
        if (!explicitlyRequested)
            return Refused("efs-backup-choice", "Request backup explicitly for the selected file-associated EFS key.");
        ArgumentNullException.ThrowIfNull(association);
        ValidateRequest(association.InvokerSid, certificateSha1);
        ct.ThrowIfCancellationRequested();
        if (!WindowsStorageTransforms.IsQualifiedHost)
            return Refused("efs-backup-host", "The EFS backup wizard requires its qualified Windows host.");
        if (association.Users.IsDefaultOrEmpty || !association.Users.Any(user => user.Sid == association.InvokerSid && user.CertificateSha1 == certificateSha1))
            return Refused("efs-backup-association", "The selected certificate is outside the invoking user's observed EFS associations.");
        if (!IsOwnedWindow(ownerWindow))
            return Refused("efs-backup-owner", "Open the EFS backup wizard from a visible FileCat window.");

        BackupResult? result = null;
        var evidence = InspectAssociatedCertificate(association.InvokerSid, certificateSha1, ct, fresh =>
        {
            ct.ThrowIfCancellationRequested();
            if (!IsOwnedWindow(ownerWindow))
            {
                result = Refused("efs-backup-owner", "The FileCat window closed before the EFS backup wizard could open.");
                return;
            }

            var export = new ExportInfo { Size = (uint)sizeof(ExportInfo), SubjectChoice = 1 /* CERT_CONTEXT */, Certificate = fresh };
            // The parented Windows wizard collects the destination and password. Private key export is mandatory and key deletion is disabled.
            const uint exportPrivateKey = 0x0100, noDeletePrivateKey = 0x0200;
            bool complete = CryptUIWizExport(exportPrivateKey | noDeletePrivateKey, ownerWindow, "Back up EFS key", &export, 0);
            int error = complete ? 0 : Marshal.GetLastPInvokeError();
            result = complete ? new(BackupState.WindowsReportedComplete, null, null, new(association.Identity, association.InvokerSid, certificateSha1))
                : error == 1223 /* ERROR_CANCELLED */
                ? new(BackupState.Canceled, error, null)
                : Refused("efs-backup-wizard", "Windows did not report completion of the EFS private-key export.", error);
        });
        return result ?? new(BackupState.Unavailable, evidence.NativeError, evidence.Problem);
    }

    private static bool IsOwnedWindow(nint window) => window != 0 && IsWindowVisible(window) &&
        GetWindowThreadProcessId(window, out uint process) != 0 && process == Environment.ProcessId;

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
}
