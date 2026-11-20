#!/usr/bin/env python3
"""One guarded private dependency setup / independent password-reader session; never a production route."""
from pathlib import Path
import hashlib,json,os,platform,subprocess,sys,tarfile

def run(task,batch):
 task=Path(task).resolve();out=task/batch;source=task/'archive-passwords-source-v4';tools=task/'password-oracle-tools-v1'
 subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
 assert (out/'self-tests-ready.txt').is_file()
 packet=task/'password-oracle-tools-v1.tar.gz'
 assert hashlib.sha256(packet.read_bytes()).hexdigest()=='7733129e7dc3caef86e5f2f4f0b90af3edbc674ae706190c52e533d4fed3ee16'
 first=not tools.exists()
 if first:
  tools.mkdir()
  with tarfile.open(packet,'r:gz') as tar:tar.extractall(tools,filter='data')
 for row in json.loads((tools/'files.json').read_text())['files']:
  path=tools/row['path'];assert path.stat().st_size==row['bytes'] and hashlib.sha256(path.read_bytes()).hexdigest()==row['sha256']
 which='mac' if sys.platform=='darwin' else 'ubuntu'
 if first:subprocess.run(['python3',str(source/'eng/validation/archive-writing/sevenzip-feasibility/install_password_oracle_modules.py'),str(tools),which],check=True)
 else:
  import zipfile
  for row in json.loads((tools/'wheel-inventory.json').read_text())['files']:
   if row['platform']!=which:continue
   with zipfile.ZipFile(tools/'wheels'/which/row['filename']) as wheel:
    for item in wheel.infolist():
     if not item.is_dir():assert (tools/'modules'/item.filename).read_bytes()==wheel.read(item)
 tool=task/'sevenzip-ppmd-filter-v2/tools'/('mac' if which=='mac' else 'linux')/'7zz'
 assert hashlib.sha256(tool.read_bytes()).hexdigest()==json.loads((tools/'official-tool-identities.json').read_text())[which]['sha256']
 helper=source/'eng/validation/archive-writing/sevenzip-feasibility/verify_password_archives.py'
 env=dict(os.environ,PYTHONDONTWRITEBYTECODE='1')
 with (out/'password-independent.log').open('xb') as log:
  subprocess.run(['python3','-B',str(helper),str(out/'password-oracles'),str(tool),str(tools/'modules')],env=env,stdout=log,stderr=subprocess.STDOUT,check=True)
 subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
 (out/'password-oracles-ready.json').write_text(json.dumps({'platform':platform.platform(),'tools':str(tools),'independent_readers_passed':True,'production_backend_qualified':False},indent=2))
 print('PASS: private password interoperability checks complete; full qualification/freeze remains separate.',flush=True)

if __name__=='__main__':run(*sys.argv[1:])
