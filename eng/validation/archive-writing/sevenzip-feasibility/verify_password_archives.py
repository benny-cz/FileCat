#!/usr/bin/env python3
"""Private owned/public-vector password interoperability. No real user/job passwords, filesystem extraction or runtime dependency."""
from pathlib import Path
import hashlib,importlib.metadata,json,os,subprocess,sys

def run(root,tool,modules):
 root=Path(root).resolve();tool=Path(tool).resolve();modules=Path(modules).resolve()
 sys.path.insert(0,str(modules))
 import py7zr
 from py7zr.io import Py7zIO,WriterFactory
 facts={'synthetic_public_vectors':True,'production_dependency':False,'python':sys.version,
  'python_executable':sys.executable,'tool':str(tool),'tool_sha256':hashlib.sha256(tool.read_bytes()).hexdigest(),
  'packages':sorted(({'name':d.metadata['Name'],'version':d.version} for d in importlib.metadata.distributions(path=[str(modules)])),key=lambda d:d['name'])}
 info=subprocess.run([str(tool),'i'],capture_output=True,check=True,timeout=60)
 (root/'password-native-tool-info.txt').write_bytes(info.stdout+info.stderr)
 directories=sorted(p.parent for p in root.glob('*/members.json'));assert len(directories)==56
 native=[];pyrecords=[];unsupported=[];archives={}
 for folder in directories:
  vector=json.loads((folder/'public-vector.json').read_text());assert vector['synthetic_public_vector'] is True
  password=vector['public_synthetic_password'];assert password=='FileCat-Public-Archive-Vector-暗号-42829303'
  archive=folder/('members.rar' if vector['format']==0 else 'members.7z');assert archive.stat().st_size<4<<20
  expected=json.loads((folder/'members.json').read_text());assert len(expected) in (0,1,7)
  archives[folder.name]=hashlib.sha256(archive.read_bytes()).hexdigest()
  result=subprocess.run([str(tool),'t','-bd','-y','-sccUTF-8','-p'+password,str(archive)],capture_output=True,timeout=120)
  (folder/'password-native-test.log').write_bytes(result.stdout+result.stderr)
  if vector['format']==1 and vector['method']==5:
   assert result.returncode==2 and b'Unsupported Method' in result.stderr
   unsupported.append({'fixture':folder.name,'method':'04f71101','native_tool':'official 7-Zip 26.05'})
  else:
   assert result.returncode==0,(folder.name,result.stdout[-2048:],result.stderr)
   for row in expected:
    if row['directory']:continue
    with subprocess.Popen([str(tool),'x','-so','-bd','-y','-p'+password,str(archive),'--',row['name']],stdout=subprocess.PIPE,stderr=subprocess.PIPE) as child:
     count=0;digest=hashlib.sha256()
     while True:
      block=child.stdout.read(65536)
      if not block:break
      count+=len(block)
      if count>row['length'] or count>1<<20:child.kill();raise AssertionError('Native public vector exceeds its payload extent')
      digest.update(block)
     error=child.stderr.read(65536);assert child.wait(timeout=120)==0,(folder.name,row['name'],error)
    assert count==row['length'] and digest.hexdigest().upper()==row['sha256'],(folder.name,row)
    native.append({'fixture':folder.name,'name':row['name'],'bytes':count,'sha256':digest.hexdigest()})
  if vector['format']==0 or vector.get('filter_id',0)!=0:continue
  known={r['name']:r for r in expected if not r['directory']};products={}
  class Sink(Py7zIO):
   def __init__(self,row):self.row=row;self.count=0;self.digest=hashlib.sha256()
   def write(self,data):
    assert self.count+len(data)<=self.row['length']<=1<<20
    self.digest.update(data);self.count+=len(data);return len(data)
   def read(self,size=None):return self.digest.digest()
   def seek(self,offset,whence=0):
    assert (whence==0 and offset in (0,self.count)) or (whence in (1,2) and offset==0)
    return self.count
   def flush(self):pass
   def size(self):return self.count
  class Factory(WriterFactory):
   def create(self,filename):
    # py7zr sanitizes output names even for a memory factory; the exact approved suffix remains authoritative.
    value=str(filename).replace('\\','/');matches=[name for name in known if value==name or value.endswith('/'+name)]
    assert len(matches)==1,(folder.name,filename);name=matches[0];assert name not in products
    product=Sink(known[name]);products[name]=product;return product
  with py7zr.SevenZipFile(archive,'r',password=password) as reader:
   assert reader.getnames()==[r['name'] for r in expected],folder.name
   for actual,row in zip(reader.files,expected):
    assert actual.is_directory==row['directory'] and actual.uncompressed==row['length'],(folder.name,row)
    assert actual.posix_mode is not None and actual.posix_mode&0o777==row['mode'],(folder.name,row,actual.posix_mode)
    timestamp=(row['mtime_seconds']+11644473600)*10000000+row['mtime_nanos']//100
    assert int(actual.lastwritetime)==timestamp,(folder.name,row,actual.lastwritetime)
   reader.extractall(factory=Factory())
  for name,row in known.items():
   product=products.get(name)
   if product is None:assert row['length']==0;count=0;sha=hashlib.sha256(b'').hexdigest()
   else:count=product.count;sha=product.digest.hexdigest()
   assert count==row['length'] and sha.upper()==row['sha256'],(folder.name,name,count)
   pyrecords.append({'fixture':folder.name,'name':name,'bytes':count,'sha256':sha})
 assert len(unsupported)==4 and len(native)==194 and len(pyrecords)==140
 facts.update(native_successful_archives=len(directories)-len(unsupported),native_exact_payloads=native,unsupported=unsupported,
  py7zr_successful_archives=29,py7zr_metadata_records=28*7,py7zr_exact_payloads=pyrecords,archive_sha256=archives)
 (root/'password-independent-readers.json').write_text(json.dumps(facts,indent=2))
 print('PASS: 52 official archives / 194 exact payloads; 29 py7zr archives / 196 metadata records / 140 exact payloads; four explicit native Zstandard-extension gaps.',flush=True)

if __name__=='__main__':run(*sys.argv[1:])
