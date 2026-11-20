"""Archive the exact metadata-recipe source delta and completed three-platform checks."""
import hashlib
import json
from pathlib import Path
import subprocess
import tarfile
import xml.etree.ElementTree as ET


root = Path.cwd()
temp = root / ".tmp"
docs = root / "docs/validation/disk-space"
manifest = docs / "source-manifest-r1352-metadata-recipes.json"
source = docs / "exact-source-deltas-r1352-metadata-recipes.tgz"
receipts = docs / "exact-receipts-r1352-metadata-recipes.tgz"
assert not any(path.exists() for path in (manifest, source, receipts))


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def member_json(archive, name):
    return json.loads(archive.extractfile(name).read().decode("utf-8-sig"))


changed = {
    "src/FileCat.App/Views/AnalysisRecipeDialog.cs", "src/FileCat.App/Views/DiskSpaceWindow.Recipes.cs",
    "src/FileCat.App/Views/DiskSpaceWindow.cs", "src/FileCat.App/Views/DiskSpaceWindow.Indexed.cs",
    "src/FileCat.Core/Analysis/AnalysisMemoryRecipes.cs", "src/FileCat.Core/Analysis/AnalysisRecipePreview.cs",
    "src/FileCat.Core/Analysis/AnalysisRecipes.cs", "tests/FileCat.Core.Tests/AnalysisDatasetSummaryTests.Recipes.cs",
    "tests/FileCat.Core.Tests/AnalysisRecipeTests.cs", "tests/FileCat.Rendering.Tests/AnalysisRenderingTests.Recipes.cs",
    "tests/FileCat.NativeAcceptance/DiskSpaceHarness/BranchRefresh.cs", "tests/FileCat.NativeAcceptance/DiskSpaceHarness/Program.cs",
}
core = read(temp / "metadata-recipes-proof-r1348.json")
render = read(temp / "recipe-rendering-proof-r1350.json")
native = read(temp / "native-branch-proof-r1351.json")
full_source = temp / "source-r1351-native-branch.tgz"
assert sha(full_source) == native["sourceSha256"]
assert all(sha(root / name) == value for name, value in core["sources"].items())
assert all(sha(root / item["path"]) == item["sha256"] for item in native["sources"])
base = subprocess.check_output(["git", "rev-parse", "7b9a98d8"], text=True).strip()
modified = set(subprocess.check_output(["git", "diff", "--name-only", base, "--", "src", "tests"], text=True).splitlines())
modified.update(subprocess.check_output(["git", "ls-files", "--others", "--exclude-standard", "src", "tests"], text=True).splitlines())
assert modified == changed, sorted(modified ^ changed)

included = {
    "metadata-recipes-proof-r1348.json", "recipe-rendering-proof-r1350.json", "native-branch-proof-r1351.json",
    "metadata-recipes-final-build-r1348.txt", "metadata-recipes-render-build-r1350.txt", "metadata-recipes-native-build-r1351.txt",
}
results = {}
for platform in ("windows", "mac", "linux"):
    tag = "ubuntu" if platform == "linux" else platform
    suffix = ".ps1" if platform == "windows" else ".sh"
    for revision, phase, expected in (("r1348", "core", 11), ("r1350", "render", 2)):
        packet = f"{tag}-batch-{revision}-results.tgz"
        post = f"{tag}-batch-{revision}-post.json"
        runner = f"batch-{platform}-{revision}{suffix}"
        included.update((packet, post, runner))
        assert not read(temp / post)["filecatOrDotnet"]
        with tarfile.open(temp / packet) as archive:
            assembly = ET.fromstring(archive.extractfile(phase + ".xml").read()).find("assembly")
            counts = tuple(int(assembly.get(key)) for key in ("total", "passed", "failed", "errors", "skipped"))
            assert counts == (expected, expected, 0, 0, 0)
            if revision == "r1350":
                observed = member_json(archive, "cache-proof.json")
                payload = next(item["files"] for item in native["platforms"] if item["platform"] == platform)
                native_binaries = {item["path"]: item["sha256"] for item in payload}
                patched = {item["path"]: item["sha256"] for item in observed["files"] if item["patch"]}
                for name in ("FileCat.dll", "FileCat.Core.dll"):
                    assert patched[name] == native_binaries[name]
        expected_runner = core["platforms"][platform]["runner"] if revision == "r1348" else render[platform]["runner"]
        assert sha(temp / runner) == expected_runner

    packet = f"{tag}-native-r1351-results.tgz"
    post = f"{tag}-native-r1351-post.json"
    included.update((packet, post, f"batch-{platform}-r1351{suffix}"))
    assert not read(temp / post)["filecatOrDotnet"]
    with tarfile.open(temp / packet) as archive:
        result = member_json(archive, "result.json")
        assert all(Path(member.name).name.startswith("branch-") for member in archive.getmembers() if Path(member.name).suffix.lower() == ".png")
    assert result["passed"] and result["exitCode"] == 0 and not result["postProcesses"]
    assert not result.get("forcedOwnedTermination")
    assert result["closed"]["state"] == "closed" and result["closed"]["sourceUnchanged"]
    journey = result["finished"]["branchRefreshProbe"]
    assert journey["recipePreviewPassed"] and journey["originalMarksAndBasketKept"] and journey["newFilesPreserved"]
    assert "RecipeProject/obj/project.assets.json" in journey["recipeEvidence"]
    assert "RecipeProject/RecipeProject.csproj" in journey["recipeEvidence"] and "RecipeLookalike" not in journey["recipeEvidence"]
    assert journey["branchRoot"] == "Photos" and journey["observed"] == ["after-scan.txt", "photo.png"]
    results[tag] = {"coreTests": 11, "dialogTests": 2, "nativeRecipeAndBranch": journey}

task = "windows-native-r1351-task-outcome.json"
included.add(task)
assert read(temp / task)["terminalExit"] == 0 and read(temp / task)["runLevel"] == "Limited"
input_hashes = {name: sha(temp / name) for name in sorted(included)}
with tarfile.open(source, "w:gz") as output:
    for name in sorted(changed):
        output.add(root / name, arcname=name, recursive=False)
with tarfile.open(receipts, "w:gz") as output:
    for name in sorted(included):
        output.add(temp / name, arcname=name, recursive=False)
manifest.write_text(json.dumps({
    "sourceBase": base, "fullNativeSourceSha256": native["sourceSha256"],
    "sourceFiles": {name: sha(root / name) for name in sorted(changed)},
    "results": results, "inputs": input_hashes,
    "archives": {path.name: sha(path) for path in (source, receipts)},
    "publicCommandRegistered": False, "physicalInputQualified": False, "screenReaderQualified": False,
    "fullGoalComplete": False, "finalIndependentReviewPerformed": False,
}, indent=2) + "\n", encoding="utf-8")
print(f"Archived {len(changed)} source files and {len(included)} build and test receipts.")
