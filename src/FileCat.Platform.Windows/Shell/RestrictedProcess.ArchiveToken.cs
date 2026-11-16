using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace FileCat.Platform.Windows.Shell;

internal sealed unsafe partial class RestrictedProcess
{
    /// <summary>
    /// Verify the LPAC access semantics on the actual suspended child's token. GetTokenInformation class 46 is
    /// not a supported Win32 query on every supported Windows version (including versions that create LPACs).
    /// AccessCheck is documented for impersonation tokens. The exact-package positive control must succeed;
    /// the user-only and ALL_APPLICATION_PACKAGES controls must both be denied. API failures refuse startup.
    /// This is an access-control verification, not an inference from the requested creation attributes.
    /// </summary>
    private static bool VerifyLessPrivilegedAccess(nint token, nint expectedSid, out string refusal)
    {
        refusal = "The archive token could not be duplicated for an access check.";
        const uint TokenImpersonate = 4;
        const int TokenImpersonation = 2;
        if (!DuplicateTokenEx(token, TOKEN_QUERY | TokenImpersonate, 0, SecurityImpersonation,
                TokenImpersonation, out nint impersonation)) return false;
        try
        {
            refusal = "The archive token's user identity could not be read.";
            if (!TokenInformation(impersonation, 1, out nint userData, out uint bytes)) return false;
            string user;
            try
            {
                if (bytes < sizeof(nint)) return false;
                user = new SecurityIdentifier(Marshal.ReadIntPtr(userData)).Value;
            }
            finally { Marshal.FreeHGlobal(userData); }
            string package = new SecurityIdentifier(expectedSid).Value;
            // Bit 1 is deliberately a plain object access bit, not an owner/privilege or generic access right.
            // Grant both principals for the positive control: AppContainer access is the intersection of the
            // user and package checks. The negative controls remove only the exact-package grant.
            byte* privileges = stackalloc byte[1024];
            foreach (var (label, packageAce, expected) in new[]
            {
                ("exact-package positive control", $"(A;;0x1;;;{package})", true),
                ("user-only negative control", "", false),
                ("ALL_APPLICATION_PACKAGES negative control", "(A;;0x1;;;S-1-15-2-1)", false),
            })
            {
                var descriptor = new RawSecurityDescriptor($"O:{user}G:{user}D:P(A;;0x1;;;{user}){packageAce}");
                byte[] security = new byte[descriptor.BinaryLength];
                descriptor.GetBinaryForm(security, 0);
                uint privilegeBytes = 1024;
                var mapping = new GENERIC_MAPPING { GenericRead = 1, GenericWrite = 1, GenericExecute = 1, GenericAll = 1 };
                fixed (byte* sd = security)
                {
                    bool ok = AccessCheck(sd, impersonation, 1, &mapping, privileges, ref privilegeBytes,
                        out uint granted, out bool allowed);
                    if (!ok || privilegeBytes > 1024 || allowed != expected || granted != (expected ? 1u : 0u))
                    {
                        refusal = $"Archive access check failed its {label} (call={ok}, allowed={allowed}, " +
                            $"granted={granted}, error={Marshal.GetLastPInvokeError()}).";
                        return false;
                    }
                }
            }
            refusal = "";
            return true;
        }
        finally { CloseHandle(impersonation); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GENERIC_MAPPING
    {
        public uint GenericRead, GenericWrite, GenericExecute, GenericAll;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AccessCheck(void* securityDescriptor, nint clientToken, uint desiredAccess,
        GENERIC_MAPPING* genericMapping, void* privilegeSet, ref uint privilegeSetLength, out uint grantedAccess,
        [MarshalAs(UnmanagedType.Bool)] out bool accessStatus);
}
