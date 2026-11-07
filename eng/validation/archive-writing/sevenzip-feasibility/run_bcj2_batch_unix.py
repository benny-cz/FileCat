#!/usr/bin/env python3
"""Guarded BCJ2, codec, password, transport and SBOM evidence session."""
from pathlib import Path
import hashlib,json,os,platform,shutil,subprocess,sys,tarfile

def run(task,packet_sha):
 task=Path(task).resolve();root=task/'sevenzip-bcj2-source-v2';out=task/'sevenzip-bcj2-v2'
 def idle():subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
 idle();root.mkdir();out.mkdir()
 packet=task/'sevenzip-bcj2-source-v2.tar.gz'
 assert hashlib.sha256(packet.read_bytes()).hexdigest()==packet_sha
 with tarfile.open(packet,'r:gz') as tar:tar.extractall(root,filter='data')
 mac=sys.platform=='darwin';sdk=Path('/Users/benny/.dotnet') if mac else task/'dotnet'
 env=dict(os.environ,DOTNET_ROOT=str(sdk),PATH=str(sdk)+os.pathsep+os.environ['PATH'],DOTNET_CLI_TELEMETRY_OPTOUT='1',
  MSBUILDDISABLENODEREUSE='1',PYTHONDONTWRITEBYTECODE='1',FILECAT_ARCHIVE_CHILD_DIAGNOSTICS_DIRECTORY=str(out/'child-diagnostics'),FILECAT_SEVENZIP_BCJ2_EXPORT_ROOT=str(out/'bcj2'),FILECAT_SEVENZIP_CODEC_EXPORT_ROOT=str(out/'oracles'),FILECAT_SEVENZIP_FILTER_EXPORT_ROOT=str(out/'vectors'))
 (out/'child-diagnostics').mkdir()
 def execute(name,args,cwd=root,custom_env=env):
  with (out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=cwd,env=custom_env,stdout=log,stderr=subprocess.STDOUT,check=True)
 dotnet=str(sdk/'dotnet')
 execute('writing-build',[dotnet,'build','src/FileCat.Archives.Writing/FileCat.Archives.Writing.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'])
 selected='FullyQualifiedName~SevenZipBcj2WritingTests|FullyQualifiedName~ArchiveWorkerSecretTests|FullyQualifiedName~ArchiveWorkerTransportSecretTests|FullyQualifiedName~ArchiveWorkerCancellationTests|FullyQualifiedName~ArchiveAssessmentClientTests|FullyQualifiedName~ArchiveVerificationClientTests|FullyQualifiedName~ArchiveManifestWireTests|FullyQualifiedName~ArchiveProxyTests|FullyQualifiedName~ArchiveWorkerTrafficPolicyTests|FullyQualifiedName~ArchivePasswordPolicyTests|FullyQualifiedName~ArchivePasswordWritingTests|FullyQualifiedName~ArchiveAesCbcWritingTests|FullyQualifiedName~SevenZipCompressedHeaderWritingTests|FullyQualifiedName~SevenZipFilterEncodingTests|FullyQualifiedName~SevenZipWritingTests|FullyQualifiedName~ArchivePasswordPrimitiveTests|FullyQualifiedName~Rar5WritingTests|FullyQualifiedName~RarReaderTests|FullyQualifiedName~SevenZipCrcTests|FullyQualifiedName~SevenZipPpmdTests|FullyQualifiedName~SevenZipHeaderBudgetTests|FullyQualifiedName~SevenZipEncodedHeaderTests|FullyQualifiedName~EmptySevenZipTests|FullyQualifiedName~ArchiveWriteBoundaryTests|FullyQualifiedName~ArchiveCapturedInputsTests|FullyQualifiedName~ArchiveDirectoryWireTests|FullyQualifiedName~ArchiveTransform|FullyQualifiedName~ArchiveCreationOriginTests|FullyQualifiedName~UnixArchiveStorageTests'
 timer=['/usr/bin/time','-l'] if mac else ['/usr/bin/time','-v']
 execute('tests',timer+[dotnet,'test','tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','--filter',selected,'--logger','trx;LogFileName=archive-worker-secrets.trx','--results-directory',str(out/'results')])
 idle()
 helpers=root/'eng/validation/archive-writing/sevenzip-feasibility'
 tools_packet=task/'sevenzip-bcj2-tools-v1.tar.gz';assert hashlib.sha256(tools_packet.read_bytes()).hexdigest()=='9fc7b0edf4748630e164d6efe4ee712b2d78c7bb27e7069b92404e685c604c6f'
 tools=out/'tools';tools.mkdir()
 with tarfile.open(tools_packet,'r:gz') as tar:tar.extractall(tools,filter='data')
 for row in json.loads((tools/'files.json').read_text())['files']:
  path=tools/row['path'];assert path.stat().st_size==row['bytes'] and hashlib.sha256(path.read_bytes()).hexdigest()==row['sha256']
 c=tools/'sdk/C';compiler='/Library/Developer/CommandLineTools/usr/bin/clang' if mac else '/usr/bin/gcc';oracle=out/'sdk-oracle'
 native_env=dict(env)
 if mac:native_env['SDKROOT']='/Library/Developer/CommandLineTools/SDKs/MacOSX27.0.sdk'
 sources=['Bra.c','Bra86.c','BraIA64.c','Delta.c','SwapBytes.c','CpuArch.c','Ppmd7.c','Ppmd7Enc.c','Bcj2Enc.c']
 command=[compiler,'-std=c11','-O2','-I',str(c),str(tools/'sdk_oracle.c')]+[str(c/name) for name in sources]+['-o',str(oracle)]
 execute('sdk-compiler',[compiler,'--version']);execute('sdk-build',command,custom_env=native_env)
 (out/'sdk-build-command.json').write_text(json.dumps(command,indent=2))
 execute('bcj2-sdk',['python3','-B',str(helpers/'verify_bcj2_sdk.py'),str(out/'bcj2'),str(oracle)])
 execute('previous-sdk',['python3','-B',str(helpers/'verify_sdk.py'),str(out/'vectors'),str(oracle)])
 reader_tools=task/'password-oracle-tools-v1';which='mac' if mac else 'ubuntu'
 execute('reader-module-reuse',['python3','-B',str(helpers/'verify_oracle_module_reuse.py'),str(reader_tools),which])
 tool=task/'sevenzip-ppmd-filter-v2/tools'/('mac' if mac else 'linux')/'7zz'
 assert hashlib.sha256(tool.read_bytes()).hexdigest()==json.loads((reader_tools/'official-tool-identities.json').read_text())[which]['sha256']
 execute('bcj2-official',['python3','-B',str(helpers/'verify_bcj2_cli.py'),str(out/'bcj2'),str(tool),str(reader_tools/'modules')])
 execute('previous-official',['python3','-B',str(helpers/'verify_header_cli.py'),str(out/'oracles'),str(tool)])
 prefix=task/'sevenzip-crypto-v1/native-oracle/install/lib';pattern='libarchive.*.dylib' if mac else 'libarchive.so.*'
 libraries=[path for path in prefix.glob(pattern) if path.is_file() and not path.is_symlink()];assert len(libraries)==1;library=libraries[0]
 execute('bcj2-native-survey',['python3','-B',str(helpers/'verify_bcj2_native.py'),str(out/'bcj2'),str(library)])
 execute('previous-native',['python3','-B',str(helpers/'verify_header_native.py'),str(out/'oracles'),str(library)])
 expected=json.loads((root/'docs/validation/archive-writing/archive-password-checkpoint.json').read_text())['unencrypted_archive_sha256']
 actual={path.parent.name:hashlib.sha256(path.read_bytes()).hexdigest() for path in (out/'oracles').glob('*/members.7z')};assert actual==expected
 (out/'previous-archives-unchanged.json').write_text(json.dumps(actual,indent=2))
 (out/'reader-tools-reused.json').write_text(json.dumps({'root':str(reader_tools),'official_tool':str(tool),'official_tool_sha256':hashlib.sha256(tool.read_bytes()).hexdigest(),'native_library':str(library),'native_sha256':hashlib.sha256(library.read_bytes()).hexdigest(),'production_dependency':False},indent=2))
 execute('sbom-controls',['python3','-B',str(root/'eng/validation/validate-artifact-sbom.py'),'--evidence-dir',str(out/'sbom-controls'),'--source-packet-sha256',packet_sha])
 binaries=out/'binaries';binaries.mkdir()
 for folder in (root/'tests/FileCat.Core.Tests/bin/Release/net10.0',root/'src/FileCat.Archives.Writing/bin/Release/net10.0',root/'tests/FileCat.WorkerProbe/bin/Release/net10.0'):
  for p in folder.iterdir():
   if p.is_file() and p.name.startswith('FileCat.') and p.suffix in ('.dll','.pdb'):shutil.copyfile(p,binaries/p.name)
 execute('audit-build',[dotnet,'build','eng/validation/archive-writing/PdbSourceAudit/PdbSourceAudit.csproj','-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1'])
 execute('source-audit',[dotnet,str(root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'),str(root),str(binaries),str(out/'compiler-sources'),str(Path.home()/'.nuget/packages')])
 sbom_binaries=out/'sbom-binaries';sbom_binaries.mkdir()
 for extension in ('.dll','.pdb'):
  shutil.copyfile(out/('sbom-controls/artifacts/bin/ArtifactSbom/release/ArtifactSbom'+extension),sbom_binaries/('ArtifactSbom'+extension))
 sbom_facts=json.loads((out/'sbom-controls/artifact-sbom-controls.json').read_text())
 execute('sbom-source-audit',[dotnet,str(root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'),sbom_facts['SourceRoot'],str(sbom_binaries),str(out/'sbom-compiler-sources'),sbom_facts['ArtifactsRoot'],'--artifact-sbom'])
 shutil.copyfile(packet,out/packet.name);shutil.copyfile(Path(__file__),out/'executed-batch.py')
 (out/'machine.json').write_text(json.dumps({'platform':platform.platform(),'sdk':subprocess.check_output([dotnet,'--info'],env=env,text=True),'new_profile_registered':False},indent=2))
 idle();census={'busy':False,'owned_filecat_or_test_processes':0}
 if mac:
  wake=subprocess.check_output(['/bin/ps','-p','36765','-o','pid=,command='],text=True);assert wake.strip();census['preserved_wake_helper']=wake.strip()
 (out/'final-census.json').write_text(json.dumps(census,indent=2))
 (out/'complete.txt').write_text('PASS: BCJ2/codec/crypto regression and independent checks; native-survey gaps and full production qualification remain explicit.\n')
 links=[{'path':p.relative_to(out).as_posix(),'target':os.readlink(p)} for p in sorted(out.rglob('*')) if p.is_symlink()]
 (out/'synthetic-symlink-fixtures.json').write_text(json.dumps(links,indent=2))
 rows=[]
 for p in sorted(out.rglob('*')):
  if p.is_symlink():continue
  if p.is_file():rows.append({'path':p.relative_to(out).as_posix(),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
 (out/'files.json').write_text(json.dumps({'files':rows,'production_backend_qualified':False},indent=2))
 archive=task/'sevenzip-bcj2-evidence-v2.tar.gz'
 with tarfile.open(archive,'x:gz',dereference=False) as tar:
  for p in sorted(out.rglob('*')):
   if not p.is_symlink():tar.add(p,arcname=out.name+'/'+p.relative_to(out).as_posix(),recursive=False)
 facts={'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'rows':len(rows)}
 (task/'sevenzip-bcj2-evidence-v2.json').write_text(json.dumps(facts,indent=2));print(json.dumps(facts),flush=True)

if __name__=='__main__':run(sys.argv[1],sys.argv[2])
