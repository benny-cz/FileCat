$ErrorActionPreference='Stop'
$rows=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -eq 'dotnet.exe' -or $_.Name -like 'FileCat*'} | Select-Object ProcessId,Name,CommandLine)
[ordered]@{nonce='disk-space-r364';observedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');processes=$rows} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath 'C:\FileCatDsa20261104R2\occupancy-r364.json'
