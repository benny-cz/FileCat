using System.Runtime.InteropServices;

namespace FileCat.Platform.Windows.Native;

/// <summary>
/// Win32 interop. Structures use explicit sequential layouts that are identical on x64 and ARM64
/// (pointer-sized fields are nint), keeping the later ARM64 port a packaging task (plan §19.3).
/// </summary>
internal static unsafe partial class NativeMethods
{
    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_TYPENAME = 0x000000400;
    public const uint SHGFI_SMALLICON = 0x000000001;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFOW
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed char szDisplayName[260];
        public fixed char szTypeName[80];
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint hIcon);

    /// <summary>A token's information of one class (TokenElevationType is 18).</summary>
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(nint token, int informationClass, void* information, int length, out int returned);

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public int fIcon;
        public int xHotspot;
        public int yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetIconInfo(nint hIcon, out ICONINFO piconinfo);

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    public static partial int GetObject(nint h, int c, void* pv);

    [LibraryImport("gdi32.dll")]
    public static partial int GetDIBits(nint hdc, nint hbm, uint start, uint cLines, void* lpvBits, BITMAPINFOHEADER* lpbmi, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint ho);

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hWnd, nint hDC);

    // ---- ShellExecuteEx ---------------------------------------------------------------------------------

    public const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    public const uint SEE_MASK_NOASYNC = 0x00000100;
    public const uint SEE_MASK_FLAG_NO_UI = 0x00000400;

    [StructLayout(LayoutKind.Sequential)]
    public struct SHELLEXECUTEINFOW
    {
        public int cbSize;
        public uint fMask;
        public nint hwnd;
        public nint lpVerb;
        public nint lpFile;
        public nint lpParameters;
        public nint lpDirectory;
        public int nShow;
        public nint hInstApp;
        public nint lpIDList;
        public nint lpClass;
        public nint hkeyClass;
        public uint dwHotKey;
        public nint hIconOrMonitor;
        public nint hProcess;
    }

    [LibraryImport("shell32.dll", EntryPoint = "ShellExecuteExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShellExecuteEx(ref SHELLEXECUTEINFOW lpExecInfo);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHParseDisplayName(string pszName, nint pbc, out nint ppidl, uint sfgaoIn, out uint psfgaoOut);

    [LibraryImport("shell32.dll")]
    public static partial int SHOpenFolderAndSelectItems(nint pidlFolder, uint cidl, nint* apidl, uint dwFlags);

    [LibraryImport("shell32.dll")]
    public static partial nint ILFindLastID(nint pidl);

    [LibraryImport("ole32.dll")]
    public static partial void CoTaskMemFree(nint pv);

    // ---- Power ------------------------------------------------------------------------------------------

    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_SYSTEM_REQUIRED = 0x00000001;

    [LibraryImport("kernel32.dll")]
    public static partial uint SetThreadExecutionState(uint esFlags);

    // ---- Volumes ----------------------------------------------------------------------------------------

    public const uint DRIVE_UNKNOWN = 0, DRIVE_NO_ROOT_DIR = 1, DRIVE_REMOVABLE = 2, DRIVE_FIXED = 3, DRIVE_REMOTE = 4, DRIVE_CDROM = 5, DRIVE_RAMDISK = 6;

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetDriveType(string lpRootPathName);

    public const uint FILE_CASE_SENSITIVE_SEARCH = 0x00000001;
    public const uint FILE_NAMED_STREAMS = 0x00040000;
    public const uint FILE_SUPPORTS_HARD_LINKS = 0x00400000;
    public const uint FILE_SUPPORTS_REPARSE_POINTS = 0x00000080;
    public const uint FILE_SUPPORTS_BLOCK_REFCOUNTING = 0x08000000;

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumeInformation(string lpRootPathName, char* lpVolumeNameBuffer, int nVolumeNameSize,
        out uint lpVolumeSerialNumber, out uint lpMaximumComponentLength, out uint lpFileSystemFlags, char* lpFileSystemNameBuffer, int nFileSystemNameSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumePathName(string lpszFileName, char* lpszVolumePathName, int cchBufferLength);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetDiskFreeSpaceEx(string lpDirectoryName, out ulong lpFreeBytesAvailable, out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);

    // ---- Session end ----------------------------------------------------------------------------------------

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShutdownBlockReasonCreate(nint hWnd, string pwszReason);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShutdownBlockReasonDestroy(nint hWnd);

    // ---- Networking ---------------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct SHARE_INFO_1
    {
        public nint shi1_netname;
        public uint shi1_type;
        public nint shi1_remark;
    }

    public const uint STYPE_DISKTREE = 0, STYPE_PRINTQ = 1, STYPE_DEVICE = 2, STYPE_IPC = 3, STYPE_SPECIAL = 0x80000000;
    public const int MAX_PREFERRED_LENGTH = -1;
    public const int NERR_Success = 0;
    public const int ERROR_MORE_DATA = 234;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_LOGON_FAILURE = 1326;
    public const int ERROR_BAD_NETPATH = 53;
    public const int ERROR_CANCELLED = 1223;

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetShareEnum(string servername, int level, out nint bufptr, int prefmaxlen, out int entriesread, out int totalentries, ref int resume_handle);

    [LibraryImport("netapi32.dll")]
    public static partial int NetApiBufferFree(nint buffer);

    public const uint RESOURCETYPE_DISK = 1;
    public const uint CONNECT_INTERACTIVE = 0x08;
    public const uint CONNECT_PROMPT = 0x10;
    public const uint CONNECT_UPDATE_PROFILE = 0x01;
    public const uint CONNECT_TEMPORARY = 0x04;

    [StructLayout(LayoutKind.Sequential)]
    public struct NETRESOURCEW
    {
        public uint dwScope;
        public uint dwType;
        public uint dwDisplayType;
        public uint dwUsage;
        public nint lpLocalName;
        public nint lpRemoteName;
        public nint lpComment;
        public nint lpProvider;
    }

    [LibraryImport("mpr.dll", EntryPoint = "WNetAddConnection3W")]
    public static partial int WNetAddConnection3(nint hwndOwner, ref NETRESOURCEW lpNetResource, nint lpPassword, nint lpUserName, uint dwFlags);

    [LibraryImport("mpr.dll", EntryPoint = "WNetConnectionDialog")]
    public static partial int WNetConnectionDialog(nint hwnd, uint dwType);

    [LibraryImport("mpr.dll", EntryPoint = "WNetDisconnectDialog")]
    public static partial int WNetDisconnectDialog(nint hwnd, uint dwType);

    [LibraryImport("mpr.dll", EntryPoint = "WNetGetConnectionW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int WNetGetConnection(string lpLocalName, char* lpRemoteName, ref int lpnLength);
}
