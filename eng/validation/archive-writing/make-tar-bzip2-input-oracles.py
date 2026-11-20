#!/usr/bin/env python3
"""Independent stdlib BZip2/TAR inputs, sharing the exact metadata golden data of the gzip fixture generator."""
import bz2
import gzip
import hashlib
from pathlib import Path
import runpy
import sys

runpy.run_path(str(Path(__file__).with_name('make-tar-gzip-input-oracles.py')), run_name='__main__')
root = Path(sys.argv[1]).resolve(strict=True)
for dialect, block_size in [('ustar', 1), ('pax', 9)]:
    tar = gzip.decompress((root / (dialect + '.tar.gz')).read_bytes())
    encoded = bz2.compress(tar, compresslevel=block_size)
    (root / (dialect + '.tar.bz2')).write_bytes(encoded)
    print(dialect, block_size, hashlib.sha256(encoded).hexdigest().upper())
