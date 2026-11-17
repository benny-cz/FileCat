import hashlib
import json
from pathlib import Path
import shutil
import tarfile

root = Path.cwd()
temp = root / ".tmp"
drivers = root / "docs/validation/disk-space/drivers"
test_bin = root / "tests/FileCat.Core.Tests/bin/Release/net10.0"
revision = "r1348"
log = (temp / "metadata-recipes-final-build-r1348.txt").read_text(encoding="utf-8-sig")
assert all(check in log for check in ("Build succeeded.", "0 Warning(s)", "0 Error(s)"))

def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

unix = (drivers / "epoch-app-unix-template-r1194.sh").read_text()
unix = unix.replace("for phase in app-rescan render settings;do", "for phase in core;do")
unix = unix.replace("  app-rescan)", "  core) assembly=FileCat.Core.Tests.dll;set -- -method '*AnalysisRecipeTests.*' -method '*IndexedRecipe*';;\n  app-rescan)")
windows = (drivers / "epoch-app-windows-template-r1195.ps1").read_text()
windows = windows.replace("'epoch','native-epoch','app-rescan','core','native','render','settings'", "'core'")
windows = windows.replace("'tests\\FileCat.Core.Tests\\bin\\Debug\\net10.0'", "'render'")
windows = windows.replace("  if($phase-eq'app-rescan'", "  if($phase-eq'core'-and([int]$a.total-ne11-or[int]$a.passed-ne11-or[int]$a.skipped-ne0)){throw 'Recipe discovery mismatch'}\n  if($phase-eq'app-rescan'")
windows = windows.replace("@('-class','FileCat.Core.Tests.Analysis*','-class','*CommandTests','-class','*DeviceIoSchedulerTests','-class','*PathAndStateTests','-class','*ZipListingRevisionTests')", "@('-method','*AnalysisRecipeTests.*','-method','*IndexedRecipe*')")
validator = """import sys, xml.etree.ElementTree as E
assembly = E.parse(sys.argv[2]).getroot().find('assembly')
keys = ('total', 'passed', 'failed', 'errors', 'skipped')
actual = tuple(int(assembly.get(key, '-1')) for key in keys)
assert actual == (11, 11, 0, 0, 0), actual
print(dict(zip(keys, actual)))
"""

stages = {}
for platform in ("windows", "mac", "linux"):
    stage = temp / f"batch-{revision}-{platform}"
    output = stage / "render"
    output.mkdir(parents=True)
    for source in test_bin.iterdir():
        if source.is_file() and source.suffix in (".dll", ".pdb", ".json"):
            shutil.copyfile(source, output / source.name)
    (stage / "validate.py").write_text(validator, encoding="utf-8")
    archive = temp / f"dsa-batch-{platform}-{revision}.tgz"
    assert not archive.exists()
    with tarfile.open(archive, "w:gz", compresslevel=4) as stream:
        for path in stage.rglob("*"):
            if path.is_file():
                stream.add(path, arcname=path.relative_to(stage), recursive=False)
    suffix = ".ps1" if platform == "windows" else ".sh"
    runner = temp / f"batch-{platform}-{revision}{suffix}"
    runner.write_text((windows if platform == "windows" else unix).replace("@HASH@", sha(archive)), encoding="utf-8", newline="\n")
    stages[platform] = {"archive": sha(archive), "bytes": archive.stat().st_size, "runner": sha(runner)}
sources = (
    "src/FileCat.Core/Analysis/AnalysisRecipes.cs", "src/FileCat.Core/Analysis/AnalysisRecipePreview.cs",
    "tests/FileCat.Core.Tests/AnalysisRecipeTests.cs", "tests/FileCat.Core.Tests/AnalysisDatasetSummaryTests.Recipes.cs",
)
proof = {"platforms": stages, "sources": {name: sha(root / name) for name in sources},
         "product": sha(test_bin / "FileCat.Core.dll"), "tests": sha(test_bin / "FileCat.Core.Tests.dll")}
(temp / f"metadata-recipes-proof-{revision}.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(proof["platforms"]))
