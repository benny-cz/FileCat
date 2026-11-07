#!/usr/bin/env python3
"""One guarded build/regression/independent-codec/source-evidence session per authorized Mac or Ubuntu machine."""
from pathlib import Path
import hashlib,json,os,platform,shutil,subprocess,sys,tarfile

def run(task,packet_sha):
 task=Path(task).resolve();root=task/'sevenzip-headers-source-v1';out=task/'sevenzip-headers-v1'
 def idle():subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
 idle();root.mkdir();out.mkdir()
 packet=task/'sevenzip-headers-source-v1.tar.gz'
 assert hashlib.sha256(packet.read_bytes()).hexdigest()==packet_sha
 with tarfile.open(packet,'r:gz') as tar:tar.extractall(root,filter='data')
 mac=sys.platform=='darwin';sdk=Path('/Users/benny/.dotnet') if mac else task/'dotnet'
 env=dict(os.environ,DOTNET_ROOT=str(sdk),PATH=str(sdk)+os.pathsep+os.environ['PATH'],DOTNET_CLI_TELEMETRY_OPTOUT='1',
  MSBUILDDISABLENODEREUSE='1',FILECAT_SEVENZIP_CODEC_EXPORT_ROOT=str(out/'oracles'),FILECAT_SEVENZIP_FILTER_EXPORT_ROOT=str(out/'vectors'))
 (out/'oracles').mkdir()
 def execute(name,args,cwd=root,custom_env=env):
  with (out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=cwd,env=custom_env,stdout=log,stderr=subprocess.STDOUT,check=True)
 dotnet=str(sdk/'dotnet')
 execute('writing-build',[dotnet,'build','src/FileCat.Archives.Writing/FileCat.Archives.Writing.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'])
 selected='FullyQualifiedName~SevenZipCompressedHeaderWritingTests|FullyQualifiedName~SevenZipFilterEncodingTests|FullyQualifiedName~SevenZipWritingTests|FullyQualifiedName~ArchivePasswordPrimitiveTests|FullyQualifiedName~Rar5WritingTests|FullyQualifiedName~RarReaderTests|FullyQualifiedName~SevenZipCrcTests|FullyQualifiedName~SevenZipPpmdTests|FullyQualifiedName~SevenZipHeaderBudgetTests|FullyQualifiedName~SevenZipEncodedHeaderTests|FullyQualifiedName~EmptySevenZipTests|FullyQualifiedName~ArchiveWriteBoundaryTests|FullyQualifiedName~ArchiveCapturedInputsTests|FullyQualifiedName~ArchiveDirectoryWireTests|FullyQualifiedName~ArchiveTransform|FullyQualifiedName~ArchiveCreationOriginTests|FullyQualifiedName~UnixArchiveStorageTests'
 timer=['/usr/bin/time','-l'] if mac else ['/usr/bin/time','-v']
 execute('tests',timer+[dotnet,'test','tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','--filter',selected,'--logger','trx;LogFileName=sevenzip-ppmd-filter.trx','--results-directory',str(out/'results')])
 idle()
 tools=out/'tools';previous=task/'sevenzip-ppmd-filter-v2'
 shutil.copytree(previous/'tools',tools)
 for row in json.loads((tools/'files.json').read_text())['files']:
  p=tools/row['path'];assert p.stat().st_size==row['bytes'] and hashlib.sha256(p.read_bytes()).hexdigest()==row['sha256']
 oracle=out/'sdk-oracle';shutil.copyfile(previous/'sdk-oracle',oracle);oracle.chmod(0o700)
 assert hashlib.sha256(oracle.read_bytes()).hexdigest()==json.loads((previous/'vectors/sdk-reader.json').read_text())['oracle_sha256']
 (out/'test-tools-reused.json').write_text(json.dumps({'source':str(previous),'sdk_oracle_sha256':hashlib.sha256(oracle.read_bytes()).hexdigest(),'production_dependency':False},indent=2))
 execute('sdk-compare',['python3',str(root/'eng/validation/archive-writing/sevenzip-feasibility/verify_sdk.py'),str(out/'vectors'),str(oracle)])
 tool=tools/('mac' if mac else 'linux')/'7zz';tool.chmod(0o700)
 execute('official',['python3',str(root/'eng/validation/archive-writing/sevenzip-feasibility/verify_header_cli.py'),str(out/'oracles'),str(tool)])
 prefix=task/'sevenzip-crypto-v1/native-oracle/install/lib'
 pattern='libarchive.*.dylib' if mac else 'libarchive.so.*'
 libraries=[p for p in prefix.glob(pattern) if p.is_file() and not p.is_symlink()];assert len(libraries)==1;library=libraries[0]
 execute('native',['python3',str(root/'eng/validation/archive-writing/sevenzip-feasibility/verify_header_native.py'),str(out/'oracles'),str(library)])
 execute('native-linked-libraries',['/usr/bin/otool','-L',str(library)] if mac else ['/usr/bin/ldd',str(library)])
 (out/'native-reuse.json').write_text(json.dumps({'library':str(library),'sha256':hashlib.sha256(library.read_bytes()).hexdigest(),
  'configured_evidence':str(task/'sevenzip-crypto-v1/native-oracle/build-facts.json'),'secondary_reader_unavailable_filters':['RiscV','Swap2','Swap4'],
  'all_filter_containers_require_official_7zip':True},indent=2))
 binaries=out/'binaries';binaries.mkdir()
 for folder in (root/'tests/FileCat.Core.Tests/bin/Release/net10.0',root/'src/FileCat.Archives.Writing/bin/Release/net10.0'):
  for p in folder.iterdir():
   if p.is_file() and p.name.startswith('FileCat.') and p.suffix in ('.dll','.pdb'):shutil.copyfile(p,binaries/p.name)
 execute('audit-build',[dotnet,'build','eng/validation/archive-writing/PdbSourceAudit/PdbSourceAudit.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'])
 execute('source-audit',[dotnet,str(root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'),str(root),str(binaries),str(out/'compiler-sources'),str(Path.home()/'.nuget/packages')])
 shutil.copyfile(packet,out/packet.name);shutil.copyfile(Path(__file__),out/'executed-batch.py')
 (out/'machine.json').write_text(json.dumps({'platform':platform.platform(),'sdk':subprocess.check_output([dotnet,'--info'],env=env,text=True),'new_profile_registered':False},indent=2))
 idle();(out/'final-census.json').write_text(json.dumps({'busy':False,'owned_filecat_or_test_processes':0}))
 rows=[]
 for p in sorted(out.rglob('*')):
  if p.is_symlink():continue
  if p.is_file():rows.append({'path':p.relative_to(out).as_posix(),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
 (out/'files.json').write_text(json.dumps({'files':rows,'production_backend_qualified':False},indent=2))
 archive=task/'sevenzip-headers-evidence-v1.tar.gz'
 with tarfile.open(archive,'x:gz',dereference=False) as tar:
  for p in sorted(out.rglob('*')):
   if not p.is_symlink():tar.add(p,arcname=out.name+'/'+p.relative_to(out).as_posix(),recursive=False)
 facts={'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'rows':len(rows)}
 (task/'sevenzip-headers-evidence-v1.json').write_text(json.dumps(facts,indent=2));print(json.dumps(facts),flush=True)

if __name__=='__main__':
 if len(sys.argv)!=3:raise SystemExit('Usage: run_batch_unix.py OWNED_TASK SOURCE_PACKET_SHA256')
 run(sys.argv[1],sys.argv[2])
