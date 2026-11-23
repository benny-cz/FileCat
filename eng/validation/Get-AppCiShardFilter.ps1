param([Parameter(Mandatory = $true)][ValidateSet('focus', 'remainder')][string]$Shard)

# A positive OR and its negative AND partition every discovered test exactly once.
# The terms balance the 3,662-case App suite on x64, ARM64 and macOS.
$terms = @('Find', 'Compare', 'Edit', 'Archive', 'Analysis', 'QuickView', 'Icon', 'Metadata')
$operator = if ($Shard -eq 'focus') { '~' } else { '!~' }
$separator = if ($Shard -eq 'focus') { '|' } else { '&' }
$conditions = $terms | ForEach-Object { "FullyQualifiedName${operator}$_" }
Write-Output ($conditions -join $separator)
