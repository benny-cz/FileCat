param([ValidateSet('Stage', 'Run')][string]$Mode)

# Disposable Win11 VM acceptance for AC-07. The orchestrator starts Stage with a
# limited token, then Run with both limited and highest tokens.
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'Temp\filecat-security-tests'
$profile = Join-Path $env:APPDATA 'FileCat\profiles\security-acceptance-20261028'
$report = Join-Path $root ($Mode.ToLowerInvariant() + '-' + [Security.Principal.WindowsIdentity]::GetCurrent().Name.Replace('\', '-') + '-' +
    ([Security.Principal.WindowsIdentity]::GetCurrent().Groups | Where-Object { $_.Value -like 'S-1-16-*' } | Select-Object -First 1).Value + '.txt')
New-Item -ItemType Directory -Path $root -Force | Out-Null

if ($Mode -eq 'Stage') {
    $zip = Join-Path $env:LOCALAPPDATA 'Temp\filecat-security-tests-win.zip'
    Expand-Archive -LiteralPath $zip -DestinationPath $root -Force
    $exe = Join-Path $root 'Marker.exe'
    if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Build the owned marker executable before medium-token staging.' }
    $baseTool = @{ Name = 'Owned medium-writer probe'; Executable = $exe;
        Arguments = @('{file}') }
    $settings = @{ SchemaVersion = 1; Editor = $baseTool;
        Associations = @(@{ Name = 'Owned association probe'; Executable = $exe;
            Arguments = @('{file}'); Intent = 'open'; Mask = '*.txt' });
        UserCommands = @(@{ Name = 'Owned command probe'; Executable = $exe;
            Arguments = @('{dir}') }) }
    New-Item -ItemType Directory -Path $profile -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $profile 'settings.json'), ($settings | ConvertTo-Json -Depth 8))
    [IO.File]::WriteAllText($report, "Staged=$profile`nWriter=$([Security.Principal.WindowsIdentity]::GetCurrent().Name)`nGroups=$((whoami /groups | Out-String))")
    exit 0
}

$env:FILECAT_SECURITY_VM_PERSISTED = '1'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$assembly = Join-Path $root 'FileCat.App.Tests.dll'
$output = & $dotnet $assembly -method 'FileCat.App.Tests.SecurityRouteTests.Native_Windows_persisted_medium_written_tools_cannot_gain_a_high_token' -reporter quiet 2>&1 | Out-String
$exit = $LASTEXITCODE
[IO.File]::WriteAllText($report, "Exit=$exit`nRunner=$output`nGroups=$((whoami /groups | Out-String))")
exit $exit
