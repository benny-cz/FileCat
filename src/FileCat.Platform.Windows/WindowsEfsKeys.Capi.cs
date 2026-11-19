using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsEfsKeys
{
    private const uint ParentedCapiFlags = 0x4 | 0x8 | 0x80; // COMPARE, NO_HEALING, WINDOW_HANDLE
    private sealed record CapiInteraction(nint Owner, int Parent);

    internal static bool CanRequestSetupKeyAccess(StorageEfsKeyEvidence key) =>
        NeedsParentedCngAccess(key) || (NeedsParentedCapiAccess(key) && WindowsEfsCapiWorker.Available);

    private static bool NeedsParentedCapiAccess(StorageEfsKeyEvidence key) =>
        key is { State: StorageEfsKeyState.InteractionRequired, Provider: StorageEfsKeyProvider.Capi,
            CertificatePresent: true, ProviderAssociationPresent: true };

    internal StorageEfsKeyEvidence AccessReviewedCapiKey(StorageEfsAssociationSnapshot association, string certificateSha1,
        nint ownerWindow, bool explicitlyRequested, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(association);
        string sid = association.InvokerSid;
        ValidateRequest(sid, certificateSha1);
        ct.ThrowIfCancellationRequested();
        if (!WindowsStorageTransforms.SupportsHost(qualifyNativeArm64))
            return Unknown("efs-key-host", "CryptoAPI interaction requires a qualified Windows host.");
        if (!explicitlyRequested || OwnedWindowThread(ownerWindow) == 0)
            return Unknown("efs-key-owner", "Request key provider access from a visible FileCat window.");
        if (association.Users.IsDefaultOrEmpty || association.CurrentUserKeys.IsDefaultOrEmpty ||
            !association.Users.Any(user => user.Sid == sid && user.CertificateSha1 == certificateSha1) ||
            association.CurrentUserKeys.FirstOrDefault(key => key.InvokerSid == sid && key.CertificateSha1 == certificateSha1 &&
                NeedsParentedCapiAccess(key)) is not { } reviewed)
            return Unknown("efs-key-review", "Choose a CryptoAPI key that requires access in the current EFS review.");

        RequireInvoker(sid, ct);
        var result = WindowsEfsCapiWorker.Compare(sid, certificateSha1, ownerWindow, ct);
        RequireInvoker(sid, ct);
        if (OwnedWindowThread(ownerWindow) == 0)
            return Unknown("efs-key-owner", "The reviewed FileCat window closed before key access was confirmed.");
        if (result is { State: StorageEfsKeyState.MatchedAvailable, NativeError: 0 })
            return Evidence(sid, certificateSha1, true, true, StorageEfsKeyState.MatchedAvailable, StorageEfsKeyProvider.Capi, null, null, null)
                with { Subject = reviewed.Subject };
        if (result is { State: StorageEfsKeyState.Unavailable, NativeError: not 0 } refused)
            return Error(sid, certificateSha1, null, null, refused.NativeError, "efs-private-key",
                "Windows could not compare the reviewed CryptoAPI key.", StorageEfsKeyProvider.Capi);
        return Unknown("efs-capi-worker", "The isolated CryptoAPI comparison did not establish access to the reviewed key.");

        StorageEfsKeyEvidence Unknown(string code, string message) =>
            Evidence(sid, certificateSha1, null, null, StorageEfsKeyState.Unknown, StorageEfsKeyProvider.Capi, null, code, message);
    }

    internal StorageEfsKeyEvidence InspectCapiInWorker(string sid, string hash, nint owner, int parent, CancellationToken ct)
    {
        if (!ExternalCapiOwner(owner, parent))
            return Evidence(sid, hash, null, null, StorageEfsKeyState.Unknown, StorageEfsKeyProvider.Capi, null,
                "efs-key-owner", "The live FileCat window could not be verified.");
        return InspectAssociatedCertificate(sid, hash, ct, useMatchedCertificate: null, capi: new(owner, parent));
    }

    private static StorageEfsKeyEvidence CompareIsolatedCapiKey(nint fresh, string sid, string hash,
        CapiInteraction window, uint expectedKeySpec, CancellationToken ct)
    {
        if (!ExternalCapiOwner(window.Owner, window.Parent))
            return Unknown("efs-key-owner", "The live FileCat window could not be verified.");

        // PP_CLIENT_HWND belongs to this one-request child process. Never set it in the application process.
        nint parent = window.Owner, key = 0;
        uint keySpec = 0;
        bool callerFree = false, acquired = false, reset, released;
        int error = 0;
        try
        {
            acquired = Acquire(fresh, ParentedCapiFlags, (nint)(&parent), out key, out keySpec, out callerFree);
            if (!acquired) error = Marshal.GetLastPInvokeError();
        }
        finally
        {
            nint cleared = 0;
            reset = SetCapiClientWindow(0, 1 /* PP_CLIENT_HWND */, &cleared, 0);
            released = !acquired || !callerFree || key == 0 ||
                (keySpec == uint.MaxValue ? FreeCng(key) == 0 : ReleaseCapi(key, 0));
        }
        RequireInvoker(sid, ct);
        if (!reset || !released)
            return Unknown("efs-key-window-cleanup", "Windows did not confirm cleanup of the CryptoAPI key or its parent window.");
        if (!ExternalCapiOwner(window.Owner, window.Parent))
            return Unknown("efs-key-owner", "The live FileCat window changed during key access.");
        if (!acquired)
            return Error(sid, hash, true, true, error, "efs-private-key", "Windows could not acquire and compare this private key.",
                StorageEfsKeyProvider.Capi);
        if (!callerFree || key == 0 || keySpec != expectedKeySpec)
            return Unknown("efs-key-comparison-unestablished", "A fresh owned CryptoAPI key comparison could not be established.");
        return Evidence(sid, hash, true, true, StorageEfsKeyState.MatchedAvailable, StorageEfsKeyProvider.Capi, null, null, null);

        StorageEfsKeyEvidence Unknown(string code, string message) =>
            Evidence(sid, hash, true, true, StorageEfsKeyState.Unknown, StorageEfsKeyProvider.Capi, null, code, message);
    }

    private static bool ExternalCapiOwner(nint window, int parent) =>
        parent > 0 && parent != Environment.ProcessId && window != 0 && IsWindowVisible(window) &&
        GetWindowThreadProcessId(window, out uint process) != 0 && process == (uint)parent;

    [LibraryImport("advapi32.dll", EntryPoint = "CryptSetProvParam", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCapiClientWindow(nint provider, uint param, nint* value, uint flags);
}
