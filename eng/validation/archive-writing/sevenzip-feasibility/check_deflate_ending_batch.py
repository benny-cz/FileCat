"""Guest-only native zlib comparison and bounded pre-fix controls for the legacy inflater history/window fixes."""
from pathlib import Path
import collections,hashlib,json,os,shutil,subprocess,sys,tarfile,xml.etree.ElementTree as E

root,out,task=map(Path,sys.argv[1:4]);resume=sys.argv[4:]==['--resume-control-launch'];windows=sys.platform=='win32';mac=sys.platform=='darwin';which='windows' if windows else 'mac' if mac else 'ubuntu'
helpers=root/'eng/validation/archive-writing/sevenzip-feasibility'
def execute(name,args):
 with (out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=root,stdout=log,stderr=subprocess.STDOUT,check=True,timeout=1800)
if resume:
 native=json.loads((out/'vectors/independent-deflate.json').read_text());assert native['accepted']==55 and not native['failures'] and native['preset_dictionary_verified']
 failed=out/'baseline-control';assert (failed/'results/pre-fix.trx').exists()
 tests=E.parse(failed/'results/pre-fix.trx').findall('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTestResult');assert not tests
 assert 'Permission denied' in (failed/'tests.log').read_text()
 failed.rename(out/'baseline-control-launch-failure')
else:execute('native-zlib',[sys.executable,'-B',str(helpers/'verify_deflate_endings.py'),str(out/'vectors')])
control=out/'baseline-control';control.mkdir();binaries=control/'bin';binaries.mkdir()
for path in (root/'tests/FileCat.Core.Tests/bin/Release/net10.0').iterdir():
 if path.is_file():shutil.copy2(path,binaries/path.name)
packet=task/'range-ending-evidence-v2.tar.gz'
expected={'mac':'6a8adc67f6fa1c56cfa38f70d05255f7e80327e335fa9f8aa779a7a47069e6e3',
 'ubuntu':'d0866b8636b6e23fe92e06e9219fdc78b5ec5d8784e06db16c508104dac508e8',
 'windows':'4098e54f07bfc00cb610fdb7f58a3cc2221ae5cf96cb6353e82346e9c7a68808'}[which]
assert hashlib.sha256(packet.read_bytes()).hexdigest().lower()==expected
with tarfile.open(packet) as tar:
 manifest=json.load(tar.extractfile('range-ending-v2/files.json'));identities=[]
 for name in ['FileCat.SharpCompress.dll','FileCat.SharpCompress.pdb']:
  row=next(row for row in manifest['files'] if row['path']=='binaries/'+name);data=tar.extractfile('range-ending-v2/'+row['path']).read()
  assert len(data)==row['bytes'] and hashlib.sha256(data).hexdigest().lower()==row['sha256'].lower();(binaries/name).write_bytes(data);identities.append(row)
selected='FullyQualifiedName~StrictDeflateStreamTests.The_legacy_inflater_never_emits_seeded_pool_bytes_for_missing_history|FullyQualifiedName~StrictDeflateStreamTests.Window_storage_is_cleared_before_returning_it_to_its_owner|FullyQualifiedName~StrictDeflateStreamTests.A_history_reset_does_not_treat_a_previous_output_counter_as_new_history'
if windows:command=['C:\\Program Files\\dotnet\\dotnet.exe','exec','C:\\Users\\fcarchive42829297\\FileCatValidation\\Capture-42829297-v7\\tester\\vstest.console.dll']
else:command=[str(Path('/Users/benny/.dotnet/dotnet') if mac else task/'dotnet/dotnet'),'vstest']
command += [str(binaries/'FileCat.Core.Tests.dll'),'/TestCaseFilter:'+selected,'/Logger:trx;LogFileName=pre-fix.trx','/ResultsDirectory:'+str(control/'results')]
env={key:value for key,value in os.environ.items() if not key.startswith('FILECAT_')}
with (control/'tests.log').open('xb') as log:result=subprocess.run(command,cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT,timeout=180)
assert result.returncode==1,result.returncode
tests=E.parse(control/'results/pre-fix.trx').findall('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTestResult')
counts=dict(collections.Counter(test.get('outcome') for test in tests));assert counts=={'Failed':4} and len(tests)==4,counts
for test in tests:
 message=''.join(test.itertext())
 if 'Window_storage' in test.get('testName'):assert 'Assert.Equal()' in message,message
 else:assert 'Assert.Throws()' in message and 'No exception was thrown' in message,message
(control/'expected-failure-control.json').write_text(json.dumps({'baseline_component_source_commit':'ea911bcf','baseline_packet_sha256':expected,
 'replaced_only':identities,'counts':counts,'all_expected':True,'current_source_passed_separately':True,'command':command},indent=2))
print('PASS: independent native zlib comparisons and four bounded expected pre-fix failures.',flush=True)
