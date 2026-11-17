import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tarfile

root = Path.cwd()
temp = root / ".tmp"
drivers = root / "docs/validation/disk-space/drivers"
revision = "r1354"
for name in ("saved-recipe-core-final-build-r1354.txt", "saved-recipe-render-build-r1354.txt"):
    log = (temp / name).read_text(encoding="utf-8-sig")
    assert all(check in log for check in ("Build succeeded.", "0 Warning(s)", "0 Error(s)"))

def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

core_patterns = ("*AnalysisRecipeTests.*", "*IndexedRecipe*", "*AnalysisSavedRecipeTests.*", "*AnalysisCatalogTests.PrivateRecipe*")
render_patterns = ("*NativeRecipeDialog*", "*NativeSavedRecipe*")

def unix_arguments(patterns):
    return " ".join(f"-method '{pattern}'" for pattern in patterns)

def windows_arguments(patterns):
    return "@(" + ",".join(f"'-method','{pattern}'" for pattern in patterns) + ")"

unix = (drivers / "epoch-app-unix-template-r1194.sh").read_text()
unix = unix.replace("for phase in app-rescan render settings;do", "for phase in core render;do")
unix = unix.replace(' assembly=FileCat.Rendering.Tests.dll', ' bin="$base/payload/$phase"\n assembly=FileCat.Rendering.Tests.dll')
unix = unix.replace("  app-rescan)", f"  core) assembly=FileCat.Core.Tests.dll;set -- {unix_arguments(core_patterns)};;\n  app-rescan)")
unix = unix.replace("  render) set -- -class '*AnalysisRenderingTests';;", f"  render) set -- {unix_arguments(render_patterns)};;")

windows = (drivers / "epoch-app-windows-template-r1195.ps1").read_text()
windows = windows.replace("'epoch','native-epoch','app-rescan','core','native','render','settings'", "'core','render'")
windows = windows.replace("'tests\\FileCat.Core.Tests\\bin\\Debug\\net10.0'", "'core'")
windows = windows.replace("@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*DeviceIoSchedulerTests','-class','*PathAndStateTests','-class','*ZipListingRevisionTests')", windows_arguments(core_patterns))
windows = windows.replace("@('-class','*AnalysisRenderingTests')", windows_arguments(render_patterns))
guard = "  if($phase-eq'core'-and([int]$a.total-ne20-or[int]$a.passed-ne20-or[int]$a.skipped-ne0)){throw 'Recipe core discovery mismatch'}\n"
guard += "  if($phase-eq'render'-and([int]$a.total-ne5-or[int]$a.passed-ne5-or[int]$a.skipped-ne0)){throw 'Recipe dialog discovery mismatch'}\n"
windows = windows.replace("  if($phase-eq'app-rescan'", guard + "  if($phase-eq'app-rescan'")
validator = """import sys, xml.etree.ElementTree as E
phase, filename = sys.argv[1:]
assembly = E.parse(filename).getroot().find('assembly')
keys = ('total', 'passed', 'failed', 'errors', 'skipped')
counts = tuple(int(assembly.get(key, '-1')) for key in keys)
expected = {'core': 20, 'render': 5}[phase]
assert counts == (expected, expected, 0, 0, 0), (phase, counts)
print(phase, dict(zip(keys, counts)))
"""

proof = {}
core_bin = root / "tests/FileCat.Core.Tests/bin/Release/net10.0"
render_bin = root / "tests/FileCat.Rendering.Tests/bin/Debug/net10.0"
for platform, rids in (("windows", ("win", "win-x64")), ("mac", ("osx", "osx-arm64")), ("linux", ("linux-x64",))):
    stage = temp / f"batch-{revision}-{platform}"
    for phase, binary in (("core", core_bin), ("render", render_bin)):
        output = stage / phase
        output.mkdir(parents=True)
        for path in binary.iterdir():
            if path.is_file() and path.suffix in (".dll", ".pdb", ".json"):
                shutil.copyfile(path, output / path.name)
        for rid in rids:
            if (binary / "runtimes" / rid).exists():
                shutil.copytree(binary / "runtimes" / rid, output / "runtimes" / rid)
    (stage / "validate.py").write_text(validator, encoding="utf-8")
    archive = temp / f"dsa-batch-{platform}-{revision}.tgz"
    assert not archive.exists()
    with tarfile.open(archive, "w:gz", compresslevel=4) as stream:
        for path in sorted(stage.rglob("*")):
            if path.is_file():
                stream.add(path, arcname=path.relative_to(stage).as_posix(), recursive=False)
    suffix = ".ps1" if platform == "windows" else ".sh"
    runner = temp / f"batch-{platform}-{revision}{suffix}"
    runner.write_text((windows if platform == "windows" else unix).replace("@HASH@", sha(archive)), encoding="utf-8", newline="\n")
    proof[platform] = dict(archive=sha(archive), bytes=archive.stat().st_size, runner=sha(runner),
                           binaries={p.relative_to(stage).as_posix(): sha(p) for p in sorted(stage.rglob("*.dll"))})

head = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
changed = set(subprocess.check_output(["git", "diff", "--name-only", "HEAD", "--", "src", "tests"], text=True).splitlines())
changed.update(subprocess.check_output(["git", "ls-files", "--others", "--exclude-standard", "src", "tests"], text=True).splitlines())
source = temp / f"source-{revision}-saved-recipes-delta.tgz"
assert not source.exists()
with tarfile.open(source, "w:gz") as output:
    for name in sorted(changed):
        output.add(root / name, arcname=name, recursive=False)
(temp / f"saved-recipe-proof-{revision}.json").write_text(json.dumps(dict(head=head, source=sha(source),
    changes={name: sha(root / name) for name in sorted(changed)}, platforms=proof), indent=2) + "\n", encoding="utf-8")
print(json.dumps({platform: record["bytes"] for platform, record in proof.items()}))
