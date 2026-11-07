"""Incremental guest-only 7z pipeline batch. Windows build/preparation and standard-user tests are separate phases."""
from pathlib import Path
import hashlib,json,os,platform,shutil,subprocess,sys,tarfile

WINDOWS=sys.platform=='win32'
WHICH='windows' if WINDOWS else 'mac' if sys.platform=='darwin' else 'ubuntu'

class Pipeline:
 def __init__(self,task):
  self.task=Path(task).resolve();self.root=self.task/('sevenzip-graph-source-v1' if WINDOWS else 'sevenzip-graph-source-v3');self.out=self.task/'sevenzip-pipeline-v3'
  self.sdk=self.task/'dotnet' if WHICH!='mac' else Path('/Users/benny/.dotnet');self.dotnet=str(self.sdk/('dotnet.exe' if WINDOWS else 'dotnet'))
  self.env=dict(os.environ,DOTNET_ROOT=str(self.sdk),PATH=str(self.sdk)+os.pathsep+os.environ['PATH'],DOTNET_CLI_TELEMETRY_OPTOUT='1',MSBUILDDISABLENODEREUSE='1',
   FILECAT_ARCHIVE_CHILD_DIAGNOSTICS_DIRECTORY=str(self.out/'child-diagnostics'))
 def idle(self):
  command=['powershell.exe','-NoProfile','-Command',"if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){exit 2}"] if WINDOWS else ['python3',str(self.task/'check-linux-archive-idle.py')]
  subprocess.run(command,check=True,stdout=subprocess.DEVNULL)
 def execute(self,name,args,env=None):
  with (self.out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=self.root,env=env or self.env,stdout=log,stderr=subprocess.STDOUT,check=True)
 def prepare(self,expected):
  self.idle();assert self.root.is_dir() and not self.out.exists();packet=self.task/'sevenzip-pipeline-delta-v3.tar.gz';assert sha(packet)==expected;self.out.mkdir();(self.out/'child-diagnostics').mkdir()
  previous=self.task/'sevenzip-pipeline-v2';history=self.out/'prior-phase-v2';history.mkdir()
  for file in previous.iterdir():
   if file.is_file():shutil.copyfile(file,history/file.name)
  for name in ('results','catalog','workers','native-vectors','child-diagnostics','prior-phase-v1'):
   if (previous/name).exists():shutil.copytree(previous/name,history/name)
  for project in ('tests/FileCat.Core.Tests','tests/FileCat.WorkerProbe','src/FileCat.Archives.Writing','src/FileCat.App'):
   folder=self.root/project/'bin/Release/net10.0'
   if not folder.exists():continue
   destination=history/'binaries'/Path(project).name;destination.mkdir(parents=True)
   for file in folder.iterdir():
    if file.is_file() and (file.name.startswith('FileCat.') or file.name in ('FileCat.dll','FileCat.pdb')) and file.suffix in ('.dll','.pdb'):shutil.copyfile(file,destination/file.name)
  with tarfile.open(packet) as tar:
   facts=json.load(tar.extractfile('delta.json'));assert set(tar.getnames())=={'delta.json',*(r['path'] for r in facts['files'])}
   for row in facts['files']:
    path=self.root/row['path'];assert path.resolve().is_relative_to(self.root);assert (sha(path) if path.exists() else None)==row['previous'][WHICH],row['path']
   for row in facts['files']:
    data=tar.extractfile(row['path']).read();assert hashlib.sha256(data).hexdigest()==row['sha256'];path=self.root/row['path'];path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(data)
  shutil.copyfile(packet,self.out/packet.name);shutil.copyfile(Path(__file__),self.out/'executed-batch.py');(self.out/'delta.json').write_text(json.dumps(facts,indent=2))
 def build(self):
  projects=['src/FileCat.ArchiveWorker','src/FileCat.Archives.Writing','tests/FileCat.Core.Tests','eng/validation/archive-writing/PdbSourceAudit']
  if WINDOWS:projects+=['tests/FileCat.Platform.Windows.Tests','tests/FileCat.App.Tests']
  for project in projects:
   self.execute(Path(project).name+'-build',[self.dotnet,'build',str(self.root/project/(Path(project).name+'.csproj')),'-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','-v','minimal'])
  if WINDOWS:
   # Runtime/dependency packages have not changed. Reuse those frozen files and replace current FileCat binaries.
   base=self.task/'rar5-native-v3';ledger=json.loads((self.task/'rar5-native-v5/actual-native-assets-v3.json').read_text(encoding='utf-8-sig'))['files']
   for row in ledger:
    path=base/row['path'];assert path.stat().st_size==row['bytes'] and sha(path).lower()==row['sha256'].lower(),row['path']
   for name,project,assembly in (('probe','tests/FileCat.WorkerProbe','FileCat.WorkerProbe'),('app','src/FileCat.App','FileCat')):
    destination=self.out/name;shutil.copytree(base/name,destination)
    folder=self.root/project/'bin/Release/net10.0'
    for file in folder.iterdir():
     if file.is_file() and (file.name==assembly+'.exe' or file.name.startswith('FileCat.') and file.suffix in ('.dll','.pdb') or file.name in ('FileCat.dll','FileCat.pdb')):
      shutil.copyfile(file,destination/file.name)
   (self.out/'runtime-reuse.json').write_text(json.dumps({'source':str(base),'manifest':str(self.task/'rar5-native-v5/actual-native-assets-v3.json'),'manifest_sha256':sha(self.task/'rar5-native-v5/actual-native-assets-v3.json'),'unchanged_runtime_and_package_files_reused':True},indent=2))
 def test_args(self,assembly,selected,name):
  if WINDOWS:return ['C:/Program Files/dotnet/dotnet.exe','exec','C:/Users/fcarchive42829297/FileCatValidation/Capture-42829297-v7/tester/vstest.console.dll',str(self.root/'tests'/assembly/'bin/Release/net10.0'/(assembly+'.dll')),'/TestCaseFilter:'+selected,'/Logger:trx;LogFileName='+name+'.trx','/ResultsDirectory:'+str(self.out/'results')]
  return [self.dotnet,'test',str(self.root/'tests'/assembly/(assembly+'.csproj')),'-c','Release','--no-build','--disable-build-servers','-p:NuGetAudit=false','-m:1','--filter',selected,'--logger','trx;LogFileName='+name+'.trx','--results-directory',str(self.out/'results')]
 def tests(self):
  self.idle();self.env.update(FILECAT_7Z_CATALOG_EXPORT_ROOT=str(self.out/'catalog'),FILECAT_7Z_WORKER_EXPORT_ROOT=str(self.out/'workers'),FILECAT_7Z_NATIVE_EXPORT_ROOT=str(self.out/'native-vectors'))
  if WINDOWS:self.env.update(FILECAT_ARCHIVE_CONTAINMENT='1',FILECAT_ARCHIVE_TXF_NATIVE='1',FILECAT_ARCHIVE_TEST_REQUIRE_STANDARD_USER='1',FILECAT_ARCHIVE_PROBE_ASSETS=str(self.out/'probe'),FILECAT_ARCHIVE_APP_ASSETS=str(self.out/'app'))
  self.execute('preflight',self.test_args('FileCat.Core.Tests','FullyQualifiedName~Windows_preflight_covers_|FullyQualifiedName~ArchiveSevenZipMetadataTests','preflight'))
  if WINDOWS:self.execute('native-preflight',self.test_args('FileCat.Platform.Windows.Tests','FullyQualifiedName~Actual_application_7z_protocol_two_workers|FullyQualifiedName~Actual_application_7z_updates_and_fresh_snapshot','native-preflight'))
  selected='|'.join('FullyQualifiedName~FileCat.Core.Tests.'+n for n in ('Archive','Strict','SevenZip','Rar','EmptySevenZip','UnixArchive','Zip'))
  self.execute('tests',self.test_args('FileCat.Core.Tests',selected,'archive-pipeline'))
  if WINDOWS:
   self.execute('native',self.test_args('FileCat.Platform.Windows.Tests','FullyQualifiedName~ArchiveAppContainer|FullyQualifiedName~ArchiveRetainedStage|FullyQualifiedName~ArchiveContent','native'))
   self.execute('app-entry',self.test_args('FileCat.App.Tests','FullyQualifiedName~ArchiveWorkerEntryTests','app-entry'))
  (self.out/'tests-complete.txt').write_text('PASS\n')

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

if __name__=='__main__':
 phase,task,*rest=sys.argv[1:];batch=Pipeline(task)
 if phase=='prepare':batch.prepare(rest[0])
 elif phase=='build':batch.build()
 elif phase=='tests':batch.tests()
 elif phase in ('oracle','finish'):
  import runpy
  runpy.run_path(str(batch.root/'eng/validation/archive-writing/sevenzip-feasibility/finish_pipeline_batch.py'))[phase](batch)
 elif phase=='run':batch.prepare(rest[0]);batch.build();batch.tests()
 else:raise SystemExit('Unknown phase')
