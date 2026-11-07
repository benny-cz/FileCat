#!/usr/bin/env python3
"""Exact four-stream SDK differential on owned, bounded BCJ2 vectors. No production dependency."""
from pathlib import Path
import hashlib,json,subprocess,sys

def run(root,oracle):
 root=Path(root).resolve();oracle=Path(oracle).resolve();folders=sorted(root.glob('raw-*'));assert len(folders)==18
 verified=[]
 for folder in folders:
  source=folder/'input.bin';assert source.stat().st_size<=4<<20
  for stream in range(4):
   output=folder/('stream-'+str(stream)+'.sdk');assert not output.exists()
   subprocess.run([str(oracle),'bcj2','split',str(source),str(output),str(stream),'0'],check=True,timeout=60)
   expected=folder/('stream-'+str(stream)+'.bin');assert output.read_bytes()==expected.read_bytes(),(folder.name,stream)
   verified.append({'fixture':folder.name,'stream':stream,'bytes':output.stat().st_size,'sha256':hashlib.sha256(output.read_bytes()).hexdigest()})
 result={'sdk_commit':'b88c6bc09339de32f7d18bbab900686b244f42ba','oracle_sha256':hashlib.sha256(oracle.read_bytes()).hexdigest(),
  'production_dependency':False,'exact_streams':verified}
 (root/'sdk-bcj2.json').write_text(json.dumps(result,indent=2));print('PASS:',len(verified),'exact SDK BCJ2 streams.',flush=True)

if __name__=='__main__':run(*sys.argv[1:])
