<# Section 13.3's public-download checks for FileCat 1.0.0, run right after the owner publishes. As an anonymous reader
   (no GitHub token for the reads and downloads) it checks:
   - the release: published, not a prerelease, the latest release, titled "FileCat 1.0.0", tag v1.0.0 peeling to the
     candidate's source, and its text equal to the release notes (LF line ends, no final line break) by SHA-256;
   - the assets: exactly the 24 names of the candidate, each downloaded anonymously, matching GitHub's digest and the three
     manifests, whose SHA-256 must be the ones the GO named;
   - signatures: both Setups Authenticode-valid; every PE file in the x64 and ARM64 ZIPs valid too;
   - GitHub's release attestation (gh release verify, and verify-asset for each file);
   - every https link in the release text and in the README, DOWNLOAD, SECURITY and PRIVACY files at v1.0.0 answers.
   The clean install from a public download (Run-C1.ps1 with -Assets this folder's downloads) and the update notice in a
   preview build are separate runs. Writes public-check.txt and public-release.json into -WorkDirectory (new or empty).

   pwsh -File Check-PublicRelease.ps1 -SourceCommit <40 hex> -WindowsManifestSHA256 <64 hex> -LinuxManifestSHA256 <64 hex> `
        -MacosManifestSHA256 <64 hex> -NotesSHA256 <64 hex> -WorkDirectory <folder>
#>
param(
    [string]$Repository = 'benny-cz/FileCat',
    [ValidatePattern('^1\.0\.0(-[0-9A-Za-z.-]+)?$')][string]$Version = '1.0.0',
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceCommit,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$WindowsManifestSHA256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$LinuxManifestSHA256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$MacosManifestSHA256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$NotesSHA256,
    [Parameter(Mandatory)][string]$WorkDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Tag = "v$Version"; $Title = "FileCat $Version"
(Get-Process -Id $PID).PriorityClass = 'BelowNormal'
$Work = [IO.Path]::GetFullPath($WorkDirectory)
if ((Test-Path -LiteralPath $Work) -and @(Get-ChildItem -LiteralPath $Work -Force).Count -ne 0) { throw "The work folder must be new or empty: $Work" }
$Assets = Join-Path $Work 'assets'
New-Item -ItemType Directory -Force -Path $Assets | Out-Null
$lines = [Collections.Generic.List[string]]::new()
$script:fail = $false
function Note([string]$t) { $lines.Add($t); Write-Host $t }
function Check([bool]$ok, [string]$t) { if (-not $ok) { $script:fail = $true }; Note ("{0} {1}" -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $t) }
function Sha([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() }
function TextSha([string]$t) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($t))).ToLowerInvariant() }
# Anonymous JSON from the REST API (60 requests an hour for an address; this script makes about ten).
function Api([string]$Path, [switch]$AllowNotFound) {
    try { $r = Invoke-WebRequest -Uri "https://api.github.com/$Path" -Headers @{ Accept = 'application/vnd.github+json'; 'User-Agent' = 'FileCat-release-check' } -UseBasicParsing }
    catch { if ($AllowNotFound -and $_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return $null }; throw }
    [Text.Encoding]::UTF8.GetString($r.RawContentStream.ToArray()) | ConvertFrom-Json
}

# --- the release and its tag --------------------------------------------------------------------------------------
$rel = Api "repos/$Repository/releases/tags/$Tag"
Check (-not $rel.draft -and -not $rel.prerelease) "published, not a prerelease (draft=$($rel.draft), prerelease=$($rel.prerelease))"
Check ($rel.name -ceq $Title) "title '$($rel.name)'"
# Previews were published with make_latest false, so before 1.0.0 the repository has no latest release (404).
$latest = Api "repos/$Repository/releases/latest" -AllowNotFound
Check ($null -ne $latest -and $latest.id -eq $rel.id) "the latest release is $Tag (latest: $(if ($latest) { $latest.tag_name } else { 'none' }))"
$ref = Api "repos/$Repository/git/ref/tags/$Tag"
$kind = $ref.object.type; $sha = $ref.object.sha
while ($kind -ceq 'tag') { $t = Api "repos/$Repository/git/tags/$sha"; $kind = $t.object.type; $sha = $t.object.sha }
Check ($kind -ceq 'commit' -and $sha -ceq $SourceCommit) "$Tag peels to $sha"
$bodySha = TextSha ((([string]$rel.body).Replace("`r`n", "`n")).TrimEnd("`n"))
Check ($bodySha -ceq $NotesSHA256) "the release text's SHA-256 is the notes' ($bodySha)"
Note "release ID $($rel.id), published $($rel.published_at), immutable=$(if ($rel.PSObject.Properties['immutable']) { $rel.immutable } else { 'not reported' })"

# --- the assets, downloaded anonymously -------------------------------------------------------------------------
$expectedManifests = @{ "FileCat-$Version-windows-assets.json" = $WindowsManifestSHA256; "FileCat-$Version-linux-assets.json" = $LinuxManifestSHA256; "FileCat-$Version-macos-assets.json" = $MacosManifestSHA256 }
$records = @()
foreach ($a in @($rel.assets)) {
    if ($a.name -cnotmatch '^[0-9A-Za-z][0-9A-Za-z._-]*$') { Check $false "unexpected asset name $($a.name)"; continue }
    $path = Join-Path $Assets $a.name
    & curl.exe -sSfL --retry 3 -o $path "https://github.com/$Repository/releases/download/$Tag/$($a.name)"
    if ($LASTEXITCODE -ne 0) { Check $false "download $($a.name) (curl $LASTEXITCODE)"; continue }
    $h = Sha $path
    $okDigest = ("sha256:$h" -ceq $a.digest) -and ((Get-Item -LiteralPath $path).Length -eq $a.size)
    if (-not $okDigest) { Check $false "$($a.name) differs from GitHub's digest" }
    $records += [pscustomobject]@{ Name = $a.name; Id = $a.id; Bytes = $a.size; SHA256 = $h; Downloads = $a.download_count }
}
$names = @($records | ForEach-Object Name)
$listed = @{}
foreach ($m in $expectedManifests.Keys) {
    $p = Join-Path $Assets $m
    if (-not (Test-Path -LiteralPath $p)) { Check $false "missing $m"; continue }
    Check ((Sha $p) -ceq $expectedManifests[$m]) "$m has the SHA-256 the GO named"
    $j = Get-Content -LiteralPath $p -Raw | ConvertFrom-Json
    $candidateRef = if ($j.PSObject.Properties['CandidateRef']) { $j.CandidateRef } else { 'no candidate reference' }
    Check ($j.Version -ceq $Version -and $j.SourceCommit -ceq $SourceCommit) "$m names $Version and the source ($candidateRef)"
    foreach ($f in @($j.Files)) {
        $listed[$f.Name] = $true
        $fp = Join-Path $Assets $f.Name
        if (-not (Test-Path -LiteralPath $fp)) { Check $false "$($f.Name), listed in $m, was not downloaded"; continue }
        if ((Sha $fp) -cne $f.SHA256 -or (Get-Item -LiteralPath $fp).Length -ne [long]$f.Bytes) { Check $false "$($f.Name) differs from its pin in $m" }
    }
}
$extra = @($names | Where-Object { -not $listed.ContainsKey($_) -and -not $expectedManifests.ContainsKey($_) })
Check ($names.Count -eq 24 -and $extra.Count -eq 0 -and $listed.Count -eq 21) "24 assets, each matching GitHub's digest and its manifest pin; none unlisted (assets $($names.Count), listed $($listed.Count), unlisted $($extra.Count))"

# --- Windows signatures --------------------------------------------------------------------------------------------
# The Setups and the Windows ZIPs as the release names them (a preview's Setups keep the plain 1.0.0 name).
$setups = @($names | Where-Object { $_ -match '-win-(x64|arm64)-setup\.exe$' })
$zips = @($names | Where-Object { $_ -match '-win-(x64|arm64)-(portable|fdd)\.zip$' })
Check ($setups.Count -eq 2 -and $zips.Count -eq 4) "Windows files found: $($setups.Count) Setups, $($zips.Count) ZIPs"
foreach ($setup in $setups) {
    $s = Get-AuthenticodeSignature -LiteralPath (Join-Path $Assets $setup)
    Check ($s.Status -eq 'Valid' -and $null -ne $s.TimeStamperCertificate) "$setup Authenticode $($s.Status), timestamped"
}
foreach ($zip in $zips) {
    $dir = Join-Path $Work ("unzipped-" + [IO.Path]::GetFileNameWithoutExtension($zip))
    Expand-Archive -LiteralPath (Join-Path $Assets $zip) -DestinationPath $dir -Force
    $pe = @(Get-ChildItem -LiteralPath $dir -Recurse -File | Where-Object { $_.Extension -in '.exe', '.dll' })
    $bad = @($pe | Where-Object { (Get-AuthenticodeSignature -LiteralPath $_.FullName).Status -ne 'Valid' })
    Check ($pe.Count -gt 0 -and $bad.Count -eq 0) "$zip`: $($pe.Count) PE files, $($bad.Count) without a valid signature$(if ($bad) { ': ' + (($bad | Select-Object -First 5 | ForEach-Object Name) -join ', ') })"
    Remove-Item -LiteralPath $dir -Recurse -Force
}

# --- GitHub's release attestation -----------------------------------------------------------------------------------
& gh release verify $Tag --repo $Repository *> (Join-Path $Work 'gh-release-verify.txt')
Check ($LASTEXITCODE -eq 0) "gh release verify $Tag"
$unverified = @()
foreach ($r in $records) {
    & gh release verify-asset $Tag (Join-Path $Assets $r.Name) --repo $Repository *> $null
    if ($LASTEXITCODE -ne 0) { $unverified += $r.Name }
}
Check ($unverified.Count -eq 0) "gh release verify-asset: $($records.Count - $unverified.Count) of $($records.Count) attested$(if ($unverified) { '; not: ' + ($unverified -join ', ') })"

# --- links ------------------------------------------------------------------------------------------------------------
$texts = @([string]$rel.body)
foreach ($doc in 'README.md', 'docs/DOWNLOAD.md', 'SECURITY.md', 'docs/PRIVACY.md') {
    $texts += (Invoke-WebRequest -Uri "https://raw.githubusercontent.com/$Repository/$Tag/$doc" -UseBasicParsing).Content
}
$urls = @($texts | ForEach-Object { [regex]::Matches($_, 'https://[^\s)\]"''<>`]+') | ForEach-Object { $_.Value.TrimEnd('.', ',', ';', ':') } } | Sort-Object -Unique)
$broken = @()
foreach ($u in $urls) {
    $code = & curl.exe -s -o NUL -w '%{http_code}' -L --max-time 30 -A 'FileCat-release-check' $u
    if ($code -notmatch '^(200|204)$') { $broken += "$code $u" }
}
Check ($broken.Count -eq 0) "links: $($urls.Count) checked, $($broken.Count) not answering$(if ($broken) { ': ' + ($broken -join '; ') })"

[pscustomobject]@{ ReleaseId = $rel.id; PublishedAt = $rel.published_at; Url = $rel.html_url; Assets = $records } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Work 'public-release.json') -Encoding utf8NoBOM
Note "RESULT fail=$script:fail"
[IO.File]::WriteAllLines((Join-Path $Work 'public-check.txt'), $lines)
