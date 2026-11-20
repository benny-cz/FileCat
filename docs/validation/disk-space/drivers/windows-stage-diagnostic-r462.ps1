$ErrorActionPreference='Stop';$failure=$null
try { & 'C:\FileCatDsa20261104R2\windows-native-inventory-r462.ps1' -Mode Stage -ExpectedHash '143cdc169421cab199bd444a1bebd79d575339c3a2bf62f198e037ef90f9829e' }
catch { $failure=[string]$_.Exception.Message }
finally { [ordered]@{error=$failure;lastExitCode=$LASTEXITCODE;appExists=(Test-Path 'C:\FileCatDsa20261104R2\desktop-r462-inventory\app\FileCat.exe')}|ConvertTo-Json|Set-Content 'C:\FileCatDsa20261104R2\stage-diagnostic-r462.json' }
