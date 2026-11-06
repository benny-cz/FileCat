"""Independent native extraction plus Python TAR metadata inspection for owned directory-workflow fixtures."""
import argparse,base64,decimal,hashlib,io,json,os,resource,subprocess,sys,tarfile,tempfile,bz2,gzip
from pathlib import Path
from zstd_oracle_common import NativeZstd
parser=argparse.ArgumentParser();parser.add_argument('directory',type=Path);args=parser.parse_args()
root=args.directory.resolve(strict=True);zstd=NativeZstd();checked=[]
def limits():
 resource.setrlimit(resource.RLIMIT_CPU,(15,15));resource.setrlimit(resource.RLIMIT_FSIZE,(1<<20,1<<20))
 if sys.platform.startswith('linux'):resource.setrlimit(resource.RLIMIT_AS,(512<<20,512<<20))
def native(arguments,data):
 with tempfile.TemporaryFile() as output,tempfile.TemporaryFile() as error:
  env=dict(os.environ,LC_ALL='en_US.UTF-8' if sys.platform=='darwin' else 'C.UTF-8')
  r=subprocess.run(['/usr/bin/tar',*arguments],input=data,stdout=output,stderr=error,timeout=20,preexec_fn=limits,env=env)
  error.seek(0);assert r.returncode==0,error.read(2048)
  output.seek(0);return output.read((1<<20)+1)
for archive in sorted(root.glob('*.archive')):
 assert not archive.is_symlink() and archive.stat().st_size<=16<<20
 recipe=json.loads(archive.with_suffix('.json').read_text());fmt=recipe['format']
 assert fmt in ('Tar','TarGzip','TarBzip2','TarZstd') and 1<=len(recipe['directories'])<=1000
 assert recipe['file']=='tree/file.bin' and all(d['name'] in ('tree/','empty/') for d in recipe['directories'])
 raw=archive.read_bytes();data=zstd.decompress(raw) if fmt=='TarZstd' else raw
 compression=['-z'] if fmt=='TarGzip' else ['-j'] if fmt=='TarBzip2' else []
 names=(['old'] if recipe.get('old',False) else [])+[d['name'] for d in recipe['directories']]+[recipe['file']]
 assert native([*compression,'-tf','-'],data).decode().splitlines()==names
 payload=base64.b64decode(recipe['payload'],validate=True)
 assert native([*compression,'-xOf','-',recipe['file']],data)==payload
 if recipe.get('old',False):assert native([*compression,'-xOf','-','old'],data)==b'before'
 if fmt in ('TarGzip','TarBzip2'):
  with (gzip.GzipFile(fileobj=io.BytesIO(data)) if fmt=='TarGzip' else bz2.BZ2File(io.BytesIO(data))) as wrapper:decoded=wrapper.read((16<<20)+1)
 else:decoded=data
 assert len(decoded)<=16<<20
 with tarfile.open(fileobj=io.BytesIO(decoded),mode='r:') as tar:
  entries=tar.getmembers();assert len(entries)==len(names)
  directories={m.name.rstrip('/')+'/':m for m in entries if m.isdir()}
  assert set(directories)=={d['name'] for d in recipe['directories']}
  for expected in recipe['directories']:
   member=directories[expected['name']];assert member.mode==expected['mode']
   instant=decimal.Decimal(member.pax_headers.get('mtime',str(member.mtime)))
   assert instant==decimal.Decimal(expected['seconds'])+decimal.Decimal(expected['nanos'])/1000000000,(archive.name,expected,instant)
 checked.append(dict(archive=archive.name,sha256=hashlib.sha256(raw).hexdigest(),members=len(entries),directory_metadata_matched=True))
assert len(checked)>=4
facts=dict(component_checks_passed=True,production_backend_qualified=False,reader=subprocess.check_output(['/usr/bin/tar','--version'],text=True).strip(),zstd=zstd.facts(),checked=checked,
 oracle_limits=dict(cpu_seconds=15,output_bytes=1<<20,wall_seconds=20,macos_hard_memory_qualified=False))
with (root/'independent-directory-verified.json').open('x') as f:json.dump(facts,f,indent=2)
print(json.dumps(facts))
