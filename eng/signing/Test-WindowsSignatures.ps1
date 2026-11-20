<#
.SYNOPSIS
  Independently verifies the Authenticode signature of every PE file FileCat ships.
.DESCRIPTION
  Every PE file under -Path (folders, files, and ZIP archives, which are extracted first) must be signed:
  - A file whose bytes are a vendor-signed input the signing manifest recorded unchanged keeps its vendor signature,
    which Windows must trust. Without a manifest, a vendor signature is accepted only from a -VendorSigner subject.
  - Every other PE file must carry exactly one signature by the -ExpectedSignerSha256 certificate: SHA-256 for the
    image digest (recomputed here, not only by Windows) and the signer, and one RFC 3161 timestamp that covers the
    signature and chains to a timestamping authority Windows trusts. The signer chain must be trusted by Windows,
    or, given -TrustedRootFile (dry runs only), end at that root.
  Negative controls built from a real signed output (tampered, stripped, untimestamped, foreign signer) must each
  fail; a verifier that would accept one of them is reported as broken.
  -Installer installs the setup silently into a temporary folder, verifies every installed PE file including the
  uninstaller, uninstalls, and requires the folder to be gone. It runs only on a GitHub Actions runner unless
  -AllowInstallOnThisMachine is given, and needs an elevated process.
#>
param(
    [Parameter(Mandatory)][string[]]$Path,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$ExpectedSignerSha256,
    [string[]]$SigningManifest = @(),
    [string]$TrustedRootFile,
    [string[]]$VendorSigner = @('Microsoft Corporation', '.NET', '.NET DAC', "AvaloniaUI O$([char]0xDC)"),
    [string]$Installer,
    [switch]$AllowInstallOnThisMachine,
    [switch]$CheckRevocation,
    [Parameter(Mandatory)][string]$ReportPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Import-AuthenticodeInspector.ps1')
$A = [FileCat.Signing.Authenticode]
$ExpectedSignerSha256 = $ExpectedSignerSha256.ToUpperInvariant()
$sha256Oids = @('2.16.840.1.101.3.4.2.1')
$strongImprints = @('2.16.840.1.101.3.4.2.1', '2.16.840.1.101.3.4.2.2', '2.16.840.1.101.3.4.2.3')
$codeSigningEku = '1.3.6.1.5.5.7.3.3'
$timestampingEku = '1.3.6.1.5.5.7.3.8'
$chainErrors = @(0x800B0109, 0x800B010A) | ForEach-Object { [int]$_ }   # CERT_E_UNTRUSTEDROOT, CERT_E_CHAINING
$badDigest = [int]0x80096010

$trustedRoot = $null
if ($TrustedRootFile) {
    $trustedRoot = [System.Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadCertificateFromFile((Resolve-Path -LiteralPath $TrustedRootFile).Path)
}
$vendorInputs = @{}
foreach ($manifestPath in $SigningManifest) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($file in @($manifest.Files | Where-Object Classification -eq 'Vendor')) {
        if ($file.InputSHA256 -cne $file.OutputSHA256) { throw "Signing manifest $manifestPath records a changed vendor file: $($file.Root)/$($file.Path)" }
        $vendorInputs[$file.InputSHA256.ToUpperInvariant()] = "$($file.Root)/$($file.Path)"
    }
}

function Get-CommonName($certificate) {
    if ($certificate) { $certificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) }
}

# Evaluates one file. Returns $null for a file that is not a PE image.
function Test-SignedFile([string]$File, [string]$Display, [switch]$RequireFileCatSigner, [string]$ExpectedSigner = $ExpectedSignerSha256) {
    $facts = $A::Inspect($File, $CheckRevocation.IsPresent)
    if (-not $facts.IsPe) { return $null }
    $failures = [System.Collections.Generic.List[string]]::new()
    $fileHash = (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash
    $signerSha = if ($facts.Signer) { $facts.Signer.GetCertHashString([System.Security.Cryptography.HashAlgorithmName]::SHA256) }
    $isVendorInput = $vendorInputs.ContainsKey($fileHash)
    $kind = if ($facts.SignatureBlobs -eq 0) { 'Unsigned' } elseif ($signerSha -ceq $ExpectedSigner) { 'FileCat' } else { 'Vendor' }
    if ($facts.Error) { $failures.Add("malformed: $($facts.Error)") }
    if ($kind -eq 'Unsigned') {
        $failures.Add('unsigned: the file carries no Authenticode signature')
    } else {
        if ($facts.SignatureBlobs -ne 1 -or $facts.SignerInfos -ne 1) { $failures.Add("signature-count: $($facts.SignatureBlobs) certificate-table entries, $($facts.SignerInfos) signers") }
        if ($facts.ContentType -cne [FileCat.Signing.Authenticode]::SpcIndirectData) { $failures.Add("content-type: $($facts.ContentType)") }
        if (-not $facts.SignatureVerifies) { $failures.Add('signature: the signer''s signature does not verify') }
        if ($facts.TrustResult -eq $badDigest -or -not $facts.SignedFileDigest -or $facts.SignedFileDigest -cne $facts.ComputedFileDigest) {
            $failures.Add('digest: the file''s bytes differ from the signed image digest')
        }
    }
    if ($kind -eq 'Vendor' -and ($RequireFileCatSigner -or -not $isVendorInput)) {
        if ($RequireFileCatSigner -or $vendorInputs.Count -gt 0 -or (Get-CommonName $facts.Signer) -notin $VendorSigner) {
            $failures.Add("signer: signed by '$(Get-CommonName $facts.Signer)' ($signerSha), not the FileCat signer and not an unchanged vendor input")
        }
    }
    if ($kind -eq 'Vendor' -and $facts.TrustResult -ne 0 -and $facts.TrustResult -ne $badDigest) {
        $failures.Add(('trust: Windows does not trust the vendor signature (0x{0:X8})' -f $facts.TrustResult))
    }
    if ($kind -eq 'FileCat') {
        if ($facts.TrustResult -ne 0 -and $facts.TrustResult -ne $badDigest) {
            if ($trustedRoot -and $facts.TrustResult -in $chainErrors) {
                $at = if ($facts.TimestampUtc) { $facts.TimestampUtc.UtcDateTime } else { [DateTime]::UtcNow }
                $chain = $A::ValidateChain($facts.Signer, $facts.EmbeddedCertificates, $trustedRoot, $at, $codeSigningEku)
                if ($chain) { $failures.Add("chain: $chain") }
            } else {
                $failures.Add(('trust: Windows does not trust the signature (0x{0:X8})' -f $facts.TrustResult))
            }
        }
        if ($facts.NestedSignatures -ne 0) { $failures.Add("nested: $($facts.NestedSignatures) nested signatures") }
        if ($facts.FileDigestAlgorithm -notin $sha256Oids -or $facts.SignerDigestAlgorithm -notin $sha256Oids) {
            $failures.Add("algorithm: image digest $($facts.FileDigestAlgorithm), signer digest $($facts.SignerDigestAlgorithm)")
        }
        if ($facts.Rfc3161Timestamps -ne 1) {
            $failures.Add("timestamp: expected one RFC 3161 timestamp, found $($facts.Rfc3161Timestamps)")
        } elseif (-not $facts.TimestampCoversSignature) {
            $failures.Add('timestamp: the RFC 3161 token does not cover this signature')
        } else {
            if ($facts.TimestampHashAlgorithm -notin $strongImprints) { $failures.Add("timestamp: imprint algorithm $($facts.TimestampHashAlgorithm)") }
            $tsChain = $A::ValidateChain($facts.TimestampSigner, $facts.TimestampCertificates, $null, $facts.TimestampUtc.UtcDateTime, $timestampingEku)
            if ($tsChain) { $failures.Add("timestamp-chain: $tsChain") }
        }
        if ($facts.LegacyCountersignature) { $failures.Add('timestamp: unexpected legacy countersignature') }
    }
    [pscustomobject]@{
        Path = $Display
        SHA256 = $fileHash
        Bytes = (Get-Item -LiteralPath $File).Length
        Kind = $kind
        Signer = Get-CommonName $facts.Signer
        SignerSHA256 = $signerSha
        WinVerifyTrust = ('0x{0:X8}' -f $facts.TrustResult)
        TimestampUtc = if ($facts.TimestampUtc) { $facts.TimestampUtc.UtcDateTime.ToString('o') }
        TimestampAuthority = Get-CommonName $facts.TimestampSigner
        Failures = @($failures)
        Pass = $failures.Count -eq 0
    }
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) ('FileCat-signature-check-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$results = [System.Collections.Generic.List[object]]::new()
$sources = [System.Collections.Generic.List[object]]::new()
# Display path -> file on disk, for outputs that stay readable afterwards (negative controls are built from these).
$localFiles = @{}

function Add-Tree([string]$Root, [string]$Label, [switch]$Keep) {
    foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
        $result = Test-SignedFile $file.FullName "$Label/$relative"
        if ($result) {
            $results.Add($result)
            if ($Keep) { $localFiles[$result.Path] = $file.FullName }
        }
    }
}

try {
    foreach ($item in $Path) {
        $resolved = (Resolve-Path -LiteralPath $item).Path
        $label = Split-Path -Leaf $resolved
        if (Test-Path -LiteralPath $resolved -PathType Container) {
            Add-Tree $resolved $label -Keep
            $sources.Add([pscustomobject]@{ Source = $label; Type = 'folder' })
        } elseif ($resolved -like '*.zip') {
            $extracted = Join-Path $scratch ('zip-' + [guid]::NewGuid().ToString('N'))
            Expand-Archive -LiteralPath $resolved -DestinationPath $extracted
            Add-Tree $extracted "$label!"
            $sources.Add([pscustomobject]@{ Source = $label; Type = 'zip'; SHA256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash })
        } else {
            $result = Test-SignedFile $resolved $label
            if (-not $result) { throw "Not a PE file: $item" }
            $results.Add($result)
            $localFiles[$result.Path] = $resolved
            $sources.Add([pscustomobject]@{ Source = $label; Type = 'file' })
        }
    }

    $installation = $null
    if ($Installer) {
        if ($env:GITHUB_ACTIONS -ne 'true' -and -not $AllowInstallOnThisMachine) { throw 'The installer round trip runs only on a disposable CI runner (or with -AllowInstallOnThisMachine).' }
        if (-not [Environment]::IsPrivilegedProcess) { throw 'The installer round trip needs an elevated process.' }
        $setup = (Resolve-Path -LiteralPath $Installer).Path
        $installDir = Join-Path $scratch 'installed'
        $log = Join-Path $scratch 'install.log'
        $install = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$installDir`"", "/LOG=`"$log`"") -Wait -PassThru
        if ($install.ExitCode -ne 0) { throw "Silent installation failed with exit code $($install.ExitCode)." }
        $before = $results.Count
        Add-Tree $installDir 'installed'
        $uninstaller = @(Get-ChildItem -LiteralPath $installDir -File -Filter 'unins*.exe')
        if ($uninstaller.Count -ne 1) { throw "Expected one uninstaller in the installed folder; found $($uninstaller.Count)." }
        $uninstall = Start-Process -FilePath $uninstaller[0].FullName -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru
        # The uninstaller re-runs itself from a temporary copy, so the folder disappears after the first process exits.
        $deadline = [DateTime]::UtcNow.AddMinutes(3)
        while ((Test-Path -LiteralPath $installDir) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Seconds 2 }
        $installation = [pscustomobject]@{
            Installer = Split-Path -Leaf $setup
            InstallExitCode = $install.ExitCode
            InstalledPeFiles = $results.Count - $before
            Uninstaller = $uninstaller[0].Name
            UninstallExitCode = $uninstall.ExitCode
            FolderRemoved = -not (Test-Path -LiteralPath $installDir)
        }
        if (-not $installation.FolderRemoved) { throw 'The uninstaller left the installation folder behind.' }
    }

    # Negative controls: real signed bytes, broken in one way each. Every one must be rejected for that reason.
    $controls = [System.Collections.Generic.List[object]]::new()
    function Add-Control([string]$Name, [string]$From, [string]$Expect, $Outcome) {
        $controls.Add([pscustomobject]@{ Control = $Name; From = $From; Expected = $Expect; Failures = $Outcome.Failures
            Rejected = -not $Outcome.Pass -and @($Outcome.Failures | Where-Object { $_ -like "${Expect}:*" }).Count -gt 0 })
    }
    $sample = $results | Where-Object { $_.Kind -eq 'FileCat' -and $_.Pass -and $localFiles.ContainsKey($_.Path) } | Select-Object -First 1
    $vendor = $results | Where-Object { $_.Kind -eq 'Vendor' -and $_.Pass -and $localFiles.ContainsKey($_.Path) } | Select-Object -First 1
    if (-not $sample) {
        Add-Control 'all' $null 'derivable' ([pscustomobject]@{ Pass = $true; Failures = @('no FileCat-signed file passed, so no control could be built') })
    } else {
        $sampleFile = $localFiles[$sample.Path]
        $bytes = [IO.File]::ReadAllBytes($sampleFile)
        $entry = 0; $offset = 0L; $size = 0L
        [void]$A::TryLocateCertificateTable($bytes, [ref]$entry, [ref]$offset, [ref]$size)
        $blob = $A::ReadSignatureBlobs($bytes, $offset, $size)[0]
        $tampered = [byte[]]$bytes.Clone()
        $pe = [BitConverter]::ToInt32($tampered, 0x3C)
        $tampered[$pe + 8] = $tampered[$pe + 8] -bxor 1   # COFF TimeDateStamp, covered by the image digest
        $built = [ordered]@{
            'tampered' = @{ Bytes = $tampered; Expect = 'digest' }
            'stripped' = @{ Bytes = $A::ReplaceSignature($bytes, $null); Expect = 'unsigned' }
            'untimestamped' = @{ Bytes = $A::ReplaceSignature($bytes, $A::WithoutTimestamp($blob)); Expect = 'timestamp' }
        }
        foreach ($name in $built.Keys) {
            $file = Join-Path $scratch "control-$name$([IO.Path]::GetExtension($sampleFile))"
            [IO.File]::WriteAllBytes($file, $built[$name].Bytes)
            Add-Control $name $sample.Path $built[$name].Expect (Test-SignedFile $file "control:$name")
        }
        if ($vendor) {
            Add-Control 'foreign-signer' $vendor.Path 'signer' (Test-SignedFile $localFiles[$vendor.Path] 'control:foreign-signer' -RequireFileCatSigner)
        } else {
            Add-Control 'foreign-signer' $sample.Path 'signer' (Test-SignedFile $sampleFile 'control:foreign-signer' -RequireFileCatSigner -ExpectedSigner ('0' * 64))
        }
    }

    $failed = @($results | Where-Object { -not $_.Pass })
    $blind = @($controls | Where-Object { -not $_.Rejected })
    $report = [ordered]@{
        Schema = 'filecat-signature-verification/1'
        UTC = [DateTime]::UtcNow.ToString('o')
        ExpectedSignerSHA256 = $ExpectedSignerSha256
        TrustAnchor = if ($trustedRoot) { "custom root $($trustedRoot.GetCertHashString([System.Security.Cryptography.HashAlgorithmName]::SHA256))" } else { 'Windows trusted roots' }
        RevocationChecked = $CheckRevocation.IsPresent
        VendorInputsFromManifest = $vendorInputs.Count
        Sources = @($sources)
        Summary = [ordered]@{
            PeFiles = $results.Count
            FileCatSigned = @($results | Where-Object Kind -eq 'FileCat').Count
            VendorSigned = @($results | Where-Object Kind -eq 'Vendor').Count
            Failed = $failed.Count
            NegativeControlsRejected = "$($controls.Count - $blind.Count)/$($controls.Count)"
        }
        Installation = $installation
        NegativeControls = @($controls)
        Files = @($results)
        Pass = $failed.Count -eq 0 -and $blind.Count -eq 0
    }
    [IO.Directory]::CreateDirectory((Split-Path -Parent ([IO.Path]::GetFullPath($ReportPath)))) | Out-Null
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($ReportPath), ($report | ConvertTo-Json -Depth 6))

    $lines = @('### Authenticode verification', '',
        "Trust anchor: $($report.TrustAnchor). Expected signer SHA-256: ``$ExpectedSignerSha256``.", '',
        '| PE files | FileCat-signed | Vendor-signed | Failed | Negative controls rejected |', '|---|---|---|---|---|',
        "| $($report.Summary.PeFiles) | $($report.Summary.FileCatSigned) | $($report.Summary.VendorSigned) | $($failed.Count) | $($report.Summary.NegativeControlsRejected) |")
    if ($installation) { $lines += '', "Installer round trip: $($installation.InstalledPeFiles) installed PE files verified, uninstaller ``$($installation.Uninstaller)``, folder removed: $($installation.FolderRemoved)." }
    foreach ($failure in $failed | Select-Object -First 25) { $lines += "- FAIL ``$($failure.Path)``: $($failure.Failures -join '; ')" }
    foreach ($control in $blind) { $lines += "- BLIND control ``$($control.Control)``: expected a '$($control.Expected)' rejection, got: $($control.Failures -join '; ')" }
    $lines | ForEach-Object { Write-Host $_ }
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ($lines -join "`n") }
    if (-not $report.Pass) { throw "Signature verification failed: $($failed.Count) files, $($blind.Count) negative controls not rejected. Report: $ReportPath" }
    [pscustomobject]$report.Summary
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
