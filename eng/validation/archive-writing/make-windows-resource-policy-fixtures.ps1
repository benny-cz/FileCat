# Explicit disposable native validation only. Changes security metadata on two newly created owned fixture files.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$NewFixtureDirectory)
$ErrorActionPreference = 'Stop'
if ($NewFixtureDirectory -notmatch '^[a-zA-Z]:[\\/]') { throw 'Supply an absolute local fixture directory.' }
$taskPolicyRoot = [IO.Path]::GetFullPath($NewFixtureDirectory)
if (Test-Path -LiteralPath $taskPolicyRoot) { throw 'Refuse to overwrite an existing fixture directory.' }
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class FileCatResourcePolicyFixture {
    [StructLayout(LayoutKind.Sequential)] struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] struct Privilege { public uint Count; public Luid Id; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] struct SecurityAttributes { public uint Size; public IntPtr Descriptor; public int Inherit; }
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr value);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool LookupPrivilegeValue(string system,string name,out Luid id);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool AdjustTokenPrivileges(IntPtr token,bool disable,ref Privilege state,uint size,out Privilege previous,out uint required);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text,uint revision,out IntPtr descriptor,out uint size);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetSecurityDescriptorSacl(IntPtr descriptor,out bool present,out IntPtr sacl,out bool defaulted);
    [DllImport("advapi32.dll")] static extern uint SetSecurityInfo(IntPtr file,int type,uint information,IntPtr owner,IntPtr group,IntPtr dacl,IntPtr sacl);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr value);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr ReOpenFile(IntPtr original,uint access,uint sharing,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateDirectory(string path,ref SecurityAttributes security);
    public static void CreatePrivateDirectory(string path,string owner) {
        IntPtr descriptor; uint size;
        if(!ConvertStringSecurityDescriptorToSecurityDescriptor("D:P(A;;FA;;;"+owner+")(A;;FA;;;SY)",1,out descriptor,out size)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            var security=new SecurityAttributes {Size=(uint)Marshal.SizeOf(typeof(SecurityAttributes)),Descriptor=descriptor};
            if(!CreateDirectory(path,ref security)) throw new Win32Exception(Marshal.GetLastWin32Error());
        } finally { LocalFree(descriptor); }
    }
    public static void Set(IntPtr original,string sid) {
        IntPtr token=IntPtr.Zero, descriptor=IntPtr.Zero, securityFile=IntPtr.Zero; Privilege previous=default(Privilege); bool changed=false;
        try {
            if(!OpenProcessToken(GetCurrentProcess(),0x28,out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            Luid id; if(!LookupPrivilegeValue(null,"SeSecurityPrivilege",out id)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var requested=new Privilege {Count=1,Id=id,Attributes=2}; uint required;
            if(!AdjustTokenPrivileges(token,false,ref requested,(uint)Marshal.SizeOf(typeof(Privilege)),out previous,out required)) throw new Win32Exception(Marshal.GetLastWin32Error());
            int error=Marshal.GetLastWin32Error(); if(error!=0) throw new Win32Exception(error); changed=true;
            string sddl="S:(RA;;;;;WD;(\"FileCatArchiveTest\",TS,0x0,\"fixture\"))(SP;;;;;"+sid+")"; uint size;
            if(!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl,1,out descriptor,out size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            bool present,defaulted; IntPtr sacl;
            if(!GetSecurityDescriptorSacl(descriptor,out present,out sacl,out defaulted)||!present||sacl==IntPtr.Zero) throw new InvalidOperationException("Missing fixture SACL.");
            // Reopen the created object's live handle; privileged metadata writes never resolve its pathname again.
            securityFile=ReOpenFile(original,0x01060000 /* ACCESS_SYSTEM_SECURITY | READ_CONTROL | WRITE_DAC */,7,0);
            if(securityFile==new IntPtr(-1)) { securityFile=IntPtr.Zero; throw new Win32Exception(Marshal.GetLastWin32Error()); }
            uint result=SetSecurityInfo(securityFile,1,0x20|0x40,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,sacl);
            if(result!=0) throw new Win32Exception((int)result);
        } finally {
            if(descriptor!=IntPtr.Zero) LocalFree(descriptor);
            if(securityFile!=IntPtr.Zero) CloseHandle(securityFile);
            try { if(changed) { Privilege ignored; uint required; if(!AdjustTokenPrivileges(token,false,ref previous,(uint)Marshal.SizeOf(typeof(Privilege)),out ignored,out required)) throw new Win32Exception(Marshal.GetLastWin32Error(),"Restore fixture security privilege failed."); } }
            finally { if(token!=IntPtr.Zero) CloseHandle(token); }
        }
    }
}
'@
$taskPolicyIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { [FileCatResourcePolicyFixture]::CreatePrivateDirectory($taskPolicyRoot, $taskPolicyIdentity.User.Value) }
finally { $taskPolicyIdentity.Dispose() }
$taskPolicyCases = @(
    @{ Name = 'a.bin'; Sid = 'S-1-17-10001-10002-10003-10004' },
    @{ Name = 'b.bin'; Sid = 'S-1-17-20001-20002-20003-20004' }
)
foreach ($taskPolicyCase in $taskPolicyCases) {
    $taskPolicyFile = Join-Path $taskPolicyRoot $taskPolicyCase.Name
    $taskPolicyStream = [IO.FileStream]::new($taskPolicyFile, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read)
    try {
        $taskPolicyStream.Write([byte[]]@(1,2,3),0,3)
        $taskPolicyStream.Flush($true)
        [FileCatResourcePolicyFixture]::Set($taskPolicyStream.SafeFileHandle.DangerousGetHandle(), $taskPolicyCase.Sid)
    } finally { $taskPolicyStream.Dispose() }
}
$taskPolicyCases | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskPolicyRoot 'cases.json')
Write-Output "Prepared two owned resource-attribute/CAP fixtures: $taskPolicyRoot"
