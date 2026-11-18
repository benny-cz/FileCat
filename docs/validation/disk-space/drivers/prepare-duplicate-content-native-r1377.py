"""Reuse the verified native host and rendering product; transfer just the native test driver."""
import hashlib
import json
from pathlib import Path
import shutil
import sys
import tarfile

root = Path.cwd()
temp = root / ".tmp"
binary = root / "tests/FileCat.NativeAcceptance/DiskSpaceHarness/bin/Debug/net10.0"
insights = sys.argv[1:] == ["--insights"]
assert not sys.argv[1:] or insights
native_baseline = "r1377" if insights else "r1370"
render_revision = "r1379" if insights else "r1376"
revision = "r1380" if insights else "r1377"
label = "recorded-insights" if insights else "duplicate-content"
template = "r1370"
drivers = {"DiskSpaceHarness.dll", "DiskSpaceHarness.pdb"}
log = (temp / f"{label}-native-build-{revision}.txt").read_text(encoding="utf-8-sig")
assert all(marker in log for marker in ("Build succeeded.", "0 Warning(s)", "0 Error(s)"))


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


prior = json.loads((temp / f"duplicate-estimate-native-proof-{template}.json").read_text())
proof = {}
for platform in ("windows", "mac", "linux"):
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
            worker = worker.replace("frame == 'duplicate-review-estimate.png'", "frame == 'recorded-insights-small-files.png'")
            worker = worker.replace("run.glob('duplicate-review-*.png')", "list(run.glob('duplicate-review-*.png')) + list(run.glob('recorded-insights-*.png'))")
            worker = worker.replace("probe['contentGroups']==0 and", "probe['contentGroups']==0 and probe['recordedInsightsPassed'] and")
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
        assert "$probe.contentGroups-ne1" in portable and "$parent.FullName-ne$inputRoot" in portable
    runner = temp / f"batch-{platform}-{revision}{suffix}"
    runner.write_text(portable.replace(old["bundle"], sha(archive)), encoding="utf-8", newline="\n")
    proof[platform] = {"bundle": sha(archive), "runner": sha(runner), "bytes": archive.stat().st_size,
                       "inputs": len(plan["files"]), "sharedRenderInputs": sum(row["render"] for row in plan["files"])}

(temp / f"cache-input-{revision}").mkdir()
harness = root / "tests/FileCat.NativeAcceptance/DiskSpaceHarness/DuplicateReview.cs"
(temp / f"{label}-native-proof-{revision}.json").write_text(json.dumps(
    {"harnessSource": sha(harness), "platforms": proof}, indent=2) + "\n", encoding="utf-8")
print(json.dumps({platform: values["bytes"] for platform, values in proof.items()}))
