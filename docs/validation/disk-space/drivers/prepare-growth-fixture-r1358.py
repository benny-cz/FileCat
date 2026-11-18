"""Retry only the corrected growth fixture on cached r1357 application binaries."""
import hashlib
import json
from pathlib import Path
import shutil
import tarfile

root = Path.cwd()
temp = root / ".tmp"
binary = root / "tests/FileCat.Rendering.Tests/bin/Debug/net10.0"
test = "NativeIndexedGrowthRecipeShowsOnlyKnownPositiveDifferencesInTheMappedScope"
log = (temp / "growth-fixture-build-r1358.txt").read_text(encoding="utf-8-sig")
assert all(marker in log for marker in ("Build succeeded.", "0 Error(s)", "0 Warning(s)"))

def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

proof = {}
for platform in ("windows", "mac", "linux"):
    prior = temp / f"focused-delta-{platform}-r1357"
    stage = temp / f"focused-delta-{platform}-r1358"
    plan = json.loads((prior / "plan.json").read_text(encoding="utf-8"))
    original = plan["baseline"]
    plan["revision"] = "r1358"
    if platform == "windows":
        plan["baseline"] = r"C:\FileCatDsa20261104R2\batch-r1357\payload"
    else:
        plan["baseline"] = original.replace("r1334/payload/render", "r1357/payload")
    plan["methods"] = [f"*{test}*"]
    changes = set()
    for item in plan["files"]:
        name = item["path"]
        item["baselineSha256"] = item["sha256"]
        item["sha256"] = sha(binary / name)
        item["patch"] = item["baselineSha256"] != item["sha256"]
        if item["patch"]:
            changes.add(name)
    assert changes == {"FileCat.Rendering.Tests.dll", "FileCat.Rendering.Tests.pdb"}, changes
    (stage / "patch").mkdir(parents=True)
    for name in changes:
        shutil.copyfile(binary / name, stage / "patch" / name)
    (stage / "plan.json").write_text(json.dumps(plan, indent=2), encoding="utf-8")
    if platform != "windows":
        worker = (prior / "run.py").read_text(encoding="utf-8").replace("[12,12,0,0,0]", "[1,1,0,0,0]")
        compile(worker, "run.py", "exec")
        (stage / "run.py").write_text(worker, encoding="utf-8")
    bundle = temp / f"dsa-batch-{platform}-r1358.tgz"
    assert not bundle.exists()
    with tarfile.open(bundle, "w:gz") as archive:
        for path in stage.rglob("*"):
            if path.is_file():
                archive.add(path, arcname=path.relative_to(stage).as_posix(), recursive=False)
    suffix = ".ps1" if platform == "windows" else ".sh"
    launcher = (temp / f"batch-{platform}-r1357{suffix}").read_text(encoding="utf-8")
    launcher = launcher.replace(sha(temp / f"dsa-batch-{platform}-r1357.tgz"), sha(bundle))
    if platform == "windows":
        launcher = launcher.replace(original, plan["baseline"]).replace("[int]$a.total-ne12-or[int]$a.passed-ne12", "[int]$a.total-ne1-or[int]$a.passed-ne1")
    path = temp / f"batch-{platform}-r1358{suffix}"
    path.write_text(launcher, encoding="utf-8", newline="\n")
    proof[platform] = {"archive": sha(bundle), "bytes": bundle.stat().st_size, "runner": sha(path)}

(temp / "cache-input-r1358").mkdir()
fixture = root / "tests/FileCat.Rendering.Tests/AnalysisRenderingTests.IndexedComparisonUi.cs"
(temp / "growth-fixture-proof-r1358.json").write_text(json.dumps({"fixture": sha(fixture), "platforms": proof}, indent=2) + "\n", encoding="utf-8")
print(json.dumps({name: record["bytes"] for name, record in proof.items()}))
