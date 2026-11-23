<#
.SYNOPSIS
  Refuses to continue unless a deployment environment is protected the way FileCat's signing credentials require.
.DESCRIPTION
  The signing job takes its credentials from this environment. The environment must already exist, because a job
  that names a missing environment would create an unprotected one. It must require a reviewer's approval, allow
  deployments only from main and v* tags, and not let administrators bypass those rules: agents work with the owner's
  administrator token, so only the reviewer gate keeps a signing run waiting for the owner. Reads GitHub's REST API,
  with GH_TOKEN when it is set.
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^[\w.-]+/[\w.-]+$')][string]$Repository,
    [ValidatePattern('^[\w.-]+$')][string]$Environment = 'signing'
)
$ErrorActionPreference = 'Stop'
function Get-GitHub([string]$Path) {
    $headers = @{ Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
    if ($env:GH_TOKEN) { $headers.Authorization = "Bearer $env:GH_TOKEN" }
    try {
        Invoke-RestMethod -Uri "https://api.github.com/$Path" -Headers $headers
    } catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return $null }
        throw
    }
}

$settings = Get-GitHub "repos/$Repository/environments/$Environment"
if (-not $settings) {
    throw "The '$Environment' environment does not exist. Create it with a required reviewer before any signing run; a job that names a missing environment would create an unprotected one."
}
$problems = [System.Collections.Generic.List[string]]::new()
$reviewRule = @($settings.protection_rules | Where-Object type -eq 'required_reviewers') | Select-Object -First 1
$reviewers = if ($reviewRule) { @($reviewRule.reviewers).Count } else { 0 }
if ($reviewers -eq 0) { $problems.Add('no reviewer must approve its deployments') }
if ($settings.can_admins_bypass -ne $false) { $problems.Add('administrators may bypass its protection rules') }
$allowed = @()
$policy = $settings.deployment_branch_policy
if (-not $policy) {
    $problems.Add('any branch or tag may deploy to it')
} elseif (-not $policy.custom_branch_policies) {
    $problems.Add('it follows branch protection instead of an explicit main and v* tag list')
} else {
    $allowed = @((Get-GitHub "repos/$Repository/environments/$Environment/deployment-branch-policies").branch_policies |
        ForEach-Object { "$($_.type):$($_.name)" })
    foreach ($entry in $allowed) {
        if ($entry -cne 'branch:main' -and $entry -cnotlike 'tag:v*') { $problems.Add("it allows deployments from $entry") }
    }
    if ('branch:main' -cnotin $allowed) { $problems.Add('it does not allow main, where signing runs start') }
}
if ($problems.Count) { throw "The '$Environment' environment is not protected enough for signing: $($problems -join '; ')." }
Write-Host "'$Environment' needs approval from one of $reviewers reviewer(s), allows only $($allowed -join ', ') and no administrator bypass."
