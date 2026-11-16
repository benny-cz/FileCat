#!/usr/bin/env python3
"""Independent bounded fixture check: py7zr headers/AES/codecs, SDK C BCJ2 merge. No FileCat decoding."""
from pathlib import Path
import hashlib,io,json,struct,subprocess,sys,zlib

def run(root,oracle,modules,output):
 root=Path(root).resolve();oracle=Path(oracle).resolve();out=Path(output).resolve();out.mkdir()
 sys.path.insert(0,str(Path(modules).resolve()))
 import py7zr
 from py7zr.compressor import AESDecompressor,SevenZipDecompressor
 methods=[b'\0',b'\x03\x01\x01',b'\x21',b'\x04\x02\x02',b'\x04\x01\x08',b'\x04\xf7\x11\x01',b'\x03\x04\x01']
 folders=sorted(root.glob('archive-*'));assert len(folders)==42;verified=[]
 for fixture in folders:
  profile=json.loads((fixture/'profile.json').read_text());rows=json.loads((fixture/'members.json').read_text())
  assert profile['synthetic_public_vector'] and profile['public_synthetic_password']=='FileCat-Public-Bcj2-Vector-暗号-42829304'
  password=profile['public_synthetic_password'] if profile['encrypted'] else None
  archive=fixture/'members.7z';assert archive.stat().st_size<4<<20
  with py7zr.SevenZipFile(archive,'r',password=password) as reader:
   streams=reader.header.main_streams;assert len(streams.unpackinfo.folders)==1
   folder=streams.unpackinfo.folders[0];pack=streams.packinfo
   encrypted=profile['encrypted'];chains=[[0,1],[2,3],[4,5],[6]] if encrypted else [[0],[1],[2],[]]
   bcj2=7 if encrypted else 3
   expected_bindings=[(1,0),(3,2),(5,4),(7,1),(8,3),(9,5),(10,6)] if encrypted else [(3,0),(4,1),(5,2)]
   assert len(folder.coders)==bcj2+1 and len(folder.unpacksizes)==bcj2+1
   assert [(b.incoder,b.outcoder) for b in folder.bindpairs]==expected_bindings
   assert folder.packed_indices==([0,2,4,6] if encrypted else [0,1,2,6])
   last=folder.coders[-1];assert last['method']==b'\x03\x03\x01\x1b' and last['numinstreams']==4 and last['numoutstreams']==1
   assert pack.numstreams==4 and len(pack.packsizes)==4 and pack.packpos==0
   assert all(pack.digestdefined) and len(pack.crcs)==4
   iv_properties=[];parts=[];offset=reader.afterheader+pack.packpos
   for branch,chain in enumerate(chains):
    count=pack.packsizes[branch];assert 0<count<4<<20
    reader.fp.seek(offset);packed=reader.fp.read(count);offset+=count
    assert len(packed)==count and zlib.crc32(packed)==pack.crcs[branch]
    coders=[folder.coders[c] for c in chain];sizes=[folder.unpacksizes[c] for c in chain]
    assert all(c['numinstreams']==c['numoutstreams']==1 for c in coders)
    if encrypted:
     assert coders[0]['method']==b'\x06\xf1\x07\x01';iv_properties.append(coders[0]['properties'])
    if branch<3:assert coders[-1]['method']==methods[profile['codec']]
    if encrypted:
     # AES returns complete blocks. The folder declares the exact plaintext extent; validate its zero padding
     # before passing that extent to the next codec, including AES-only range and AES+Copy branches.
     aes=AESDecompressor(coders[0]['properties'],password,65536);plain=bytearray()
     for start in range(0,len(packed),65536):plain.extend(aes.decompress(packed[start:start+65536]))
     plain.extend(aes.decompress(b''))
     assert len(plain)==len(packed) and 0<=len(plain)-sizes[0]<=15 and not any(plain[sizes[0]:]),(fixture.name,branch,'padding')
     packed=bytes(plain[:sizes[0]]);coders=coders[1:];sizes=sizes[1:];count=len(packed)
    if not coders:decoded=packed
    else:
     assert all(0<size<1<<20 for size in sizes)
     decoder=SevenZipDecompressor(coders,count,sizes,None,password,65536);source=io.BytesIO(packed);decoded=bytearray()
     for attempt in range(128):
      # PPMd7 streams use the folder's declared symbol count; requesting an extra symbol is not an EOF check.
      block=decoder.decompress(source,min(65536,sizes[-1]-len(decoded)));decoded.extend(block)
      assert len(decoded)<=sizes[-1],(fixture.name,branch,len(decoded),sizes[-1])
      if len(decoded)==sizes[-1]:break
     assert len(decoded)==sizes[-1] and decoder.consumed==count
    parts.append(bytes(decoded))
   if encrypted:assert len(set(iv_properties))==4
   payload=next(row for row in rows if row['length']);assert folder.unpacksizes[-1]==payload['length']
   assert sum(map(len,parts[:3]))==payload['length']
   packed_path=out/(fixture.name+'.streams');decoded_path=out/(fixture.name+'.decoded')
   packed_path.write_bytes(struct.pack('<4I',*(len(p) for p in parts))+b''.join(parts))
   subprocess.run([str(oracle),str(packed_path),str(decoded_path),str(payload['length'])],check=True,timeout=60)
   data=decoded_path.read_bytes();assert len(data)==payload['length'] and hashlib.sha256(data).hexdigest().upper()==payload['sha256']
   verified.append({'fixture':fixture.name,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'branch_bytes':list(map(len,parts)),
    'encrypted':encrypted,'headers_encrypted':profile['hidden_names'],'coder_methods':[c['method'].hex() for c in folder.coders]})
 assert len(verified)==42 and sum('archive-5-' in row['fixture'] for row in verified)==6
 result={'verified':verified,'oracle_sha256':hashlib.sha256(oracle.read_bytes()).hexdigest(),'py7zr_version':py7zr.__version__,
  'production_dependency':False,'public_synthetic_vectors_only':True,'implementation':'Independent py7zr parsing/AES/linear codecs plus pinned SDK C BCJ2 decoder; no full py7zr BCJ2 graph support claimed.'}
 (out/'graph-bcj2.json').write_text(json.dumps(result,indent=2));print('PASS: 42 independently decoded BCJ2 graphs, including all six Zstandard fixtures.',flush=True)

if __name__=='__main__':run(*sys.argv[1:])
