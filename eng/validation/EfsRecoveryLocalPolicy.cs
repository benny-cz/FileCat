using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

// Used by the disposable hosted runner's recovery fixture.
public static class EfsRecoveryLocalPolicy
{
    private const string EfsStore = @"Software\Policies\Microsoft\SystemCertificates\EFS";
    private static readonly Guid LocalGroupPolicy = new Guid("EA502722-A23D-11D1-A7D3-0000F87571E3");

    [ComImport, Guid("EA502723-A23D-11D1-A7D3-0000F87571E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGroupPolicyObject
    {
        void New(IntPtr domain, IntPtr name, uint flags);
        void OpenDSGPO(IntPtr path, uint flags);
        void OpenLocalMachineGPO(uint flags);
        void OpenRemoteMachineGPO(IntPtr machine, uint flags);
        void Save([MarshalAs(UnmanagedType.Bool)] bool machine, [MarshalAs(UnmanagedType.Bool)] bool add,
            ref Guid extension, ref Guid editor);
        void Delete();
        void GetName(IntPtr name, int chars);
        void GetDisplayName(IntPtr name, int chars);
        void SetDisplayName(IntPtr name);
        void GetPath(IntPtr path, int chars);
        void GetDSPath(uint section, IntPtr path, int chars);
        void GetFileSysPath(uint section, IntPtr path, int chars);
        void GetRegistryKey(uint section, out IntPtr key);
    }

    public static void Update(string thumbprint, byte[] publicCertificate, byte[] recoveryBlob, bool install)
    {
        var policy = (IGroupPolicyObject)Activator.CreateInstance(Type.GetTypeFromCLSID(LocalGroupPolicy, true));
        try
        {
            policy.OpenLocalMachineGPO(1); // GPO_OPEN_LOAD_REGISTRY
            IntPtr key;
            policy.GetRegistryKey(2, out key); // GPO_SECTION_MACHINE
            using (var handle = new SafeRegistryHandle(key, true))
            using (var root = RegistryKey.FromHandle(handle))
            using (var efs = root.CreateSubKey(EfsStore))
            {
                string certificate = @"Certificates\" + thumbprint;
                if (install)
                {
                    using (var existingStore = efs.OpenSubKey("Certificates"))
                    {
                        if (efs.GetValue("EfsBlob") != null || existingStore != null && existingStore.SubKeyCount > 0)
                            throw new InvalidOperationException("The runner already has a local EFS recovery policy.");
                    }
                    using (var store = efs.CreateSubKey(certificate))
                        store.SetValue("Blob", publicCertificate, RegistryValueKind.Binary);
                    efs.SetValue("EfsBlob", recoveryBlob, RegistryValueKind.Binary);
                }
                else
                {
                    var storedRecovery = efs.GetValue("EfsBlob") as byte[];
                    if (storedRecovery != null && storedRecovery.SequenceEqual(recoveryBlob))
                        efs.DeleteValue("EfsBlob");
                    byte[] storedCertificate;
                    using (var store = efs.OpenSubKey(certificate))
                        storedCertificate = store == null ? null : store.GetValue("Blob") as byte[];
                    if (storedCertificate != null && storedCertificate.SequenceEqual(publicCertificate))
                        efs.DeleteSubKey(certificate);
                }
            }
            var registryExtension = new Guid("35378EAC-683F-11D2-A89A-00C04FBBCFA2");
            var efsExtension = new Guid("B1BE8D72-6EAC-11D2-A4EA-00C04F79F83A");
            var computerPolicyEditor = new Guid("53D6AB1D-2488-11D1-A28C-00C04FB94F17");
            policy.Save(true, true, ref registryExtension, ref computerPolicyEditor);
            policy.Save(true, install, ref efsExtension, ref computerPolicyEditor);
        }
        finally { Marshal.FinalReleaseComObject(policy); }
    }
}
