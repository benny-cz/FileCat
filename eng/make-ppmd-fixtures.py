#!/usr/bin/env python3
"""Builds FileCat's 7z PPMd test archives with 7-Zip and pins what 7-Zip extracts from them.

Usage: python -I eng/make-ppmd-fixtures.py OUTDIR [--7z PATH]

The member files are generated here, byte for byte the same on every machine (a SplitMix64 generator, no Python
random module). 7-Zip compresses them with PPMd at several model orders and memory sizes; the archives keep no
timestamps. Then 7-Zip extracts every archive in OUTDIR named FileCat.PPMd.*.7z or 7Zip.PPMd.7z (SharpCompress's
fixture, when it is there) into a new folder, and FileCat.PPMd.json records each archive's SHA-256, 7-Zip's
listing of its coders and the size and SHA-256 of every member 7-Zip extracted: the reference FileCat's 7z reader
and the upstream SharpCompress reader are held to (SevenZipPpmdTests).
"""
import argparse
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile

MASK = (1 << 64) - 1


class SplitMix64:
    def __init__(self, seed):
        self.state = seed & MASK

    def next(self):
        self.state = (self.state + 0x9E3779B97F4A7C15) & MASK
        z = self.state
        z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & MASK
        z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & MASK
        return z ^ (z >> 31)

    def below(self, n):
        return self.next() % n


def text(size, seed):
    """Words from a skewed vocabulary, with punctuation, line breaks and now and then a run of noise bytes."""
    rng = SplitMix64(seed)
    words = []
    for _ in range(3000):
        words.append(bytes(97 + rng.below(26) for _ in range(1 + rng.below(10))))
    out = bytearray()
    while len(out) < size:
        roll = rng.below(1000)
        if roll < 4:
            out += bytes(rng.below(256) for _ in range(1 + rng.below(24)))
            continue
        pick = rng.below(len(words)) * rng.below(len(words)) // len(words)  # small indexes are common
        word = words[pick]
        if roll < 60:
            word = word[:1].upper() + word[1:]
        out += word
        out += b"\n" if roll < 90 else b", " if roll < 180 else b". " if roll < 230 else b" "
    return bytes(out[:size])


def binary(size, seed):
    """Little-endian records: a counter, a slowly changing value, a tag and a few noise bytes."""
    rng = SplitMix64(seed)
    out = bytearray()
    value = 1000
    n = 0
    while len(out) < size:
        value += rng.below(7) - 3
        out += n.to_bytes(4, "little") + (value & 0xFFFFFFFF).to_bytes(4, "little")
        out += b"REC" + bytes([65 + n % 26]) + bytes(rng.below(256) for _ in range(4))
        n += 1
    return bytes(out[:size])


# (archive, PPMd model order, model memory, solid, members)
FIXTURES = [
    ("FileCat.PPMd.o2.m64k.7z", 2, "64k", False, ["text.txt"]),
    ("FileCat.PPMd.o16.m1m.7z", 16, "1m", False, ["text.txt", "data/records.bin"]),
    ("FileCat.PPMd.o32.m4m.7z", 32, "4m", False, ["text.txt"]),  # 7-Zip lowers a larger memory to 16x the input
    ("FileCat.PPMd.solid.7z", 8, "8m", True, ["text.txt", "data/records.bin", "empty.txt", "notes/readme.txt"]),
    ("FileCat.PPMd.restarts.7z", 6, "256k", False, ["large.txt"]),
]

MEMBERS = {
    "text.txt": lambda: text(256 * 1024, 1),
    "data/records.bin": lambda: binary(96 * 1024, 2),
    "empty.txt": lambda: b"",
    "notes/readme.txt": lambda: text(3000, 3),
    "large.txt": lambda: text(768 * 1024, 4),  # three times the model memory of its archive
}


def sha256(path):
    with open(path, "rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()


def run(command, cwd):
    result = subprocess.run(command, cwd=cwd, capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"{command} failed ({result.returncode}):\n{result.stdout}\n{result.stderr}")
    return result.stdout


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("outdir", type=pathlib.Path)
    parser.add_argument("--7z", dest="sevenzip", default=os.environ.get("FILECAT_7Z") or r"C:\Program Files\7-Zip\7z.exe")
    args = parser.parse_args()
    out = args.outdir.resolve()
    out.mkdir(parents=True, exist_ok=True)
    version = run([args.sevenzip], cwd=out).strip().splitlines()[0]

    with tempfile.TemporaryDirectory(prefix="filecat-ppmd-") as work:
        stage = pathlib.Path(work) / "stage"
        for name, make in MEMBERS.items():
            path = stage / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(make())
        for archive, order, memory, solid, members in FIXTURES:
            target = out / archive
            if target.exists():
                target.unlink()
            run([args.sevenzip, "a", "-t7z", f"-m0=PPMd:o={order}:mem={memory}", "-ms=" + ("on" if solid else "off"),
                 "-mtc=off", "-mta=off", "-mtm=off", "-mhc=on", str(target)] + [m.replace("/", os.sep) for m in members], cwd=stage)

        manifest = {"Generator": "eng/make-ppmd-fixtures.py", "SevenZip": version, "Fixtures": []}
        names = sorted(p.name for p in out.glob("FileCat.PPMd.*.7z"))
        if (out / "7Zip.PPMd.7z").exists():
            names.insert(0, "7Zip.PPMd.7z")
        for name in names:
            archive = out / name
            listing = run([args.sevenzip, "l", "-slt", str(archive)], cwd=out)
            methods = sorted({line.split("=", 1)[1].strip() for line in listing.splitlines()
                              if line.startswith("Method = ") and line.split("=", 1)[1].strip()})
            extracted = pathlib.Path(work) / ("x-" + name)
            run([args.sevenzip, "x", "-y", "-o" + str(extracted), str(archive)], cwd=out)
            files = []
            for path in sorted(p for p in extracted.rglob("*") if p.is_file()):
                files.append({"Path": path.relative_to(extracted).as_posix(), "Bytes": path.stat().st_size, "SHA256": sha256(path)})
            spec = next((f for f in FIXTURES if f[0] == name), None)
            manifest["Fixtures"].append({
                "Name": name,
                "Bytes": archive.stat().st_size,
                "SHA256": sha256(archive),
                "Owner": "FileCat" if spec else "SharpCompress 0.50.5 (e4193af83b6ec8bdd5fb4f6ebffc2315dd3fc626)",
                "Switches": None if spec is None else
                    f"a -t7z -m0=PPMd:o={spec[1]}:mem={spec[2]} -ms={'on' if spec[3] else 'off'} -mtc=off -mta=off -mtm=off -mhc=on",
                "Methods7Zip": methods,
                "Members7Zip": files,
            })
            shutil.rmtree(extracted)
    (out / "FileCat.PPMd.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    for fixture in manifest["Fixtures"]:
        print(f"{fixture['Name']}: {fixture['Bytes']} bytes, {fixture['SHA256']}, {', '.join(fixture['Methods7Zip'])}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
