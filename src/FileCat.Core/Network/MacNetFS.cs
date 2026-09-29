using System.Runtime.InteropServices;
using System.Text;

namespace FileCat.Core.Network;

/// <summary>
/// Mounts an SMB share on macOS the way Finder's Connect to Server does (D-54): NetFS, which asks for a user name and
/// password in the system's own dialog when the server wants one, and remembers it in the keychain if the user says
/// so. The share appears under /Volumes.
/// </summary>
public static class MacNetFS
{
    private const string NetFS = "/System/Library/Frameworks/NetFS.framework/NetFS";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8 = 0x08000100;
    private const int AlreadyMounted = 17, UserCanceled = -128;

    [DllImport(NetFS)]
    private static extern int NetFSMountURLSync(nint url, nint mountPath, nint user, nint password, nint openOptions, nint mountOptions, out nint mountPoints);

    [DllImport(CoreFoundation)] private static extern nint CFStringCreateWithCString(nint allocator, byte[] text, uint encoding);
    [DllImport(CoreFoundation)] private static extern nint CFURLCreateWithString(nint allocator, nint text, nint baseUrl);
    [DllImport(CoreFoundation)] private static extern nint CFDictionaryCreateMutable(nint allocator, nint capacity, nint keyCallbacks, nint valueCallbacks);
    [DllImport(CoreFoundation)] private static extern void CFDictionarySetValue(nint dictionary, nint key, nint value);
    [DllImport(CoreFoundation)] private static extern nint CFArrayGetCount(nint array);
    [DllImport(CoreFoundation)] private static extern nint CFArrayGetValueAtIndex(nint array, nint index);
    [DllImport(CoreFoundation)] private static extern byte CFStringGetCString(nint text, byte[] buffer, nint size, uint encoding);
    [DllImport(CoreFoundation)] private static extern void CFRelease(nint value);

    private static nint String(string text) => CFStringCreateWithCString(0, Encoding.UTF8.GetBytes(text + "\0"), Utf8);

    /// <summary>
    /// Mounts smb://server/share (the system asks for sign-in if needed) and gives its mount point. Blocks while the
    /// dialog is up: call it off the UI thread.
    /// </summary>
    public static string Mount(string server, string share)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        nint urlText = String($"smb://{server}/{Uri.EscapeDataString(share)}");
        nint url = CFURLCreateWithString(0, urlText, 0);
        nint callbacks = NativeLibrary.Load(CoreFoundation);
        nint options = CFDictionaryCreateMutable(0, 0, NativeLibrary.GetExport(callbacks, "kCFTypeDictionaryKeyCallBacks"), NativeLibrary.GetExport(callbacks, "kCFTypeDictionaryValueCallBacks"));
        nint uiKey = String("UIOption"), allowUi = String("AllowUI");
        try
        {
            CFDictionarySetValue(options, uiKey, allowUi);
            int status = NetFSMountURLSync(url, 0, 0, 0, options, 0, out nint points);
            try
            {
                if (status == 0 && points != 0 && CFArrayGetCount(points) > 0)
                {
                    var buffer = new byte[4096];
                    if (CFStringGetCString(CFArrayGetValueAtIndex(points, 0), buffer, buffer.Length, Utf8) != 0)
                        return Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0));
                }
                if (status == AlreadyMounted && SmbTools.FindMount(server, share) is { } mounted) return mounted;
                if (status == UserCanceled) throw new OperationCanceledException("Signing in was canceled.");
                throw status switch
                {
                    13 or 80 or -5045 => new SmbSignInRequiredException($"{server} did not accept the user name and password."),
                    2 or 64 or 65 or -5023 => new DirectoryNotFoundException($"{server} is not reachable, or it has no share “{share}”."),
                    _ => new IOException($"macOS could not mount “{share}” on {server} (error {status})."),
                };
            }
            finally { if (points != 0) CFRelease(points); }
        }
        finally
        {
            CFRelease(uiKey);
            CFRelease(allowUi);
            CFRelease(options);
            if (url != 0) CFRelease(url);
            CFRelease(urlText);
        }
    }
}
