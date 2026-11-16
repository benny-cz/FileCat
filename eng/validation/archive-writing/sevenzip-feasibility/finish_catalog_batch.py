from pathlib import Path
import hashlib,json,runpy,shutil,subprocess,sys,xml.etree.ElementTree as ET
phase,task,expected=sys.argv[1:];task=Path(task).resolve();windows=sys.platform=='win32';root=task/('sevenzip-graph-source-v1' if windows else 'sevenzip-graph-source-v3');out=task/'sevenzip-graph-v4'
module=runpy.run_path(str(root/'eng/validation/archive-writing/sevenzip-feasibility/run_catalog_delta.py'));batch=module['Batch'](task);batch.idle()
if phase=='oracle':
 ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};files=list((out/'results').glob('*.trx'));assert len(files)==1
 doc=ET.parse(files[0]);counters=doc.find('.//t:Counters',ns).attrib;assert counters['total']=='3310' and counters['failed']=='0'
 cases=[r for r in doc.findall('.//t:UnitTestResult',ns) if '.StrictSevenZipCatalogTests.' in r.attrib['testName']];assert len(cases)==316 and all(r.attrib['outcome']=='Passed' for r in cases)
 helper=out/'verify-catalog-v6.py';assert hashlib.sha256(helper.read_bytes()).hexdigest()==expected
 base=task/('sevenzip-bcj2-supplement-v3' if windows else 'sevenzip-bcj2-supplement-v1');oracle=base/'tools'/('sdk-bcj2-decode-oracle.exe' if windows else 'sdk-bcj2-decode-oracle')
 row=next(r for r in json.loads((base/'files.json').read_text(encoding='utf-8-sig'))['files'] if r['path']=='tools/'+oracle.name);assert hashlib.sha256(oracle.read_bytes()).hexdigest().lower()==row['sha256'].lower()
 filters=task/('sevenzip-ppmd-filter-v2/tools/windows/sdk-oracle.exe' if windows else 'sevenzip-ppmd-filter-v2/sdk-oracle')
 ledger=task/'sevenzip-ppmd-filter-v2'/('tools/files.json' if windows else 'files.json');key='windows/sdk-oracle.exe' if windows else 'sdk-oracle'
 row=next(r for r in json.loads(ledger.read_text(encoding='utf-8-sig'))['files'] if r['path']==key);assert hashlib.sha256(filters.read_bytes()).hexdigest().lower()==row['sha256'].lower()
 tool=task/('password-oracle-tools-v1/7zip/7z.exe' if windows else 'sevenzip-ppmd-filter-v2/tools/'+('mac' if sys.platform=='darwin' else 'linux')+'/7zz')
 expected_tool='94205cfc054dd3bbeb978905bc619a65b3f04e9e73bb2717841110b6f46d962e' if windows else ('8e495531e1b2b8cb169ca583493922d4ab386007756418c15b95f07f642c35fb' if sys.platform=='darwin' else 'd32657a7a0e4cdc0a8aa57cafe474746944d76e861e753e9c902b9c9356fa886');assert hashlib.sha256(tool.read_bytes()).hexdigest()==expected_tool
 batch.execute('cached-modules-v6',[sys.executable,'-B',str(root/'eng/validation/archive-writing/sevenzip-feasibility/verify_oracle_module_reuse.py'),str(task/'password-oracle-tools-v1'),'windows' if windows else 'mac' if sys.platform=='darwin' else 'ubuntu'])
 shutil.copyfile(Path(__file__),out/'executed-finish-v6.py')
 batch.execute('independent-catalog-v6',[sys.executable,'-B',str(helper),str(out/'vectors'),str(tool),str(task/'password-oracle-tools-v1/modules'),str(filters),str(oracle)])
 (out/'test-and-oracle-complete.txt').write_text('PASS\n')
elif phase=='finish':batch.finish()
else:raise SystemExit('Unknown phase')
