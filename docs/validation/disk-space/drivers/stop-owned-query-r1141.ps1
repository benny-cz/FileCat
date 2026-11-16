$ErrorActionPreference='Stop'
$base='C:\FileCatDsa20261104R2\batch-r1141'
trap { [string]$_.Exception.Message|Set-Content -LiteralPath (Join-Path $base 'owned-stop-error.txt'); exit 1 }
$state=Get-Content -LiteralPath (Join-Path $base 'state.txt') -Raw
if($state -notmatch '^RUNNING phase=smoke runnerPid=(\d+)'){throw 'This cohort is not in its identified stalled phase'}
$runner=[int]$Matches[1]
$expected=Join-Path $base 'smoke-r1141.xml'
$targets=@(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"|Where-Object{$_.ParentProcessId-eq$runner-and$_.CommandLine.Contains($expected)-and$_.CommandLine.Contains('FileCat.Core.Tests.dll')})
if($targets.Count-ne1){throw 'Expected exactly this cohort child; refusing to stop another process'}
$target=$targets[0];$owner=Invoke-CimMethod -InputObject $target -MethodName GetOwner
if($owner.ReturnValue-ne0-or$owner.User-ne'filecatdsa_r2'){throw 'Unexpected test account'}
[ordered]@{observedUtc=[DateTime]::UtcNow.ToString('o');pid=$target.ProcessId;parent=$runner;owner=$owner.User;command=$target.CommandLine;reason='Diagnosed retained unexpected query after assertion; exact owned preflight child terminated';forced=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $base 'owned-stop-r1141.json')
Stop-Process -Id $target.ProcessId -Force
