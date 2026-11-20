$ErrorActionPreference='Stop'
$taskName='FileCatDsaR2-R1109'
$script='C:\FileCatDsa20261104R2\recycle-r1109-run.ps1'
if((Get-FileHash -LiteralPath $script -Algorithm SHA256).Hash-ne'1D7B0793147E559B52C4AD8D5B8ACDFFF621DD828B9F5D544257C8CBABB08762'){throw 'Runner changed'}
if(Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue){throw 'A fresh task is required'}
if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){throw 'Another test instance is running'}
$principal=New-ScheduledTaskPrincipal -UserId ($env:COMPUTERNAME+'\filecatdsa_r2') -LogonType Interactive -RunLevel Limited
$action=New-ScheduledTaskAction -Execute 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -Argument '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File C:\FileCatDsa20261104R2\recycle-r1109-run.ps1'
$settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 5)
Register-ScheduledTask -TaskName $taskName -Principal $principal -Action $action -Settings $settings|Out-Null
$began=Get-Date
Start-ScheduledTask -TaskName $taskName
$deadline=(Get-Date).AddMinutes(4)
do{
 Start-Sleep -Milliseconds 500
 $state=Get-ScheduledTask -TaskName $taskName
 $info=Get-ScheduledTaskInfo -TaskName $taskName
 if($info.LastRunTime-ge$began.AddSeconds(-2)-and$state.State-ne'Running'){
  $result=$info.LastTaskResult
  Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
  exit $result
 }
}while((Get-Date)-lt$deadline)
throw 'The actual scheduled task is still running; inspect it without restarting'