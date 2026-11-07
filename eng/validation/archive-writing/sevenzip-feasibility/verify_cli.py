#!/usr/bin/env python3
"""Official test-only 7-Zip validates and streams owned fixture members without filesystem extraction."""
from pathlib import Path
import hashlib,json,subprocess,sys
root=Path(sys.argv[1]);tool=Path(sys.argv[2]);verified=[];unsupported=[]
folders=sorted(p.parent for p in root.glob('*/members.json'));assert len(folders)==21
version=subprocess.check_output([str(tool),'i'],text=True)
(root/'official-tool-info.txt').write_text(version)
for folder in folders:
 archive=folder/'members.7z';rows=json.loads((folder/'members.json').read_text())
 assert archive.stat().st_size<=16<<20 and 1<=len(rows)<=7
 result=subprocess.run([str(tool),'t','-bd','-y',str(archive)],capture_output=True,timeout=120)
 (folder/'official-test-v2.log').write_bytes(result.stdout+result.stderr)
 if folder.name=='codec-5':
  # Official 26.05 reads standalone zstd but does not register 7z's 04F71101 extension. This is an explicit
  # interoperability gap, requiring the separate libarchive exact-payload result; never count it as a pass.
  assert result.returncode==2 and b'Unsupported Method' in result.stderr
  unsupported.append({'fixture':folder.name,'method_id':'04f71101','reason':'not registered by official 7-Zip 26.05'})
  continue
 assert result.returncode==0,(folder.name,result.stderr)
 for row in rows:
  if row['directory']:continue
  with subprocess.Popen([str(tool),'x','-so','-bd','-y',str(archive),'--',row['name']],stdout=subprocess.PIPE,stderr=subprocess.PIPE) as child:
   count=0;digest=hashlib.sha256()
   while True:
    data=child.stdout.read(65536)
    if not data:break
    count+=len(data)
    if count>row['length'] or count>3<<20:child.kill();raise AssertionError('Independent extraction exceeds fixture bound')
    digest.update(data)
   error=child.stderr.read(65536);assert child.wait(timeout=120)==0,(folder.name,row['name'],error)
  assert count==row['length'] and digest.hexdigest().upper()==row['sha256'],(folder.name,row)
  verified.append({'fixture':folder.name,'name':row['name'],'bytes':count,'sha256':digest.hexdigest()})
(root/'official-reader.json').write_text(json.dumps({'tool_sha256':hashlib.sha256(tool.read_bytes()).hexdigest(),
 'tested_archives':len(folders)-len(unsupported),'unsupported':unsupported,'verified':verified,'production_dependency':False},indent=2))
print('PASS:',len(folders)-len(unsupported),'officially tested archives,',len(verified),'exact streamed payloads;',len(unsupported),'explicit unsupported extension.')
