#!/usr/bin/env python3
"""Guarded, consolidated RAR C# codec/regression/native evidence batch on an owned Mac/Linux test machine."""
from pathlib import Path
import hashlib,json,os,platform,shutil,subprocess,sys,tarfile

def run(task,packet_sha,batch,resume=False):
 import re
 if not re.fullmatch(r'v[1-9][0-9]{0,3}',batch):raise ValueError('Invalid batch identity')
 task=Path(task).resolve();root=task/('rar5-csharp-source-'+batch);out=task/('rar5-csharp-'+batch)
 def idle():subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
 idle()
 if not resume:out.mkdir();root.mkdir()
 packet=task/('rar5-csharp-source-'+batch+'.tar.gz')
 assert hashlib.sha256(packet.read_bytes()).hexdigest()==packet_sha
 if not resume:
  with tarfile.open(packet,'r:gz') as tar:tar.extractall(root,filter='data')
 mac=sys.platform=='darwin';sdk=Path('/Users/benny/.dotnet') if mac else task/'dotnet'
 env=dict(os.environ,DOTNET_ROOT=str(sdk),PATH=str(sdk)+os.pathsep+os.environ['PATH'],
  DOTNET_CLI_TELEMETRY_OPTOUT='1',MSBUILDDISABLENODEREUSE='1',FILECAT_RAR5_CODEC_EXPORT_ROOT=str(out/'oracles'))
 if not resume:(out/'oracles').mkdir()
 def execute(name,args,cwd=root,extra_env=env):
  with (out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=cwd,env=extra_env,stdout=log,stderr=subprocess.STDOUT,check=True)
 dotnet=str(sdk/'dotnet')
 if resume:
  import xml.etree.ElementTree as ET
  trx=ET.parse(out/'results/rar5-csharp.trx');c=next(x for x in trx.iter() if x.tag.endswith('}Counters'))
  assert int(c.attrib['failed'])==0 and int(c.attrib['passed'])>=26
 else:
  execute('writing-build',[dotnet,'build','src/FileCat.Archives.Writing/FileCat.Archives.Writing.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'])
  selected='FullyQualifiedName~Rar5WritingTests|FullyQualifiedName~RarReaderTests|FullyQualifiedName~ArchiveWriteBoundaryTests|FullyQualifiedName~ArchiveCapturedInputsTests|FullyQualifiedName~ArchiveDirectoryWireTests|FullyQualifiedName~ArchiveTransform|FullyQualifiedName~ArchiveCreationOriginTests|FullyQualifiedName~UnixArchiveStorageTests'
  timer=['/usr/bin/time','-l'] if mac else ['/usr/bin/time','-v']
  execute('tests',timer+[dotnet,'test','tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','--filter',selected,'--logger','trx;LogFileName=rar5-csharp.trx','--results-directory',str(out/'results')])
 idle()
 if mac:library=task/'rar5-native-oracle-v1/install/lib/libarchive.13.dylib'
 else:
  # Exact private oracle, not an installed production dependency. System readers can discard RAR nanoseconds.
  source_archive=task/'libarchive-3.8.11.tar.xz'
  assert source_archive.stat().st_size==6754236
  assert hashlib.sha256(source_archive.read_bytes()).hexdigest()=='a3dc0872b1b39da34dc18ef1b944f0f11ffd37425d70ae04be679279f1cf43d2'
  if resume and (out/'native-oracle/build-facts.json').is_file():
   oracle=out/'native-oracle';facts=json.loads((oracle/'build-facts.json').read_text());library=Path(facts['library'])
   assert hashlib.sha256(library.read_bytes()).hexdigest()==facts['library_sha256']
  else:
   oracle=out/'native-oracle';(oracle/'source').mkdir(parents=True,exist_ok=True);(oracle/'build').mkdir(exist_ok=True)
   if not (oracle/'source/libarchive-3.8.11/configure').is_file():
    with tarfile.open(source_archive,'r:xz') as tar:tar.extractall(oracle/'source',filter='data')
   source=oracle/'source/libarchive-3.8.11';prefix=oracle/'install'
   options=['--prefix='+str(prefix),'--disable-static','--enable-shared','--without-zlib','--without-bz2lib','--without-lzma','--without-lzo2','--without-lz4','--without-zstd','--without-openssl','--without-nettle','--without-xml2','--without-expat','--disable-bsdtar','--disable-bsdcpio','--disable-bsdcat','--disable-bsdunzip','--disable-acl','--disable-xattr']
   native_env=dict(env,CC='/usr/bin/gcc',CFLAGS='-O2')
   execute('oracle-configure-resumed' if resume else 'oracle-configure',['/bin/sh',str(source/'configure')]+options,oracle/'build',native_env)
   execute('oracle-make',['/usr/bin/make','-j2'],oracle/'build',native_env)
   execute('oracle-install',['/usr/bin/make','install'],oracle/'build',native_env)
   library=prefix/'lib/libarchive.so.13.8.11'
   if not library.is_file():
    matches=[p for p in (prefix/'lib').glob('libarchive.so.*') if not p.is_symlink()];assert len(matches)==1;library=matches[0]
   (oracle/'build-facts.json').write_text(json.dumps({'archive_sha256':hashlib.sha256(source_archive.read_bytes()).hexdigest(),'configure':options,'library':str(library),'library_sha256':hashlib.sha256(library.read_bytes()).hexdigest(),'production_dependency':False},indent=2))
 checker=Path(__file__).resolve().with_name('verify_csharp_native.py') if resume else root/'eng/validation/archive-writing/rar5-independent-probe/verify_csharp_native.py'
 shutil.copyfile(checker,out/'executed-native-check.py');shutil.copyfile(Path(__file__),out/'executed-batch.py')
 if not (out/'oracles/native-reader.json').is_file():
  execute('native-resumed' if resume else 'native',['python3',str(checker),str(out/'oracles'),str(library)])
 else:assert len(json.loads((out/'oracles/native-reader.json').read_text())['verified'])==84
 # Keep exactly the built assemblies/symbols and the compiler's source snapshots with the outputs.
 binaries=out/'binaries';binaries.mkdir(exist_ok=True)
 for folder in (root/'tests/FileCat.Core.Tests/bin/Release/net10.0',root/'src/FileCat.Archives.Writing/bin/Release/net10.0'):
  for p in folder.iterdir():
   if p.is_file() and p.name.startswith('FileCat.') and p.suffix in ('.dll','.pdb'):shutil.copyfile(p,binaries/p.name)
 execute('audit-build-v3',[dotnet,'build','eng/validation/archive-writing/PdbSourceAudit/PdbSourceAudit.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'])
 execute('source-audit-v3',[dotnet,str(root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'),str(root),str(binaries),str(out/'compiler-sources-v3'),str(Path.home()/'.nuget/packages')])
 shutil.copyfile(packet,out/packet.name)
 shutil.copyfile(root/'eng/validation/archive-writing/PdbSourceAudit/Program.cs',out/'executed-source-audit.cs')
 (out/'machine.json').write_text(json.dumps({'platform':platform.platform(),'sdk':subprocess.check_output([dotnet,'--info'],env=env,text=True),'new_writer_registered':False},indent=2))
 idle();(out/'final-census.json').write_text(json.dumps({'busy':False,'owned_filecat_or_test_processes':0}))
 # Freeze immutable evidence after tests, verification and owned-process reaping. Include no symlinks.
 rows=[]
 for p in sorted(out.rglob('*')):
  if p.is_symlink():continue
  if p.is_file():rows.append({'path':p.relative_to(out).as_posix(),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
 (out/'files.json').write_text(json.dumps({'files':rows,'production_backend_qualified':False},indent=2))
 archive=task/('rar5-csharp-evidence-'+batch+'.tar.gz')
 with tarfile.open(archive,'x:gz',dereference=False) as tar:
  for p in sorted(out.rglob('*')):
   if not p.is_symlink():tar.add(p,arcname=out.name+'/'+p.relative_to(out).as_posix(),recursive=False)
 facts={'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'rows':len(rows)}
 (task/('rar5-csharp-evidence-'+batch+'.json')).write_text(json.dumps(facts,indent=2));print(json.dumps(facts),flush=True)

if __name__=='__main__':
 if len(sys.argv) not in (4,5):raise SystemExit('Usage: run_csharp_unix.py OWNED_TASK SOURCE_PACKET_SHA256 BATCH [--continue-after-tests]')
 if len(sys.argv)==5 and sys.argv[4]!='--continue-after-tests':raise SystemExit(64)
 run(sys.argv[1],sys.argv[2],sys.argv[3],len(sys.argv)==5)
