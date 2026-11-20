#!/usr/bin/env python3
"""Guest-only independent boundary checks and a pinned pre-fix assembly failure control."""
from pathlib import Path
import collections,hashlib,json,os,shutil,subprocess,sys,xml.etree.ElementTree as ET

def run(root,out,task):
 root=Path(root);out=Path(out);task=Path(task);windows=sys.platform=='win32';mac=sys.platform=='darwin'
 helpers=root/'eng/validation/archive-writing/rar5-independent-probe'
 tool=task/'password-oracle-tools-v1/7zip/7z.exe' if windows else task/'sevenzip-ppmd-filter-v2/tools'/('mac' if mac else 'linux')/'7zz'
 expected_tool='94205cfc054dd3bbeb978905bc619a65b3f04e9e73bb2717841110b6f46d962e' if windows else '8e495531e1b2b8cb169ca583493922d4ab386007756418c15b95f07f642c35fb' if mac else 'd32657a7a0e4cdc0a8aa57cafe474746944d76e861e753e9c902b9c9356fa886'
 assert hashlib.sha256(tool.read_bytes()).hexdigest()==expected_tool
 with (out/'official-boundary.log').open('xb') as log:
  subprocess.run([sys.executable,'-B',str(helpers/'verify_boundary_cli.py'),str(out/'boundary-fixtures'),str(tool)],check=True,cwd=root,stdout=log,stderr=subprocess.STDOUT)
 expected=json.loads((root/'docs/validation/archive-writing/rar5-csharp-checkpoint.json').read_text())['platforms'][0]['archive_hashes']
 actual={p.parent.name:hashlib.sha256(p.read_bytes()).hexdigest().upper() for p in (out/'rar-fixtures').glob('*/members.rar')}
 assert len(actual)==6 and actual==expected
 (out/'previous-rar-archives-unchanged.json').write_text(json.dumps(actual,indent=2))
 control=out/'baseline-control';control.mkdir();binaries=control/'bin';binaries.mkdir()
 # These six tests generate all their data in memory. Preserve executable modes; no TestData tree is needed.
 for path in (root/'tests/FileCat.Core.Tests/bin/Release/net10.0').iterdir():
  if path.is_file():shutil.copy2(path,binaries/path.name)
 base=task/'strict-rar5-framing-v1';manifest=json.loads((base/'files.json').read_text(encoding='utf-8-sig'))
 identities=[]
 for name in ['FileCat.Archives.dll','FileCat.Archives.pdb']:
  row=next(r for r in manifest['files'] if r['path']=='binaries/'+name);original=base/row['path']
  assert original.stat().st_size==row['bytes'] and hashlib.sha256(original.read_bytes()).hexdigest().lower()==row['sha256'].lower()
  shutil.copyfile(original,binaries/name);identities.append(row)
 (control/'assembly-origin.json').write_text(json.dumps({'source_packet_sha256':'5bf8c874773c606912a1604b0613945717c3a44343116017e8238634ff0528fc','replaced_only':identities,'expected_failures':5,'expected_passes':1},indent=2))
 if windows:
  command=['C:\\Program Files\\dotnet\\dotnet.exe','exec','C:\\Users\\fcarchive42829297\\FileCatValidation\\Capture-42829297-v7\\tester\\vstest.console.dll']
 else:
  dotnet=Path('/Users/benny/.dotnet/dotnet') if mac else task/'dotnet/dotnet';command=[str(dotnet),'vstest']
 command += [str(binaries/'FileCat.Core.Tests.dll'),'/TestCaseFilter:FullyQualifiedName~Rar5WritingBoundaryTests','/Logger:trx;LogFileName=boundary-baseline.trx','/ResultsDirectory:'+str(control/'results')]
 env=dict(os.environ)
 for name in list(env):
  if name.startswith('FILECAT_'):env.pop(name)
 with (control/'tests.log').open('xb') as log:result=subprocess.run(command,cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT,timeout=180)
 assert result.returncode==1,result.returncode
 ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
 tests=ET.parse(control/'results/boundary-baseline.trx').findall('.//t:UnitTestResult',ns)
 counts=collections.Counter(t.get('outcome') for t in tests);assert len(tests)==6 and counts=={'Failed':5,'Passed':1},counts
 (control/'expected-failure-control.json').write_text(json.dumps({'counts':dict(counts),'all_expected':True,'current_source_tests_passed_separately':True,'command':command},indent=2))
 print('PASS: three independent boundary payloads; six old archives unchanged; pre-fix assembly reproduces five expected boundary failures.',flush=True)

if __name__=='__main__':run(*sys.argv[1:])
