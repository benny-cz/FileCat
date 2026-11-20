$ErrorActionPreference='Stop'
$rows=@(Get-CimInstance Win32_Process)
$selected=@($rows|Where-Object{$_.Name -match '^(FileCat.*|dotnet)\.exe$' -or $_.ProcessId -in @(1332,6040)})
$result=@($selected|ForEach-Object{
 $item=$_; $owner=Invoke-CimMethod -InputObject $item -MethodName GetOwner -ErrorAction SilentlyContinue
 $parents=@();$parent=$item.ParentProcessId
 for($i=0;$i -lt 8 -and $parent;$i++){
  $found=$rows|Where-Object ProcessId -eq $parent|Select-Object -First 1
  if(!$found){break}
  $parents+=[ordered]@{pid=$found.ProcessId;name=$found.Name;commandLine=$found.CommandLine;creationDate=$found.CreationDate}
  $parent=$found.ParentProcessId
 }
 [ordered]@{pid=$item.ProcessId;parent=$item.ParentProcessId;name=$item.Name;commandLine=$item.CommandLine;executablePath=$item.ExecutablePath;owner=$owner.User;domain=$owner.Domain;ownerResult=$owner.ReturnValue;creationDate=$item.CreationDate;parents=$parents}
})
[ordered]@{observedUtc=[DateTime]::UtcNow.ToString('o');processes=$result}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath 'C:\FileCatDsa20261104R2\process-details-r769.json'
