param(
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$LogFileName,
    [string]$Filter,
    [switch]$BlameHang
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Enter-MediumIntegrityTestProcess.ps1')
$testArguments = @('test', $Project, '-c', 'Release', '--no-build', '--logger', "trx;LogFileName=$LogFileName")
if ($Filter) { $testArguments += @('--filter', $Filter) }
if ($BlameHang) { $testArguments += @('--blame-hang-timeout', '4m', '--blame-hang-dump-type', 'mini') }
& dotnet @testArguments
exit $LASTEXITCODE
