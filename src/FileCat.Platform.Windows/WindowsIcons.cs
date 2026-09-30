using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace FileCat.Platform.Windows;

/// <summary>An icon inside a resource file: a DLL, EXE, or ICO path and an index (negative: a resource ID).</summary>
public readonly record struct IconLocation(string File, int Index)
{
    /// <summary>"%SystemRoot%\system32\imageres.dll,-112" (the form shortcuts, desktop.ini, and the Registry use).</summary>
    public static IconLocation? Parse(string? text, int defaultIndex = 0)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().Trim('"');
        int comma = text.LastIndexOf(',');
        int index = defaultIndex;
        if (comma > 0 && int.TryParse(text.AsSpan(comma + 1).Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
        {
            index = parsed;
            text = text[..comma].Trim().Trim('"');
        }
        if (text.Length == 0 || text.Contains('\0') || text.Contains('|')) return null;
        return new IconLocation(Environment.ExpandEnvironmentVariables(text), index);
    }

    public override string ToString() => File + "," + Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Windows icons at the exact pixel size a display needs (crisp at any scale): extraction from resource files, the
/// Shell's stock icons (drives, the shortcut arrow), type icons by extension, and the known folders' icons as the
/// Registry defines them. Only locations FileCat trusts are extracted here; ones named inside user files (shortcuts,
/// desktop.ini) go through the restricted Shell helper.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class WindowsIcons
{
    // SHSTOCKICONID values (shellapi.h).
    public const int StockDocumentNoAssociation = 0, StockApplication = 2, StockFolder = 3, StockRemovableDrive = 7, StockFixedDrive = 8, StockNetworkDrive = 9, StockNetworkDriveOffline = 10,
        StockOpticalDrive = 11, StockRamDrive = 12, StockServer = 15, StockLink = 29, StockServerShare = 51, StockStack = 55, StockUnknownDrive = 58, StockDesktopPc = 94, StockPhone = 99, StockNetwork = 17;

    /// <summary>Extracts one icon at <paramref name="size"/> pixels as straight (not premultiplied) BGRA.</summary>
    public static bool TryExtract(IconLocation location, int size, out int width, out int height, out byte[] bgra)
    {
        width = height = 0;
        bgra = [];
        nint icon = 0;
        uint id = 0;
        uint count = PrivateExtractIconsW(location.File, location.Index, size, size, &icon, &id, 1, 0);
        if (count == 0 || count == uint.MaxValue || icon == 0) return false;
        try
        {
            return Pixels(icon, out width, out height, out bgra);
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    /// <summary>
    /// The icon only when Windows assigned an installed overlay to this real item. Run from the restricted Shell
    /// helper: third-party overlay handlers execute during SHGetFileInfo.
    /// </summary>
    public static bool TryShellOverlay(string path, int size, out int width, out int height, out byte[] bgra)
    {
        width = height = 0;
        bgra = [];
        if (!IsLocal(path) || path.Contains('\0')) return false;
        var info = new SHFILEINFOW();
        const uint iconFlag = 0x100, smallFlag = 0x1, addOverlays = 0x20, overlayIndex = 0x40;
        uint flags = iconFlag | addOverlays | overlayIndex | (size <= 24 ? smallFlag : 0);
        if (SHGetFileInfoW(path, 0, &info, (uint)sizeof(SHFILEINFOW), flags) == 0 || info.hIcon == 0) return false;
        try
        {
            if (((uint)info.iIcon >> 24) == 0) return false;
            return Pixels(info.hIcon, out width, out height, out bgra);
        }
        finally { DestroyIcon(info.hIcon); }
    }

    /// <summary>Where the Shell keeps a stock icon (drives, the shortcut arrow), without reading any file.</summary>
    public static IconLocation? StockLocation(int stockId)
    {
        var info = new SHSTOCKICONINFO { cbSize = (uint)sizeof(SHSTOCKICONINFO) };
        if (SHGetStockIconInfo(stockId, SHGSI_ICONLOCATION, &info) < 0) return null;
        string path = new string(info.szPath);
        return path.Length == 0 ? null : new IconLocation(path, info.iIcon);
    }

    /// <summary>
    /// Where a type's icon lives, by extension alone (SHGFI_USEFILEATTRIBUTES: the file is never read). Null for types
    /// whose icon depends on each file, and for locations off this computer.
    /// </summary>
    public static IconLocation? TypeLocation(string name, bool isDirectory)
    {
        var info = new SHFILEINFOW();
        uint attributes = isDirectory ? 0x10u : 0x80u;
        if (SHGetFileInfoW(name, attributes, &info, (uint)sizeof(SHFILEINFOW), SHGFI_ICONLOCATION | SHGFI_USEFILEATTRIBUTES) == 0) return null;
        string path = new string(info.szDisplayName);
        if (path.Length == 0 || path.StartsWith('*') || !IsLocal(path)) return null;
        return new IconLocation(Environment.ExpandEnvironmentVariables(path), info.iIcon);
    }

    /// <summary>A path on this computer's fixed drives (never a share, whose contact would reveal credentials).</summary>
    public static bool IsLocal(string path) =>
        Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal) && !Shell.ShellPreviewPolicy.IsNetworkOrRemovable(path);

    /// <summary>
    /// The known folders' icons as Windows defines them (HKLM\...\Explorer\FolderDescriptions), with each folder's
    /// current path where it has one: Documents, Downloads, Desktop, Music, Pictures, Videos, OneDrive, and the others,
    /// wherever they were moved to.
    /// </summary>
    public static List<(Guid Id, string? Path, IconLocation Icon)> KnownFolders()
    {
        var list = new List<(Guid, string?, IconLocation)>();
        try
        {
            using var descriptions = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FolderDescriptions");
            if (descriptions is null) return list;
            foreach (var name in descriptions.GetSubKeyNames())
            {
                if (!Guid.TryParse(name, out var id)) continue;
                using var folder = descriptions.OpenSubKey(name);
                if (IconLocation.Parse(folder?.GetValue("Icon") as string) is not { } icon || !IsLocal(icon.File)) continue;
                list.Add((id, KnownFolderPath(id)?.TrimEnd('\\'), icon));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return list;
    }

    private static string? KnownFolderPath(Guid id)
    {
        const uint DontVerify = 0x4000;
        if (SHGetKnownFolderPath(&id, DontVerify, 0, out nint path) < 0 || path == 0) return null;
        try
        {
            return Marshal.PtrToStringUni(path);
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    /// <summary>An icon's pixels as straight BGRA; icons without an alpha channel take it from their mask.</summary>
    public static bool Pixels(nint icon, out int width, out int height, out byte[] bgra)
    {
        width = height = 0;
        bgra = [];
        if (!GetIconInfo(icon, out var info)) return false;
        try
        {
            if (info.hbmColor == 0) return false;
            BITMAP bm;
            if (GetObjectW(info.hbmColor, sizeof(BITMAP), &bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight <= 0 || bm.bmWidth > 1024 || bm.bmHeight > 1024) return false;
            int w = bm.bmWidth, h = bm.bmHeight;
            var pixels = new byte[w * h * 4];
            var header = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            nint dc = GetDC(0);
            try
            {
                fixed (byte* p = pixels)
                {
                    if (GetDIBits(dc, info.hbmColor, 0, (uint)h, p, &header, 0) == 0) return false;
                }
                bool hasAlpha = false;
                for (int i = 3; i < pixels.Length && !hasAlpha; i += 4) hasAlpha = pixels[i] != 0;
                if (!hasAlpha && info.hbmMask != 0)
                {
                    var mask = new byte[w * h * 4];
                    var maskHeader = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                    fixed (byte* m = mask)
                    {
                        GetDIBits(dc, info.hbmMask, 0, (uint)h, m, &maskHeader, 0);
                    }
                    for (int i = 0; i < w * h; i++) pixels[i * 4 + 3] = mask[i * 4] == 0 ? (byte)255 : (byte)0;
                }
            }
            finally
            {
                ReleaseDC(0, dc);
            }
            width = w;
            height = h;
            bgra = pixels;
            return true;
        }
        finally
        {
            if (info.hbmColor != 0) DeleteObject(info.hbmColor);
            if (info.hbmMask != 0) DeleteObject(info.hbmMask);
        }
    }

    /// <summary>Straight BGRA to premultiplied, in place.</summary>
    public static void Premultiply(byte[] bgra)
    {
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            int a = bgra[i + 3];
            if (a == 255) continue;
            bgra[i] = (byte)(bgra[i] * a / 255);
            bgra[i + 1] = (byte)(bgra[i + 1] * a / 255);
            bgra[i + 2] = (byte)(bgra[i + 2] * a / 255);
        }
    }

    private const uint SHGSI_ICONLOCATION = 0, SHGFI_ICONLOCATION = 0x1000, SHGFI_USEFILEATTRIBUTES = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public nint hIcon;
        public int iSysImageIndex;
        public int iIcon;
        public fixed char szPath[260];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEINFOW
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed char szDisplayName[260];
        public fixed char szTypeName[80];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public int xHotspot, yHotspot;
        public nint hbmMask, hbmColor;
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

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PrivateExtractIconsW(string file, int index, int cx, int cy, nint* icons, uint* ids, uint count, uint flags);

    [LibraryImport("shell32.dll")]
    private static partial int SHGetStockIconInfo(int siid, uint flags, SHSTOCKICONINFO* info);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SHGetFileInfoW(string path, uint attributes, SHFILEINFOW* info, uint size, uint flags);

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(Guid* id, uint flags, nint token, out nint path);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(nint icon, out ICONINFO info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint h, int c, void* pv);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint hdc, nint hbm, uint start, uint lines, void* bits, BITMAPINFOHEADER* header, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint ho);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hWnd, nint hDC);
}
