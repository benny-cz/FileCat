param(
    [Parameter(Mandatory)][string] $CheckoutPath,
    [Parameter(Mandatory)][string] $Commit,
    [Parameter(Mandatory)][string] $ReportPath
)

# Build one exact pushed Git commit inside the isolated Windows VMware guest.
# Refuse tracked changes; retain every untracked fixture and publish directory.
$ErrorActionPreference = 'Stop'
$validation = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FileCatValidation'))
$prefix = $validation + [IO.Path]::DirectorySeparatorChar
$repo = [IO.Path]::GetFullPath($CheckoutPath)
$report = [IO.Path]::GetFullPath($ReportPath)
if (-not $repo.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not (Test-Path -LiteralPath (Join-Path $repo '.git')) -or
    $Commit -notmatch '^[0-9a-fA-F]{40}$' -or
    -not $report.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetDirectoryName($report) -ine $validation -or
    (Test-Path -LiteralPath $report)) {
    throw 'Require isolated Git worktree, exact commit and fresh validation report'
}
$git = 'C:\Program Files\Git\cmd\git.exe'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$result = [ordered]@{ Passed = $false; Checkout = $repo; RequestedCommit = $Commit; Error = $null }
try {
    $changes = @(& $git -c "safe.directory=$repo" -C $repo status --porcelain --untracked-files=no)
    if ($LASTEXITCODE -ne 0 -or $changes.Count) { throw 'Preserve tracked VM changes before updating' }
    & $git -c "safe.directory=$repo" -C $repo fetch origin main --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Git fetch failed' }
    & $git -c "safe.directory=$repo" -C $repo cat-file -e "$Commit^{commit}"
    if ($LASTEXITCODE -ne 0) { throw 'Requested commit is not available in the VM repository' }
    & $git -c "safe.directory=$repo" -C $repo checkout --detach $Commit --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Git checkout refused; preserve VM files' }
    $result.Head = (& $git -c "safe.directory=$repo" -C $repo rev-parse HEAD).Trim()
    if ($result.Head -ine $Commit) { throw 'VM checkout differs from requested Git commit' }
    $project = Join-Path $repo 'src\FileCat.App\FileCat.App.csproj'
    $result.RestoreLog = $report + '.restore.log'
    & $dotnet restore $project --force -p:RestoreLockedMode=true --nologo -v:minimal *> $result.RestoreLog
    if ($LASTEXITCODE -ne 0) { throw 'Locked Git app restore failed' }
    $result.BuildLog = $report + '.build.log'
    & $dotnet build $project -c Release -m:16 --no-restore --nologo -v:minimal *> $result.BuildLog
    if ($LASTEXITCODE -ne 0) { throw 'Exact Git app build failed' }
    $app = Join-Path $repo 'src\FileCat.App\bin\Release\net10.0\FileCat.exe'
    if (-not (Test-Path -LiteralPath $app -PathType Leaf)) { throw 'Git-built FileCat.exe is missing' }
    $result.AppPath = $app
    $result.AppDllSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'FileCat.dll') -Algorithm SHA256).Hash
    $result.Passed = $true
} catch {
    $result.Error = $_.Exception.Message
}
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $report -Encoding UTF8
if (-not $result.Passed) { throw $result.Error }
