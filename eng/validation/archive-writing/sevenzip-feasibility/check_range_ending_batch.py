"""Guest-only build/pin native SDK oracles, then compare new range endings and shared-input LZMA regressions."""
from pathlib import Path
import hashlib,json,os,subprocess,sys,tarfile

root,out,task=map(Path,sys.argv[1:]);windows=sys.platform=='win32';mac=sys.platform=='darwin';helpers=root/'eng/validation/archive-writing/sevenzip-feasibility'
def execute(name,args,env=None):
 with (out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT,check=True,timeout=1800)
tools=out/'tools';tools.mkdir();packet=task/'range-ending-tools-v1.tar.gz'
assert hashlib.sha256(packet.read_bytes()).hexdigest()=='4e10169e87466c057d8b46b4f4195817699528fab5cf18b14bf8380be38f6ee6'
with tarfile.open(packet) as tar:tar.extractall(tools,filter='data')
ledger=json.loads((helpers/'range-ending-sdk-sources.json').read_text());assert json.loads((tools/'sources.json').read_text())==ledger
for row in ledger['files']:
 p=tools/'sdk'/row['path'];assert p.stat().st_size==row['bytes'] and hashlib.sha256(p.read_bytes()).hexdigest()==row['sha256']
oracles={};env=dict(os.environ);commands=[]
if mac:env['SDKROOT']='/Library/Developer/CommandLineTools/SDKs/MacOSX27.0.sdk'
for kind,sources in [('ppmd',['Ppmd7.c','Ppmd7Dec.c']),('bcj2',['Bcj2.c','CpuArch.c'])]:
 wrapper=helpers/f'sdk_{"ppmd_end" if kind=="ppmd" else "bcj2_decode"}_oracle.c'
 if windows:
  fact=next(row for row in json.loads((tools/'windows/build.json').read_text()) if row['binary']==f'sdk-{kind}-end-oracle.exe')
  oracle=tools/'windows'/fact['binary'];assert hashlib.sha256(oracle.read_bytes()).hexdigest()==fact['binary_sha256']
  assert hashlib.sha256(wrapper.read_bytes()).hexdigest()==fact['wrapper_sha256']
 else:
  compiler='/Library/Developer/CommandLineTools/usr/bin/clang' if mac else '/usr/bin/gcc';oracle=tools/f'sdk-{kind}-end-oracle'
  command=[compiler,'-std=c11','-O2','-I',str(tools/'sdk/C'),str(wrapper),*[str(tools/'sdk/C'/source) for source in sources],'-o',str(oracle)]
  execute('sdk-'+kind+'-build',command,env);commands.append(command)
 oracles[kind]=oracle
(out/'sdk-build-commands.json').write_text(json.dumps(commands,indent=2))
execute('sdk-range-compare',[sys.executable,'-B',str(helpers/'verify_range_endings_sdk.py'),str(out/'vectors'),str(oracles['ppmd']),str(oracles['bcj2']),str(out/'sdk-range-comparison')])
lzma=out/'lzma-tools';lzma.mkdir();packet=task/'lzma-end-tools-v1.tar.gz'
assert hashlib.sha256(packet.read_bytes()).hexdigest()=='aa7ee1ff6f335e52a8bcee38d04e645112957b83298ca5994e03a1654f76e2ee'
with tarfile.open(packet) as tar:tar.extractall(lzma,filter='data')
for row in json.loads((lzma/'files.json').read_text())['files']:
 p=lzma/row['path'];assert p.stat().st_size==row['bytes'] and hashlib.sha256(p.read_bytes()).hexdigest()==row['sha256']
oracle=lzma/'sdk-lzma-end-oracle.exe'
if not windows:
 oracle=lzma/'sdk-lzma-end-oracle';command=[compiler,'-std=c11','-O2','-I',str(lzma/'sdk/C'),str(helpers/'sdk_lzma_end_oracle.c'),
  str(lzma/'sdk/C/LzmaDec.c'),str(lzma/'sdk/C/Lzma2Dec.c'),'-o',str(oracle)]
 execute('sdk-lzma-build',command,env)
execute('sdk-lzma-compare',[sys.executable,'-B',str(helpers/'verify_lzma_end_sdk.py'),str(out/'lzma-vectors'),str(oracle),str(out/'sdk-lzma-comparison')])
facts=json.loads((out/'sdk-lzma-comparison/comparison.json').read_text());assert len(facts['rows'])==224 and not facts['failures']
