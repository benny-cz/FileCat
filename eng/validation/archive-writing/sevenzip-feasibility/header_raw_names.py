#!/usr/bin/env python3
"""Bounded independent observation of owned raw/LZMA2-header fixtures; never production source admission."""
from raw_names import Reader
import lzma,struct,zlib

def observe(path):
 data=path.read_bytes();assert 32<=len(data)<=16<<20 and data[:8]==b'7z\xbc\xaf\x27\x1c\0\4'
 assert zlib.crc32(data[12:32])==struct.unpack_from('<I',data,8)[0]
 offset,size,crc=struct.unpack_from('<QQI',data,12);start=32+offset
 assert size<=4<<20 and start+size==len(data) and zlib.crc32(data[start:])==crc
 header=data[start:];main_packed=offset;encoded=False
 if header[0]==0x17:
  encoded=True;r=Reader(header);r.expect(0x17);r.expect(6);position=r.number();r.expect(1);r.expect(9);packed=r.number()
  r.expect(10);r.expect(1);packed_crc=struct.unpack('<I',r.take(4))[0];r.expect(0)
  r.expect(7);r.expect(11);r.expect(1);r.expect(0);r.expect(1);r.expect(0x21);r.expect(0x21);r.expect(1);prop=r.byte()
  assert prop<=24;dictionary=(2|(prop&1))<<((prop>>1)+11)
  r.expect(12);unpacked=r.number();assert 0<unpacked<=4<<20
  r.expect(10);r.expect(1);raw_crc=struct.unpack('<I',r.take(4))[0];r.expect(0);r.expect(8);r.expect(0);r.expect(0)
  assert r.at==len(r.data) and 32+position+packed==start
  compressed=data[32+position:start];assert zlib.crc32(compressed)==packed_crc
  decoder=lzma.LZMADecompressor(format=lzma.FORMAT_RAW,filters=[{'id':lzma.FILTER_LZMA2,'dict_size':dictionary}])
  header=decoder.decompress(compressed,max_length=(4<<20)+1)
  assert decoder.eof and not decoder.unused_data and len(header)==unpacked and zlib.crc32(header)==raw_crc
  main_packed=position
 r=Reader(header);r.expect(1);kind=r.byte();graphs=[]
 if kind==4:
  r.expect(6);pack_pos=r.number();count=r.number();assert pack_pos==0 and count<=100
  r.expect(9);sizes=[r.number() for _ in range(count)];assert sum(sizes)==main_packed
  r.expect(10);r.digests(count);r.expect(0);r.expect(7);r.expect(11);folders=r.number();assert folders==count;r.expect(0);outputs=0
  for _ in range(folders):
   coders=r.number();assert 1<=coders<=8;inputs=0;outs=0;graph=[]
   for _ in range(coders):
    flags=r.byte();assert flags&0xc0==0 and flags&15
    method=r.take(flags&15);ins,out=(r.number(),r.number()) if flags&16 else (1,1)
    assert 1<=ins<=8 and 1<=out<=8
    props=r.take(r.number()) if flags&32 else b'';assert len(props)<=64
    graph.append({'method_id':method.hex(),'properties':props.hex(),'inputs':ins,'outputs':out});inputs+=ins;outs+=out
   assert 0<=outs-1<=inputs
   for _ in range(outs-1):r.number();r.number()
   packed=inputs-outs+1;assert 1<=packed<=8
   if packed>1:
    for _ in range(packed):r.number()
   graphs.append(graph);outputs+=outs
  r.expect(12)
  for _ in range(outputs):r.number()
  r.expect(10);r.digests(folders);r.expect(0);r.expect(8);r.expect(0);r.expect(0);kind=r.byte()
 assert kind==5;files=r.number();assert files<=2048;names=None
 while True:
  kind=r.byte()
  if kind==0:break
  prop=r.take(r.number())
  if kind==17:
   assert names is None and prop[0]==0 and prop[-2:]==b'\0\0'
   names=prop[1:].decode('utf-16-le').split('\0')[:-1];assert len(names)==files
 r.expect(0);assert r.at==len(r.data) and names is not None
 return {'names':names,'graphs':graphs,'lzma2_header':encoded,'decoded_header_bytes':len(header),'fixture_only_observer':True}
