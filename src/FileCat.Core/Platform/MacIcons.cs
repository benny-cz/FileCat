using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FileCat.Core.Platform;

/// <summary>
/// Icons as Finder shows them, through NSWorkspace: a type's icon by extension, and by path for folders (the home,
/// Desktop, Documents, Applications, custom folder icons), application bundles, and volumes. Drawn by Core Graphics at
/// the exact pixel size, as premultiplied BGRA. Calls are wrapped in their own autorelease pool, so a background thread
/// may use them.
/// </summary>
[SupportedOSPlatform("macos")]
public static unsafe class MacIcons
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private static readonly Lazy<nint> Workspace = new(() =>
    {
        // AppKit is loaded in the app; tests and tools load it here.
        NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out _);
        nint cls = objc_getClass("NSWorkspace");
        return cls == 0 ? 0 : objc_msgSend(cls, sel_registerName("sharedWorkspace"));
    });

    /// <summary>The icon Finder shows for files with this extension ("" for a plain file).</summary>
    public static bool TryTypeIcon(string extension, int size, out int width, out int height, out byte[] bgra) =>
        Draw(workspace => objc_msgSend(workspace, sel_registerName("iconForFileType:"), NSString(extension)), size, out width, out height, out bgra);

    /// <summary>The icon Finder shows for this item itself: special and customized folders, applications, volumes.</summary>
    public static bool TryPathIcon(string path, int size, out int width, out int height, out byte[] bgra) =>
        Draw(workspace => objc_msgSend(workspace, sel_registerName("iconForFile:"), NSString(path)), size, out width, out height, out bgra);

    private static bool Draw(Func<nint, nint> image, int size, out int width, out int height, out byte[] bgra)
    {
        width = height = 0;
        bgra = [];
        if (size is < 1 or > 512 || Workspace.Value == 0) return false;
        nint pool = objc_autoreleasePoolPush();
        try
        {
            nint nsImage = image(Workspace.Value);
            if (nsImage == 0) return false;
            var rect = new CGRect(0, 0, size, size);
            nint cgImage = objc_msgSend_rect(nsImage, sel_registerName("CGImageForProposedRect:context:hints:"), &rect, 0, 0);
            if (cgImage == 0) return false;
            nint space = CGColorSpaceCreateDeviceRGB();
            // 32-bit little-endian with premultiplied alpha first: BGRA in memory.
            nint context = CGBitmapContextCreate(0, size, size, 8, size * 4, space, 0x2 | 0x2000);
            CGColorSpaceRelease(space);
            if (context == 0) return false;
            try
            {
                CGContextDrawImage(context, new CGRect(0, 0, size, size), cgImage);
                byte* data = (byte*)CGBitmapContextGetData(context);
                if (data == null) return false;
                var pixels = new byte[size * size * 4];
                new ReadOnlySpan<byte>(data, pixels.Length).CopyTo(pixels);
                (width, height, bgra) = (size, size, pixels);
                return true;
            }
            finally
            {
                CGContextRelease(context);
            }
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private static nint NSString(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\0");
        fixed (byte* p = bytes)
            return objc_msgSend(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), (nint)p);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGRect(double x, double y, double width, double height)
    {
        public readonly double X = x, Y = y, Width = width, Height = height;
    }

    [DllImport(ObjC)]
    private static extern nint objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC)]
    private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend(nint receiver, nint selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend(nint receiver, nint selector, nint argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_rect(nint receiver, nint selector, CGRect* rect, nint context, nint hints);

    [DllImport(ObjC)]
    private static extern nint objc_autoreleasePoolPush();

    [DllImport(ObjC)]
    private static extern void objc_autoreleasePoolPop(nint pool);

    [DllImport(CoreGraphics)]
    private static extern nint CGColorSpaceCreateDeviceRGB();

    [DllImport(CoreGraphics)]
    private static extern void CGColorSpaceRelease(nint space);

    [DllImport(CoreGraphics)]
    private static extern nint CGBitmapContextCreate(nint data, nint width, nint height, nint bitsPerComponent, nint bytesPerRow, nint space, uint bitmapInfo);

    [DllImport(CoreGraphics)]
    private static extern void CGContextDrawImage(nint context, CGRect rect, nint image);

    [DllImport(CoreGraphics)]
    private static extern nint CGBitmapContextGetData(nint context);

    [DllImport(CoreGraphics)]
    private static extern void CGContextRelease(nint context);
}
