#!/usr/bin/env python3
"""Check the tar launcher with GNOME's real parser and spawned argv oracle.

Run on a Linux desktop with PyGObject/Gio and desktop-file-validate. Fixtures and
results are retained in a new evidence directory; no user menu entry is replaced.
"""
import argparse
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import time

from gi.repository import Gio


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--helper", type=pathlib.Path, required=True)
    parser.add_argument("--evidence-dir", type=pathlib.Path, required=True)
    args = parser.parse_args()
    if args.evidence_dir.exists():
        parser.error("evidence directory already exists")
    args.evidence_dir.mkdir(mode=0o700)
    names = ["simple", "with spaces", "with & ampersand", 'with "double" and \'single\' quotes',
             "with %f and 100%", "with $ and `", "with \\ backslash", "with | ; # ( )",
             "žluťoučký UTF-8", "with\ttab\nand newline", "trailing newline\n", "equals=name"]
    results = []
    for i, name in enumerate(names):
        case_root = args.evidence_dir / str(i)
        app_dir = case_root / name
        app_dir.mkdir(parents=True)
        helper = app_dir / "install-desktop-entry.sh"
        shutil.copyfile(args.helper, helper)
        helper.chmod(0o700)
        executable = app_dir / "FileCat"
        executable.write_text(
            "#!/usr/bin/python3\nimport json,pathlib,sys\n"
            "pathlib.Path(sys.argv[0]).with_name('launch-argv.json').write_text(json.dumps(sys.argv))\n")
        executable.chmod(0o700)
        icon = app_dir / "filecat.png"
        icon.touch()
        (app_dir / "filecat.desktop").write_text(
            "[Desktop Entry]\nType=Application\nName=FileCat\nExec=filecat %F\nIcon=filecat\nTerminal=false\n")
        selected = case_root / 'selected & % " žluťoučký.txt'
        selected.touch()
        menu = case_root / "isolated menu"
        env = {**os.environ, "XDG_DATA_HOME": str(menu)}
        result = {"case": name, "passed": False}
        try:
            installed = subprocess.run([str(helper)], env=env, capture_output=True, text=True)
            result["helper_exit"] = installed.returncode
            if installed.returncode:
                raise RuntimeError(installed.stderr)
            entry = menu / "applications/filecat.desktop"
            validated = subprocess.run(["desktop-file-validate", str(entry)], capture_output=True, text=True)
            result["validator_exit"] = validated.returncode
            result["validator_output"] = validated.stdout + validated.stderr
            if validated.returncode:
                raise RuntimeError("native desktop validation failed")
            app = Gio.DesktopAppInfo.new_from_filename(str(entry))
            parsed_icon = app.get_icon()
            if not isinstance(parsed_icon, Gio.FileIcon) or parsed_icon.get_file().get_path() != str(icon):
                raise RuntimeError("native icon path differs")
            if not app.launch([Gio.File.new_for_path(str(selected))], None):
                raise RuntimeError("native launch failed")
            captured = app_dir / "launch-argv.json"
            deadline = time.monotonic() + 5
            while not captured.exists() and time.monotonic() < deadline:
                time.sleep(0.05)
            argv = json.loads(captured.read_text())
            result["actual_argv"] = argv
            if argv != [str(executable), str(selected)]:
                raise RuntimeError("native spawned executable/arguments differ")
            result["passed"] = True
        except Exception as error:
            result["error"] = str(error)
        results.append(result)
        print(json.dumps(result, ensure_ascii=False), flush=True)
    manifest = {"helper_sha256": hashlib.sha256(args.helper.read_bytes()).hexdigest(), "results": results,
                "passed": sum(r["passed"] for r in results), "total": len(results)}
    (args.evidence_dir / "results.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2))
    print(f"{manifest['passed']}/{manifest['total']} passed")
    return 0 if all(r["passed"] for r in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
