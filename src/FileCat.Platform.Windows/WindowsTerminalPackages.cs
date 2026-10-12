using System.Runtime.InteropServices;

namespace FileCat.Platform.Windows;

/// <summary>Resolve the current user's registered Windows Terminal package without consulting PATH.</summary>
internal static unsafe class WindowsTerminalPackages
{
    private static readonly string[] Families =
    [
        "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
        "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe",
        "Microsoft.WindowsTerminalCanary_8wekyb3d8bbwe",
    ];
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    internal static string? FindExecutable()
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (string family in Families)
        {
            if (FindFamilyExecutable(family) is { } executable) return executable;
        }
        return null;
    }

    private static string? FindFamilyExecutable(string family)
    {
        string fullNamePrefix = family[..family.LastIndexOf('_')] + '_';
        uint count = 0;
        uint bufferLength = 0;
        int status = GetPackagesByPackageFamily(family, ref count, null, ref bufferLength, null);
        if (status != ErrorInsufficientBuffer || count is 0 or > 16 || bufferLength is 0 or > 4096)
            return null;

        uint countCapacity = count;
        uint bufferCapacity = bufferLength;
        nint* names = stackalloc nint[(int)countCapacity];
        char* buffer = stackalloc char[(int)bufferCapacity];
        char* pathBuffer = stackalloc char[4096];
        status = GetPackagesByPackageFamily(family, ref count, names, ref bufferLength, buffer);
        if (status != ErrorSuccess || count is 0 or > 16 || count > countCapacity || bufferLength > bufferCapacity) return null;

        for (int index = 0; index < count; index++)
        {
            string? fullName = Marshal.PtrToStringUni(names[index]);
            if (fullName is null || !fullName.StartsWith(fullNamePrefix, StringComparison.Ordinal))
                continue;

            uint pathLength = 4096;
            status = GetPackagePathByFullName(fullName, ref pathLength, pathBuffer);
            if (status != ErrorSuccess || pathLength is < 2 or > 4096 || pathBuffer[(int)pathLength - 1] != '\0') continue;
            string candidate = Path.Combine(new string(pathBuffer, 0, (int)pathLength - 1), "wt.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // https://learn.microsoft.com/windows/win32/api/appmodel/nf-appmodel-getpackagesbypackagefamily
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagesByPackageFamily(string packageFamilyName, ref uint count,
        nint* packageFullNames, ref uint bufferLength, char* buffer);

    // https://learn.microsoft.com/windows/win32/api/appmodel/nf-appmodel-getpackagepathbyfullname
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagePathByFullName(string packageFullName, ref uint pathLength, char* path);
}
