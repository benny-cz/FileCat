#Requires -Version 7
param(
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Project = [IO.Path]::GetFullPath($Project)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$LockPath = Join-Path (Split-Path $Project -Parent) 'packages.lock.json'
$Lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json -AsHashtable
if ($Lock.version -ne 2 -or -not $Lock.dependencies.ContainsKey('net10.0')) {
    throw 'The package inventory requires the reviewed net10.0 dependency lock.'
}
$Expected = @{}
foreach ($Entry in $Lock.dependencies['net10.0'].GetEnumerator()) {
    if ($Entry.Value.type -eq 'Project') { continue }
    if ($Entry.Value.type -notin @('Direct', 'Transitive', 'CentralTransitive') -or
        [string]::IsNullOrWhiteSpace($Entry.Value.resolved)) {
        throw 'Unexpected locked package identity.'
    }
    $Expected.Add($Entry.Key, $Entry.Value)
}

# Listing must not restore or silently replace the frozen dependency graph.
$Lines = @(& dotnet list $Project package --include-transitive --format json --no-restore)
$ExitCode = $LASTEXITCODE
if ($ExitCode -ne 0) { throw "Package inventory command failed ($ExitCode)." }
$Text = $Lines -join "`n"
$Inventory = $Text | ConvertFrom-Json -AsHashtable
if ($Inventory.version -ne 1 -or @($Inventory.projects).Count -ne 1 -or
    $Inventory.parameters -notmatch '--include-transitive') {
    throw 'Unexpected package inventory schema or scope.'
}
$ListedProject = $Inventory.projects[0]
if ([IO.Path]::GetFullPath($ListedProject.path) -ne $Project -or
    @($ListedProject.frameworks).Count -ne 1 -or $ListedProject.frameworks[0].framework -ne 'net10.0') {
    throw 'Package inventory project or framework differs from the requested scope.'
}
$Seen = @{}
$Framework = $ListedProject.frameworks[0]
foreach ($Category in @('topLevelPackages', 'transitivePackages')) {
    foreach ($Package in @($Framework[$Category])) {
        if (-not $Package -or [string]::IsNullOrWhiteSpace($Package.id) -or $Seen.ContainsKey($Package.id) -or
            -not $Expected.ContainsKey($Package.id) -or $Package.resolvedVersion -ne $Expected[$Package.id].resolved -or
            (($Category -eq 'topLevelPackages') -ne ($Expected[$Package.id].type -eq 'Direct'))) {
            throw 'Package inventory contains a missing, duplicate or mismatched package identity.'
        }
        $Seen.Add($Package.id, $Package.resolvedVersion)
    }
}
if ($Seen.Count -ne $Expected.Count) { throw 'Package inventory omits locked packages.' }

# Validate everything before replacing the prior inventory; keep it on any failure.
$Directory = Split-Path $OutputPath -Parent
[void][IO.Directory]::CreateDirectory($Directory)
$Temporary = Join-Path $Directory ('.package-inventory-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::WriteAllText($Temporary, $Text + "`n", [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($Temporary, $OutputPath, $true)
}
finally {
    if ([IO.File]::Exists($Temporary)) { [IO.File]::Delete($Temporary) }
}
Write-Host "Verified package inventory: $($Seen.Count) locked packages."
