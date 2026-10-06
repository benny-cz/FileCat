<#
Acquires the exact official Inno Setup package into a new per-user tool directory.
The compiler's version resource is 0.0.0.0; content pins establish its identity.
InstallerFile supports an offline copy of the same pinned package. Receipts belong
to the build that used this tool and must be retained with its installer outputs.
#>
param(
    [Parameter(Mandatory)][string]$ToolRoot,
    [Parameter(Mandatory)][string]$ReceiptDirectory,
    [string]$InstallerFile
)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Inno Setup acquisition requires Windows.' }
foreach ($path in @($ToolRoot, $ReceiptDirectory)) {
    if (-not ([IO.Path]::IsPathRooted($path) -and [IO.Path]::GetPathRoot($path).Length -ge 3)) { throw 'Tool and receipt directories must be absolute.' }
    if (Test-Path -LiteralPath $path) { throw "Acquisition directory already exists: $path" }
}
$ToolRoot = [IO.Path]::GetFullPath($ToolRoot)
$ReceiptDirectory = [IO.Path]::GetFullPath($ReceiptDirectory)
$manifest = Join-Path $PSScriptRoot 'toolchains/inno-setup.json'
$pin = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
function AssertInstaller([string]$Path) {
    if ((Get-Item -LiteralPath $Path).Length -ne $pin.InstallerBytes -or
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pin.InstallerSHA256) {
        throw 'Inno Setup installer differs from the pinned official package.'
    }
}
if ($InstallerFile) { AssertInstaller $InstallerFile }
$existingCompiler = @(Get-ChildItem -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
    Get-ItemProperty | Where-Object { $_.DisplayName -like 'Inno Setup*' })
if ($existingCompiler.Count) { throw 'An existing per-user Inno Setup installation would share its registry entry; use an isolated build account.' }
[void][IO.Directory]::CreateDirectory($ToolRoot)
[void][IO.Directory]::CreateDirectory($ReceiptDirectory)
$installer = Join-Path $ToolRoot "innosetup-$($pin.Version).exe"
if ($InstallerFile) {
    Copy-Item -LiteralPath $InstallerFile -Destination $installer
} else {
    Invoke-WebRequest -Uri $pin.InstallerURL -OutFile $installer
}
AssertInstaller $installer
$compilerRoot = Join-Path $ToolRoot 'compiler'
$installArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/NOICONS', '/TASKS=""',
    ('/DIR="' + $compilerRoot + '"'), ('/LOG="' + (Join-Path $ReceiptDirectory 'install.log') + '"'))
$process = Start-Process -FilePath $installer -ArgumentList $installArguments -WorkingDirectory $ToolRoot -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Pinned Inno Setup installation failed: $($process.ExitCode)" }
$validated = & (Join-Path $PSScriptRoot 'Assert-InnoSetupCompiler.ps1') -ToolRoot $compilerRoot
$inventory = @(Get-ChildItem -LiteralPath $compilerRoot -Recurse -File | ForEach-Object {
    [pscustomobject]@{
        Path = $_.FullName.Substring($compilerRoot.Length + 1).Replace('\', '/')
        Bytes = $_.Length
        SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
$signature = Get-AuthenticodeSignature -LiteralPath $installer
$receipt = [ordered]@{
    Version = $pin.Version
    ReleaseURL = $pin.ReleaseURL
    InstallerURL = $pin.InstallerURL
    InstallerSHA256 = $pin.InstallerSHA256
    InstallerBytes = $pin.InstallerBytes
    InstallerSignatureStatus = $signature.Status.ToString()
    InstallerSignerThumbprint = $signature.SignerCertificate.Thumbprint
    ManifestSHA256 = $validated.ManifestSHA256
    FrozenFilesVerified = $validated.FilesVerified
    ToolRoot = $compilerRoot
    UTC = [DateTime]::UtcNow.ToString('o')
    InstallExitCode = $process.ExitCode
    Files = $inventory
}
[IO.File]::WriteAllText((Join-Path $ReceiptDirectory 'compiler.json'), ($receipt | ConvertTo-Json -Depth 6))
Write-Host "Verified Inno Setup $($pin.Version): $($validated.FilesVerified) frozen input files."
[pscustomobject]@{ Executable = $validated.Executable; ToolRoot = $compilerRoot; ReceiptDirectory = $ReceiptDirectory; Version = $pin.Version }
