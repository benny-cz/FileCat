#!/usr/bin/env python3
"""Run the controlled native Page failure harness on the logged-in Ubuntu desktop.

Usage: linux-page-engine-fault-launch.py HARNESS.dll REPORT.json
The report and harness must be inside the isolated FileCatValidation checkout.
"""

import os
import json
from pathlib import Path
import runpy
import subprocess
import sys


def main(harness: Path, report: Path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    harness = harness.resolve(strict=True)
    if (harness.name != "PageEngineFaultHarness.dll" or validation not in harness.parents or
            report.parent != validation or report.exists()):
        raise AssertionError("Use an isolated harness and fresh validation report")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Close other FileCat processes before this native engine check")
    repo = next(parent for parent in harness.parents if (parent / ".git").exists())
    environment = os.environ.copy()
    environment.update(runpy.run_path(str(repo /
        "tests/FileCat.NativeAcceptance/linux-atspi-ftp-large-listing.py"))["desktop_environment"]())
    environment["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    completed = subprocess.run([str(validation / "dotnet-sdk/dotnet"), str(harness), str(report)],
                               env=environment, timeout=60, check=False)
    if completed.returncode != 0 or not report.exists():
        return completed.returncode or 1
    return 0 if json.loads(report.read_text(encoding="utf-8")).get("passed") is True else 1


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    raise SystemExit(main(Path(sys.argv[1]), Path(sys.argv[2])))
