#!/usr/bin/env python3
"""Guest-only independent py7zr metadata/graph and exact payload checks of public framing observations."""
from pathlib import Path
import hashlib,json,sys

def run(root,modules):
 root=Path(root);sys.path.insert(0,str(Path(modules)))
 import py7zr
 from py7zr.io import Py7zIO,WriterFactory
 verified=[];fixtures=sorted(root.glob('codec-*-header-*-protection-*'));assert len(fixtures)==42
 for fixture in fixtures:
  facts=json.loads((fixture/'observed.json').read_text(encoding='utf-8'));assert facts['synthetic_public_vector']
  assert facts['public_synthetic_password']=='Public-7z-framing-暗号-42829308'
  archive=fixture/'members.7z';assert archive.stat().st_size<4<<20
  assert hashlib.sha256(archive.read_bytes()).hexdigest().upper()==facts['archive_sha256']
  known={row['name']:row for row in facts['members'] if not row['directory']};products={}
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
    name=str(filename).replace('\\','/');matches=[key for key in known if name==key or name.endswith('/'+key)]
    assert len(matches)==1,(fixture.name,filename);key=matches[0];assert key not in products
    product=Sink(known[key]);products[key]=product;return product
  password=facts['public_synthetic_password'] if facts['encrypted'] else None
  with py7zr.SevenZipFile(archive,'r',password=password) as reader:
   assert reader.getnames()==[row['name'] for row in facts['members']]
   for actual,raw,row in zip(reader.files,reader.header.files_info.files,facts['members']):
    assert actual.is_directory==row['directory'] and actual.uncompressed==row['length']
    assert raw.get('attributes')==row['attributes']
    for key,column in [('creationtime','created'),('lastaccesstime','accessed'),('lastwritetime','modified')]:
     value=raw.get(key);assert (None if value is None else int(value))==row[column],(fixture.name,row['name'],key)
   streams=reader.header.main_streams;assert len(streams.unpackinfo.folders)==len(facts['folders'])==1
   for actual,row in zip(streams.unpackinfo.folders,facts['folders']):
    assert actual.unpacksizes==row['unpacked_sizes'] and actual.packed_indices==row['packed_inputs'] and actual.crc==row['crc32']
    assert [(x.incoder,x.outcoder) for x in actual.bindpairs]==[(x['Input'],x['Output']) for x in row['bindings']]
    assert len(actual.coders)==len(row['coders'])
    for coder,expected in zip(actual.coders,row['coders']):
     assert coder['method'].hex().upper()==expected['method'] and (coder.get('properties') or b'').hex().upper()==expected['properties']
     assert coder['numinstreams']==expected['inputs'] and coder['numoutstreams']==expected['outputs']
   pack=streams.packinfo;assert pack.packsizes==[row['Length'] for row in facts['packs']]
   assert all(pack.digestdefined) and pack.crcs==[row['Crc32'] for row in facts['packs']]
   offset=reader.afterheader+pack.packpos
   for row in facts['packs']:assert row['Offset']==offset;offset+=row['Length']
   reader.extractall(factory=Factory())
  for name,row in known.items():
   product=products.get(name)
   if product is None:assert row['length']==0;count=0;digest=hashlib.sha256(b'').hexdigest()
   else:count=product.count;digest=product.digest.hexdigest()
   assert count==row['length'] and digest.upper()==row['sha256'],(fixture.name,name)
  verified.append({'fixture':fixture.name,'metadata_records':len(facts['members']),'exact_payloads':len(known),'archive_sha256':facts['archive_sha256']})
 result={'py7zr_version':py7zr.__version__,'synthetic_public_vectors_only':True,'production_dependency':False,
  'archives':len(verified),'metadata_records':sum(x['metadata_records'] for x in verified),'exact_payloads':sum(x['exact_payloads'] for x in verified),'verified':verified}
 (root/'independent-framing.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
 print('PASS: 42 independent header/graph observations, 126 exact metadata records and 84 exact file payloads.',flush=True)

if __name__=='__main__':run(*sys.argv[1:])
