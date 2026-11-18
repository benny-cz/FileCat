#!/usr/bin/env python3
"""Exercise File-system details with native keyboard events and optional muted VoiceOver speech.

Usage: mac-ax-filesystem-details.py BUNDLE.app AX_OBSERVER.swift REPORT.json [--voiceover] [--narrow]
"""

import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time


def main():
    options = sys.argv[4:]
    if len(sys.argv) < 4 or len(options) != len(set(options)) or set(options) - {"--voiceover", "--narrow"}:
        raise SystemExit(__doc__)
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle, source = (Path(arg).resolve(strict=True) for arg in sys.argv[1:3])
    report_path = Path(sys.argv[3]).resolve()
    voiceover, narrow = "--voiceover" in options, "--narrow" in options
    if bundle.parent != validation or bundle.suffix != ".app" or report_path.parent != validation or report_path.exists():
        raise ValueError("Use a dedicated application and a fresh result under ~/FileCatValidation.")
    app = bundle / "Contents" / "MacOS" / "FileCat"
    if subprocess.run(["/usr/bin/pgrep", "-x", "FileCat"], capture_output=True).returncode == 0:
        raise RuntimeError("Another FileCat process is already running.")
    fixture = Path(tempfile.mkdtemp(prefix="filesystem-details-ax-", dir=validation))
    root, target, state = (fixture / name for name in ("source", "target", "state"))
    if narrow:
        root = (root / "archived-project-material-for-the-native-file-system-details-review" /
                "working-notes-and-background-files-for-the-reviewed-path-and-its-clipboard-report")
    for folder in (root, target, state):
        folder.mkdir(parents=True)
    original = root / "alpha-details.txt"
    original.write_text("FILECAT-DATA-PAYLOAD-CANARY", encoding="utf-8")
    (root / "beta-not-selected.txt").write_text("alternative", encoding="utf-8")
    subprocess.run(["/usr/bin/xattr", "-w", "org.filecat.details", "FILECAT-XATTR-PAYLOAD-CANARY", str(original)], check=True)
    observer = fixture / "ax-observer"
    subprocess.run(["/usr/bin/swiftc", "-framework", "ApplicationServices", str(source), "-o", str(observer)], check=True, timeout=120)
    evidence = dict(passed=False, platform="macOS accessibility bridge", voiceOver=voiceover, fixture=str(fixture),
                    appDllSha256=hashlib.sha256(app.with_name("FileCat.dll").read_bytes()).hexdigest(), steps=[])
    pid = None
    expected_command = f"{app} --new-instance --data {state}"

    def osa(script):
        return subprocess.check_output(["/usr/bin/osascript", "-e", script], text=True, timeout=20).strip()

    def last_phrase():
        return osa('tell application "VoiceOver" to get content of last phrase')

    def speech(label, *terms):
        if not voiceover:
            return
        heard = []

        def spoken():
            phrase = last_phrase()
            if phrase and phrase not in heard:
                heard.append(phrase)
            return phrase if all(term.casefold() in phrase.casefold() for term in terms) else None

        try:
            wait(spoken, label, seconds=8)
        finally:
            evidence["steps"].append(dict(step=label, spoken=heard))

    def wait(check, label, seconds=16):
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            value = check()
            if value:
                return value
            time.sleep(0.18)
        raise AssertionError(f"Timed out waiting for {label}")

    def active_pid():
        return osa('tell application "System Events" to get unix id of first application process whose frontmost is true')

    def key(code):
        if active_pid() != str(pid):
            raise RuntimeError("The test FileCat application lost keyboard ownership.")
        osa(f'tell application "System Events" to {code}')

    def focused():
        return json.loads(subprocess.check_output([str(observer), "focused", str(pid)], text=True, timeout=8))

    def focus_text(record):
        return " ".join(str(record.get(key, "")) for key in ("title", "description", "value", "help"))

    def expect_focus(label, name):
        def matching():
            record = focused()
            return record if name.casefold() in focus_text(record).casefold() else None
        evidence["steps"].append(dict(step=label, focus=wait(matching, label)))

    def window_titles():
        return osa(f'''tell application "System Events"
set matches to every application process whose unix id is {pid}
if (count of matches) is 0 then return ""
return name of every window of (item 1 of matches)
end tell''')

    def resize_report():
        size = osa(f'''tell application "System Events" to tell (first application process whose unix id is {pid})
repeat with w in windows
    if (name of w as text) contains "File-system details" then
        set position of w to {{80, 40}}
        set size of w to {{520, 420}}
        set actual to size of w
        return (item 1 of actual as text) & "|" & (item 2 of actual as text)
    end if
end repeat
end tell
error "The owned details window is unavailable"''')
        width, height = map(int, size.split("|"))
        if abs(width - 520) > 2 or abs(height - 420) > 2:
            raise AssertionError(f"Mac did not set the requested narrow report size: {width}×{height}")
        evidence["reportSizePoints"] = [width, height]
        evidence["sourcePathLength"] = len(str(original))

    def tab_to(name):
        for _ in range(28):
            record = focused()
            if name.casefold() in focus_text(record).casefold():
                evidence["steps"].append(dict(step=f"Keyboard reaches {name}", focus=record))
                return
            key("key code 48")
            time.sleep(0.20)
        raise AssertionError(f"Could not reach {name}")

    try:
        if voiceover:
            if "output muted:true" not in osa("get volume settings"):
                raise AssertionError("Mute test Mac output before recording VoiceOver speech.")
            last_phrase()
        log = fixture / "app.log"
        subprocess.run(["/usr/bin/open", "-n", str(bundle), "--env", f"DOTNET_ROOT={Path.home() / '.dotnet'}",
                        "--stdout", str(log), "--stderr", str(log), "--args", "--new-instance", "--data", str(state),
                        "--left", str(root), "--right", str(target)], check=True, capture_output=True, timeout=15)

        def matching_process():
            candidates = subprocess.run(["/usr/bin/pgrep", "-x", "FileCat"], capture_output=True, text=True, timeout=5).stdout.split()
            for candidate in candidates:
                command = subprocess.run(["/bin/ps", "-p", candidate, "-o", "command="], capture_output=True, text=True, timeout=5).stdout.strip()
                if command.startswith(expected_command + " "):
                    return int(candidate)
            return None

        pid = wait(matching_process, "the isolated FileCat process", seconds=30)
        wait(window_titles, "the Mac application window", seconds=30)
        osa(f'tell application "System Events" to tell (first application process whose unix id is {pid}) to set frontmost to true')
        wait(lambda: active_pid() == str(pid), "the owned application to have focus")
        expect_focus("Initial file panel item", "Go to parent location")
        key("key code 125")
        expect_focus("Selected file by keyboard", original.name)
        speech("VoiceOver speaks the selected file", original.name)
        key("key code 35 using {control down, shift down}")
        expect_focus("Command search by keyboard", "Search commands")
        key('keystroke "file-system details"')
        wait(lambda: focused().get("value", "").casefold() == "file-system details", "the command search text")
        key("key code 36")
        wait(lambda: "File-system details" in window_titles(), "the selected file details window")
        speech("VoiceOver announces the details window", "File-system details")
        if narrow:
            resize_report()
        tab_to("Read full record")
        key("key code 49")
        speech("VoiceOver announces the full observation", "full", "observation")
        tab_to("Show summary")
        tab_to("Copy all")
        key("key code 49")

        def metadata_clipboard():
            text = subprocess.check_output(["/usr/bin/pbpaste"], text=True, timeout=5)
            return text if str(original) in text and "Full record" in text else None

        report = wait(metadata_clipboard, "the full record on the Mac pasteboard")
        assert "supplementary-data values and previews" in report and "metadata only" in report
        assert "FILECAT-DATA-PAYLOAD-CANARY" not in report and "FILECAT-XATTR-PAYLOAD-CANARY" not in report
        assert "beta-not-selected" not in report
        evidence["fullReportPasteboardBytes"] = len(report.encode("utf-8"))
        tab_to("Show summary")
        key("key code 49")
        speech("VoiceOver announces the summary observation", "summary", "observation")
        tab_to("Show full record")
        key("key code 53")
        wait(lambda: "File-system details" not in window_titles(), "the details window to close")
        expect_focus("Focus returns to the same file", original.name)
        speech("VoiceOver returns to the same file", original.name)
        assert original.read_text(encoding="utf-8") == "FILECAT-DATA-PAYLOAD-CANARY"
        xattr = subprocess.check_output(["/usr/bin/xattr", "-p", "org.filecat.details", str(original)], text=True)
        assert xattr.strip() == "FILECAT-XATTR-PAYLOAD-CANARY"
        evidence["passed"] = True
    except Exception as error:
        evidence["error"] = f"{type(error).__name__}: {error}"
        raise
    finally:
        if pid is not None and matching_process() == pid:
            os.kill(pid, signal.SIGTERM)
        report_path.write_text(json.dumps(evidence, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
