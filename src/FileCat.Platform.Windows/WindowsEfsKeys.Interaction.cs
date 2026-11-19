using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsEfsKeys
{
    private const uint ParentedCngFlags = 0x4 | 0x8 | 0x80 | 0x40000; // COMPARE, NO_HEALING, WINDOW_HANDLE, ONLY_NCRYPT
    private const string KeyWindowProperty = "HWND Handle";
    private sealed record CngInteraction(nint Owner, Action<Action>? InvokeOwner);

    internal static bool NeedsParentedCngAccess(StorageEfsKeyEvidence key) =>
        key is { State: StorageEfsKeyState.InteractionRequired, Provider: StorageEfsKeyProvider.Cng,
            CertificatePresent: true, ProviderAssociationPresent: true };

    internal StorageEfsKeyEvidence AccessReviewedCngKey(StorageEfsAssociationSnapshot association, string certificateSha1,
        nint ownerWindow, bool explicitlyRequested, CancellationToken ct, Action<Action>? invokeOwner)
    {
        ArgumentNullException.ThrowIfNull(association);
        ValidateRequest(association.InvokerSid, certificateSha1);
        ct.ThrowIfCancellationRequested();
        if (!WindowsStorageTransforms.SupportsHost(qualifyNativeArm64))
            return Evidence(association.InvokerSid, certificateSha1, null, null, StorageEfsKeyState.Unsupported,
                StorageEfsKeyProvider.Unknown, null, "efs-key-host", "Key provider interaction requires a qualified Windows host.");
        if (!explicitlyRequested || OwnedWindowThread(ownerWindow) == 0)
            return Evidence(association.InvokerSid, certificateSha1, null, null, StorageEfsKeyState.InteractionRequired,
                StorageEfsKeyProvider.Unknown, null, "efs-key-owner", "Request key provider access from a visible FileCat window.");

        string sid = association.InvokerSid;
        if (association.Users.IsDefaultOrEmpty || association.CurrentUserKeys.IsDefaultOrEmpty ||
            !association.Users.Any(user => user.Sid == sid && user.CertificateSha1 == certificateSha1) ||
            !association.CurrentUserKeys.Any(key => key.InvokerSid == sid && key.CertificateSha1 == certificateSha1 &&
                NeedsParentedCngAccess(key)))
            return Evidence(sid, certificateSha1, null, null, StorageEfsKeyState.Unknown, StorageEfsKeyProvider.Unknown, null,
                "efs-key-review", "Choose a key that requires provider access in the current EFS review.");

        return InspectAssociatedCertificate(sid, certificateSha1, ct, useMatchedCertificate: null,
            interaction: new(ownerWindow, invokeOwner));
    }

    internal static StorageEfsKeyEvidence ProbeParentedCngCertificate(nint certificate, string invokerSid, string certificateSha1,
        nint ownerWindow, bool explicitlyRequested, CancellationToken ct, Action<Action>? invokeOwner = null)
    {
        ValidateRequest(invokerSid, certificateSha1);
        ct.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return Evidence(invokerSid, certificateSha1, null, null, StorageEfsKeyState.Unsupported,
                StorageEfsKeyProvider.Unknown, null, "efs-key-host", "Key provider interaction requires Windows.");
        if (!explicitlyRequested || OwnedWindowThread(ownerWindow) == 0)
            return Evidence(invokerSid, certificateSha1, null, null, StorageEfsKeyState.InteractionRequired,
                StorageEfsKeyProvider.Unknown, null, "efs-key-owner", "Request key provider access from a visible FileCat window.");

        return ProbeHeldCertificate(certificate, invokerSid, certificateSha1, ct, useMatchedCertificate: null,
            interaction: new(ownerWindow, invokeOwner));
    }

    private static StorageEfsKeyEvidence CompareParentedCngKey(nint fresh, string sid, string hash, CngInteraction interaction,
        CancellationToken ct)
    {
        var lifetime = new object();
        bool active = true, entered = false;
        StorageEfsKeyEvidence? result = null;
        // The worker retains the fresh certificate and both scratch slots until this synchronous action returns.
        try
        {
            if (interaction.InvokeOwner is { } invokeOwner) invokeOwner(CompareOnce);
            else CompareOnce();
        }
        finally { lock (lifetime) active = false; }
        CheckInvoker(sid, ct);
        return result ?? Unknown("efs-key-owner", "The FileCat window did not run the requested key comparison.");

        void CompareOnce()
        {
            lock (lifetime)
            {
                if (!active || entered) return;
                entered = true;
                Compare();
            }
        }

        void Compare()
        {
            ct.ThrowIfCancellationRequested();
            if (OwnedWindowThread(interaction.Owner) != GetCurrentThreadId())
            {
                result = Unknown("efs-key-owner", "The key provider requires a live FileCat window on its owning thread.");
                return;
            }
            CheckInvoker(sid, ct);
            nint parent = interaction.Owner;
            bool acquired = Acquire(fresh, ParentedCngFlags, (nint)(&parent), out nint key, out uint spec, out bool callerFree);
            int error = acquired ? 0 : Marshal.GetLastPInvokeError();
            try
            {
                CheckInvoker(sid, ct);
                if (!acquired)
                {
                    result = Error(sid, hash, true, true, error, "efs-private-key", "Windows could not acquire and compare this private key.",
                        StorageEfsKeyProvider.Cng);
                    return;
                }
                if (!callerFree || key == 0 || spec != uint.MaxValue)
                {
                    result = Unknown("efs-key-comparison-unestablished", "A fresh owned CNG comparison could not be established.");
                    return;
                }

                nint actual = 0;
                int status = ReadCngProperty(key, KeyWindowProperty, &actual, sizeof(nint), out int bytes, 0);
                if (status != 0 || bytes != sizeof(nint) || actual != interaction.Owner ||
                    OwnedWindowThread(interaction.Owner) != GetCurrentThreadId())
                {
                    result = Unknown("efs-key-window-unestablished", "Windows did not establish the live owner window on the compared key.",
                        status == 0 ? null : status);
                    return;
                }
                result = Evidence(sid, hash, true, true, StorageEfsKeyState.MatchedAvailable, StorageEfsKeyProvider.Cng, null, null, null)
                    with { Subject = CertificateSubject(fresh), BackupProblem = ReadCngBackupProblem(key, ct) };
            }
            finally
            {
                if (acquired && callerFree && key != 0)
                {
                    int failure;
                    if (spec == uint.MaxValue)
                    {
                        nint cleared = 0;
                        int reset = SetKeyWindow(key, KeyWindowProperty, &cleared, sizeof(nint), 0);
                        int release = FreeCng(key);
                        failure = reset != 0 ? reset : release;
                    }
                    else failure = ReleaseCapi(key, 0) ? 0 : Marshal.GetLastPInvokeError();
                    if (failure != 0)
                        result = Unknown("efs-key-window-cleanup", "Windows did not confirm cleanup of the compared key's window or handle.", failure);
                }
            }
        }

        StorageEfsKeyEvidence Unknown(string code, string message, int? error = null) =>
            Evidence(sid, hash, true, true, StorageEfsKeyState.Unknown, StorageEfsKeyProvider.Cng, error, code, message);
    }

    private static StorageProblem? ReadCngBackupProblem(nint key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        uint policy = 0;
        int status = ReadCngProperty(key, "Export Policy", &policy, sizeof(uint), out int bytes, 0x40 /* NCRYPT_SILENT_FLAG */);
        ct.ThrowIfCancellationRequested();
        return status == 0 && bytes == sizeof(uint) && policy == 0
            ? new(StorageReasonKind.PermissionOrPolicy, "efs-key-nonexportable", "Windows marks this EFS private key as non-exportable.", true)
            : null;
    }

    [LibraryImport("ncrypt.dll", EntryPoint = "NCryptGetProperty", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int ReadCngProperty(nint key, string property, void* data, int size, out int bytes, uint flags);
    [LibraryImport("ncrypt.dll", EntryPoint = "NCryptSetProperty", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetKeyWindow(nint key, string property, nint* data, int size, uint flags);
}
