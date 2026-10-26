#!/usr/bin/env python3
"""Check actual muted VoiceOver speech after stopping an isolated Mac viewer search.

Usage: python3 mac-voiceover-viewer-stop.py BUNDLE.app REPORT.json [--text]
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


voiceover = runpy.run_path(str(Path(__file__).with_name("mac-voiceover-list.py")))
osa = voiceover["osa"]
last_phrase = voiceover["last_phrase"]
app_pids = voiceover["app_pids"]
wait_for = voiceover["wait_for"]
LENGTH = 2 * 1024 * 1024 * 1024


def sha256(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def ax(helper, executable, pid, mode, *args):
    done = subprocess.run([str(helper), str(executable), str(pid), mode, *args],
                          capture_output=True, text=True, timeout=15)
    if done.returncode:
        raise AssertionError(f"Direct Mac AX {mode} {args} failed: " +
                             (done.stderr.strip() or "unknown native error"))
    return json.loads(done.stdout) if mode == "snapshot" else done.stdout.strip()


def sample_intact(path):
    if path.stat().st_size != LENGTH:
        return False
    with path.open("rb") as source:
        for offset in (0, LENGTH // 2, LENGTH - 32):
            source.seek(offset)
            if source.read(32) != bytes(32):
                return False
    return True


def run(bundle, report, text_mode):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    executable = bundle / "Contents/MacOS/FileCat"
    if (bundle.parent != validation or bundle.suffix != ".app" or
            not executable.is_file() or report.parent.resolve() != validation or report.exists()):
        raise AssertionError("Use a signed isolated Mac app and fresh validation report")
    if "output muted:true" not in osa("get volume settings"):
        raise AssertionError("Mute Mac audio before the VoiceOver journey")
    last_phrase()
    if app_pids():
        raise AssertionError("An unrelated FileCat process makes the Mac speech result ambiguous")
    root = validation / "MacVoiceOverStop" / uuid4().hex
    source, target, data = (root / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    test_file = source / "search-stop.bin"
    with test_file.open("xb") as output:
        output.truncate(LENGTH)
    if not sample_intact(test_file):
        raise AssertionError("The exact sparse source fixture is unavailable")
    ax_source = Path(__file__).with_name("mac-ax-modal-control.swift")
    if not ax_source.is_file():
        raise AssertionError("The bounded direct AX helper source is missing")
    helper = root / "mac-ax-modal-control"
    developer = subprocess.run(["/usr/bin/xcode-select", "-p"], capture_output=True,
                               text=True, check=True, timeout=5).stdout.strip()
    sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
    if not sdk.is_dir():
        raise AssertionError("The matching macOS SDK is unavailable")
    compiled = subprocess.run(["/usr/bin/swiftc", "-sdk", str(sdk), str(ax_source),
                               "-o", str(helper)], capture_output=True, text=True, timeout=45)
    if compiled.returncode:
        raise AssertionError("Direct Mac AX helper build failed: " + compiled.stderr[-1200:])
    clipboard = subprocess.run(["/usr/bin/pbpaste"], capture_output=True,
                               timeout=5).stdout
    pid = None
    try:
        launched = subprocess.run(["/usr/bin/open", "-n", str(bundle), "--args",
                                   "--new-instance", "--data", str(data),
                                   "--left", str(source), "--right", str(target)],
                                  capture_output=True, text=True, timeout=15)
        if launched.returncode:
            raise AssertionError(launched.stderr.strip() or "The isolated Mac app did not open")
        pid = wait_for(lambda: next(iter(app_pids()), None))
        command = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "command="],
                                 capture_output=True, text=True, check=True,
                                 timeout=5).stdout.strip()
        if not command.startswith(str(executable) + " --new-instance --data " + str(data)):
            raise AssertionError("The active FileCat process is not the isolated test app")
        def ax_ready():
            try:
                return ax(helper, executable, pid, "snapshot")["windows"] >= 1
            except AssertionError:
                return False  # FileCat may still be building its first AX window.
        wait_for(ax_ready)
        ax(helper, executable, pid, "focus-row", "search-stop.bin")
        osa('tell application "System Events" to tell process "FileCat" to set frontmost to true')
        osa('tell application "System Events" to key code 99')  # F3 opens the focused file.
        title = "search-stop.bin — FileCat Viewer"
        wait_for(lambda: title in osa(
            'tell application "System Events" to tell process "FileCat" to get name of every window'))
        if text_mode:
            ax(helper, executable, pid, "press-button", "Text", "top")
        osa('tell application "System Events" to key code 98')  # F7 opens Find.
        for code in (6, 6, 6, 6, 6, 6, 36):  # zzzzzz, Return.
            osa(f'tell application "System Events" to key code {code}')
        wait_for(lambda: any(node["role"] == "AXButton" and node["name"] == "Stop search"
                             for node in ax(helper, executable, pid, "snapshot")["nodes"]),
                 seconds=10)
        before = last_phrase()
        ax(helper, executable, pid, "press-button", "Stop search", "top")
        heard = []
        deadline = time.monotonic() + 8
        while time.monotonic() < deadline:
            phrase = last_phrase()
            if phrase not in heard:
                heard.append(phrase)
            if "Search canceled" in phrase:
                break
            time.sleep(0.2)
        if not any("Search canceled" in phrase for phrase in heard):
            raise AssertionError(f"VoiceOver did not speak viewer Stop: before={before!r} after={heard!r}")
        if not sample_intact(test_file) or list(target.iterdir()):
            raise AssertionError("The VoiceOver Stop journey changed the source or target")
        if subprocess.run(["/usr/bin/pbpaste"], capture_output=True, timeout=5).stdout != clipboard:
            raise AssertionError("The VoiceOver Stop journey changed the clipboard")
        return {"passed": True, "platform": "macOS", "mode": "Text" if text_mode else "Hex",
                "appDllSha256": sha256(executable.with_name("FileCat.dll")),
                "fixture": str(root), "sourceLength": LENGTH,
                "voiceOverSpokeCancellation": True, "speechBeforeStop": before,
                "speechAfterStop": heard, "sourceAndTargetUnchanged": True,
                "clipboardTextPreserved": True}
    finally:
        if pid is not None and pid in app_pids():
            os.kill(pid, signal.SIGTERM)
            wait_for(lambda: pid not in app_pids())
        test_file.unlink(missing_ok=True)


def main():
    if len(sys.argv) not in (3, 4) or (len(sys.argv) == 4 and sys.argv[3] != "--text"):
        raise SystemExit(__doc__)
    report = Path(sys.argv[2])
    try:
        result = run(Path(sys.argv[1]), report, text_mode=len(sys.argv) == 4)
    except Exception as error:
        result = {"passed": False, "error": f"{type(error).__name__}: {error}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
