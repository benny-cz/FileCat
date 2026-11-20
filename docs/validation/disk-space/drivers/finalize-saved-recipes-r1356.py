"""Verify saved-recipe checks and retain release evidence without desktop pixels."""
import hashlib
import io
import json
from pathlib import Path
import tarfile
import xml.etree.ElementTree as ET


worktree = Path.cwd()
temp = worktree / ".tmp"
docs = worktree / "docs/validation/disk-space"
manifest = docs / "source-manifest-r1356-saved-recipes.json"
source_output = docs / "exact-source-deltas-r1356-saved-recipes.tgz"
receipts = docs / "exact-receipts-r1356-saved-recipes.tgz"
assert all(not path.exists() for path in (manifest, source_output, receipts))


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def archive_json(archive, name):
    return json.loads(archive.extractfile(name).read().decode("utf-8-sig"))


focused = read(temp / "saved-recipe-proof-r1354.json")
native = read(temp / "native-branch-proof-r1355.json")
source_delta = temp / "source-r1354-saved-recipes-delta.tgz"
assert sha(source_delta) == focused["source"]
assert sha(temp / "source-r1355-native-branch.tgz") == native["sourceSha256"]
for name, expected in focused["changes"].items():
    assert sha(worktree / name) == expected, name
for item in native["sources"]:
    assert sha(worktree / item["path"]) == item["sha256"], item["path"]

files = {
    "saved-recipe-core-final-build-r1354.txt", "saved-recipe-render-build-r1354.txt", "saved-recipe-native-build-r1355.txt",
    "saved-recipe-proof-r1354.json", "native-branch-proof-r1355.json", "windows-native-r1355-task-outcome.json",
    "windows-batch-r1355-results.tgz", "windows-batch-r1355-post.json",
}
with tarfile.open(temp / "windows-batch-r1355-results.tgz") as refused:
    assert refused.extractfile("state.txt").read().decode("utf-8-sig").strip() == "BUSY phase=verify"
    assert not any(member.name in ("owned.pid", "task-outcome.json") for member in refused.getmembers())
task = read(temp / "windows-native-r1355-task-outcome.json")
assert task["terminalExit"] == 0 and task["runLevel"] == "Limited"
platform_checks = {}
private_desktop = None

for platform in ("windows", "mac", "linux"):
    tag = "ubuntu" if platform == "linux" else platform
    extension = ".ps1" if platform == "windows" else ".sh"
    files.update({f"{tag}-batch-r1354-results.tgz", f"{tag}-batch-r1354-post.json", f"batch-{platform}-r1354{extension}",
                  f"{tag}-native-r1355-post.json", f"batch-{platform}-r1355{extension}"})
    assert not read(temp / f"{tag}-batch-r1354-post.json")["filecatOrDotnet"]
    assert not read(temp / f"{tag}-native-r1355-post.json")["filecatOrDotnet"]
    with tarfile.open(temp / f"{tag}-batch-r1354-results.tgz") as archive:
        for phase, expected in (("core", 20), ("render", 5)):
            assembly = ET.fromstring(archive.extractfile(phase + ".xml").read()).find("assembly")
            counts = tuple(int(assembly.get(key)) for key in ("total", "passed", "failed", "errors", "skipped"))
            assert counts == (expected, expected, 0, 0, 0)

    name = f"{tag}-native-r1355-results.tgz"
    record = temp / name
    with tarfile.open(record) as archive:
        data = archive_json(archive, "result.json")
        captures = data.get("compositorCaptures", [])
        if platform == "mac":
            capture = captures[0]
            private_name = "compositor-branch-saved-recipe.png"
            assert len(captures) == 1 and capture["frame"] == "branch-saved-recipe.png"
            assert capture["ownedWindow"]["kCGWindowOwnerPID"] == data["pid"]
            private_image = archive.extractfile(private_name).read()
            assert hashlib.sha256(private_image).hexdigest() == capture["sha256"]
            private_desktop = {"rawArchiveSha256": sha(record), "capture": capture}
            stripped = io.BytesIO()
            with tarfile.open(fileobj=stripped, mode="w:gz") as safe:
                for member in archive.getmembers():
                    assert member.isfile() and member.name == Path(member.name).name
                    if member.name != private_name:
                        assert not member.name.startswith("compositor-")
                        safe.addfile(member, archive.extractfile(member))
        else:
            assert not captures and not any(member.name.startswith("compositor-") for member in archive.getmembers())
            files.add(name)

    assert data["passed"] and data["exitCode"] == 0 and not data["postProcesses"] and not data.get("forcedOwnedTermination")
    assert data["closed"]["state"] == "closed" and data["closed"]["sourceUnchanged"]
    native_product = next(row for row in native["platforms"] if row["platform"] == platform)
    native_binary = {row["path"]: row["sha256"] for row in native_product["files"]}
    for binary in ("FileCat.dll", "FileCat.Core.dll"):
        assert native_binary[binary] == focused["platforms"][platform]["binaries"]["render/" + binary]
    assert data["finished"]["appSha256"] == native_binary["FileCat.dll"]
    journey = data["finished"]["branchRefreshProbe"]
    assert journey["savedRecipePassed"] and journey["recipePreviewPassed"] and journey["originalMarksAndBasketKept"]
    assert journey["observed"] == ["after-scan.txt", "photo.png"] and journey["newFilesPreserved"]
    platform_checks[tag] = {"core": 20, "renderedNativeFilesystem": 5, "nativeJourney": journey}

assert private_desktop is not None
retained_hashes = {name: sha(temp / name) for name in sorted(files)}
safe_mac = stripped.getvalue()
safe_name = "mac-native-r1355-results-without-desktop.tgz"
retained_hashes[safe_name] = hashlib.sha256(safe_mac).hexdigest()
with tarfile.open(receipts, "w:gz") as archive:
    for name in sorted(files):
        archive.add(temp / name, arcname=name, recursive=False)
    item = tarfile.TarInfo(safe_name)
    item.size = len(safe_mac)
    archive.addfile(item, io.BytesIO(safe_mac))

test_driver_paths = ("tests/FileCat.NativeAcceptance/DiskSpaceHarness/BranchRefresh.cs", "tests/FileCat.NativeAcceptance/DiskSpaceHarness/Program.cs")
with tarfile.open(source_output, "w:gz") as archive:
    for path in sorted(set(focused["changes"]).union(test_driver_paths)):
        archive.add(worktree / path, arcname=path, recursive=False)

manifest.write_text(json.dumps({"sourceBase": focused["head"], "focusedSourceSha256": focused["source"],
    "nativeSourceSha256": native["sourceSha256"], "checks": platform_checks, "receipts": retained_hashes,
    "archives": {path.name: sha(path) for path in (source_output, receipts)}, "localMacDesktopCapture": private_desktop,
    "physicalInputQualified": False, "screenReaderQualified": False, "fullGoalComplete": False,
    "publicCommandEnabled": False, "finalIndependentReviewPerformed": False}, indent=2) + "\n", encoding="utf-8")
print(f"Retained {len(retained_hashes)} verified inputs and receipts.")
