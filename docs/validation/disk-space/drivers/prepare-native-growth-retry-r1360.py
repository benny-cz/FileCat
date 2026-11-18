"""Reuse verified r1359 native inputs for growth retries and duplicate review."""
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
assert revision in ("r1360", "r1362", "r1364", "r1368")
duplicate = revision == "r1368"
build = "duplicate-estimate-native-build" if duplicate else "growth-native-build"
log = (temp / f"{build}-{revision}.txt").read_text(encoding="utf-8-sig")
assert all(marker in log for marker in ("Build succeeded.", "0 Warning(s)", "0 Error(s)"))

def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

prior = json.loads((temp / "native-branch-proof-r1359.json").read_text(encoding="utf-8"))
proof = {}
drivers = {"DiskSpaceHarness.dll", "DiskSpaceHarness.pdb"}
previous_retry = json.loads((temp / "growth-native-proof-r1364.json").read_text(encoding="utf-8")) if duplicate else None
for record in prior["platforms"]:
    platform = record["platform"]
    original = temp / f"batch-r1359-{platform}"
    stage = temp / f"batch-{revision}-{platform}"
    (stage / "patch").mkdir(parents=True)
    rendered = {}
    if duplicate:
        plan = json.loads((temp / f"focused-delta-{platform}-r1366/plan.json").read_text(encoding="utf-8"))
        rendered = {row["path"]: row["sha256"] for row in plan["files"]}
    files = []
    for item in record["files"]:
        name = item["path"]
        if name == "run.py":
            continue
        old = sha(original / name)
        assert old == item["sha256"], name
        changed = name in drivers or duplicate and name.endswith((".dll", ".pdb")) and name in rendered and rendered[name] != old
        if changed:
            shutil.copyfile(managed / name, stage / "patch" / name)
        if name in ("FileCat.dll", "FileCat.Core.dll"):
            assert (rendered[name] if duplicate else old) == sha(managed / name), name
        if duplicate and changed and name not in drivers:
            assert rendered[name] == sha(managed / name), name
        files.append(dict(path=name, baselineSha256=old, sha256=sha(managed / name) if changed else old, patch=changed))
    changed_paths = tuple(sorted(row["path"] for row in files if row["patch"]))
    if platform == "windows":
        baseline = r"C:\FileCatDsa20261104R2\batch-r1359\app"
    else:
        home = "/Users/benny" if platform == "mac" else "/home/benny"
        baseline = home + "/FileCatValidation/dsa-batch-20261107-r1359/app"
        worker = ((temp / f"batch-r1364-{platform}" if duplicate else original) / "run.py").read_text(encoding="utf-8")
        if revision == "r1364":
            worker = worker.replace("probe['sessions']==4", "probe['sessions']==2 and probe['earlierTabsClosed']==2")
        if duplicate:
            worker = worker.replace("--growth-recipe-probe", "--duplicate-review-probe").replace("growthRecipeProbe", "duplicateReviewProbe")
            worker = worker.replace("growth-observed.png", "duplicate-review-estimate.png").replace("growth-*.png", "duplicate-review-*.png")
            worker = worker.replace("('DiskSpaceHarness.dll', 'DiskSpaceHarness.pdb')", repr(changed_paths))
            worker = worker.replace("probe['name']=='growth-payload.log' and probe['sessions']==2 and probe['earlierTabsClosed']==2 and probe['addedBytes']==32768 and probe['sourceAndSavedInventoryPreserved']",
                                    "probe['sessions']==2 and probe['candidateObjects']==2 and probe['knownReadBytes']==8 and probe['capturedMarks']==4 and probe['additionalHardLinkNames']==1 and probe['preservingDefault'] and probe['retainedHiddenMarksAndBasket']")
            assert "growth-" not in worker and "probe['knownReadBytes']==8" in worker
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
    template = "r1364" if duplicate else "r1359"
    launcher = (temp / f"batch-{platform}-{template}{suffix}").read_text(encoding="utf-8")
    previous_hash = previous_retry["platforms"][platform]["bundle"] if duplicate else record["sha256"]
    assert previous_hash in launcher
    if platform == "windows" and revision == "r1364":
        launcher = launcher.replace("$probe.sessions-ne4", "$probe.sessions-ne2-or$probe.earlierTabsClosed-ne2")
    if platform == "windows" and duplicate:
        launcher = launcher.replace("--growth-recipe-probe", "--duplicate-review-probe").replace("growthRecipeProbe", "duplicateReviewProbe")
        launcher = launcher.replace("growth-*.png", "duplicate-review-*.png").replace("growth probe", "duplicate review")
        launcher = launcher.replace("'DiskSpaceHarness.dll','DiskSpaceHarness.pdb'", ",".join(f"'{path}'" for path in changed_paths))
        launcher = launcher.replace("$probe.name-ne'growth-payload.log'-or$probe.sessions-ne2-or$probe.earlierTabsClosed-ne2-or$probe.addedBytes-ne32768-or!$probe.sourceAndSavedInventoryPreserved",
                                    "$probe.sessions-ne2-or$probe.candidateObjects-ne2-or$probe.knownReadBytes-ne8-or$probe.capturedMarks-ne4-or$probe.additionalHardLinkNames-ne1-or!$probe.preservingDefault-or!$probe.retainedHiddenMarksAndBasket")
        assert "growth-" not in launcher and "$probe.knownReadBytes-ne8" in launcher
    runner = temp / f"batch-{platform}-{revision}{suffix}"
    runner.write_text(launcher.replace(previous_hash, sha(bundle)), encoding="utf-8", newline="\n")
    proof[platform] = dict(bundle=sha(bundle), runner=sha(runner), bytes=bundle.stat().st_size, inputs=len(files))

(temp / f"cache-input-{revision}").mkdir()
harness = root / "tests/FileCat.NativeAcceptance/DiskSpaceHarness"
sources = {"harnessSource": sha(harness / ("DuplicateReview.cs" if duplicate else "GrowthRecipe.cs"))}
if duplicate:
    sources["programSource"] = sha(harness / "Program.cs")
proof_name = "duplicate-estimate" if duplicate else "growth"
(temp / f"{proof_name}-native-proof-{revision}.json").write_text(json.dumps({**sources, "platforms": proof}, indent=2) + "\n", encoding="utf-8")
print(json.dumps({name: outcome["bytes"] for name, outcome in proof.items()}))
