#!/usr/bin/env python3
"""Exercise File-system details with native keyboard events and optional muted VoiceOver speech.

Usage: mac-ax-filesystem-details.py BUNDLE.app AX_OBSERVER.swift REPORT.json [--voiceover] [--narrow] [--link-target | --find-results] [--properties] [--supplementary]
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
    supported_options = {"--voiceover", "--narrow", "--link-target", "--find-results", "--properties", "--supplementary"}
    if (len(sys.argv) < 4 or len(options) != len(set(options)) or set(options) - supported_options
            or {"--link-target", "--find-results"}.issubset(options)):
        raise SystemExit(__doc__)
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle, source = (Path(arg).resolve(strict=True) for arg in sys.argv[1:3])
    report_path = Path(sys.argv[3]).resolve()
    voiceover, narrow = "--voiceover" in options, "--narrow" in options
    link_target = "--link-target" in options
    find_results = "--find-results" in options
    properties = "--properties" in options
    supplementary = "--supplementary" in options
    if supplementary and not find_results:
        raise SystemExit("--supplementary requires --find-results")
    if bundle.parent != validation or bundle.suffix != ".app" or report_path.parent != validation or report_path.exists():
        raise ValueError("Use a dedicated application and a fresh result under ~/FileCatValidation.")
    app = bundle / "Contents" / "MacOS" / "FileCat"
    for process in ("dotnet", "testhost", "FileCat", "VoiceOver") if not voiceover else ("dotnet", "testhost", "FileCat"):
        if subprocess.run(["/usr/bin/pgrep", "-x", process], capture_output=True).returncode == 0:
            raise RuntimeError(f"An unrelated {process} process is already active.")
    fixture = Path(tempfile.mkdtemp(prefix="filesystem-details-ax-", dir=validation))
    root, target, state = (fixture / name for name in ("source", "target", "state"))
    if narrow:
        root = (root / "archived-project-material-for-the-native-file-system-details-review" /
                "working-notes-and-background-files-for-the-reviewed-path-and-its-clipboard-report")
    for folder in (target, state):
        folder.mkdir(parents=True)
    if link_target:
        destination = fixture / "actual-parent"
        subfolder = destination / "child"
        subfolder.mkdir(parents=True)
        root.parent.mkdir(parents=True, exist_ok=True)
        root.symlink_to(subfolder, target_is_directory=True)
        target_file = destination / "landed.txt"
        (root.parent / target_file.name).write_text("incorrect lexical target", encoding="utf-8")
        original = root / "alpha-link"
        stored_target = f"../{target_file.name}"
        original.symlink_to(stored_target)
    else:
        root.mkdir(parents=True)
        target_file = original = root / "alpha-details.txt"
    target_file.write_text("FILECAT-DATA-PAYLOAD-CANARY", encoding="utf-8")
    (root / "beta-not-selected.txt").write_text("alternative", encoding="utf-8")
    subprocess.run(["/usr/bin/xattr", "-w", "org.filecat.details", "FILECAT-XATTR-PAYLOAD-CANARY", str(target_file)], check=True)
    observer = fixture / "ax-observer"
    subprocess.run(["/usr/bin/swiftc", "-framework", "ApplicationServices", str(source), "-o", str(observer)], check=True, timeout=120)
    evidence = dict(passed=False, platform="macOS accessibility bridge", voiceOver=voiceover, propertiesShortcut=properties,
                    findResults=find_results, fixture=str(fixture),
                    appDllSha256=hashlib.sha256(app.with_name("FileCat.dll").read_bytes()).hexdigest(), steps=[])
    pid = None
    expected_command = f"{app} --new-instance --data {state}"

    def osa(script):
        return subprocess.check_output(["/usr/bin/osascript", "-e", script], text=True, timeout=20).strip()

    def last_phrase():
        return osa('tell application "VoiceOver" to get content of last phrase')

    def speech(label, *terms, required=True):
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
            return True
        except AssertionError:
            if required:
                raise
            return False
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

    def front_window():
        return osa(f'tell application "System Events" to tell (first application process whose unix id is {pid}) to get name of front window')

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

    def tab_to(name, role=None):
        for _ in range(28):
            record = focused()
            if (role is None or record.get("role") == role) and name.casefold() in focus_text(record).casefold():
                evidence["steps"].append(dict(step=f"Keyboard reaches {name}", focus=record))
                return
            key("key code 48")
            time.sleep(0.20)
        raise AssertionError(f"Could not reach {name}")

    def search_command(command):
        key("key code 35 using {control down, shift down}")
        expect_focus("Command search by keyboard", "Search commands")
        key(f'keystroke "{command}"')
        wait(lambda: focused().get("value", "").casefold() == command, "the command search text")
        key("key code 36")

    def open_find_result():
        key("key code 98 using {option down}")
        wait(lambda: "Find files" in window_titles(), "the standalone Find window")
        tab_to("Names")
        key('keystroke "a" using {command down}')
        key(f'keystroke "{original.name}"')
        wait(lambda: focused().get("value") == original.name, "the exact Find filename")
        key("key code 36")
        wait(lambda: f"Find: {original.name}" in window_titles(), "the standalone Find results")
        tab_to(original.name, role="AXRow")
        speech("VoiceOver speaks the standalone Find result", original.name)

    def open_find_supplementary(find_title):
        attribute = "org.filecat.details"

        def inventory_window():
            name = front_window()
            return name if name != find_title and original.name in name else None

        key("key code 36 using {option down, shift down}")
        evidence["workspaceInventoryWindow"] = wait(inventory_window, "the selected original's supplementary-data workspace")
        expect_focus("Workspace focuses the parent row of the attribute inventory", "Go to parent location")
        key("key code 125")
        expect_focus("Workspace exposes the selected original's attribute name", attribute)
        speech("VoiceOver speaks the selected original's attribute name", attribute)
        key("key code 50 using {command down}")
        wait(lambda: front_window() == find_title, "focus on the original Find results window")
        expect_focus("Workspace switch leaves the original Find result focused", original.name)
        if voiceover:
            evidence["workspaceReturnSpoken"] = speech(
                "VoiceOver speaks the retained Find result", original.name, "table row", required=False,
            )
        if "FILECAT-XATTR-PAYLOAD-CANARY" in json.dumps(evidence["steps"]):
            raise AssertionError("Names-only browsing spoke an attribute value.")

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
        if find_results:
            open_find_result()
        else:
            speech("VoiceOver speaks the selected file", original.name)
        if properties:
            key("key code 36 using {option down}")
        elif find_results:
            key("key code 36 using {control down, option down}")
        else:
            search_command("file-system details")
        wait(lambda: "File-system details" in window_titles(), "the selected file details window")
        if find_results:
            speech("VoiceOver announces the initial summary", "summary", "observation")
        else:
            speech("VoiceOver announces the details window", "File-system details")
        if narrow:
            resize_report()
        if link_target:
            assert destination.name not in window_titles()
            tab_to("Open target location")
            key("key code 49")
            wait(lambda: destination.name in window_titles(), "the real target directory tab")
            expect_focus("Workspace selects the resolved destination item", target_file.name)
            speech("VoiceOver announces the resolved destination item", target_file.name)
            assert os.readlink(original) == stored_target
            assert target_file.read_text(encoding="utf-8") == "FILECAT-DATA-PAYLOAD-CANARY"
            target_file.unlink()
            osa(f'''tell application "System Events" to tell (first application process whose unix id is {pid})
repeat with w in windows
    if (name of w as text) contains "File-system details" then
        perform action "AXRaise" of w
        return
    end if
end repeat
end tell
error "The owned details window is unavailable"''')
            wait(lambda: "File-system details" in front_window(),
                 "the original link report to regain focus")
            tab_to("Open target location")
            key("key code 49")

            def missing_status():
                result = subprocess.check_output([str(observer), "find", str(pid), "Target could not be opened: Missing"],
                                                 text=True, timeout=8)
                return next((record for record in map(json.loads, result.splitlines()) if record.get("pid") == str(pid)), None)

            evidence["steps"].append(dict(step="Accessibility exposes a missing destination after explicit activation",
                                          status=wait(missing_status, "the missing target status")))
            assert os.readlink(original) == stored_target
            evidence.update(passed=True, storedTarget=stored_target, destination=str(destination))
            return
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
        if find_results:
            key("key code 49")
            speech("VoiceOver announces the cached full observation", "full", "observation")
        key("key code 53")
        wait(lambda: "File-system details" not in window_titles(), "the details window to close")
        if find_results:
            expected_window = f"Find: {original.name}"
            wait(lambda: front_window() == expected_window, "focus on the standalone Find results window")
            evidence["returnedWindow"] = expected_window
        expect_focus("Focus returns to the same file", original.name)
        speech("VoiceOver returns to the same file", original.name)
        if supplementary:
            open_find_supplementary(expected_window)
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
