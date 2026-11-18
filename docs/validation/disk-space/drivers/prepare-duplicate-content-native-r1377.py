"""Reuse the verified native host and rendering product; transfer just the native test driver."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import sys
import tarfile

root = Path.cwd()
temp = root / ".tmp"
binary = root / "tests/FileCat.NativeAcceptance/DiskSpaceHarness/bin/Debug/net10.0"
mac_capture_retry = sys.argv[1:] == ["--ntfs-streams-mac-capture"]
forks = sys.argv[1:] == ["--apfs-forks"]
cleanup = sys.argv[1:2] == ["--cleanup-review"]
if cleanup:
    assert len(sys.argv) == 4 and all(re.fullmatch(r"r[0-9]+", arg) for arg in sys.argv[2:]), "Specify renderer and native revisions."
streams = sys.argv[1:] == ["--ntfs-streams"] or mac_capture_retry or forks
staging = sys.argv[1:] == ["--recipe-staging"] or streams or cleanup
folders = sys.argv[1:] == ["--folders"] or staging
insights = sys.argv[1:] == ["--insights"] or folders
assert not sys.argv[1:] or insights
native_baseline = "r1377" if insights else "r1370"
render_revision = sys.argv[2] if cleanup else "r1398" if forks else "r1394" if streams else "r1389" if staging else "r1382" if folders else "r1379" if insights else "r1376"
revision = sys.argv[3] if cleanup else "r1399" if forks else "r1397" if mac_capture_retry else "r1395" if streams else "r1390" if staging else "r1384" if folders else "r1380" if insights else "r1377"
label = "cleanup-review" if cleanup else "apfs-forks" if forks else "ntfs-streams" if streams else "recipe-staging" if staging else "recorded-insights" if insights else "duplicate-content"
template = "r1370"
drivers = {"DiskSpaceHarness.dll", "DiskSpaceHarness.pdb"}
log = (temp / f"{label}-native-build-{revision}.txt").read_text(encoding="utf-8-sig")
assert all(marker in log for marker in ("Build succeeded.", "0 Warning(s)", "0 Error(s)"))


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


prior = json.loads((temp / f"duplicate-estimate-native-proof-{template}.json").read_text())
proof = {}
for platform in (("mac",) if mac_capture_retry else ("windows", "mac", "linux")):
    original = temp / f"batch-{native_baseline}-{platform}"
    plan = json.loads((original / "plan.json").read_text())
    prior_path = plan["baseline"]
    plan["baseline"] = prior_path.replace("r1370" if insights else "r1368", native_baseline)
    assert plan["baseline"] != prior_path and plan["baseline"].endswith("app")
    plan["render"] = plan["baseline"][:-3].replace(native_baseline, render_revision) + "payload"
    rendering = json.loads((temp / f"focused-delta-{platform}-{render_revision}/plan.json").read_text())
    rendered = {row["path"]: row["sha256"] for row in rendering["files"]}
    stage = temp / f"batch-{revision}-{platform}"
    (stage / "patch").mkdir(parents=True)
    for row in plan["files"]:
        name = row["path"]
        digest = row["sha256"]
        row["patch"] = name in drivers
        row["render"] = name.endswith((".dll", ".pdb")) and name in rendered and rendered[name] != digest
        if row["render"]:
            digest = rendered[name]
            assert sha(binary / name) == digest, name
        row["baselineSha256"] = digest
        row["sha256"] = sha(binary / name) if row["patch"] else digest
        if row["patch"]:
            shutil.copyfile(binary / name, stage / "patch" / name)
    paths = {row["path"]: row for row in plan["files"]}
    for name in ("FileCat.dll", "FileCat.Core.dll", "FileCat.Platform.Windows.dll"):
        assert paths[name]["render"] and paths[name]["sha256"] == rendered[name]
    (stage / "plan.json").write_text(json.dumps(plan, indent=2), encoding="utf-8")
    if platform != "windows":
        worker = (temp / f"batch-{template}-{platform}/run.py").read_text(encoding="utf-8")
        fixed_root = f"pathlib.Path.home() / 'FileCatValidation/dsa-batch-20261107-{render_revision}/payload'"
        worker = worker.replace("    names = set()", f"    render = {fixed_root}\n    assert render == pathlib.Path(plan['render'])\n    names = set()")
        worker = worker.replace("        original = baseline / str(name)", "        original = (render if entry['render'] else baseline) / str(name)")
        worker = worker.replace("else baseline / entry['path']", "else (render if entry['render'] else baseline) / entry['path']")
        worker = worker.replace("'baseline': str(baseline),", "'baseline': str(baseline), 'render': str(render),")
        worker = worker.replace("and probe['retainedHiddenMarksAndBasket'] and", "and probe['retainedHiddenMarksAndBasket'] and not probe['contentAvailable'] and probe['contentGroups']==0 and")
        if insights:
            frame = "cleanup-review-preserving.png" if cleanup else "apfs-resource-fork-allocation.png" if forks else "ntfs-stream-allocation.png" if streams else "recipe-staging-included.png" if staging else "recorded-insights-largest-folders.png" if folders else "recorded-insights-small-files.png"
            worker = worker.replace("frame == 'duplicate-review-estimate.png'", f"frame == '{frame}'")
            worker = worker.replace("run.glob('duplicate-review-*.png')", "list(run.glob('duplicate-review-*.png')) + list(run.glob('recorded-insights-*.png'))")
            worker = worker.replace("probe['contentGroups']==0 and", "probe['contentGroups']==0 and probe['recordedInsightsPassed'] and")
        if folders:
            worker = worker.replace("probe['recordedInsightsPassed'] and", "probe['recordedInsightsPassed'] and probe['largestFoldersPassed'] and")
        if staging:
            worker = worker.replace("probe['largestFoldersPassed'] and", "probe['largestFoldersPassed'] and probe['recipeStagingPassed'] and")
            worker = worker.replace("list(run.glob('recorded-insights-*.png'))", "list(run.glob('recorded-insights-*.png')) + list(run.glob('recipe-staging-*.png'))")
        if streams:
            worker = worker.replace("'--duplicate-review-probe'", "'--detailed-stream-probe'")
            worker = worker.replace("probe['recipeStagingPassed'] and", "probe['recipeStagingPassed'] and probe['detailedStreamProbe'] and not probe['namedAllocationAvailable'] and probe['namedStreamBytesPreserved'] and")
            worker = worker.replace("list(run.glob('recipe-staging-*.png'))", "list(run.glob('recipe-staging-*.png')) + list(run.glob('ntfs-stream-allocation.png'))")
        if forks:
            worker = worker.replace("'--detailed-stream-probe'", "'--detailed-fork-probe'")
            worker = worker.replace("probe['namedStreamBytesPreserved'] and", "probe['namedStreamBytesPreserved'] and probe['resourceForkBytesPreserved'] and probe['resourceAllocationAvailable'] == (sys.platform == 'darwin') and")
            worker = worker.replace("list(run.glob('ntfs-stream-allocation.png'))", "list(run.glob('apfs-resource-fork-allocation.png'))")
        if cleanup:
            worker = worker.replace("'--duplicate-review-probe'", "'--cleanup-review-probe'")
            worker = worker.replace("probe['recipeStagingPassed'] and", "probe['recipeStagingPassed'] and probe['cleanupReviewPassed'] and")
            worker = worker.replace("list(run.glob('recipe-staging-*.png'))", "list(run.glob('recipe-staging-*.png')) + list(run.glob('cleanup-review-*.png'))")
        if mac_capture_retry or forks or cleanup:
            worker = worker.replace(f"assert frame == '{frame}'", f"assert frame == '{frame}', " + "f'Unexpected capture request {frame!r}'")
            worker = worker.replace("    assert len(owned) == 1", "    assert len(owned) == 1, f'Expected one visible owned window, got {len(owned)}'")
            worker = worker.replace("    (run / 'capture.response').write_text(frame)",
                "    response = run / 'capture.response.pending'\n    response.write_text(frame)\n    response.replace(run / 'capture.response')")
        assert "not probe['contentAvailable']" in worker and "(render if entry['render'] else baseline)" in worker
        compile(worker, "run.py", "exec")
        (stage / "run.py").write_text(worker, encoding="utf-8", newline="\n")
    archive = temp / f"dsa-batch-{platform}-{revision}.tgz"
    assert not archive.exists()
    with tarfile.open(archive, "w:gz") as bundle:
        for path in stage.rglob("*"):
            if path.is_file():
                bundle.add(path, arcname=path.relative_to(stage).as_posix(), recursive=False)
    suffix = ".ps1" if platform == "windows" else ".sh"
    original_runner = temp / f"batch-{platform}-{template}{suffix}"
    portable = original_runner.read_text(encoding="utf-8")
    old = prior["platforms"][platform]
    assert sha(original_runner) == old["runner"] and old["bundle"] in portable
    if platform == "windows":
        portable = portable.replace(" $names=@{}", f" if($plan.render-cne'{plan['render']}'){{throw 'Unexpected rendering cache root'}}\n"
            " if((Get-Item -LiteralPath $plan.render).Attributes-band[IO.FileAttributes]::ReparsePoint){throw 'Rendering cache is redirected'}\n $names=@{}")
        portable = portable.replace("  $original=Join-Path $plan.baseline $item.path",
            "  $inputRoot=if($item.render){$plan.render}else{$plan.baseline}\n  $original=Join-Path $inputRoot $item.path")
        portable = portable.replace("$parent.FullName-ne$plan.baseline", "$parent.FullName-ne$inputRoot")
        portable = portable.replace("else{Join-Path $plan.baseline $item.path}", "elseif($item.render){Join-Path $plan.render $item.path}else{Join-Path $plan.baseline $item.path}")
        portable = portable.replace("@{baseline=$plan.baseline;", "@{baseline=$plan.baseline;render=$plan.render;")
        portable = portable.replace("-or!$probe.retainedHiddenMarksAndBasket)", "-or!$probe.retainedHiddenMarksAndBasket-or!$probe.contentAvailable-or$probe.contentGroups-ne1)")
        if insights:
            portable = portable.replace("$probe.contentGroups-ne1)", "$probe.contentGroups-ne1-or!$probe.recordedInsightsPassed)")
            portable = portable.replace("-Filter 'duplicate-review-*.png' -File", "-File | Where-Object {$_.Name -like 'duplicate-review-*.png' -or $_.Name -like 'recorded-insights-*.png'}")
        if folders:
            portable = portable.replace("-or!$probe.recordedInsightsPassed)", "-or!$probe.recordedInsightsPassed-or!$probe.largestFoldersPassed)")
        if staging:
            portable = portable.replace("-or!$probe.largestFoldersPassed)", "-or!$probe.largestFoldersPassed-or!$probe.recipeStagingPassed)")
            portable = portable.replace("$_.Name -like 'recorded-insights-*.png'", "$_.Name -like 'recorded-insights-*.png' -or $_.Name -like 'recipe-staging-*.png'")
        if streams:
            portable = portable.replace("'--duplicate-review-probe'", "'--detailed-stream-probe'")
            portable = portable.replace("-or!$probe.recipeStagingPassed)", "-or!$probe.recipeStagingPassed-or!$probe.detailedStreamProbe-or!$probe.namedAllocationAvailable-or!$probe.namedStreamBytesPreserved)")
            portable = portable.replace("$_.Name -like 'recipe-staging-*.png'", "$_.Name -like 'recipe-staging-*.png' -or $_.Name -eq 'ntfs-stream-allocation.png'")
        if forks:
            portable = portable.replace("'--detailed-stream-probe'", "'--detailed-fork-probe'")
            portable = portable.replace("-or!$probe.namedStreamBytesPreserved)", "-or!$probe.namedStreamBytesPreserved-or$probe.resourceAllocationAvailable-or!$probe.resourceForkBytesPreserved)")
            portable = portable.replace("ntfs-stream-allocation.png", "apfs-resource-fork-allocation.png")
        if cleanup:
            portable = portable.replace("'--duplicate-review-probe'", "'--cleanup-review-probe'")
            portable = portable.replace("-or!$probe.recipeStagingPassed)", "-or!$probe.recipeStagingPassed-or!$probe.cleanupReviewPassed)")
            portable = portable.replace("$_.Name -like 'recipe-staging-*.png'", "$_.Name -like 'recipe-staging-*.png' -or $_.Name -like 'cleanup-review-*.png'")
        assert "$probe.contentGroups-ne1" in portable and "$parent.FullName-ne$inputRoot" in portable
    runner = temp / f"batch-{platform}-{revision}{suffix}"
    runner.write_text(portable.replace(old["bundle"], sha(archive)), encoding="utf-8", newline="\n")
    proof[platform] = {"bundle": sha(archive), "runner": sha(runner), "bytes": archive.stat().st_size,
                       "inputs": len(plan["files"]), "sharedRenderInputs": sum(row["render"] for row in plan["files"])}

(temp / f"cache-input-{revision}").mkdir()
harness = root / "tests/FileCat.NativeAcceptance/DiskSpaceHarness/DuplicateReview.cs"
native_sources = {str(harness.relative_to(root).as_posix()): sha(harness)}
if staging:
    program = harness.with_name("Program.cs")
    native_sources[program.relative_to(root).as_posix()] = sha(program)
if mac_capture_retry or cleanup:
    capture = harness.with_name("BranchRefresh.cs")
    native_sources[capture.relative_to(root).as_posix()] = sha(capture)
(temp / f"{label}-native-proof-{revision}.json").write_text(json.dumps(
    {"harnessSource": sha(harness), "sources": native_sources, "platforms": proof}, indent=2) + "\n", encoding="utf-8")
print(json.dumps({platform: values["bytes"] for platform, values in proof.items()}))
