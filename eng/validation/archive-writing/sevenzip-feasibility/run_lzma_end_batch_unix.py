#!/usr/bin/env python3
"""Guarded exact LZMA/LZMA2 endings, SDK differential and affected regression/source evidence session."""
from pathlib import Path
import hashlib,json,os,platform,shutil,subprocess,sys,tarfile

def run(task,packet_sha):
 task=Path(task).resolve();root=task/'lzma-end-source-v2';out=task/'lzma-end-v2'
 def idle():subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
 idle();root.mkdir();out.mkdir()
 packet=task/'lzma-end-source-v2.tar.gz'
 assert hashlib.sha256(packet.read_bytes()).hexdigest()==packet_sha
 with tarfile.open(packet,'r:gz') as tar:tar.extractall(root,filter='data')
 mac=sys.platform=='darwin';sdk=Path('/Users/benny/.dotnet') if mac else task/'dotnet'
 env=dict(os.environ,DOTNET_ROOT=str(sdk),PATH=str(sdk)+os.pathsep+os.environ['PATH'],DOTNET_CLI_TELEMETRY_OPTOUT='1',
  MSBUILDDISABLENODEREUSE='1',FILECAT_ARCHIVE_CHILD_DIAGNOSTICS_DIRECTORY=str(out/'child-diagnostics'))
 (out/'child-diagnostics').mkdir()
 env['FILECAT_LZMA_END_EXPORT_ROOT']=str(out/'vectors')
 def execute(name,args,cwd=root,custom_env=env):
  with (out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=cwd,env=custom_env,stdout=log,stderr=subprocess.STDOUT,check=True)
 dotnet=str(sdk/'dotnet')
 execute('writing-build',[dotnet,'build','src/FileCat.Archives.Writing/FileCat.Archives.Writing.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'])
 selected='FullyQualifiedName~StrictLzmaStreamTests|FullyQualifiedName~StrictSevenZipFramingTests|FullyQualifiedName~StrictRar5CatalogTests|FullyQualifiedName~Rar5WritingBoundaryTests|FullyQualifiedName~StrictRar5FramingTests|FullyQualifiedName~SevenZipBcj2WritingTests|FullyQualifiedName~ArchiveWorkerSecretTests|FullyQualifiedName~ArchiveWorkerTransportSecretTests|FullyQualifiedName~ArchiveWorkerCancellationTests|FullyQualifiedName~ArchiveAssessmentClientTests|FullyQualifiedName~ArchiveVerificationClientTests|FullyQualifiedName~ArchiveManifestWireTests|FullyQualifiedName~ArchiveProxyTests|FullyQualifiedName~ArchiveWorkerTrafficPolicyTests|FullyQualifiedName~ArchivePasswordPolicyTests|FullyQualifiedName~ArchivePasswordWritingTests|FullyQualifiedName~ArchiveAesCbcWritingTests|FullyQualifiedName~SevenZipCompressedHeaderWritingTests|FullyQualifiedName~SevenZipFilterEncodingTests|FullyQualifiedName~SevenZipWritingTests|FullyQualifiedName~ArchivePasswordPrimitiveTests|FullyQualifiedName~Rar5WritingTests|FullyQualifiedName~RarReaderTests|FullyQualifiedName~SevenZipCrcTests|FullyQualifiedName~SevenZipPpmdTests|FullyQualifiedName~SevenZipHeaderBudgetTests|FullyQualifiedName~SevenZipEncodedHeaderTests|FullyQualifiedName~EmptySevenZipTests|FullyQualifiedName~ArchiveWriteBoundaryTests|FullyQualifiedName~ArchiveCapturedInputsTests|FullyQualifiedName~ArchiveDirectoryWireTests|FullyQualifiedName~ArchiveTransform|FullyQualifiedName~ArchiveCreationOriginTests|FullyQualifiedName~UnixArchiveStorageTests'
 timer=['/usr/bin/time','-l'] if mac else ['/usr/bin/time','-v']
 execute('tests',timer+[dotnet,'test','tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','--filter',selected,'--logger','trx;LogFileName=archive-worker-secrets.trx','--results-directory',str(out/'results')])
 tools=out/'tools';tools.mkdir();tools_packet=task/'lzma-end-tools-v1.tar.gz'
 assert hashlib.sha256(tools_packet.read_bytes()).hexdigest()=='aa7ee1ff6f335e52a8bcee38d04e645112957b83298ca5994e03a1654f76e2ee'
 with tarfile.open(tools_packet,'r:gz') as tar:tar.extractall(tools,filter='data')
 for row in json.loads((tools/'files.json').read_text())['files']:
  p=tools/row['path'];assert p.stat().st_size==row['bytes'] and hashlib.sha256(p.read_bytes()).hexdigest()==row['sha256']
 c=tools/'sdk/C';compiler='/Library/Developer/CommandLineTools/usr/bin/clang' if mac else '/usr/bin/gcc';oracle=tools/'sdk-lzma-end-oracle'
 native_env=dict(env)
 if mac:native_env['SDKROOT']='/Library/Developer/CommandLineTools/SDKs/MacOSX27.0.sdk'
 execute('sdk-compiler',[compiler,'--version'])
 command=[compiler,'-std=c11','-O2','-I',str(c),str(root/'eng/validation/archive-writing/sevenzip-feasibility/sdk_lzma_end_oracle.c'),str(c/'LzmaDec.c'),str(c/'Lzma2Dec.c'),'-o',str(oracle)]
 execute('sdk-build',command,custom_env=native_env);(out/'sdk-build-command.json').write_text(json.dumps(command,indent=2))
 execute('sdk-compare',['python3','-B',str(root/'eng/validation/archive-writing/sevenzip-feasibility/verify_lzma_end_sdk.py'),str(out/'vectors'),str(oracle),str(out/'sdk-comparison')])
 idle()
 deps=root/'src/FileCat.Archives.Writing/bin/Release/net10.0/FileCat.Archives.Writing.deps.json'
 libraries=json.loads(deps.read_text())['libraries'];assert all('Rar15' not in name and 'DiscUtils' not in name for name in libraries)
 shutil.copyfile(deps,out/deps.name)
 (out/'writing-closure.json').write_text(json.dumps({'libraries':sorted(libraries),'Rar15_or_DiscUtils_present':False},indent=2))
 binaries=out/'binaries';binaries.mkdir()
 for folder in (root/'tests/FileCat.Core.Tests/bin/Release/net10.0',root/'src/FileCat.Archives.Writing/bin/Release/net10.0',root/'tests/FileCat.WorkerProbe/bin/Release/net10.0'):
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
 (out/'complete.txt').write_text('PASS: exact LZMA/LZMA2 marked/markerless codec endings and SDK differential plus affected regressions. Other codec endings, catalog/workflow/qualification remain open.\n')
 rows=[]
 for p in sorted(out.rglob('*')):
  assert not p.is_symlink(),str(p)
  if p.is_file():rows.append({'path':p.relative_to(out).as_posix(),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
 (out/'files.json').write_text(json.dumps({'files':rows,'production_backend_qualified':False},indent=2))
 archive=task/'lzma-end-evidence-v2.tar.gz'
 with tarfile.open(archive,'x:gz',dereference=False) as tar:
  for p in sorted(out.rglob('*')):tar.add(p,arcname=out.name+'/'+p.relative_to(out).as_posix(),recursive=False)
 facts={'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'rows':len(rows)}
 (task/'lzma-end-evidence-v2.json').write_text(json.dumps(facts,indent=2));print(json.dumps(facts),flush=True)

if __name__=='__main__':run(sys.argv[1],sys.argv[2])
