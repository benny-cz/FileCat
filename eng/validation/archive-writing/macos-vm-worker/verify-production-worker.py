"""Check the actual helper dependency closure, startup refusals and shared guest kernel policy."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import select
import subprocess
import time
import uuid

parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,required=True);args=parser.parse_args()
root=args.root.resolve(strict=True);out=root/'production-policy';out.mkdir(exist_ok=False)
publish=root/'guest-publish'
required={'FileCat.ArchiveWorker.dll','FileCat.Archives.Writing.dll','FileCat.Core.dll','FileCat.SharpCompress.dll'}
assert required <= {path.name for path in publish.iterdir()}
assert not any('rar15' in path.name.lower() or 'discutils' in path.name.lower() or path.name=='FileCat.Archives.dll' for path in publish.iterdir())
deps=json.loads((publish/'FileCat.ArchiveWorker.deps.json').read_text())
assert not any('rar15' in name.lower() or 'discutils' in name.lower() for name in deps['libraries'])
assert b'/worker/FileCat.ArchiveWorker\0' in (root/'input/init-control').read_bytes()

def idle():
 for line in subprocess.check_output(['/bin/ps','-axo','pid=,comm='],text=True).splitlines():
  fields=line.strip().split(None,1)
  if len(fields)==2 and (Path(fields[1]).name.startswith(('FileCat','testhost')) or Path(fields[1]).name in ('dotnet','filecat')):
   raise SystemExit('Another tester is active; postpone.\n'+line)
def read_exact(fd,count):
 data=b'';deadline=time.monotonic()+15
 while len(data)<count:
  left=deadline-time.monotonic();assert left>0 and select.select([fd],[],[],left)[0]
  part=os.read(fd,count-len(data));assert part;data+=part
 return data
def launch(name,image,header,policy=False):
 idle();reader,writer=os.pipe();logpath=out/(name+'.log')
 with logpath.open('xb') as log:
  try:p=subprocess.Popen([str(root/'FileCat.ArchiveLinuxVmWorker'),str(root/'input/kernel.Image'),str(image),'1024',str(os.getpid()),str(reader)],
   pass_fds=(reader,),stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=log,start_new_session=True)
  finally:os.close(reader)
  try:
   assert read_exact(p.stdout.fileno(),8)==b'FCVMRDY1';p.stdin.write(header);p.stdin.flush()
   fields=None
   if policy:
    assert read_exact(p.stdout.fileno(),8)==b'FCVMPOL1'
    size=int.from_bytes(read_exact(p.stdout.fileno(),4),'little');assert 0<size<=8192
    fields=json.loads(read_exact(p.stdout.fileno(),size))
    expected={'runtimeRead','directoryLinkDenied','fileLinkDenied','createDenied','controlWriteDenied','sealedReadDenied','hostReadDenied',
     'tcpDenied','udpDenied','unixSocketDenied','processCloneDenied','execDenied','execAtDenied','openAt2Denied','limitRaiseDenied',
     'parentSignalDenied','namespaceMutationDenied','ownThreadSignalProbeAllowed','supervisorThreadSignalDenied',
     'unapprovedThreadSignalDenied','tracingDenied','newNamespaceDenied','parentDeathClearDenied','archiveDirectoryAndFileOpened',
     'archiveDirectoryLinkDenied','archiveFileLinkDenied','aclDirectoryOpened','aclDirectoryLinkDenied','aclFileAsDirectoryDenied'}
    assert fields.keys()==expected and all(value is True for value in fields.values()),fields
   assert p.wait(timeout=10)==0
   remaining=p.stdout.read();assert remaining==b''
   text=logpath.read_text();rows=[json.loads(line.removeprefix('FILECAT_HOST_FACTS '))for line in text.splitlines()if line.startswith('FILECAT_HOST_FACTS ')]
   assert len(rows)==1 and rows[0]['state']==0 and rows[0]['guestStopped'] is True and rows[0]['forcedStop'] is False
   assert ('FILECAT_WAIT_STATUS=0000000000000000' if policy else 'FILECAT_WAIT_STATUS=0000000000004100') in text
   return dict(case=name,pid=p.pid,exit=p.returncode,reaped=True,fields=fields)
  finally:
   if p.poll() is None:p.kill();p.wait(timeout=5)
   p.stdin.close();p.stdout.close();os.close(writer)

sentinel=Path('/Users/benny/FileCatValidation/archive-writing-42829297/guest-host-read-sentinel')
content=b'Owned guest boundary sentinel 42829297.\n'
with sentinel.open('xb') as stream:stream.write(content)
os.chmod(sentinel,0o600)
try:
 header=b'FCVMIPC1'+uuid.uuid4().bytes+b'\x08'+bytes(7)
 policy=launch('kernel-policy',root/'probe-image/input/initrd-worker.cpio',header,policy=True)
 assert sentinel.read_bytes()==content
finally:sentinel.unlink()
refused=[]
for name,role in [('diagnostic-role',4),('policy-role',8),('zero-role',0)]:
 refused.append(launch(name,root/'input/initrd-worker.cpio',b'FCVMIPC1'+uuid.uuid4().bytes+bytes([role])+bytes(7)))
bad=bytearray(b'FCVMIPC1'+uuid.uuid4().bytes+b'\x01'+bytes(7));bad[25]=1
refused.append(launch('reserved-byte',root/'input/initrd-worker.cpio',bad))
refused.append(launch('empty-nonce',root/'input/initrd-worker.cpio',b'FCVMIPC1'+bytes(16)+b'\x01'+bytes(7)))
idle()
facts=dict(component_checks_passed=True,production_backend_qualified=False,dependency_libraries=sorted(deps['libraries']),
 policy=policy,refused_startups=refused,host_sentinel_sha256=hashlib.sha256(content).hexdigest(),host_sentinel_unchanged=True)
(out/'facts.json').write_text(json.dumps(facts,indent=2)+'\n');print(json.dumps(facts))
