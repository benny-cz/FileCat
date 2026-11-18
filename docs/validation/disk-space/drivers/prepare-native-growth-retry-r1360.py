"""Reuse the r1359 application and overlay only its native test driver."""
import hashlib
import json
from pathlib import Path
import shutil
import sys
import tarfile

root = Path.cwd()
temp = root / ".tmp"
managed = root / "tests/FileCat.NativeAcceptance/DiskSpaceHarness/bin/Debug/net10.0"
revision = sys.argv[1] if len(sys.argv) == 2 else "r1360"
assert revision in ("r1360", "r1362", "r1364")
log = (temp / f"growth-native-build-{revision}.txt").read_text(encoding="utf-8-sig")
assert all(marker in log for marker in ("Build succeeded.", "0 Warning(s)", "0 Error(s)"))

def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

prior = json.loads((temp / "native-branch-proof-r1359.json").read_text(encoding="utf-8"))
proof = {}
drivers = {"DiskSpaceHarness.dll", "DiskSpaceHarness.pdb"}
for record in prior["platforms"]:
    platform = record["platform"]
    original = temp / f"batch-r1359-{platform}"
    stage = temp / f"batch-{revision}-{platform}"
    (stage / "patch").mkdir(parents=True)
    files = []
    for item in record["files"]:
        name = item["path"]
        if name == "run.py":
            continue
        old = sha(original / name)
        assert old == item["sha256"], name
        changed = name in drivers
        if changed:
            shutil.copyfile(managed / name, stage / "patch" / name)
        if name in ("FileCat.dll", "FileCat.Core.dll"):
            assert old == sha(managed / name), name
        files.append(dict(path=name, baselineSha256=old, sha256=sha(managed / name) if changed else old, patch=changed))
    if platform == "windows":
        baseline = r"C:\FileCatDsa20261104R2\batch-r1359\app"
    else:
        home = "/Users/benny" if platform == "mac" else "/home/benny"
        baseline = home + "/FileCatValidation/dsa-batch-20261107-r1359/app"
        worker = (original / "run.py").read_text(encoding="utf-8")
        if revision == "r1364":
            worker = worker.replace("probe['sessions']==4", "probe['sessions']==2 and probe['earlierTabsClosed']==2")
        compile(worker, "run.py", "exec")
        (stage / "run.py").write_text(worker, encoding="utf-8", newline="\n")
    (stage / "plan.json").write_text(json.dumps(dict(baseline=baseline, files=files), indent=2), encoding="utf-8")
    bundle = temp / f"dsa-batch-{platform}-{revision}.tgz"
    assert not bundle.exists()
    with tarfile.open(bundle, "w:gz") as output:
        for path in stage.rglob("*"):
            if path.is_file():
                output.add(path, arcname=path.relative_to(stage).as_posix(), recursive=False)
    suffix = ".ps1" if platform == "windows" else ".sh"
    launcher = (temp / f"batch-{platform}-r1359{suffix}").read_text(encoding="utf-8")
    assert record["sha256"] in launcher
    if platform == "windows" and revision == "r1364":
        launcher = launcher.replace("$probe.sessions-ne4", "$probe.sessions-ne2-or$probe.earlierTabsClosed-ne2")
    runner = temp / f"batch-{platform}-{revision}{suffix}"
    runner.write_text(launcher.replace(record["sha256"], sha(bundle)), encoding="utf-8", newline="\n")
    proof[platform] = dict(bundle=sha(bundle), runner=sha(runner), bytes=bundle.stat().st_size, inputs=len(files))

(temp / f"cache-input-{revision}").mkdir()
source = root / "tests/FileCat.NativeAcceptance/DiskSpaceHarness/GrowthRecipe.cs"
(temp / f"growth-native-proof-{revision}.json").write_text(json.dumps({"harnessSource": sha(source), "platforms": proof}, indent=2) + "\n", encoding="utf-8")
print(json.dumps({name: outcome["bytes"] for name, outcome in proof.items()}))
