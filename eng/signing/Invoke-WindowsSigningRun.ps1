<#
.SYNOPSIS
  One phase of a Windows signing run. Each GitHub Actions step runs one phase, so each step gets only what it needs.
.DESCRIPTION
  Signing credentials reach only SignPayload and SignInstaller, which run no third-party build code. The payload is
  built by Build in a separate job (another machine) with no credentials, and Prepare checks it byte for byte
  against the manifest that Build reported. Phases share state through WorkDirectory/run.json.

    Build          publish.ps1 -PayloadOnly for each runtime; writes payload-manifest.json and reports its SHA-256 (a
                   job output)
    Prepare        checks the payload against that SHA-256 and manifest, restores packages for the SBOMs, acquires
                   Jsign and Inno Setup, and, for TestCertificate, makes the throwaway test signer
    SignPayload    Sign-WindowsPayload.ps1 on each runtime's two payload folders; when a signer is pinned, one
                   canary file is signed first, so a credential that resolves to another certificate costs a single
                   signature
    Package        publish.ps1 -FromPayload for each runtime: SBOMs and archives of the signed payloads
    SignInstaller  Compile-WindowsInstaller.ps1 -Sign for each runtime: Setup and its uninstaller
    Draft          (ESigner) a private draft release with the signed outputs, manifests and report, never published

  Runtimes: win-x64 (the default) and win-arm64. Authenticode does not depend on the processor, so one x64 machine
  signs both; each runtime keeps its own manifests under artifacts/signing/<runtime>. A release candidate (a
  v1.0.0-rc.N tag) passes -ProductVersion 1.0.0 and -CandidateRef to Build, and later phases reuse them from run.json.

  Modes:
    TestCertificate  no secrets
    ESignerSandbox   SSL.com's sandbox, through the FILECAT_ESIGNER_* variables
    ESigner          FileCat's SSL.com certificate, pinned by FILECAT_EXPECTED_SIGNER_SHA256
  In ESigner mode, nothing this script prints names the signer; run.json and the report go only into the draft.
#>
param(
    [Parameter(Mandatory)][ValidateSet('Build', 'Prepare', 'SignPayload', 'Package', 'SignInstaller', 'Draft')][string]$Phase,
    [Parameter(Mandatory)][string]$WorkDirectory,
    # Build: the package version, the runtimes to publish, and for a release candidate its product version and tag ref
    # (publish.ps1 checks them against each other and against the workflow's own ref and commit).
    [string]$Version,
    [ValidateSet('win-x64', 'win-arm64')][string[]]$Runtimes = @('win-x64'),
    [string]$ProductVersion,
    [string]$CandidateRef,
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
$manifestFile = Join-Path $WorkDirectory 'payload-manifest.json'
$stateFile = Join-Path $WorkDirectory 'run.json'

function Read-State { Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json }
function Save-State($State) { [IO.File]::WriteAllText($stateFile, ($State | ConvertTo-Json -Depth 6)) }
function Get-Sha256([string]$File) { (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash }
# Each runtime's self-contained and framework-dependent payload folders, as publish.ps1 -PayloadOnly writes them.
function Get-PayloadFolders([string[]]$RuntimeList) {
    foreach ($runtime in $RuntimeList) { (Join-Path $artifacts "publish/$runtime"); (Join-Path $artifacts "publish/$runtime-fdd") }
}
# publish.ps1's version arguments for this run: a candidate's product version and tag ref travel with every call.
function Get-VersionArguments($Source) {
    $arguments = @{ Version = $Source.Version }
    if ($Source.ProductVersion) { $arguments.ProductVersion = $Source.ProductVersion }
    if ($Source.CandidateRef) { $arguments.CandidateRef = $Source.CandidateRef }
    $arguments
}
function Get-PayloadInventory([string[]]$RuntimeList) {
    foreach ($root in @(Get-PayloadFolders $RuntimeList)) {
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
        $runtimeList = @($Runtimes | Select-Object -Unique)
        [void][IO.Directory]::CreateDirectory($WorkDirectory)
        $source = [ordered]@{ Version = $Version; ProductVersion = $ProductVersion; CandidateRef = $CandidateRef }
        $versionArguments = Get-VersionArguments $source
        foreach ($runtime in $runtimeList) {
            & (Join-Path $repo 'eng/publish.ps1') @versionArguments -Runtime $runtime -ArtifactDirectory $artifacts -PayloadOnly
        }
        $manifest = [ordered]@{
            Schema = 'filecat-payload-manifest/2'
            SourceCommit = "$(git -C $repo rev-parse HEAD)".Trim()
            Version = $Version
            ProductVersion = if ($ProductVersion) { $ProductVersion } else { $null }
            CandidateRef = if ($CandidateRef) { $CandidateRef } else { $null }
            Runtimes = $runtimeList
            Files = @(Get-PayloadInventory $runtimeList)
        }
        [IO.File]::WriteAllText($manifestFile, ($manifest | ConvertTo-Json -Depth 4))
        $sha = Get-Sha256 $manifestFile
        Write-Host "Unsigned payload ($($runtimeList -join ', ')): $($manifest.Files.Count) files; payload-manifest.json SHA-256 $sha"
        if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "payload_manifest_sha256=$sha" }
    }
    'Prepare' {
        if (-not $Mode -or -not $PayloadManifestSha256) { throw 'Prepare needs -Mode and -PayloadManifestSha256.' }
        if (Test-Path -LiteralPath $stateFile) { throw "This work directory was already prepared: $stateFile" }
        # The payload must be exactly what Build produced: the manifest by the hash Build reported, every file by the manifest.
        if ((Get-Sha256 $manifestFile) -ne $PayloadManifestSha256.ToUpperInvariant()) { throw 'payload-manifest.json differs from the one the build job reported.' }
        $manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
        if ($manifest.Schema -cne 'filecat-payload-manifest/2') { throw "Unknown payload manifest schema: $($manifest.Schema)" }
        $runtimeList = @($manifest.Runtimes)
        if (-not $runtimeList.Count -or @($runtimeList | Where-Object { $_ -cnotin 'win-x64', 'win-arm64' }).Count) { throw "Unexpected runtimes in the payload manifest: $($runtimeList -join ', ')" }
        $expected = @($manifest.Files | ForEach-Object { "$($_.Root)/$($_.Path)|$($_.Bytes)|$($_.SHA256)" })
        $actual = @(Get-PayloadInventory $runtimeList | ForEach-Object { "$($_.Root)/$($_.Path)|$($_.Bytes)|$($_.SHA256)" })
        $difference = @(Compare-Object -ReferenceObject $expected -DifferenceObject $actual -CaseSensitive)
        if ($difference.Count) { throw "The downloaded payload differs from its manifest in $($difference.Count) entries." }
        $sourceCommit = "$(git -C $repo rev-parse HEAD)".Trim()
        if ($manifest.SourceCommit -cne $sourceCommit) { throw "The payload was built from $($manifest.SourceCommit), not this checkout ($sourceCommit)." }
        Write-Host "Payload verified: $($expected.Count) files match the build job's manifest."

        # The SBOMs check every package the payload takes against the restored archives; restoring runs no package code.
        # RuntimeIdentifier, as dotnet publish -r sets it: restore's own -r would replace the projects' locked runtime list.
        foreach ($runtime in $runtimeList) {
            foreach ($project in 'src/FileCat.App/FileCat.App.csproj', 'src/FileCat.PrivilegedHost/FileCat.PrivilegedHost.csproj', 'src/FileCat.ShellHost/FileCat.ShellHost.csproj') {
                dotnet restore (Join-Path $repo $project) -p:RuntimeIdentifier=$runtime -p:SelfContained=true -p:PublishReadyToRun=true
                if ($LASTEXITCODE -ne 0) { throw "Locked restore failed: $project ($runtime)" }
            }
        }
        $jsign = & (Join-Path $PSScriptRoot 'Acquire-Jsign.ps1') -ToolRoot (Join-Path $WorkDirectory 'tools/jsign')
        $receipts = Join-Path $artifacts 'toolchains/inno-setup'
        $inno = & (Join-Path $repo 'eng/Acquire-InnoSetup.ps1') -ToolRoot (Join-Path $WorkDirectory 'tools/inno') -ReceiptDirectory $receipts
        $state = [ordered]@{
            Schema = 'filecat-signing-run/1'
            Mode = $Mode
            Version = $manifest.Version
            ProductVersion = $manifest.ProductVersion
            CandidateRef = $manifest.CandidateRef
            Runtimes = $runtimeList
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
        foreach ($runtime in @($state.Runtimes)) {
            $folders = @(Get-PayloadFolders $runtime)
            $result = & (Join-Path $PSScriptRoot 'Sign-WindowsPayload.ps1') -Path $folders -ManifestDirectory (Join-Path $artifacts "signing/$runtime")
            if ($state.Mode -eq 'ESignerSandbox' -and -not $state.ExpectedSignerSHA256) {
                # The sandbox signer is whatever SSL.com's demo credential holds. Its root, if the signature embeds one, is
                # the trust anchor: that proves the eSigner plumbing only; FileCat's own runs trust Windows' roots alone.
                # The first runtime's signer then pins the others, with a canary first like the real signer's.
                $state.ExpectedSignerSHA256 = $result.SignerSHA256
                $env:FILECAT_EXPECTED_SIGNER_SHA256 = $result.SignerSHA256
                . (Join-Path $PSScriptRoot 'Import-AuthenticodeInspector.ps1')
                $root = [FileCat.Signing.Authenticode]::Inspect((Join-Path $folders[0] 'FileCat.exe'), $false).EmbeddedCertificates |
                    Where-Object { $_.Subject -ceq $_.Issuer } | Select-Object -First 1
                if ($root) {
                    $state.TrustedRootFile = Join-Path $WorkDirectory 'sandbox-root.cer'
                    [IO.File]::WriteAllBytes($state.TrustedRootFile, $root.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
                }
            } elseif ($result.SignerSHA256 -cne $state.ExpectedSignerSHA256) {
                throw "The $runtime payload was signed by a certificate other than the pinned signer."
            }
            Write-Host "${runtime}: signed $($result.DistinctFilesSigned) distinct files into $($result.FilesSigned) places; $($result.VendorSigned) vendor-signed files unchanged."
        }
        Save-State $state
    }
    'Package' {
        $state = Read-State
        $versionArguments = Get-VersionArguments $state
        foreach ($runtime in @($state.Runtimes)) {
            & (Join-Path $repo 'eng/publish.ps1') @versionArguments -Runtime $runtime -ArtifactDirectory $artifacts -FromPayload
        }
        # No build server outlives this step into the next one, which holds the signing credentials.
        dotnet build-server shutdown | Out-Null
    }
    'SignInstaller' {
        $state = Read-State
        Set-SigningEnvironment $state
        $installers = foreach ($runtime in @($state.Runtimes)) {
            $installer = & (Join-Path $repo 'eng/Compile-WindowsInstaller.ps1') -Version ($state.Version -split '-')[0] -Architecture $runtime.Substring(4) `
                -CompilerRoot $state.InnoCompilerRoot -ReceiptDirectory $state.InnoReceipts -SourceCommit $state.SourceCommit -ArtifactDirectory $artifacts -Sign
            $receipt = Get-Content -LiteralPath (Join-Path $state.InnoReceipts "installer-$runtime.json") -Raw | ConvertFrom-Json
            if ($state.ExpectedSignerSHA256 -and $receipt.OutputSignerSHA256 -cne $state.ExpectedSignerSHA256) { throw "The $runtime Setup was signed by a certificate other than the pinned signer." }
            Write-Host "Signed $(Split-Path -Leaf $installer.Path) and its uninstaller."
            $installer.Path
        }
        $state.Outputs = [ordered]@{
            Payload = @(Get-PayloadFolders @($state.Runtimes))
            Zips = @(Get-ChildItem -LiteralPath $artifacts -File -Filter '*.zip' | Sort-Object Name | ForEach-Object FullName)
            Installers = @($installers)
        }
        Save-State $state
    }
    'Draft' {
        $state = Read-State
        if ($state.Mode -ne 'ESigner') { throw 'Only ESigner runs go to a draft release; dry runs keep their outputs on the runner.' }
        if (-not $env:GH_TOKEN -or -not $env:GITHUB_RUN_ID) { throw 'Draft runs in GitHub Actions with GH_TOKEN.' }
        # Whatever the run produced goes in, so a run that stopped part-way can still be examined privately.
        $assets = [System.Collections.Generic.List[string]]::new()
        Get-ChildItem -LiteralPath $artifacts -File | Where-Object { $_.Name -like '*-setup.exe' -or $_.Name -like '*.zip' -or $_.Name -like 'sbom-*.spdx.json' } |
            Sort-Object Name | ForEach-Object { $assets.Add($_.FullName) }
        # A release asset is named by its file name, so each runtime's signing manifests go in under names that hold it.
        $staged = Join-Path $WorkDirectory 'draft-records'
        [void][IO.Directory]::CreateDirectory($staged)
        $records = foreach ($runtime in @($state.Runtimes)) {
            foreach ($name in 'unsigned-inputs', 'signed-outputs') {
                $source = Join-Path $artifacts "signing/$runtime/$name.json"
                if (Test-Path -LiteralPath $source) {
                    $copy = Join-Path $staged "$name-$runtime.json"
                    Copy-Item -LiteralPath $source -Destination $copy
                    $copy
                }
            }
            (Join-Path $state.InnoReceipts "installer-$runtime.json"); (Join-Path $state.InnoReceipts "compile-$runtime.log")
        }
        foreach ($file in @(@($records) + @($manifestFile, $stateFile, (Join-Path $WorkDirectory 'verification.json')))) {
            if (Test-Path -LiteralPath $file) { $assets.Add($file) }
        }
        $tag = "signing-pilot-$env:GITHUB_RUN_ID"
        $title = "Signing pilot $env:GITHUB_RUN_ID - PRIVATE DRAFT, never publish (verification: $VerificationOutcome)"
        $notes = "Private signing pilot of FileCat $($state.Version) at $($state.SourceCommit), workflow run $env:GITHUB_RUN_ID. " +
            'It holds FileCat-signed files for the owner''s own verification (signatures, Smart App Control, installed bytes). ' +
            'Do not publish it; delete it when the verification is recorded.'
        # No --target: a workflow token may not create a release at a commit whose workflow files differ from the default
        # branch, and main moves on while a run signs. A draft creates no tag, and its notes and run.json name the source.
        gh release create $tag --repo $env:GITHUB_REPOSITORY --draft --title $title --notes $notes @assets | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Creating the private draft release failed.' }
        $draft = gh release view $tag --repo $env:GITHUB_REPOSITORY --json isDraft,assets --jq '"\(.isDraft) \(.assets | length)"'
        if ($draft -cne "true $($assets.Count)") { throw "The draft release is not what was uploaded: $draft" }
        Write-Host "Private draft release $tag holds $($assets.Count) files."
    }
}
