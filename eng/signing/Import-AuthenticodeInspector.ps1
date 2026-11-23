# Dot-sourced by the eng/signing scripts: compiles AuthenticodeInspector.cs once per session. Once any reference is
# named, Add-Type compiles against only a minimal set, so the facades the code uses are named too.
if (-not ('FileCat.Signing.Authenticode' -as [type])) {
    Add-Type -AssemblyName System.Security.Cryptography.Pkcs
    Add-Type -Path (Join-Path $PSScriptRoot 'AuthenticodeInspector.cs') -ReferencedAssemblies @(
        [System.Security.Cryptography.Pkcs.SignedCms].Assembly.Location
        [System.Formats.Asn1.AsnReader].Assembly.Location
        [System.Security.Cryptography.X509Certificates.X509Certificate2].Assembly.Location
        'System.Collections', 'System.Collections.NonGeneric', 'System.Runtime', 'System.Runtime.InteropServices', 'System.Memory'
    )
}
