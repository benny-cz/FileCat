#!/usr/bin/env python3
"""Independent native libzstd inputs over the shared exact USTAR/PAX metadata fixture corpus."""
import gzip
import hashlib
import json
from pathlib import Path
import runpy
import sys
from zstd_oracle_common import NativeZstd

runpy.run_path(str(Path(__file__).with_name('make-tar-bzip2-input-oracles.py')), run_name='__main__')
root = Path(sys.argv[1]).resolve(strict=True)
oracle = NativeZstd()
for dialect, checksum in [('ustar', False), ('pax', True)]:
    tar = gzip.decompress((root / (dialect + '.tar.gz')).read_bytes())
    encoded = oracle.compress(tar, checksum)
    assert oracle.decompress(encoded) == tar
    (root / (dialect + '.tar.zst')).write_bytes(encoded)
    print(dialect, checksum, hashlib.sha256(encoded).hexdigest().upper())
print(json.dumps(oracle.facts(), sort_keys=True))
