#!/usr/bin/env python3
"""One guarded build/regression/independent-codec/source-evidence session per authorized Mac or Ubuntu machine."""
from pathlib import Path
import hashlib,json,os,platform,shutil,subprocess,sys,tarfile

def run(task,packet_sha):
 task=Path(task).resolve();root=task/'sevenzip-crypto-source-v1';out=task/'sevenzip-crypto-v1'
 def idle():subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
 idle();root.mkdir();out.mkdir()
 packet=task/'sevenzip-crypto-source-v1.tar.gz'
 assert hashlib.sha256(packet.read_bytes()).hexdigest()==packet_sha
 with tarfile.open(packet,'r:gz') as tar:tar.extractall(root,filter='data')
 mac=sys.platform=='darwin';sdk=Path('/Users/benny/.dotnet') if mac else task/'dotnet'
 env=dict(os.environ,DOTNET_ROOT=str(sdk),PATH=str(sdk)+os.pathsep+os.environ['PATH'],DOTNET_CLI_TELEMETRY_OPTOUT='1',
  MSBUILDDISABLENODEREUSE='1',FILECAT_SEVENZIP_CODEC_EXPORT_ROOT=str(out/'oracles'))
 (out/'oracles').mkdir()
 def execute(name,args,cwd=root,custom_env=env):
  with (out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=cwd,env=custom_env,stdout=log,stderr=subprocess.STDOUT,check=True)
 dotnet=str(sdk/'dotnet')
 execute('writing-build',[dotnet,'build','src/FileCat.Archives.Writing/FileCat.Archives.Writing.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'])
 selected='FullyQualifiedName~SevenZipWritingTests|FullyQualifiedName~ArchivePasswordPrimitiveTests|FullyQualifiedName~Rar5WritingTests|FullyQualifiedName~RarReaderTests|FullyQualifiedName~SevenZipCrcTests|FullyQualifiedName~SevenZipPpmdTests|FullyQualifiedName~SevenZipHeaderBudgetTests|FullyQualifiedName~SevenZipEncodedHeaderTests|FullyQualifiedName~EmptySevenZipTests|FullyQualifiedName~ArchiveWriteBoundaryTests|FullyQualifiedName~ArchiveCapturedInputsTests|FullyQualifiedName~ArchiveDirectoryWireTests|FullyQualifiedName~ArchiveTransform|FullyQualifiedName~ArchiveCreationOriginTests|FullyQualifiedName~UnixArchiveStorageTests'
 timer=['/usr/bin/time','-l'] if mac else ['/usr/bin/time','-v']
 execute('tests',timer+[dotnet,'test','tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','--filter',selected,'--logger','trx;LogFileName=sevenzip-crypto.trx','--results-directory',str(out/'results')])
 idle()
 # Build an exact private independent reader with the codecs needed by this batch. The prior RAR-only reader
 # deliberately disabled these dependencies and cannot establish 7z method coverage.
 source_archive=task/'libarchive-3.8.11.tar.xz'
 assert source_archive.stat().st_size==6754236
 assert hashlib.sha256(source_archive.read_bytes()).hexdigest()=='a3dc0872b1b39da34dc18ef1b944f0f11ffd37425d70ae04be679279f1cf43d2'
 oracle=out/'native-oracle';(oracle/'source').mkdir(parents=True);(oracle/'build').mkdir()
 with tarfile.open(source_archive,'r:xz') as tar:tar.extractall(oracle/'source',filter='data')
 source=oracle/'source/libarchive-3.8.11';prefix=oracle/'install'
 options=['--prefix='+str(prefix),'--disable-static','--enable-shared','--with-zlib','--with-bz2lib','--with-lzma','--with-zstd',
 '--without-lzo2','--without-lz4','--without-openssl','--without-nettle','--without-xml2','--without-expat',
 '--disable-bsdtar','--disable-bsdcpio','--disable-bsdcat','--disable-bsdunzip','--disable-acl','--disable-xattr']
 if mac:
  sdk_path='/Library/Developer/CommandLineTools/SDKs/MacOSX27.0.sdk'
  native_env=dict(env,CC='/Library/Developer/CommandLineTools/usr/bin/clang',SDKROOT=sdk_path,CFLAGS='-O2 -isysroot '+sdk_path,
   CPPFLAGS='-I/opt/homebrew/include',LDFLAGS='-L/opt/homebrew/lib -Wl,-rpath,/opt/homebrew/lib',PKG_CONFIG_PATH='/opt/homebrew/lib/pkgconfig')
 else:native_env=dict(env,CC='/usr/bin/gcc',CFLAGS='-O2')
 execute('oracle-configure',['/bin/sh',str(source/'configure')]+options,oracle/'build',native_env)
 execute('oracle-make',['/usr/bin/make','-j2'],oracle/'build',native_env)
 execute('oracle-install',['/usr/bin/make','install'],oracle/'build',native_env)
 pattern='libarchive.*.dylib' if mac else 'libarchive.so.*'
 libraries=[p for p in (prefix/'lib').glob(pattern) if p.is_file() and not p.is_symlink()];assert len(libraries)==1;library=libraries[0]
 execute('native',['python3',str(root/'eng/validation/archive-writing/sevenzip-feasibility/verify_native.py'),str(out/'oracles'),str(library)])
 execute('native-linked-libraries',['/usr/bin/otool','-L',str(library)] if mac else ['/usr/bin/ldd',str(library)])
 (oracle/'build-facts.json').write_text(json.dumps({'archive_sha256':hashlib.sha256(source_archive.read_bytes()).hexdigest(),'configure':options,
 'library':str(library),'library_sha256':hashlib.sha256(library.read_bytes()).hexdigest(),'production_dependency':False},indent=2))
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
 archive=task/'sevenzip-crypto-evidence-v1.tar.gz'
 with tarfile.open(archive,'x:gz',dereference=False) as tar:
  for p in sorted(out.rglob('*')):
   if not p.is_symlink():tar.add(p,arcname=out.name+'/'+p.relative_to(out).as_posix(),recursive=False)
 facts={'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'rows':len(rows)}
 (task/'sevenzip-crypto-evidence-v1.json').write_text(json.dumps(facts,indent=2));print(json.dumps(facts),flush=True)

if __name__=='__main__':
 if len(sys.argv)!=3:raise SystemExit('Usage: run_batch_unix.py OWNED_TASK SOURCE_PACKET_SHA256')
 run(sys.argv[1],sys.argv[2])
