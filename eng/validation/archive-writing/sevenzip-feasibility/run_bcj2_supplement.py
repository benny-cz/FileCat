#!/usr/bin/env python3
"""One guarded BCJ2 failure-cleanup and independent-graph batch, using frozen v2 fixtures."""
from pathlib import Path
import hashlib,json,os,platform,shutil,subprocess,sys,tarfile

def run(task,sha,prepared=False):
 task=Path(task);windows=sys.platform=='win32';mac=sys.platform=='darwin'
 root=task/'sevenzip-bcj2-supplement-source-v3';out=task/'sevenzip-bcj2-supplement-v3';base=task/'sevenzip-bcj2-v2'
 def idle():
  command=['powershell','-NoProfile','-Command',"if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){exit 1}"] if windows else ['python3',str(task/'check-linux-archive-idle.py')]
  subprocess.run(command,check=True)
 idle();packet=task/'sevenzip-bcj2-supplement-source-v3.tar.gz';assert hashlib.sha256(packet.read_bytes()).hexdigest()==sha
 if not prepared:
  root.mkdir();out.mkdir()
  with tarfile.open(packet,'r:gz') as tar:tar.extractall(root,filter='data')
 sdk=Path('/Users/benny/.dotnet') if mac else task/'dotnet';dotnet=str(sdk/('dotnet.exe' if windows else 'dotnet'))
 env=dict(os.environ,DOTNET_ROOT=str(sdk),PATH=str(sdk)+os.pathsep+os.environ['PATH'],MSBUILDDISABLENODEREUSE='1',PYTHONDONTWRITEBYTECODE='1')
 for name in list(env):
  if name.startswith('FILECAT_'):env.pop(name)
 def execute(name,args,custom=env):
  with (out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=root,env=custom,stdout=log,stderr=subprocess.STDOUT,check=True)
 audit=root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'
 if not windows:
  for name,project in [('writing','src/FileCat.Archives.Writing/FileCat.Archives.Writing.csproj'),('tests','tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj'),('audit','eng/validation/archive-writing/PdbSourceAudit/PdbSourceAudit.csproj')]:
   execute(name+'-build',[dotnet,'build',project,'-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'])
 if windows:
  execute('tests',['C:\\Program Files\\dotnet\\dotnet.exe','exec',str(Path('C:/Users/fcarchive42829297/FileCatValidation/Capture-42829297-v7/tester/vstest.console.dll')),str(root/'tests/FileCat.Core.Tests/bin/Release/net10.0/FileCat.Core.Tests.dll'),'/TestCaseFilter:FullyQualifiedName~SevenZipBcj2WritingTests','/Logger:trx;LogFileName=bcj2-supplement.trx','/ResultsDirectory:'+str(out/'results')])
 else:
  execute('tests',[dotnet,'test','tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj','-c','Release','--no-build','--no-restore','--filter','FullyQualifiedName~SevenZipBcj2WritingTests','--logger','trx;LogFileName=bcj2-supplement.trx','--results-directory',str(out/'results')])
 idle();assert (base/'complete.txt').exists()
 manifest=json.loads((base/'files.json').read_text(encoding='utf-8-sig'));pinned={r['path']:r for r in manifest['files']}
 fixtures=out/'fixtures';fixtures.mkdir()
 for folder in sorted((base/'bcj2').glob('archive-*')):
  destination=fixtures/folder.name;destination.mkdir()
  for name in ['members.7z','members.json','profile.json']:
   original=folder/name;row=pinned[original.relative_to(base).as_posix()]
   assert original.stat().st_size==row['bytes'] and hashlib.sha256(original.read_bytes()).hexdigest().lower()==row['sha256'].lower()
   shutil.copyfile(original,destination/name)
 (out/'fixture-origin.json').write_text(json.dumps({'source_packet_sha256':'dff9c8d2b39596340d40e55d4aeb3a7f18b585c4b58b3c27b4a5e8a437197c06','manifest_sha256':hashlib.sha256((base/'files.json').read_bytes()).hexdigest(),'root':str(base),'fixtures':42},indent=2))
 helpers=root/'eng/validation/archive-writing/sevenzip-feasibility';tools=out/'tools';tools.mkdir()
 ledger=json.loads((helpers/'ppmd-filter-sources.json').read_text());c=tools/'sdk/C';c.mkdir(parents=True)
 for row in ledger['files']:
  name=row['path'].split('/')[-1];source=base/'tools/sdk/C'/name
  assert hashlib.sha256(source.read_bytes()).hexdigest()==row['sha256'];shutil.copyfile(source,c/name)
 oracle=tools/('sdk-bcj2-decode-oracle.exe' if windows else 'sdk-bcj2-decode-oracle')
 if windows:
  source=task/'sdk-bcj2-decode-oracle-v1.exe';assert hashlib.sha256(source.read_bytes()).hexdigest()=='172c4f63495aaadd4f83b3581b614c2367932130d23e7844dbfc5fdab42e3a93';shutil.copyfile(source,oracle)
 else:
  compiler='/Library/Developer/CommandLineTools/usr/bin/clang' if mac else '/usr/bin/gcc';custom=dict(env)
  if mac:custom['SDKROOT']='/Library/Developer/CommandLineTools/SDKs/MacOSX27.0.sdk'
  command=[compiler,'-std=c11','-O2','-I',str(c),str(helpers/'sdk_bcj2_decode_oracle.c'),str(c/'Bcj2.c'),str(c/'CpuArch.c'),'-o',str(oracle)]
  execute('sdk-build',command,custom);execute('sdk-compiler',[compiler,'--version']);(out/'sdk-build-command.json').write_text(json.dumps(command,indent=2))
 reader_tools=task/'password-oracle-tools-v1'
 execute('reader-module-reuse',[sys.executable,'-B',str(helpers/'verify_oracle_module_reuse.py'),str(reader_tools),'windows' if windows else 'mac' if mac else 'ubuntu'])
 execute('independent-graphs',[sys.executable,'-B',str(helpers/'verify_bcj2_graph.py'),str(fixtures),str(oracle),str(reader_tools/'modules'),str(out/'independent-graphs')])
 binaries=out/'binaries';binaries.mkdir()
 for folder in (root/'tests/FileCat.Core.Tests/bin/Release/net10.0',root/'src/FileCat.Archives.Writing/bin/Release/net10.0',root/'tests/FileCat.WorkerProbe/bin/Release/net10.0'):
  for path in folder.iterdir():
   if path.is_file() and path.name.startswith('FileCat.') and path.suffix in ('.dll','.pdb'):shutil.copyfile(path,binaries/path.name)
 if windows:
  print('PASS: standard-user tests/graphs; parent compiler-source audit and evidence finalization required.',flush=True)
  return
 nuget=Path('C:/Users/Admin/.nuget/packages') if windows else Path.home()/'.nuget/packages'
 execute('source-audit',[dotnet,str(audit),str(root),str(binaries),str(out/'compiler-sources'),str(nuget)])
 shutil.copyfile(packet,out/packet.name);shutil.copyfile(Path(__file__),out/'executed-batch.py')
 idle();census={'owned_filecat_or_test_processes':0,'platform':platform.platform()}
 if mac:census['preserved_wake_helper']=subprocess.check_output(['/bin/ps','-p','36765','-o','pid=,command='],text=True).strip();assert census['preserved_wake_helper']
 (out/'final-census.json').write_text(json.dumps(census,indent=2));(out/'complete.txt').write_text('PASS: focused BCJ2 cleanup/codec tests and 42 independent graph payloads. Full production qualification remains open.\n')
 rows=[{'path':p.relative_to(out).as_posix(),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(out.rglob('*')) if p.is_file()]
 (out/'files.json').write_text(json.dumps({'files':rows,'production_backend_qualified':False},indent=2))
 archive=task/'sevenzip-bcj2-supplement-evidence-v3.tar.gz'
 with tarfile.open(archive,'x:gz') as tar:tar.add(out,arcname=out.name)
 facts={'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'rows':len(rows)}
 (task/'sevenzip-bcj2-supplement-evidence-v3.json').write_text(json.dumps(facts,indent=2));print(json.dumps(facts),flush=True)

if __name__=='__main__':run(sys.argv[1],sys.argv[2],len(sys.argv)==4 and sys.argv[3]=='--windows-prepared')
