#!/usr/bin/env python3
"""Re-encodes FileCat.Bomb.tar.bz2's TAR stream as one zstd frame (RFC 8878), for FileCat.Bomb.tar.zst.

The TAR bytes are kept exactly: the first member's 512-byte header as a raw block, its 2 GiB of zeros as RLE blocks of
128 KiB, and the rest (the second member and the end of the archive) as one raw block, the last.

Usage: python -I eng/make-tar-zst-bomb.py tests/FileCat.Core.Tests/TestData/Archives/FileCat.Bomb.tar.bz2 OUT.tar.zst
"""
import bz2
import hashlib
import struct
import sys

BLOCK = 128 * 1024
ZEROS = 2 * 1024 ** 3


def block_header(size, kind, last):
    value = (size << 3) | (kind << 1) | (1 if last else 0)
    return struct.pack("<I", value)[:3]


def main(source, target):
    tar = hashlib.sha256()
    with bz2.open(source, "rb") as reader, open(target, "wb") as out:
        out.write(b"\x28\xb5\x2f\xfd")  # magic
        out.write(b"\x00")  # no single segment, no checksum, no dictionary, no content size
        out.write(bytes([17 - 10 << 3]))  # window 2^17 = 128 KiB, the largest block
        header = reader.read(512)
        assert len(header) == 512 and header.startswith(b"a-zeros.bin\x00"), header[:16]
        tar.update(header)
        out.write(block_header(512, 0, False) + header)
        left = ZEROS
        zero = bytes(BLOCK)
        while left:
            chunk = reader.read(min(BLOCK, left))
            assert chunk == zero[: len(chunk)], "the first member is not all zeros"
            tar.update(chunk)
            out.write(block_header(len(chunk), 1, False) + b"\x00")
            left -= len(chunk)
        rest = reader.read()
        assert 0 < len(rest) <= BLOCK and b"b-small.txt" in rest[:100], len(rest)
        tar.update(rest)
        out.write(block_header(len(rest), 0, True) + rest)
    print("tar sha256", tar.hexdigest(), "rest", len(rest))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
