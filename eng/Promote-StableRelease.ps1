<# Manifest-only promotion of a qualified FileCat 1.0.0 release candidate to the stable release's private draft.

   After the owner's GO, this copies the exact bytes of the candidate's private draft (v1.0.0-rc.N) into a new private
   draft "FileCat 1.0.0" for the tag v1.0.0 at the candidate's source commit. It builds, signs and repackages nothing,
   and it never publishes: the owner reviews the draft and presses Publish release.

   Every check runs before the first write, and a failed check writes nothing:
   - the candidate tag peels to the given source commit, and so does v1.0.0, which a real promotion needs (the owner
     creates it; a dry run may run before it exists);
   - no release for v1.0.0 exists yet, as a draft or published;
   - the candidate release is a draft prerelease of the candidate tag;
   - each of its assets matches GitHub's digest; the three producer manifests have the SHA-256 values the GO names,
     name version 1.0.0, the candidate tag and the source commit, and pin every other asset by size and SHA-256; the
     assets are exactly the manifests and the files they list;
   - the release notes, with line feeds as line ends, have the SHA-256 the GO names and no open [[...]] placeholder.
   It then creates the draft, uploads each file once, and checks the draft: its tag, commit, title, notes and kind, and
   every asset by name, size and GitHub's digest; it downloads each uploaded asset again by its ID and compares it with
   the candidate's bytes. -DryRun makes the same draft under a dry-run title and deletes it at the end, or when it
   fails (-KeepDryRunDraft keeps it). A real promotion that fails after creating the draft leaves the draft for review.
   Writes promotion-report.json into -WorkDirectory, which must be new or empty.

   pwsh -File eng/Promote-StableRelease.ps1 -CandidateTag v1.0.0-rc.5 -CandidateReleaseId 458724408 `
        -SourceCommit <40 hex> -WindowsManifestSHA256 <64 hex> -LinuxManifestSHA256 <64 hex> `
        -MacosManifestSHA256 <64 hex> -NotesPath docs/release/1.0.0/RELEASE_NOTES_1.0.0.md -NotesSHA256 <64 hex> `
        -WorkDirectory <new folder> [-DryRun [-KeepDryRunDraft]]
#>
param(
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository = 'benny-cz/FileCat',
    [Parameter(Mandatory)][ValidatePattern('^v1\.0\.0-rc\.[1-9][0-9]*$')][string]$CandidateTag,
    [Parameter(Mandatory)][ValidateRange(1, [long]::MaxValue)][long]$CandidateReleaseId,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceCommit,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$WindowsManifestSHA256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$LinuxManifestSHA256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$MacosManifestSHA256,
    [Parameter(Mandatory)][string]$NotesPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$NotesSHA256,
    [Parameter(Mandatory)][string]$WorkDirectory,
    [switch]$DryRun,
    [switch]$KeepDryRunDraft
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$StableTag = 'v1.0.0'
$Title = if ($DryRun) { 'FileCat 1.0.0 (promotion dry run: never publish)' } else { 'FileCat 1.0.0' }
$ExpectedManifests = [ordered]@{
    'FileCat-1.0.0-windows-assets.json' = $WindowsManifestSHA256
    'FileCat-1.0.0-linux-assets.json' = $LinuxManifestSHA256
    'FileCat-1.0.0-macos-assets.json' = $MacosManifestSHA256
}
$AssetName = '^[0-9A-Za-z][0-9A-Za-z._-]*$'

function Write-Step([string]$Text) { Write-Host "$(Get-Date -Format 'HH:mm:ss') $Text" }

function Get-Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

# One GitHub API call through gh; JSON comes back as objects. A 404 returns $null only where the caller allows it.
function Invoke-GitHub([string]$Endpoint, [string]$Method = 'GET', [string]$InputPath, [string]$ContentType, [switch]$AllowNotFound) {
    $arguments = @('api', '--method', $Method, '-H', 'Accept: application/vnd.github+json')
    if ($ContentType) { $arguments += @('-H', "Content-Type: $ContentType") }
    if ($InputPath) { $arguments += @('--input', $InputPath) }
    $arguments += $Endpoint
    $errorPath = [IO.Path]::GetTempFileName()
    try {
        $out = & gh @arguments 2>$errorPath
        if ($LASTEXITCODE -ne 0) {
            $message = (Get-Content -LiteralPath $errorPath -Raw)
            if ($AllowNotFound -and $message -match 'HTTP 404') { return $null }
            throw "gh api $Method $Endpoint failed: $message"
        }
        $text = ($out -join "`n").Trim()
        if (-not $text) { return $null }
        return $text | ConvertFrom-Json
    }
    finally { Remove-Item -LiteralPath $errorPath -ErrorAction SilentlyContinue }
}

# Downloads one release asset's bytes by ID; binary-safe, so not through the PowerShell pipeline.
function Save-Asset([long]$Id, [string]$Path) {
    $part = "$Path.part"
    $errorPath = "$Path.err"
    $p = Start-Process -FilePath gh -NoNewWindow -Wait -PassThru -RedirectStandardOutput $part -RedirectStandardError $errorPath `
        -ArgumentList @('api', '-H', '"Accept: application/octet-stream"', "repos/$Repository/releases/assets/$Id")
    if ($p.ExitCode -ne 0) { throw "Downloading asset $Id failed: $(Get-Content -LiteralPath $errorPath -Raw)" }
    Remove-Item -LiteralPath $errorPath
    Move-Item -LiteralPath $part -Destination $Path
}

function Get-TagCommit([string]$Tag) {
    $ref = Invoke-GitHub "repos/$Repository/git/ref/tags/$Tag" -AllowNotFound
    if ($null -eq $ref) { return $null }
    if ($ref.ref -cne "refs/tags/$Tag") { throw "GitHub answered $($ref.ref) for refs/tags/$Tag" }
    $kind = $ref.object.type; $sha = $ref.object.sha; $annotated = $false
    while ($kind -ceq 'tag') {
        $annotated = $true
        $object = Invoke-GitHub "repos/$Repository/git/tags/$sha"
        $kind = $object.object.type; $sha = $object.object.sha
    }
    if ($kind -cne 'commit') { throw "$Tag does not peel to a commit" }
    [pscustomobject]@{ Commit = $sha; Annotated = $annotated }
}

function Get-ReleaseAssets([long]$ReleaseId) {
    @(Invoke-GitHub "repos/$Repository/releases/$ReleaseId/assets?per_page=100")
}

# --- the work folder: new or empty, and reached through no link ---------------------------------------------------
$Work = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $Work) {
    if (@(Get-ChildItem -LiteralPath $Work -Force).Count -ne 0) { throw "The work folder must be new or empty: $Work" }
}
else { New-Item -ItemType Directory -Path $Work | Out-Null }
$current = Get-Item -LiteralPath $Work -Force
while ($null -ne $current) {
    if ($current.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "The work folder's path goes through a link: $($current.FullName)" }
    $current = $current.Parent
}
$CandidateDir = Join-Path $Work 'candidate'
$StableDir = Join-Path $Work 'stable-draft'
New-Item -ItemType Directory -Path $CandidateDir, $StableDir | Out-Null
$report = [ordered]@{
    Mode = $(if ($DryRun) { 'dry-run' } else { 'promotion' }); Repository = $Repository; Started = (Get-Date).ToUniversalTime().ToString('o')
    CandidateTag = $CandidateTag; CandidateReleaseId = $CandidateReleaseId; SourceCommit = $SourceCommit; StableTag = $StableTag
    StableTagExisted = $false; Title = $Title; NotesSHA256 = $NotesSHA256; Manifests = $ExpectedManifests
    StableReleaseId = $null; StableReleaseUrl = $null; Assets = @(); Result = 'refused'; Note = ''
}
$ReportPath = Join-Path $Work 'promotion-report.json'
function Save-Report { [IO.File]::WriteAllText($ReportPath, ($report | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false)) }

try {
    # --- the release notes, as LF text ---------------------------------------------------------------------------
    $notesFull = [IO.Path]::GetFullPath($NotesPath)
    $notesText = [IO.File]::ReadAllText($notesFull, [Text.UTF8Encoding]::new($false, $true)).Replace("`r`n", "`n")
    $notesBytes = [Text.UTF8Encoding]::new($false).GetBytes($notesText)
    $notesSha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($notesBytes)).ToLowerInvariant()
    if ($notesSha -cne $NotesSHA256) { throw "The release notes' SHA-256 is $notesSha, not the $NotesSHA256 the GO names" }
    if ($notesText.Contains('[[')) { throw 'The release notes still hold an open [[...]] placeholder' }
    if ($notesText.Length -gt 125000) { throw 'The release notes are longer than GitHub keeps (125,000 characters)' }
    Write-Step "notes: $notesFull, $($notesBytes.Length) bytes, SHA-256 as named"

    # --- tags and existing releases ----------------------------------------------------------------------------
    $candidateRef = Get-TagCommit $CandidateTag
    if ($null -eq $candidateRef -or $candidateRef.Commit -cne $SourceCommit) { throw "$CandidateTag does not peel to $SourceCommit" }
    $stableRef = Get-TagCommit $StableTag
    if ($null -ne $stableRef) {
        if ($stableRef.Commit -cne $SourceCommit) { throw "$StableTag peels to $($stableRef.Commit), not to the candidate's source $SourceCommit" }
        if (-not $stableRef.Annotated) { throw "$StableTag is a lightweight tag; the owner's tag is annotated" }
        $report.StableTagExisted = $true
    }
    elseif (-not $DryRun) { throw "$StableTag does not exist yet: the owner creates it at $SourceCommit before the promotion" }
    Write-Step "tags: $CandidateTag peels to the source; $StableTag $(if ($stableRef) { 'peels to the source' } else { 'does not exist yet (dry run)' })"
    $existing = @(& gh api --paginate "repos/$Repository/releases?per_page=100" --jq '.[] | select(.tag_name == "v1.0.0") | .id')
    if ($LASTEXITCODE -ne 0) { throw 'Listing the releases failed' }
    if ($existing.Count -ne 0) { throw "A release for $StableTag already exists (ID $($existing -join ', ')); a promotion makes the only one" }

    # --- the candidate's draft and its bytes -----------------------------------------------------------------
    $candidate = Invoke-GitHub "repos/$Repository/releases/$CandidateReleaseId"
    if (-not $candidate.draft -or -not $candidate.prerelease -or $candidate.tag_name -cne $CandidateTag) {
        throw "Release $CandidateReleaseId is not the draft prerelease of $CandidateTag"
    }
    $assets = @{}
    foreach ($a in (Get-ReleaseAssets $CandidateReleaseId)) {
        if ($a.name -cnotmatch $AssetName) { throw "Unexpected asset name in the candidate: $($a.name)" }
        if ($assets.ContainsKey($a.name)) { throw "Two candidate assets are named $($a.name)" }
        if ($a.state -cne 'uploaded' -or $a.digest -cnotmatch '^sha256:[0-9a-f]{64}$') { throw "Candidate asset $($a.name) is not complete or has no digest" }
        $assets[$a.name] = $a
    }
    # The manifests first, so that a wrong GO value stops the promotion before the large downloads.
    $names = @{}
    function Get-CandidateFile([string]$Name) {
        $a = $assets[$Name]
        $path = Join-Path $CandidateDir $Name
        Save-Asset $a.id $path
        $sha = Get-Sha256 $path
        if ((Get-Item -LiteralPath $path).Length -ne $a.size -or "sha256:$sha" -cne $a.digest) { throw "Candidate asset $Name does not match GitHub's digest" }
        $names[$Name] = [pscustomobject]@{ Bytes = [long]$a.size; SHA256 = $sha; CandidateAssetId = [long]$a.id }
    }
    $pins = @{}
    foreach ($m in $ExpectedManifests.Keys) {
        if (-not $assets.ContainsKey($m)) { throw "The candidate lacks $m" }
        Get-CandidateFile $m
        if ($names[$m].SHA256 -cne $ExpectedManifests[$m]) { throw "$m has SHA-256 $($names[$m].SHA256), not the $($ExpectedManifests[$m]) the GO names" }
        $manifest = Get-Content -LiteralPath (Join-Path $CandidateDir $m) -Raw | ConvertFrom-Json
        if ($manifest.Version -cne '1.0.0' -or $manifest.CandidateRef -cne "refs/tags/$CandidateTag" -or $manifest.SourceCommit -cne $SourceCommit) {
            throw "$m does not name version 1.0.0, refs/tags/$CandidateTag and $SourceCommit"
        }
        foreach ($f in @($manifest.Files)) {
            if ($pins.ContainsKey($f.Name) -or $ExpectedManifests.Contains($f.Name)) { throw "$($f.Name) is listed twice" }
            if (-not $assets.ContainsKey($f.Name)) { throw "$m lists $($f.Name), which the candidate lacks" }
            $pins[$f.Name] = $f
        }
    }
    $extra = @($assets.Keys | Where-Object { -not $pins.ContainsKey($_) -and -not $ExpectedManifests.Contains($_) })
    if ($extra.Count -ne 0) { throw "The candidate has assets no manifest lists: $($extra -join ', ')" }
    Write-Step "manifests: the three SHA-256 values the GO names, version 1.0.0, $CandidateTag and the source; they list the $($pins.Count) other assets"
    foreach ($name in ($pins.Keys | Sort-Object)) {
        Get-CandidateFile $name
        if ($names[$name].Bytes -ne [long]$pins[$name].Bytes -or $names[$name].SHA256 -cne $pins[$name].SHA256) { throw "$name differs from its manifest pin" }
    }
    Write-Step "candidate: release $CandidateReleaseId, all $($names.Count) assets downloaded, matching GitHub's digests and the manifests' pins"
}
catch { $report.Note = "$_"; Save-Report; throw }

# --- the writes: one private draft, its uploads and their checks -----------------------------------------------
$draftId = $null
$keepDraft = -not $DryRun -or $KeepDryRunDraft
try {
    $payload = [ordered]@{ tag_name = $StableTag; target_commitish = $SourceCommit; name = $Title; body = $notesText; draft = $true; prerelease = $false }
    $payloadPath = Join-Path $Work 'create-release.json'
    [IO.File]::WriteAllText($payloadPath, ($payload | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    $draft = Invoke-GitHub "repos/$Repository/releases" -Method POST -InputPath $payloadPath
    $draftId = [long]$draft.id
    $report.StableReleaseId = $draftId; $report.StableReleaseUrl = $draft.html_url
    Remove-Item -LiteralPath $payloadPath
    Write-Step "draft: release $draftId created ($Title)"
    foreach ($name in ($names.Keys | Sort-Object)) {
        $uploaded = Invoke-GitHub "https://uploads.github.com/repos/$Repository/releases/$draftId/assets?name=$name" -Method POST `
            -InputPath (Join-Path $CandidateDir $name) -ContentType 'application/octet-stream'
        if ($uploaded.name -cne $name) { throw "GitHub stored $name as $($uploaded.name)" }
        Write-Step "uploaded $name ($($names[$name].Bytes) bytes) as asset $($uploaded.id)"
    }
    # The draft as GitHub keeps it: kind, tag, commit, title, notes and assets.
    # Where v1.0.0 exists, it fixes the commit (checked above); before that, the draft's target commit does.
    $kept = Invoke-GitHub "repos/$Repository/releases/$draftId"
    if (-not $kept.draft -or $kept.prerelease -or $kept.tag_name -cne $StableTag -or $kept.name -cne $Title -or
        (-not $report.StableTagExisted -and $kept.target_commitish -cne $SourceCommit)) {
        throw 'The draft is not the private, stable-kind draft of v1.0.0 at the source commit with the expected title'
    }
    $keptBody = ([string]$kept.body).Replace("`r`n", "`n")
    $keptSha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($keptBody))).ToLowerInvariant()
    if ($keptSha -cne $NotesSHA256) { throw "The draft's notes have SHA-256 $keptSha, not the notes' $NotesSHA256" }
    $keptAssets = Get-ReleaseAssets $draftId
    if ($keptAssets.Count -ne $names.Count) { throw "The draft has $($keptAssets.Count) assets, not $($names.Count)" }
    $seen = @{}
    foreach ($a in $keptAssets) {
        if (-not $names.ContainsKey($a.name) -or $seen.ContainsKey($a.name)) { throw "Unexpected or doubled draft asset: $($a.name)" }
        $seen[$a.name] = $true
        $want = $names[$a.name]
        if ($a.state -cne 'uploaded' -or [long]$a.size -ne $want.Bytes -or $a.digest -cne "sha256:$($want.SHA256)") { throw "Draft asset $($a.name) does not match the candidate's bytes" }
        $path = Join-Path $StableDir $a.name
        Save-Asset $a.id $path
        if ((Get-Sha256 $path) -cne $want.SHA256) { throw "Draft asset $($a.name) downloaded again differs from the candidate's bytes" }
        $report.Assets += [pscustomobject]@{ Name = $a.name; Bytes = $want.Bytes; SHA256 = $want.SHA256; CandidateAssetId = $want.CandidateAssetId; StableAssetId = [long]$a.id }
    }
    Write-Step "verified: the draft's kind, tag, commit, title and notes, and all $($keptAssets.Count) assets downloaded again by ID"
    $report.Result = if ($DryRun) { 'dry run passed' } else { 'draft ready for the owner to publish' }
}
catch {
    $report.Result = 'failed'; $report.Note = "$_"
    throw
}
finally {
    if ($null -ne $draftId -and -not $keepDraft) {
        $null = Invoke-GitHub "repos/$Repository/releases/$draftId" -Method DELETE
        if ($null -ne (Invoke-GitHub "repos/$Repository/releases/$draftId" -AllowNotFound)) { $report.Note += " The dry-run draft $draftId is still there." }
        else { $report.Note += " The dry-run draft $draftId was deleted."; Write-Step "dry run: draft $draftId deleted" }
    }
    $report.Finished = (Get-Date).ToUniversalTime().ToString('o')
    Save-Report
}
if (-not $DryRun) { Write-Step "Nothing is published. The owner reviews $($report.StableReleaseUrl) and presses Publish release." }
[pscustomobject]$report
