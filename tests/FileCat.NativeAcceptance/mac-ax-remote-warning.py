#!/usr/bin/env python3
"""Inspect and hear an isolated Mac remote replacement review.

Usage: mac-ax-remote-warning.py BUNDLE.app REPORT.json FTP_HELPER ftp|ftpes|ftps|sftp [--probe|--narrow|--pointer-probe|--pointer-narrow|--pointer-race-probe|--ax-probe|--ax-narrow|--change-during-upload|--change-content-during-upload|--disconnect-during-upload|--disconnect-and-speak]
The probes end at the expanded Copy options; pointer and AX modes skip keyboard panel switching.
Narrow tests both reviews at 640 × 480 points.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import socket
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
'''.replace("ROLE", json.dumps(role)).replace("NAME", json.dumps(name, ensure_ascii=False))
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


def dismiss_account_notice():
    # A Mac lab account notice can float above the app for minutes and hide
    # the very warning this image must prove. Dismiss only its explicit Later
    # action, never a FileCat decision or a different system prompt.
    return run_command(["/usr/bin/osascript", "-e", '''
tell application "System Events"
    if not (exists process "UserNotificationCenter") then return "absent"
    tell process "UserNotificationCenter"
        repeat with noticeWindow in windows
            if (exists button "Later" of noticeWindow) and ¬
               ((name of every button of noticeWindow as text) contains "Account Settings") then
                click button "Later" of noticeWindow
                return "dismissed"
            end if
        end repeat
    end tell
end tell
return "absent"
'''])


def capture_screen(validation):
    helper = validation / "FileCatCapture.app"
    if not helper.is_dir():
        return None
    dismiss_account_notice()
    before = set(validation.glob("native-screen-*.png"))
    run_command(["/usr/bin/open", "-a", str(helper)], seconds=30)
    return wait_for(lambda: next((str(path) for path in
                    sorted(set(validation.glob("native-screen-*.png")) - before,
                           key=lambda path: path.stat().st_mtime_ns, reverse=True)
                    if path.stat().st_size > 100_000), None), "owned Mac warning screenshot", seconds=15)


def resize_window(width, height):
    raw = osa(f'''
tell application "System Events"
    tell process "FileCat"
        set size of window 1 to {{{width}, {height}}}
        return size of window 1
    end tell
end tell
''')
    size = [int(value) for value in re.findall(r"\d+", raw)]
    if len(size) != 2 or any(abs(actual - requested) > 2 for actual, requested in zip(size, (width, height))):
        raise AssertionError(f"The native review refused {width} × {height}: {raw!r}")
    return size


def run(bundle, report_path, ftp_helper, protocol, probe, narrow, pointer_probe, ax_route,
        pointer_focus_race, change_during_upload=False, same_metadata_change=False,
        disconnect_during_upload=False, speak_issue=False):
    trace_launch = os.environ.get("FILECAT_POINTER_TRACE", "")
    trace_mode = trace_launch in ("1", "open")
    disconnect_during_upload = disconnect_during_upload or speak_issue
    if (change_during_upload or same_metadata_change or disconnect_during_upload) and protocol == "sftp":
        raise ValueError("Upload failure acceptance requires an owned FTP/FTPS server")
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    ftp_helper = ftp_helper.resolve(strict=True)
    sftp_helper = Path(__file__).with_name("mac-sftp-owned-selftest.py").resolve(strict=True)
    report_path = report_path.resolve()
    executable = bundle / "Contents/MacOS/FileCat"
    if (bundle.parent != validation or bundle.suffix != ".app" or not executable.is_file() or
            report_path.parent != validation or ftp_helper.name != "win-ftp-owned-stage.py" or
            ftp_helper.parent != validation or sftp_helper.parent != validation):
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
    source.write_bytes((b"owned incoming content\n" * 50_000)[:1_000_000]
                       if change_during_upload or same_metadata_change or disconnect_during_upload
                       else b"owned incoming content\n")
    target.write_bytes(b"owned existing remote content\n")
    receipt = {"passed": False, "probe": probe, "narrow": narrow,
               "changeDuringUpload": change_during_upload or same_metadata_change,
               "sameMetadataChange": same_metadata_change,
               "disconnectDuringUpload": disconnect_during_upload,
               "pointerTraceEnabled": trace_mode, "pointerTraceLaunch": trace_launch,
               "pointerProbe": pointer_probe and probe, "axRoute": ax_route,
               "pointerFocusRace": pointer_focus_race,
               "protocol": protocol, "bundle": str(bundle),
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
    race_helper = fixture / "ax-pointer-focus-race"
    if pointer_focus_race:
        run_command(["/usr/bin/swiftc", "-sdk", str(sdk),
                     str(Path(__file__).with_name("mac-ax-pointer-focus-race.swift")),
                     "-o", str(race_helper)], seconds=60)
    server = None
    pid = None
    try:
        ready = fixture / "server.ready"
        if protocol == "sftp":
            ssh = json.loads(run_command([sys.executable, str(sftp_helper), "prepare",
                                          str(fixture), str(server_dir)]))
            if Path(ssh["fixture"]) != fixture or Path(ssh["serverDir"]) != server_dir:
                raise AssertionError("The owned SFTP preparation escaped the exact fixture")
            receipt["hostFingerprint"] = ssh["hostFingerprint"]
            receipt["sshKeyFile"] = ssh["clientKey"]
            port = str(ssh["port"])
            with (fixture / "sshd.log").open("wb") as log:
                server = subprocess.Popen(["/usr/sbin/sshd", "-D", "-e", "-f", ssh["config"]],
                                          stdout=log, stderr=subprocess.STDOUT)
            def listening():
                if server.poll() is not None:
                    raise AssertionError("The owned SFTP server exited before connection")
                with socket.socket() as probe_socket:
                    probe_socket.settimeout(0.2)
                    return probe_socket.connect_ex(("127.0.0.1", int(port))) == 0
            wait_for(listening, "test-owned loopback SFTP server", seconds=10)
        else:
            with (fixture / "server.log").open("wb") as log:
                server = subprocess.Popen([sys.executable, str(ftp_helper),
                                           {"ftpes": "serve-tls", "ftps": "serve-implicit-tls"}.get(protocol, "serve"),
                                           str(server_dir), str(ready)],
                                          stdout=log, stderr=subprocess.STDOUT)
            port = wait_for(lambda: ready.read_text(encoding="ascii").strip()
                            if ready.is_file() and server.poll() is None else None,
                            "test-owned loopback FTP server")
        if not re.fullmatch(r"\d{1,5}", port) or not 1 <= int(port) <= 65535:
            raise AssertionError("The owned remote fixture returned an invalid port")
        address = (f"sftp://benny@127.0.0.1:{port}{server_dir}/" if protocol == "sftp" else
                   f"{protocol}://tester@127.0.0.1:{port}/")
        arguments = ["--new-instance", "--data", str(data),
                     "--left", str(source_dir), "--right", str(server_dir)]
        if trace_launch == "1":
            with (fixture / "app-console.log").open("wb") as log:
                subprocess.Popen([str(executable), *arguments], cwd=executable.parent,
                                 env={**os.environ, "FILECAT_MENU_TRACE": "1"},
                                 stdout=log, stderr=subprocess.STDOUT)
        elif trace_launch == "open":
            launched = subprocess.run(["/usr/bin/open", "-n", str(bundle), "--args", *arguments],
                                      capture_output=True, text=True, timeout=15,
                                      env={**os.environ, "FILECAT_MENU_TRACE": "1"})
            if launched.returncode:
                raise AssertionError(launched.stderr[-1200:] or "Could not open traced app")
        else:
            run_command(["/usr/bin/open", "-n", str(bundle), "--args", *arguments])
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
            for attempt in range(6):
                rows = [node for node in nodes() if node["role"] == "AXRow" and
                        node["name"] == "target.txt"]
                source_row = min(rows, key=lambda node: node["bounds"][0])
                source_tab = next((node for node in nodes() if node["role"] == "AXButton" and
                                   node["name"].startswith("source, ") and
                                   node["name"].endswith(", " + str(source_dir)) and
                                   node["bounds"][2] > 8), None)
                if source_tab is None or os.environ.get("FILECAT_FORCE_KEYBOARD_SOURCE") == "1":
                    # macOS AX sometimes retains the exact visible tab but reports a
                    # zero-sized frame after an FTPS certificate sheet closes. Use
                    # FileCat's native panel switch and End key in that case.
                    source_tab_unframed = next((node for node in nodes() if
                        node["role"] == "AXButton" and node["name"].startswith("source, ") and
                        node["name"].endswith(", " + str(source_dir))), None)
                    if source_tab_unframed is None:
                        raise AssertionError("The exact local source tab is unavailable")
                    osa('''tell application "System Events" to tell process "FileCat"
    set frontmost to true
    key code 48
    key code 119
end tell''')
                    wait_for(lambda: any(node["role"] == "AXWindow" and
                             node["name"].startswith("source — FileCat") for node in nodes()),
                             "keyboard activation of the exact local source panel", seconds=8)
                    click_ax("AXButton", "Copy…")
                    destination = wait_for(lambda: next((node["value"] for node in nodes() if
                        node["role"] == "AXTextField" and node["name"] == "Destination"), None),
                        "keyboard-route Copy review destination", seconds=6)
                    if destination.rstrip("/") != address.rstrip("/"):
                        raise AssertionError("Keyboard-route Copy review changed the destination")
                    receipt["steps"].append("Zero-frame AX tab bypassed with native Tab and End")
                    return
                if ax_route:
                    click_ax("AXButton", source_tab["name"])
                    wait_for(lambda: any(node["role"] == "AXWindow" and
                                 node["name"].startswith("source — FileCat") for node in nodes()),
                             "AX activation of the exact local source panel", seconds=4)
                    receipt["steps"].append("Local source panel activated through its AX tab action")
                else:
                    for activation in range(4):
                        receipt.setdefault("sourcePointerAttempts", []).append({
                            "tabBounds": source_tab["bounds"],
                            "frontmostBefore": osa('tell application "System Events" to get name of first process whose frontmost is true'),
                        })
                        if pointer_focus_race and activation == 0:
                            x, y, width, height = source_tab["bounds"]
                            receipt["staleFocusRequest"] = run_command([
                                str(race_helper), str(executable), str(pid),
                                str(x + width / 2), str(y + height / 2)], seconds=30)
                            # Allow native AX and VoiceOver focus notifications to settle before
                            # reading the title; an immediate sample could miss the late bounce.
                            time.sleep(0.35)
                        else:
                            click_node(source_tab)
                        receipt["sourcePointerAttempts"][-1]["pointerAfter"] = run_command(
                            ["/opt/homebrew/bin/cliclick", "p"])
                        try:
                            wait_for(lambda: any(node["role"] == "AXWindow" and
                                         node["name"].startswith("source — FileCat") for node in nodes()),
                                     "pointer activation of the exact local source panel", seconds=1)
                            receipt["sourcePointerAttempts"][-1]["activated"] = True
                            receipt["steps"].append(f"Local source panel activated on tab click {activation + 1}")
                            break
                        except AssertionError:
                            receipt["sourcePointerAttempts"][-1]["activated"] = False
                            if pointer_focus_race:
                                raise AssertionError("The source panel lost the injected stale AX focus race")
                            if activation == 3:
                                hit_source = Path(__file__).with_name("mac-ax-hit-test.swift")
                                hit_helper = fixture / "mac-ax-hit-test"
                                if hit_source.is_file():
                                    try:
                                        run_command(["/usr/bin/swiftc", "-framework", "ApplicationServices",
                                                     str(hit_source), "-o", str(hit_helper)], seconds=60)
                                    except Exception as exc:
                                        receipt["sourceHitCompileError"] = str(exc)
                                if hit_helper.is_file():
                                    try:
                                        x, y, width, height = source_tab["bounds"]
                                        receipt["sourceTabNativeHit"] = run_command([
                                            str(hit_helper), str(pid),
                                            str(x + width / 2), str(y + height / 2)], seconds=30)
                                    except Exception as exc:
                                        receipt["sourceTabNativeHitError"] = str(exc)
                                click_node(source_row)
                                if hit_helper.is_file():
                                    try:
                                        x, y, width, height = source_row["bounds"]
                                        receipt["sourceRowNativeHit"] = run_command([
                                            str(hit_helper), str(pid),
                                            str(x + width / 2), str(y + height / 2)], seconds=30)
                                    except Exception as exc:
                                        receipt["sourceRowNativeHitError"] = str(exc)
                                receipt["sourceRowPointerActivated"] = bool(
                                    next((node for node in nodes() if node["role"] == "AXWindow" and
                                          node["name"].startswith("source — FileCat")), None))
                                raise AssertionError("The exact local source tab did not activate its panel by pointer")
                receipt["steps"].append(run_command([str(ax_setter), str(executable), str(pid),
                                                     "target.txt", "leftmost"]))
                click_node(source_row)
                if attempt:
                    click_node(source_row)  # VoiceOver can consume a click to move its cursor.
                try:
                    copy_buttons = wait_for(lambda: [node for node in nodes() if
                                            node["role"] == "AXButton" and node["name"] == "Copy…" and
                                            node["enabled"]], "enabled native Copy action", seconds=3)
                except AssertionError:
                    receipt["steps"].append(f"Copy remained disabled after source activation attempt {attempt + 1}")
                    continue
                if len(copy_buttons) != 1:
                    raise AssertionError(f"Expected one enabled Copy command, found {len(copy_buttons)}")
                click_ax("AXButton", "Copy…")
                try:
                    destination = wait_for(lambda: next((node["value"] for node in nodes() if
                                           node["role"] == "AXTextField" and node["name"] == "Destination"), None),
                                           "Copy review destination", seconds=6)
                except AssertionError:
                    continue
                if destination.rstrip("/") == address.rstrip("/"):
                    receipt["steps"].append(f"Copy review opened for exact remote destination on attempt {attempt + 1}")
                    return
                receipt["steps"].append(f"Copy review opened for unexpected destination: {destination}")
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
        if protocol == "sftp":
            main_window = next(node for node in nodes() if node["role"] == "AXWindow" and
                               node["bounds"][2] >= 600)
            panel_midpoint = main_window["bounds"][0] + main_window["bounds"][2] / 2
            connect_buttons = [node for node in nodes() if node["role"] == "AXButton" and
                               node["name"] == "Connect to a server…" and node["bounds"][2] > 8 and
                               node["bounds"][0] >= panel_midpoint]
            if len(connect_buttons) != 1:
                raise AssertionError("The right panel's native Connect command is unavailable")
            click_node(connect_buttons[0])
            wait_for(lambda: any(node["role"] == "AXTextField" and node["name"] == "Server"
                                 for node in nodes()), "fresh SFTP connection form")
            set_field("Server", "127.0.0.1")
            set_field("Port", port)
            set_field("User name", "benny")
            click_ax("AXPopUpButton", "Sign in with")
            key_choice = wait_for(lambda: next((node for node in nodes() if
                                  node["name"] == "Private key file" and
                                  node["role"] == "AXStaticText" and node["bounds"][2] > 8), None),
                                  "native private-key sign-in choice")
            click_node(key_choice)
            set_field("Private key file", ssh["clientKey"])
            set_field("Start folder", str(server_dir))
            click_ax("AXButton", "Connect")
            receipt["steps"].append("Public-key-only SFTP profile submitted")
            wait_for(lambda: any("New server key" in node["name"] for node in nodes()),
                     "new SFTP host-key review")
            if not any(ssh["hostFingerprint"] in node["name"] for node in nodes()):
                raise AssertionError("SFTP host-key prompt omitted the independent test fingerprint")
            trust = [node for node in nodes() if node["role"] == "AXButton" and
                     node["name"] == "Connect once" and node["bounds"][2] > 8]
            if len(trust) != 1:
                raise AssertionError(f"Expected one visible SFTP Connect once action, found {len(trust)}")
            click_node(trust[0])
            try:
                wait_for(lambda: not any(node["role"] == "AXButton" and node["name"] == "Connect once"
                                         for node in nodes()), "SFTP host-key prompt closure", seconds=3)
            except AssertionError:
                click_node(trust[0])  # VoiceOver may consume the first pointer click.
                wait_for(lambda: not any(node["role"] == "AXButton" and node["name"] == "Connect once"
                                         for node in nodes()), "SFTP host-key prompt closure", seconds=10)
            receipt["steps"].append("Independent SFTP host fingerprint matched before Connect once")
        else:
            set_field("Location", address, rightmost=True, enter=True)
            receipt["steps"].append("Remote address entered")
        if protocol == "ftp":
            wait_for(lambda: any(node["name"] == "Connect unencrypted" for node in nodes()),
                     "explicit FTP plaintext consent")
            receipt["steps"].append("Plaintext consent shown")
            click_ax("AXButton", "Connect unencrypted")
            receipt["steps"].append("Plaintext consent clicked")
        if protocol != "sftp":
            wait_for(lambda: any(node["role"] == "AXTextField" and node["name"] == "Password" for node in nodes()),
                     "test server password field")
            one("AXTextField", "Password")
            set_field("Password", "secret-pass")
            receipt["steps"].append("Password entered")
            click_ax("AXButton", "Connect")
            receipt["steps"].append("Connect clicked")
            if protocol in ("ftpes", "ftps"):
                wait_for(lambda: any(node["role"] == "AXButton" and node["name"] == "Connect once"
                                     for node in nodes()), "fresh FTPS certificate decision")
                click_ax("AXButton", "Connect once")
                receipt["steps"].append("Fresh FTPS certificate accepted for this connection")
        wait_for(lambda: len([node for node in nodes() if node["role"] == "AXRow" and
                              node["name"] == "target.txt"]) >= 2,
                 "both exact source and remote target rows", seconds=45)
        if protocol == "sftp" and not pointer_probe and not ax_route:
            # After the host-key dialog, macOS may leave the right Location box
            # editing the new address. Escape returns to that list; Tab then
            # uses FileCat's own panel-switch command to reach the source.
            osa('''tell application "System Events" to tell process "FileCat"
    set frontmost to true
    key code 53
    key code 48
end tell''')
            wait_for(lambda: any(node["role"] == "AXWindow" and
                                 node["name"].startswith("source — FileCat") for node in nodes()),
                     "Mac keyboard return to the exact local source panel", seconds=8)
            receipt["steps"].append("Esc then Tab returned focus from SFTP Location to the local source panel")
        open_remote_copy_review()
        if narrow:
            main = next((node for node in nodes() if node["role"] == "AXWindow" and
                         node["bounds"][2] >= 600), None)
            if main is None:
                raise AssertionError("The original native window geometry is unavailable")
            receipt["originalReviewSize"] = main["bounds"][2:4]
            receipt["narrowReviewSize"] = resize_window(640, 480)
            receipt["steps"].append("Copy review narrowed to 640 × 480 Mac points")
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
        if change_during_upload or same_metadata_change or disconnect_during_upload:
            receipt["warningScreenshot"] = capture_screen(validation)
            set_field("Speed limit (MB/s):", "0.0625")
            copies = [node for node in nodes() if node["role"] == "AXButton" and
                      node["name"] == "Copy" and node["bounds"][2] > 8]
            if not copies:
                raise AssertionError("The upload Copy approval is absent")
            click_node(max(copies, key=lambda node: node["bounds"][0]))

            def partial_stage():
                stages = [path for path in server_dir.iterdir() if path.is_file() and
                          path.name.startswith(".fc-") and 0 < path.stat().st_size < source.stat().st_size]
                return stages[0] if len(stages) == 1 else None

            stage = wait_for(partial_stage, "partial owned upload stage", seconds=20)
            receipt["stagedBytesBeforeChange"] = stage.stat().st_size
            if disconnect_during_upload:
                staged_prefix = stage.read_bytes()
                if not source.read_bytes().startswith(staged_prefix):
                    raise AssertionError("The owned remote stage does not contain the exact source prefix")
                receipt["stageName"] = stage.name
                server.terminate()
                server.wait(timeout=5)
                decision = wait_for(lambda: next((node for node in nodes() if
                    node["role"] == "AXButton" and node["name"].startswith("Review decision") and
                    node["bounds"][2] > 8), None), "dropped-connection Review decision", seconds=30)
                click_node(decision)
                wait_for(lambda: any(node["role"] == "AXButton" and node["name"] == "Skip"
                                     for node in nodes()), "dropped-connection Skip choice", seconds=10)
                click_ax("AXButton", "Skip")
                wait_for(lambda: not any(node["role"] == "AXButton" and node["name"] == "Skip"
                                         for node in nodes()), "dropped-connection decision closure")
                click_ax("AXButton", "Details (Ctrl+J)")
                receipt["retainedStageIssue"] = wait_for(lambda: next((node["name"] for node in nodes() if
                    "partial copy is left on the server" in node["name"].lower()), None),
                    "visible retained-stage warning in operation details", seconds=10)
                if not receipt["retainedStageIssue"].startswith("Warning: A partial copy is left on the server") or (
                        f"Path: /{stage.name}" not in receipt["retainedStageIssue"] or
                        "JobIssue {" in receipt["retainedStageIssue"]):
                    raise AssertionError("The operation issue must expose a concise warning and its complete stage path")
                if speak_issue:
                    candidates = [node for node in nodes() if node["name"] == receipt["retainedStageIssue"]]
                    receipt["issueSpeechCandidates"] = [{"role": node["role"], "bounds": node["bounds"]}
                                                        for node in candidates]
                    issue_row = next((node for node in candidates if node["bounds"][2] > 8 and
                                      node["bounds"][3] > 8), None)
                    if issue_row is None:
                        raise AssertionError("The visible operation issue row has no usable Mac accessibility bounds")
                    click_node(issue_row)
                    receipt["issueSpeech"] = wait_for(lambda: (phrase if stage.name in phrase and
                        "partial copy is left on the server" in phrase.lower() else None)
                        if (phrase := voiceover_last()) else None,
                        "VoiceOver speech for the selected retained-stage issue", seconds=12)
                receipt["expectedManifest"] = sorted([stage.name, target.name])
                receipt["stageBytesAfterDrop"] = stage.stat().st_size
                if not 0 < receipt["stageBytesAfterDrop"] < source.stat().st_size:
                    raise AssertionError("The retained stage is not a partial upload")
                if not source.read_bytes().startswith(stage.read_bytes()):
                    raise AssertionError("The retained stage no longer matches the source prefix")
                receipt["steps"].append("Server stopped mid-upload; Skip retained only the exact unpublished stage")
                receipt["passed"] = True
                return receipt
            if same_metadata_change:
                prior = target.stat()
                changed = b"N" * prior.st_size
                if changed == target.read_bytes():
                    raise AssertionError("The new content must differ from the approved destination")
                target.write_bytes(changed)
                os.utime(target, ns=(prior.st_atime_ns, prior.st_mtime_ns))
                current = target.stat()
                if current.st_size != prior.st_size or current.st_mtime_ns != prior.st_mtime_ns:
                    raise AssertionError("The controlled content change did not retain size and timestamp")
                receipt["mutationKind"] = "same size and timestamp, different content"
            else:
                target.write_bytes(b"newer contents from another client\n")
                receipt["mutationKind"] = "changed metadata and content"
            receipt["targetSha256"] = sha256(target)
            receipt["steps"].append("Another client changed the exact target during the rate-limited upload")
            wait_for(lambda: not any(path.name.startswith(".fc-") for path in server_dir.iterdir()),
                     "failed upload stage cleanup", seconds=45)
            if sha256(target) != receipt["targetSha256"] or sha256(source) != receipt["sourceSha256"]:
                raise AssertionError("The rejected replacement changed the newer target or incoming source")
            receipt["cleanupIssue"] = wait_for(lambda: next((node["name"] for node in nodes() if
                "destination changed on the server" in node["name"] or
                "destination content changed on the server" in node["name"]), None),
                "visible changed-target refusal", seconds=12)
            if same_metadata_change and "destination content changed on the server" not in receipt["cleanupIssue"]:
                raise AssertionError("The refusal did not identify the changed destination content")
            receipt["steps"].append("The changed target was retained; stage was removed and refusal was visible")
            receipt["passed"] = True
            return receipt
        receipt["warningSpeech"] = wait_for(lambda: (phrase if warning_text in phrase else None)
                                              if (phrase := voiceover_last()) else None,
                                              "complete automatically spoken VoiceOver replacement warning", seconds=10)
        receipt["steps"].append("Complete replacement warning spoken automatically")
        receipt["warningScreenshot"] = capture_screen(validation)
        if narrow:
            window = next((node for node in nodes() if node["role"] == "AXWindow" and
                           abs(node["bounds"][2] - 640) <= 2), None)
            cancel = next((node for node in nodes() if node["role"] == "AXButton" and
                           node["name"] == "Cancel" and node["bounds"][2] > 8), None)
            if window is None or cancel is None:
                raise AssertionError("Narrow warning review lost its native window or Cancel action")
            approve = next((node for node in nodes() if node["role"] == "AXButton" and
                            node["name"] == "Copy" and node["bounds"][2] > 8 and
                            abs(node["bounds"][1] - cancel["bounds"][1]) <= 5), None)
            if approve is None:
                raise AssertionError("Narrow warning review lost its Copy approval action")
            wx, wy, ww, wh = window["bounds"]
            for name, action in (("Cancel", cancel), ("Copy", approve)):
                x, y, w, h = action["bounds"]
                if not (wx <= x and wy <= y and x + w <= wx + ww and y + h <= wy + wh):
                    raise AssertionError(f"The narrow review's {name} action left the window")
            receipt["narrowCancelBounds"] = cancel["bounds"]
            receipt["narrowCopyBounds"] = approve["bounds"]
            resize_window(*[round(value) for value in receipt["originalReviewSize"]])
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
        if narrow:
            receipt["narrowConflictSize"] = resize_window(640, 480)
            conflict_nodes = nodes()
            window = next((node for node in conflict_nodes if node["role"] == "AXWindow" and
                           abs(node["bounds"][2] - 640) <= 2), None)
            if window is None:
                raise AssertionError("The narrow per-item conflict review lost its native window")
            wx, wy, ww, wh = window["bounds"]
            for name in ("Skip", "Cancel operation"):
                action = next((node for node in conflict_nodes if node["role"] == "AXButton" and
                               node["name"] == name and node["bounds"][2] > 8), None)
                if action is None:
                    raise AssertionError(f"The narrow per-item conflict lost {name}")
                x, y, w, h = action["bounds"]
                if not (wx <= x and wy <= y and x + w <= wx + ww and y + h <= wy + wh):
                    raise AssertionError(f"The narrow per-item conflict's {name} action left the window")
                receipt[f"narrowConflict{name.replace(' ', '')}Bounds"] = action["bounds"]
            receipt["conflictScreenshot"] = capture_screen(validation)
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
                                               "bounds": node["bounds"], "enabled": node["enabled"],
                                               "selected": node["selected"], "focused": node["focused"]}
                                              for node in nodes() if node["role"] in
                                              ("AXWindow", "AXTextField", "AXRow", "AXButton", "AXStaticText") and
                                              len(node["name"]) < 180][:100]
            except Exception:
                pass
            try:
                receipt["failureScreenshot"] = capture_screen(validation)
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
        if trace_mode:
            log_file = data / "local/diagnostics/filecat.log"
            if log_file.is_file():
                receipt["pointerTrace"] = [line for line in log_file.read_text(
                    encoding="utf-8", errors="replace").splitlines() if "MenuTrace" in line and
                    ("pointer-press" in line or "active-panel=" in line or
                     "text-focus" in line)][-200:]
        receipt["passed"] = (receipt["passed"] and receipt["sourceUnchanged"] and
                             receipt["targetUnchanged"] and receipt["manifest"] ==
                             receipt.get("expectedManifest", ["target.txt"]))
        report_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    if len(sys.argv) not in (5, 6) or sys.argv[4] not in ("ftp", "ftpes", "ftps", "sftp") or (
            len(sys.argv) == 6 and sys.argv[5] not in ("--probe", "--narrow", "--pointer-probe", "--pointer-narrow", "--pointer-race-probe", "--ax-probe", "--ax-narrow", "--change-during-upload", "--change-content-during-upload", "--disconnect-during-upload", "--disconnect-and-speak")):
        raise SystemExit(__doc__)
    result = run(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]),
                 sys.argv[4], len(sys.argv) == 6 and sys.argv[5] in ("--probe", "--pointer-probe", "--pointer-race-probe", "--ax-probe"),
                 len(sys.argv) == 6 and sys.argv[5] in ("--narrow", "--pointer-narrow", "--ax-narrow"),
                 len(sys.argv) == 6 and sys.argv[5] in ("--pointer-probe", "--pointer-narrow", "--pointer-race-probe"),
                 len(sys.argv) == 6 and sys.argv[5] in ("--ax-probe", "--ax-narrow"),
                 len(sys.argv) == 6 and sys.argv[5] == "--pointer-race-probe",
                 len(sys.argv) == 6 and sys.argv[5] == "--change-during-upload",
                 len(sys.argv) == 6 and sys.argv[5] == "--change-content-during-upload",
                 len(sys.argv) == 6 and sys.argv[5] == "--disconnect-during-upload",
                 len(sys.argv) == 6 and sys.argv[5] == "--disconnect-and-speak")
    raise SystemExit(0 if result["passed"] else 1)
