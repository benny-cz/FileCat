#!/usr/bin/env python3
"""Seal the test-only fuzz harness without substituting host product binaries."""

import hashlib
import json
from pathlib import Path
import tarfile
import sys


def main():
    root = Path(__file__).resolve().parents[3]
    project = Path(__file__).resolve().parent / "ArchiveFuzzHarness"
    output = project / "bin" / "Release" / "net10.0"
    packet = Path(sys.argv[1]).resolve()
    if packet.exists():
        raise FileExistsError(packet)
    if not packet.parent.is_dir():
        raise FileNotFoundError(packet.parent)
    names = ("FileCat.ArchiveFuzzHarness.dll", "FileCat.ArchiveFuzzHarness.pdb",
             "FileCat.ArchiveFuzzHarness.deps.json", "FileCat.ArchiveFuzzHarness.runtimeconfig.json",
             "SharpFuzz.dll", "SharpFuzz.Common.dll", "dnlib.dll")
    source = ("Program.cs", "ArchiveFuzzHarness.csproj", "packages.lock.json", "toolchain.json")
    contents = [(output / n, "harness/" + n) for n in names] + [(project / n, "source/" + n) for n in source]
    for path, _ in contents:
        if not path.is_file():
            raise FileNotFoundError(path)
    with tarfile.open(packet, "x:gz") as tar:
        for path, name in contents:
            tar.add(path, name)
    facts = {"packet": str(packet), "bytes": packet.stat().st_size,
             "sha256": hashlib.sha256(packet.read_bytes()).hexdigest(),
             "files": {name: {"bytes": path.stat().st_size,
                                "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
                       for path, name in contents},
             "product_binaries_in_packet": False}
    packet.with_suffix(packet.suffix + ".json").write_text(json.dumps(facts, indent=2))
    print(json.dumps(facts))


if __name__ == "__main__":
    main()
