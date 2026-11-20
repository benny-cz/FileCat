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
$fixture = Join-Path $repo 'tests/FileCat.NativeAcceptance/win-ftp-owned-stage.py'
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

function Read-ReadyPort([string]$path, [Diagnostics.Process]$server) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (!$server.HasExited -and $clock.Elapsed.TotalSeconds -lt 15) {
        try {
            $value = [IO.File]::ReadAllText($path).Trim()
        } catch [IO.IOException] {
            Start-Sleep -Milliseconds 100
            continue
        }
        if ($value -match '^[0-9]{1,5}$' -and [int]$value -ge 1 -and [int]$value -le 65535) {
            return [int]$value
        }
        throw 'The owned FTP server published an invalid port.'
    }
    throw 'The owned FTP server did not publish its port.'
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

$routes = [ordered]@{ ftp = 'serve'; ftpes = 'serve-tls'; ftps = 'serve-implicit-tls' }
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
        foreach ($arg in @($fixture, $routes[$protocol], $serverRoot, $ready)) { $start.ArgumentList.Add($arg) }
        $server = [Diagnostics.Process]::Start($start)
        $serverError = $server.StandardError.ReadToEndAsync()
        try {
            $port = Read-ReadyPort $ready $server
            $fingerprint = $null
            if ($protocol -ne 'ftp') {
                $certificate = Join-Path $folder 'owned-ftps-cert.pem'
                $fingerprint = (& $python $fixture fingerprint $certificate | Out-String).Trim()
                if ($LASTEXITCODE -ne 0 -or $fingerprint -notmatch '^[0-9A-F]{64}$') {
                    throw "The owned $protocol certificate fingerprint is unavailable."
                }
            }
            $lab.FILECAT_EFS_UPLOAD_LAB_PROTOCOL = $protocol
            $lab.FILECAT_EFS_UPLOAD_LAB_PORT = $port.ToString()
            $lab.FILECAT_EFS_UPLOAD_LAB_FINGERPRINT = $fingerprint
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
                certificateSha256 = $fingerprint
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
