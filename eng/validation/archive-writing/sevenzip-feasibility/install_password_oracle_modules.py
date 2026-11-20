#!/usr/bin/env python3
"""Unpack already hash-pinned wheel artifacts into a private module directory; no installer scripts/global changes."""
from pathlib import Path,PurePosixPath
import hashlib,json,sys,zipfile

def install(root,platform):
 root=Path(root).resolve();modules=root/'modules';modules.mkdir()
 inventory=json.loads((root/'wheel-inventory.json').read_text());rows=[r for r in inventory['files'] if r['platform']==platform]
 assert len(rows)==10
 for row in rows:
  path=root/'wheels'/platform/row['filename'];assert path.stat().st_size==row['bytes'] and hashlib.sha256(path.read_bytes()).hexdigest()==row['sha256']
  with zipfile.ZipFile(path) as archive:
   for info in archive.infolist():
    name=PurePosixPath(info.filename);assert not name.is_absolute() and '..' not in name.parts and not info.filename.startswith(('\\','/'))
    assert ((info.external_attr>>16)&0o170000)!=0o120000
   archive.extractall(modules)
 (root/'private-modules.json').write_text(json.dumps({'platform':platform,'installed_wheels':rows,'production_dependency':False},indent=2))

if __name__=='__main__':install(*sys.argv[1:])
