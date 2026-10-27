#!/usr/bin/env python3
"""Hold an owned rename-target stat while testing the live native dialog.

Usage: linux-atspi-rename-slow-check.py APP_PATH REPORT.json KEY_HELPER
Requires the logged-in Ubuntu VMware desktop, strace and a Git-built FileCat in
~/FileCatValidation. FILECAT_NATIVE_SUDO_PASSWORD is consumed only by the
physical-key helper and is removed from the FileCat environment.
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

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


shared = runpy.run_path(str(Path(__file__).with_name("linux-atspi-dialog-pointer-dismiss.py")))
descendants = shared["descendants"]
named = shared["named"]
wait_for = shared["wait_for"]
activate_window = shared["activate_window"]


def one(root, role, name):
    matches = [node for node in descendants(root) if node.get_role_name() == role and
               (node.get_name() == name or
                (role == "list item" and node.get_name().startswith(name + ", file,")))]
    if len(matches) != 1:
        raise AssertionError(f"Expected one {role} {name!r}; found {len(matches)}")
    return matches[0]


def modal_button(root, name):
    matches = []
    for button in named(root, "button", name):
        parent = button
        for _ in range(16):
            parent = parent.get_parent() if parent is not None else None
            if parent is None:
                break
            if parent.get_name() == "Bulk rename dialog":
                matches.append(button)
                break
    if len(matches) != 1:
        raise AssertionError(f"Expected one Bulk rename dialog button {name!r}; found {len(matches)}")
    return matches[0]


def main(app_path, report_path, key_helper):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    key_helper = key_helper.resolve(strict=True)
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            key_helper.name != "linux-uinput-key.py" or validation not in key_helper.parents or
            report_path.parent.resolve() != validation):
        raise AssertionError("Use the Git-built app, owned input helper and isolated report")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise AssertionError("The physical-key helper needs transient guest authentication")
    os.environ["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")

    fixture = validation / "RenameSlowCheckFixture" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    original = {"a.txt": b"alpha\n", "b.txt": b"beta\n",
                "slow_1.txt": b"occupied sentinel\n"}
    for name, content in original.items():
        (source / name).write_bytes(content)
    delayed_path = source / "slow_1.txt"
    trace = fixture / "target-stat.log"
    result = {"passed": False, "platform": "Ubuntu", "fixture": str(fixture),
              "appDllSha256": hashlib.sha256(app_path.with_name("FileCat.dll").read_bytes()).hexdigest(),
              "delayedPath": str(delayed_path), "steps": []}
    process = tracer = None
    try:
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([
                str(app_path), "--new-instance", "--data", str(data),
                "--left", str(source), "--right", str(target),
            ], cwd=fixture, env=os.environ.copy(), stdout=log,
                stderr=subprocess.STDOUT, start_new_session=True)
            pid = process.pid
            desktop = Atspi.get_desktop(0)
            app = wait_for(lambda: next((candidate for candidate in
                (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                if candidate.get_name() == "FileCat" and
                named(candidate, "list", f"Panel 1, source, {source}")), None),
                "isolated source panel", seconds=30)

            def send(chord):
                activate_window(pid)
                sent = subprocess.run(["sudo", "-S", sys.executable, str(key_helper), chord],
                                      input=sudo_password + "\n", capture_output=True,
                                      text=True, timeout=15)
                if sent.returncode:
                    raise AssertionError(f"Physical {chord} failed: {sent.stderr[-300:]}")

            try:
                row = wait_for(lambda: next((node for node in descendants(app)
                    if node.get_role_name() == "list item" and
                    node.get_name().startswith("a.txt, file,")), None),
                               "a.txt row", seconds=25)
            except AssertionError:
                result["startupTree"] = [(node.get_role_name(), node.get_name())
                                         for node in descendants(app)
                                         if node.get_role_name() in ("list", "list item", "label")][-80:]
                result["appExit"] = process.poll()
                raise
            if not row.grab_focus():
                raise AssertionError("Could not focus a.txt")
            send("insert")
            wait_for(lambda: one(app, "list item", "b.txt").get_state_set().contains(
                Atspi.StateType.FOCUSED), "b.txt focus")
            send("insert")
            wait_for(lambda: all(one(app, "list item", name).get_state_set().contains(
                Atspi.StateType.SELECTED) for name in ("a.txt", "b.txt")), "exact two marks")
            send("ctrl+m")
            wait_for(lambda: named(app, "entry", "Name mask") and named(app, "button", "Rename"),
                     "Bulk rename dialog")
            mask = one(app, "entry", "Name mask")
            rename = modal_button(app, "Rename")
            tracer = subprocess.Popen([
                "sudo", "-S", "/usr/bin/strace", "-f", "-p", str(pid),
                "-o", str(trace), "-e", "trace=%file",
                "-e", "inject=lstat:delay_enter=8s", "-P", str(delayed_path),
            ], stdin=subprocess.PIPE, stdout=subprocess.DEVNULL,
                stderr=(fixture / "strace.err").open("wb"), start_new_session=True)
            tracer.stdin.write((sudo_password + "\n").encode())
            tracer.stdin.flush()
            tracer.stdin.close()
            wait_for(lambda: any(line.startswith("TracerPid:") and int(line.split()[1]) > 0
                                 for line in Path(f"/proc/{pid}/status").read_text().splitlines()),
                     "isolated FileCat strace attachment", seconds=10)
            if not mask.get_editable_text().set_text_contents("slow_[C]"):
                raise AssertionError("Could not enter the slow rename rule")
            wait_for(lambda: not rename.get_state_set().contains(Atspi.StateType.ENABLED),
                     "approval disabled during slow check")
            # Allow the dialog's 150 ms rule debounce to enter the existence
            # check before replacing the rule. The isolated target is the only
            # instrumented path.
            time.sleep(0.6)
            # The occupied target's stat is held by strace in the same app process.
            # The newer rule must publish while the old call remains delayed.
            start = time.monotonic()
            if not mask.get_editable_text().set_text_contents("fast_[C]"):
                raise AssertionError("Could not replace the delayed rule")
            wait_for(lambda: rename.get_state_set().contains(Atspi.StateType.ENABLED) and
                     any(node.get_name().startswith("2 of 2 names change")
                         for node in descendants(app)), "newest mapping approval", seconds=8)
            elapsed = time.monotonic() - start
            if elapsed >= 7.5:
                raise AssertionError(f"New rule waited for held stat: {elapsed:.3f}s")
            detail = one(app, "entry", "Full old and new paths of selected rename")
            expected = f"From: {source / 'a.txt'}\nTo: {source / 'fast_1.txt'}"
            wait_for(lambda: Atspi.Text.get_text(detail.get_text_iface(), 0, -1) == expected,
                     "latest full-path mapping")
            result["newPreviewSeconds"] = round(elapsed, 3)
            result["steps"].append("New rule and exact mapping became approvable before held stat returned")

            wait_for(lambda: "DELAYED" in trace.read_text(errors="replace") and
                     str(delayed_path) in trace.read_text(errors="replace"),
                     "instrumented target stat completion", seconds=15)
            if (Atspi.Text.get_text(detail.get_text_iface(), 0, -1) != expected or
                    not rename.get_state_set().contains(Atspi.StateType.ENABLED)):
                raise AssertionError("Old target result replaced the newest preview")
            result["steps"].append("Old delayed stat completed without stale preview publication")
            send("escape")
            wait_for(lambda: not named(app, "entry", "Name mask"), "Cancel closed review", seconds=5)
            if ({entry.name for entry in source.iterdir()} != set(original) or
                    any((source / name).read_bytes() != value for name, value in original.items()) or
                    list(target.iterdir())):
                raise AssertionError("Cancel changed source bytes or target")
            result["steps"].append("Physical Escape canceled without a rename or changed bytes")
            result["passed"] = True
    except Exception as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
    finally:
        if tracer is not None and tracer.poll() is None:
            os.killpg(tracer.pid, signal.SIGTERM)
            try:
                tracer.wait(timeout=6)
            except subprocess.TimeoutExpired:
                os.killpg(tracer.pid, signal.SIGKILL)
                tracer.wait(timeout=6)
        if process is not None and process.poll() is None:
            os.killpg(process.pid, signal.SIGTERM)
            try:
                process.wait(timeout=6)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait(timeout=6)
        if trace.is_file():
            lines = [line.strip() for line in trace.read_text(errors="replace").splitlines()
                     if str(delayed_path) in line or "lstat resumed" in line]
            result["delayedStatCalls"] = lines[-5:]
        result["sourceSha256"] = {name: hashlib.sha256((source / name).read_bytes()).hexdigest()
                                  for name in original if (source / name).is_file()}
        result["targetNames"] = sorted(entry.name for entry in target.iterdir())
        report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    raise SystemExit(main(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])))
