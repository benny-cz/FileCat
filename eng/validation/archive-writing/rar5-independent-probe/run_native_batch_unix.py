#!/usr/bin/env python3
"""Guarded RAR5 native capture, sealing, creation and broad archive regression."""
from pathlib import Path
import hashlib,json,os,platform,shutil,subprocess,sys,tarfile

def run(task,packet_sha):
 task=Path(task).resolve();root=task/'rar5-native-source-v3';out=task/'rar5-native-v3'
 def idle():subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
 idle();root.mkdir();out.mkdir()
 packet=task/'rar5-native-source-v3.tar.gz'
 assert hashlib.sha256(packet.read_bytes()).hexdigest()==packet_sha
 with tarfile.open(packet,'r:gz') as tar:tar.extractall(root,filter='data')
 mac=sys.platform=='darwin';sdk=Path('/Users/benny/.dotnet') if mac else task/'dotnet'
 env=dict(os.environ,DOTNET_ROOT=str(sdk),PATH=str(sdk)+os.pathsep+os.environ['PATH'],DOTNET_CLI_TELEMETRY_OPTOUT='1',
  MSBUILDDISABLENODEREUSE='1',FILECAT_ARCHIVE_CHILD_DIAGNOSTICS_DIRECTORY=str(out/'child-diagnostics'))
 (out/'child-diagnostics').mkdir()
 env['FILECAT_RAR_WORKER_EXPORT_ROOT']=str(out/'vectors')
 env['FILECAT_RAR_NATIVE_EXPORT_ROOT']=str(out/'native-vectors')
 def execute(name,args,cwd=root,custom_env=env):
  with (out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=cwd,env=custom_env,stdout=log,stderr=subprocess.STDOUT,check=True)
 dotnet=str(sdk/'dotnet')
 execute('dedicated-build',[dotnet,'build','src/FileCat.ArchiveWorker/FileCat.ArchiveWorker.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'])
 execute('writing-build',[dotnet,'build','src/FileCat.Archives.Writing/FileCat.Archives.Writing.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'])
 selected='FullyQualifiedName~FileCat.Core.Tests.Archive|FullyQualifiedName~FileCat.Core.Tests.Strict|FullyQualifiedName~FileCat.Core.Tests.SevenZip|FullyQualifiedName~FileCat.Core.Tests.Rar|FullyQualifiedName~FileCat.Core.Tests.EmptySevenZip|FullyQualifiedName~FileCat.Core.Tests.UnixArchive|FullyQualifiedName~FileCat.Core.Tests.Zip'
 timer=['/usr/bin/time','-l'] if mac else ['/usr/bin/time','-v']
 execute('tests',timer+[dotnet,'test','tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','--filter',selected,'--logger','trx;LogFileName=archive-worker-secrets.trx','--results-directory',str(out/'results')])
 execute('rar-independent',['python3','-B',str(root/'eng/validation/archive-writing/rar5-independent-probe/check_native_batch.py'),str(root),str(out),str(task)])
 idle()
 deps=root/'src/FileCat.Archives.Writing/bin/Release/net10.0/FileCat.Archives.Writing.deps.json'
 libraries=json.loads(deps.read_text())['libraries'];assert all('Rar15' not in name and 'DiscUtils' not in name for name in libraries)
 shutil.copyfile(deps,out/deps.name)
 (out/'writing-closure.json').write_text(json.dumps({'libraries':sorted(libraries),'Rar15_or_DiscUtils_present':False},indent=2))
 binaries=out/'binaries';binaries.mkdir()
 for folder in (root/'tests/FileCat.Core.Tests/bin/Release/net10.0',root/'src/FileCat.Archives.Writing/bin/Release/net10.0',root/'tests/FileCat.WorkerProbe/bin/Release/net10.0',root/'src/FileCat.ArchiveWorker/bin/Release/net10.0'):
  for p in folder.iterdir():
   if p.is_file() and p.name.startswith('FileCat.') and p.suffix in ('.dll','.pdb'):shutil.copyfile(p,binaries/p.name)
 execute('audit-build',[dotnet,'build','eng/validation/archive-writing/PdbSourceAudit/PdbSourceAudit.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'])
 execute('source-audit',[dotnet,str(root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'),str(root),str(binaries),str(out/'compiler-sources'),str(Path.home()/'.nuget/packages')])
 shutil.copyfile(packet,out/packet.name);shutil.copyfile(Path(__file__),out/'executed-batch.py')
 (out/'machine.json').write_text(json.dumps({'platform':platform.platform(),'sdk':subprocess.check_output([dotnet,'--info'],env=env,text=True),'new_profile_registered':False},indent=2))
 idle();census={'busy':False,'owned_filecat_or_test_processes':0}
 if mac:
  wake=subprocess.check_output(['/bin/ps','-p','36765','-o','pid=,command='],text=True);assert wake.strip();census['preserved_wake_helper']=wake.strip()
 (out/'final-census.json').write_text(json.dumps(census,indent=2))
 (out/'complete.txt').write_text('PASS: RAR native Unix capture, sealing and creation with fixed-protocol subprocesses, independent metadata/native readers and broad regression. Unix confined production launch/update and UI qualification remain open.\n')
 rows=[]
 for p in sorted(out.rglob('*')):
  assert not p.is_symlink(),str(p)
  if p.is_file():rows.append({'path':p.relative_to(out).as_posix(),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
 (out/'files.json').write_text(json.dumps({'files':rows,'production_backend_qualified':False},indent=2))
 archive=task/'rar5-native-evidence-v3.tar.gz'
 with tarfile.open(archive,'x:gz',dereference=False) as tar:
  for p in sorted(out.rglob('*')):tar.add(p,arcname=out.name+'/'+p.relative_to(out).as_posix(),recursive=False)
 facts={'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'rows':len(rows)}
 (task/'rar5-native-evidence-v3.json').write_text(json.dumps(facts,indent=2));print(json.dumps(facts),flush=True)

if __name__=='__main__':run(sys.argv[1],sys.argv[2])
