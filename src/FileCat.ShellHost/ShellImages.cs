using System.Runtime.InteropServices;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Shell;

namespace FileCat.ShellHost;

/// <summary>Thumbnails and per-file icons through IShellItemImageFactory, as premultiplied BGRA pixels.</summary>
internal static unsafe partial class ShellImages
{
    private const int SIIGBF_ICONONLY = 0x04, SIIGBF_THUMBNAILONLY = 0x08;
    private static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    public static ShellImage? Get(string path, ShellImageKind kind, int size, out string? problem)
    {
        problem = null;
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0'))
        {
            problem = "Not a full path.";
            return null;
        }
        int hr = SHCreateItemFromParsingName(path, 0, IID_IShellItemImageFactory, out var factoryPtr);
        if (hr < 0 || factoryPtr == 0)
        {
            problem = $"The Shell does not know this item (0x{hr:X8}).";
            return null;
        }
        nint bitmap = 0;
        try
        {
            var factory = (IShellItemImageFactory)Marshal.GetObjectForIUnknown(factoryPtr);
            try
            {
                hr = factory.GetImage(new SIZE { cx = size, cy = size }, kind == ShellImageKind.Thumbnail ? SIIGBF_THUMBNAILONLY : SIIGBF_ICONONLY, out bitmap);
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
            // No thumbnail handler for the type is an ordinary answer, not a failure.
            if (hr < 0 || bitmap == 0) return null;
            return Pixels(bitmap, out problem);
        }
        finally
        {
            Marshal.Release(factoryPtr);
            if (bitmap != 0) DeleteObject(bitmap);
        }
    }

    /// <summary>An icon from a resource file ("index|file"), premultiplied like every answer of the helper.</summary>
    public static ShellImage? GetResource(string request, int size, out string? problem)
    {
        problem = null;
        if (IconResourceRequest.Parse(request) is not { } location || !Path.IsPathFullyQualified(location.File) || location.File.Contains('\0'))
        {
            problem = "Not an icon location.";
            return null;
        }
        if (!WindowsIcons.TryExtract(location, size, out int w, out int h, out var bgra)) return null;
        WindowsIcons.Premultiply(bgra);
        return new ShellImage(w, h, bgra);
    }

    public static ShellImage? GetOverlayIcon(string path, int size, out string? problem)
    {
        problem = null;
        if (!WindowsIcons.TryShellOverlay(path, size, out int width, out int height, out var bgra)) return null;
        WindowsIcons.Premultiply(bgra);
        return new ShellImage(width, height, bgra);
    }

    private static ShellImage? Pixels(nint bitmap, out string? problem)
    {
        problem = null;
        BITMAP bm;
        if (GetObjectW(bitmap, sizeof(BITMAP), &bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight == 0)
        {
            problem = "The handler returned an unreadable picture.";
            return null;
        }
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        if (w > ShellHostProtocol.MaxPixels || h > ShellHostProtocol.MaxPixels)
        {
            problem = "The handler returned an oversized picture.";
            return null;
        }
        var pixels = new byte[w * h * 4];
        var header = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        nint dc = GetDC(0);
        try
        {
            fixed (byte* p = pixels)
            {
                if (GetDIBits(dc, bitmap, 0, (uint)h, p, &header, 0) == 0)
                {
                    problem = "The picture's pixels could not be read.";
                    return null;
                }
            }
        }
        finally
        {
            ReleaseDC(0, dc);
        }
        // Many handlers leave the alpha channel empty on opaque pictures.
        bool anyAlpha = false;
        for (int i = 3; i < pixels.Length && !anyAlpha; i += 4) anyAlpha = pixels[i] != 0;
        if (!anyAlpha)
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return new ShellImage(w, h, pixels);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out nint phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx, cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string pszPath, nint pbc, in Guid riid, out nint ppv);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint h, int c, void* pv);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint hdc, nint hbm, uint start, uint cLines, void* lpvBits, BITMAPINFOHEADER* lpbmi, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint ho);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hWnd, nint hDC);
}
