#!/usr/bin/env python3
"""Keep Copy, Move and Delete reviews modal under native macOS AX actions.

Usage: python3 mac-ax-modal-guard.py BUNDLE.app REPORT.json
Run on the unlocked test Mac with direct Accessibility API permission.
"""

import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
from uuid import uuid4


def wait_for(check, description, seconds=20):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        try:
            value = check()
            if value:
                return value
        except AssertionError:
            pass
        time.sleep(0.2)
    raise AssertionError(f"Timed out waiting for {description}")


def app_pids():
    done = subprocess.run(["/usr/bin/pgrep", "-x", "FileCat"],
                          capture_output=True, text=True, timeout=5)
    return {int(value) for value in done.stdout.split()}


def ax(helper, executable, pid, mode, *args):
    done = subprocess.run([str(helper), str(executable), str(pid), mode, *args],
                          capture_output=True, text=True, timeout=15)
    if done.returncode:
        raise AssertionError(done.stderr.strip() or "Direct macOS AX action failed")
    return json.loads(done.stdout) if mode == "snapshot" else done.stdout.strip()


def review_state(helper, executable, pid, verb):
    tree = ax(helper, executable, pid, "snapshot")
    if tree["visited"] >= 5000 or tree["windows"] != 1:
        raise AssertionError("The isolated Mac modal tree is absent or unbounded")
    nodes = tree["nodes"]
    count = lambda role, predicate: sum(node["role"] == role and predicate(node["name"])
                                        for node in nodes)
    summary = (lambda name: "Move" in name and "a.txt" in name and "Recycle Bin" in name)
    if verb != "Delete":
        summary = lambda name: verb in name and "a.txt" in name
    return (count("AXButton", lambda name: name == "Cancel"),
            count("AXTextField", lambda name: name == "Destination"),
            count("AXStaticText", summary))


def sha256(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def run(bundle, report):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    executable = bundle / "Contents/MacOS/FileCat"
    if (bundle.parent != validation or bundle.suffix != ".app" or
            not executable.is_file() or report.parent.resolve() != validation):
        raise AssertionError("Use an isolated Mac app and report below FileCatValidation")
    if app_pids():
        raise AssertionError("An existing FileCat process makes modal ownership ambiguous")
    fixture = validation / "MacModalGuard" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    source_file = source / "a.txt"
    source_file.write_bytes(b"modal source remains")
    original_hash = sha256(source_file)
    ax_source = Path(__file__).with_name("mac-ax-modal-control.swift")
    if not ax_source.is_file():
        raise AssertionError("The bounded direct AX helper source is missing")
    ax_helper = fixture / "mac-ax-modal-control"
    developer = subprocess.run(["/usr/bin/xcode-select", "-p"], check=True,
                               capture_output=True, text=True, timeout=5).stdout.strip()
    sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
    if not sdk.is_dir():
        raise AssertionError("The matching macOS SDK is unavailable")
    compiled = subprocess.run(["/usr/bin/swiftc", "-sdk", str(sdk), str(ax_source),
                               "-o", str(ax_helper)], capture_output=True,
                              text=True, timeout=45)
    if compiled.returncode:
        raise AssertionError("Direct AX helper build failed: " + compiled.stderr[-1200:])
    receipt = {"passed": False, "bundle": str(bundle),
               "appDllSha256": sha256(executable.with_name("FileCat.dll")),
               "fixture": str(fixture), "sourceSha256": original_hash,
               "journeys": []}
    pid = None
    try:
        opened = subprocess.run(["/usr/bin/open", "-n", str(bundle), "--args",
                                 "--new-instance", "--data", str(data),
                                 "--left", str(source), "--right", str(target)],
                                capture_output=True, text=True, timeout=15)
        if opened.returncode:
            raise AssertionError(opened.stderr.strip() or "Mac test app did not open")
        pid = wait_for(lambda: next(iter(app_pids()), None), "isolated FileCat process")
        command = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "command="],
                                 capture_output=True, text=True, check=True, timeout=5).stdout.strip()
        if not command.startswith(str(executable) + " --new-instance --data " + str(data)):
            raise AssertionError("An unexpected FileCat process owns the Mac desktop")
        for verb in ("Copy", "Move", "Delete"):
            wait_for(lambda: ax(ax_helper, executable, pid, "focus-row", "a.txt"),
                     "the exact a.txt source row")
            ax(ax_helper, executable, pid, "press-button",
               "Move / rename…" if verb == "Move" else f"{verb}…", "top")
            expected_destination = 0 if verb == "Delete" else 1
            initial = wait_for(lambda: (state if (state := review_state(ax_helper, executable, pid, verb))[0] == 1 else None),
                               f"the single {verb} review")
            if initial[1] != expected_destination or initial[2] < 1:
                names = [node["name"] for node in
                         ax(ax_helper, executable, pid, "snapshot")["nodes"]
                         if node["role"] == "AXStaticText"]
                relevant = [name for name in names if verb in name or "a.txt" in name]
                raise AssertionError(f"The {verb} review lacks its destination or file summary: {initial}; labels: {relevant[:20]}")
            attempts = []
            for name in ("Copy…", "Move / rename…", "Delete…", "Copy", "Move", "Delete"):
                ax(ax_helper, executable, pid, "press-button", name,
                   "bottom" if not name.endswith("…") else "top")
                time.sleep(0.25)
                current = review_state(ax_helper, executable, pid, verb)
                if current != initial or not source_file.is_file() or sha256(source_file) != original_hash or list(target.iterdir()):
                    raise AssertionError(f"Background {name} changed the {verb} review or fixture: {current}")
                attempts.append({"command": name, "modalStillSingle": True})
            ax(ax_helper, executable, pid, "press-button", "Cancel", "top")
            wait_for(lambda: review_state(ax_helper, executable, pid, verb)[0] == 0,
                     f"{verb} review to close")
            if not source_file.is_file() or sha256(source_file) != original_hash or list(target.iterdir()):
                raise AssertionError(f"Cancel changed the fixture after {verb}")
            receipt["journeys"].append({"review": verb, "initial": initial,
                                        "attempts": attempts, "targetAbsent": True})
        receipt["passed"] = True
    finally:
        if pid is not None and pid in app_pids():
            os.kill(pid, signal.SIGTERM)
            wait_for(lambda: pid not in app_pids(), "isolated FileCat process to exit")
    return receipt


def main():
    if len(sys.argv) != 3:
        raise SystemExit("Usage: mac-ax-modal-guard.py BUNDLE.app REPORT.json")
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
