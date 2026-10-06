param([string]$Root, [string]$PayloadHash, [string]$ManifestHash)
$ErrorActionPreference='Stop'
function HashFile([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
if ((Split-Path -Parent $Root) -ne 'C:\FileCatReleaseValidation' -or (Split-Path -Leaf $Root) -notmatch '^broker-loader-[0-9a-f]{32}$') { throw 'Unexpected owned root' }
if ((HashFile (Join-Path $Root 'payload.zip')) -ne $PayloadHash -or (HashFile (Join-Path $Root 'manifest.json')) -ne $ManifestHash) { throw 'Transport changed' }
$Manifest=[IO.File]::ReadAllText((Join-Path $Root 'manifest.json'))|ConvertFrom-Json
$Identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]::new($Identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Native component controls require the guest administrative token' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Payload=Join-Path $Root 'payload'
[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $Root 'payload.zip'),$Payload)
function VerifyPayload {
 foreach($Pin in $Manifest.Files){$Path=Join-Path $Payload $Pin.Path;if((HashFile $Path)-ne$Pin.SHA256 -or (Get-Item -LiteralPath $Path).Length-ne$Pin.Bytes){throw ('Payload changed: '+$Pin.Path)}}
}
VerifyPayload
$Output=Join-Path $Root 'results';[void][IO.Directory]::CreateDirectory($Output)
$ProtectedParent=Join-Path $env:ProgramFiles 'FileCatValidation'
$ParentCreated=-not(Test-Path -LiteralPath $ProtectedParent)
$Protected=Join-Path $ProtectedParent (Split-Path -Leaf $Root)
if(Test-Path -LiteralPath $Protected){throw 'Protected fixture collision'}
[void][IO.Directory]::CreateDirectory($Protected)
$Hook=Join-Path $Payload 'hook/Hook.dll'
# Explicitly grant the ordinary account write access to the owned hook directory, never the protected broker folder.
$HookDirectory=Split-Path -Parent $Hook
$Acl=Get-Acl -LiteralPath $HookDirectory
$Rule=New-Object Security.AccessControl.FileSystemAccessRule($Identity.User,'Modify','ContainerInherit,ObjectInherit','None','Allow')
$Acl.AddAccessRule($Rule);Set-Acl -LiteralPath $HookDirectory -AclObject $Acl
function StartControl([string]$Label,[string]$Arguments,[bool]$DirectHook){
 $Marker=Join-Path $Output ($Label+'-marker.json');$Receipt=Join-Path $Output ($Label+'-launcher.json')
 $Info=New-Object Diagnostics.ProcessStartInfo;$Info.FileName=Join-Path $Payload 'launcher/Launcher.exe';$Info.Arguments=$Arguments;$Info.WorkingDirectory=$Root
 $Info.UseShellExecute=$false;$Info.CreateNoWindow=$true;$Info.RedirectStandardOutput=$true;$Info.RedirectStandardError=$true
 $Info.EnvironmentVariables.Remove('DOTNET_STARTUP_HOOKS')
 if($DirectHook){$Info.EnvironmentVariables['DOTNET_STARTUP_HOOKS']=$Hook;$Info.EnvironmentVariables['FILECAT_OWNED_HOOK_MARKER']=$Marker}
 $Process=New-Object Diagnostics.Process;$Process.StartInfo=$Info;[void]$Process.Start();$PidValue=$Process.Id
 $Stdout=$Process.StandardOutput.ReadToEndAsync();$Stderr=$Process.StandardError.ReadToEndAsync();$TimedOut=-not$Process.WaitForExit(20000)
 if($TimedOut){$Process.Kill();$Process.WaitForExit()};$Code=$Process.ExitCode
 [IO.File]::WriteAllText((Join-Path $Output ($Label+'-stdout.log')),$Stdout.GetAwaiter().GetResult());[IO.File]::WriteAllText((Join-Path $Output ($Label+'-stderr.log')),$Stderr.GetAwaiter().GetResult());$Process.Dispose()
 @{Executable=$Info.FileName;Arguments=$Arguments;PID=$PidValue;ExitCode=$Code;TimedOut=$TimedOut;DirectHookControl=$DirectHook}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $Output ($Label+'-command.json')) -Encoding UTF8
 if($TimedOut){throw 'Bounded launcher timed out'}
 if($DirectHook){if($Code-ne73 -or -not(Test-Path -LiteralPath $Marker)){throw 'Direct startup-hook positive control failed'}}
 elseif($Code-ne0 -or -not(Test-Path -LiteralPath $Receipt)){throw 'Actual broker launch did not produce its observation'}
}
try{
 StartControl 'control-before' '' $true
 foreach($Lane in @('sc','fdd')){
  $Source=Join-Path $Payload ('broker-'+$Lane);$Target=Join-Path $Protected $Lane;[void][IO.Directory]::CreateDirectory($Target)
  foreach($Pin in $Manifest.Files|Where-Object{$_.Path.StartsWith('broker-'+$Lane+'/')}){
   $Rel=$Pin.Path.Substring(('broker-'+$Lane+'/').Length);$Destination=Join-Path $Target $Rel
   [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Destination));Copy-Item -LiteralPath (Join-Path $Source $Rel) -Destination $Destination
   if((HashFile $Destination)-ne$Pin.SHA256){throw 'Protected broker copy changed'}
  }
  $Broker=Join-Path $Target 'FileCat.PrivilegedHost.exe';$Marker=Join-Path $Output ($Lane+'-marker.json');$Receipt=Join-Path $Output ($Lane+'-launcher.json')
  $Arguments='"'+$Broker+'" "'+$Hook+'" "'+$Marker+'" "'+$Receipt+'"'
  StartControl $Lane $Arguments $false
 }
 StartControl 'control-after' '' $true
 VerifyPayload
 $Owned=@(Get-CimInstance Win32_Process|Where-Object{($_.ExecutablePath-and$_.ExecutablePath.StartsWith($Payload+'\',[StringComparison]::OrdinalIgnoreCase))-or($_.ExecutablePath-and$_.ExecutablePath.StartsWith($Protected+'\',[StringComparison]::OrdinalIgnoreCase))})
 if($Owned.Count-ne0){throw 'Owned processes remain'}
}
finally{
 $Remaining=@(Get-CimInstance Win32_Process|Where-Object{$_.ExecutablePath-and$_.ExecutablePath.StartsWith($Protected+'\',[StringComparison]::OrdinalIgnoreCase)})
 if($Remaining.Count-ne0){throw 'Protected cleanup blocked by owned process; preserve fixture'}
 $Resolved=[IO.Path]::GetFullPath($Protected)
 if($Resolved-ne[IO.Path]::GetFullPath((Join-Path $env:ProgramFiles ('FileCatValidation\'+(Split-Path -Leaf $Root))))-or(Get-Item -LiteralPath $Protected).Attributes-band[IO.FileAttributes]::ReparsePoint){throw 'Unsafe protected cleanup path'}
 Remove-Item -LiteralPath $Protected -Recurse -Force
 if($ParentCreated -and @(Get-ChildItem -LiteralPath $ProtectedParent -Force).Count-eq0){[IO.Directory]::Delete($ProtectedParent)}
}
Copy-Item -LiteralPath (Join-Path $Root 'manifest.json') -Destination (Join-Path $Output 'manifest.json')
$Retained=@(Get-ChildItem -LiteralPath $Output -File|ForEach-Object{@{Path=$_.Name;SHA256=(HashFile $_.FullName);Bytes=$_.Length}})
$Result=@{UTC=[DateTime]::UtcNow.ToString('o');NativeRoot=$Root;Identity=$Identity.Name;SID=$Identity.User.Value;CallerAdministrator=$true;OS=[Environment]::OSVersion.VersionString;SourceCommit=$Manifest.SourceCommit;PayloadPinsVerified=$Manifest.Files.Count;OwnedProcessesAbsent=$true;OwnedProtectedFixtureRemoved=(-not(Test-Path -LiteralPath $Protected));MachineOrUserEnvironmentChanged=$false;HookFolderOrdinaryAccountModifyGranted=$true;LimitedCallerOrUACConsentTest=$false;NativeDialogObserved=$false;CandidateQualified=$false;RetainedPins=$Retained;UAC=(Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'|Select-Object EnableLUA,ConsentPromptBehaviorAdmin)}
$Result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $Output 'native-result.json') -Encoding UTF8
[IO.Compression.ZipFile]::CreateFromDirectory($Output,(Join-Path $Root 'outputs.zip'))
Write-Output 'Bounded actual broker controls and cleanup complete'
