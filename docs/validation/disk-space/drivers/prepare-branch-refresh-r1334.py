"""Package the branch binding and rendering checks for native test machines."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tarfile


root = Path.cwd()
tmp = root / ".tmp"
drivers = root / "docs/validation/disk-space/drivers"
revision = "r1334"
for name in ("branch-refresh-controls-build-r1332.txt", "branch-refresh-fixture-build-r1333.txt"):
    build = (tmp / name).read_text(encoding="utf-8-sig")
    assert all(marker in build for marker in ("Build succeeded.", "0 Warning(s)", "0 Error(s)"))


def hash_file(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


windows = (drivers / "epoch-app-windows-template-r1195.ps1").read_text()
windows = windows.replace(
    "foreach($phase in 'epoch','native-epoch','app-rescan','core','native','render','settings')",
    "foreach($phase in 'app-rescan','render')",
).replace("'-method','*NativeRescanPreparation*'", "'-method','*NativeBranchRefresh*'")
windows = windows.replace("$a.total-ne7-or[int]$a.passed-ne7", "$a.total-ne6-or[int]$a.passed-ne6")
windows = windows.replace(
    "  if($phase-eq'epoch'",
    "  if($phase-eq'render'-and([int]$a.total-ne851-or[int]$a.passed-ne850-or[int]$a.skipped-ne1)){throw 'Rendering discovery mismatch'}\n  if($phase-eq'epoch'",
)
unix = (drivers / "epoch-app-unix-template-r1194.sh").read_text()
unix = unix.replace("for phase in app-rescan render settings;do", "for phase in app-rescan render;do")
unix = unix.replace("'*NativeRescanPreparation*'", "'*NativeBranchRefresh*'")
validator = """import sys, xml.etree.ElementTree as E
phase, source = sys.argv[1:]
assembly = E.parse(source).getroot().find('assembly')
keys = ('total', 'passed', 'failed', 'errors', 'skipped')
actual = tuple(int(assembly.get(key, '-1')) for key in keys)
expected = {'app-rescan': (6,6,0,0,0), 'render': (851,851,0,0,0)}
assert actual == expected[phase], (phase, actual)
print(phase, dict(zip(keys, actual)))
"""

test_bin = root / "tests/FileCat.Rendering.Tests/bin/Debug/net10.0"
platforms = {}
for platform, rids in (("windows", ("win", "win-x64")), ("mac", ("osx", "osx-arm64")), ("linux", ("linux-x64",))):
    stage = tmp / f"batch-{revision}-{platform}"
    output = stage / "render"
    output.mkdir(parents=True)
    for path in test_bin.iterdir():
        if path.is_file():
            shutil.copy2(path, output / path.name)
        elif path.name == "TestData":
            shutil.copytree(path, output / path.name)
    for rid in rids:
        source = test_bin / "runtimes" / rid
        if source.exists():
            shutil.copytree(source, output / "runtimes" / rid)
    (stage / "validate.py").write_text(validator, encoding="utf-8")
    archive = tmp / f"dsa-batch-{platform}-{revision}.tgz"
    assert not archive.exists()
    subprocess.run(["tar.exe", "-czf", str(archive), "-C", str(stage), "."], check=True)
    suffix = ".ps1" if platform == "windows" else ".sh"
    runner = tmp / f"batch-{platform}-{revision}{suffix}"
    runner.write_text((windows if platform == "windows" else unix).replace("@HASH@", hash_file(archive)), encoding="utf-8", newline="\n")
    platforms[platform] = {
        "archiveSha256": hash_file(archive),
        "archiveBytes": archive.stat().st_size,
        "runnerSha256": hash_file(runner),
        "inputs": {p.relative_to(stage).as_posix(): hash_file(p) for p in stage.rglob("*") if p.is_file()},
    }

paths = subprocess.check_output(
    ["git", "ls-files", "--cached", "--others", "--exclude-standard", "src", "tests", "FileCat.slnx",
     "global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"],
    text=True,
).splitlines()
source_archive = tmp / f"source-{revision}-branch-refresh.tgz"
assert not source_archive.exists()
with tarfile.open(source_archive, "w:gz") as destination:
    for name in paths:
        destination.add(root / name, arcname=name, recursive=False)
proof = {
    "head": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
    "sourceArchiveSha256": hash_file(source_archive),
    "sources": {name: hash_file(root / name) for name in paths},
    "platforms": platforms,
}
(tmp / f"branch-refresh-proof-{revision}.json").write_text(json.dumps(proof, indent=2), encoding="utf-8")
print(json.dumps({platform: details["archiveBytes"] for platform, details in platforms.items()}))
