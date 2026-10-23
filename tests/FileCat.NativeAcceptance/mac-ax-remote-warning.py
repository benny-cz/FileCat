#!/usr/bin/env python3
"""Inspect and hear an isolated Mac remote replacement review.

Usage: mac-ax-remote-warning.py BUNDLE.app REPORT.json FTP_HELPER ftp|ftpes [--probe]
The probe ends at the expanded Copy options and records native AX controls.
"""

import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import time
from uuid import uuid4


def run_command(args, seconds=15):
    result = subprocess.run(args, capture_output=True, text=True, timeout=seconds)
    if result.returncode:
        raise AssertionError(result.stderr[-1200:] or f"Command failed: {args[0]}")
    return result.stdout.strip()


def osa(script):
    return run_command(["/usr/bin/osascript", "-e", script], seconds=30)


def wait_for(check, description, seconds=25):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        value = check()
        if value:
            return value
        time.sleep(0.2)
    raise AssertionError(f"Timed out waiting for {description}")


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def app_pids():
    result = subprocess.run(["/usr/bin/pgrep", "-x", "FileCat"], capture_output=True, text=True, timeout=5)
    return {int(value) for value in result.stdout.split()}


def voiceover_last():
    return osa('tell application "VoiceOver" to get content of last phrase')


def click_ax(role, name):
    script = '''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        repeat with w in windows
            set allElements to entire contents of w
            repeat with e in allElements
                try
                    if (role of e as text) is ROLE and (name of e as text) is NAME then
                        click e
                        return "clicked"
                    end if
                end try
            end repeat
        end repeat
    end tell
end tell
error "The owned AX action was absent"
'''.replace("ROLE", json.dumps(role)).replace("NAME", json.dumps(name))
    if osa(script) != "clicked":
        raise AssertionError(f"Could not click {role} {name!r}")


def set_field(name, value, rightmost=False, enter=False):
    script = '''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        set chosen to missing value
        repeat with w in windows
            set allElements to entire contents of w
            repeat with e in allElements
                try
                    if (role of e as text) is "AXTextField" and (name of e as text) is FIELD then
                        if chosen is missing value or RIGHTMOST then set chosen to e
                    end if
                end try
            end repeat
        end repeat
        if chosen is missing value then error "The exact native field is absent"
        set value of chosen to VALUE
        set focused of chosen to true
        if ENTER then key code 36
        return "set"
    end tell
end tell
'''.replace("FIELD", json.dumps(name)).replace("VALUE", json.dumps(value)).replace(
        "RIGHTMOST", "true" if rightmost else "false").replace("ENTER", "true" if enter else "false")
    last = None
    for _ in range(8):
        try:
            return osa(script)
        except AssertionError as exc:
            last = exc
            time.sleep(0.25)
    raise last


def click_node(node):
    x, y, width, height = node["bounds"]
    if width < 8 or height < 8:
        raise AssertionError(f"Unusable native AX bounds for {node['name']!r}: {node['bounds']}")
    run_command(["/opt/homebrew/bin/cliclick", f"c:{round(x + width / 2)},{round(y + height / 2)}"])


def capture_screen(validation):
    helper = validation / "FileCatCapture.app"
    if not helper.is_dir():
        return None
    before = set(validation.glob("native-screen-*.png"))
    run_command(["/usr/bin/open", "-a", str(helper)], seconds=30)
    return wait_for(lambda: next((str(path) for path in
                    sorted(set(validation.glob("native-screen-*.png")) - before,
                           key=lambda path: path.stat().st_mtime_ns, reverse=True)
                    if path.stat().st_size > 100_000), None), "owned Mac warning screenshot", seconds=15)


def run(bundle, report_path, ftp_helper, protocol, probe):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    ftp_helper = ftp_helper.resolve(strict=True)
    report_path = report_path.resolve()
    executable = bundle / "Contents/MacOS/FileCat"
    if (bundle.parent != validation or bundle.suffix != ".app" or not executable.is_file() or
            report_path.parent != validation or ftp_helper.name != "win-ftp-owned-stage.py" or
            ftp_helper.parent != validation):
        raise AssertionError("Use only the isolated signed app and Git fixture helper below FileCatValidation")
    run_command(["/usr/bin/codesign", "--verify", "--deep", "--strict", str(bundle)])
    if app_pids():
        raise AssertionError("An existing FileCat process makes the Mac review ambiguous")
    if "output muted:true" not in osa("get volume settings"):
        raise AssertionError("Mute Mac output during VoiceOver acceptance")
    voiceover_last()
    fixture = validation / "MacRemoteWarning" / uuid4().hex
    source_dir, server_dir, data = (fixture / part for part in ("source", "server", "data"))
    for folder in (source_dir, server_dir, data):
        folder.mkdir(parents=True)
    source, target = source_dir / "target.txt", server_dir / "target.txt"
    source.write_bytes(b"owned incoming content\n")
    target.write_bytes(b"owned existing remote content\n")
    receipt = {"passed": False, "probe": probe, "protocol": protocol, "bundle": str(bundle),
               "appDllSha256": sha256(executable.with_name("FileCat.dll")),
               "fixture": str(fixture), "sourceSha256": sha256(source), "targetSha256": sha256(target),
               "steps": []}
    developer = run_command(["/usr/bin/xcode-select", "-p"], seconds=5)
    sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
    ax_helper = fixture / "ax-remote-snapshot"
    ax_setter = fixture / "ax-remote-set-field"
    swift = Path(__file__).with_name("mac-ax-rename-snapshot.swift")
    run_command(["/usr/bin/swiftc", "-sdk", str(sdk), str(swift), "-o", str(ax_helper)], seconds=40)
    run_command(["/usr/bin/swiftc", "-sdk", str(sdk),
                 str(Path(__file__).with_name("mac-ax-set-field.swift")), "-o", str(ax_setter)], seconds=40)
    server = None
    pid = None
    try:
        ready = fixture / "server.ready"
        with (fixture / "server.log").open("wb") as log:
            server = subprocess.Popen([sys.executable, str(ftp_helper),
                                       "serve-tls" if protocol == "ftpes" else "serve",
                                       str(server_dir), str(ready)],
                                      stdout=log, stderr=subprocess.STDOUT)
        port = wait_for(lambda: ready.read_text(encoding="ascii").strip()
                        if ready.is_file() and server.poll() is None else None,
                        "test-owned loopback FTP server")
        if not re.fullmatch(r"\d{1,5}", port) or not 1 <= int(port) <= 65535:
            raise AssertionError("The owned FTP fixture returned an invalid port")
        address = f"{protocol}://tester@127.0.0.1:{port}/"
        run_command(["/usr/bin/open", "-n", str(bundle), "--args", "--new-instance",
                     "--data", str(data), "--left", str(source_dir), "--right", str(server_dir)])
        pid = wait_for(lambda: next(iter(app_pids()), None), "isolated Mac FileCat process")
        command = run_command(["/bin/ps", "-p", str(pid), "-o", "command="], seconds=5)
        if not command.startswith(str(executable) + " --new-instance --data " + str(data)):
            raise AssertionError("The active process is not the exact isolated bundle and data profile")

        def nodes():
            state = json.loads(run_command([str(ax_helper), str(executable), str(pid)]))
            if state["visited"] >= 5000:
                raise AssertionError("The bounded native AX tree exceeded its safety limit")
            if state["windows"] < 1:
                return []
            return state["nodes"]

        def one(role, name):
            matches = [node for node in nodes() if node["role"] == role and node["name"] == name]
            if len(matches) != 1:
                raise AssertionError(f"Expected one {role} {name!r}, found {len(matches)}")
            return matches[0]

        def open_remote_copy_review():
            for attempt in range(4):
                rows = [node for node in nodes() if node["role"] == "AXRow" and
                        node["name"] == "target.txt"]
                source_row = min(rows, key=lambda node: node["bounds"][0])
                receipt["steps"].append(run_command([str(ax_setter), str(executable), str(pid),
                                                     "target.txt", "leftmost"]))
                click_node(source_row)
                if attempt:
                    click_node(source_row)  # VoiceOver can consume a click to move its cursor.
                copy_buttons = [node for node in nodes() if node["role"] == "AXButton" and
                                node["name"] == "Copy…"]
                if len(copy_buttons) != 1:
                    raise AssertionError(f"Expected one Copy command, found {len(copy_buttons)}")
                click_node(copy_buttons[0])
                destination = wait_for(lambda: next((node["value"] for node in nodes() if
                                       node["role"] == "AXTextField" and node["name"] == "Destination"), None),
                                       "Copy review destination")
                if destination == address:
                    receipt["steps"].append(f"Copy review opened for exact remote destination on attempt {attempt + 1}")
                    return
                click_ax("AXButton", "Cancel")
                wait_for(lambda: not any(node["role"] == "AXTextField" and node["name"] == "Destination"
                                         for node in nodes()), "wrong-panel Copy review dismissed")
            raise AssertionError("Copy review did not capture the exact test-owned remote destination")

        wait_for(lambda: len([node for node in nodes() if node["role"] == "AXRow" and
                              node["name"] == "target.txt"]) == 2,
                 "both initialized local fixture listings", seconds=30)
        time.sleep(0.5)
        receipt["nativeWindows"] = [{"name": node["name"], "bounds": node["bounds"]}
                                    for node in nodes() if node["role"] == "AXWindow"]
        set_field("Location", address, rightmost=True, enter=True)
        receipt["steps"].append("Remote address entered")
        if protocol == "ftp":
            wait_for(lambda: any(node["name"] == "Connect unencrypted" for node in nodes()),
                     "explicit FTP plaintext consent")
            receipt["steps"].append("Plaintext consent shown")
            click_ax("AXButton", "Connect unencrypted")
            receipt["steps"].append("Plaintext consent clicked")
        wait_for(lambda: any(node["role"] == "AXTextField" and node["name"] == "Password" for node in nodes()),
                 "test server password field")
        one("AXTextField", "Password")
        set_field("Password", "secret-pass")
        receipt["steps"].append("Password entered")
        click_ax("AXButton", "Connect")
        receipt["steps"].append("Connect clicked")
        if protocol == "ftpes":
            wait_for(lambda: any(node["role"] == "AXButton" and node["name"] == "Connect once"
                                 for node in nodes()), "fresh FTPS certificate decision")
            click_ax("AXButton", "Connect once")
            receipt["steps"].append("Fresh FTPS certificate accepted for this connection")
        wait_for(lambda: len([node for node in nodes() if node["role"] == "AXRow" and
                              node["name"] == "target.txt"]) >= 2,
                 "both exact source and remote target rows", seconds=45)
        open_remote_copy_review()
        review_nodes = nodes()
        receipt["reviewControls"] = [{"role": node["role"], "name": node["name"],
                                      "value": node["value"], "bounds": node["bounds"]}
                                     for node in review_nodes if node["name"] and
                                     node["role"] in ("AXButton", "AXCheckBox", "AXPopUpButton",
                                                      "AXDisclosureTriangle", "AXTextField") and
                                     len(node["name"]) < 180]
        options = next((node for node in review_nodes if node["name"] == "Options" and
                        node["role"] in ("AXButton", "AXCheckBox", "AXDisclosureTriangle")), None)
        if options is None:
            raise AssertionError("Native Copy options control is absent")
        click_ax(options["role"], "Options")
        current = nodes()
        receipt["optionsControls"] = [{"role": node["role"], "name": node["name"],
                                       "value": node["value"], "bounds": node["bounds"]}
                                      for node in current if node["role"] in
                                      ("AXButton", "AXCheckBox", "AXPopUpButton", "AXComboBox") and
                                      node["name"] and len(node["name"]) < 180]
        click_ax("AXPopUpButton", "If an item already exists:")
        receipt["steps"].append("Existing-item policy opened")
        receipt["choiceControls"] = [{"role": node["role"], "name": node["name"],
                                      "value": node["value"], "bounds": node["bounds"]}
                                     for node in nodes() if node["role"] in
                                     ("AXMenuItem", "AXListItem", "AXButton", "AXPopUpButton",
                                      "AXComboBox", "AXStaticText") and node["name"] and
                                     ("Replace" in node["name"] or "Skip" in node["name"] or
                                      "Ask each" in node["name"] or "Keep both" in node["name"])]
        if probe:
            receipt["passed"] = True
            return receipt
        choices = [node for node in nodes() if node["role"] == "AXStaticText" and
                   node["name"] == "Replace" and node["bounds"][2] > 8]
        if len(choices) != 1:
            raise AssertionError(f"Expected one exact Replace menu choice, found {len(choices)}")
        click_node(choices[0])
        warning_text = ("Another app can change this remote file after FileCat's final check. "
                        "If you choose Replace, that newer change could be lost. "
                        "Choose Keep both: rename the copy to keep this destination.")
        warning = wait_for(lambda: next((node for node in nodes() if
                           node["name"] == warning_text and node["role"] in
                           ("AXStaticText", "AXTextField")), None), "complete remote Replace warning")
        receipt["warningBounds"] = warning["bounds"]
        receipt["steps"].append("Complete replacement warning visible")
        receipt["warningSpeech"] = wait_for(lambda: (phrase if warning_text in phrase else None)
                                              if (phrase := voiceover_last()) else None,
                                              "complete automatically spoken VoiceOver replacement warning", seconds=10)
        receipt["steps"].append("Complete replacement warning spoken automatically")
        receipt["warningScreenshot"] = capture_screen(validation)
        click_ax("AXButton", "Cancel")
        wait_for(lambda: not any(node["name"] == "Destination" and node["role"] == "AXTextField"
                                 for node in nodes()), "Copy review dismissed without transfer")
        receipt["steps"].append("Replace review canceled with both exact files retained")

        open_remote_copy_review()
        dialog_copies = [node for node in nodes() if node["role"] == "AXButton" and
                         node["name"] == "Copy" and node["bounds"][2] > 8]
        if len(dialog_copies) < 2:
            raise AssertionError("The second Copy review lost its explicit approval action")
        click_node(max(dialog_copies, key=lambda node: node["bounds"][0]))
        receipt["steps"].append("Ask policy submitted for existing remote target")
        decision = wait_for(lambda: next((node for node in nodes() if node["role"] == "AXButton" and
                            node["name"].startswith("Review decision") and node["bounds"][2] > 8), None),
                            "background conflict Review decision action", seconds=45)
        click_node(decision)
        conflict_warning = wait_for(lambda: next((node for node in nodes() if node["name"] == warning_text and
                                    node["role"] in ("AXStaticText", "AXTextField")), None),
                                    "per-item remote replacement warning", seconds=20)
        receipt["conflictWarningBounds"] = conflict_warning["bounds"]
        receipt["conflictSpeech"] = wait_for(lambda: (phrase if warning_text in phrase else None)
                                             if (phrase := voiceover_last()) else None,
                                             "complete automatically spoken per-item warning", seconds=10)
        click_ax("AXButton", "Skip")
        wait_for(lambda: not any(node["role"] == "AXButton" and node["name"] == "Skip"
                                 for node in nodes()), "per-item Skip decision closure")
        receipt["steps"].append("Per-item warning spoken and Skip preserved the remote target")
        receipt["passed"] = True
        return receipt
    except Exception as exc:
        receipt["error"] = f"{type(exc).__name__}: {exc}"
        try:
            receipt["voiceoverLastOnFailure"] = voiceover_last()
        except Exception:
            pass
        if pid is not None and pid in app_pids():
            try:
                receipt["systemEventsFields"] = osa('''
tell application "System Events"
    tell process "FileCat"
        set descriptions to ""
        repeat with w in windows
            set allElements to entire contents of w
            repeat with e in allElements
                try
                    if (role of e as text) is "AXTextField" then
                        set descriptions to descriptions & "[" & (name of e as text) & "/" & (description of e as text) & "]"
                    end if
                end try
            end repeat
        end repeat
        return descriptions
    end tell
end tell
''')
            except Exception:
                pass
            try:
                receipt["failureControls"] = [{"role": node["role"], "name": node["name"],
                                               "value": "<redacted>" if node["name"] == "Password" else node["value"],
                                               "bounds": node["bounds"]}
                                              for node in nodes() if node["role"] in
                                              ("AXTextField", "AXRow") and
                                              len(node["name"]) < 100][:35]
            except Exception:
                pass
        return receipt
    finally:
        if pid is not None:
            subprocess.run(["/bin/kill", "-TERM", str(pid)], capture_output=True, timeout=5)
            deadline = time.monotonic() + 8
            while pid in app_pids() and time.monotonic() < deadline:
                time.sleep(0.2)
            if pid in app_pids():
                subprocess.run(["/bin/kill", "-KILL", str(pid)], capture_output=True, timeout=5)
        if server is not None and server.poll() is None:
            server.terminate()
            try:
                server.wait(timeout=5)
            except subprocess.TimeoutExpired:
                server.kill()
        receipt["sourceUnchanged"] = sha256(source) == receipt["sourceSha256"]
        receipt["targetUnchanged"] = sha256(target) == receipt["targetSha256"]
        receipt["manifest"] = sorted(path.name for path in server_dir.iterdir())
        receipt["passed"] = (receipt["passed"] and receipt["sourceUnchanged"] and
                             receipt["targetUnchanged"] and receipt["manifest"] == ["target.txt"])
        report_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    if len(sys.argv) not in (5, 6) or sys.argv[4] not in ("ftp", "ftpes") or (
            len(sys.argv) == 6 and sys.argv[5] != "--probe"):
        raise SystemExit(__doc__)
    result = run(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]),
                 sys.argv[4], len(sys.argv) == 6)
    raise SystemExit(0 if result["passed"] else 1)
