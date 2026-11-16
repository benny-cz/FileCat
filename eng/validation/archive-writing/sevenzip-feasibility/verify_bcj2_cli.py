#!/usr/bin/env python3
"""Independent official 7-Zip payloads and py7zr header metadata for bounded public BCJ2 fixtures."""
from pathlib import Path
import hashlib,importlib.metadata,json,subprocess,sys

def run(root,tool,modules):
 root=Path(root).resolve();tool=Path(tool).resolve();sys.path.insert(0,str(Path(modules).resolve()))
 import py7zr
 folders=sorted(root.glob('archive-*'));assert len(folders)==42
 payloads=[];metadata=[];unsupported=[]
 for folder in folders:
  profile=json.loads((folder/'profile.json').read_text());assert profile['synthetic_public_vector']
  password=profile['public_synthetic_password'];assert password=='FileCat-Public-Bcj2-Vector-暗号-42829304'
  rows=json.loads((folder/'members.json').read_text());assert len(rows)==3
  archive=folder/'members.7z';assert archive.stat().st_size<4<<20
  args=[str(tool),'t','-bd','-y','-sccUTF-8','-p'+password,str(archive)]
  tested=subprocess.run(args,capture_output=True,timeout=120);(folder/'official-test.log').write_bytes(tested.stdout+tested.stderr)
  if profile['codec']==5:
   assert tested.returncode==2 and b'Unsupported Method' in tested.stderr,(folder.name,tested.stderr)
   unsupported.append(folder.name)
  else:
   assert tested.returncode==0,(folder.name,tested.stdout[-2048:],tested.stderr)
   for row in rows:
    if row['directory']:continue
    result=subprocess.run([str(tool),'x','-so','-bd','-y','-p'+password,str(archive),'--',row['name']],capture_output=True,timeout=120)
    assert result.returncode==0 and len(result.stdout)==row['length'] and hashlib.sha256(result.stdout).hexdigest().upper()==row['sha256'],(folder.name,row['name'],result.stderr)
    payloads.append({'fixture':folder.name,'name':row['name'],'bytes':len(result.stdout),'sha256':hashlib.sha256(result.stdout).hexdigest()})
  with py7zr.SevenZipFile(archive,'r',password=password if profile['encrypted'] else None) as reader:
   assert reader.getnames()==[r['name'] for r in rows],folder.name
   for actual,row in zip(reader.files,rows):
    assert actual.is_directory==row['directory'] and actual.uncompressed==row['length'],(folder.name,row)
    assert actual.posix_mode is not None and actual.posix_mode&0o777==row['mode'],(folder.name,row)
    assert int(actual.lastwritetime)==(row['mtime_seconds']+11644473600)*10000000+row['mtime_nanos']//100,(folder.name,row)
    metadata.append({'fixture':folder.name,'name':row['name'],'length':row['length'],'mode':row['mode']})
 assert len(payloads)==72 and len(metadata)==126 and len(unsupported)==6
 result={'tool':str(tool),'tool_sha256':hashlib.sha256(tool.read_bytes()).hexdigest(),'py7zr_version':importlib.metadata.version('py7zr'),
  'official_archives':36,'exact_payloads':payloads,'unsupported_zstandard_extension':unsupported,'independent_header_records':metadata,
  'py7zr_payload_graphs_not_checked':True,'synthetic_public_passwords_only':True,'production_dependency':False}
 (root/'official-bcj2.json').write_text(json.dumps(result,indent=2));print('PASS: 36 official BCJ2 archives / 72 exact payloads; 126 py7zr metadata records; six explicit native Zstandard gaps.',flush=True)

if __name__=='__main__':run(*sys.argv[1:])
