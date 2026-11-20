#requires -Version 7.0
param(
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][ValidateSet('X64', 'Arm64')][string]$Architecture
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $repo 'tests/FileCat.App.Tests'
$assembly = Join-Path $project 'bin/Release/net10.0/FileCat.App.Tests.dll'
$controller = Join-Path $PSScriptRoot 'run-medium-integrity-test.ps1'
$ftpFixture = Join-Path $repo 'tests/FileCat.NativeAcceptance/win-ftp-owned-stage.py'
$sftpFixture = Join-Path $repo 'tests/FileCat.NativeAcceptance/win-sftp-owned-stage.py'
$python = (Get-Command python -CommandType Application | Select-Object -First 1).Source
$shell = (Get-Process -Id $PID).Path
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $root) { throw 'Use a fresh evidence directory.' }
if (!(Test-Path -LiteralPath $assembly -PathType Leaf)) { throw 'Build the Release App test assembly before running qualification.' }
$volume = Get-Volume -DriveLetter ([IO.Path]::GetPathRoot($root)[0])
if ($volume.FileSystem -ne 'NTFS' -or $volume.AllocationUnitSize -ne 4096) {
    throw 'The EFS source fixture requires 4 KiB NTFS.'
}
New-Item -ItemType Directory -Path $root | Out-Null

function Read-ReadyEndpoint([string]$path, [Diagnostics.Process]$server, [string]$protocol) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (!$server.HasExited -and $clock.Elapsed.TotalSeconds -lt 15) {
        try {
            $lines = [IO.File]::ReadAllLines($path)
        } catch [IO.IOException] {
            Start-Sleep -Milliseconds 100
            continue
        }
        $expectedLines = if ($protocol -eq 'sftp') { 2 } else { 1 }
        if ($lines.Length -ne $expectedLines -or $lines[0] -notmatch '^[0-9]{1,5}$' -or
            [int]$lines[0] -lt 1 -or [int]$lines[0] -gt 65535) {
            throw "The owned $protocol server published an invalid endpoint."
        }
        if ($protocol -eq 'sftp' -and $lines[1] -notmatch '^SHA256:[a-zA-Z0-9+/]{43}$') {
            throw 'The owned SFTP server published an invalid host-key fingerprint.'
        }
        return @{ Port = [int]$lines[0]; HostKey = $lines[1] }
    }
    throw "The owned $protocol server did not publish its endpoint."
}

function Set-TestEnvironment([System.Collections.IDictionary]$values) {
    foreach ($name in $values.Keys) {
        $value = $values[$name]
        if ($null -eq $value) {
            [Environment]::SetEnvironmentVariable($name, [NullString]::Value, 'Process')
        } else {
            [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        }
    }
}

$os = Get-CimInstance Win32_OperatingSystem
[ordered]@{
    revision = (git -C $repo rev-parse HEAD).Trim()
    architecture = $Architecture
    osBuild = $os.BuildNumber
    allocationUnit = $volume.AllocationUnitSize
    appTestAssembly = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'environment.json')

$lab = [ordered]@{
    FILECAT_TEST_EFS = '1'
    FILECAT_EFS_UPLOAD_LAB_HOST = '127.0.0.1'
    FILECAT_EFS_UPLOAD_LAB_USER = 'tester'
    FILECAT_EFS_UPLOAD_LAB_PASSWORD = 'secret-pass'
    FILECAT_EFS_UPLOAD_LAB_ROOT = '/'
    FILECAT_EFS_UPLOAD_LAB_ARCHITECTURE = $Architecture
    FILECAT_EFS_UPLOAD_LAB_KEY = $null
    FILECAT_EFS_UPLOAD_LAB_PROTOCOL = $null
    FILECAT_EFS_UPLOAD_LAB_PORT = $null
    FILECAT_EFS_UPLOAD_LAB_FINGERPRINT = $null
    TMP = (Join-Path $root 'source-temp')
    TEMP = (Join-Path $root 'source-temp')
}
$previous = @{}
foreach ($name in $lab.Keys) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
New-Item -ItemType Directory -Path $lab.TMP | Out-Null

$routes = [ordered]@{ ftp = 'serve'; ftpes = 'serve-tls'; ftps = 'serve-implicit-tls'; sftp = '' }
try {
    foreach ($protocol in $routes.Keys) {
        $folder = Join-Path $root $protocol
        $serverRoot = Join-Path $folder 'server'
        New-Item -ItemType Directory -Path $serverRoot | Out-Null
        $ready = Join-Path $folder 'ready.txt'
        $start = [Diagnostics.ProcessStartInfo]::new($python)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardError = $true
        $serverArgs = if ($protocol -eq 'sftp') {
            @($sftpFixture, $serverRoot, $ready)
        } else {
            @($ftpFixture, $routes[$protocol], $serverRoot, $ready)
        }
        foreach ($arg in $serverArgs) { $start.ArgumentList.Add($arg) }
        $server = [Diagnostics.Process]::Start($start)
        $serverError = $server.StandardError.ReadToEndAsync()
        try {
            $endpoint = Read-ReadyEndpoint $ready $server $protocol
            $certificateSha256 = $null
            if ($protocol -in @('ftpes', 'ftps')) {
                $certificate = Join-Path $folder 'owned-ftps-cert.pem'
                $certificateSha256 = (& $python $ftpFixture fingerprint $certificate | Out-String).Trim()
                if ($LASTEXITCODE -ne 0 -or $certificateSha256 -notmatch '^[0-9A-F]{64}$') {
                    throw "The owned $protocol certificate fingerprint is unavailable."
                }
            }
            $lab.FILECAT_EFS_UPLOAD_LAB_PROTOCOL = $protocol
            $lab.FILECAT_EFS_UPLOAD_LAB_PORT = $endpoint.Port.ToString()
            $lab.FILECAT_EFS_UPLOAD_LAB_FINGERPRINT = if ($protocol -eq 'sftp') { $endpoint.HostKey } else { $certificateSha256 }
            Set-TestEnvironment $lab

            # The child lowers its own token. The parent retains ownership of its server process.
            $log = "native-efs-live-$protocol-$([guid]::NewGuid().ToString('N')).trx"
            & $shell -NoProfile -NonInteractive -File $controller -Project $project -LogFileName $log -BlameHang `
                -Filter 'FullyQualifiedName~FileCat.App.Tests.EfsLiveRemoteUploadTests'
            $testExit = $LASTEXITCODE
            $result = Join-Path $project "TestResults/$log"
            if (!(Test-Path -LiteralPath $result -PathType Leaf)) { throw "The $protocol tests produced no receipt (exit $testExit)." }
            $receipt = Join-Path $root "$protocol.trx"
            Copy-Item -LiteralPath $result -Destination $receipt
            [xml]$xml = Get-Content -LiteralPath $receipt -Raw
            $counters = $xml.TestRun.ResultSummary.Counters
            if ($testExit -ne 0 -or $counters.total -ne '2' -or $counters.executed -ne '2' -or $counters.passed -ne '2') {
                throw "Both live EFS $protocol cases must execute and pass."
            }
            if (Get-ChildItem -LiteralPath $serverRoot -Force) { throw "The $protocol fixture retained remote output." }
            [ordered]@{
                protocol = $protocol
                certificateSha256 = $certificateSha256
                hostKeySha256 = $endpoint.HostKey
                passed = 2
                remoteFixtureEmpty = $true
                trxSha256 = (Get-FileHash -LiteralPath $receipt -Algorithm SHA256).Hash
            } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root "$protocol.json")
        }
        finally {
            if (!$server.HasExited) { $server.Kill() }
            if (!$server.WaitForExit(5000)) { throw "The owned $protocol server did not exit." }
            $serverError.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $root "$protocol-server.log")
            $server.Dispose()
        }
    }
}
finally {
    Set-TestEnvironment $previous
}
