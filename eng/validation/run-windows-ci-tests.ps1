param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$platform = 'tests/FileCat.Platform.Windows.Tests/FileCat.Platform.Windows.Tests.csproj'
$pageViews = 'FullyQualifiedName~FileCat.Platform.Windows.Tests.PageViewTests'
$projects = @(
    $platform,
    'tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj',
    'tests/FileCat.Remote.Tests/FileCat.Remote.Tests.csproj',
    'tests/FileCat.App.Tests/FileCat.App.Tests.csproj',
    'tests/FileCat.Rendering.Tests/FileCat.Rendering.Tests.csproj'
)
$results = [Collections.Generic.List[object]]::new()

Push-Location $repo
try {
    # WebView2's browser process does not start under the hosted runner's administrator token once that token is
    # lowered to medium integrity (the page reports RPC_E_DISCONNECTED), so the page view tests run first, at the
    # runner's own token, and the medium-integrity run of their project below leaves them out.
    $started = [DateTimeOffset]::UtcNow
    Write-Host "::group::Windows tests: page view at the runner's token"
    & dotnet test $platform -c Release --no-build --filter $pageViews --logger 'trx;LogFileName=page-view-results.trx' --blame-hang-timeout 4m --blame-hang-dump-type mini
    $code = $LASTEXITCODE
    Write-Host "::endgroup::"
    $results.Add([ordered]@{
        Project = "$platform (page view)"
        StartedUtc = $started.ToString('O')
        EndedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        ExitCode = $code
    })
    Write-Host "Windows tests: $platform (page view) exited $code"
}
finally {
    Pop-Location
}
. (Join-Path $PSScriptRoot 'Enter-MediumIntegrityTestProcess.ps1')

Push-Location $repo
try {
    foreach ($project in $projects) {
        $started = [DateTimeOffset]::UtcNow
        Write-Host "::group::Windows tests: $project"
        # Core tests run independent job and filesystem fixtures in parallel.
        # Bound them to this runner's actual processors; a small hosted runner
        # must not schedule the workstation's sixteen concurrent collections.
        if ($project -like '*FileCat.Core.Tests*') {
            $coreThreads = [Math]::Min(16, [Environment]::ProcessorCount)
            Write-Host "Core test collections: $coreThreads parallel threads on $([Environment]::ProcessorCount) processors"
            & dotnet test $project -c Release --no-build --logger 'trx;LogFileName=results.trx' --blame-hang-timeout 4m --blame-hang-dump-type mini -- "xUnit.MaxParallelThreads=$coreThreads"
        }
        elseif ($project -eq $platform) {
            & dotnet test $project -c Release --no-build --filter "FullyQualifiedName!~FileCat.Platform.Windows.Tests.PageViewTests" --logger 'trx;LogFileName=results.trx' --blame-hang-timeout 4m --blame-hang-dump-type mini
        }
        else {
            & dotnet test $project -c Release --no-build --logger 'trx;LogFileName=results.trx' --blame-hang-timeout 4m --blame-hang-dump-type mini
        }
        $code = $LASTEXITCODE
        Write-Host "::endgroup::"
        $results.Add([ordered]@{
            Project = $project
            StartedUtc = $started.ToString('O')
            EndedUtc = [DateTimeOffset]::UtcNow.ToString('O')
            ExitCode = $code
        })
        Write-Host "Windows tests: $project exited $code"
    }
}
finally {
    Pop-Location
}

$receipt = Join-Path $EvidenceDirectory 'results.json'
$results | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $receipt -Encoding utf8
$failed = @($results | Where-Object { $_.ExitCode -ne 0 })
if ($failed.Count) {
    throw "Windows test projects failed: $(($failed | ForEach-Object { $_.Project }) -join ', '). All project commands and the result receipt were retained."
}
