<#
Build and run filtered tests from one hash-pinned source packet on the disposable Windows test VM.
Run this wrapper as the VM administrator (for example through vmrun runProgramInGuest, which has an
elevated batch token). The child runs as -User in that user's interactive desktop session with a
LIMITED (UAC-filtered, medium-integrity) token through a one-shot scheduled task, so tests see the same
token an ordinary signed-in user has. The wrapper refuses (status POSTPONED) while another FileCat/.NET
test process is running and seals C:\FileCatValidation\aw-claude\<RunId>-evidence.zip.
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]{1,60}$')][string]$RunId,
    [Parameter(Mandatory)][string]$Packet,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$Sha256,
    [Parameter(Mandatory)][string]$Filter,
    [Parameter(Mandatory)][string]$Projects,
    [string]$User = 'Admin',
    [string]$Dotnet = 'C:\FileCatValidation\dotnet\dotnet.exe',
    [int]$TimeoutMinutes = 240
)
$ErrorActionPreference = 'Stop'
$root = 'C:\FileCatValidation\aw-claude'
$run = Join-Path $root $RunId
$evidence = "$run-evidence.zip"
$status = "$run-status.txt"
$task = "FcRun-$RunId"
New-Item -ItemType Directory -Force -Path $root | Out-Null
if ((Test-Path -LiteralPath $run) -or (Test-Path -LiteralPath $evidence)) { throw 'Fresh run id required.' }
$busy = Get-Process -Name 'FileCat*', 'testhost*', 'vstest*', 'dotnet' -ErrorAction SilentlyContinue
if ($busy) {
    'POSTPONED: ' + (($busy | ForEach-Object { "$($_.Name):$($_.Id)" }) -join ',') | Set-Content -LiteralPath $status
    exit 3
}
try {
    if ((Get-FileHash -LiteralPath $Packet -Algorithm SHA256).Hash -ne $Sha256.ToUpperInvariant()) { throw 'Packet hash mismatch.' }
    if (-not (Test-Path -LiteralPath $Dotnet -PathType Leaf)) { throw "SDK missing: $Dotnet" }
    New-Item -ItemType Directory -Path $run, (Join-Path $run 'results') | Out-Null
    @{ RunId = $RunId; Packet = $Packet; Sha256 = $Sha256.ToLowerInvariant(); Filter = $Filter; Projects = $Projects; User = $User;
       Started = (Get-Date).ToUniversalTime().ToString('o');
       Os = (Get-CimInstance Win32_OperatingSystem | Select-Object -ExpandProperty Caption) + ' ' + [Environment]::OSVersion.Version;
       Sdk = (& $Dotnet --version) } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'machine.json')
    $child = @'
param([string]$Run, [string]$Packet, [string]$Filter, [string]$Projects, [string]$Dotnet)
$ErrorActionPreference = 'Stop'
try {
    Add-Type -Namespace FcToken -Name Native -MemberDefinition @"
[DllImport("advapi32.dll", SetLastError=true)] public static extern bool GetTokenInformation(System.IntPtr t, int c, out int v, int l, out int r);
"@
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $elevationType = 0; $ret = 0
    [void][FcToken.Native]::GetTokenInformation($identity.Token, 18, [ref]$elevationType, 4, [ref]$ret)  # TokenElevationType
    # TokenElevationType: 1 default (no split token), 2 full (elevated), 3 limited (UAC-filtered). PowerShell 5.1
    # runs on .NET Framework, where Environment.IsPrivilegedProcess does not exist.
    if ($elevationType -notin @(1, 3)) { throw "The child must run with a non-elevated token (elevation type $elevationType)." }
    $admin = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($admin) { throw 'The child token unexpectedly has an enabled Administrators group.' }
    @{ Name = $identity.Name; Sid = $identity.User.Value; EnabledAdministrators = $admin; ElevationType = $elevationType;
       Session = [Diagnostics.Process]::GetCurrentProcess().SessionId; Cpus = [Environment]::ProcessorCount } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Run 'account.json')
    $src = Join-Path $Run 'src'; New-Item -ItemType Directory -Path $src | Out-Null
    & tar.exe -xzf $Packet -C $src
    if ($LASTEXITCODE -ne 0) { throw 'Packet extraction failed.' }
    $env:DOTNET_ROOT = Split-Path $Dotnet; $env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:MSBUILDDISABLENODEREUSE = '1'; $env:DOTNET_NOLOGO = '1'
    $exits = @{}
    foreach ($project in $Projects.Split(',')) {
        $path = Join-Path $src $project; $name = [IO.Path]::GetFileNameWithoutExtension($project)
        & $Dotnet build $path -c Release --disable-build-servers -p:NuGetAudit=false -m:4 -v:minimal *> (Join-Path $Run "build-$name.log")
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $name" }
        $arguments = @('test', $path, '-c', 'Release', '--no-build', '--no-restore', '--disable-build-servers', '-p:NuGetAudit=false',
            '--logger', "trx;LogFileName=$name.trx", '--results-directory', (Join-Path $Run 'results'), '--blame-hang-timeout', '20m')
        if ($Filter -ne '*') { $arguments += @('--filter', $Filter) }
        & $Dotnet @arguments *> (Join-Path $Run "test-$name.log")
        $exits[$name] = $LASTEXITCODE
    }
    $suites = @{}; $failed = @(); $ok = $true
    foreach ($trx in Get-ChildItem (Join-Path $Run 'results') -Filter '*.trx') {
        [xml]$x = Get-Content -LiteralPath $trx.FullName -Raw
        $rows = @($x.TestRun.Results.UnitTestResult)
        $bad = @($rows | Where-Object { $_.outcome -notin @('Passed', 'NotExecuted') } | ForEach-Object { $_.testName })
        $suites[$trx.BaseName] = @{ total = $rows.Count; passed = @($rows | Where-Object { $_.outcome -eq 'Passed' }).Count;
            skipped = @($rows | Where-Object { $_.outcome -eq 'NotExecuted' }).Count; failed = $bad.Count;
            counters_total = [int]$x.TestRun.ResultSummary.Counters.total }
        $failed += $bad
        if ($bad.Count -ne 0 -or $rows.Count -eq 0 -or $rows.Count -ne [int]$x.TestRun.ResultSummary.Counters.total) { $ok = $false }
    }
    if ($suites.Count -eq 0 -or @($exits.Values | Where-Object { $_ -ne 0 }).Count -ne 0) { $ok = $false }
    @{ suites = $suites; failed = @($failed | Select-Object -First 200); exit_codes = $exits; ok = $ok } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Run 'summary.json')
    $(if ($ok) { 'PASS' } else { 'FAIL' }) | Set-Content -LiteralPath (Join-Path $Run 'status.txt')
} catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $Run 'error.txt')
    'FAIL' | Set-Content -LiteralPath (Join-Path $Run 'status.txt')
}
'@
    [IO.File]::WriteAllText((Join-Path $run 'child.ps1'), $child)
    $launcher = "@echo off`r`nC:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$run\child.ps1`" -Run `"$run`" -Packet `"$Packet`" -Filter `"$Filter`" -Projects `"$Projects`" -Dotnet `"$Dotnet`" > `"$run\child.out`" 2>&1`r`n"
    [IO.File]::WriteAllText((Join-Path $run 'child.cmd'), $launcher)
    & schtasks.exe /create /tn $task /tr "`"$run\child.cmd`"" /sc once /st 23:59 /ru $User /it /rl LIMITED /f *> (Join-Path $run 'task.log')
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the interactive limited task.' }
    & schtasks.exe /run /tn $task *>> (Join-Path $run 'task.log')
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the interactive limited task (is the user signed in?).' }
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while (-not (Test-Path -LiteralPath (Join-Path $run 'status.txt'))) {
        if ((Get-Date) -gt $deadline) { throw 'The child did not finish before its deadline.' }
        Start-Sleep -Seconds 10
    }
    Start-Sleep -Seconds 5
    @{ Finished = (Get-Date).ToUniversalTime().ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'launch.json')
    $leftover = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($run) }
    if ($leftover) {
        'leftover: ' + (($leftover | ForEach-Object { $_.ProcessId }) -join ',') | Add-Content -LiteralPath (Join-Path $run 'status.txt')
        $leftover | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    }
    (Get-Content -LiteralPath (Join-Path $run 'status.txt') -Raw).Trim() | Set-Content -LiteralPath $status
} catch {
    ('FAIL: ' + $_.Exception.Message) | Set-Content -LiteralPath $status
    if (Test-Path -LiteralPath $run) { $_ | Out-String | Set-Content -LiteralPath (Join-Path $run 'wrapper-error.txt') }
} finally {
    & schtasks.exe /delete /tn $task /f *> $null
    if (Test-Path -LiteralPath $run) {
        $items = Get-ChildItem -LiteralPath $run -Force | Where-Object { $_.Name -ne 'src' }
        Compress-Archive -Path $items.FullName -DestinationPath $evidence -Force
    }
}
