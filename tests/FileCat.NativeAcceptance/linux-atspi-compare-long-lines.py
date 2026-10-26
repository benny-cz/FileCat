#!/usr/bin/env python3
"""Check long comparison lines and wrap alignment on the Ubuntu desktop.

Usage: linux-atspi-compare-long-lines.py APP REPORT.json
Run only in the isolated, logged-in VM with FILECAT_NATIVE_SUDO_PASSWORD set.
The runner owns its source files and restores the previous clipboard text.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

common = runpy.run_path(str(Path(__file__).with_name("linux-atspi-viewer-find.py")))
descendants = common["descendants"]
find = common["find"]
wait_for = common["wait_for"]
checked_action = common["checked_action"]


def command(*args, input_text=None):
    return subprocess.run(args, input=input_text, text=True, capture_output=True,
                          timeout=8, check=True).stdout


def native_key(helper, chord, password):
    sent = subprocess.run(["sudo", "-S", sys.executable, str(helper), chord],
                          input=password + "\n", text=True, capture_output=True,
                          timeout=15)
    if sent.returncode:
        raise AssertionError(f"Native {chord} key failed: {sent.stderr[-300:]}")


def clipboard(allow_empty=False):
    read = subprocess.run(["/usr/bin/xclip", "-selection", "clipboard", "-out"],
                          text=True, capture_output=True, timeout=8)
    if read.returncode == 0:
        return read.stdout
    if allow_empty and read.returncode == 1:
        return ""
    raise AssertionError(f"Cannot read the isolated desktop clipboard: {read.stderr[-200:]}")


def set_clipboard(value):
    subprocess.run(["/usr/bin/xclip", "-selection", "clipboard", "-in"],
                   input=value.encode(), stdout=subprocess.DEVNULL,
                   stderr=subprocess.DEVNULL, timeout=5, check=True)


def compare_frame(app):
    return next((node for node in descendants(app)
                 if node.get_role_name() == "frame" and
                 (node.get_name() or "").startswith("Compare: compare.txt")), None)


def copy_source_line(frame, sentinel, expected):
    set_clipboard(sentinel)
    checked_action(find(frame, "button", "Copy line"), "Copy line")
    value = wait_for(lambda: (text if (text := clipboard()) != sentinel else None),
                     "the complete copied source line")
    if value != expected:
        raise AssertionError(f"The comparison copied {len(value)} characters, expected "
                             f"{len(expected)} exact source characters")
    return len(value)


def run(app_path):
    password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not password:
        raise AssertionError("A one-time native keyboard sudo credential is required")
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    if app_path.name != "FileCat" or validation not in app_path.parents:
        raise AssertionError("Use a Git-built FileCat app inside the validation folder")
    repo = next(parent for parent in app_path.parents if (parent / ".git").is_dir())
    revision = command("/usr/bin/git", "-C", str(repo), "rev-parse", "HEAD").strip()
    helper = validation / "linux-uinput-key.py"
    if not helper.is_file():
        raise AssertionError("The native keyboard helper is missing")
    root = validation / "CompareLong" / uuid4().hex
    left, right, data = (root / part for part in ("left", "right", "data"))
    for folder in (left, right, data):
        folder.mkdir(parents=True)
    prefix = "x" * 2000
    left_lines = ["first " + prefix + "LEFT", "equal middle", "second " + prefix + "L"]
    right_lines = ["first " + prefix + "RIGHT", "equal middle", "second " + prefix + "R"]
    sources = [(left / "compare.txt", ("\n".join(left_lines) + "\n").encode()),
               (right / "compare.txt", ("\n".join(right_lines) + "\n").encode())]
    for path, content in sources:
        path.write_bytes(content)
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    os.environ.setdefault("DISPLAY", ":0")
    authority = list(Path(os.environ["XDG_RUNTIME_DIR"]).glob(".mutter-Xwaylandauth.*"))
    if len(authority) != 1 or authority[0].stat().st_uid != os.getuid():
        raise AssertionError("The logged-in Xwayland authority is unavailable")
    os.environ["XAUTHORITY"] = str(authority[0])
    original = clipboard(allow_empty=True)
    sdk = validation / "dotnet-sdk"
    if not (sdk / "dotnet").is_file():
        raise AssertionError("The isolated .NET runtime is unavailable")
    os.environ["DOTNET_ROOT"] = str(sdk)
    os.environ["PATH"] = str(sdk) + os.pathsep + os.environ.get("PATH", "")
    result = {"passed": False, "revision": revision, "fixture": str(root),
              "appSha256": hashlib.sha256(app_path.read_bytes()).hexdigest()}
    process = None
    try:
        with (root / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(left), "--right", str(right)],
                                       cwd=root, env=os.environ.copy(), stdout=log,
                                       stderr=subprocess.STDOUT)
            desktop = Atspi.get_desktop(0)
            app = wait_for(lambda: next((candidate for candidate in
                (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                if candidate.get_name() == "FileCat" and
                find(candidate, "list", f"Panel 1, source, {left}")), None),
                "exact owned FileCat source panel", seconds=20)
            if app.get_process_id() != process.pid:
                raise AssertionError("The accessible app is not the exact test process")
            main = next((parts[0] for line in command("/usr/bin/wmctrl", "-lp").splitlines()
                         if (parts := line.split(maxsplit=4)) and len(parts) >= 5
                         and parts[2] == str(process.pid) and "FileCat" in parts[4]), None)
            if not main:
                raise AssertionError("The owned FileCat main window is absent")
            command("/usr/bin/wmctrl", "-ia", main)
            listing = find(app, "list", f"Panel 1, source, {left}")
            row = wait_for(lambda: next((node for node in descendants(listing)
                                         if node.get_role_name() == "list item" and
                                         ((node.get_name() or "") == "compare.txt" or
                                          (node.get_name() or "").startswith("compare.txt, file,"))), None),
                           "exact compare.txt source row", seconds=20)
            if not row.grab_focus():
                raise AssertionError("Cannot focus the test-owned source file")
            native_key(helper, "ctrl+i", password)
            frame = wait_for(lambda: compare_frame(app), "owned Compare window", seconds=20)
            title = frame.get_name()
            window = next((parts[0] for line in command("/usr/bin/wmctrl", "-lp").splitlines()
                           if (parts := line.split(maxsplit=4)) and len(parts) == 5
                           and parts[2] == str(process.pid) and parts[4] == title), None)
            if not window:
                raise AssertionError(f"The native Compare window is absent: {title!r}")
            result["compareTitle"] = title
            result["accessibleControls"] = [(node.get_role_name(), node.get_name())
                                            for node in descendants(frame)
                                            if node.get_name() and node.get_role_name() in
                                            ("button", "check box", "list")][:60]
            wait_for(lambda: find(frame, "button", "Copy line"), "Copy line button")
            result["firstCopiedLength"] = copy_source_line(frame, "FileCat first sentinel", left_lines[0])
            native_key(helper, "f8", password)
            result["secondCopiedLength"] = copy_source_line(frame, "FileCat second sentinel", left_lines[2])
            # A 640-DIP frame makes the toolbar wrap. The aligned text rows must
            # continue to expose both source lines after Wrap is enabled.
            command("/usr/bin/wmctrl", "-ir", window, "-b", "remove,maximized_vert,maximized_horz")
            command("/usr/bin/wmctrl", "-ir", window, "-e", "0,8,8,640,600")
            wrap = wait_for(lambda: find(frame, "check box", "Wrap long source lines in the aligned comparison"),
                            "comparison Wrap control")
            checked_action(wrap, "Wrap long lines")
            if not wrap.get_state_set().contains(Atspi.StateType.CHECKED):
                raise AssertionError("Wrap long lines did not become selected")
            result["wrappedCopiedLength"] = copy_source_line(frame, "FileCat wrap sentinel", left_lines[2])
            result["wrapChecked"] = True
            if any(path.read_bytes() != content for path, content in sources):
                raise AssertionError("Comparison changed an exact source file")
            result["sourceBytesUnchanged"] = True
            result["passed"] = True
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        set_clipboard(original)
    if result["passed"]:
        if root.parent.resolve(strict=True) != (validation / "CompareLong").resolve(strict=True):
            raise AssertionError("Refusing to clean a fixture outside CompareLong")
        shutil.rmtree(root)
    return result


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    report = Path(sys.argv[2]).resolve()
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    if report.parent != validation or report.exists():
        raise SystemExit("Use a fresh report inside FileCatValidation")
    try:
        result = run(Path(sys.argv[1]))
    except Exception as error:
        result = {"passed": False, "error": f"{type(error).__name__}: {error}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
