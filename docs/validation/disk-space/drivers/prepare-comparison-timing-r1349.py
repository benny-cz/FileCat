"""Re-run the four Windows catalog comparison cases on the final frozen branch build."""
import hashlib
import json
from pathlib import Path
import shutil
import tarfile

root = Path.cwd() / ".tmp"
original = root / "focused-delta-windows-r1345"
stage = root / "focused-delta-windows-r1349"
stage.mkdir()
shutil.copytree(original / "patch", stage / "patch")
plan = json.loads((original / "plan.json").read_text(encoding="utf-8"))
plan["revision"] = "r1349"
plan["methods"] = ["*NativeIndexedComparisonCatalogRouteUsesEntireRevisionsAndPagesBothRecordedSides*"]
(stage / "plan.json").write_text(json.dumps(plan, indent=2), encoding="utf-8")

def sha(path):
    with path.open("rb") as content:
        return hashlib.file_digest(content, "sha256").hexdigest()

archive = root / "dsa-batch-windows-r1349.tgz"
assert not archive.exists()
with tarfile.open(archive, "w:gz") as output:
    for path in stage.rglob("*"):
        if path.is_file():
            output.add(path, arcname=path.relative_to(stage).as_posix(), recursive=False)

runner = (root / "batch-windows-r1345.ps1").read_text(encoding="utf-8")
original_sha = sha(root / "dsa-batch-windows-r1345.tgz")
assert original_sha in runner
previous_guard = "[int]$a.total-ne10-or[int]$a.passed-ne10"
assert previous_guard in runner
runner = runner.replace(original_sha, sha(archive)).replace("r1345", "r1349").replace(previous_guard, "[int]$a.total-ne4-or[int]$a.passed-ne4")
(root / "batch-windows-r1349.ps1").write_text(runner, encoding="utf-8", newline="\n")
(root / "cache-input-r1349").mkdir()
