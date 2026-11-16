#!/usr/bin/env python3
"""Verify the already qualified private reader modules against their pinned wheel bytes; never installs/imports them."""
from pathlib import Path
import hashlib,json,sys,zipfile

def verify(root,platform):
 root=Path(root).resolve();rows=[r for r in json.loads((root/'wheel-inventory.json').read_text())['files'] if r['platform']==platform];assert len(rows)==10
 for row in rows:
  wheel=root/'wheels'/platform/row['filename'];assert wheel.stat().st_size==row['bytes'] and hashlib.sha256(wheel.read_bytes()).hexdigest()==row['sha256']
  with zipfile.ZipFile(wheel) as archive:
   for item in archive.infolist():
    if not item.is_dir():assert (root/'modules'/item.filename).read_bytes()==archive.read(item)
 print('PASS: ten previously qualified private reader wheels and installed module bytes match.',flush=True)

if __name__=='__main__':verify(*sys.argv[1:])
