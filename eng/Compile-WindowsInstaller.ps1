<# Compiles the installer with verified tool inputs and retains its recipe/output identity. #>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [Parameter(Mandatory)][string]$CompilerRoot,
    [Parameter(Mandatory)][string]$ReceiptDirectory,
    [ValidatePattern('^[0-9a-f]{40}$')][string]$SourceCommit = $env:GITHUB_SHA,
    # The folder eng/publish.ps1 wrote the payload and its SBOMs to (its -ArtifactDirectory); artifacts/ by default.
    [string]$ArtifactDirectory
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$recipe = Join-Path $PSScriptRoot 'installer/FileCat.iss'
$artifacts = if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) { Join-Path $repo 'artifacts' } else { [IO.Path]::GetFullPath($ArtifactDirectory) }
$publish = Join-Path $artifacts "publish/win-$Architecture"
$output = Join-Path $artifacts "FileCat-$Version-win-$Architecture-setup.exe"
function RepoPath([string]$Path) {
    if ($Path.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { $Path.Substring($repo.Length + 1).Replace('\', '/') } else { $Path.Replace('\', '/') }
}
if (Test-Path -LiteralPath $output) { throw 'Installer output already exists; use a clean package workspace.' }
if (-not (Test-Path -LiteralPath $ReceiptDirectory -PathType Container)) { throw 'Compiler receipt directory is missing.' }
$compiler = & (Join-Path $PSScriptRoot 'Assert-InnoSetupCompiler.ps1') -ToolRoot $CompilerRoot
function InputInventory {
    $files = @(Get-ChildItem -LiteralPath $publish -Recurse -File)
    if (-not $files.Count) { throw 'Published installer payload is missing.' }
    $files += @(Get-Item -LiteralPath $recipe, (Join-Path $repo 'LICENSE'),
        (Join-Path $repo 'src/FileCat.App/Assets/filecat.ico'), (Join-Path $PSScriptRoot 'installer/licenses/InnoSetup-6.7.1.txt'))
    @($files | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{
            Path = RepoPath $_.FullName
            Bytes = $_.Length
            SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
}
$before = InputInventory
# The installer packages exactly the payload its setup SBOM describes, from the folder publish.ps1 wrote, into that folder.
$sboms = @(Get-ChildItem -LiteralPath $artifacts -File -Filter "sbom-$Version*-win-$Architecture-setup-payload.spdx.json")
if ($sboms.Count -ne 1) { throw "Expected one setup SBOM for $Version win-$Architecture in $artifacts; found $($sboms.Count)." }
$sbomCheck = & (Join-Path $PSScriptRoot 'Assert-SetupPayloadMatchesSbom.ps1') -Payload $publish -Sbom $sboms[0].FullName
$arguments = '"/DAppVersion=' + $Version + '" "/DArch=' + $Architecture + '" "/DPayloadDir=' + $publish + '" "/O' + $artifacts + '" "' + $recipe + '"'
$start = [Diagnostics.ProcessStartInfo]::new($compiler.Executable)
$start.Arguments = $arguments
$start.WorkingDirectory = $repo
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$child = [Diagnostics.Process]::new()
$child.StartInfo = $start
[void]$child.Start()
$stdout = $child.StandardOutput.ReadToEndAsync()
$stderr = $child.StandardError.ReadToEndAsync()
$timedOut = -not $child.WaitForExit(600000)
if ($timedOut) { $child.Kill(); $child.WaitForExit() }
$exitCode = $child.ExitCode
$child.Dispose()
$outText = $stdout.GetAwaiter().GetResult()
$errText = $stderr.GetAwaiter().GetResult()
[IO.File]::WriteAllText((Join-Path $ReceiptDirectory "compile-win-$Architecture.log"), $outText)
[IO.File]::WriteAllText((Join-Path $ReceiptDirectory "compile-win-$Architecture-stderr.log"), $errText)
$after = InputInventory
$unchanged = ($before | ConvertTo-Json -Depth 4 -Compress) -ceq ($after | ConvertTo-Json -Depth 4 -Compress)
$compilerAfter = & (Join-Path $PSScriptRoot 'Assert-InnoSetupCompiler.ps1') -ToolRoot $CompilerRoot
$outputPin = $null
if (Test-Path -LiteralPath $output) {
    $outputPin = [ordered]@{ Path = RepoPath $output; Bytes = (Get-Item -LiteralPath $output).Length
        SHA256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$receipt = [ordered]@{
    UTC = [DateTime]::UtcNow.ToString('o'); SourceCommit = $SourceCommit; Version = $Version; Architecture = $Architecture
    Arguments = $arguments; CompilerSHA256 = (Get-FileHash -LiteralPath $compiler.Executable -Algorithm SHA256).Hash.ToLowerInvariant()
    CompilerVersion = $compiler.Version; CompilerManifestSHA256 = $compiler.ManifestSHA256
    FrozenFilesVerifiedBeforeAndAfter = $compilerAfter.FilesVerified; Inputs = $before; InputsUnchanged = $unchanged
    SetupSbom = RepoPath $sbomCheck.Sbom; PayloadMatchesSetupSbom = $true
    ExitCode = $exitCode; TimedOut = $timedOut; Output = $outputPin
}
[IO.File]::WriteAllText((Join-Path $ReceiptDirectory "installer-win-$Architecture.json"), ($receipt | ConvertTo-Json -Depth 7))
Write-Host $outText
if ($errText) { Write-Host $errText }
if ($timedOut -or $exitCode -ne 0 -or -not $outputPin -or -not $unchanged) { throw 'Pinned installer compilation or input verification failed; receipt retained.' }
[pscustomobject]@{ Path = $output; SHA256 = $outputPin.SHA256; Bytes = $outputPin.Bytes }
