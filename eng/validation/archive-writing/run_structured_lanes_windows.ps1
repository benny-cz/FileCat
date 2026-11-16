# Cross-RID structured mutation lanes on Windows (test tooling): build product + harness at the candidate from a source
# packet, then run every target's structured mutation for SecondsEach, Lanes targets at a time, on the image's seeds.
param([string]$Work, [string]$Packet, [string]$PacketSha256, [string]$Seeds, [int]$SecondsEach, [int]$Lanes, [int]$SeedBase = 42840001)
$ErrorActionPreference = 'Stop'
$log = Join-Path (Split-Path $Work) ((Split-Path $Work -Leaf) + '-lanes.log')
function Say($text) { Add-Content -Path $log -Value ((Get-Date).ToUniversalTime().ToString('s') + 'Z ' + $text) }
try {
    if ((Get-FileHash -Algorithm SHA256 $Packet).Hash.ToLowerInvariant() -ne $PacketSha256) { throw 'packet hash mismatch' }
    if (Test-Path $Work) { Remove-Item -Recurse -Force $Work }
    New-Item -ItemType Directory -Force "$Work\src", "$Work\seeds", "$Work\reports" | Out-Null
    tar -xzf $Packet -C "$Work\src"; if ($LASTEXITCODE) { throw 'packet extraction failed' }
    tar -xzf $Seeds -C "$Work\seeds"; if ($LASTEXITCODE) { throw 'seed extraction failed' }
    # The VM's SDK lives beside the test runner's, not in Program Files (that dotnet has only runtimes).
    $env:DOTNET_ROOT = 'C:\FileCatValidation\dotnet'; $env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'; $env:MSBUILDDISABLENODEREUSE = '1'
    $ErrorActionPreference = 'Continue'
    Push-Location "$Work\src"
    & dotnet build src\FileCat.Archives\FileCat.Archives.csproj -c Release -p:NuGetAudit=false -p:CopyLocalLockFileAssemblies=true --nologo -v q *> "$Work\build.log"
    if ($LASTEXITCODE) { throw 'product build failed' }
    & dotnet build eng\validation\archive-writing\ArchiveFuzzHarness\ArchiveFuzzHarness.csproj -c Release -p:NuGetAudit=false --nologo -v q *>> "$Work\build.log"
    if ($LASTEXITCODE) { throw 'harness build failed' }
    Pop-Location
    $ErrorActionPreference = 'Stop'
    $harness = "$Work\src\eng\validation\archive-writing\ArchiveFuzzHarness\bin\Release\net10.0\FileCat.ArchiveFuzzHarness.dll"
    Say 'built'
    $env:DOTNET_GCHeapHardLimit = '20000000'
    # A later pass needs another base: the same random seed repeats the same mutation sequence.
    $seed = $SeedBase; $running = @()
    foreach ($folder in Get-ChildItem -Directory "$Work\seeds" | Where-Object Name -like 'seeds-*' | Sort-Object Name) {
        $target = $folder.Name.Substring(6)
        if ($target -eq 'publication-races') { Say 'skip publication-races: the Unix publication route'; continue }
        while (@($running | Where-Object { -not $_.HasExited }).Count -ge $Lanes) { Start-Sleep -Seconds 5 }
        $arguments = "`"$harness`" structured-mutation $target `"$($folder.FullName)`" $SecondsEach $seed `"$Work\reports\$target.json`""
        $running += Start-Process -FilePath "$env:DOTNET_ROOT\dotnet.exe" -ArgumentList $arguments -NoNewWindow -PassThru `
            -RedirectStandardOutput "$Work\reports\$target.out" -RedirectStandardError "$Work\reports\$target.err"
        Say "started $target"; $seed++
    }
    $running | ForEach-Object { $_.WaitForExit() }
    Set-Content -Path "$Work\done.txt" -Value (Get-Date).ToUniversalTime().ToString('s')
    Say 'done'
}
catch { Say ('FAILED: ' + ($_ | Out-String) + ' ' + $_.InvocationInfo.PositionMessage + ' ' + $_.Exception.ToString()) }
