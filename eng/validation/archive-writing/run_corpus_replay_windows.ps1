# Replay one fuzz image's collected regression corpus on Windows (test tooling, section 13.6): every target's folder
# runs once in one process through replay-corpus, -Jobs targets at a time (default 1), the folders with most files first.
# Reuses a structured-lane build of the same candidate. The Unix publication route's target is not replayed here.
param([string]$Work, [string]$Harness, [string]$Corpora, [string]$CorporaSha256, [int]$Jobs = 1)
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
    $folders = Get-ChildItem -Directory "$Work\corpora" |
        Sort-Object @{ Expression = { @(Get-ChildItem -File $_.FullName).Count }; Descending = $true }, Name
    $running = @{}
    function Reap {
        foreach ($name in @($running.Keys)) {
            if ($running[$name].HasExited) { Say "$name exit=$($running[$name].ExitCode)"; $running.Remove($name) }
        }
    }
    foreach ($folder in $folders) {
        if ($folder.Name -eq 'publication-races') { Say 'skip publication-races: the Unix publication route'; continue }
        while ($running.Count -ge $Jobs) { Start-Sleep -Seconds 2; Reap }
        $arguments = @($Harness, 'replay-corpus', $folder.Name, $folder.FullName, "$Work\reports\$($folder.Name).json")
        $process = Start-Process -FilePath "$env:DOTNET_ROOT\dotnet.exe" -ArgumentList $arguments -NoNewWindow -PassThru `
            -RedirectStandardOutput "$Work\reports\$($folder.Name).out" -RedirectStandardError "$Work\reports\$($folder.Name).err"
        $null = $process.Handle # Windows PowerShell keeps the exit code only for a process whose handle was opened.
        $running[$folder.Name] = $process
    }
    while ($running.Count -gt 0) { Start-Sleep -Seconds 2; Reap }
    Set-Content -Path "$Work\done.txt" -Value (Get-Date).ToUniversalTime().ToString('s')
    Say 'done'
}
catch { Say ('FAILED: ' + ($_ | Out-String)) }
