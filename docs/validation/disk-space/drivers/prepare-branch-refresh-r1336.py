"""Run the full rendering suite from verified cached branch-refresh inputs."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tarfile


root = Path.cwd()
tmp = root / ".tmp"
drivers = root / "docs/validation/disk-space/drivers"
parser = argparse.ArgumentParser()
mode = parser.add_mutually_exclusive_group()
mode.add_argument("--change-hints", action="store_true")
mode.add_argument("--scope-fix", action="store_true")
options = parser.parse_args()
scope_fix = options.scope_fix
change_hints = options.change_hints or scope_fix
revision = "r1345" if scope_fix else "r1339" if change_hints else "r1336"
total = 10 if scope_fix else 854 if change_hints else 851
windows_skips = 0 if scope_fix else 1
previous = json.loads((tmp / "branch-refresh-proof-r1334.json").read_text())
build_name = "branch-change-hints-build-r1338.txt" if change_hints else "branch-refresh-recorded-fixture-build-r1335.txt"
if scope_fix:
    build_name = "branch-refresh-scope-final-build-r1344.txt"
build = (tmp / build_name).read_text(encoding="utf-8-sig")
assert all(marker in build for marker in ("Build succeeded.", "0 Warning(s)", "0 Error(s)"))


def digest(path):
    with path.open("rb") as content:
        return hashlib.file_digest(content, "sha256").hexdigest()


modified = {name: digest(root / name) for name, old in previous["sources"].items() if digest(root / name) != old}
fixture = "tests/FileCat.Rendering.Tests/AnalysisRenderingTests.BranchRefresh.cs"
expected_changes = {fixture}
new_sources = set()
if change_hints:
    expected_changes.update({
        "src/FileCat.App/Services/AnalysisIndexedAcquisition.cs",
        "src/FileCat.App/ViewModels/DiskSpaceViewModel.Rescan.cs",
        "src/FileCat.App/Views/DiskSpaceWindow.Indexed.cs",
        "src/FileCat.App/Views/DiskSpaceWindow.cs",
    })
    new_sources = {
        "src/FileCat.App/Services/AnalysisIndexedAcquisition.ChangeHints.cs",
        "src/FileCat.App/Services/AnalysisRootChangeHints.cs",
        "src/FileCat.App/Views/DiskSpaceWindow.RuntimeChangeHint.cs",
        "tests/FileCat.Rendering.Tests/AnalysisRenderingTests.RuntimeChangeHints.cs",
    }
    if scope_fix:
        expected_changes.update({
            "src/FileCat.App/Views/DiskSpaceWindow.BranchRefresh.cs",
            "tests/FileCat.NativeAcceptance/DiskSpaceHarness/Program.cs",
        })
        new_sources.add("tests/FileCat.NativeAcceptance/DiskSpaceHarness/BranchRefresh.cs")
    actual = subprocess.check_output(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "src", "tests"], text=True
    ).splitlines()
    assert set(actual) - previous["sources"].keys() == new_sources
assert set(modified) == expected_changes, modified
modified.update({name: digest(root / name) for name in new_sources})
source = tmp / f"source-{revision}-branch-fixture.tgz"
assert not source.exists()
with tarfile.open(source, "w:gz") as output:
    for name in sorted(modified):
        output.add(root / name, arcname=name)

unix = (drivers / "run-focused-test-delta.py").read_text()
unix = unix.replace("['pgrep','-f','^[^ ]*dotnet +.*FileCat']", "['pgrep','-x','dotnet']")
unix = unix.replace("('render-'+revision+'.xml')", "'render.xml'").replace("('render-'+revision+'.log')", "'render.log'")
start = unix.index(" (output/'cache-proof.txt')")
end = unix.index("\n", start)
unix = unix[:start] + " (output/'cache-proof.json').write_text(json.dumps(plan,indent=2))" + unix[end:]
unix = unix.replace(
    " state('FINISHED exit='+str(code));sys.exit(code)",
    " import xml.etree.ElementTree as E\n"
    " a=E.parse(output/'render.xml').getroot().find('assembly')\n"
    f" if code==0 and [int(a.get(k,'-1')) for k in ('total','passed','failed','errors','skipped')]!=[{total},{total},0,0,0]:raise RuntimeError('Rendering discovery mismatch')\n"
    " state('FINISHED exit='+str(code));sys.exit(code)",
)
compile(unix, "run.py", "exec")
windows = (drivers / "run-focused-test-delta.ps1").read_text()
windows = windows.replace("render-@REV@.xml", "render.xml").replace("render-@REV@.log", "render.log")
start = windows.index(" ('VERIFIED cachedFiles=")
end = windows.index("\n", start)
windows = windows[:start] + " $plan|ConvertTo-Json -Depth 8|Set-Content (Join-Path $base 'cache-proof.json')" + windows[end:]
windows = windows.replace(
    "$ErrorActionPreference='Stop';('FINISHED exit='+$result)",
    "$ErrorActionPreference='Stop';if($result-eq0){"
    "[xml]$xml=Get-Content -Raw -LiteralPath (Join-Path $base 'render.xml');$a=$xml.assemblies.assembly;"
    f"if([int]$a.total-ne{total}-or[int]$a.passed-ne{total-windows_skips}-or[int]$a.failed-ne0-or[int]$a.errors-ne0-or[int]$a.skipped-ne{windows_skips})"
    "{throw 'Rendering discovery mismatch'}};"
    "('FINISHED exit='+$result)",
)

binary_dir = root / "tests/FileCat.Rendering.Tests/bin/Debug/net10.0"
assembly = "FileCat.Rendering.Tests"
patched = {assembly + ".dll", assembly + ".pdb"}
if change_hints:
    patched.update({"FileCat.dll", "FileCat.pdb"})
    unix = unix.replace("not in(plan['assembly'],plan['assembly'][:-4]+'.pdb')",
                        "not in(plan['assembly'],plan['assembly'][:-4]+'.pdb','FileCat.dll','FileCat.pdb')")
    windows = windows.replace("$item.path-notin $plan.assembly,($plan.assembly-replace'[.]dll$','.pdb')",
                              "$item.path-notin $plan.assembly,($plan.assembly-replace'[.]dll$','.pdb'),'FileCat.dll','FileCat.pdb'")
proof = {}
for platform in ("windows", "mac", "linux"):
    stage = tmp / f"focused-delta-{platform}-{revision}"
    stage.mkdir()
    (stage / "patch").mkdir()
    old_dir = tmp / f"batch-r1334-{platform}"
    files = []
    actual_changes = set()
    for key, old in previous["platforms"][platform]["inputs"].items():
        if not key.startswith("render/"):
            continue
        name = key.removeprefix("render/")
        assert digest(old_dir / key) == old, key
        current = binary_dir / name
        new = digest(current)
        if new != old:
            assert name in patched, key
            actual_changes.add(name)
            shutil.copy2(current, stage / "patch" / name)
        files.append(dict(path=name, baselineSha256=old, sha256=new, patch=new != old))
    assert actual_changes == patched
    if platform == "windows":
        baseline = r"C:\FileCatDsa20261104R2\batch-r1334\payload\render"
    else:
        home = "/Users/benny" if platform == "mac" else "/home/benny"
        baseline = f"{home}/FileCatValidation/dsa-batch-20261107-r1334/payload/render"
        (stage / "run.py").write_text(unix, encoding="utf-8")
    methods = ["*NativeBranchRefresh*", "*NativeAnalysisChangeHints*"] if scope_fix else ["*AnalysisRenderingTests.*"]
    plan = dict(revision=revision, baseline=baseline, assembly=assembly + ".dll", methods=methods, files=files)
    (stage / "plan.json").write_text(json.dumps(plan, indent=2), encoding="utf-8")
    archive = tmp / f"dsa-batch-{platform}-{revision}.tgz"
    assert not archive.exists()
    with tarfile.open(archive, "w:gz") as output:
        for path in stage.rglob("*"):
            if path.is_file():
                output.add(path, arcname=path.relative_to(stage).as_posix())
    hash_value = digest(archive)
    if platform == "windows":
        suffix = ".ps1"
        launcher = windows.replace("@BASELINE@", baseline).replace("@REV@", revision).replace("@HASH@", hash_value)
    else:
        suffix = ".sh"
        launcher = """set -eu
cd "$(dirname "$0")"
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -x dotnet >/dev/null 2>&1; then printf 'BUSY phase=verify\\n' > state.txt; exit 75; fi
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
if [ "$actual" != '@HASH@' ]; then printf 'FAILED phase=integrity\\n' > state.txt; exit 74; fi
umask 077
mkdir driver
tar -xzf bundle.tgz -C driver
python3 driver/run.py
""".replace("@HASH@", hash_value)
    runner = tmp / f"batch-{platform}-{revision}{suffix}"
    runner.write_text(launcher, encoding="utf-8", newline="\n")
    proof[platform] = dict(bytes=archive.stat().st_size, sha256=hash_value, runnerSha256=digest(runner), cachedInputs=len(files))
(tmp / f"cache-input-{revision}").mkdir()
(tmp / f"branch-refresh-proof-{revision}.json").write_text(json.dumps({
    "baseline": "r1334",
    "baselineProofSha256": digest(tmp / "branch-refresh-proof-r1334.json"),
    "sourceFixtureSha256": digest(source),
    "changedSources": modified,
    "platforms": proof,
}, indent=2), encoding="utf-8")
print(json.dumps(proof))
