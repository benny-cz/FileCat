"""Guest-only incremental batch: preserve failures, apply checked delta, smoke, broad tests, independent readers, source audit."""
from pathlib import Path
import hashlib,json,os,platform,shutil,subprocess,sys,tarfile

WINDOWS=sys.platform=='win32'
SELECTED='|'.join('FullyQualifiedName~FileCat.Core.Tests.'+name for name in ('Archive','Strict','SevenZip','Rar','EmptySevenZip','UnixArchive','Zip'))

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

class Batch:
 def __init__(self,task):
  self.task=Path(task).resolve();self.root=self.task/('sevenzip-graph-source-v1' if WINDOWS else 'sevenzip-graph-source-v3');self.out=self.task/'sevenzip-graph-v4'
  self.sdk=self.task/'dotnet' if WINDOWS or sys.platform!='darwin' else Path('/Users/benny/.dotnet')
  self.dotnet=str(self.sdk/('dotnet.exe' if WINDOWS else 'dotnet'))
  self.env=dict(os.environ,DOTNET_ROOT=str(self.sdk),PATH=str(self.sdk)+os.pathsep+os.environ['PATH'],DOTNET_CLI_TELEMETRY_OPTOUT='1',MSBUILDDISABLENODEREUSE='1',
   FILECAT_ARCHIVE_CHILD_DIAGNOSTICS_DIRECTORY=str(self.out/'child-diagnostics'))
 def idle(self):
  if WINDOWS:
   subprocess.run(['powershell.exe','-NoProfile','-Command',"if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){exit 2}"],check=True,stdout=subprocess.DEVNULL)
  else:subprocess.run(['python3',str(self.task/'check-linux-archive-idle.py')],check=True)
 def execute(self,name,args,env=None):
  with (self.out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=self.root,env=env or self.env,stdout=log,stderr=subprocess.STDOUT,check=True)
 def binary_folders(self):return [self.root/p/'bin/Release/net10.0' for p in ('tests/FileCat.Core.Tests','src/FileCat.Archives.Writing','tests/FileCat.WorkerProbe','src/FileCat.ArchiveWorker')]
 def prepare(self,packet_sha):
  self.idle();assert self.root.is_dir() and not self.out.exists();packet=self.task/'sevenzip-graph-delta-v4.tar.gz';assert sha(packet)==packet_sha
  self.out.mkdir();(self.out/'child-diagnostics').mkdir()
  for folder in self.binary_folders():
   if not folder.exists():continue
   target=self.out/'prior-binaries'/folder.parts[-4];target.mkdir(parents=True)
   for file in folder.iterdir():
    if file.is_file() and file.name.startswith('FileCat.') and file.suffix in ('.dll','.pdb'):shutil.copyfile(file,target/file.name)
  prior=self.task/('sevenzip-graph-preflight-v3' if WINDOWS else 'sevenzip-graph-v3')
  for file in prior.rglob('*'):
   if file.is_file() and file.suffix in ('.log','.trx','.txt','.json'):
    target=self.out/'prior-phase'/file.relative_to(prior);target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(file,target)
  with tarfile.open(packet,'r:gz') as tar:
   facts=json.load(tar.extractfile('delta.json'));expected={'delta.json',*(r['path'] for r in facts['files'])};assert set(tar.getnames())==expected
   for row in facts['files']:
    path=self.root/row['path'];assert path.resolve().is_relative_to(self.root);assert (sha(path) if path.exists() else None)==row['previous_windows_sha256' if WINDOWS else 'previous_sha256'],row['path']
   for row in facts['files']:
    data=tar.extractfile(row['path']).read();assert hashlib.sha256(data).hexdigest()==row['sha256'];path=self.root/row['path'];path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(data)
  shutil.copyfile(packet,self.out/packet.name);shutil.copyfile(Path(__file__),self.out/'executed-batch.py');(self.out/'applied-delta.json').write_text(json.dumps(facts,indent=2))
 def build(self):
  for name,path in (('dedicated','src/FileCat.ArchiveWorker'),('writing','src/FileCat.Archives.Writing'),('tests','tests/FileCat.Core.Tests'),('audit','eng/validation/archive-writing/PdbSourceAudit')):
   self.execute(name+'-build',[self.dotnet,'build',str(self.root/path/(Path(path).name+'.csproj')),'-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','-v','minimal'])
 def tests(self):
  self.idle()
  if WINDOWS:
   runner=Path('C:/Users/fcarchive42829297/FileCatValidation/Capture-42829297-v7/tester/vstest.console.dll')
   command=['C:/Program Files/dotnet/dotnet.exe','exec',str(runner),str(self.root/'tests/FileCat.Core.Tests/bin/Release/net10.0/FileCat.Core.Tests.dll')]
   def args(selected,name):return command+['/TestCaseFilter:'+selected,'/Logger:trx;LogFileName='+name+'.trx','/ResultsDirectory:'+str(self.out/name)]
  else:
   command=[self.dotnet,'test','tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj','-c','Release','--no-build','--disable-build-servers','-p:NuGetAudit=false','-m:1']
   def args(selected,name):return command+['--filter',selected,'--logger','trx;LogFileName='+name+'.trx','--results-directory',str(self.out/name)]
  self.execute('preflight',args('FullyQualifiedName~Windows_preflight_covers_','preflight-results'))
  env=dict(self.env,FILECAT_7Z_CATALOG_EXPORT_ROOT=str(self.out/'vectors'));self.execute('tests',args(SELECTED,'results'),env)
  tool=self.task/('password-oracle-tools-v1/7zip/7z.exe' if WINDOWS else 'sevenzip-ppmd-filter-v2/tools/'+('mac' if sys.platform=='darwin' else 'linux')+'/7zz')
  expected='94205cfc054dd3bbeb978905bc619a65b3f04e9e73bb2717841110b6f46d962e' if WINDOWS else ('8e495531e1b2b8cb169ca583493922d4ab386007756418c15b95f07f642c35fb' if sys.platform=='darwin' else 'd32657a7a0e4cdc0a8aa57cafe474746944d76e861e753e9c902b9c9356fa886');assert sha(tool)==expected
  oracle=self.task/('sevenzip-ppmd-filter-v2/tools/windows/sdk-oracle.exe' if WINDOWS else 'sevenzip-ppmd-filter-v2/sdk-oracle')
  if WINDOWS:
   rows=json.loads((self.task/'sevenzip-ppmd-filter-v2/tools/files.json').read_text(encoding='utf-8-sig'))['files'];row=next(r for r in rows if r['path']=='windows/sdk-oracle.exe');assert sha(oracle)==row['sha256']
  else:
   prior=json.loads((self.task/'sevenzip-ppmd-filter-v2/files.json').read_text())['files'];row=next(r for r in prior if r['path']=='sdk-oracle');assert sha(oracle)==row['sha256']
  base=self.task/('sevenzip-bcj2-supplement-v3' if WINDOWS else 'sevenzip-bcj2-supplement-v1')
  bcj2=base/'tools'/('sdk-bcj2-decode-oracle.exe' if WINDOWS else 'sdk-bcj2-decode-oracle')
  row=next(r for r in json.loads((base/'files.json').read_text(encoding='utf-8-sig'))['files'] if r['path']=='tools/'+bcj2.name);assert sha(bcj2).lower()==row['sha256'].lower()
  self.execute('cached-modules',[sys.executable,'-B',str(self.root/'eng/validation/archive-writing/sevenzip-feasibility/verify_oracle_module_reuse.py'),str(self.task/'password-oracle-tools-v1'),'windows' if WINDOWS else 'mac' if sys.platform=='darwin' else 'ubuntu'])
  self.execute('independent-catalog',[sys.executable,'-B',str(self.root/'eng/validation/archive-writing/sevenzip-feasibility/verify_catalog.py'),str(self.out/'vectors'),str(tool),str(self.task/'password-oracle-tools-v1/modules'),str(oracle),str(bcj2)])
  (self.out/'test-and-oracle-complete.txt').write_text('PASS\n')
 def finish(self):
  self.idle();assert (self.out/'test-and-oracle-complete.txt').read_text()=='PASS\n'
  deps=self.root/'src/FileCat.Archives.Writing/bin/Release/net10.0/FileCat.Archives.Writing.deps.json';libraries=json.loads(deps.read_text())['libraries'];assert all('Rar15' not in name and 'DiscUtils' not in name for name in libraries)
  shutil.copyfile(deps,self.out/deps.name);(self.out/'writing-closure.json').write_text(json.dumps({'libraries':sorted(libraries),'Rar15_or_DiscUtils_present':False},indent=2))
  binaries=self.out/'binaries';binaries.mkdir()
  for folder in self.binary_folders():
   for p in folder.iterdir():
    if p.is_file() and p.name.startswith('FileCat.') and p.suffix in ('.dll','.pdb'):shutil.copyfile(p,binaries/p.name)
  extra='C:/Users/Admin/.nuget/packages' if WINDOWS else str(Path.home()/'.nuget/packages')
  self.execute('source-audit',[self.dotnet,str(self.root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'),str(self.root),str(binaries),str(self.out/'compiler-sources'),extra])
  (self.out/'machine.json').write_text(json.dumps({'platform':platform.platform(),'sdk':subprocess.check_output([self.dotnet,'--info'],env=self.env,text=True),'new_profile_registered':False},indent=2))
  self.idle();census={'busy':False,'owned_filecat_or_test_processes':0}
  if sys.platform=='darwin':wake=subprocess.check_output(['/bin/ps','-p','36765','-o','pid=,command='],text=True);assert wake.strip();census['preserved_wake_helper']=wake.strip()
  (self.out/'final-census.json').write_text(json.dumps(census,indent=2));(self.out/'complete.txt').write_text('PASS: complete member catalog, broad regression and independent readers. Production integration and qualification remain open.\n')
  rows=[]
  for file in sorted(self.out.rglob('*')):
   assert not file.is_symlink()
   if file.is_file():rows.append({'path':file.relative_to(self.out).as_posix(),'bytes':file.stat().st_size,'sha256':sha(file)})
  (self.out/'files.json').write_text(json.dumps({'files':rows,'production_backend_qualified':False},indent=2))
  archive=self.task/'sevenzip-graph-evidence-v4.tar.gz'
  with tarfile.open(archive,'x:gz',dereference=False) as tar:
   for file in sorted(self.out.rglob('*')):tar.add(file,arcname=self.out.name+'/'+file.relative_to(self.out).as_posix(),recursive=False)
  facts={'bytes':archive.stat().st_size,'sha256':sha(archive),'rows':len(rows)};(self.task/'sevenzip-graph-evidence-v4.json').write_text(json.dumps(facts,indent=2));print(json.dumps(facts),flush=True)

if __name__=='__main__':
 phase,task,*rest=sys.argv[1:];batch=Batch(task)
 if phase=='run':batch.prepare(rest[0]);batch.build();batch.tests();batch.finish()
 elif phase=='prepare':batch.prepare(rest[0])
 elif phase=='build':batch.build()
 elif phase=='tests':batch.tests()
 elif phase=='finish':batch.finish()
 else:raise SystemExit('Unknown batch phase')
