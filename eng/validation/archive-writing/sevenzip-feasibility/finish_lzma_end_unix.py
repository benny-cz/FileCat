"""Retain the full v2 result; correct only its two malformed positive fixtures, then finish SDK/source capture."""
from pathlib import Path
import hashlib,json,os,platform,shutil,subprocess,sys,tarfile,xml.etree.ElementTree as E

def run(task,packet_sha):
 task=Path(task).resolve();old=task/'lzma-end-source-v2';old_out=task/'lzma-end-v2';root=task/'lzma-end-source-v3';out=task/'lzma-end-v3'
 def idle():subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
 idle();root.mkdir();out.mkdir();packet=task/'lzma-end-source-v3.tar.gz'
 assert hashlib.sha256(packet.read_bytes()).hexdigest()==packet_sha
 ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
 results=E.parse(old_out/'results/archive-worker-secrets.trx');all_rows=results.findall('.//t:UnitTestResult',ns)
 failed=[x.attrib['testName'] for x in all_rows if x.attrib['outcome']=='Failed']
 assert len(all_rows)==1138 and len(failed)==2 and all('LZMA2_raw_continuation_and_dictionary_reset_chunks_are_complete' in x for x in failed),failed
 assert len(list((old_out/'vectors').glob('*/vector.bin')))==222
 with tarfile.open(packet,'r:gz') as tar:tar.extractall(root,filter='data')
 # Verify that production changes are identical to the broad regression run; only two test-fixture bytes changed.
 changed=[]
 for folder in ['src','tests']:
  for p in (root/folder).rglob('*.cs'):
   if any(x in ('bin','obj') for x in p.relative_to(root).parts):continue
   prior=old/p.relative_to(root)
   assert prior.is_file(),str(p)
   if p.read_bytes()!=prior.read_bytes():changed.append(p.relative_to(root).as_posix())
 assert changed==['tests/FileCat.Core.Tests/StrictLzmaStreamTests.cs'],changed
 (out/'prior-result.json').write_text(json.dumps({'total':len(all_rows),'failed':failed,'outcomes':{k:sum(x.attrib['outcome']==k for x in all_rows) for k in ['Passed','Failed','NotExecuted']},'changed_cs':changed},indent=2))
 mac=sys.platform=='darwin';sdk=Path('/Users/benny/.dotnet') if mac else task/'dotnet';dotnet=str(sdk/'dotnet')
 env=dict(os.environ,DOTNET_ROOT=str(sdk),PATH=str(sdk)+os.pathsep+os.environ['PATH'],DOTNET_CLI_TELEMETRY_OPTOUT='1',MSBUILDDISABLENODEREUSE='1',FILECAT_LZMA_END_EXPORT_ROOT=str(out/'vectors'))
 def execute(name,args,cwd=root,custom_env=env):
  with (out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=cwd,env=custom_env,stdout=log,stderr=subprocess.STDOUT,check=True)
 prior=out/'prior-v2';shutil.copytree(old_out,prior);shutil.copyfile(task/'lzma-end-batch-v2.log',prior/'batch.log')
 # Preserve the earlier stack-overflow evidence as a failed helper run, never as passing regression evidence.
 first=task/'lzma-end-v1'
 if first.exists():
  fail=out/'prior-helper-failure-v1';shutil.copytree(first,fail);shutil.copyfile(task/'lzma-end-batch-v1.log',fail/'batch.log')
  shutil.copyfile(task/'lzma-end-source-v1.tar.gz',fail/'source.tar.gz')
  crash=Path.home()/'Library/Logs/DiagnosticReports/FileCat.Core.Tests-2026-11-07-100316.ips'
  if mac and crash.is_file():shutil.copyfile(crash,fail/crash.name)
 def capture(source,name):
  binaries=out/name;binaries.mkdir()
  for folder in (source/'tests/FileCat.Core.Tests/bin/Release/net10.0',source/'src/FileCat.Archives.Writing/bin/Release/net10.0',source/'tests/FileCat.WorkerProbe/bin/Release/net10.0'):
   for p in folder.iterdir():
    if p.is_file() and p.name.startswith('FileCat.') and p.suffix in ('.dll','.pdb'):shutil.copyfile(p,binaries/p.name)
  execute(name+'-audit-build',[dotnet,'build','eng/validation/archive-writing/PdbSourceAudit/PdbSourceAudit.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'],cwd=source)
  execute(name+'-source-audit',[dotnet,str(source/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'),str(source),str(binaries),str(out/(name+'-compiler-sources')),str(Path.home()/'.nuget/packages')])
 capture(old,'v2-binaries')
 shutil.copytree(old_out/'vectors',out/'vectors')
 execute('writing-build',[dotnet,'build','src/FileCat.Archives.Writing/FileCat.Archives.Writing.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'])
 execute('fixture-followup',[dotnet,'test','tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','--filter','FullyQualifiedName~StrictLzmaStreamTests.LZMA2_raw_continuation_and_dictionary_reset_chunks_are_complete','--logger','trx;LogFileName=fixture-followup.trx','--results-directory',str(out/'results')])
 capture(root,'v3-binaries')
 tools=out/'tools';tools.mkdir();tools_packet=task/'lzma-end-tools-v1.tar.gz'
 assert hashlib.sha256(tools_packet.read_bytes()).hexdigest()=='aa7ee1ff6f335e52a8bcee38d04e645112957b83298ca5994e03a1654f76e2ee'
 with tarfile.open(tools_packet,'r:gz') as tar:tar.extractall(tools,filter='data')
 for row in json.loads((tools/'files.json').read_text())['files']:
  p=tools/row['path'];assert p.stat().st_size==row['bytes'] and hashlib.sha256(p.read_bytes()).hexdigest()==row['sha256']
 c=tools/'sdk/C';compiler='/Library/Developer/CommandLineTools/usr/bin/clang' if mac else '/usr/bin/gcc';oracle=tools/'sdk-lzma-end-oracle';native_env=dict(env)
 if mac:native_env['SDKROOT']='/Library/Developer/CommandLineTools/SDKs/MacOSX27.0.sdk'
 command=[compiler,'-std=c11','-O2','-I',str(c),str(root/'eng/validation/archive-writing/sevenzip-feasibility/sdk_lzma_end_oracle.c'),str(c/'LzmaDec.c'),str(c/'Lzma2Dec.c'),'-o',str(oracle)]
 execute('sdk-compiler',[compiler,'--version']);execute('sdk-build',command,custom_env=native_env);(out/'sdk-build-command.json').write_text(json.dumps(command,indent=2))
 execute('sdk-compare',['python3','-B',str(root/'eng/validation/archive-writing/sevenzip-feasibility/verify_lzma_end_sdk.py'),str(out/'vectors'),str(oracle),str(out/'sdk-comparison')])
 deps=root/'src/FileCat.Archives.Writing/bin/Release/net10.0/FileCat.Archives.Writing.deps.json';libraries=json.loads(deps.read_text())['libraries'];assert all('Rar15' not in name and 'DiscUtils' not in name for name in libraries)
 shutil.copyfile(deps,out/deps.name);(out/'writing-closure.json').write_text(json.dumps({'libraries':sorted(libraries),'Rar15_or_DiscUtils_present':False},indent=2))
 for v in ['v2','v3']:shutil.copyfile(task/('lzma-end-source-'+v+'.tar.gz'),out/('lzma-end-source-'+v+'.tar.gz'))
 shutil.copyfile(Path(__file__),out/'executed-finisher.py');(out/'machine.json').write_text(json.dumps({'platform':platform.platform(),'sdk':subprocess.check_output([dotnet,'--info'],env=env,text=True),'new_profile_registered':False},indent=2))
 idle();census={'busy':False,'owned_filecat_or_test_processes':0}
 if mac:
  wake=subprocess.check_output(['/bin/ps','-p','36765','-o','pid=,command='],text=True);assert wake.strip();census['preserved_wake_helper']=wake.strip()
 (out/'final-census.json').write_text(json.dumps(census,indent=2));(out/'complete.txt').write_text('PASS: exact LZMA/LZMA2 endings, official SDK differential, broad affected regression plus two fixture-only corrections. Catalog/transaction/UI/qualification remain open.\n')
 rows=[]
 for p in sorted(out.rglob('*')):
  assert not p.is_symlink(),str(p)
  if p.is_file():rows.append({'path':p.relative_to(out).as_posix(),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
 (out/'files.json').write_text(json.dumps({'files':rows,'production_backend_qualified':False},indent=2));archive=task/'lzma-end-evidence-v3.tar.gz'
 with tarfile.open(archive,'x:gz',dereference=False) as tar:
  for p in sorted(out.rglob('*')):tar.add(p,arcname=out.name+'/'+p.relative_to(out).as_posix(),recursive=False)
 facts={'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'rows':len(rows)}
 (task/'lzma-end-evidence-v3.json').write_text(json.dumps(facts,indent=2));print(json.dumps(facts),flush=True)

if __name__=='__main__':run(*sys.argv[1:])
