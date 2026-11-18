"""Record the exact NTFS reader, original failures and successful native controls."""
import hashlib
import json
from pathlib import Path
import tarfile
import xml.etree.ElementTree as ET

root = Path.cwd()
temp = root / ".tmp"
validation = root / "docs/validation/disk-space"
manifest = validation / "source-manifest-r1375-ntfs-primary-content.json"
receipt = validation / "exact-receipts-r1375-ntfs-primary-content.tgz"
assert not manifest.exists() and not receipt.exists()


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


revisions = {
    "r1372": {"terminal": 1, "native": (11, 8, 3, 0, 0)},
    "r1373": {"terminal": 0, "native": (11, 11, 0, 0, 0), "native-epoch": (6, 6, 0, 0, 0)},
    "r1374": {"terminal": 0, "native": (3, 3, 0, 0, 0)},
}
proof = {}
files = {"primary-content-bootstrap-build-failure-r1372.txt", "primary-content-fixture-build-failure-r1372.txt",
         "primary-content-usn-build-failure-r1373.txt", "primary-content-platform-build-r1373.txt",
         "batch-r1374-windows/plan.json"}
case_names = {}
for revision, expected in revisions.items():
    source_input = f"source-{revision}-primary-content.tgz"
    source_proof = f"primary-content-proof-{revision}.json"
    build = f"primary-content-build-{revision}.txt"
    runner = f"batch-windows-{revision}.ps1"
    results = f"windows-batch-{revision}-results.tgz"
    post = f"windows-batch-{revision}-post.json"
    files.update((source_input, source_proof, build, runner, results, post))
    current = proof[revision] = read(temp / source_proof)
    assert sha(temp / source_input) == current["sourceArchive"]
    assert sha(temp / runner) == current["runner"]
    assert sha(temp / f"dsa-batch-windows-{revision}.tgz") == current["bundle"]
    assert not read(temp / post)["filecatOrDotnet"]
    assert "FileCat.PrivilegedHost.exe" not in current["inputs"]
    with tarfile.open(temp / source_input) as archive:
        assert set(archive.getnames()) == set(current["sources"])
        for name, digest in current["sources"].items():
            assert hashlib.sha256(archive.extractfile(name).read()).hexdigest() == digest
    with tarfile.open(temp / results) as archive:
        task = json.loads(archive.extractfile("task-outcome.json").read().decode("utf-8-sig"))
        assert task["runLevel"] == "Limited" and task["terminalExit"] == expected["terminal"]
        installed = json.loads(archive.extractfile("cache-proof.json").read().decode("utf-8-sig"))
        assert set(installed) == {"FileCat.Core.dll", "FileCat.Platform.Windows.dll", "FileCat.Platform.Windows.Tests.dll"}
        assert all(current["inputs"][name] == digest for name, digest in installed.items())
        for phase, counts in expected.items():
            if phase == "terminal":
                continue
            run = ET.fromstring(archive.extractfile(f"{phase}.xml").read()).find("assembly")
            actual = tuple(int(run.get(key)) for key in ("total", "passed", "failed", "errors", "skipped"))
            assert actual == counts
            case_names[f"{revision}/{phase}"] = [test.get("name") for test in run.iter("test")]

first = proof["r1373"]
final = proof["r1374"]
assert final["baselineRevision"] == "r1373"
assert {name for name in final["inputs"] if first["inputs"][name] != final["inputs"][name]} == {
    "FileCat.Platform.Windows.Tests.dll", "FileCat.Platform.Windows.Tests.pdb"}
assert {name for name in final["sources"] if first["sources"][name] != final["sources"][name]} == {
    "tests/FileCat.Platform.Windows.Tests/AnalysisWindowsTests.PrimaryContent.cs"}
assert all(sha(root / name) == digest for name, digest in final["sources"].items())
hashes = {name: sha(temp / name) for name in sorted(files)}
with tarfile.open(receipt, "w:gz") as archive:
    for name in hashes:
        archive.add(temp / name, arcname=name, recursive=False)
manifest.write_text(json.dumps({"sourceBase": final["base"], "sources": final["sources"], "inputs": hashes,
    "archiveSha256": sha(receipt), "nativeWindowsCases": case_names, "windowsOnly": True,
    "applicationContentReadsEnabled": False, "publicEntryEnabled": False, "fullGoalComplete": False,
    "finalIndependentReviewPerformed": False}, indent=2) + "\n", encoding="utf-8")
print(f"Retained {len(hashes)} exact Windows reader inputs and receipts.")
