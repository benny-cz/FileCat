#!/usr/bin/env python3
"""Deterministic FZ02 seeds for the production strict wrapper readers."""

import argparse
import bz2
import gzip
import hashlib
import json
from pathlib import Path


def zstd_raw(payload: bytes, *, single: bool = True, window: int = 0) -> bytes:
    if len(payload) > 0x1FFFFF:
        raise ValueError("A raw Zstd block cannot hold this seed")
    if single:
        flags = 0x20 if len(payload) < 256 else 0x60
        size = bytes([len(payload)]) if len(payload) < 256 else (len(payload) - 256).to_bytes(2, "little")
        header = bytes.fromhex("28b52ffd") + bytes([flags]) + size
    else:
        header = bytes.fromhex("28b52ffd") + bytes([0, window])
    return header + ((len(payload) << 3) | 1).to_bytes(3, "little") + payload


def variants(target: str) -> dict[str, tuple[bytes, bool]]:
    payload = b"FileCat strict wrapper fuzz seed: " + bytes(range(128))
    if target == "gzip-strict":
        valid = gzip.compress(payload, mtime=0)
        empty = gzip.compress(b"", mtime=0)
        crc = bytearray(valid); crc[-8] ^= 1
        size = bytearray(valid); size[-1] ^= 1
        metadata = bytearray(valid); metadata[3] = 8
        return {
            "empty.gz": (empty, True), "payload.gz": (valid, True),
            "bad-crc.gz": (bytes(crc), False), "bad-size.gz": (bytes(size), False),
            "metadata.gz": (bytes(metadata), False),
            "truncated.gz": (valid[:-1], False), "concatenated.gz": (valid + empty, False),
            "trailing.gz": (valid + b"x", False),
        }
    if target == "bzip2-strict":
        valid = bz2.compress(payload, compresslevel=9)
        small_block = bz2.compress(payload, compresslevel=1)
        empty = bz2.compress(b"")
        crc = bytearray(valid); crc[-2] ^= 0x80
        return {
            "empty.bz2": (empty, True), "payload.bz2": (valid, True),
            "block-one.bz2": (small_block, True), "bad-crc.bz2": (bytes(crc), False),
            "truncated.bz2": (valid[:-1], False), "concatenated.bz2": (valid + empty, False),
            "trailing.bz2": (valid + b"x", False),
        }
    valid = zstd_raw(payload)
    empty = zstd_raw(b"")
    windowed = zstd_raw(payload, single=False, window=0x37)
    huge_window = zstd_raw(payload, single=False, window=0xFF)
    fixture = Path(__file__).resolve().parents[3] / "tests/FileCat.Core.Tests/TestData/Archives/Tar.tar.zst"
    checked = fixture.read_bytes()
    corrupt = bytearray(checked); corrupt[-1] ^= 1
    return {
        "empty.zst": (empty, True), "payload.zst": (valid, True),
        "windowed.zst": (windowed, True), "checksummed-fixture.zst": (checked, True),
        "bad-checksum.zst": (bytes(corrupt), False), "huge-window.zst": (huge_window, False),
        "truncated.zst": (valid[:-1], False), "concatenated.zst": (valid + empty, False),
        "trailing.zst": (valid + b"x", False),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--target", choices=("gzip-strict", "bzip2-strict", "zstd-strict"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    rows = []
    for name, (data, accepted) in sorted(variants(args.target).items()):
        if len(data) > 1048576:
            raise ValueError("A wrapper seed exceeds the native input limit")
        (output / name).write_bytes(data)
        rows.append({"name": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(),
                     "accepted": accepted})
    (output / "manifest.json").write_text(json.dumps({"target": args.target, "seeds": rows}, indent=2))
    print(json.dumps({"target": args.target, "seed_count": len(rows), "accepted": sum(row["accepted"] for row in rows)}))


if __name__ == "__main__":
    main()
