$ErrorActionPreference='Stop'
$root='C:\FileCatDsa20261104R2\crash-probe-r184'
if(Test-Path -LiteralPath $root){throw 'Fresh probe root required'}
New-Item -ItemType Directory -Path $root|Out-Null
$results=@()
for($n=1;$n -le 30;$n++){
 if(@(Get-Process -Name 'FileCat*' -ErrorAction SilentlyContinue).Count -or @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"|Where-Object{$_.CommandLine -match '(?i)FileCat.*[.]dll'}).Count){throw 'BUSY'}
 $casePath=Join-Path $root ('case'+$n);New-Item -ItemType Directory -Path $casePath|Out-Null
 $start=New-Object Diagnostics.ProcessStartInfo
 $start.FileName='C:\Program Files\dotnet\dotnet.exe'
 $start.Arguments='"C:\FileCatDsa20261104R2\native-r184\FileCat.CoreCrashHarness.dll" --analysis-scratch-hold "'+$casePath+'"'
 $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $start.EnvironmentVariables['FILECAT_TEST_SCRATCH_WINDOWS_ADAPTER']='C:\FileCatDsa20261104R2\native-r184\FileCat.Platform.Windows.dll'
 $child=[Diagnostics.Process]::Start($start)
 try{
  $ready=$child.StandardOutput.ReadLineAsync();if(!$ready.Wait(15000) -or $ready.Result -ne 'SCRATCH_READY'){throw 'Child not ready'}
  $foreign=Join-Path $casePath 'scratch\user-file.txt';[IO.File]::WriteAllText($foreign,'preserve me')
  $child.Kill();if(!$child.WaitForExit(10000)){throw 'Child did not exit'}
  $clock=[Diagnostics.Stopwatch]::StartNew();$entries=@([IO.Directory]::GetFileSystemEntries((Join-Path $casePath 'scratch')));$first=$entries.Count
  while($entries.Count -ne 1 -and $clock.ElapsedMilliseconds -lt 3000){Start-Sleep -Milliseconds 10;$entries=@([IO.Directory]::GetFileSystemEntries((Join-Path $casePath 'scratch')))}
  $results+=@{case=$n;firstEntries=$first;finalEntries=$entries.Count;delayMs=$clock.ElapsedMilliseconds;exited=$child.HasExited;foreignIntact=([IO.File]::ReadAllText($foreign) -eq 'preserve me')}
 }finally{if(!$child.HasExited){$child.Kill();$child.WaitForExit(10000)|Out-Null};$child.Dispose()}
}
$results|ConvertTo-Json -Depth 8|Set-Content (Join-Path $root 'results.json')
if(@($results|Where-Object{$_.finalEntries -ne 1 -or !$_.foreignIntact -or !$_.exited}).Count){exit 1}
