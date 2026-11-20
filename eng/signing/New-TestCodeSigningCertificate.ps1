<#
.SYNOPSIS
  Creates a throwaway code-signing root and signer for signing dry runs, in memory (no certificate store is touched).
.DESCRIPTION
  Writes test-root.cer (the only trust anchor a verifier may be given for it) and test-signer.pfx (the signer's
  certificate and key) to a new OutputDirectory. The PFX password is random and is returned, never written to disk.
  Windows trusts neither certificate, so nothing signed with them can pass a verification that uses Windows' roots.
#>
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1, 30)][int]$ValidDays = 3
)
$ErrorActionPreference = 'Stop'
if (-not ([IO.Path]::IsPathRooted($OutputDirectory) -and [IO.Path]::GetPathRoot($OutputDirectory).Length -ge 3)) {
    throw 'OutputDirectory must be an absolute path.'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw "OutputDirectory already exists: $OutputDirectory" }

$sha256 = [System.Security.Cryptography.HashAlgorithmName]::SHA256
$pkcs1 = [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
$now = [DateTimeOffset]::UtcNow
function NewSerial {
    $serial = [byte[]]::new(16)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($serial)
    $serial[0] = $serial[0] -band 0x7F
    $serial
}

$rootKey = [System.Security.Cryptography.RSA]::Create(3072)
$rootRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
    'CN=FileCat signing dry-run test root (untrusted), O=FileCat CI', $rootKey, $sha256, $pkcs1)
$rootRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $true, 0, $true))
$rootRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
    [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]'KeyCertSign, CrlSign', $true))
$rootRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($rootRequest.PublicKey, $false))
$root = $rootRequest.CreateSelfSigned($now.AddDays(-1), $now.AddDays($ValidDays + 1))

$signerKey = [System.Security.Cryptography.RSA]::Create(3072)
$signerRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
    'CN=FileCat signing dry-run test signer, O=FileCat CI', $signerKey, $sha256, $pkcs1)
$signerRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
$signerRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
    [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true))
$codeSigning = [System.Security.Cryptography.OidCollection]::new()
[void]$codeSigning.Add([System.Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
$signerRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($codeSigning, $false))
$signerRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($signerRequest.PublicKey, $false))
$signerRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509AuthorityKeyIdentifierExtension]::CreateFromCertificate($root, $true, $false))
$signerPublic = $signerRequest.Create($root, $now.AddHours(-1), $now.AddDays($ValidDays), (NewSerial))
$signer = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::CopyWithPrivateKey($signerPublic, $signerKey)

$passwordBytes = [byte[]]::new(24)
[System.Security.Cryptography.RandomNumberGenerator]::Fill($passwordBytes)
$password = [Convert]::ToHexString($passwordBytes).ToLowerInvariant()
# The PFX holds only the signer, so Jsign finds exactly one key entry; verifiers get the root separately.
$bundle = [System.Security.Cryptography.X509Certificates.X509Certificate2Collection]::new()
[void]$bundle.Add($signer)

[void][IO.Directory]::CreateDirectory($OutputDirectory)
$rootFile = Join-Path $OutputDirectory 'test-root.cer'
$pfxFile = Join-Path $OutputDirectory 'test-signer.pfx'
[IO.File]::WriteAllBytes($rootFile, $root.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
[IO.File]::WriteAllBytes($pfxFile, $bundle.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pkcs12, $password))

[pscustomobject]@{
    RootCertificate = $rootFile
    RootSha256 = $root.GetCertHashString($sha256)
    Pfx = $pfxFile
    PfxPassword = $password
    SignerSubject = $signer.Subject
    SignerSha256 = $signer.GetCertHashString($sha256)
    SignerSha1 = $signer.Thumbprint
    NotAfterUtc = $signer.NotAfter.ToUniversalTime().ToString('o')
}
