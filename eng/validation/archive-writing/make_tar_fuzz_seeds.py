#!/usr/bin/env python3
"""Generate licence-clean strict TAR corpus seeds from deterministic tiny data."""

import hashlib
import io
import json
from pathlib import Path
import sys
import tarfile


def archive(fmt, members):
    output = io.BytesIO()
    with tarfile.open(fileobj=output, mode="w", format=fmt) as tar:
        for name, content, directory, extra in members:
            info = tarfile.TarInfo(name)
            info.type = tarfile.DIRTYPE if directory else tarfile.REGTYPE
            info.mode = 0o755 if directory else 0o644
            info.uid = 1
            info.gid = 2
            info.uname = "author"
            info.gname = "group"
            info.mtime = 1700000000
            info.size = 0 if directory else len(content)
            info.pax_headers = extra
            tar.addfile(info, None if directory else io.BytesIO(content))
    return output.getvalue()


def main():
    folder = Path(sys.argv[1]).resolve()
    folder.mkdir(parents=True, exist_ok=False)
    seeds = {
        "ustar-one-file.tar": archive(tarfile.USTAR_FORMAT, [("a.txt", b"hello\n", False, {})]),
        "ustar-directory.tar": archive(tarfile.USTAR_FORMAT, [("dir/", b"", True, {}),
                                                                  ("dir/a.txt", b"small payload", False, {})]),
        "ustar-duplicate.tar": archive(tarfile.USTAR_FORMAT, [("same.txt", b"first", False, {}),
                                                                  ("same.txt", b"second", False, {})]),
        "pax-unicode-time.tar": archive(tarfile.PAX_FORMAT, [("folder/žlutý.txt", b"pax data", False,
                                                              {"mtime": "1700000000.123456789", "atime": "1700000001.5"})]),
    }
    facts = {"generator": "FileCat make_tar_fuzz_seeds.py", "source": "new deterministic synthetic data",
             "licence": "FileCat repository", "seeds": {}}
    for name, data in seeds.items():
        if len(data) > 1048576:
            raise ValueError("A generated seed exceeds the 1 MiB fuzz input cap")
        (folder / name).write_bytes(data)
        facts["seeds"][name] = {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
    (folder / "manifest.json").write_text(json.dumps(facts, indent=2))
    print(json.dumps(facts))


if __name__ == "__main__":
    main()
