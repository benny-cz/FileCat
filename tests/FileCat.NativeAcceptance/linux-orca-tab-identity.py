#!/usr/bin/env python3
"""Same-named local, archive and remote tabs, their lock modes, a tab drag and a clipped name, with Orca.

Usage: linux-orca-tab-identity.py APP_PATH REPORT.json KEY_HELPER CLICK_HELPER
Run in the logged-in GNOME session against an isolated Git-built FileCat below ~/FileCatValidation, with no other Orca
or FileCat running. FILECAT_NATIVE_SUDO_PASSWORD (physical input) and FILECAT_NATIVE_SFTP_PASSWORD (the VM's own
loopback OpenSSH test account) are taken from the environment and removed before Orca or FileCat starts; the SFTP
host key must match the server's own public keys. Spoken claims are judged from Orca's SPEECH OUTPUT.

1. Panel 1 gets three tabs all named "reports": a local folder, the folder of that name inside bundle.zip, and one on
   loopback SFTP. Their headers carry distinct cues and their accessible names the full location (sftp://user@host).
2. A right click on tab 1 gives it a navigation lock and on tab 2 a root lock; their names carry the markers and the
   lock meanings.
3. Ctrl+Tab cycles the three tabs and Orca speaks "Tab N of 3" with each tab's header, lock meaning and location.
4. The three names do not all fit, so the strip is scrolled to keep the remote tab in sight and most of the first tab
   is hidden: a pointer drag of the remote tab held at the strip's left end scrolls it back, and dropped there the
   remote tab goes first.
5. In the local tab a long file name is clipped: hovering its Name cell shows the whole name in a tooltip, and
   Ctrl+Shift+I opens every full value, which Orca reads.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import time
from uuid import uuid4
import zipfile

import gi
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

HERE = Path(__file__).resolve().parent
speech_runner = runpy.run_path(str(HERE / "linux-orca-speech.py"))
SpeechLog, ORCA_WRAPPER = speech_runner["SpeechLog"], speech_runner["ORCA_WRAPPER"]
base = runpy.run_path(str(HERE / "linux-atspi-ftp-large-listing.py"))
wait_for, tree, matching, action, desktop_environment, stop = (
    base[name] for name in ("wait_for", "tree", "matching", "action", "desktop_environment", "stop"))
LONG = ("quarterly-report-with-a-name-long-enough-to-be-clipped-in-any-narrow-name-column-of-a-panel-"
        "kept-for-inspection.txt")


def showing(node):
    try:
        return node.get_state_set().contains(Atspi.StateType.SHOWING)
    except Exception:
        return False


def tabs(app):
    # The tab strip's tabs are buttons named "<header>, <lock meaning>, <full location>".
    found = [node for node in tree(app) if node.get_role_name() in ("page tab", "button") and showing(node)
             and "reports" in (node.get_name() or "") and
             any(lock in (node.get_name() or "") for lock in ("Unlocked tab", "Navigation lock", "Root lock"))]
    return sorted(found, key=lambda node: node.get_extents(Atspi.CoordType.SCREEN).x)


def center(node):
    box = node.get_extents(Atspi.CoordType.SCREEN)
    return box.x + box.width // 2, box.y + box.height // 2


def tab_strip(tab):
    # The tabs' list sits in the strip's scroll viewer, whose extents are the part of the strip that shows.
    node = tab.get_parent()
    while node is not None and node.get_role_name() != "list":
        node = node.get_parent()
    viewer = node.get_parent() if node is not None else None
    if viewer is None:
        raise AssertionError("The tab strip's scroll viewer is not exposed")
    return viewer.get_extents(Atspi.CoordType.SCREEN)


def row_named(app, name):
    return next((node for node in tree(app) if node.get_role_name() == "list item" and showing(node)
                 and (node.get_name() or "").startswith(name + ",")), None)


def main():
    if len(sys.argv) != 5:
        raise SystemExit(__doc__)
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, key_helper, click_helper = (Path(v).resolve(strict=True) for v in (sys.argv[1], sys.argv[3], sys.argv[4]))
    report_path = Path(sys.argv[2]).resolve()
    if (app_path.name != "FileCat" or validation not in app_path.parents or report_path.parent != validation or
            key_helper.name != "linux-uinput-key.py" or click_helper.name != "linux-uinput-click.py" or
            validation not in key_helper.parents or validation not in click_helper.parents or report_path.exists()):
        raise SystemExit("Use the isolated FileCat build, the owned input helpers and a fresh report under ~/FileCatValidation")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    sftp_password = os.environ.pop("FILECAT_NATIVE_SFTP_PASSWORD", None)
    if not sudo_password or not sftp_password:
        raise SystemExit("Transient input and SFTP test-account credentials are required")
    for program in ("orca", "FileCat", "filecat"):
        if subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", program], capture_output=True).returncode == 0:
            raise SystemExit(f"Another {program} is running; stop it so this test owns the only one")
    root = validation / "TabIdentityFixture" / uuid4().hex
    local, server, target, data = (root / part for part in ("local", "server", "target", "data"))
    (local / "reports").mkdir(parents=True)
    (server / "reports").mkdir(parents=True)
    for folder in (target, data):
        folder.mkdir(parents=True)
    (local / "reports" / "short.txt").write_bytes(b"short\n")
    (local / "reports" / LONG).write_bytes(b"clipped name\n")
    (server / "reports" / "remote.txt").write_bytes(b"remote\n")
    with zipfile.ZipFile(local / "bundle.zip", "w") as archive:
        archive.writestr("reports/inner.txt", "inside the archive\n")
    hashes = {str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest()
              for path in sorted(root.rglob("*")) if path.is_file()}
    keys = runpy.run_path(str(validation / "linux-sftp-replace-fixture.py"))["fingerprints"]()
    os.environ.update(desktop_environment())
    screen = [int(line.split(":")[1]) for line in subprocess.run(["/usr/bin/xwininfo", "-root"], capture_output=True,
              text=True, timeout=5).stdout.splitlines() if line.strip().startswith(("Width:", "Height:"))]
    report = {"passed": False, "stage": "starting", "fixture": str(root), "screen": screen, "steps": [],
              "appDllSha256": hashlib.sha256(app_path.with_name("FileCat.dll").read_bytes()).hexdigest()}
    speech = SpeechLog(root / "orca-debug.log")
    orca = process = None

    def key(chord, settle=0.8):
        done = subprocess.run(["sudo", "-S", "/usr/bin/python3", str(key_helper), chord], input=sudo_password + "\n",
                              capture_output=True, text=True, timeout=20)
        if done.returncode:
            raise AssertionError(f"Physical {chord} failed")
        time.sleep(settle)

    def pointer(x, y, *mode, settle=1.0):
        done = subprocess.run(["sudo", "-S", "/usr/bin/python3", str(click_helper), str(x), str(y), str(screen[0]),
                               str(screen[1]), *[str(m) for m in mode]], input=sudo_password + "\n",
                              capture_output=True, text=True, timeout=30)
        if done.returncode:
            raise AssertionError(f"Pointer {mode or 'click'} at {x},{y} failed: {done.stderr[-200:]}")
        time.sleep(settle)

    def step(label, value, heard=None):
        report["steps"].append({"step": label, "value": value, "heard": heard})
        return value

    try:
        orca_env = {**os.environ, "GDK_BACKEND": "wayland", "WAYLAND_DISPLAY": "wayland-0"}
        orca = subprocess.Popen(["/usr/bin/python3", "-c", ORCA_WRAPPER, str(speech.path)], env=orca_env,
                                stdout=subprocess.DEVNULL, stderr=(root / "orca.err").open("wb"))
        wait_for(lambda: any("Screen reader on" in phrase for phrase in speech.take()), "Orca", 25)
        with (root / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data), "--left",
                                        str(local / "reports"), "--right", str(target)], cwd=root,
                                       env=os.environ.copy(), stdout=log, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                                     if node is not None and node.get_process_id() == process.pid
                                     and node.get_name() == "FileCat"), None), "test-owned FileCat", 40)
        time.sleep(3)
        listing = subprocess.run(["/usr/bin/wmctrl", "-lp"], capture_output=True, text=True, timeout=5).stdout
        window = next(line.split()[0] for line in listing.splitlines() if len(line.split()) > 2 and line.split()[2] == str(process.pid))
        subprocess.run(["/usr/bin/wmctrl", "-ir", window, "-b", "add,maximized_vert,maximized_horz"], timeout=5)
        subprocess.run(["/usr/bin/wmctrl", "-ia", window], check=True, timeout=5)
        time.sleep(1.5)

        # 1. Three tabs named "reports".
        report["stage"] = "archive tab"
        wait_for(lambda: row_named(app, "short.txt"), "the local listing")
        key("ctrl+t")
        key("backspace", 1.5)
        bundle = wait_for(lambda: row_named(app, "bundle.zip"), "bundle.zip in the parent folder")
        bundle.grab_focus()
        time.sleep(0.5)
        key("enter", 2.0)
        inner = wait_for(lambda: row_named(app, "reports"), "reports inside bundle.zip")
        inner.grab_focus()
        time.sleep(0.5)
        key("enter", 2.0)
        wait_for(lambda: row_named(app, "inner.txt"), "the archive's reports folder")
        report["stage"] = "remote tab"
        key("ctrl+t")
        location = wait_for(lambda: next(iter(matching(app, "entry", "Location")), None), "the left Location field")
        address = f"sftp://benny@127.0.0.1{server}/reports"
        if not location.grab_focus() or not location.get_editable_text().set_text_contents(address):
            raise AssertionError("Could not enter the loopback SFTP address")
        key("enter", 2.0)
        password = wait_for(lambda: next(iter(matching(app, "password text", "Password") + matching(app, "entry", "Password")), None),
                            "the SFTP password field")
        if not password.get_editable_text().set_text_contents(sftp_password):
            raise AssertionError("Could not enter the SFTP test-account password")
        action(wait_for(lambda: next(iter((matching(app, "button", "Connect") + matching(app, "push button", "Connect"))), None), "Connect"), "Connect")
        wait_for(lambda: any("New server key" in (node.get_name() or "") for node in tree(app) if showing(node)), "the host-key decision")
        shown = {node.get_name() for node in tree(app) if showing(node)}
        matched = sorted(set(keys) & shown)
        if not matched:
            raise AssertionError("The SFTP host key did not match the server's own public keys")
        report["hostKeyMatched"] = matched
        action(wait_for(lambda: next(iter((matching(app, "button", "Connect once") + matching(app, "push button", "Connect once"))), None), "Connect once"), "Connect once")
        wait_for(lambda: row_named(app, "remote.txt"), "the remote listing", 40)
        try:
            found = wait_for(lambda: tabs(app) if len(tabs(app)) == 3 else None, "three reports tabs", 15)
        except AssertionError:
            report["candidates"] = [(node.get_role_name(), node.get_name()) for node in tree(app)
                                    if showing(node) and "reports" in (node.get_name() or "")][:40]
            raise
        names = step("three same-named tabs", [tab.get_name() for tab in found])
        cues = {"local": "reports · local", "archive": "reports · bundle.zip", "remote": "sftp://benny@127.0.0.1"}
        for kind, cue in cues.items():
            if not any(cue in name for name in names):
                raise AssertionError(f"No tab name carries the {kind} cue {cue!r}: {names}")
        if len(set(names)) != 3:
            raise AssertionError(f"Tab names are not distinct: {names}")

        # 2. Lock modes from the tab menu.
        report["stage"] = "locks"
        for index, item in ((0, "Lock tab (navigation opens new tabs)"), (1, "Lock tab at root (returns here when revisited)")):
            tab = tabs(app)[index]
            pointer(*center(tab), "right", settle=1.2)
            action(wait_for(lambda: next(iter(matching(app, "menu item", item)), None), item, 10), item)
            time.sleep(1.0)
        locked = step("locked tab names", [tab.get_name() for tab in tabs(app)])
        if not (locked[0].startswith("🔒 ") and "Navigation lock" in locked[0]):
            raise AssertionError(f"Tab 1 lacks the navigation lock: {locked[0]!r}")
        if not (locked[1].startswith("⌂ ") and "Root lock" in locked[1]):
            raise AssertionError(f"Tab 2 lacks the root lock: {locked[1]!r}")

        # 3. Ctrl+Tab with Orca: each tab's listing says where it is; focusing each tab says its lock.
        report["stage"] = "Ctrl+Tab"
        speech.take()
        heard = []
        for _ in range(3):
            key("ctrl+tab", 1.5)
            heard += speech.take()
        step("Ctrl+Tab through the tabs", None, heard)
        spoken = " ".join(heard)
        for marker in ("Tab ", "local/reports", "bundle.zip/reports", "sftp://benny@127.0.0.1", "Navigation lock", "Root lock"):
            if marker not in spoken:
                raise AssertionError(f"Orca did not speak {marker!r} while switching tabs: {heard}")

        # 4. Drag the remote tab to the first place, holding it at the strip's left end while the strip scrolls back.
        report["stage"] = "drag"
        order = tabs(app)
        remote = next(tab for tab in order if "sftp://benny@127.0.0.1" in tab.get_name())
        strip = tab_strip(order[0])
        first = order[0].get_extents(Atspi.CoordType.SCREEN)
        start = center(remote)
        drop = (strip.x + 8, start[1])
        report["drag"] = {"from": start, "to": drop, "strip": [strip.x, strip.y, strip.width, strip.height],
                          "firstTab": [first.x, first.y, first.width, first.height], "firstTabPartlyHidden": first.x < strip.x}
        pointer(start[0], start[1], "drag", drop[0], drop[1], 2, settle=1.5)
        after = step("tab order after the drag", [tab.get_name() for tab in tabs(app)])
        if "sftp://benny@127.0.0.1" not in after[0]:
            raise AssertionError(f"The remote tab did not move first: {after}")

        # 5. A clipped name: hover and keyboard inspection.
        report["stage"] = "clipped name"
        local_tab = next(tab for tab in tabs(app) if "reports · local" in tab.get_name())
        pointer(*center(local_tab), settle=1.5)
        row = wait_for(lambda: row_named(app, LONG), "the long-name row")
        box = row.get_extents(Atspi.CoordType.SCREEN)
        pointer(box.x + min(120, box.width // 4), box.y + box.height // 2, "move", 3, settle=0)
        tip = [node.get_name() for node in tree(app) if showing(node) and node.get_role_name() in ("tool tip", "label", "window")
               and LONG in (node.get_name() or "") and not (node.get_name() or "").startswith(LONG + ",")]
        step("hover tooltip", tip)
        if not tip:
            raise AssertionError("Hovering the clipped name showed no tooltip with the whole name")
        row = row_named(app, LONG)
        row.grab_focus()
        time.sleep(0.6)
        speech.take()
        key("ctrl+shift+i", 2.0)
        heard = speech.take()
        texts = [node.get_name() for node in tree(app) if showing(node) and LONG in (node.get_name() or "")]
        step("Ctrl+Shift+I full values", texts, heard)
        if not any(LONG in phrase for phrase in heard):
            raise AssertionError(f"Orca did not read the full name from the inspection: {heard}")
        key("escape", 1.0)

        changed = [name for name, digest in hashes.items()
                   if hashlib.sha256((root / name).read_bytes()).hexdigest() != digest]
        if changed or any(target.iterdir()):
            raise AssertionError(f"The journey changed fixture files: {changed}")
        report["passed"] = True
        report["stage"] = "done"
    except Exception as error:
        report["error"] = str(error)
    finally:
        stop(process)
        if orca is not None and orca.poll() is None:
            orca.terminate()
            try:
                orca.wait(timeout=5)
            except subprocess.TimeoutExpired:
                orca.kill()
        report_path.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
