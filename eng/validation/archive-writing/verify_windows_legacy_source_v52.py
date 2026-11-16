#!/usr/bin/env python3
"""Verify the Windows v52 read-only legacy edit source checkpoint from copied VM files."""

import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

SOURCE = (
    "src/FileCat.Core/Edit/EditSessions.cs",
    "src/FileCat.Core/Edit/UnixLegacyEditSource.cs",
    "src/FileCat.Platform.Windows/Archives/WindowsArchiveDirectory.cs",
    "src/FileCat.Platform.Windows/Archives/WindowsArchiveFile.cs",
    "src/FileCat.Platform.Windows/Archives/WindowsArchiveNative.cs",
    "src/FileCat.Platform.Windows/Archives/WindowsLegacyEditSource.cs",
    "src/FileCat.Platform.Windows/Shell/LocalIconFileLease.cs",
    "tests/FileCat.Platform.Windows.Tests/WindowsLegacyEditSourceTests.cs",
)
BINARIES = ("FileCat.Core.dll", "FileCat.Platform.Windows.dll", "FileCat.Platform.Windows.Tests.dll")
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inventory(path):
    return {"bytes": path.stat().st_size, "sha256": digest(path)}


def results(path, expected):
    xml = ET.parse(path)
    rows = xml.findall(".//t:UnitTestResult", NS)
    tally = {outcome: sum(row.attrib.get("outcome") == outcome for row in rows)
             for outcome in ("Passed", "NotExecuted", "Failed")}
    assert tally == expected, (path, tally, expected)
    counters = xml.find(".//t:Counters", NS)
    assert counters is not None
    assert int(counters.attrib["total"]) == len(rows)
    assert int(counters.attrib["passed"]) == tally["Passed"]
    assert int(counters.attrib["failed"]) == 0
    return rows, tally


def main():
    evidence = Path(sys.argv[1]).resolve(strict=True)
    root = Path(__file__).resolve().parents[3]
    files = {}
    for relative in SOURCE:
        current = root / relative
        guest = evidence / "source" / relative
        assert current.is_file() and guest.is_file() and digest(current) == digest(guest), relative
        files["source/" + relative] = inventory(guest)
    for name in BINARIES:
        path = evidence / name
        assert path.is_file() and 10000 < path.stat().st_size < 30_000_000, name
        files[name] = inventory(path)
    raw_build = (evidence / "build.log").read_bytes()
    build = raw_build.decode("utf-16" if raw_build.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
    assert "Build succeeded." in build and "0 Warning(s)" in build and "0 Error(s)" in build
    assert json.loads((evidence / "standard-launch.json").read_text(encoding="utf-8-sig"))["ExitCode"] == 0
    focused, counts1 = results(evidence / "focused-v51.trx", {"Passed": 10, "NotExecuted": 0, "Failed": 0})
    adjacent, counts2 = results(evidence / "adjacent-v52.trx", {"Passed": 168, "NotExecuted": 139, "Failed": 0})
    assert all("WindowsLegacyEditSourceTests" in row.attrib["testName"] for row in focused)
    assert sum(row.attrib["outcome"] == "Passed" and "WindowsLegacyEditSourceTests" in row.attrib["testName"]
               for row in adjacent) == 10
    for name in ("build.log", "standard-launch.json", "test.log", "focused-v51.trx", "adjacent-v52.trx"):
        files[name] = inventory(evidence / name)
    manifest = {"platform": "Windows 11 VM", "account": "fcarchive42829297",
                "scope": "read-only legacy source admission and adjacent native regression",
                "focused": counts1, "adjacent": counts2, "files": files}
    (evidence / "verified-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(json.dumps({"focused": counts1, "adjacent": counts2, "source_files": len(SOURCE),
                      "guest_binaries": len(BINARIES), "manifest_sha256": digest(evidence / "verified-manifest.json")}))


if __name__ == "__main__":
    main()
