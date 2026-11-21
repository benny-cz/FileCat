# N-ENUM's case-distinct SMB fixture for a machine without a Samba server (the hosted Windows on Arm runner). The Windows
# SMB server refuses to list names that differ only in case (error 442, see Test-NativeSmbListing.ps1). This script serves
# a case-sensitive NTFS folder from a throwaway user-mode SMB server (impacket's smbserver.py, which may run under x64
# emulation) on a loopback alternative port. The native SMB client reaches it through a mapping with that port. The
# listing test then runs at medium integrity in its case-distinct mode.
param(
    [Parameter(Mandatory = $true)][string]$TestBinary,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][ValidateSet('X64','Arm64')][string]$Architecture,
    [Parameter(Mandatory = $true)][string]$ServerPython,
    [int]$Port = 4445,
    [switch]$RunTests
)

$ErrorActionPreference = 'Stop'
$binary = (Resolve-Path -LiteralPath $TestBinary).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$log = 'native-listing-smb-case.trx'
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -ne $Architecture -or
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString() -ne $Architecture) {
    throw "The SMB client must be native $Architecture Windows."
}
if ($RunTests) {
    . (Join-Path $PSScriptRoot 'Enter-MediumIntegrityTestProcess.ps1')
    $env:FILECAT_ENUM_SMB_ARCHITECTURE = $Architecture
    Remove-Item Env:FILECAT_ENUM_SMB_WINDOWS_SERVER -ErrorAction SilentlyContinue
    $filter = 'FullyQualifiedName~Native_listing_matches_managed_listing_on_a_prepared_mapped_and_unc_smb_folder'
    & dotnet test $binary --filter $filter --logger "trx;LogFileName=$log" --results-directory $evidence
    exit $LASTEXITCODE
}

if (Test-Path -LiteralPath $evidence) { throw 'The SMB evidence directory must be new.' }
$volume = Get-Volume -DriveLetter ([IO.Path]::GetPathRoot($evidence)[0])
if ($volume.FileSystem -ne 'NTFS') { throw 'The share fixture requires an NTFS directory.' }
$server = (Resolve-Path -LiteralPath (Join-Path (& $ServerPython -c "import sysconfig; print(sysconfig.get_path('scripts'))") 'smbserver.py')).Path
$impacket = & $ServerPython -c "import importlib.metadata as m; print(m.version('impacket'))"

$shareName = 'FILECATCASE'
$unc = "\\127.0.0.1\$shareName"
$sharePath = Join-Path $evidence 'share'
$cases = @('Case.txt', 'case.txt')
$contents = @('Upper', 'Lower case')
$unicodeName = 'Unicode ' + [char]0x540D + [char]0x524D + '.bin'
$user = 'filecat-fixture'
$password = 'Fc-' + [guid]::NewGuid().ToString('N')
$signing = (Get-SmbClientConfiguration).RequireSecuritySignature
$process = $null
$mapped = $null
New-Item -Path $evidence -ItemType Directory | Out-Null
try {
    [IO.Directory]::CreateDirectory($sharePath) | Out-Null
    & "$env:SystemRoot\System32\fsutil.exe" file setCaseSensitiveInfo $sharePath enable |
        Set-Content -LiteralPath (Join-Path $evidence 'case-sensitivity.txt')
    if ($LASTEXITCODE -ne 0) { throw 'The fixture directory did not enable case-sensitive names.' }
    for ($i = 0; $i -lt $cases.Length; $i++) { [IO.File]::WriteAllText((Join-Path $sharePath $cases[$i]), $contents[$i]) }
    if ([IO.File]::ReadAllText((Join-Path $sharePath $cases[0])) -ne $contents[0] -or
        [IO.File]::ReadAllText((Join-Path $sharePath $cases[1])) -ne $contents[1]) {
        throw 'The share folder did not retain both case-distinct files.'
    }
    [IO.File]::SetLastWriteTimeUtc((Join-Path $sharePath $cases[0]), [DateTime]::new(2024, 2, 29, 12, 0, 0, [DateTimeKind]::Utc))
    [IO.File]::WriteAllText((Join-Path $sharePath $unicodeName), 'Unicode filename')
    [IO.Directory]::CreateDirectory((Join-Path $sharePath 'owned-folder')) | Out-Null

    $process = Start-Process -FilePath $ServerPython -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $evidence 'smbserver.log') -RedirectStandardError (Join-Path $evidence 'smbserver.err') `
        -ArgumentList @("`"$server`"", '-smb2support', '-port', $Port, '-username', $user, '-password', $password, $shareName, "`"$sharePath`"")
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while (!(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)) {
        if ($process.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'The fixture SMB server did not start listening.' }
        Start-Sleep -Milliseconds 250
    }
    # The throwaway server does not sign; the client's signing requirement is restored in finally.
    Set-SmbClientConfiguration -RequireSecuritySignature $false -Force -Confirm:$false
    $letter = [char[]]([char]'Z'..[char]'G') | Where-Object { !(Test-Path "$($_):\") } | Select-Object -First 1
    if (!$letter) { throw 'No free drive letter for the fixture mapping.' }
    $mapped = "$($letter):"
    New-SmbMapping -LocalPath $mapped -RemotePath $unc -TcpPort $Port -UserName $user -Password $password | Out-Null
    $mappedRoot = "$mapped\"
    foreach ($path in $unc, $mappedRoot) {
        $names = [IO.Directory]::GetFileSystemEntries($path) | ForEach-Object { [IO.Path]::GetFileName($_) }
        foreach ($name in $cases + $unicodeName + 'owned-folder') {
            if ($names -cnotcontains $name) { throw "The SMB fixture at $path does not list $name." }
        }
    }
    $connection = Get-SmbConnection | Where-Object ShareName -eq $shareName | Select-Object -First 1
    if (!$connection) { throw 'The fixture did not establish an SMB connection to the owned share.' }
    $os = Get-CimInstance Win32_OperatingSystem
    [ordered]@{
        commit = $env:GITHUB_SHA
        architecture = $Architecture
        operatingSystem = $os.Caption
        build = $os.BuildNumber
        filesystem = $volume.FileSystem
        server = "impacket $impacket smbserver.py on 127.0.0.1:$Port"
        serverPython = (& $ServerPython -c "import sys; print(sys.version)")  # the build tag, e.g. (AMD64) under emulation
        smbDialect = $connection.Dialect
        mapped = $mappedRoot
        unc = $unc
        testAssembly = (Get-FileHash -LiteralPath $binary).Hash
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'smb-case-listing.json')

    $env:FILECAT_ENUM_MAPPED_SMB_ROOT = $mappedRoot
    $env:FILECAT_ENUM_NATIVE_SMB_ROOT = $unc
    $shell = (Get-Process -Id $PID).Path
    & $shell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $PSCommandPath -TestBinary $binary `
        -EvidenceDirectory $evidence -Architecture $Architecture -ServerPython $ServerPython -Port $Port -RunTests
    if ($LASTEXITCODE -ne 0) { throw "The case-distinct SMB listing comparison exited with code $LASTEXITCODE." }
    [xml]$receipt = Get-Content -LiteralPath (Join-Path $evidence $log) -Raw
    $counters = $receipt.TestRun.ResultSummary.Counters
    if ($counters.total -ne '1' -or $counters.executed -ne '1' -or $counters.passed -ne '1') {
        throw 'The case-distinct SMB fixture must execute and pass.'
    }
}
finally {
    try { if ($mapped) { Remove-SmbMapping -LocalPath $mapped -Force -Confirm:$false -ErrorAction SilentlyContinue } }
    finally {
        try { if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force } }
        finally { Set-SmbClientConfiguration -RequireSecuritySignature $signing -Force -Confirm:$false }
    }
}
