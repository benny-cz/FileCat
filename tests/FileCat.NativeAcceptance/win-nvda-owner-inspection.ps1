param([Parameter(Mandatory)][string]$AppPath, [Parameter(Mandatory)][string]$NvdaPath,
    [Parameter(Mandatory)][string]$ReportPath)
# GA1: a focused native readout check on an idle disposable VMware guest, with a private reader profile.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class OwnerInspectionInput {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint k, uint t);
    [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
    public static void Key(byte k, bool extended) {
        uint f = extended ? 1u : 0u; byte s = (byte)MapVirtualKey(k, 0);
        keybd_event(k,s,f,UIntPtr.Zero); keybd_event(k,s,f|2u,UIntPtr.Zero);
    }
    public static void Chord(byte[] keys) {
        foreach (byte k in keys) keybd_event(k,(byte)MapVirtualKey(k,0),0,UIntPtr.Zero);
        for (int i=keys.Length-1;i>=0;i--) keybd_event(keys[i],(byte)MapVirtualKey(keys[i],0),2,UIntPtr.Zero);
    }
}
'@
$AE = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]
function All($root) { @($root.FindAll($scope::Descendants,[System.Windows.Automation.Condition]::TrueCondition)) }
function Await([scriptblock]$check,[string]$label,[int]$seconds=20) {
    $clock=[Diagnostics.Stopwatch]::StartNew()
    while($clock.Elapsed.TotalSeconds -lt $seconds) {
        try { $answer=& $check; if($null -ne $answer -and $answer -ne $false){return $answer} }
        catch [System.Windows.Automation.ElementNotAvailableException] {}
        catch [Runtime.InteropServices.COMException] {}
        Start-Sleep -Milliseconds 150
    }
    throw "Timed out: $label"
}
function Named($root,[string]$name) { @(All $root | Where-Object {$_.Current.Name -eq $name})[0] }
function Key([byte]$key,[bool]$extended=$false) { [OwnerInspectionInput]::Key($key,$extended); Start-Sleep -Milliseconds 250 }
function Chord([byte[]]$keys) { [OwnerInspectionInput]::Chord($keys); Start-Sleep -Milliseconds 400 }
function Speech {
    $lines=@(Get-Content -LiteralPath $script:log -Encoding UTF8 -ErrorAction SilentlyContinue)
    $fresh=@($lines | Select-Object -Skip $script:speechIndex); $script:speechIndex=$lines.Count
    @($fresh | Where-Object {$_ -match 'Speaking \['} | ForEach-Object {$_.Substring($_.IndexOf('Speaking [')+9).Trim()})
}
function Inspect($main,[string]$name,[string[]]$expected) {
    $list=@(All $main | Where-Object {$_.Current.ClassName -eq 'FileList'})[0]
    if(!$list){throw 'No native FileList in owned app.'}
    $list.SetFocus(); Start-Sleep -Milliseconds 400
    $ownerHandle=[IntPtr]$script:app.MainWindowHandle
    [void][OwnerInspectionInput]::SetForegroundWindow($ownerHandle)
    if([OwnerInspectionInput]::GetForegroundWindow() -ne $ownerHandle){throw 'Owned app is not foreground; refusing keys.'}
    Key 0x24 $true; Key 0x28 $true # parent, then the first owned file
    if($name -eq 'b-offline.txt'){Key 0x28 $true}
    Start-Sleep -Milliseconds 1000
    [void](Speech)
    Chord @(0x11,0x10,0x49)
    $text=Await {Named $main 'Full values of focused item'} 'inspection text'
    $shown=$text.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    foreach($part in $expected){if(!$shown.Contains($part)){throw "Inspection lacks expected $part : $shown"}}
    $text.SetFocus(); Start-Sleep -Milliseconds 300
    Chord @(0x11,0x41)
    $heard=@(); $clock=[Diagnostics.Stopwatch]::StartNew()
    while($clock.Elapsed.TotalSeconds -lt 8) {
        $heard+=@(Speech)
        $combined=($heard -join ' ').Replace('\\','\')
        $missing=@($expected|Where-Object {!$combined.Contains($_)})
        if($missing.Count -eq 0){break}
        Start-Sleep -Milliseconds 200
    }
    if($missing.Count){throw "NVDA did not speak $($missing -join ', '): $($heard -join ' / ')"}
    Key 0x1B
    Await {!(Named $main 'Full values of focused item')} 'inspection close' | Out-Null
    [pscustomobject]@{File=$name;Shown=$shown;Spoken=$heard}
}
$root=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix=$root.TrimEnd('\')+'\'
$appPath=[IO.Path]::GetFullPath($AppPath); $nvdaPath=[IO.Path]::GetFullPath($NvdaPath); $report=[IO.Path]::GetFullPath($ReportPath)
$machine=Get-CimInstance Win32_ComputerSystem
if($machine.Manufacturer -notmatch 'VMware' -or [Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0 -or
    !$appPath.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or !$nvdaPath.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or
    !$report.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $report) -or
    (Get-Process -Name FileCat,nvda -ErrorAction SilentlyContinue) -or (Get-AuthenticodeSignature -LiteralPath $nvdaPath).Status -ne 'Valid') {
    throw 'Use an idle interactive VMware guest, Git-built app, signed existing NVDA and fresh private validation paths.'
}
$fixture=Join-Path (Split-Path $report) ('fixture-'+[Guid]::NewGuid().ToString('N'))
$data=Join-Path $fixture 'data'; $left=Join-Path $fixture 'left';$right=Join-Path $fixture 'right';$config=Join-Path $fixture 'nvda-config'
New-Item -ItemType Directory -Force -Path $data,$left,$right,$config | Out-Null
$owned=Join-Path $left 'a-owned.txt';$offline=Join-Path $left 'b-offline.txt'
[IO.File]::WriteAllText($owned,"Owned identity fixture`r`n");[IO.File]::WriteAllText($offline,"Detected Offline flag fixture`r`n")
$before=@((Get-FileHash -Algorithm SHA256 -LiteralPath $owned).Hash,(Get-FileHash -Algorithm SHA256 -LiteralPath $offline).Hash)
[IO.File]::SetAttributes($offline,([IO.File]::GetAttributes($offline) -bor [IO.FileAttributes]::Offline))
if(!([IO.File]::GetAttributes($offline) -band [IO.FileAttributes]::Offline)){throw 'Owned Offline fixture flag was not retained.'}
$sid=(Get-Acl -LiteralPath $owned).GetOwner([Security.Principal.SecurityIdentifier]).Value
[IO.File]::WriteAllText((Join-Path $data 'settings.json'),'{"SchemaVersion":1,"ColumnProfiles":[{"Name":"Owner validation","Columns":[{"Field":"name","Width":0},{"Field":"meta:owner","Width":260}]}]}')
[IO.File]::WriteAllText((Join-Path $config 'nvda.ini'),"[general]`r`nshowWelcomeDialogAtStartup = False`r`n[update]`r`nautoCheck = False`r`nstartupNotification = False`r`nallowUsageStats = False`r`naskedAllowUsageStats = True`r`n")
$script:log=Join-Path $fixture 'nvda.log';$script:speechIndex=0;$script:app=$null;$reader=$null
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$result=[ordered]@{Passed=$false;Stage='starting';Revision='d62b8f24';Computer=$env:COMPUTERNAME;User=$env:USERNAME;
    Session=[Diagnostics.Process]::GetCurrentProcess().SessionId;Elevated=(New-Object Security.Principal.WindowsPrincipal $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator);
    AppDllSha256=(Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path (Split-Path $appPath) 'FileCat.dll')).Hash;
    NvdaVersion=(Get-Item -LiteralPath $nvdaPath).VersionInfo.ProductVersion;OwnerSid=$sid;Fixture=$fixture;Checks=@();Error=$null}
try {
    $reader=Start-Process -FilePath $nvdaPath -WindowStyle Hidden -PassThru -ArgumentList @("--log-file=$script:log",'--log-level=12',"--config-path=$config",'--disable-addons')
    Await {(Test-Path -LiteralPath $script:log) -and (Select-String -LiteralPath $script:log -SimpleMatch 'NVDA initialized' -Quiet)} 'NVDA initialization' 30 | Out-Null
    $script:app=Start-Process -FilePath $appPath -WorkingDirectory (Split-Path $appPath) -PassThru -WindowStyle Normal -ArgumentList @('--new-instance','--data',$data,'--left',$left,'--right',$right)
    Await {$script:app.Refresh();$script:app.MainWindowHandle -ne [IntPtr]::Zero} 'owned app window' | Out-Null
    $main=$AE::FromHandle($script:app.MainWindowHandle)
    Start-Sleep -Seconds 3
    $result.Stage='current owner';$result.Checks+=Inspect $main 'a-owned.txt' @('a-owned.txt','Owner:',$sid)
    $result.Stage='detected placeholder';$result.Checks+=Inspect $main 'b-offline.txt' @('b-offline.txt','Owner:','Not read for a detected cloud placeholder.')
    $after=@((Get-FileHash -Algorithm SHA256 -LiteralPath $owned).Hash,(Get-FileHash -Algorithm SHA256 -LiteralPath $offline).Hash)
    if(($before -join ',') -ne ($after -join ',')){throw 'Owned fixture bytes changed.'}
    $result.SourceHashes=$after;$result.Passed=$true;$result.Stage='done'
} catch {$result.Error=$_.Exception.Message} finally {
    if($script:app -and !$script:app.HasExited){$script:app.Kill();[void]$script:app.WaitForExit(5000)}
    if($reader -and !$reader.HasExited){$reader.Kill();[void]$reader.WaitForExit(5000)}
    $result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $report -Encoding UTF8
}
