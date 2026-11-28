$ErrorActionPreference = 'Stop'

$hklm = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', 'Registry64')
try {
    $entry = $hklm.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6F3C2B5E-9A41-4E57-8C5E-1F2A3B4C5D6E}_is1')
    if ($null -eq $entry) {
        Write-Output 'FileCat is already absent from the 64-bit uninstall registry.'
        return
    }
    try {
        $command = $entry.GetValue('UninstallString')
        $location = $entry.GetValue('InstallLocation')
    }
    finally { $entry.Dispose() }
}
finally { $hklm.Dispose() }

# Use the registered executable, including alternate Inno names such as unins001.exe.
# Supply our own switches and never evaluate the registry value as a shell command.
if ($command -notmatch '^"(?<exe>(?:[A-Za-z]:\\|\\\\)[^"\r\n]+)"(?:\s|$)' -or -not $location) {
    throw 'The FileCat uninstall registration is missing a valid Inno Setup executable or install location.'
}
$uninstaller = $Matches['exe']
if ([IO.Path]::GetFileName($uninstaller) -notmatch '^unins[0-9]+\.exe\z' -or
    [IO.Path]::GetFullPath($location).TrimEnd('\') -ine [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($uninstaller)).TrimEnd('\') -or
    -not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
    throw "The registered FileCat uninstaller is missing or outside its install folder: $uninstaller"
}

$log = Join-Path ([IO.Path]::GetTempPath()) "FileCat-Chocolatey-uninstall-$([guid]::NewGuid()).log"
Write-Host "FileCat uninstall log: $log"
Uninstall-ChocolateyPackage -PackageName 'filecat' -FileType 'exe' -File $uninstaller `
    -SilentArgs "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$log`"" -ValidExitCodes @(0)
