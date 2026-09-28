using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.State;

namespace FileCat.Platform.Windows;

/// <summary>
/// Secrets in Windows Credential Manager (generic credentials of the current user on this machine), so a saved
/// password never touches FileCat's files (plan §14.1).
/// </summary>
internal sealed unsafe partial class WindowsCredentialStore : ISecretStore
{
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;
    private const int ERROR_NOT_FOUND = 1168;

    public bool IsPersistent => true;

    public string? Read(string key)
    {
        if (!CredReadW(key, CRED_TYPE_GENERIC, 0, out var credential))
        {
            int error = Marshal.GetLastPInvokeError();
            if (error == ERROR_NOT_FOUND) return null;
            throw new IOException("Windows Credential Manager could not read the saved secret: " + new System.ComponentModel.Win32Exception(error).Message);
        }
        try
        {
            return credential->CredentialBlobSize == 0 ? string.Empty
                : Encoding.Unicode.GetString(credential->CredentialBlob, (int)credential->CredentialBlobSize);
        }
        finally
        {
            CredFree(credential);
        }
    }

    public void Write(string key, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        fixed (char* target = key)
        fixed (char* user = "FileCat")
        fixed (byte* data = blob)
        {
            var credential = new Credential
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                UserName = user,
                CredentialBlob = data,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
            };
            try
            {
                if (!CredWriteW(&credential, 0))
                    throw new IOException("Windows Credential Manager could not save the secret: " + new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()).Message);
            }
            finally
            {
                Array.Clear(blob);
            }
        }
    }

    public void Delete(string key)
    {
        if (!CredDeleteW(key, CRED_TYPE_GENERIC, 0) && Marshal.GetLastPInvokeError() is var error && error != ERROR_NOT_FOUND)
            throw new IOException("Windows Credential Manager could not remove the saved secret: " + new System.ComponentModel.Win32Exception(error).Message);
    }

    // CREDENTIALW
    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public char* TargetName;
        public char* Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public byte* CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public char* TargetAlias;
        public char* UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredReadW(string target, uint type, uint flags, out Credential* credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWriteW(Credential* credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDeleteW(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(void* buffer);
}
