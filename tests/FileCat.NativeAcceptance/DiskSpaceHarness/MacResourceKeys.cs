using System.Runtime.InteropServices;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PostResourceKey(nint window, int key);

    private static void PostCocoaResourceKey(nint window, string key)
    {
        nint native = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "filecat-cocoa-keys.dylib"));
        try
        {
            var post = Marshal.GetDelegateForFunctionPointer<PostResourceKey>(NativeLibrary.GetExport(native, "FileCatPostResourceKey"));
            int result = post(window, key == "Return" ? 0 : 1);
            if (result != 0)
                throw new InvalidOperationException($"AppKit rejected the owned window's {key} event with code {result}.");
        }
        finally { NativeLibrary.Free(native); }
    }
}
