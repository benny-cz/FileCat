"""Copy verified cached inputs to a fresh run, overlay only a test DLL/PDB and run selected methods."""
import hashlib, json, pathlib, shutil, subprocess, sys

driver=pathlib.Path(__file__).resolve().parent
output=driver.parent
plan=json.loads((driver/'plan.json').read_text(encoding='utf-8-sig'))
revision=plan['revision']
def state(value): (output/'state.txt').write_text(value+'\n')
def busy():
 for command in (['pgrep','-x','FileCat([.].*)?'],['pgrep','-f','^[^ ]*dotnet +.*FileCat']):
  probe=subprocess.run(command,capture_output=True)
  if probe.returncode==0:return True
  if probe.returncode!=1:raise RuntimeError('Process inventory failed')
 return False
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
try:
 if busy():state('BUSY phase=verify');sys.exit(75)
 baseline=pathlib.Path(plan['baseline']).resolve()
 if not baseline.is_relative_to(pathlib.Path.home()/'FileCatValidation'):raise RuntimeError('Unexpected baseline root')
 payload=output/'payload'
 if payload.exists():raise RuntimeError('Fresh focused payload required')
 for item in plan['files']:
  relative=pathlib.PurePosixPath(item['path'])
  if relative.is_absolute()or'..'in relative.parts:raise RuntimeError('Invalid cached input path')
  source=(baseline/relative).resolve()
  if not source.is_relative_to(baseline)or digest(source)!=item['baselineSha256']:raise RuntimeError('Cached input changed')
 for item in plan['files']:
  relative=pathlib.PurePosixPath(item['path'])
  if item['patch']and item['path']not in(plan['assembly'],plan['assembly'][:-4]+'.pdb'):raise RuntimeError('Production input patch refused')
  source=driver/'patch'/relative if item['patch']else baseline/relative
  target=payload/relative;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
  if digest(target)!=item['sha256']:raise RuntimeError('Copied input mismatch')
 (output/'cache-proof.txt').write_text('VERIFIED cachedFiles='+str(len(plan['files']))+' changedFiles='+str(sum(bool(row['patch'])for row in plan['files']))+'\n')
 if busy():state('BUSY phase=render');sys.exit(75)
 arguments=[str(pathlib.Path.home()/'.dotnet/dotnet'),str(payload/plan['assembly'])]
 for method in plan['methods']:arguments+=['-method',method]
 arguments+=['-noLogo','-noColor','-maxThreads','2','-xml',str(output/('render-'+revision+'.xml'))]
 state('RUNNING phase=render')
 with(output/('render-'+revision+'.log')).open('wb')as log:code=subprocess.run(arguments,cwd=payload,stdout=log,stderr=subprocess.STDOUT).returncode
 state('FINISHED exit='+str(code));sys.exit(code)
except Exception as error:
 (output/'failure.txt').write_text(str(error));state('FAILED');raise
