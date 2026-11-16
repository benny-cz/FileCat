"""Independent payload checks with CPU/output-limited native readers of owned capture fixtures."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import resource
import subprocess
import sys
import tempfile
from zstd_oracle_common import NativeZstd

parser=argparse.ArgumentParser();parser.add_argument('directory',type=Path);args=parser.parse_args()
directory=args.directory.resolve(strict=True);oracle=NativeZstd();checked=[]
def limits():
 resource.setrlimit(resource.RLIMIT_CPU,(15,15));resource.setrlimit(resource.RLIMIT_FSIZE,(1<<20,1<<20))
 if sys.platform.startswith('linux'):resource.setrlimit(resource.RLIMIT_AS,(512<<20,512<<20))
def tar(arguments,data):
 with tempfile.TemporaryFile() as output,tempfile.TemporaryFile() as error:
  environment=dict(os.environ,LC_ALL='en_US.UTF-8' if sys.platform=='darwin' else 'C.UTF-8')
  result=subprocess.run(['/usr/bin/tar',*arguments],input=data,stdout=output,stderr=error,timeout=20,preexec_fn=limits,env=environment)
  error.seek(0);diagnostic=error.read(1<<20)
  assert result.returncode==0,(result.returncode,diagnostic[:2048])
  output.seek(0);return output.read((1<<20)+1)
for archive in sorted(directory.glob('*.archive')):
 assert not archive.is_symlink() and archive.stat().st_size<=16<<20
 recipe=json.loads(archive.with_suffix('.json').read_text())
 assert recipe['format'] in ('Tar','TarGzip','TarBzip2','TarZstd')
 assert recipe['prefix']=='界-' and recipe['suffix']=='.bin' and recipe['reversed'] is True
 count=recipe['count'];assert type(count) is int and 1<=count<=10000
 data=archive.read_bytes()
 if recipe['format']=='TarZstd':data=oracle.decompress(data)
 compression=['-z'] if recipe['format']=='TarGzip' else ['-j'] if recipe['format']=='TarBzip2' else []
 expected=['界-'+str(id)+'.bin' for id in range(count,0,-1)]
 assert tar([*compression,'-tf','-'],data).decode('utf-8').splitlines()==expected
 for id,name in zip(range(count,0,-1),expected):
  payload=tar([*compression,'-xOf','-',name],data)
  assert payload==bytes((id&255,0,255,(id>>8)&255)),(archive.name,name)
 checked.append(dict(archive=archive.name,bytes=archive.stat().st_size,sha256=hashlib.sha256(archive.read_bytes()).hexdigest(),members=count,each_payload_bytes=4))
assert len(checked)==4
facts=dict(component_checks_passed=True,production_backend_qualified=False,reader=subprocess.check_output(['/usr/bin/tar','--version'],text=True).strip(),
 oracle_limits=dict(cpu_seconds=15,output_bytes=1<<20,wall_seconds=20,address_space_bytes=(512<<20) if sys.platform.startswith('linux') else None,macos_hard_memory_qualified=False),zstd=oracle.facts(),checked=checked)
with (directory/'independent-verified.json').open('x') as stream:json.dump(facts,stream,indent=2);stream.write('\n')
print(json.dumps(facts))
