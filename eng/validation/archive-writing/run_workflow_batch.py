"""Guest-only full-source shared workflow batch; no production profile registration or unconfined fallback."""
from pathlib import Path
import hashlib,json,os,runpy,shutil,subprocess,sys,tarfile,xml.etree.ElementTree as ET

SOURCE_FOLDER='archive-workflow-source-v1'
BATCH_FOLDER='archive-workflow-v3'
PREVIOUS_FOLDER='archive-workflow-v2'
DELTA_NAME='archive-workflow-delta-v3.tar.gz'

PREFLIGHT='|'.join('FullyQualifiedName~'+name for name in ('Shared_workflow','Recursive_workflow','Directory_merge_associations','Exact_workflow','Sevenzip_new_input_precision','Sevenzip_capture_precision','Workflow_source_manifest','Workflow_recursive_rename'))
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def idle(task):
 command=['powershell.exe','-NoProfile','-Command',"if(Get-Process -Name 'FileCat*','dotnet','testhost' -ErrorAction SilentlyContinue){exit 2}"] if sys.platform=='win32' else ['python3',str(task/'check-linux-archive-idle.py')]
 subprocess.run(command,check=True)

def prepare(task,expected):
 idle(task);packet=task/DELTA_NAME;assert sha(packet)==expected
 root=task/SOURCE_FOLDER;out=task/BATCH_FOLDER;previous=task/PREVIOUS_FOLDER;assert root.is_dir() and previous.is_dir() and not out.exists()
 with tarfile.open(packet) as tar:
  delta=json.load(tar.extractfile('delta.json'));assert set(tar.getnames())=={'delta.json',*(r['path'] for r in delta['files'])}
  for row in delta['files']:
   name=Path(row['path']);assert not name.is_absolute() and '..' not in name.parts;assert sha(root/name)==row['previous_sha256'],row['path']
  out.mkdir();history=out/'prior-phase-v2';history.mkdir()
  refused=task/'archive-workflow-launch-v3.log'
  if refused.exists():shutil.copyfile(refused,out/'preparation-refused-before-mutation-v3.log')
  for file in previous.iterdir():
   if file.is_file() and file.suffix in ('.json','.log','.txt','.py','.ps1','.err','.out'):shutil.copyfile(file,history/file.name)
  for name in ('results','workflows','catalog','workers','native-vectors','child-diagnostics','prior-phase-v1'):
   if (previous/name).exists():shutil.copytree(previous/name,history/name)
  binaries=history/'binaries';binaries.mkdir()
  folders=[root/p/'bin/Release/net10.0' for p in ('tests/FileCat.Core.Tests','tests/FileCat.WorkerProbe','src/FileCat.ArchiveWorker','src/FileCat.Archives.Writing')]
  if sys.platform=='win32':folders += [root/p/'bin/Release/net10.0' for p in ('tests/FileCat.Platform.Windows.Tests','tests/FileCat.App.Tests')]+[previous/'app',previous/'probe']
  for folder in folders:
   for file in folder.iterdir():
    if file.is_file() and (file.name.startswith('FileCat.') or file.name in ('FileCat.dll','FileCat.pdb')) and file.suffix in ('.dll','.pdb'):
     destination=binaries/file.name
     if destination.exists():assert sha(destination)==sha(file)
     else:shutil.copyfile(file,destination)
  old=json.loads((previous/'source-files.json').read_text());rows={r['path']:r for r in old['files']}
  for row in delta['files']:
   data=tar.extractfile(row['path']).read();assert len(data)==row['bytes'] and hashlib.sha256(data).hexdigest()==row['sha256']
   backup=history/'previous-source'/row['path'];backup.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(root/row['path'],backup)
   (root/row['path']).write_bytes(data);rows[row['path']]={k:row[k] for k in ('path','bytes','sha256')}
  old['files']=[rows[key] for key in sorted(rows)];(out/'source-files.json').write_text(json.dumps(old,indent=2))
 (out/'applied-delta.json').write_text(json.dumps(delta,indent=2));shutil.copyfile(packet,out/packet.name);shutil.copyfile(Path(__file__),out/'executed-batch.py')
 source=task/'archive-workflow-source-v1.tar.gz';assert sha(source)=='e8021cf6141f747579753a1cd5ea9eae82bc597b7d64fd2f6b0ba63e2aefa552';shutil.copyfile(source,out/source.name)

def batch_for(task):
 root=task/SOURCE_FOLDER;batch=runpy.run_path(str(root/'eng/validation/archive-writing/sevenzip-feasibility/run_pipeline_batch.py'))['Pipeline'](task)
 batch.root=root;batch.out=task/BATCH_FOLDER;batch.env['FILECAT_ARCHIVE_CHILD_DIAGNOSTICS_DIRECTORY']=str(batch.out/'child-diagnostics')
 return batch

def tests(batch):
 batch.idle();out=batch.out
 batch.env.update(FILECAT_7Z_CATALOG_EXPORT_ROOT=str(out/'catalog'),FILECAT_7Z_WORKER_EXPORT_ROOT=str(out/'workers'),
  FILECAT_7Z_NATIVE_EXPORT_ROOT=str(out/'native-vectors'),FILECAT_ARCHIVE_WORKFLOW_EXPORT_ROOT=str(out/'workflows'))
 if sys.platform=='win32':batch.env.update(FILECAT_ARCHIVE_CONTAINMENT='1',FILECAT_ARCHIVE_TXF_NATIVE='1',FILECAT_ARCHIVE_TEST_REQUIRE_STANDARD_USER='1',FILECAT_ARCHIVE_PROBE_ASSETS=str(out/'probe'),FILECAT_ARCHIVE_APP_ASSETS=str(out/'app'))
 batch.execute('workflow-preflight',batch.test_args('FileCat.Core.Tests',PREFLIGHT,'workflow-preflight'))
 ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};p=ET.parse(out/'results/workflow-preflight.trx');counts=p.find('.//t:Counters',ns).attrib
 assert counts['total']=='62' and counts['passed']=='62',counts
 if sys.platform=='win32':batch.execute('native-preflight',batch.test_args('FileCat.Platform.Windows.Tests','FullyQualifiedName~Actual_application_7z_updates_and_fresh_snapshot|FullyQualifiedName~Actual_application_RAR_updates_and_fresh_snapshot','native-preflight'))
 selected='|'.join('FullyQualifiedName~FileCat.Core.Tests.'+name for name in ('Archive','Strict','SevenZip','Rar','EmptySevenZip','UnixArchive','Zip'))
 batch.execute('tests',batch.test_args('FileCat.Core.Tests',selected,'archive-pipeline'))
 if sys.platform=='win32':
  batch.execute('native',batch.test_args('FileCat.Platform.Windows.Tests','FullyQualifiedName~ArchiveAppContainer|FullyQualifiedName~ArchiveRetainedStage|FullyQualifiedName~ArchiveContent','native'))
  batch.execute('app-entry',batch.test_args('FileCat.App.Tests','FullyQualifiedName~ArchiveWorkerEntryTests','app-entry'))
 (out/'tests-complete.txt').write_text('PASS\n')

def oracles(batch):
 batch.idle();helpers=batch.root/'eng/validation/archive-writing/sevenzip-feasibility'
 rows=json.loads((batch.out/'source-files.json').read_text())['files'];pins={r['path'].rsplit('/',1)[-1]:r['sha256'] for r in rows if r['path'].startswith('eng/validation/archive-writing/sevenzip-feasibility/')}
 runpy.run_path(str(helpers/'finish_pipeline_batch.py'))['oracle'](batch,helpers,pins)
 task=batch.task;windows=sys.platform=='win32'
 tool=task/('password-oracle-tools-v1/7zip/7z.exe' if windows else 'sevenzip-ppmd-filter-v2/tools/'+('mac' if sys.platform=='darwin' else 'linux')+'/7zz')
 # The catalog phase already checks these executable/module pins. These workflow cases have no BCJ2/Zstandard 7z coder.
 batch.execute('workflow-sevenzip',[sys.executable,'-B',str(helpers/'verify_pipeline.py'),str(batch.out/'workflows'),str(tool),str(task/'password-oracle-tools-v1/modules'),str(tool)])
 batch.execute('workflow-rar',[sys.executable,'-B',str(batch.root/'eng/validation/archive-writing/rar5-independent-probe/verify_native_publication.py'),str(batch.out/'workflows'),str(tool),str(task/'password-oracle-tools-v1/modules'),'0','0','--workflow'])
 batch.execute('workflow-tar',[sys.executable,'-B',str(batch.root/'eng/validation/archive-writing/verify-workflow-tar.py'),str(batch.out/'workflows'),str(tool),str(task/'password-oracle-tools-v1/modules'),str(helpers/'verify_pipeline.py')])
 (batch.out/'workflow-oracles-complete.txt').write_text('PASS\n')

def finish(batch):
 assert (batch.out/'workflow-oracles-complete.txt').read_text()=='PASS\n'
 runpy.run_path(str(batch.root/'eng/validation/archive-writing/sevenzip-feasibility/finish_pipeline_batch.py'))['finish'](batch)

if __name__=='__main__':
 phase,task,*rest=sys.argv[1:];task=Path(task).resolve()
 if phase=='prepare':prepare(task,rest[0])
 elif phase=='run':
  prepare(task,rest[0]);batch=batch_for(task);batch.build();tests(batch);oracles(batch);finish(batch)
 else:
  batch=batch_for(task)
  if phase=='build':batch.build()
  elif phase=='tests':tests(batch)
  elif phase=='oracle':oracles(batch)
  elif phase=='finish':finish(batch)
  else:raise SystemExit('Unknown phase')
