#!/usr/bin/env python3
"""Measure the archive-writing UI budget in the logged-in Ubuntu desktop (plan §11.8, AT16).

Usage: linux-ui-budget-archive.py HOST_PATH WORKER_PATH REPORT.json [FILES]
HOST_PATH is the published archive qualification host, started with its --ui-budget probe (a 10 ms UI-thread timer that
notes every gap over 50 ms with its time); WORKER_PATH is the published FileCat it starts as the archive workers. Both,
and REPORT.json, must be below ~/FileCatValidation. Mirrors win-ui-budget-archive.ps1: with keys (xdotool, XTest) going
only to the owned FileCat window, the run records UTC windows for idle, navigation (Down/Up, no archive work), pack
(FILES small files packed as TAR + gzip with Down/Up going), open (the archive's listing and write capability built) and
add (F5 of one file: format scan, member review and job, with Tab going), and attributes each over-budget gap to its
window, and to garbage collection when a pause covers most of it. Never sends Enter, Space or Escape while measuring.
"""

import json
import hashlib
import os
from datetime import datetime, timezone
from pathlib import Path
import subprocess
import sys
import time
import traceback
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

BUTTON = ("button", "push button")


def descendants(node):
    if node is None:
        return
    yield node
    for index in range(node.get_child_count()):
        child = node.get_child_at_index(index)
        if child is not None:
            yield from descendants(child)


def showing(node):
    return node.get_state_set().contains(Atspi.StateType.SHOWING)


def wait_for(check, description, seconds=20):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        value = check()
        if value:
            return value
        time.sleep(0.15)
    raise AssertionError(f"Timed out waiting for {description}")


def text_of(node):
    count = Atspi.Text.get_character_count(node)
    return Atspi.Text.get_text(node, 0, count)


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def windows_of(pid):
    listing = subprocess.run(["/usr/bin/wmctrl", "-lp"], capture_output=True, text=True, timeout=5)
    if listing.returncode != 0:
        return []
    return [line.split()[0] for line in listing.stdout.splitlines() if len(line.split()) > 2 and line.split()[2] == str(pid)]


def utc(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)


def main():
    if len(sys.argv) not in (4, 5):
        raise SystemExit(__doc__)
    host_path, worker_path, report_path = (Path(value).resolve() for value in sys.argv[1:4])
    files = int(sys.argv[4]) if len(sys.argv) == 5 else 20000
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    if (host_path.name != "FileCat.ArchiveQualificationHost" or worker_path.name != "FileCat" or
            validation not in host_path.parents or validation not in worker_path.parents or
            report_path.parent != validation or report_path.exists() or files < 1000):
        raise SystemExit("Use a published qualification host and worker below ~/FileCatValidation and a fresh report there")
    for name in ("FileCat", "FileCat.ArchiveQualificationHost"):
        if subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", name[:15]], capture_output=True).returncode == 0:
            raise SystemExit(f"Another {name} instance is running")
    os.environ.setdefault("DISPLAY", ":0")

    fixture = validation / "ArchiveUiBudget" / uuid4().hex
    data, left, right = fixture / "data", fixture / "left", fixture / "right"
    for folder in (data, left, right):
        folder.mkdir(parents=True)
    content = b"x" * 64
    for d in range((files + 199) // 200):
        folder = left / "many" / f"d{d:03}"
        folder.mkdir(parents=True)
        for f in range(min(200, files - d * 200)):
            (folder / f"f{f:03}.txt").write_bytes(content)
    (left / "a.txt").write_text("alpha\n", encoding="ascii")
    probe_path = fixture / "ui-budget.json"
    phases = []
    report = {"passed": False, "stage": "starting", "fixture": str(fixture), "files": files, "error": None,
              "hostSha256": sha256(host_path), "workerSha256": sha256(worker_path), "processors": os.cpu_count()}
    process = None
    try:
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(host_path), "--data", str(data), "--worker", str(worker_path), "--ui-budget", str(probe_path),
                                        "--new-instance", "--left", str(left), "--right", str(right)],
                                       cwd=host_path.parent, stdout=log, stderr=subprocess.STDOUT, env=os.environ.copy())
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((candidate for candidate in
            (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
            if candidate is not None and candidate.get_name() == "FileCat" and
            any(node.get_role_name() == "list" and node.get_name().startswith("Panel 1") for node in descendants(candidate))), None),
            "the qualification host's FileCat window", seconds=60)
        window = wait_for(lambda: (lambda owned: owned[0] if len(owned) == 1 else None)(windows_of(process.pid)), "the owned window")

        def owned():
            active = subprocess.run(["/usr/bin/xdotool", "getactivewindow", "getwindowpid"], capture_output=True, text=True, timeout=5)
            return active.returncode == 0 and active.stdout.strip() == str(process.pid)

        def activate():
            if not owned():
                subprocess.run(["/usr/bin/wmctrl", "-ia", window], check=True, timeout=5)
                time.sleep(0.4)
            if not owned():
                raise AssertionError("The owned FileCat window did not take the foreground; refusing keys")

        def key(chord, pause=0.3):
            activate()
            subprocess.run(["/usr/bin/xdotool", "key", "--clearmodifiers", chord], check=True, timeout=5)
            time.sleep(pause)

        def focused_name():
            for node in descendants(app):
                states = node.get_state_set()
                if states.contains(Atspi.StateType.FOCUSED) and node.get_role_name() in ("list item", "table row", "row", "tree item"):
                    return node.get_name()
            return None

        def focus_row(name):
            key("Home")
            for _ in range(14):
                at = focused_name() or ""
                if at == name or at.startswith(name + ","):
                    return
                key("Down")
            raise AssertionError(f"The cursor never reached {name} (at {focused_name()!r})")

        def named(root, role, name):
            roles = (role,) if isinstance(role, str) else role
            return next((node for node in descendants(root) if node.get_role_name() in roles and node.get_name() == name and showing(node)), None)

        def dialog(title):
            return wait_for(lambda: next((node for node in descendants(app) if node.get_name() == f"{title} dialog" and showing(node)), None),
                            f"the {title} dialog", seconds=30)

        def keep_pressing(mode, until, limit, after=3.0):
            # Down/Up keeps the cursor near where it is and Tab only moves focus; never Enter, Space or Escape.
            start, done, down, presses = time.monotonic(), None, True, 0
            while True:
                now = time.monotonic() - start
                if done is None and until():
                    done = now
                if done is not None and now >= done + after:
                    return presses
                if now > limit:
                    raise AssertionError(f"Timed out after {limit} s in {report['stage']}")
                if mode == "tab":
                    key("Tab", 0.12)
                else:
                    key("Down" if down else "Up", 0.12)
                    down = not down
                presses += 1

        def phase(name, body):
            report["stage"] = name
            start = datetime.now(timezone.utc)
            detail = body()
            phases.append({"name": name, "start": start, "end": datetime.now(timezone.utc), "presses": detail})

        activate()
        wait_for(lambda: probe_path.is_file(), "the UI budget probe", seconds=15)
        phase("idle", lambda: time.sleep(8))
        focus_row("a.txt")
        phase("navigation", lambda: keep_pressing("arrows", lambda: True, 30, after=8))

        focus_row("many")
        key("alt+F5", 1.0)
        create = dialog("Create archive")
        key("Tab", 0.6)  # from the path to the format
        key("Down", 0.6); key("Down", 0.8)  # ZIP, TAR, TAR + gzip
        path_box = wait_for(lambda: named(create, ("text", "entry"), "Archive path"), "the path box")
        tar = Path(text_of(path_box))
        if tar.name != "many.tar.gz":
            raise AssertionError(f"Unexpected archive path: {tar}")
        pack = wait_for(lambda: (lambda node: node if node is not None and node.get_state_set().contains(Atspi.StateType.ENABLED) else None)(
            named(create, BUTTON, "Pack")), "an available Pack button", seconds=30)
        if pack.get_n_actions() < 1 or not pack.do_action(0):
            raise AssertionError("Cannot invoke Pack")
        wait_for(lambda: not any(node.get_name() == "Create archive dialog" and showing(node) for node in descendants(app)),
                 "the Create archive dialog to close")
        phase("pack", lambda: keep_pressing("arrows", lambda: tar.is_file(), 900))
        report["archiveBytes"] = tar.stat().st_size

        key("Tab", 0.6)  # the archive is in the other panel's folder
        focus_row("many.tar.gz")
        key("Return", 0.3)
        phase("open", lambda: keep_pressing("arrows", lambda: any(node.get_role_name() == "list" and "many.tar.gz" in node.get_name() and showing(node)
                                                                   for node in descendants(app)), 300))

        before = sha256(tar)
        key("Tab", 0.6)  # back to the source panel
        focus_row("a.txt")
        key("F5", 0.5)
        dialog("Copy")
        time.sleep(0.8)
        key("Return", 0.3)  # adds a.txt to the archive
        def changed():
            try:
                return sha256(tar) != before
            except OSError:  # replaced or held while it is published: not done yet
                return False
        phase("add", lambda: keep_pressing("tab", changed, 900))

        time.sleep(2.5)  # one more probe report after the last window
        probe = json.loads(probe_path.read_text(encoding="utf-8"))
        report["probe"] = {key_: probe[key_] for key_ in ("Ticks", "OverBudget", "WorstMs", "Histogram")}
        report["phases"] = []
        for item in phases:
            gaps = [gap for gap in probe["Gaps"] if item["start"] <= utc(gap["EndUtc"]) <= item["end"]]
            unexplained = [gap for gap in gaps if gap["GcPauseMs"] < gap["Ms"] / 2]
            report["phases"].append({"name": item["name"], "seconds": round((item["end"] - item["start"]).total_seconds(), 1),
                                     "presses": item["presses"], "overBudget": len(gaps), "unexplained": len(unexplained),
                                     "worstMs": max((gap["Ms"] for gap in gaps), default=None), "gaps": gaps})
        # One gap can be a one-off (a dialog or listing laid out once); repeated unexplained gaps fail the budget.
        report["passed"] = not any(item["name"] in ("pack", "open", "add") and item["unexplained"] > 1 for item in report["phases"])
        report["stage"] = "done"
    except Exception as error:  # The report records what failed; the exit code still fails.
        report["error"] = f"{type(error).__name__}: {error}"
        report["trace"] = traceback.format_exc()[-2000:]
    finally:
        if process is not None and process.poll() is None:
            process.kill()
            process.wait(timeout=10)
        report_path.write_text(json.dumps(report, indent=1, default=str), encoding="utf-8")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
