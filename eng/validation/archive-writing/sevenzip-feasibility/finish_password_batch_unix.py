#!/usr/bin/env python3
"""Finish the already-passing v4 password batch, without rerunning managed tests."""
from pathlib import Path
import hashlib,json,os,shutil,subprocess,sys,tarfile

def digest(path):
 h=hashlib.sha256()
 with Path(path).open('rb') as f:
  while block:=f.read(1<<20):h.update(block)
 return h.hexdigest()

def run(task):
 task=Path(task).resolve();out=task/'archive-passwords-v4';source=task/'archive-passwords-source-v4'
 def idle():subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
 idle();assert (out/'self-tests-ready.txt').is_file() and (out/'password-oracles-ready.json').is_file()
 assert not (out/'files.json').exists()
 mac=sys.platform=='darwin';previous=task/'sevenzip-ppmd-filter-v2';helpers=source/'eng/validation/archive-writing/sevenzip-feasibility'
 def execute(name,args):
  with (out/(name+'.log')).open('xb') as log:
   subprocess.run(args,env=dict(os.environ,PYTHONDONTWRITEBYTECODE='1'),stdout=log,stderr=subprocess.STDOUT,check=True)
 if '--freeze-only' in sys.argv[2:]:
  assert len(json.loads((out/'vectors/sdk-reader.json').read_text())['verified'])==19
  official=json.loads((out/'oracles/official-reader.json').read_text());assert official['tested_archives']==28 and len(official['verified'])==74
  assert len(json.loads((out/'oracles/native-reader.json').read_text())['verified'])==4205
  assert (out/'password-test-tools/modules').is_dir() and (out/'earlier-attempts').is_dir()
 else:
  tools=out/'tools';shutil.copytree(previous/'tools',tools)
  for row in json.loads((tools/'files.json').read_text())['files']:
   p=tools/row['path'];assert p.stat().st_size==row['bytes'] and digest(p)==row['sha256']
  oracle=out/'sdk-oracle';shutil.copyfile(previous/'sdk-oracle',oracle);oracle.chmod(0o700)
  assert digest(oracle)==json.loads((previous/'vectors/sdk-reader.json').read_text())['oracle_sha256']
  execute('sdk-compare',['python3','-B',str(helpers/'verify_sdk.py'),str(out/'vectors'),str(oracle)])
  tool=tools/('mac' if mac else 'linux')/'7zz';tool.chmod(0o700)
  execute('official-unencrypted',['python3','-B',str(helpers/'verify_header_cli.py'),str(out/'oracles'),str(tool)])
  prefix=task/'sevenzip-crypto-v1/native-oracle/install/lib';pattern='libarchive.*.dylib' if mac else 'libarchive.so.*'
  libraries=[p for p in prefix.glob(pattern) if p.is_file() and not p.is_symlink()];assert len(libraries)==1;library=libraries[0]
  execute('native-unencrypted',['python3','-B',str(helpers/'verify_header_native.py'),str(out/'oracles'),str(library)])
  execute('native-linked-libraries',['/usr/bin/otool','-L',str(library)] if mac else ['/usr/bin/ldd',str(library)])
  native=out/'current-native';native.mkdir();shutil.copyfile(library,native/library.name)
  (out/'native-reuse.json').write_text(json.dumps({'library':str(library),'sha256':digest(library),
   'configured_evidence':str(task/'sevenzip-crypto-v1/native-oracle/build-facts.json'),
   'secondary_reader_unavailable_filters':['RiscV','Swap2','Swap4'],'production_dependency':False},indent=2))
  password_tools=task/'password-oracle-tools-v1'
  shutil.copytree(password_tools,out/'password-test-tools')
  shutil.copyfile(task/'password-oracle-tools-v1.tar.gz',out/'password-oracle-tools-v1.tar.gz')
  attempts=out/'earlier-attempts';attempts.mkdir()
  for version in (1,2,3):
   old=task/('archive-passwords-v'+str(version));rows=[]
   if not old.exists():continue
   dest=attempts/('v'+str(version));dest.mkdir()
   for name in ('tests.log','password-independent.log','standard-error.txt','self-tests-ready.txt'):
    p=old/name
    if p.is_file():shutil.copyfile(p,dest/name)
   for p in (old/'results').glob('*.trx'):shutil.copyfile(p,dest/p.name)
   packet=task/('archive-passwords-source-v'+str(version)+'.tar.gz')
   if packet.is_file():rows.append({'source_packet':str(packet),'bytes':packet.stat().st_size,'sha256':digest(packet)})
   (dest/'retained-source-identity.json').write_text(json.dumps(rows,indent=2))
  shutil.copyfile(Path(__file__),out/'executed-finish.py')
  for name in ('password-self-batch-v4.log','password-oracles-v2.log'):
   p=task/name
   if p.is_file():shutil.copyfile(p,out/name)
 shutil.copyfile(Path(__file__),out/'executed-finish.py')
 idle()
 facts={'busy':False,'owned_filecat_or_test_processes':0}
 if mac:
  wake=subprocess.run(['/bin/ps','-p','36765','-o','pid=,command='],capture_output=True,text=True,check=True)
  assert wake.stdout.strip();facts['preserved_wake_helper']=wake.stdout.strip()
 (out/'final-census.json').write_text(json.dumps(facts,indent=2))
 (out/'complete.txt').write_text('PASS: password foundation and inherited codec/header checks; production qualification remains open.\n')
 rows=[]
 for p in sorted(out.rglob('*')):
  assert not p.is_symlink(),str(p)
  if p.is_file():rows.append({'path':p.relative_to(out).as_posix(),'bytes':p.stat().st_size,'sha256':digest(p)})
 (out/'files.json').write_text(json.dumps({'files':rows,'production_backend_qualified':False},indent=2))
 archive=task/'archive-passwords-evidence-v4.tar.gz'
 with tarfile.open(archive,'x:gz',dereference=False) as tar:
  for p in sorted(out.rglob('*')):tar.add(p,arcname=out.name+'/'+p.relative_to(out).as_posix(),recursive=False)
 result={'bytes':archive.stat().st_size,'sha256':digest(archive),'rows':len(rows)}
 (task/'archive-passwords-evidence-v4.json').write_text(json.dumps(result,indent=2));print(json.dumps(result),flush=True)

if __name__=='__main__':run(sys.argv[1])
