#!/usr/bin/env python3
"""Freeze the initial full batch, explicitly retaining its three fixture-envelope test failures."""
from pathlib import Path
import csv,hashlib,json,os,shutil,subprocess,sys,tarfile,time,xml.etree.ElementTree as ET

windows=sys.platform=='win32';mac=sys.platform=='darwin'
task=Path(sys.argv[1]);assert task.is_absolute()
# Preserve the exact compiler path spelling on Windows; the PDB audit intentionally compares it ordinally.
if not windows:task=task.resolve()
root=task/'strict-rar5-catalog-source-v1';out=task/'strict-rar5-catalog-v1'
assert not (out/'files.json').exists(), 'Already frozen; do not modify the evidence.'
def idle():
 if windows:
  deadline=time.monotonic()+1800
  while True:
   rows=csv.reader(subprocess.check_output([str(Path(os.environ['SystemRoot'])/'System32/tasklist.exe'),'/FO','CSV','/NH']).decode('utf-8',errors='replace').splitlines())
   busy=[row[:2] for row in rows if row and (row[0].lower().startswith('filecat') or row[0].lower() in ('dotnet.exe','testhost.exe'))]
   if not busy:break
   if time.monotonic()>=deadline:raise RuntimeError('Another tester is active; postpone: '+repr(busy))
   time.sleep(15)
 else:subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
idle()
trx=ET.parse(out/'results/archive-worker-secrets.trx');ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
results=trx.findall('.//t:UnitTestResult',ns);failed=[x for x in results if x.attrib['outcome']=='Failed']
assert len(results)==964 and len(failed)==3
assert all('StrictRar5CatalogTests.Existing_independent_RAR5_fixtures_match_the_test_only_reference_reader' in x.attrib['testName'] for x in failed)
assert all('Repeated RAR main headers or archive metadata cannot be preserved' in ''.join(x.itertext()) for x in failed)
(out/'diagnostic-outcome.json').write_text(json.dumps({'total':len(results),'passed':sum(x.attrib['outcome']=='Passed' for x in results),
 'skipped':sum(x.attrib['outcome']=='NotExecuted' for x in results),'failed':3,'expected_failed_tests':[x.attrib['testName'] for x in failed],
 'status':'INITIAL TEST FAILURE; corrected fixture helper requires a separate passing follow-up','production_backend_qualified':False},indent=2),encoding='utf-8')
sdk=task/'dotnet' if not mac else Path('/Users/benny/.dotnet');dotnet=str(sdk/('dotnet.exe' if windows else 'dotnet'))
env=dict(os.environ,DOTNET_ROOT=str(sdk),MSBUILDDISABLENODEREUSE='1',DOTNET_CLI_TELEMETRY_OPTOUT='1')
def execute(name,args):
 path=out/(name+'.log');attempt=1
 while path.exists():
  attempt+=1;path=out/(name+'-attempt-'+str(attempt)+'.log')
 with path.open('xb') as log:subprocess.run(args,cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT,check=True)
deps=root/'src/FileCat.Archives.Writing/bin/Release/net10.0/FileCat.Archives.Writing.deps.json'
libraries=json.loads(deps.read_text(encoding='utf-8-sig'))['libraries'];assert all('Rar15' not in x and 'DiscUtils' not in x for x in libraries)
shutil.copyfile(deps,out/deps.name)
(out/'writing-closure.json').write_text(json.dumps({'libraries':sorted(libraries),'Rar15_or_DiscUtils_present':False},indent=2),encoding='utf-8')
binaries=out/'binaries';binaries.mkdir(exist_ok=True)
for folder in (root/'tests/FileCat.Core.Tests/bin/Release/net10.0',root/'src/FileCat.Archives.Writing/bin/Release/net10.0',root/'tests/FileCat.WorkerProbe/bin/Release/net10.0'):
 for p in folder.iterdir():
  if p.is_file() and p.name.startswith('FileCat.') and p.suffix in ('.dll','.pdb'):shutil.copyfile(p,binaries/p.name)
execute('diagnostic-audit-build',[dotnet,'build','eng/validation/archive-writing/PdbSourceAudit/PdbSourceAudit.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'])
extra=Path('C:/Users/Admin/.nuget/packages') if windows else Path.home()/'.nuget/packages'
audit=out/'compiler-sources';attempt=1
while audit.exists():
 attempt+=1;audit=out/('compiler-sources-attempt-'+str(attempt))
execute('diagnostic-source-audit',[dotnet,str(root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'),str(root),str(binaries),str(audit),str(extra)])
packet=task/'strict-rar5-catalog-source-v1.tar.gz'
assert hashlib.sha256(packet.read_bytes()).hexdigest()=='a2f69827c020d3ac48f04929f295edecf1b7971eb067463e6b0be3b967d1ae3a'
shutil.copyfile(packet,out/packet.name);shutil.copyfile(__file__,out/'executed-diagnostic-capture.py')
for name in ('strict-rar5-catalog-launch-v1.log','strict-rar5-catalog-wait-v1.log','strict-rar5-catalog-error-v1.txt'):
 if (task/name).is_file():shutil.copyfile(task/name,out/name)
idle();census={'busy':False,'owned_filecat_or_test_processes':0}
if mac:
 wake=subprocess.check_output(['/bin/ps','-p','36765','-o','pid=,command='],text=True);assert wake.strip();census['preserved_wake_helper']=wake.strip()
(out/'final-census.json').write_text(json.dumps(census,indent=2),encoding='utf-8')
rows=[]
for p in sorted(out.rglob('*')):
 assert not p.is_symlink(),str(p)
 if p.is_file():rows.append({'path':p.relative_to(out).as_posix(),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
(out/'files.json').write_text(json.dumps({'files':rows,'initial_failed_test_fixture_batch':True,'production_backend_qualified':False},indent=2),encoding='utf-8')
archive=task/'strict-rar5-catalog-diagnostic-evidence-v1.tar.gz'
with tarfile.open(archive,'x:gz',dereference=False) as tar:
 for p in sorted(out.rglob('*')):tar.add(p,arcname=out.name+'/'+p.relative_to(out).as_posix(),recursive=False)
facts={'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'rows':len(rows)}
(task/'strict-rar5-catalog-diagnostic-evidence-v1.json').write_text(json.dumps(facts,indent=2),encoding='utf-8');print(json.dumps(facts),flush=True)
