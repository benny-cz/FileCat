using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

/// <summary>Binds a previously observed source identity to the actual file opened for reading.</summary>
public static class OpenedSourceIdentity
{
    public static void RequireMatch(SafeFileHandle handle, string? expected, Func<SafeFileHandle, string?> identify)
    {
        if (string.IsNullOrEmpty(expected)) return;
        string? opened = identify(handle);
        if (!string.Equals(expected, opened, StringComparison.Ordinal))
            throw new IOException("The source changed or its open-file identity could not be checked. Open it again.");
    }
}
