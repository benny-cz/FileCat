"""Stage only the corrected test harness on top of the exact r1368 application."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import tarfile

root = Path.cwd()
temp = root / ".tmp"
managed = root / "tests/FileCat.NativeAcceptance/DiskSpaceHarness/bin/Debug/net10.0"
log = (temp / "duplicate-estimate-native-build-r1370.txt").read_text(encoding="utf-8-sig")
assert all(marker in log for marker in ("Build succeeded.", "0 Warning(s)", "0 Error(s)"))


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


prior = json.loads((temp / "duplicate-estimate-native-proof-r1368.json").read_text())
drivers = {"DiskSpaceHarness.dll", "DiskSpaceHarness.pdb"}
proof = {}
for platform in ("windows", "mac", "linux"):
    old = prior["platforms"][platform]
    original = temp / f"batch-r1368-{platform}"
    assert sha(temp / f"dsa-batch-{platform}-r1368.tgz") == old["bundle"]
    plan = json.loads((original / "plan.json").read_text())
    stage = temp / f"batch-r1370-{platform}"
    (stage / "patch").mkdir(parents=True)
    for row in plan["files"]:
        name = row["path"]
        old_path = original / "patch" / name if row["patch"] else temp / f"batch-r1359-{platform}" / name
        assert sha(old_path) == row["sha256"]
        row["baselineSha256"] = row["sha256"]
        row["patch"] = name in drivers
        if name in drivers:
            shutil.copyfile(managed / name, stage / "patch" / name)
            row["sha256"] = sha(managed / name)
        if name in ("FileCat.dll", "FileCat.Core.dll"):
            assert row["sha256"] == sha(managed / name)
    plan["baseline"] = plan["baseline"].replace("r1359", "r1368")
    (stage / "plan.json").write_text(json.dumps(plan, indent=2), encoding="utf-8")
    if platform != "windows":
        worker = (original / "run.py").read_text(encoding="utf-8")
        worker, replaced = re.subn(r"assert str\(name\) in \(.*?\), 'Product patch refused'",
                                    "assert str(name) in ('DiskSpaceHarness.dll', 'DiskSpaceHarness.pdb'), 'Product patch refused'", worker)
        assert replaced == 1
        compile(worker, "run.py", "exec")
        (stage / "run.py").write_text(worker, encoding="utf-8", newline="\n")
    suffix = ".ps1" if platform == "windows" else ".sh"
    old_runner = temp / f"batch-{platform}-r1368{suffix}"
    assert sha(old_runner) == old["runner"]
    runner = old_runner.read_text(encoding="utf-8")
    if platform == "windows":
        runner, replaced = re.subn(r"\$item\.path-notin@\(.*?\)\)\{throw 'Native product patch refused'\}",
                                    "$item.path-notin@('DiskSpaceHarness.dll','DiskSpaceHarness.pdb')){throw 'Native product patch refused'}", runner)
        assert replaced == 1
    bundle = temp / f"dsa-batch-{platform}-r1370.tgz"
    assert not bundle.exists() and old["bundle"] in runner
    with tarfile.open(bundle, "w:gz") as output:
        for path in stage.rglob("*"):
            if path.is_file():
                output.add(path, arcname=path.relative_to(stage).as_posix(), recursive=False)
    launcher = temp / f"batch-{platform}-r1370{suffix}"
    launcher.write_text(runner.replace(old["bundle"], sha(bundle)), encoding="utf-8", newline="\n")
    proof[platform] = {"bundle": sha(bundle), "runner": sha(launcher), "bytes": bundle.stat().st_size, "inputs": len(plan["files"])}

(temp / "cache-input-r1370").mkdir()
harness = root / "tests/FileCat.NativeAcceptance/DiskSpaceHarness"
document = {"harnessSource": sha(harness / "DuplicateReview.cs"), "programSource": sha(harness / "Program.cs"), "platforms": proof}
assert document["programSource"] == prior["programSource"]
(temp / "duplicate-estimate-native-proof-r1370.json").write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
print(json.dumps({name: result["bytes"] for name, result in proof.items()}))
