using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Elevation;

public static partial class ElevationBroker
{
    private static bool HasTrustedExecutable(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return false;
        try
        {
            string full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) full = full[4..];
            string? final = FinalDosPath(path);
            // ShellExecute receives the original name. A resolved trusted target cannot protect a writable alias.
            if (final is null || !string.Equals(full, final, StringComparison.OrdinalIgnoreCase)) return false;
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                string root = Environment.GetFolderPath(folder);
                if (root.Length == 0) continue;
                string? finalRoot = FinalDosPath(root)?.TrimEnd('\\');
                if (finalRoot is null || !final.StartsWith(finalRoot + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                string node = final;
                while (true)
                {
                    if ((File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0 || !AdministratorOwnedWithoutUserWrite(node)) return false;
                    if (string.Equals(node, finalRoot, StringComparison.OrdinalIgnoreCase)) return true;
                    string? parent = Path.GetDirectoryName(node);
                    if (parent is null || parent.Length < finalRoot.Length) return false;
                    node = parent;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or Win32Exception or System.Security.SecurityException)
        {
            return false;
        }
        return false;
    }

    private static bool TrustedFileWriter(string? sid) => sid is "S-1-5-18" or "S-1-5-32-544" or
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    private static bool AdministratorOwnedWithoutUserWrite(string path)
    {
        using var handle = CreateFileForQuery(path, 0x00020000 /* READ_CONTROL */, 7, 0, 3,
            0x02200000 /* BACKUP_SEMANTICS | OPEN_REPARSE_POINT */, 0);
        if (handle.IsInvalid) return false;
        int error = GetSecurityInfo(handle, 1 /* SE_FILE_OBJECT */, 1 | 4 /* OWNER | DACL */,
            out _, out _, out _, out _, out nint descriptor);
        try
        {
            if (error != 0 || descriptor == 0) return false;
            int length = GetSecurityDescriptorLength(descriptor);
            if (length <= 0 || length > 1024 * 1024) return false;
            var bytes = new byte[length];
            Marshal.Copy(descriptor, bytes, 0, length);
            var security = new RawSecurityDescriptor(bytes, 0);
            if (!TrustedFileWriter(security.Owner?.Value) || security.DiscretionaryAcl is null) return false;
            const int write = 0x500D0156; // Generic all/write, write DAC/owner, delete and file/directory write bits.
            foreach (GenericAce ace in security.DiscretionaryAcl)
            {
                if ((ace.AceFlags & AceFlags.InheritOnly) != 0) continue;
                if (ace is QualifiedAce { AceQualifier: AceQualifier.AccessDenied }) continue;
                // Conditional/object grants need a different access evaluation; do not assume they are safe.
                if (ace is not CommonAce { AceQualifier: AceQualifier.AccessAllowed, IsCallback: false } allow) return false;
                if ((allow.AccessMask & write) != 0 && !TrustedFileWriter(allow.SecurityIdentifier.Value)) return false;
            }
            return true;
        }
        finally
        {
            if (descriptor != 0) LocalFree(descriptor);
        }
    }

    [LibraryImport("advapi32.dll")]
    private static partial int GetSecurityInfo(SafeFileHandle handle, int objectType, uint information,
        out nint owner, out nint group, out nint dacl, out nint sacl, out nint descriptor);

    [LibraryImport("advapi32.dll")]
    private static partial int GetSecurityDescriptorLength(nint descriptor);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
