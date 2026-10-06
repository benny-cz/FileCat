<# Compiles the installer with verified tool inputs and retains its recipe/output identity. #>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [Parameter(Mandatory)][string]$CompilerRoot,
    [Parameter(Mandatory)][string]$ReceiptDirectory,
    [ValidatePattern('^[0-9a-f]{40}$')][string]$SourceCommit = $env:GITHUB_SHA
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$recipe = Join-Path $PSScriptRoot 'installer/FileCat.iss'
$publish = Join-Path $repo "artifacts/publish/win-$Architecture"
$output = Join-Path $repo "artifacts/FileCat-$Version-win-$Architecture-setup.exe"
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
            Path = $_.FullName.Substring($repo.Length + 1).Replace('\', '/')
            Bytes = $_.Length
            SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
}
$before = InputInventory
$arguments = '"/DAppVersion=' + $Version + '" "/DArch=' + $Architecture + '" "' + $recipe + '"'
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
    $outputPin = [ordered]@{ Path = $output.Substring($repo.Length + 1).Replace('\', '/'); Bytes = (Get-Item -LiteralPath $output).Length
        SHA256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$receipt = [ordered]@{
    UTC = [DateTime]::UtcNow.ToString('o'); SourceCommit = $SourceCommit; Version = $Version; Architecture = $Architecture
    Arguments = $arguments; CompilerSHA256 = (Get-FileHash -LiteralPath $compiler.Executable -Algorithm SHA256).Hash.ToLowerInvariant()
    CompilerVersion = $compiler.Version; CompilerManifestSHA256 = $compiler.ManifestSHA256
    FrozenFilesVerifiedBeforeAndAfter = $compilerAfter.FilesVerified; Inputs = $before; InputsUnchanged = $unchanged
    ExitCode = $exitCode; TimedOut = $timedOut; Output = $outputPin
}
[IO.File]::WriteAllText((Join-Path $ReceiptDirectory "installer-win-$Architecture.json"), ($receipt | ConvertTo-Json -Depth 7))
Write-Host $outText
if ($errText) { Write-Host $errText }
if ($timedOut -or $exitCode -ne 0 -or -not $outputPin -or -not $unchanged) { throw 'Pinned installer compilation or input verification failed; receipt retained.' }
[pscustomobject]@{ Path = $output; SHA256 = $outputPin.SHA256; Bytes = $outputPin.Bytes }
