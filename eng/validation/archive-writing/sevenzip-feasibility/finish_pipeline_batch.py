"""Guest-only independent readers and compiler-source evidence for a completed pipeline batch."""
from pathlib import Path
import hashlib,json,os,platform,shutil,subprocess,sys,tarfile,xml.etree.ElementTree as ET

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def oracle(batch, helper_directory=None, helper_pins=None):
 batch.idle();out=batch.out;task=batch.task;windows=sys.platform=='win32'
 assert (out/'tests-complete.txt').read_text()=='PASS\n'
 ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};counts={}
 for path in sorted((out/'results').glob('*.trx')):
  doc=ET.parse(path);counters=doc.find('.//t:Counters',ns).attrib
  assert int(counters['failed'])==0 and int(counters['executed'])>0,(path,counters);counts[path.name]=counters
  for row in doc.findall('.//t:UnitTestResult',ns):
   if any(name in row.get('testName','') for name in ('ArchiveSevenZip','SevenZipWorkerPipeline','StrictSevenZipCatalogTests')):
    assert row.get('outcome')=='Passed' or windows and 'UnixArchiveStorageTests' in row.get('testName',''),row.attrib
 (out/'test-counters.json').write_text(json.dumps(counts,indent=2))
 base=task/('sevenzip-bcj2-supplement-v3' if windows else 'sevenzip-bcj2-supplement-v1');bcj2=base/'tools'/('sdk-bcj2-decode-oracle.exe' if windows else 'sdk-bcj2-decode-oracle')
 row=next(r for r in json.loads((base/'files.json').read_text(encoding='utf-8-sig'))['files'] if r['path']=='tools/'+bcj2.name);assert sha(bcj2).lower()==row['sha256'].lower()
 filters=task/('sevenzip-ppmd-filter-v2/tools/windows/sdk-oracle.exe' if windows else 'sevenzip-ppmd-filter-v2/sdk-oracle')
 ledger=task/'sevenzip-ppmd-filter-v2'/('tools/files.json' if windows else 'files.json');key='windows/sdk-oracle.exe' if windows else 'sdk-oracle'
 row=next(r for r in json.loads(ledger.read_text(encoding='utf-8-sig'))['files'] if r['path']==key);assert sha(filters).lower()==row['sha256'].lower()
 tool=task/('password-oracle-tools-v1/7zip/7z.exe' if windows else 'sevenzip-ppmd-filter-v2/tools/'+('mac' if sys.platform=='darwin' else 'linux')+'/7zz')
 expected='94205cfc054dd3bbeb978905bc619a65b3f04e9e73bb2717841110b6f46d962e' if windows else ('8e495531e1b2b8cb169ca583493922d4ab386007756418c15b95f07f642c35fb' if sys.platform=='darwin' else 'd32657a7a0e4cdc0a8aa57cafe474746944d76e861e753e9c902b9c9356fa886');assert sha(tool)==expected
 helper=Path(helper_directory) if helper_directory is not None else batch.root/'eng/validation/archive-writing/sevenzip-feasibility'
 pins=helper_pins or {row['path'].rsplit('/',1)[-1]:row['sha256'] for row in json.loads((out/'delta.json').read_text())['files']}
 for name in ('finish_pipeline_batch.py','verify_pipeline.py','verify_catalog.py','verify_oracle_module_reuse.py'):
  assert sha(helper/name)==pins[name]
  if helper_directory is None:shutil.copyfile(helper/name,out/('executed-'+name))
 batch.execute('cached-modules',[sys.executable,'-B',str(helper/'verify_oracle_module_reuse.py'),str(task/'password-oracle-tools-v1'),'windows' if windows else 'mac' if sys.platform=='darwin' else 'ubuntu'])
 batch.execute('independent-catalog',[sys.executable,'-B',str(helper/'verify_catalog.py'),str(out/'catalog'),str(tool),str(task/'password-oracle-tools-v1/modules'),str(filters),str(bcj2)])
 for name in ('workers','native-vectors'):
  args=[sys.executable,'-B',str(helper/'verify_pipeline.py'),str(out/name),str(tool),str(task/'password-oracle-tools-v1/modules'),str(bcj2)]
  # A full regression batch also exports the explicit CRC/anti/header-order preservation vectors here.
  if name=='workers':args.append('--partial-folder-crc')
  batch.execute('independent-'+name,args)
 (out/'test-and-oracle-complete.txt').write_text('PASS\n')

def finish(batch):
 batch.idle();out=batch.out;root=batch.root;assert (out/'test-and-oracle-complete.txt').read_text()=='PASS\n'
 deps=root/'src/FileCat.Archives.Writing/bin/Release/net10.0/FileCat.Archives.Writing.deps.json';libraries=json.loads(deps.read_text())['libraries'];assert all('Rar15' not in name and 'DiscUtils' not in name for name in libraries)
 shutil.copyfile(deps,out/deps.name);(out/'writing-closure.json').write_text(json.dumps({'libraries':sorted(libraries),'Rar15_or_DiscUtils_present':False},indent=2))
 binaries=out/'binaries';binaries.mkdir();rows=[]
 projects=['tests/FileCat.Core.Tests','tests/FileCat.WorkerProbe','src/FileCat.ArchiveWorker','src/FileCat.Archives.Writing']
 folders=[root/p/'bin/Release/net10.0' for p in projects]
 if sys.platform=='win32':folders += [root/p/'bin/Release/net10.0' for p in ('tests/FileCat.Platform.Windows.Tests','tests/FileCat.App.Tests')]+[out/'app',out/'probe']
 for folder in folders:
  for file in sorted(folder.iterdir()):
   if file.is_file() and (file.name.startswith('FileCat.') or file.name in ('FileCat.dll','FileCat.pdb')) and file.suffix in ('.dll','.pdb'):
    destination=binaries/file.name
    if destination.exists():assert sha(destination)==sha(file),(file,destination)
    else:shutil.copyfile(file,destination)
    rows.append({'path':str(file),'bytes':file.stat().st_size,'sha256':sha(file)})
 (out/'actual-binaries.json').write_text(json.dumps({'files':rows},indent=2))
 if sys.platform=='win32':
  assets=[]
  for name in ('app','probe'):
   for file in sorted((out/name).rglob('*')):
    assert not file.is_symlink()
    if file.is_file():assets.append({'path':file.relative_to(out).as_posix(),'bytes':file.stat().st_size,'sha256':sha(file)})
  (out/'actual-native-assets.json').write_text(json.dumps({'files':assets},indent=2))
 extra='C:/Users/Admin/.nuget/packages' if sys.platform=='win32' else str(Path.home()/'.nuget/packages')
 batch.execute('source-audit',[batch.dotnet,str(root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'),str(root),str(binaries),str(out/'compiler-sources'),extra])
 (out/'machine.json').write_text(json.dumps({'platform':platform.platform(),'sdk':subprocess.check_output([batch.dotnet,'--info'],env=batch.env,text=True),'new_profile_registered':False},indent=2))
 batch.idle();census={'busy':False,'owned_filecat_or_test_processes':0}
 if sys.platform=='darwin':
  wake=subprocess.check_output(['/bin/ps','-p','36765','-o','pid=,command='],text=True);assert wake.strip();census['preserved_wake_helper']=wake.strip()
 (out/'final-census.json').write_text(json.dumps(census,indent=2));(out/'complete.txt').write_text('PASS: semantic planning, retained-stream rewrite, role-bound workers and independent readers. Profile registration and remaining qualification gates are open.\n')
 rows=[]
 for file in sorted(out.rglob('*')):
  assert not file.is_symlink()
  if file.is_file():rows.append({'path':file.relative_to(out).as_posix(),'bytes':file.stat().st_size,'sha256':sha(file)})
 (out/'files.json').write_text(json.dumps({'files':rows,'production_backend_qualified':False},indent=2))
 archive=batch.task/(out.name+'-evidence.tar.gz')
 with tarfile.open(archive,'x:gz',dereference=False) as tar:
  for file in sorted(out.rglob('*')):tar.add(file,arcname=out.name+'/'+file.relative_to(out).as_posix(),recursive=False)
 facts={'bytes':archive.stat().st_size,'sha256':sha(archive),'rows':len(rows)};(archive.with_suffix('').with_suffix('.json')).write_text(json.dumps(facts,indent=2));print(json.dumps(facts),flush=True)
