#!/usr/bin/env python3
"""Guest-only independent metadata, padded-ID CLI and pinned pre-fix AES-construction disposal controls."""
from pathlib import Path
import collections,hashlib,json,os,shutil,subprocess,sys,tarfile,xml.etree.ElementTree as ET

def run(root,out,task,resume=None):
 root=Path(root);out=Path(out);task=Path(task);windows=sys.platform=='win32';mac=sys.platform=='darwin';which='windows' if windows else 'mac' if mac else 'ubuntu'
 helpers=root/'eng/validation/archive-writing/sevenzip-feasibility';tools=task/'password-oracle-tools-v1'
 def execute(name,args):
  with (out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=root,stdout=log,stderr=subprocess.STDOUT,check=True,timeout=300)
 assert resume in (None,'--resume-independent')
 if resume:
  assert not (out/'files.json').exists(),'Evidence is already frozen.'
  prior=json.loads((out/'logical-fixtures/independent-framing.json').read_text(encoding='utf-8'))
  assert (prior['archives'],prior['metadata_records'],prior['exact_payloads'])==(42,126,84)
  for item in prior['verified']:
   assert hashlib.sha256((out/'logical-fixtures'/item['fixture']/'members.7z').read_bytes()).hexdigest().upper()==item['archive_sha256']
 execute('reader-module-reuse-resume' if resume else 'reader-module-reuse',[sys.executable,'-B',str(helpers/'verify_oracle_module_reuse.py'),str(tools),which])
 if not resume:execute('independent-metadata',[sys.executable,'-B',str(helpers/'verify_framing_metadata.py'),str(out/'logical-fixtures'),str(tools/'modules')])
 tool=tools/'7zip/7z.exe' if windows else task/'sevenzip-ppmd-filter-v2/tools'/('mac' if mac else 'linux')/'7zz'
 expected_tool='94205cfc054dd3bbeb978905bc619a65b3f04e9e73bb2717841110b6f46d962e' if windows else json.loads((tools/'official-tool-identities.json').read_text(encoding='utf-8-sig'))[which]['sha256']
 assert hashlib.sha256(tool.read_bytes()).hexdigest().lower()==expected_tool.lower()
 padded=[]
 for fixture in sorted((out/'logical-fixtures').glob('padded-*')):
  expected=json.loads((fixture/'expected.json').read_text(encoding='utf-8'));archive=fixture/'members.7z';assert archive.stat().st_size<1<<20
  result=subprocess.run([str(tool),'t','-bd','-y',str(archive)],capture_output=True,timeout=120)
  (fixture/'official-test.log').write_bytes(result.stdout+result.stderr);assert result.returncode==0,(fixture.name,result.stderr)
  result=subprocess.run([str(tool),'x','-so','-bd','-y',str(archive),'--',expected['name']],capture_output=True,timeout=120)
  (fixture/'official-extract.log').write_bytes(result.stderr)
  assert result.returncode==0 and len(result.stdout)==expected['length'] and hashlib.sha256(result.stdout).hexdigest().upper()==expected['sha256']
  padded.append({'fixture':fixture.name,'bytes':len(result.stdout),'sha256':expected['sha256']})
 assert len(padded)==2
 (out/'independent-padded-identifiers.json').write_text(json.dumps({'tool_sha256':expected_tool,'verified':padded},indent=2),encoding='utf-8')
 control=out/'baseline-control';control.mkdir();binaries=control/'bin';binaries.mkdir()
 for p in (root/'tests/FileCat.Core.Tests/bin/Release/net10.0').iterdir():
  if p.is_file():shutil.copy2(p,binaries/p.name)
 packet=task/'strict-rar5-catalog-evidence-v3.tar.gz'
 expected={'mac':'cac6955eaf2c1667e25629438dfd841f5ba4ac9c5953df3d440bd766f536d97d',
 'ubuntu':'48a99abebe15e88c3b62d83918947bf8859407762133cc759c57d2a478430ed4',
 'windows':'4e817486e0fa13007eaf510bf16510ed70d732e5036938a58075f702948621f7'}[which]
 assert hashlib.sha256(packet.read_bytes()).hexdigest()==expected
 identities=[]
 with tarfile.open(packet,'r:gz') as tar:
  manifest=json.load(tar.extractfile('strict-rar5-catalog-v3/files.json'))
  for name in ['FileCat.SharpCompress.dll','FileCat.SharpCompress.pdb']:
   row=next(row for row in manifest['files'] if row['path']=='binaries/'+name)
   data=tar.extractfile('strict-rar5-catalog-v3/'+row['path']).read();assert len(data)==row['bytes'] and hashlib.sha256(data).hexdigest().lower()==row['sha256'].lower()
   (binaries/name).write_bytes(data);identities.append(row)
 (control/'assembly-origin.json').write_text(json.dumps({'baseline_commit':'a5eb10bf','frozen_baseline_sha256':expected,
  'replaced_only':identities,'expected_failed_controls':2},indent=2),encoding='utf-8')
 if windows:command=['C:\\Program Files\\dotnet\\dotnet.exe','exec','C:\\Users\\fcarchive42829297\\FileCatValidation\\Capture-42829297-v7\\tester\\vstest.console.dll']
 else:command=[str(Path('/Users/benny/.dotnet/dotnet') if mac else task/'dotnet/dotnet'),'vstest']
 command += [str(binaries/'FileCat.Core.Tests.dll'),'/TestCaseFilter:FullyQualifiedName~StrictSevenZipFramingTests.Failed_decoder_construction_disposes_preceding_AES_transforms',
  '/Logger:trx;LogFileName=construction-baseline.trx','/ResultsDirectory:'+str(control/'results')]
 env={key:value for key,value in os.environ.items() if not key.startswith('FILECAT_')}
 with (control/'tests.log').open('xb') as log:result=subprocess.run(command,cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT,timeout=180)
 assert result.returncode==1,result.returncode
 ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
 tests=ET.parse(control/'results/construction-baseline.trx').findall('.//t:UnitTestResult',ns)
 counts=collections.Counter(test.get('outcome') for test in tests);assert len(tests)==2 and counts=={'Failed':2},counts
 for test in tests:
  text=''.join(test.itertext());assert 'Expected: 1' in text and 'Actual:   0' in text,text
 (control/'expected-failure-control.json').write_text(json.dumps({'counts':dict(counts),'all_expected':True,
  'current_source_tests_passed_separately':True,'command':command},indent=2),encoding='utf-8')
 print('PASS: independent 42-archive metadata/payload checks, two official padded-ID fixtures and two expected pre-fix disposal failures.',flush=True)

if __name__=='__main__':run(*sys.argv[1:])
