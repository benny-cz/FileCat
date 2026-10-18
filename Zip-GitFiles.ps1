param(
    [Parameter(Position = 0)]
    [string]$InputDirectory = (Get-Location).Path,

    [Parameter(Position = 1)]
    [string]$OutputPath = "InterCat.zip"
)

$ErrorActionPreference = "Stop"

# Resolve input directory.
$InputDirectory = (Resolve-Path -LiteralPath $InputDirectory).Path

# Find Git repository root.
$RepoRoot = (& git -C $InputDirectory rev-parse --show-toplevel 2>$null)

if ($LASTEXITCODE -ne 0 -or -not $RepoRoot) {
    throw "'$InputDirectory' is not inside a Git repository."
}

$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot.Trim()).Path

# Normalize output path.
if (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $InputDirectory $OutputPath
}

$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

# Get Git-tracked files. -z safely handles spaces and unusual filenames.
$rawFiles = (& git -C $RepoRoot ls-files -z)

if ($LASTEXITCODE -ne 0) {
    throw "Failed to obtain the list of Git-tracked files."
}

$trackedFiles = @(
    $rawFiles -split "`0" |
        Where-Object { $_ }
)

# Determine the path of InputDirectory relative to the Git repository.
$repoUri = [Uri]($RepoRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar)
$inputUri = [Uri]($InputDirectory.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar)

$relativeInput = [Uri]::UnescapeDataString(
    $repoUri.MakeRelativeUri($inputUri).ToString()
).TrimEnd('/')

# Make sure the input directory is inside the repository.
if ($relativeInput.StartsWith("../")) {
    throw "Input directory must be inside the Git repository."
}

# Filter tracked files to those underneath InputDirectory.
$filesToArchive = foreach ($gitPath in $trackedFiles) {
    $normalizedGitPath = $gitPath.Replace('\', '/')

    if ([string]::IsNullOrEmpty($relativeInput)) {
        $archivePath = $normalizedGitPath
    }
    elseif (
        $normalizedGitPath -eq $relativeInput -or
        $normalizedGitPath.StartsWith("$relativeInput/")
    ) {
        $archivePath = $normalizedGitPath.Substring($relativeInput.Length).TrimStart('/')
    }
    else {
        continue
    }

    $fullPath = Join-Path $RepoRoot ($normalizedGitPath.Replace('/', [IO.Path]::DirectorySeparatorChar))

    if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
        [PSCustomObject]@{
            FullPath    = $fullPath
            ArchivePath = $archivePath
        }
    }
}

if (-not $filesToArchive) {
    throw "No Git-tracked files found under '$InputDirectory'."
}

# Create output directory if necessary.
$outputDirectory = Split-Path -Parent $OutputPath

if ($outputDirectory -and -not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

# Replace an existing archive.
if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Force
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Use the strongest compression level available on this .NET version.
$compressionLevel =
    if ([Enum]::GetNames([System.IO.Compression.CompressionLevel]) -contains "SmallestSize") {
        [System.IO.Compression.CompressionLevel]::SmallestSize
    }
    else {
        [System.IO.Compression.CompressionLevel]::Optimal
    }

$fileStream = [System.IO.File]::Open(
    $OutputPath,
    [System.IO.FileMode]::Create
)

try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $fileStream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false
    )

    try {
        foreach ($file in $filesToArchive) {
            $entryName = $file.ArchivePath.Replace('\', '/')

            $entry = $archive.CreateEntry(
                $entryName,
                $compressionLevel
            )

            $entryStream = $entry.Open()

            try {
                $sourceStream = [System.IO.File]::OpenRead($file.FullPath)

                try {
                    $sourceStream.CopyTo($entryStream)
                }
                finally {
                    $sourceStream.Dispose()
                }
            }
            finally {
                $entryStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $fileStream.Dispose()
}

Write-Host "Created: $OutputPath"
Write-Host "Archived $($filesToArchive.Count) Git-tracked files."