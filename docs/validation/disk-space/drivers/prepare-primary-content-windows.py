"""Prepare the exact Windows NTFS primary-content test batch and source evidence."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile

root = Path.cwd()
temp = root / ".tmp"
revision = sys.argv[1]
baseline_revision = sys.argv[2] if len(sys.argv) == 3 else None
assert re.fullmatch(r"r[0-9]+", revision) and (baseline_revision is None or re.fullmatch(r"r[0-9]+", baseline_revision))
binary = root / "tests/FileCat.Platform.Windows.Tests/bin/Debug/net10.0"
stage = temp / f"batch-{revision}-windows"
bundle = temp / f"dsa-batch-windows-{revision}.tgz"
runner = temp / f"batch-windows-{revision}.ps1"
assert not stage.exists() and not bundle.exists() and not runner.exists()
log = (temp / f"primary-content-build-{revision}.txt").read_text(encoding="utf-8-sig")
assert all(marker in log for marker in ("Build succeeded.", "0 Warning(s)", "0 Error(s)"))


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


stage.mkdir()
inputs = {}
overlay = []
test_inputs = {"FileCat.Platform.Windows.Tests.dll", "FileCat.Platform.Windows.Tests.pdb"}
if baseline_revision:
    baseline = json.loads((temp / f"primary-content-proof-{baseline_revision}.json").read_text())["inputs"]
for path in sorted(binary.rglob("*")):
    if not path.is_file() or path.name == "FileCat.PrivilegedHost.exe":
        continue
    assert not path.is_symlink()
    relative = path.relative_to(binary).as_posix()
    inputs[relative] = sha(path)
    if baseline_revision:
        assert sha(temp / f"batch-{baseline_revision}-windows" / relative) == baseline[relative]
        changed = inputs[relative] != baseline[relative]
        assert not changed or relative in test_inputs, relative
        overlay.append({"path": relative, "old": baseline[relative], "new": inputs[relative], "patch": changed})
        if not changed:
            continue
    target = stage / ("patch" if baseline_revision else "") / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(path, target)
    assert inputs[relative] == sha(target)
assert all(name in inputs for name in ("FileCat.Core.dll", "FileCat.Platform.Windows.dll", "FileCat.Platform.Windows.Tests.dll"))
if baseline_revision:
    assert set(baseline) == set(inputs)
    assert {row["path"] for row in overlay if row["patch"]} == test_inputs
    (stage / "plan.json").write_text(json.dumps(overlay, indent=2), encoding="utf-8")
    (temp / f"cache-input-{revision}").mkdir()
with tarfile.open(bundle, "w:gz", compresslevel=4) as archive:
    for path in sorted(stage.rglob("*")):
        if path.is_file():
            archive.add(path, arcname=path.relative_to(stage).as_posix(), recursive=False)

launcher = r"""
$ErrorActionPreference = 'Stop'
$base = $PSScriptRoot
$state = Join-Path $base 'state.txt'
function Busy { @(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count -ne 0 }
function Run-Native([string]$phase, [string[]]$methods, [int]$expected) {
 if(Busy) { "BUSY phase=$phase" | Set-Content $state; exit 75 }
 "RUNNING phase=$phase" | Set-Content $state
 $arguments = @((Join-Path $payload 'FileCat.Platform.Windows.Tests.dll'))
 foreach($name in $methods) { $arguments += @('-method', $name) }
 $arguments += @('-noLogo','-noColor','-maxThreads','1','-xml',(Join-Path $base ($phase+'.xml')))
 $ErrorActionPreference = 'Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' @arguments *> (Join-Path $base ($phase+'.log'))
 $result = $LASTEXITCODE
 $ErrorActionPreference = 'Stop'
 if($result -ne 0) { "FINISHED exit=$result phase=$phase" | Set-Content $state; exit $result }
 [xml]$results = Get-Content -LiteralPath (Join-Path $base ($phase+'.xml')) -Raw
 $summary = $results.assemblies.assembly
 if([int]$summary.total -ne $expected -or [int]$summary.passed -ne $expected -or [int]$summary.skipped -ne 0 -or [int]$summary.errors -ne 0) {
  throw "Expected $expected passing cases in $phase"
 }
}
if(Busy) { 'BUSY phase=verify' | Set-Content $state; exit 75 }
try {
 $archive = Join-Path $base 'bundle.tgz'
 if((Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -ne '@BUNDLE@') { throw 'Primary-content bundle hash mismatch' }
 $payload = Join-Path $base 'payload'
 New-Item -ItemType Directory -Path $payload | Out-Null
 @PREPARE@
 Set-Location -LiteralPath $payload
 $observed = [ordered]@{}
 foreach($name in 'FileCat.Core.dll','FileCat.Platform.Windows.dll','FileCat.Platform.Windows.Tests.dll') {
  $observed[$name] = (Get-FileHash -LiteralPath (Join-Path $payload $name)).Hash.ToLowerInvariant()
 }
 $observed | ConvertTo-Json | Set-Content (Join-Path $base 'cache-proof.json')
 @TESTS@
 'FINISHED exit=0' | Set-Content $state
 exit 0
} catch {
 ($_ | Out-String) | Set-Content (Join-Path $base 'failure.txt')
 'FAILED' | Set-Content $state
 exit 1
}
"""
if baseline_revision:
    prepare = r"""
 $driver = Join-Path $base 'driver'
 New-Item -ItemType Directory -Path $driver | Out-Null
 & tar.exe -xzf $archive -C $driver
 if($LASTEXITCODE) { throw 'Primary-content overlay extraction failed' }
 $baseline = 'C:\FileCatDsa20261104R2\batch-@BASELINE@\payload'
 $plan = Get-Content -LiteralPath (Join-Path $driver 'plan.json') -Raw | ConvertFrom-Json
 foreach($row in $plan) {
  if([IO.Path]::IsPathRooted($row.path) -or $row.path.Contains(':') -or @($row.path -split '/' | Where-Object {$_ -eq '..'}).Count) {
   throw 'Invalid cached input'
  }
  $previous = Join-Path $baseline $row.path
  if((Get-FileHash -LiteralPath $previous).Hash.ToLowerInvariant() -ne $row.old) { throw 'Cached input changed' }
  if($row.patch -and $row.path -notin @('FileCat.Platform.Windows.Tests.dll','FileCat.Platform.Windows.Tests.pdb')) {
   throw 'Only the native test assembly can be overlaid'
  }
  $inputPath = if($row.patch) { Join-Path $driver ('patch/'+$row.path) } else { $previous }
  $target = Join-Path $payload $row.path
  New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
  Copy-Item -LiteralPath $inputPath -Destination $target
  if((Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant() -ne $row.new) { throw 'Staged input changed' }
 }
""".replace("@BASELINE@", baseline_revision)
    tests = "Run-Native 'native' @('*NativeWindowsPrimaryContentRefusesAClosedMappedWrite*','*NativeWindowsPrimaryContentRefusesAReplacementDirectoryJunction*','*NativeWindowsPrimaryContentRefusesARevisionWithoutTheJournalWitness*') 3"
else:
    prepare = "& tar.exe -xzf $archive -C $payload\n if($LASTEXITCODE) { throw 'Primary-content bundle extraction failed' }"
    tests = "Run-Native 'native' @('*NativeWindowsPrimaryContent*') 11\n Run-Native 'native-epoch' @('*NativeWindowsCreationAndChangeTimes*','*CloudTagsRepresentRegularFilePaths*','*QueuedDirectoryCannotIgnoreRenamedAncestors*') 6"
runner.write_text(launcher.replace("@BUNDLE@", sha(bundle)).replace("@PREPARE@", prepare).replace("@TESTS@", tests),
                  encoding="utf-8", newline="\n")

changes = set(subprocess.check_output(["git", "diff", "--name-only", "HEAD", "--", "src", "tests"], text=True).splitlines())
changes.update(subprocess.check_output(["git", "ls-files", "--others", "--exclude-standard", "src", "tests"], text=True).splitlines())
delta = temp / f"source-{revision}-primary-content.tgz"
assert not delta.exists() and changes
with tarfile.open(delta, "w:gz") as archive:
    for name in sorted(changes):
        archive.add(root / name, arcname=name, recursive=False)
proof = {"base": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
         "sources": {name: sha(root / name) for name in sorted(changes)}, "sourceArchive": sha(delta),
         "bundle": sha(bundle), "runner": sha(runner), "inputs": inputs, "baselineRevision": baseline_revision}
(temp / f"primary-content-proof-{revision}.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(bundle.name, bundle.stat().st_size, "bytes")
