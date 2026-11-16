#!/usr/bin/env python3
"""Independent name/graph observation for bounded owned raw-header fixtures, not production source admission."""
import struct,zlib

class Reader:
 def __init__(self,data):self.data=data;self.at=0
 def take(self,count):
  if count<0 or count>len(self.data)-self.at:raise ValueError('Truncated owned fixture header')
  part=self.data[self.at:self.at+count];self.at+=count;return part
 def byte(self):return self.take(1)[0]
 def number(self):
  first=self.byte();mask=128;value=0
  for index in range(8):
   if first&mask==0:return value|((first&(mask-1))<<(8*index))
   value|=self.byte()<<(8*index);mask>>=1
  return value
 def expect(self,value):
  got=self.byte()
  if got!=value:raise ValueError(('Unexpected owned fixture header field',value,got,self.at))
 def digests(self,count):
  all_defined=self.byte();assert all_defined in (0,1)
  mask=None if all_defined else self.take((count+7)//8)
  for index in range(count):
   if all_defined or mask[index//8]&(128>>(index&7)):self.take(4)

def observe(path):
 data=path.read_bytes();assert 32<=len(data)<=16<<20 and data[:8]==b'7z\xbc\xaf\x27\x1c\0\4'
 assert zlib.crc32(data[12:32])==struct.unpack_from('<I',data,8)[0]
 offset,size,crc=struct.unpack_from('<QQI',data,12);start=32+offset
 assert size<=1<<20 and start+size==len(data) and zlib.crc32(data[start:])==crc
 r=Reader(data[start:]);r.expect(1);kind=r.byte();graphs=[]
 if kind==4:
  r.expect(6);pack_pos=r.number();count=r.number();assert pack_pos==0 and count<=100
  r.expect(9);sizes=[r.number() for _ in range(count)];assert sum(sizes)==offset
  r.expect(10);r.digests(count);r.expect(0)
  r.expect(7);r.expect(11);folders=r.number();assert folders==count;r.expect(0);outputs=0
  for _ in range(folders):
   coders=r.number();assert 1<=coders<=8;inputs=0;outs=0;graph=[]
   for _ in range(coders):
    flags=r.byte();assert flags&0xc0==0 and flags&15
    method=r.take(flags&15)
    ins,out=(r.number(),r.number()) if flags&16 else (1,1)
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
 assert kind==5;files=r.number();assert files<=100;names=None
 while True:
  kind=r.byte()
  if kind==0:break
  prop=r.take(r.number())
  if kind==17:
   assert names is None and prop[0]==0 and prop[-2:]==b'\0\0'
   names=prop[1:].decode('utf-16-le').split('\0')[:-1];assert len(names)==files
 r.expect(0);assert r.at==len(r.data) and names is not None
 return {'names':names,'graphs':graphs,'fixture_only_observer':True}
