# Replay one fuzz image's collected regression corpus on Windows (test tooling, section 13.6): every target's folder
# runs once in one process through replay-corpus. Reuses a structured-lane build of the same candidate. The Unix
# publication route's target is not replayed here.
param([string]$Work, [string]$Harness, [string]$Corpora, [string]$CorporaSha256)
$ErrorActionPreference = 'Stop'
$log = Join-Path (Split-Path $Work) ((Split-Path $Work -Leaf) + '-replay.log')
function Say($text) { Add-Content -Path $log -Value ((Get-Date).ToUniversalTime().ToString('s') + 'Z ' + $text) }
try {
    $s = [IO.File]::OpenRead($Corpora); try { $h = [Security.Cryptography.SHA256]::Create().ComputeHash($s) } finally { $s.Dispose() }
    if ((([BitConverter]::ToString($h)) -replace '-','').ToLowerInvariant() -ne $CorporaSha256) { throw 'corpora hash mismatch' }
    if (Test-Path $Work) { Remove-Item -Recurse -Force $Work }
    New-Item -ItemType Directory -Force "$Work\reports" | Out-Null
    & tar.exe -xzf $Corpora -C $Work; if ($LASTEXITCODE) { throw 'corpora extraction failed' }
    $env:DOTNET_ROOT = 'C:\FileCatValidation\dotnet'; $env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'; $env:DOTNET_GCHeapHardLimit = '20000000'
    $ErrorActionPreference = 'Continue'
    foreach ($folder in Get-ChildItem -Directory "$Work\corpora" | Sort-Object Name) {
        if ($folder.Name -eq 'publication-races') { Say 'skip publication-races: the Unix publication route'; continue }
        & "$env:DOTNET_ROOT\dotnet.exe" $Harness replay-corpus $folder.Name $folder.FullName "$Work\reports\$($folder.Name).json" *> "$Work\reports\$($folder.Name).out"
        Say "$($folder.Name) exit=$LASTEXITCODE"
    }
    Set-Content -Path "$Work\done.txt" -Value (Get-Date).ToUniversalTime().ToString('s')
    Say 'done'
}
catch { Say ('FAILED: ' + ($_ | Out-String)) }
