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
    [int]$TimeoutMinutes = 240,
    [ValidateRange(1, 600)][int]$HangMinutes = 20,
    # Optional 'NAME=value;NAME2=value' test environment; {run} expands to the run folder (for published assets).
    [ValidatePattern('^([A-Z][A-Z0-9_]{0,63}=[^;"]*)?(;[A-Z][A-Z0-9_]{0,63}=[^;"]*)*$')][string]$Env = ''

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
    @{ RunId = $RunId; Packet = $Packet; Sha256 = $Sha256.ToLowerInvariant(); Filter = $Filter; Projects = $Projects; User = $User; Env = $Env;
       Started = (Get-Date).ToUniversalTime().ToString('o');
       Os = (Get-CimInstance Win32_OperatingSystem | Select-Object -ExpandProperty Caption) + ' ' + [Environment]::OSVersion.Version;
       Sdk = (& $Dotnet --version); Cpus = [Environment]::ProcessorCount;
       # Timings depend on on-access scanning; the state is recorded (read only), never changed by this runner.
       Defender = $(try { $m = Get-MpComputerStatus; @{ RealTime = $m.RealTimeProtectionEnabled; OnAccess = $m.OnAccessProtectionEnabled;
           Behavior = $m.BehaviorMonitorEnabled; Exclusions = @((Get-MpPreference).ExclusionPath) } } catch { 'unavailable' }) } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'machine.json')
    $child = @'
param([string]$Run, [string]$Packet, [string]$Filter, [string]$Projects, [string]$Dotnet, [string]$Env, [int]$HangMinutes = 20)
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
    foreach ($pair in @($Env -split ';' | Where-Object { $_ })) {
        $key, $value = $pair -split '=', 2
        [Environment]::SetEnvironmentVariable($key, $value.Replace('{run}', $Run), 'Process')
    }
    $exits = @{}
    # Windows PowerShell 5.1 turns a native command's stderr line into an ErrorRecord; under Stop that would end the run at
    # the first failing test's output. Native outcomes are judged by exit codes and TRX files instead.
    $ErrorActionPreference = 'Continue'
    # A test project may name a shard after '#': PATH.csproj#ClassA+ClassB runs only tests whose name contains one of them,
    # PATH.csproj#!ClassA+ClassB every other test; each shard is its own test process with its own TRX and exit code.
    # FileCat's archive-worker gate is per process, so shards let worker-heavy classes use more than one CPU. A parallel:on
    # entry runs all test processes at the same time once every build has finished (default: one after another).
    $parallel = $false; $built = @{}; $tests = @()
    foreach ($entry in $Projects.Split(',')) {
        if ($entry -eq 'parallel:on') { $parallel = $true; continue }
        # build: only builds; publish: makes a self-contained win-x64 bundle in {run}\publish\NAME; otherwise build and test.
        $mode = 'test'; $project = $entry; $spec = ''
        if ($entry.StartsWith('build:')) { $mode = 'build'; $project = $entry.Substring(6) }
        elseif ($entry.StartsWith('publish:')) { $mode = 'publish'; $project = $entry.Substring(8) }
        elseif ($entry.Contains('#')) { $spec = $entry.Substring($entry.IndexOf('#') + 1); $project = $entry.Substring(0, $entry.IndexOf('#')) }
        $path = Join-Path $src $project; $name = [IO.Path]::GetFileNameWithoutExtension($project)
        if ($mode -eq 'publish') {
            & $Dotnet publish $path -c Release -r win-x64 --self-contained true -o (Join-Path $Run "publish\$name") --disable-build-servers -p:NuGetAudit=false -m:4 -v:minimal *> (Join-Path $Run "publish-$name.log")
            if ($LASTEXITCODE -ne 0) { throw "Publish failed: $name" }
            continue
        }
        if (-not $built.ContainsKey($path)) {
            & $Dotnet build $path -c Release --disable-build-servers -p:NuGetAudit=false -m:4 -v:minimal *> (Join-Path $Run "build-$name.log")
            if ($LASTEXITCODE -ne 0) { throw "Build failed: $name" }
            $built[$path] = $true
        }
        if ($mode -eq 'build') { continue }
        $testFilter = $Filter
        if ($spec) {
            $negated = $spec.StartsWith('!'); if ($negated) { $spec = $spec.Substring(1) }
            if ($spec -notmatch '^[A-Za-z0-9_.]+(\+[A-Za-z0-9_.]+)*$') { throw "Bad shard: $spec" }
            $parts = $spec.Split('+')
            if ($negated) { $shard = '(' + (($parts | ForEach-Object { "FullyQualifiedName!~$_" }) -join '&') + ')'; $name = "$name-not-" + ($parts -join '-') }
            else { $shard = '(' + (($parts | ForEach-Object { "FullyQualifiedName~$_" }) -join '|') + ')'; $name = "$name-" + ($parts -join '-') }
            $testFilter = if ($Filter -eq '*') { $shard } else { "($Filter)&$shard" }
        }
        $arguments = @('test', $path, '-c', 'Release', '--no-build', '--no-restore', '--disable-build-servers', '-p:NuGetAudit=false',
            '--logger', "trx;LogFileName=$name.trx", '--results-directory', (Join-Path $Run 'results'), '--blame-hang-timeout', "$($HangMinutes)m", '--blame-hang-dump-type', 'mini')
        if ($testFilter -ne '*') { $arguments += @('--filter', $testFilter) }
        "test ${name}: $testFilter" | Add-Content -LiteralPath (Join-Path $Run 'tests.txt')
        $tests += ,@($name, $arguments)
    }
    if ($parallel) {
        # Each job is its own PowerShell process with this process's environment; it returns the test run's exit code.
        $jobs = @{}
        foreach ($t in $tests) {
            $jobs[$t[0]] = Start-Job -ArgumentList $Dotnet, $t[1], (Join-Path $Run "test-$($t[0]).log") -ScriptBlock {
                param($dotnet, $arguments, $log) $list = [string[]]@($arguments); & $dotnet @list *> $log; $LASTEXITCODE }
        }
        foreach ($name in @($jobs.Keys)) { $exits[$name] = [int](@(Receive-Job -Job $jobs[$name] -Wait -AutoRemoveJob)[-1]) }
    } else {
        foreach ($t in $tests) { $arguments = $t[1]; & $Dotnet @arguments *> (Join-Path $Run "test-$($t[0]).log"); $exits[$t[0]] = $LASTEXITCODE }
    }
    $ErrorActionPreference = 'Stop'
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
    $launcher = "@echo off`r`nC:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$run\child.ps1`" -Run `"$run`" -Packet `"$Packet`" -Filter `"$Filter`" -Projects `"$Projects`" -Dotnet `"$Dotnet`" -Env `"$Env`" -HangMinutes $HangMinutes > `"$run\child.out`" 2>&1`r`n"
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
        $items = Get-ChildItem -LiteralPath $run -Force | Where-Object { $_.Name -notin @('src', 'publish') }
        Compress-Archive -Path $items.FullName -DestinationPath $evidence -Force
    }
}
