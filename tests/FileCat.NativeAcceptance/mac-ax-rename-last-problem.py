#!/usr/bin/env python3
"""Review the last blocking row of a 3,001-file native macOS bulk rename.

Usage: python3 mac-ax-rename-last-problem.py BUNDLE.app REPORT.json
Run only in the unlocked, isolated ~/FileCatValidation desktop session.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import signal
import subprocess
import sys
import time
from uuid import uuid4

shared = runpy.run_path(str(Path(__file__).with_name("mac-ax-rename-editor.py")))
osa = shared["osa"]
key = shared["key"]
item = shared["item"]
click_item = shared["click_item"]
center = shared["center"]
ax_activate = shared["ax_activate"]
ax_focus = shared["ax_focus"]
wait_for = shared["wait_for"]
app_pids = shared["app_pids"]
foreground = shared["foreground"]
voiceover_last = shared["voiceover_last"]


def run(bundle: Path, report_path: Path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    executable = bundle / "Contents" / "MacOS" / "FileCat"
    if bundle.parent != validation or bundle.suffix != ".app" or not executable.is_file():
        raise AssertionError("Use a signed, isolated FileCat bundle below FileCatValidation")
    if report_path.parent.resolve() != validation or report_path.exists():
        raise AssertionError("Use a new report inside FileCatValidation")
    if app_pids():
        raise AssertionError("An existing FileCat process makes this fixture ambiguous")
    if "output muted:true" not in osa("get volume settings"):
        raise AssertionError("Mute Mac output before VoiceOver acceptance")
    voiceover_last()

    fixture = validation / "MacRenameLastProblem" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    expected = {}
    for index in range(3001):
        name = f"item{index:04}.txt"
        content = f"owned item {index}\n".encode("ascii")
        (source / name).write_bytes(content)
        expected[name] = hashlib.sha256(content).hexdigest()
    blocker = source / "review_3001.txt"
    blocker.mkdir()
    (blocker / "keep.txt").write_bytes(b"owned blocking folder\n")

    helper_source = Path(__file__).with_name("mac-ax-rename-snapshot.swift")
    helper = fixture / "ax-rename-snapshot"
    developer = subprocess.run(["/usr/bin/xcode-select", "-p"], check=True,
                               capture_output=True, text=True, timeout=5).stdout.strip()
    sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
    built = subprocess.run(["/usr/bin/swiftc", "-sdk", str(sdk), str(helper_source), "-o", str(helper)],
                           capture_output=True, text=True, timeout=40)
    if built.returncode:
        raise AssertionError("Direct AX helper build failed: " + built.stderr[-1200:])

    pid = None
    try:
        subprocess.run(["/usr/bin/open", "-n", str(bundle), "--args", "--new-instance",
                        "--data", str(data), "--left", str(source), "--right", str(target)],
                       check=True, capture_output=True, timeout=15)
        pid = wait_for(lambda: next(iter(app_pids()), None), "the isolated Mac app")
        command = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "command="],
                                 check=True, capture_output=True, text=True, timeout=5).stdout.strip()
        if not command.startswith(str(executable) + " --new-instance --data " + str(data)):
            raise AssertionError("The process is not the exact isolated app")
        osa(foreground)

        def nodes():
            completed = subprocess.run([str(helper), str(executable), str(pid)],
                                       capture_output=True, text=True, timeout=15)
            if completed.returncode:
                raise AssertionError(completed.stderr.strip() or "Direct AX snapshot failed")
            snapshot = json.loads(completed.stdout)
            if snapshot["visited"] >= 5000 or snapshot["windows"] != 1:
                raise AssertionError("The Mac AX tree is unavailable or unbounded")
            return snapshot["nodes"]

        def first_source():
            found = nodes()
            return found if any(n["role"] == "AXRow" and n["name"] == "item0000.txt"
                                for n in found) else None

        initial = wait_for(first_source, "the first of 3,001 source files", seconds=35)
        click_item(initial, "AXRow", "item0000.txt")
        key("key code 0 using {control down}")  # FileCat Select all in this panel.
        # FileCat's Select all includes folders, too. The pre-existing folder
        # is deliberately left outside the submitted rename scope while still
        # occupying the last proposed filename.
        ax_focus("AXRow", blocker.name)
        key("key code 49")  # Space toggles only the focused folder's mark.

        def marked_files_only():
            found = nodes()
            statuses = [n["name"].replace("\u00a0", " ") for n in found if n["role"] == "AXStaticText"]
            return found if any("Marked 3 001 of 3 001 files" in text and "folder" not in text
                                for text in statuses) else None

        try:
            wait_for(marked_files_only, "3,001 marked files and an unmarked blocker")
        except AssertionError as exc:
            current = nodes()
            statuses = [(n["role"], n["name"], n["focused"], n["selected"]) for n in current
                        if "Marked" in n["name"] or n["name"] == blocker.name]
            raise AssertionError(f"{exc}; native mark status: {statuses[:15]!r}") from exc
        key("key code 46 using {control down}")  # Ctrl+M: Bulk rename.
        def opened_form():
            found = nodes()
            return found if any(n["role"] == "AXTextField" and n["name"] == "Name mask"
                                for n in found) else None

        wait_for(opened_form, "the bulk-rename form", seconds=30)
        ax_activate("AXTextField", "Name mask", focus=True)
        key('key code 0 using {command down}\nkeystroke "review_[C]"')
        started = time.monotonic()

        def blocking_review():
            found = nodes()
            problem = next((n for n in found if n["role"] == "AXButton" and
                            n["name"] == "Show blocking rename problems" and n["enabled"]), None)
            full_count = any("3 001" in n["name"].replace("\u00a0", " ") and
                             "names change" in n["name"] for n in found)
            return found if problem and full_count else None

        try:
            review = wait_for(blocking_review, "3,001 reviewed names and the blocking problem", seconds=45)
        except AssertionError as exc:
            current = nodes()
            status = [(n["role"], n["name"], n["enabled"]) for n in current if
                      "names change" in n["name"] or "problem" in n["name"].lower()
                      or "marked" in n["name"].lower() or n["name"] == "Bulk rename dialog"]
            raise AssertionError(f"{exc}; native review status: {status[:25]!r}") from exc
        preview_seconds = round(time.monotonic() - started, 3)
        ax_activate("AXButton", "Show blocking rename problems")

        old_path = source / "item3000.txt"
        new_path = source / "review_3001.txt"

        def last_problem():
            found = nodes()
            detail = next((n["value"] for n in found if n["role"] == "AXTextField" and
                           n["name"] == "Full old and new paths of selected rename"), "")
            position = next((n["name"] for n in found if n["role"] == "AXStaticText" and
                             n["name"].startswith("1 of 1 shown")), "")
            return (found, detail, position) if str(old_path) in detail and str(new_path) in detail and position else None

        problem_nodes, detail, position = wait_for(last_problem, "the last blocking full-path mapping", seconds=20)
        if "Problem:" not in detail:
            raise AssertionError("The blocking reason is missing from the full-path mapping")

        def spoken_problem():
            phrase = voiceover_last()
            return phrase if (str(old_path) in phrase and str(new_path) in phrase and
                              "Problem:" in phrase) else None

        spoken = wait_for(spoken_problem, "VoiceOver's complete blocking mapping", seconds=7)
        card = item(problem_nodes, "AXGroup", "Bulk rename dialog")
        cx, cy, cw, ch = card["bounds"]
        rename_buttons = [n for n in problem_nodes if n["role"] == "AXButton" and
                          n["name"] == "Rename" and cx <= center(n)[0] < cx + cw and
                          cy <= center(n)[1] < cy + ch]
        if len(rename_buttons) != 1 or rename_buttons[0]["enabled"]:
            raise AssertionError(f"The blocked dialog Rename action is missing or enabled: {rename_buttons!r}")
        ax_activate("AXButton", "Cancel")
        wait_for(lambda: not any(n["role"] == "AXGroup" and n["name"] == "Bulk rename dialog"
                                 for n in nodes()), "the review to close")
        if {p.name for p in source.iterdir()} != set(expected) | {blocker.name} or list(target.iterdir()):
            raise AssertionError("Cancel changed the fixture manifest")
        for name, digest in expected.items():
            if hashlib.sha256((source / name).read_bytes()).hexdigest() != digest:
                raise AssertionError(f"Cancel changed source bytes: {name}")
        if (blocker / "keep.txt").read_bytes() != b"owned blocking folder\n":
            raise AssertionError("The blocking folder changed")
        return {"passed": True, "platform": "macOS", "acceptedFiles": len(expected),
                "previewSeconds": preview_seconds, "problemPosition": position,
                "problemDetail": detail, "approvalDisabled": True,
                "voiceOverProblemPhrase": spoken,
                "boundedAxNodes": len(problem_nodes), "sourceAndTargetUnchanged": True,
                "appSha256": hashlib.sha256((bundle / "Contents/MacOS/FileCat.dll").read_bytes()).hexdigest(),
                "fixture": str(fixture)}
    finally:
        if pid is not None:
            command = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "command="],
                                     capture_output=True, text=True, timeout=5).stdout.strip()
            if command.startswith(str(executable) + " --new-instance --data " + str(data)):
                os.kill(pid, signal.SIGTERM)


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    report = Path(sys.argv[2])
    try:
        result = run(Path(sys.argv[1]), report)
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
