#Requires -Version 7
<#
.SYNOPSIS
Inventories evidence and reports retention advice without cleanup or scheduling.
.DESCRIPTION
Uses the Windows-native scanner, preserves source access times on its read
handles, skips reparse traversal, and verifies the known keepers/archive packages.
Unknown legacy evidence remains held for review. Reports are create-new.
.EXAMPLE
./eng/Write-EvidenceRetentionReport.ps1
.EXAMPLE
./eng/Write-EvidenceRetentionReport.ps1 -HashBudgetBytes 10000000000
#>
param(
    [string]$PolicyPath = (Join-Path $PSScriptRoot 'evidence-retention.policy.json'),
    [string]$OutputDirectory,
    [ValidateRange(0, [long]::MaxValue)][long]$HashBudgetBytes = 0,
    [string]$PythonPath = 'python.exe'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Python = (Get-Command $PythonPath -ErrorAction Stop).Source
$Arguments = @('-B', (Join-Path $PSScriptRoot 'evidence_retention.py'), '--policy',
    [IO.Path]::GetFullPath($PolicyPath), '--hash-budget-bytes', [string]$HashBudgetBytes)
if ($OutputDirectory) {
    $Arguments += @('--output', [IO.Path]::GetFullPath($OutputDirectory))
}
& $Python @Arguments
if ($LASTEXITCODE -ne 0) {
    throw "Evidence inventory reported a critical failure (exit $LASTEXITCODE). No cleanup is available."
}
