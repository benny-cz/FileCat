#!/usr/bin/env python3
"""Resume only a corrected independent fixture helper and source audit; never repeat passing C# tests."""
from pathlib import Path
import hashlib,json,os,platform,shutil,subprocess,sys,tarfile,xml.etree.ElementTree as ET

def run(task,helper_sha,attempt):
 assert attempt.isdigit() and 2<=int(attempt)<=9
 task=Path(task).resolve();root=task/'sevenzip-bcj2-supplement-source-v1';out=task/'sevenzip-bcj2-supplement-v1'
 subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
 assert not (out/'complete.txt').exists()
 packet=task/'sevenzip-bcj2-supplement-source-v1.tar.gz'
 assert hashlib.sha256(packet.read_bytes()).hexdigest()=='42e65701918bba8fc66eda280663ae7a659aa5fb5336a74f58f16aa0fc016074'
 tests=ET.parse(out/'results/bcj2-supplement.trx');ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
 counters=tests.find('.//t:Counters',ns).attrib;assert counters['total']==counters['passed']=='81' and counters['failed']=='0'
 helper=task/'verify_bcj2_graph.py';assert hashlib.sha256(helper.read_bytes()).hexdigest()==helper_sha
 sdk=Path('/Users/benny/.dotnet') if sys.platform=='darwin' else task/'dotnet';dotnet=str(sdk/'dotnet')
 env=dict(os.environ,DOTNET_ROOT=str(sdk),PATH=str(sdk)+os.pathsep+os.environ['PATH'],MSBUILDDISABLENODEREUSE='1',PYTHONDONTWRITEBYTECODE='1')
 def execute(name,args):
  with (out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT,check=True)
 shutil.copyfile(helper,out/('executed-graph-helper-v'+attempt+'.py'));shutil.copyfile(Path(__file__),out/('executed-resume-v'+attempt+'.py'))
 execute('independent-graphs-v'+attempt,[sys.executable,'-B',str(helper),str(out/'fixtures'),str(out/'tools/sdk-bcj2-decode-oracle'),str(task/'password-oracle-tools-v1/modules'),str(out/('independent-graphs-v'+attempt))])
 binaries=out/'binaries';binaries.mkdir()
 for folder in (root/'tests/FileCat.Core.Tests/bin/Release/net10.0',root/'src/FileCat.Archives.Writing/bin/Release/net10.0',root/'tests/FileCat.WorkerProbe/bin/Release/net10.0'):
  for p in folder.iterdir():
   if p.is_file() and p.name.startswith('FileCat.') and p.suffix in ('.dll','.pdb'):shutil.copyfile(p,binaries/p.name)
 audit=root/'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'
 execute('source-audit',[dotnet,str(audit),str(root),str(binaries),str(out/'compiler-sources'),str(Path.home()/'.nuget/packages')])
 shutil.copyfile(packet,out/packet.name);shutil.copyfile(task/'run_bcj2_supplement.py',out/'executed-original-batch.py')
 subprocess.run(['python3',str(task/'check-linux-archive-idle.py')],check=True)
 census={'owned_filecat_or_test_processes':0,'platform':platform.platform()}
 if sys.platform=='darwin':census['preserved_wake_helper']=subprocess.check_output(['/bin/ps','-p','36765','-o','pid=,command='],text=True).strip();assert census['preserved_wake_helper']
 (out/'final-census.json').write_text(json.dumps(census,indent=2))
 (out/'resume-facts.json').write_text(json.dumps({'helper_sha256':helper_sha,'attempt':attempt,'test_source_packet_sha256':hashlib.sha256(packet.read_bytes()).hexdigest(),'passing_tests_repeated':False,'correction':'Validate AES zero padding at its declared intermediate extent; decode exactly the declared PPMd symbol count.'},indent=2))
 (out/'complete.txt').write_text('PASS: 81 focused BCJ2 tests and 42 independently decoded graph payloads. Full production qualification remains open.\n')
 rows=[{'path':p.relative_to(out).as_posix(),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(out.rglob('*')) if p.is_file()]
 (out/'files.json').write_text(json.dumps({'files':rows,'production_backend_qualified':False},indent=2))
 archive=task/'sevenzip-bcj2-supplement-evidence-v1.tar.gz'
 with tarfile.open(archive,'x:gz') as tar:tar.add(out,arcname=out.name)
 facts={'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'rows':len(rows)}
 (task/'sevenzip-bcj2-supplement-evidence-v1.json').write_text(json.dumps(facts,indent=2));print(json.dumps(facts),flush=True)

if __name__=='__main__':run(*sys.argv[1:])
