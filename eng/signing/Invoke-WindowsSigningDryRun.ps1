<#
.SYNOPSIS
  Signing dry run: builds FileCat's win-x64 payloads, archives and installer signed by a throwaway test certificate
  or by SSL.com's eSigner sandbox, so the whole signing pipeline runs without FileCat's real signing identity.
.DESCRIPTION
  TestCertificate needs no secrets: New-TestCodeSigningCertificate.ps1 makes a signer whose root only this run's
  verifier is told to trust. ESignerSandbox signs through SSL.com's sandbox with the FILECAT_ESIGNER_* variables
  (SSL.com's published demo account). Signing happens inside eng/publish.ps1 -Sign and eng/Compile-WindowsInstaller.ps1
  -Sign, exactly where a release signs. dry-run.json names the outputs, the expected signer and the trust anchor;
  verification is the separate Test-WindowsSignatures.ps1 step, which reads only that file and the outputs.
#>
param(
    [ValidateSet('TestCertificate', 'ESignerSandbox')][string]$Mode = 'TestCertificate',
    [Parameter(Mandatory)][string]$WorkDirectory,
    [string]$Version = '0.0.0-signing-dry-run',
    # An Inno Setup compiler already acquired by eng/Acquire-InnoSetup.ps1; acquired into the work directory otherwise.
    [string]$InnoCompilerRoot
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not ([IO.Path]::IsPathRooted($WorkDirectory) -and [IO.Path]::GetPathRoot($WorkDirectory).Length -ge 3)) { throw 'WorkDirectory must be an absolute path.' }
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $WorkDirectory) { throw "WorkDirectory already exists: $WorkDirectory" }
[void][IO.Directory]::CreateDirectory($WorkDirectory)
$artifacts = Join-Path $WorkDirectory 'artifacts'
$sourceCommit = "$(git -C $repo rev-parse HEAD)".Trim()

$jsign = & (Join-Path $PSScriptRoot 'Acquire-Jsign.ps1') -ToolRoot (Join-Path $WorkDirectory 'tools/jsign')
Write-Host "Jsign $($jsign.Version) ($($jsign.JarSHA256)) on $($jsign.JavaVersion)"
$env:FILECAT_JSIGN_JAR = $jsign.Jar
$env:FILECAT_JAVA = $jsign.Java
$env:FILECAT_SIGN_MODE = $Mode
$trustedRoot = $null
$expectedSigner = $null
if ($Mode -eq 'TestCertificate') {
    $certificate = & (Join-Path $PSScriptRoot 'New-TestCodeSigningCertificate.ps1') -OutputDirectory (Join-Path $WorkDirectory 'test-ca')
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host "::add-mask::$($certificate.PfxPassword)" }
    $env:FILECAT_SIGN_PFX = $certificate.Pfx
    $env:FILECAT_SIGN_PFX_PASSWORD = $certificate.PfxPassword
    $trustedRoot = $certificate.RootCertificate
    $expectedSigner = $certificate.SignerSha256
    Write-Host "Test signer $($certificate.SignerSubject), SHA-256 $expectedSigner; test root SHA-256 $($certificate.RootSha256)"
} else {
    foreach ($name in 'FILECAT_ESIGNER_USERNAME', 'FILECAT_ESIGNER_PASSWORD', 'FILECAT_ESIGNER_TOTP_SECRET') {
        if (-not [Environment]::GetEnvironmentVariable($name)) { throw "$name is not set; ESignerSandbox takes SSL.com's published sandbox values from repository secrets." }
    }
}

& (Join-Path $repo 'eng/publish.ps1') -Version $Version -Runtime win-x64 -ArtifactDirectory $artifacts -Sign
$receipts = Join-Path $artifacts 'toolchains/inno-setup'
$compilerRoot = $InnoCompilerRoot
if ($compilerRoot) {
    [void][IO.Directory]::CreateDirectory($receipts)
} else {
    $compilerRoot = (& (Join-Path $repo 'eng/Acquire-InnoSetup.ps1') -ToolRoot (Join-Path $WorkDirectory 'tools/inno') -ReceiptDirectory $receipts).ToolRoot
}
$installer = & (Join-Path $repo 'eng/Compile-WindowsInstaller.ps1') -Version ($Version -split '-')[0] -Architecture x64 `
    -CompilerRoot $compilerRoot -ReceiptDirectory $receipts -SourceCommit $sourceCommit -ArtifactDirectory $artifacts -Sign

$manifest = Join-Path $artifacts 'signing/win-x64/signed-outputs.json'
$observed = (Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json).Signer.SHA256
if ($expectedSigner -and $observed -cne $expectedSigner) { throw "The payload was signed by $observed, not this run's signer $expectedSigner." }
if (-not $expectedSigner) {
    # The sandbox signer is whatever SSL.com's demo credential holds. Its root, when the signature embeds one, is the
    # trust anchor: that proves the eSigner plumbing only; a real signing run trusts Windows' roots and nothing else.
    $expectedSigner = $observed
    . (Join-Path $PSScriptRoot 'Import-AuthenticodeInspector.ps1')
    $signedFile = Join-Path $artifacts 'publish/win-x64/FileCat.exe'
    $root = [FileCat.Signing.Authenticode]::Inspect($signedFile, $false).EmbeddedCertificates | Where-Object { $_.Subject -ceq $_.Issuer } | Select-Object -First 1
    if ($root) {
        $trustedRoot = Join-Path $WorkDirectory 'sandbox-root.cer'
        [IO.File]::WriteAllBytes($trustedRoot, $root.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    }
}

$run = [ordered]@{
    Schema = 'filecat-signing-dry-run/1'
    UTC = [DateTime]::UtcNow.ToString('o')
    Mode = $Mode
    SourceCommit = $sourceCommit
    Version = $Version
    Jsign = [ordered]@{ Version = $jsign.Version; JarSHA256 = $jsign.JarSHA256; Java = $jsign.JavaVersion }
    ExpectedSignerSHA256 = $expectedSigner
    TrustedRootFile = $trustedRoot
    SigningManifests = @($manifest)
    Outputs = [ordered]@{
        Payload = @((Join-Path $artifacts 'publish/win-x64'), (Join-Path $artifacts 'publish/win-x64-fdd'))
        Zips = @(Get-ChildItem -LiteralPath $artifacts -File -Filter '*.zip' | Sort-Object Name | ForEach-Object FullName)
        Installer = $installer.Path
        SignedUninstallerDirectory = $installer.SignedUninstallerDirectory
    }
}
[IO.File]::WriteAllText((Join-Path $WorkDirectory 'dry-run.json'), ($run | ConvertTo-Json -Depth 5))
[pscustomobject]$run
