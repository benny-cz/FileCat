"""Stage recipe, growth, or duplicate review tests over the verified rendering build."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tarfile

parser = argparse.ArgumentParser()
parser.add_argument("revision")
scope = parser.add_mutually_exclusive_group()
scope.add_argument("--growth", action="store_true")
scope.add_argument("--duplicates", action="store_true")
scope.add_argument("--duplicate-content", action="store_true")
options = parser.parse_args()
revision = options.revision
methods = ["*NativeRecipeDialogPreviewsOnlyCapturedFilesWithContextAndLeavesSourceAndMarksAlone*"]
expected = 2
if options.growth:
    methods = ["*ComparisonPaneFiltersGrowthInspectsBothSidesAndKeepsItsCloseDefault*",
               "*NativeIndexedGrowthRecipeShowsOnlyKnownPositiveDifferencesInTheMappedScope*",
               "*NativeIndexedComparisonIncompatiblePoliciesRequireExplicitLimitedReview*",
               "*NativeIndexedComparisonQueryViewAppliesSidesCohortsOrdersAndPreservesAnInvalidRequest*",
               "*NativeIndexedComparisonCatalogRouteUsesEntireRevisionsAndPagesBothRecordedSides*"]
    expected = 12
    log = (Path.cwd() / ".tmp/growth-insight-render-build-r1357.txt").read_text(encoding="utf-8-sig")
    assert all(check in log for check in ("Build succeeded.", "0 Warning(s)", "0 Error(s)"))
if options.duplicates or options.duplicate_content:
    methods = ["*DuplicateReview*",
               "*NativeRecipeDialogPreviewsOnlyCapturedFilesWithContextAndLeavesSourceAndMarksAlone*",
               "*NativeSavedRecipeStorePreservesCapturedScopeAcrossPrivateOpens*",
               "*NativeSavedRecipeDialogKeepsExplicitRootsWhenTheQueryMentionsAnotherLocation*",
               "*ComparisonPaneFiltersGrowthInspectsBothSidesAndKeepsItsCloseDefault*",
               "*NativeIndexedGrowthRecipeShowsOnlyKnownPositiveDifferencesInTheMappedScope*",
               "*IndexedSelectionBasketPagesItsCapturedSnapshotAndAllowsRemovalWithoutChangingMarksOrSource*"]
    expected = 12
    name = f"duplicate-estimate-render-build-{revision}-serial"
    if options.duplicate_content:
        methods.extend(("*NativeDuplicateReadConsent*", "*DuplicateReadPlanBindsConfirmedMembersCostAndOneRun*"))
        expected = 16
        name = f"duplicate-read-render-build-{revision}"
    log = (Path.cwd() / f".tmp/{name}.txt").read_text(encoding="utf-8-sig")
    assert all(check in log for check in ("Build succeeded.", "0 Warning(s)", "0 Error(s)"))
root = Path.cwd()
temp = root / ".tmp"
binary = root / "tests/FileCat.Rendering.Tests/bin/Debug/net10.0"
assemblies = (
    "FileCat", "FileCat.Archives", "FileCat.Archives.Rar15", "FileCat.Core", "FileCat.CoreCrashHarness", "FileCat.Platform.Windows",
    "FileCat.Recovery", "FileCat.Remote", "FileCat.Rendering.Tests", "FileCat.SharpCompress", "FileCat.ShellHost",
)
allow = {f"{name}.{extension}" for name in assemblies for extension in ("dll", "pdb")}
allow.update(f"{name}.exe" for name in ("FileCat", "FileCat.CoreCrashHarness", "FileCat.Rendering.Tests", "FileCat.ShellHost"))

def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

proof = {}
for platform in ("windows", "mac", "linux"):
    baseline = temp / f"batch-r1334-{platform}/render"
    template = temp / f"focused-delta-{platform}-r1345"
    plan = json.loads((template / "plan.json").read_text(encoding="utf-8"))
    plan["revision"] = revision
    plan["methods"] = methods
    changes = set()
    for entry in plan["files"]:
        name = entry["path"]
        assert sha(baseline / name) == entry["baselineSha256"], name
        entry["sha256"] = sha(binary / name)
        entry["patch"] = entry["sha256"] != entry["baselineSha256"]
        if entry["patch"]:
            changes.add(name)
    assert changes <= allow, f"Review changed rendering inputs: {sorted(changes - allow)}"
    assert {"FileCat.Core.dll", "FileCat.dll", "FileCat.Rendering.Tests.dll"} <= changes
    stage = temp / f"focused-delta-{platform}-{revision}"
    (stage / "patch").mkdir(parents=True)
    for name in sorted(changes):
        shutil.copyfile(binary / name, stage / "patch" / name)
    (stage / "plan.json").write_text(json.dumps(plan, indent=2), encoding="utf-8")
    if platform != "windows":
        worker = (template / "run.py").read_text(encoding="utf-8").replace(
            "(plan['assembly'],plan['assembly'][:-4]+'.pdb','FileCat.dll','FileCat.pdb')", repr(tuple(sorted(changes)))
        ).replace("[10,10,0,0,0]", f"[{expected},{expected},0,0,0]")
        compile(worker, "run.py", "exec")
        (stage / "run.py").write_text(worker, encoding="utf-8", newline="\n")
    archive = temp / f"dsa-batch-{platform}-{revision}.tgz"
    assert not archive.exists()
    with tarfile.open(archive, "w:gz", compresslevel=4) as output:
        for path in stage.rglob("*"):
            if path.is_file():
                output.add(path, arcname=path.relative_to(stage).as_posix(), recursive=False)
    suffix = ".ps1" if platform == "windows" else ".sh"
    runner = (temp / f"batch-{platform}-r1345{suffix}").read_text(encoding="utf-8").replace(
        sha(temp / f"dsa-batch-{platform}-r1345.tgz"), sha(archive)
    ).replace("r1345", revision)
    if platform == "windows":
        quoted = ",".join(f"'{name}'" for name in sorted(changes))
        runner = runner.replace("$plan.assembly,($plan.assembly-replace'[.]dll$','.pdb'),'FileCat.dll','FileCat.pdb'", f"@({quoted})").replace(
            "[int]$a.total-ne10-or[int]$a.passed-ne10", f"[int]$a.total-ne{expected}-or[int]$a.passed-ne{expected}"
        )
    runner_path = temp / f"batch-{platform}-{revision}{suffix}"
    runner_path.write_text(runner, encoding="utf-8", newline="\n")
    proof[platform] = {"archive": sha(archive), "runner": sha(runner_path), "size": archive.stat().st_size, "patch": sorted(changes)}
(temp / f"cache-input-{revision}").mkdir()
(temp / f"recipe-rendering-proof-{revision}.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
if options.growth or options.duplicates or options.duplicate_content:
    slice_name = "growth" if options.growth else "duplicate-content" if options.duplicate_content else "duplicate-estimate"
    source_changes = set(subprocess.check_output(["git", "diff", "--name-only", "HEAD", "--", "src", "tests"], text=True).splitlines())
    source_changes.update(subprocess.check_output(["git", "ls-files", "--others", "--exclude-standard", "src", "tests"], text=True).splitlines())
    source_archive = temp / f"source-{revision}-{slice_name}-delta.tgz"
    assert not source_archive.exists()
    with tarfile.open(source_archive, "w:gz") as archive:
        for name in sorted(source_changes):
            archive.add(root / name, arcname=name, recursive=False)
    origin = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    (temp / f"{slice_name}-source-proof-{revision}.json").write_text(json.dumps({"head": origin, "archive": sha(source_archive),
        "changes": {name: sha(root / name) for name in sorted(source_changes)}}, indent=2) + "\n", encoding="utf-8")
print(json.dumps(proof))
