import hashlib
import json
from pathlib import Path
import shutil
import tarfile

root = Path.cwd()
temp = root / ".tmp"
managed = root / "tests/FileCat.NativeAcceptance/DiskSpaceHarness/bin/Debug/net10.0"
prior = temp / "batch-r1351-mac"
stage = temp / "batch-r1353-mac"
(stage / "patch").mkdir(parents=True)

def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

native = json.loads((temp / "native-branch-proof-r1351.json").read_text(encoding="utf-8"))
baseline = next(record for record in native["platforms"] if record["platform"] == "mac")
files = []
for entry in baseline["files"]:
    name = entry["path"]
    if name == "run.py":
        continue
    old = sha(prior / name)
    assert old == entry["sha256"]
    patch = name in ("DiskSpaceHarness.dll", "DiskSpaceHarness.pdb")
    if patch:
        shutil.copyfile(managed / name, stage / "patch" / name)
    if name in ("FileCat.dll", "FileCat.Core.dll"):
        assert old == sha(managed / name)
    files.append(dict(path=name, baselineSha256=old, sha256=sha(managed / name) if patch else old, patch=patch))
plan = dict(baseline="/Users/benny/FileCatValidation/dsa-batch-20261107-r1351/app", files=files)
(stage / "plan.json").write_text(json.dumps(plan, indent=2), encoding="utf-8")

controller = (prior / "run.py").read_text(encoding="utf-8")
controller = controller.replace("arguments = ['./FileCat', '--branch-refresh-probe']", "arguments = ['./FileCat', '--branch-refresh-probe', '--replacement-compositor-capture']")
controller = controller.replace("assert re.fullmatch(r'replacement-(memory|indexed)-(keep|replace)-(ClassicDark|Classic|HighContrast)[.]png', frame)",
                                "assert frame == 'branch-recipe-evidence.png'")
controller = controller.replace("and probe['recipePreviewPassed']", "and probe['recipePreviewPassed'] and len(captured) == 1")
assert "frame == 'branch-recipe-evidence.png'" in controller and "len(captured) == 1" in controller
compile(controller, "run.py", "exec")
(stage / "run.py").write_text(controller, encoding="utf-8", newline="\n")

bundle = temp / "dsa-batch-mac-r1353.tgz"
assert not bundle.exists()
with tarfile.open(bundle, "w:gz") as output:
    for path in stage.rglob("*"):
        if path.is_file():
            output.add(path, arcname=path.relative_to(stage), recursive=False)
old = sha(temp / "dsa-batch-mac-r1351.tgz")
runner = (temp / "batch-mac-r1351.sh").read_text(encoding="utf-8").replace(old, sha(bundle))
assert old not in runner and sha(bundle) in runner
(temp / "batch-mac-r1353.sh").write_text(runner, encoding="utf-8", newline="\n")
print(f"Verified cache inputs: {len(files)}. Candidate transfer: {bundle.stat().st_size} bytes.")
