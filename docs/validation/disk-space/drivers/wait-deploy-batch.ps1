param(
 [Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision,
 [Parameter(Mandatory=$true)][ValidateSet('windows','mac','linux')][string]$Platform,
 [ValidateRange(30,1800)][int]$WaitSeconds=600
)
$ErrorActionPreference='Stop'
$tag=if($Platform-eq'linux'){'ubuntu'}else{$Platform}
$receipt=Join-Path $PWD ('.tmp/'+$tag+'-batch-'+$Revision+'-original-run.json')
# A fresh host receipt is an interprocess claim on this deployment attempt.
# Never restart an uploaded/extracted batch just because its state looks old.
$claim=[IO.File]::Open($receipt,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
$claim.Dispose()
function Record([string]$state,$code){
 [ordered]@{revision=$Revision;platform=$Platform;state=$state;exitCode=$code;observedUtc=[DateTime]::UtcNow.ToString('o');hostPid=$PID}|
  ConvertTo-Json|Set-Content -LiteralPath $receipt
}
$deadline=[DateTime]::UtcNow.AddSeconds($WaitSeconds)
Record 'waiting-for-idle' $null
try{
 while($true){
  & pwsh.exe -NoProfile -File (Join-Path $PSScriptRoot 'preflight-indexed-r732.ps1') -Platform $Platform -Revision ($Revision+'wait')
  $probe=$LASTEXITCODE
  if($probe-eq0){break}
  if($probe-ne75){throw ('Guest preflight failed with '+$probe)}
  if([DateTime]::UtcNow-ge$deadline){Record 'postponed-no-deployment' 75;exit 75}
  Start-Sleep -Seconds 30
 }
 Record 'running-original-deployment' $null
 & pwsh.exe -NoProfile -File (Join-Path $PSScriptRoot 'deploy-batch.ps1') -Revision $Revision -Platform $Platform
 $result=$LASTEXITCODE
 Record 'original-deployment-terminal' $result
 exit $result
}catch{
 Record 'wrapper-failed-inspect-original-processes-before-retry' 1
 throw
}
