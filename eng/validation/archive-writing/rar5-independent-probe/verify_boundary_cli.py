#!/usr/bin/env python3
"""Read-only official 7-Zip checks of owned synthetic RAR name-boundary fixtures; no runtime dependency."""
from pathlib import Path
import hashlib,json,subprocess,sys

def run(root,tool):
 root=Path(root).resolve();tool=Path(tool).resolve();verified=[]
 for protection in range(3):
  folder=root/('protection-'+str(protection));record=json.loads((folder/'expected.json').read_text())
  assert record['protection']==protection and record['synthetic_public_password']=='FileCat-Public-Rar-Name-Boundary-暗号-42829306'
  assert len(record['name'].encode('utf-8'))==65536 and record['bytes']==1
  archive=folder/'members.rar';assert archive.stat().st_size<1<<20
  password=record['synthetic_public_password']
  result=subprocess.run([str(tool),'t','-bd','-y','-p'+password,str(archive)],capture_output=True,timeout=120)
  (folder/'official-test.log').write_bytes(result.stdout+result.stderr);assert result.returncode==0,result.stderr[-2048:]
  result=subprocess.run([str(tool),'x','-so','-bd','-y','-p'+password,str(archive),'--',record['name']],capture_output=True,timeout=120)
  assert result.returncode==0 and len(result.stdout)==record['bytes'] and hashlib.sha256(result.stdout).hexdigest().upper()==record['sha256'],result.stderr[-2048:]
  verified.append({'protection':protection,'name_utf8_bytes':65536,'payload_bytes':len(result.stdout),'payload_sha256':hashlib.sha256(result.stdout).hexdigest(),'archive_sha256':hashlib.sha256(archive.read_bytes()).hexdigest()})
 (root/'official-boundary.json').write_text(json.dumps({'verified':verified,'tool':str(tool),'tool_sha256':hashlib.sha256(tool.read_bytes()).hexdigest(),'production_dependency':False,'public_synthetic_passwords_only':True},indent=2))
 print('PASS: three official 7-Zip RAR boundary archives and exact payloads, including data/header encryption.',flush=True)

if __name__=='__main__':run(*sys.argv[1:])
