<# Producer eligibility only. Stable promotion requires approved retained artifacts and a separate publisher. #>
param(
    [Parameter(Mandatory)][string]$Reference,
    [Parameter(Mandatory)][ValidateSet('push','pull_request','workflow_dispatch')][string]$EventName
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Reference -match '[\r\n]') { throw 'A producer reference cannot contain newlines.' }
if ($EventName -eq 'pull_request') {
    if ($Reference -notmatch '^refs/pull/[1-9][0-9]*/(merge|head)$') { throw 'A pull-request producer requires its pull-request reference.' }
    [pscustomobject]@{ Reference=$Reference; EventName=$EventName; Mode='development'; StablePromotionAuthorized=$false }
    return
}
if ($Reference.StartsWith('refs/heads/', [StringComparison]::Ordinal) -and $Reference.Length -gt 'refs/heads/'.Length) {
    [pscustomobject]@{ Reference=$Reference; EventName=$EventName; Mode='development'; StablePromotionAuthorized=$false }
    return
}
$Number = '(0|[1-9][0-9]*)'
$ProductVersion = "$Number\.$Number\.$Number"
if ($Reference -match ("^refs/tags/v$ProductVersion(\+[0-9A-Za-z.-]+)?$")) {
    throw 'Stable references cannot invoke a producer. Approved manifest-only promotion is not configured.'
}
if ($Reference -notmatch ("^refs/tags/v$ProductVersion-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*$")) {
    throw 'A tagged producer requires an explicit valid prerelease reference.'
}
[pscustomobject]@{ Reference=$Reference; EventName=$EventName; Mode='prerelease'; StablePromotionAuthorized=$false }
