"""Guest-only preservation supplement; reuse unchanged production binaries and rerun the new boundary cases."""
from pathlib import Path
import hashlib,json,os,runpy,shutil,subprocess,sys,tarfile,xml.etree.ElementTree as ET

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def run(task,expected,phase='run'):
 task=Path(task).resolve();batch=runpy.run_path(str(task/'run-pipeline-batch-v3.py'))['Pipeline'](task);batch.idle()
 batch.out=task/'sevenzip-preserved-headers-v1';out=batch.out;assert phase in ('run','finish')
 if phase=='run':
  packet=task/'sevenzip-preserved-headers-v1.tar.gz';assert sha(packet)==expected;assert not out.exists();out.mkdir()
  batch.env.update(FILECAT_7Z_WORKER_EXPORT_ROOT=str(out/'vectors'),FILECAT_ARCHIVE_CHILD_DIAGNOSTICS_DIRECTORY=str(out/'child-diagnostics'))
  with tarfile.open(packet) as tar:
   facts=json.load(tar.extractfile('files.json'));assert set(tar.getnames())=={'files.json',*(r['path'] for r in facts['files'])}
   for row in facts['files']:
    path=Path(row['path']);assert not path.is_absolute() and '..' not in path.parts;data=tar.extractfile(row['path']).read();assert len(data)==row['bytes'] and hashlib.sha256(data).hexdigest()==row['sha256']
    destination=batch.root/path if path.suffix=='.cs' else out/path.name
    assert not destination.exists();destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(data)
  shutil.copyfile(packet,out/packet.name);shutil.copyfile(Path(__file__),out/'executed-batch.py');(out/'applied-files.json').write_text(json.dumps(facts,indent=2))
  batch.execute('build',[batch.dotnet,'build',str(batch.root/'tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj'),'-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','-v','minimal'])
  batch.execute('tests',batch.test_args('FileCat.Core.Tests','FullyQualifiedName~ArchiveSevenZipHeaderPreservationTests','preserved-headers'))
 else:
  assert out.is_dir();helper=task/'verify-pipeline-headers-v3.py';assert sha(helper)==expected;shutil.copyfile(helper,out/helper.name);shutil.copyfile(Path(__file__),out/'executed-resume-v3.py')
  execute=batch.execute;batch.execute=lambda name,args,env=None:execute(name+'-v3',args,env)
 ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};doc=ET.parse(out/'results/preserved-headers.trx');counters=doc.find('.//t:Counters',ns).attrib
 assert counters['total']=='10' and counters['passed']=='10' and counters['failed']=='0',counters
 batch.idle();which='windows' if sys.platform=='win32' else 'mac' if sys.platform=='darwin' else 'ubuntu'
 batch.execute('modules',[sys.executable,'-B',str(out/'verify_oracle_module_reuse.py'),str(task/'password-oracle-tools-v1'),which])
 tool=task/('password-oracle-tools-v1/7zip/7z.exe' if which=='windows' else 'sevenzip-ppmd-filter-v2/tools/'+('mac' if which=='mac' else 'linux')+'/7zz')
 pins={'windows':'94205cfc054dd3bbeb978905bc619a65b3f04e9e73bb2717841110b6f46d962e','mac':'8e495531e1b2b8cb169ca583493922d4ab386007756418c15b95f07f642c35fb','ubuntu':'d32657a7a0e4cdc0a8aa57cafe474746944d76e861e753e9c902b9c9356fa886'};assert sha(tool)==pins[which]
 # These cases have no Zstandard/BCJ2. The required unused argument is the checked official executable.
 batch.execute('independent',[sys.executable,'-B',str(out/('verify_pipeline.py' if phase=='run' else 'verify-pipeline-headers-v3.py')),str(out/'vectors'),str(tool),str(task/'password-oracle-tools-v1/modules'),str(tool),'--partial-folder-crc'])
 binaries=out/'binaries';binaries.mkdir()
 for project in ('tests/FileCat.Core.Tests','tests/FileCat.WorkerProbe','src/FileCat.ArchiveWorker','src/FileCat.Archives.Writing'):
  for file in (batch.root/project/'bin/Release/net10.0').iterdir():
   if file.name.startswith('FileCat.') and file.suffix in ('.dll','.pdb'):
    destination=binaries/file.name
    if destination.exists():assert sha(destination)==sha(file)
    else:shutil.copyfile(file,destination)
 extra='C:/Users/Admin/.nuget/packages' if which=='windows' else str(Path.home()/'.nuget/packages')
 batch.execute('source-audit',[batch.dotnet,str(batch.root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'),str(batch.root),str(binaries),str(out/'compiler-sources'),extra])
 batch.idle();census={'busy':False,'owned_filecat_or_test_processes':0}
 if which=='mac':census['preserved_wake_helper']=subprocess.check_output(['/bin/ps','-p','36765','-o','pid=,command='],text=True).strip();assert census['preserved_wake_helper']
 (out/'final-census.json').write_text(json.dumps(census,indent=2));(out/'complete.txt').write_text('PASS: ten header/presence preservation cases and twenty independent source/output checks. Further production qualification remains open.\n')
 rows=[{'path':file.relative_to(out).as_posix(),'bytes':file.stat().st_size,'sha256':sha(file)} for file in sorted(out.rglob('*')) if file.is_file()];(out/'files.json').write_text(json.dumps({'files':rows},indent=2))
 archive=task/(out.name+'-evidence.tar.gz')
 with tarfile.open(archive,'x:gz') as tar:
  for file in sorted(out.rglob('*')):tar.add(file,arcname=out.name+'/'+file.relative_to(out).as_posix(),recursive=False)
 facts={'bytes':archive.stat().st_size,'sha256':sha(archive),'rows':len(rows)};(task/(out.name+'-evidence.json')).write_text(json.dumps(facts,indent=2));print(json.dumps(facts),flush=True)

if __name__=='__main__':run(*sys.argv[1:])
