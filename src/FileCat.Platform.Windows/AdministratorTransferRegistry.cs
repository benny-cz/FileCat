using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.State;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>Private storage for remembered preferences and window placement in elevated FileCat.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class AdministratorTransferRegistry
{
    private const string RegistryPath = @"SOFTWARE\FileCat.AdministratorTransfers";
    private const int KeyAccess = (int)(RegistryRights.ReadKey | RegistryRights.SetValue) | 0x100 /* KEY_WOW64_64KEY */;
    private const int FullControl = (int)RegistryRights.FullControl;
    private const string AdministratorSid = "S-1-5-32-544";
    private const string SystemSid = "S-1-5-18";
    private const uint StringType = 1; // REG_SZ
    private const int NotFound = 2, MoreData = 234;
    private readonly string _path;

    public AdministratorTransferRegistry() : this(RegistryPath) { }
    internal AdministratorTransferRegistry(string path) => _path = path;

    public string? Read(string name, int maxCharacters)
    {
        RequireAdministrator();
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(_path);
        if (key is null) return null;
        ValidateAccess(key.Handle);
        uint length = 0;
        int error = QueryValue(key.Handle, name, 0, out uint type, null, ref length);
        if (error == NotFound) return null;
        if (error != 0 && error != MoreData) throw new Win32Exception(error);
        if (type != StringType || length < 2 || (length & 1) != 0 || length > (maxCharacters + 1) * 2)
            throw new IOException("The remembered transfer is invalid or too large.");
        var bytes = new byte[length];
        error = QueryValue(key.Handle, name, 0, out type, bytes, ref length);
        if (error == NotFound) return null;
        if (error != 0) throw new Win32Exception(error);
        if (type != StringType || length < 2 || (length & 1) != 0 || bytes[length - 1] != 0 || bytes[length - 2] != 0)
            throw new IOException("The remembered transfer is invalid.");
        return System.Text.Encoding.Unicode.GetString(bytes, 0, (int)length - 2);
    }

    public void Save(string name, string text, int maxCharacters)
    {
        RequireAdministrator();
        if (text.Length > maxCharacters) throw new IOException("The transfer exceeds the size limit.");
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = OpenOrCreate(machine.Handle);
        ValidateAccess(key);
        byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text + '\0');
        int error = SetValue(key, name, 0, StringType, bytes, (uint)bytes.Length);
        if (error != 0) throw new Win32Exception(error);
    }

    public void Forget(string name)
    {
        RequireAdministrator();
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(_path, writable: true);
        if (key is null) return;
        ValidateAccess(key.Handle);
        key.DeleteValue(name, throwOnMissingValue: false);
    }

    private static void RequireAdministrator()
    {
        if (!OperatingSystem.IsWindows() || !WindowsTokenAuthority.IsHighIntegrity)
            throw new UnauthorizedAccessException("Remembered transfers are available only in an administrator session.");
    }

    private static void ValidateAccess(SafeRegistryHandle key)
    {
        var descriptor = RegistryAcl.ReadDescriptor(key);
        var acl = descriptor.DiscretionaryAcl;
        if (descriptor.Owner?.Value != AdministratorSid ||
            (descriptor.ControlFlags & ControlFlags.DiscretionaryAclProtected) == 0 ||
            acl is not { Count: 2 } ||
            !AllowsExactly(acl, SystemSid) || !AllowsExactly(acl, AdministratorSid))
            throw new UnauthorizedAccessException("The administrator transfer store is not private to Windows administrators.");
    }

    private static bool AllowsExactly(RawAcl acl, string sid) => acl.Cast<GenericAce>().Any(ace =>
        ace is CommonAce { AceQualifier: AceQualifier.AccessAllowed, AceFlags: AceFlags.None, AccessMask: FullControl } rule &&
        rule.SecurityIdentifier.Value == sid);

    private unsafe SafeRegistryHandle OpenOrCreate(SafeRegistryHandle machine)
    {
        var access = new RawSecurityDescriptor("O:BAG:BAD:P(A;;KA;;;SY)(A;;KA;;;BA)");
        var bytes = new byte[access.BinaryLength];
        access.GetBinaryForm(bytes, 0);
        fixed (byte* descriptor = bytes)
        {
            var native = new SecurityAttributes { Length = (uint)sizeof(SecurityAttributes), Descriptor = (nint)descriptor };
            int error = CreateKey(machine, _path, 0, null, 0, KeyAccess, (nint)(&native), out var key, out _);
            if (error == 0) return key;
            key.Dispose();
            throw new Win32Exception(error);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal uint Length;
        internal nint Descriptor;
        internal int InheritHandle;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "RegCreateKeyExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CreateKey(SafeRegistryHandle key, string name, uint reserved, string? className,
        uint options, int access, nint securityAttributes, out SafeRegistryHandle result, out uint disposition);

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int QueryValue(SafeRegistryHandle key, string name, nint reserved, out uint type, [Out] byte[]? data, ref uint length);

    [LibraryImport("advapi32.dll", EntryPoint = "RegSetValueExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetValue(SafeRegistryHandle key, string name, uint reserved, uint type, byte[] data, uint length);
}
