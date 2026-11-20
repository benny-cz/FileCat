<#
.SYNOPSIS
  One phase of a Windows signing run. Each GitHub Actions step runs one phase, so each step gets only what it needs.
.DESCRIPTION
  Signing credentials reach only SignPayload and SignInstaller, which run no third-party build code. The payload is
  built by Build in a separate job (another machine) with no credentials, and Prepare checks it byte for byte
  against the manifest that Build reported. Phases share state through WorkDirectory/run.json.

    Build          publish.ps1 -PayloadOnly; writes payload-manifest.json and reports its SHA-256 (a job output)
    Prepare        checks the payload against that SHA-256 and manifest, restores packages for the SBOMs, acquires
                   Jsign and Inno Setup, and, for TestCertificate, makes the throwaway test signer
    SignPayload    Sign-WindowsPayload.ps1 on both payload folders; when a signer is pinned, one canary file is
                   signed first, so a credential that resolves to another certificate costs a single signature
    Package        publish.ps1 -FromPayload: SBOMs and archives of the signed payloads
    SignInstaller  Compile-WindowsInstaller.ps1 -Sign: Setup and its uninstaller
    Draft          (ESigner) a private draft release with the signed outputs, manifests and report, never published

  Modes:
    TestCertificate  no secrets
    ESignerSandbox   SSL.com's sandbox, through the FILECAT_ESIGNER_* variables
    ESigner          FileCat's SSL.com certificate, pinned by FILECAT_EXPECTED_SIGNER_SHA256
  In ESigner mode, nothing this script prints names the signer; run.json and the report go only into the draft.
#>
param(
    [Parameter(Mandatory)][ValidateSet('Build', 'Prepare', 'SignPayload', 'Package', 'SignInstaller', 'Draft')][string]$Phase,
    [Parameter(Mandatory)][string]$WorkDirectory,
    # Build: the package version.
    [string]$Version,
    # Prepare: the signing mode and the payload-manifest SHA-256 that Build reported.
    [ValidateSet('TestCertificate', 'ESignerSandbox', 'ESigner')][string]$Mode,
    [ValidatePattern('^$|^[0-9A-Fa-f]{64}$')][string]$PayloadManifestSha256,
    # Draft: the verification step's outcome, for the draft's title.
    [string]$VerificationOutcome = 'unknown'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not ([IO.Path]::IsPathRooted($WorkDirectory) -and [IO.Path]::GetPathRoot($WorkDirectory).Length -ge 3)) { throw 'WorkDirectory must be an absolute path.' }
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
$artifacts = Join-Path $WorkDirectory 'artifacts'
$payloads = @((Join-Path $artifacts 'publish/win-x64'), (Join-Path $artifacts 'publish/win-x64-fdd'))
$manifestFile = Join-Path $WorkDirectory 'payload-manifest.json'
$stateFile = Join-Path $WorkDirectory 'run.json'

function Read-State { Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json }
function Save-State($State) { [IO.File]::WriteAllText($stateFile, ($State | ConvertTo-Json -Depth 6)) }
function Get-Sha256([string]$File) { (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash }
function Get-PayloadInventory {
    foreach ($root in $payloads) {
        $label = Split-Path -Leaf $root
        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName) {
            [pscustomobject]@{
                Root = $label
                Path = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
                Bytes = $file.Length
                SHA256 = Get-Sha256 $file.FullName
            }
        }
    }
}
# The signing identity reaches Invoke-Jsign.ps1 (also run by Inno Setup's compiler) through the environment.
function Set-SigningEnvironment($State) {
    $env:FILECAT_JSIGN_JAR = $State.Jsign.Jar
    $env:FILECAT_JAVA = $State.Jsign.Java
    $env:FILECAT_SIGN_MODE = $State.Mode
    if ($State.Mode -eq 'TestCertificate') {
        $env:FILECAT_SIGN_PFX = $State.TestCertificate.Pfx
        if (-not $env:FILECAT_SIGN_PFX_PASSWORD) { $env:FILECAT_SIGN_PFX_PASSWORD = $State.TestCertificate.LocalPfxPassword }
        $env:FILECAT_EXPECTED_SIGNER_SHA256 = $State.ExpectedSignerSHA256
    } elseif ($State.Mode -eq 'ESigner' -and -not $env:FILECAT_EXPECTED_SIGNER_SHA256) {
        throw 'FILECAT_EXPECTED_SIGNER_SHA256 must pin FileCat''s certificate in ESigner mode.'
    }
}

switch ($Phase) {
    'Build' {
        if (-not $Version) { throw 'Build needs -Version.' }
        if (Test-Path -LiteralPath $WorkDirectory) { throw "WorkDirectory already exists: $WorkDirectory" }
        [void][IO.Directory]::CreateDirectory($WorkDirectory)
        & (Join-Path $repo 'eng/publish.ps1') -Version $Version -Runtime win-x64 -ArtifactDirectory $artifacts -PayloadOnly
        $manifest = [ordered]@{
            Schema = 'filecat-payload-manifest/1'
            SourceCommit = "$(git -C $repo rev-parse HEAD)".Trim()
            Version = $Version
            Runtime = 'win-x64'
            Files = @(Get-PayloadInventory)
        }
        [IO.File]::WriteAllText($manifestFile, ($manifest | ConvertTo-Json -Depth 4))
        $sha = Get-Sha256 $manifestFile
        Write-Host "Unsigned payload: $($manifest.Files.Count) files; payload-manifest.json SHA-256 $sha"
        if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "payload_manifest_sha256=$sha" }
    }
    'Prepare' {
        if (-not $Mode -or -not $PayloadManifestSha256) { throw 'Prepare needs -Mode and -PayloadManifestSha256.' }
        if (Test-Path -LiteralPath $stateFile) { throw "This work directory was already prepared: $stateFile" }
        # The payload must be exactly what Build produced: the manifest by the hash Build reported, every file by the manifest.
        if ((Get-Sha256 $manifestFile) -ne $PayloadManifestSha256.ToUpperInvariant()) { throw 'payload-manifest.json differs from the one the build job reported.' }
        $manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
        $expected = @($manifest.Files | ForEach-Object { "$($_.Root)/$($_.Path)|$($_.Bytes)|$($_.SHA256)" })
        $actual = @(Get-PayloadInventory | ForEach-Object { "$($_.Root)/$($_.Path)|$($_.Bytes)|$($_.SHA256)" })
        $difference = @(Compare-Object -ReferenceObject $expected -DifferenceObject $actual -CaseSensitive)
        if ($difference.Count) { throw "The downloaded payload differs from its manifest in $($difference.Count) entries." }
        $sourceCommit = "$(git -C $repo rev-parse HEAD)".Trim()
        if ($manifest.SourceCommit -cne $sourceCommit) { throw "The payload was built from $($manifest.SourceCommit), not this checkout ($sourceCommit)." }
        Write-Host "Payload verified: $($expected.Count) files match the build job's manifest."

        # The SBOMs check every package the payload takes against the restored archives; restoring runs no package code.
        foreach ($project in 'src/FileCat.App/FileCat.App.csproj', 'src/FileCat.PrivilegedHost/FileCat.PrivilegedHost.csproj', 'src/FileCat.ShellHost/FileCat.ShellHost.csproj') {
            dotnet restore (Join-Path $repo $project) -r win-x64 -p:SelfContained=true -p:PublishReadyToRun=true
            if ($LASTEXITCODE -ne 0) { throw "Locked restore failed: $project" }
        }
        $jsign = & (Join-Path $PSScriptRoot 'Acquire-Jsign.ps1') -ToolRoot (Join-Path $WorkDirectory 'tools/jsign')
        $receipts = Join-Path $artifacts 'toolchains/inno-setup'
        $inno = & (Join-Path $repo 'eng/Acquire-InnoSetup.ps1') -ToolRoot (Join-Path $WorkDirectory 'tools/inno') -ReceiptDirectory $receipts
        $state = [ordered]@{
            Schema = 'filecat-signing-run/1'
            Mode = $Mode
            Version = $manifest.Version
            SourceCommit = $sourceCommit
            PayloadManifestSHA256 = $PayloadManifestSha256.ToUpperInvariant()
            Jsign = [ordered]@{ Jar = $jsign.Jar; Java = $jsign.Java; Version = $jsign.Version; JarSHA256 = $jsign.JarSHA256; JavaVersion = $jsign.JavaVersion }
            InnoCompilerRoot = $inno.ToolRoot
            InnoReceipts = $receipts
            TestCertificate = $null
            ExpectedSignerSHA256 = $null
            TrustedRootFile = $null
            Outputs = $null
        }
        if ($Mode -eq 'TestCertificate') {
            $certificate = & (Join-Path $PSScriptRoot 'New-TestCodeSigningCertificate.ps1') -OutputDirectory (Join-Path $WorkDirectory 'test-ca')
            $state.TestCertificate = [ordered]@{ Pfx = $certificate.Pfx; RootCertificate = $certificate.RootCertificate; SignerSHA256 = $certificate.SignerSha256 }
            if ($env:GITHUB_ENV) {
                # Later steps of this job get the throwaway key's password; it never reaches a file.
                Write-Host "::add-mask::$($certificate.PfxPassword)"
                Add-Content -LiteralPath $env:GITHUB_ENV -Value "FILECAT_SIGN_PFX_PASSWORD=$($certificate.PfxPassword)"
            } else {
                $state.TestCertificate.LocalPfxPassword = $certificate.PfxPassword
            }
            $state.ExpectedSignerSHA256 = $certificate.SignerSha256
            $state.TrustedRootFile = $certificate.RootCertificate
            Write-Host "Test signer SHA-256 $($certificate.SignerSha256); test root SHA-256 $($certificate.RootSha256)"
        }
        Save-State $state
        Write-Host "Prepared $Mode signing of $($manifest.Version) at $sourceCommit with Jsign $($jsign.Version) on $($jsign.JavaVersion)."
    }
    'SignPayload' {
        $state = Read-State
        Set-SigningEnvironment $state
        if ($state.Mode -eq 'ESigner') { $state.ExpectedSignerSHA256 = $env:FILECAT_EXPECTED_SIGNER_SHA256.ToUpperInvariant() }
        $result = & (Join-Path $PSScriptRoot 'Sign-WindowsPayload.ps1') -Path $payloads -ManifestDirectory (Join-Path $artifacts 'signing/win-x64')
        if ($state.Mode -eq 'ESignerSandbox') {
            # The sandbox signer is whatever SSL.com's demo credential holds. Its root, if the signature embeds one, is the
            # trust anchor: that proves the eSigner plumbing only; FileCat's own runs trust Windows' roots alone.
            $state.ExpectedSignerSHA256 = $result.SignerSHA256
            . (Join-Path $PSScriptRoot 'Import-AuthenticodeInspector.ps1')
            $root = [FileCat.Signing.Authenticode]::Inspect((Join-Path $payloads[0] 'FileCat.exe'), $false).EmbeddedCertificates |
                Where-Object { $_.Subject -ceq $_.Issuer } | Select-Object -First 1
            if ($root) {
                $state.TrustedRootFile = Join-Path $WorkDirectory 'sandbox-root.cer'
                [IO.File]::WriteAllBytes($state.TrustedRootFile, $root.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
            }
        } elseif ($result.SignerSHA256 -cne $state.ExpectedSignerSHA256) {
            throw 'The payload was signed by a certificate other than the pinned signer.'
        }
        Save-State $state
        Write-Host "Signed $($result.DistinctFilesSigned) distinct files into $($result.FilesSigned) places; $($result.VendorSigned) vendor-signed files unchanged."
    }
    'Package' {
        $state = Read-State
        & (Join-Path $repo 'eng/publish.ps1') -Version $state.Version -Runtime win-x64 -ArtifactDirectory $artifacts -FromPayload
        # No build server outlives this step into the next one, which holds the signing credentials.
        dotnet build-server shutdown | Out-Null
    }
    'SignInstaller' {
        $state = Read-State
        Set-SigningEnvironment $state
        $installer = & (Join-Path $repo 'eng/Compile-WindowsInstaller.ps1') -Version ($state.Version -split '-')[0] -Architecture x64 `
            -CompilerRoot $state.InnoCompilerRoot -ReceiptDirectory $state.InnoReceipts -SourceCommit $state.SourceCommit -ArtifactDirectory $artifacts -Sign
        $receipt = Get-Content -LiteralPath (Join-Path $state.InnoReceipts 'installer-win-x64.json') -Raw | ConvertFrom-Json
        if ($state.ExpectedSignerSHA256 -and $receipt.OutputSignerSHA256 -cne $state.ExpectedSignerSHA256) { throw 'Setup was signed by a certificate other than the pinned signer.' }
        $state.Outputs = [ordered]@{
            Payload = $payloads
            Zips = @(Get-ChildItem -LiteralPath $artifacts -File -Filter '*.zip' | Sort-Object Name | ForEach-Object FullName)
            Installer = $installer.Path
        }
        Save-State $state
        Write-Host "Signed $(Split-Path -Leaf $installer.Path) and its uninstaller."
    }
    'Draft' {
        $state = Read-State
        if ($state.Mode -ne 'ESigner') { throw 'Only ESigner runs go to a draft release; dry runs keep their outputs on the runner.' }
        if (-not $env:GH_TOKEN -or -not $env:GITHUB_RUN_ID) { throw 'Draft runs in GitHub Actions with GH_TOKEN.' }
        # Whatever the run produced goes in, so a run that stopped part-way can still be examined privately.
        $assets = [System.Collections.Generic.List[string]]::new()
        Get-ChildItem -LiteralPath $artifacts -File | Where-Object { $_.Name -like '*-setup.exe' -or $_.Name -like '*.zip' -or $_.Name -like 'sbom-*.spdx.json' } |
            Sort-Object Name | ForEach-Object { $assets.Add($_.FullName) }
        foreach ($file in @((Join-Path $artifacts 'signing/win-x64/unsigned-inputs.json'), (Join-Path $artifacts 'signing/win-x64/signed-outputs.json'),
                (Join-Path $state.InnoReceipts 'installer-win-x64.json'), (Join-Path $state.InnoReceipts 'compile-win-x64.log'),
                $manifestFile, $stateFile, (Join-Path $WorkDirectory 'verification.json'))) {
            if (Test-Path -LiteralPath $file) { $assets.Add($file) }
        }
        $tag = "signing-pilot-$env:GITHUB_RUN_ID"
        $title = "Signing pilot $env:GITHUB_RUN_ID - PRIVATE DRAFT, never publish (verification: $VerificationOutcome)"
        $notes = "Private signing pilot of FileCat $($state.Version) at $($state.SourceCommit), workflow run $env:GITHUB_RUN_ID. " +
            'It holds FileCat-signed files for the owner''s own verification (signatures, Smart App Control, installed bytes). ' +
            'Do not publish it; delete it when the verification is recorded.'
        gh release create $tag --repo $env:GITHUB_REPOSITORY --draft --target $state.SourceCommit --title $title --notes $notes @assets | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Creating the private draft release failed.' }
        $draft = gh release view $tag --repo $env:GITHUB_REPOSITORY --json isDraft,assets --jq '"\(.isDraft) \(.assets | length)"'
        if ($draft -cne "true $($assets.Count)") { throw "The draft release is not what was uploaded: $draft" }
        Write-Host "Private draft release $tag holds $($assets.Count) files."
    }
}
