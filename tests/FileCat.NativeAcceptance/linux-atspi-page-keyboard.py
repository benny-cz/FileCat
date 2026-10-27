#!/usr/bin/env python3
"""Follow the keyboard between the viewer's rendered page and its controls, wherever the pointer is.

Usage: linux-atspi-page-keyboard.py APP_PATH REPORT.json KEY_HELPER CLICK_HELPER
Run in the logged-in GNOME session against an isolated Git-built FileCat below ~/FileCatValidation, with no other
FileCat running. Keys come from linux-uinput-key.py and the clicks from linux-uinput-click.py (XTEST clicks do not
reach X11 windows under GNOME's Xwayland); their guest sudo password comes only through
FILECAT_NATIVE_SUDO_PASSWORD and is removed before FileCat starts. Each step records the X input focus window, the
pointer and the focused accessible, and judges where the keys went from AT-SPI (the Find field's text) and from the
X focus: GTK's 1-pixel focus window in the page's plug, or the viewer's window.

1. F3 on page.md opens the viewer in Page mode: the page has the X focus and its host the accessible focus.
2. With the pointer parked over the page, F7 and "mar" fill Find; the viewer's window has the X focus.
3. Escape gives the keys back to the page; with the pointer over the toolbar, Tab stays in the page.
4. Tab past the page's last item goes on to the viewer's Page button; Shift+Tab comes back to the page.
   With Find open, the page's plug keeps the size of its native host window (not twice it on a 200% desktop).
5. With Find focused, a real click in the page gives it focus and keys: typed letters leave Find unchanged.
6. A real click on the Text button shows the text.
"""

import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
from uuid import uuid4

import gi
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


NAME = "page.md"
CONTENT = ("# Page\n\nSome marker text in a paragraph.\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\nAfter the table.\n").encode()


def run(*command):
    return subprocess.run(list(command), capture_output=True, text=True, timeout=10).stdout.strip()


def geometry(window):
    lines = run("/usr/bin/xwininfo", "-id", window).splitlines()
    value = lambda key: int(next(line.split(":")[1] for line in lines if line.strip().startswith(key)))
    return value("Absolute upper-left X"), value("Absolute upper-left Y"), value("Width:"), value("Height:")


def page_sizes(viewer):
    """The native host's window under the viewer and the page's plug inside it, as width-by-height strings."""
    sizes = [found.group(1) for line in run("/usr/bin/xwininfo", "-tree", "-id", viewer).splitlines()
             if line.strip().startswith("0x") and (found := re.search(r"\s(\d+x\d+)[+-]", line)) and
             found.group(1) != "1x1"]
    return sizes[:2]


def walk(node, depth=0):
    if node is None or depth > 60:
        return
    yield node
    for index in range(node.get_child_count()):
        yield from walk(node.get_child_at_index(index), depth + 1)


def nodes(pid):
    desktop = Atspi.get_desktop(0)
    for index in range(desktop.get_child_count()):
        app = desktop.get_child_at_index(index)
        if app.get_process_id() == pid:
            yield from walk(app)


def focused(pid):
    for node in nodes(pid):
        try:
            if node.get_state_set().contains(Atspi.StateType.FOCUSED):
                return f"{node.get_role_name()} {node.get_name()!r}"
        except Exception:
            pass
    return None


def text_of(pid, name):
    for node in nodes(pid):
        try:
            if node.get_name() == name:
                return Atspi.Text.get_text(node, 0, -1)
        except Exception:
            pass
    return None


def main():
    if len(sys.argv) != 5:
        raise SystemExit(__doc__)
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, key_helper, click_helper = (Path(value).resolve(strict=True) for value in (sys.argv[1], sys.argv[3], sys.argv[4]))
    report_path = Path(sys.argv[2]).resolve()
    if (app_path.name != "FileCat" or validation not in app_path.parents or report_path.parent != validation or
            key_helper.name != "linux-uinput-key.py" or click_helper.name != "linux-uinput-click.py" or
            validation not in key_helper.parents or validation not in click_helper.parents):
        raise SystemExit("Use the isolated FileCat build, the owned input helpers and a report under ~/FileCatValidation")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise SystemExit("A transient guest sudo credential is required for physical test input")
    # The packaged FileCat runs as "filecat".
    for program in ("FileCat", "filecat"):
        if subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", program], capture_output=True).returncode == 0:
            raise SystemExit(f"Another {program} is running; stop it so this test owns the only one")
    fixture = validation / "PageKeyboardFixture" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    (source / NAME).write_bytes(CONTENT)
    app_dll = app_path.with_name("FileCat.dll")
    report = {"passed": False, "appDllSha256": run("/usr/bin/sha256sum", str(app_dll)).split()[0],
              "displayScale": next((line.split(":")[1].strip() for line in run("/usr/bin/gdctl", "show").splitlines()
                                    if "Scale:" in line), None),
              "fixture": str(fixture), "input": "uinput keyboard and absolute pointer", "steps": []}
    failures = []
    process = None

    def sudo(helper, *arguments):
        done = subprocess.run(["sudo", "-S", "/usr/bin/python3", str(helper), *arguments], input=sudo_password + "\n",
                              capture_output=True, text=True, timeout=20)
        if done.returncode:
            raise AssertionError(f"{helper.name} {' '.join(arguments)} failed with exit {done.returncode}")

    try:
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log, stderr=subprocess.STDOUT)

        def windows():
            listing = run("/usr/bin/wmctrl", "-lp").splitlines()
            return [(line.split()[0], " ".join(line.split()[4:])) for line in listing
                    if len(line.split()) > 2 and line.split()[2] == str(process.pid)]

        deadline = time.monotonic() + 40
        while len(windows()) != 1:
            if time.monotonic() > deadline or process.poll() is not None:
                raise AssertionError("The isolated FileCat window did not appear")
            time.sleep(0.25)
        time.sleep(3.0)
        subprocess.run(["/usr/bin/wmctrl", "-ia", windows()[0][0]], check=True, timeout=5)
        time.sleep(0.8)
        screen = [int(line.split(":")[1]) for line in run("/usr/bin/xwininfo", "-root").splitlines()
                  if line.strip().startswith(("Width:", "Height:"))]

        def key(chord, settle=0.9):
            sudo(key_helper, chord)
            time.sleep(settle)

        def click(x, y, settle=1.5):
            sudo(click_helper, str(x), str(y), str(screen[0]), str(screen[1]))
            time.sleep(settle)

        def step(label, expect):
            active = run("/usr/bin/xprop", "-root", "_NET_ACTIVE_WINDOW").split()[-1:]
            entry = {"step": label, "xFocus": hex(int(run("/usr/bin/xdotool", "getwindowfocus", "-f") or 0)),
                     "activeWindow": hex(int(active[0], 16)) if active else None,
                     "pointer": run("/usr/bin/xdotool", "getmouselocation"), "focused": focused(process.pid),
                     "findText": text_of(process.pid, "Find in file")}
            unmet = [name for name, wanted in expect.items() if entry.get(name) != wanted]
            entry.update(expect={name: wanted for name, wanted in expect.items()}, passed=not unmet)
            report["steps"].append(entry)
            if unmet:
                failures.append(f"{label}: {', '.join(f'{name} was {entry.get(name)!r}' for name in unmet)}")

        key("down")
        key("f3", settle=6.0)
        viewer = next((window for window, title in windows() if "Viewer" in title), None)
        if viewer is None:
            raise AssertionError("No viewer window")
        page_keys = next((line.split()[0] for line in run("/usr/bin/xwininfo", "-tree", "-id", viewer).splitlines()
                          if "1x1+-1+-1" in line), None)
        if page_keys is None:
            raise AssertionError("No GTK focus window in the viewer's page")
        page_keys, viewer_keys = hex(int(page_keys, 16)), hex(int(viewer, 16))
        left, top, width, height = geometry(viewer)
        report.update(viewer=viewer_keys, pageFocusWindow=page_keys, viewerGeometry=[left, top, width, height],
                      screen=screen)
        page_point = (left + width // 3, top + int(height * 0.3))
        step("F3 opens the page with its keys", {"xFocus": page_keys, "focused": "panel 'Web page'"})

        subprocess.run(["/usr/bin/xdotool", "mousemove", str(page_point[0]), str(page_point[1])], timeout=5)
        key("f7")
        key("type:mar")
        step("F7 and typing with the pointer over the page fill Find",
             {"xFocus": viewer_keys, "focused": "entry 'Find in file'", "findText": "mar"})
        # With Find open the page's room shrinks: the plug must keep the host's size, not GTK units taken as pixels.
        host_size, plug_size = (page_sizes(viewer) + [None, None])[:2]
        report["pageSizesWithFind"] = {"host": host_size, "plug": plug_size}
        if host_size is None or host_size != plug_size:
            failures.append(f"With Find open the page is {plug_size} in a {host_size} host")
        key("escape")
        step("Escape gives the keys back to the page", {"xFocus": page_keys, "focused": "panel 'Web page'"})
        subprocess.run(["/usr/bin/xdotool", "mousemove", str(left + 40), str(top + 20)], timeout=5)
        key("tab")
        step("Tab with the pointer over the toolbar stays in the page", {"xFocus": page_keys, "focused": "panel 'Web page'"})
        key("tab", settle=1.5)
        step("Tab past the page's last item goes on to the Page button",
             {"xFocus": viewer_keys, "focused": "toggle button 'Page'"})
        key("shift+tab", settle=1.5)
        step("Shift+Tab comes back to the page", {"xFocus": page_keys, "focused": "panel 'Web page'"})

        key("f7")
        click(*page_point)
        step("A click in the page with Find focused gives the page focus and keys",
             {"xFocus": page_keys, "focused": "panel 'Web page'"})
        key("type:zz")
        step("Letters typed after the click leave Find unchanged", {"findText": "mar"})
        click(left + 166, top + 37)
        step("A click on the Text button shows the text", {"focused": "entry 'Text viewer'"})
        if (source / NAME).read_bytes() != CONTENT or any(target.iterdir()):
            raise AssertionError("The journey changed the owned source or destination")
        if failures:
            raise AssertionError("Unmet: " + "; ".join(failures))
        report["passed"] = True
    except Exception as error:
        report["error"] = str(error)
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        report["log"] = "".join(path.read_text(errors="replace")[-1500:] for path in fixture.rglob("filecat.log"))
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
