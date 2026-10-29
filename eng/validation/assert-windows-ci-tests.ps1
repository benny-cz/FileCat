param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
$receipt = Join-Path $EvidenceDirectory 'results.json'
if (-not (Test-Path -LiteralPath $receipt -PathType Leaf)) {
    throw "Windows test-project receipt is missing: $receipt"
}
$rows = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
$expected = @(
    'tests/FileCat.Platform.Windows.Tests/FileCat.Platform.Windows.Tests.csproj (page view)',
    'tests/FileCat.Platform.Windows.Tests/FileCat.Platform.Windows.Tests.csproj',
    'tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj',
    'tests/FileCat.Remote.Tests/FileCat.Remote.Tests.csproj',
    'tests/FileCat.App.Tests/FileCat.App.Tests.csproj',
    'tests/FileCat.Rendering.Tests/FileCat.Rendering.Tests.csproj'
)
if ($rows.Count -ne $expected.Count) {
    throw "Windows test-project receipt has $($rows.Count) rows, expected $($expected.Count)."
}
for ($i = 0; $i -lt $expected.Count; $i++) {
    if ($rows[$i].Project -ne $expected[$i] -or $rows[$i].ExitCode -ne 0) {
        throw "Windows test project $($expected[$i]) did not pass: recorded project=$($rows[$i].Project), exit=$($rows[$i].ExitCode)."
    }
}
Write-Host 'The page view run and all five Windows test projects passed and have a complete ordered receipt.'
