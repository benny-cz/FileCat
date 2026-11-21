param([Parameter(Mandatory = $true)][string]$TrxPath)

$ErrorActionPreference = 'Stop'
[xml]$receipt = Get-Content -LiteralPath $TrxPath -Raw
$counts = $receipt.TestRun.ResultSummary.Counters
if ($null -eq $counts -or [int]$counts.total -lt 1000 -or [int]$counts.executed -lt 1000 -or [int]$counts.failed -ne 0) {
    throw "App CI shard has too few executed cases or a failure: $TrxPath"
}
Write-Output "App CI shard executed $($counts.executed) of $($counts.total) cases."
