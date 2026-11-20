#!/usr/bin/env python3
"""Private, bounded feasibility experiment. No production registration or publication authority.

Original FileCat encoder: fixed canonical tables and a one-candidate greedy LZ matcher. Wire facts were checked
against the existing BSD-2-Clause rardecode port and the public container description; no UnRAR material used.
This is not a clean-room claim. See licenses/rardecode and the accompanying provenance/feasibility record.
"""
from pathlib import Path
import hashlib,json,random,struct,sys,time,zlib

BLOCK=32768
MAX_INPUT=1<<20
MAX_OUTPUT=2<<20
MAIN=[8]*206+[9]*100
DISTANCE=[6]*64
LOW=[4]*16
REPEAT=[5]*20+[6]*24
LENGTHS=MAIN+DISTANCE+LOW+REPEAT
TABLE_LENGTHS=[4]*12+[5]*8

def codes(lengths):
 counts=[0]*16
 for n in lengths:
  assert 0<=n<=15;counts[n]+=1
 counts[0]=0;next_code=[0]*16;value=0
 for n in range(1,16):value=(value+counts[n-1])<<1;next_code[n]=value
 assert next_code[15]+counts[15]==1<<15 # Complete code, independent of decoder padding tolerance.
 result=[]
 for n in lengths:
  result.append((next_code[n],n));next_code[n]+=1
 return result
MAIN_CODES=codes(MAIN);DISTANCE_CODES=codes(DISTANCE);TABLE_CODES=codes(TABLE_LENGTHS)

class Bits:
 def __init__(self):self.data=bytearray();self.cache=0;self.pending=0;self.total=0
 def write(self,value,count):
  assert 0<=count<=32 and 0<=value<1<<count
  self.total+=count
  if self.total>65536*8:raise ValueError('Compressed block exceeds the experiment limit')
  self.cache=(self.cache<<count)|value;self.pending+=count
  while self.pending>=8:
   self.pending-=8;self.data.append((self.cache>>self.pending)&255);self.cache&=(1<<self.pending)-1
 def symbol(self,table,symbol):self.write(*table[symbol])
 def finish(self):
  if self.pending:self.data.append(self.cache<<(8-self.pending))
  return bytes(self.data),((self.total-1)&7)+1

def length_slot(length):
 assert 2<=length<=4097
 if length<=9:return length-2,0,0
 for slot in range(8,44):
  bits=slot//4-1;base=((4|(slot&3))<<bits)+2
  if base<=length<base+(1<<bits):return slot,length-base,bits
 raise AssertionError(length)

def distance_slot(distance):
 assert 1<=distance<=BLOCK
 if distance<=4:return distance-1,0,0
 value=distance-1;bits=value.bit_length()-2;odd=(value>>bits)&1
 return 2*(bits+1)+odd,value-((2|odd)<<bits),bits

def hash3(data,at):return ((data[at]*251+data[at+1])*251+data[at+2])&65535

def block(data,last):
 assert 0<len(data)<=BLOCK
 writer=Bits()
 for n in TABLE_LENGTHS:writer.write(n,4)
 for n in LENGTHS:writer.symbol(TABLE_CODES,n)
 previous=[-1]*65536;position=0;matches=0;comparisons=0
 while position<len(data):
  length=0;distance=0
  if position+2<len(data):
   candidate=previous[hash3(data,position)]
   if candidate>=0:
    distance=position-candidate;maximum=min(4097,len(data)-position)
    while length<maximum:
     comparisons+=1
     if data[candidate+length]!=data[position+length]:break
     length+=1
    adjustment=int(distance>256)+int(distance>8192)
    if length<max(3,2+adjustment):length=0
  count=length or 1
  if length:
   slot,extra,bits=length_slot(length-int(distance>256)-int(distance>8192))
   writer.symbol(MAIN_CODES,262+slot);writer.write(extra,bits)
   slot,extra,bits=distance_slot(distance)
   writer.symbol(DISTANCE_CODES,slot)
   # The fixed four-bit low-distance code is numerically identical to its raw low nibble.
   writer.write(extra,bits);matches+=1
  else:writer.symbol(MAIN_CODES,data[position])
  for at in range(position,min(position+count,len(data)-2)):previous[hash3(data,at)]=at
  position+=count
 payload,used=writer.finish();size=len(payload);size_bytes=max(1,(size.bit_length()+7)//8)
 assert size_bytes<=3 and comparisons<=5*len(data)
 flags=0x80|(0x40 if last else 0)|((size_bytes-1)<<3)|(used-1)
 length_bytes=size.to_bytes(size_bytes,'little');checksum=0x5a^flags
 for value in length_bytes:checksum^=value
 return bytes([flags,checksum])+length_bytes+payload,{'matches':matches,'comparisons':comparisons,'input':len(data),'wire':size+size_bytes+2}

def encode(data):
 if len(data)>MAX_INPUT:raise ValueError('Input exceeds the experiment limit')
 output=bytearray();stats=[]
 for at in range(0,len(data),BLOCK):
  part,counts=block(data[at:at+BLOCK],at+BLOCK>=len(data));output.extend(part);stats.append(counts)
  if len(output)>MAX_OUTPUT:raise ValueError('Output exceeds the experiment limit')
 return bytes(output),stats

def vint(value):
 assert 0<=value<(1<<64);out=bytearray()
 while value>=128:out.append((value&127)|128);value>>=7
 out.append(value);return bytes(out)

def header(kind,body,data_size=None,extra=b''):
 payload=vint(kind)+vint((0 if data_size is None else 2)|(1 if extra else 0))
 if extra:payload+=vint(len(extra))
 if data_size is not None:payload+=vint(data_size)
 payload+=body+extra;size=vint(len(payload));assert len(size)<=3
 return struct.pack('<I',zlib.crc32(size+payload))+size+payload

def archive(name,data,compressed=True):
 name_bytes=name.encode('utf-8');assert 0<len(name_bytes)<=1024 and '/' not in name and '\\' not in name
 packed,stats=encode(data) if compressed else (data,[])
 # Empty members are deliberately stored; every nonempty compressed test is forced through the encoder.
 method=1 if compressed and data else 0
 # One authoritative high-precision time record avoids old readers overwriting its nanoseconds with basic mtime.
 fields=vint(4)+vint(len(data))+vint(0x81a4)+struct.pack('<I',zlib.crc32(data))+vint(method<<7)+vint(1)+vint(len(name_bytes))+name_bytes
 time_record=vint(3)+vint(0x13)+struct.pack('<II',1700000000,123456789)
 extra=vint(len(time_record))+time_record
 output=b'Rar!\x1a\x07\x01\x00'+header(1,vint(0))+header(2,fields,len(packed),extra)+packed+header(5,vint(0))
 assert len(output)<=MAX_OUTPUT
 return output,stats,method

def run(output):
 output.mkdir();random_bytes=random.Random(42829297).randbytes(MAX_INPUT)
 cases={'zeros':bytes(MAX_INPUT),'alphabet':bytes(range(256))*1024,'text':(b'FileCat independent bounded RAR5 compression.\n'*20000)[:MAX_INPUT],
  'noise':random_bytes,'tiny':bytes([0,255,17]),'empty':b'','boundary':random_bytes[:BLOCK-1]+b'Z'*8194+random_bytes[:BLOCK+1],
  'periodic':(bytes(range(251))*4000)[:MAX_INPUT]}
 results=[]
 for case,data in cases.items():
  name='界-'+case+'.bin';start=time.monotonic();encoded,stats,method=archive(name,data)
  (output/(case+'.rar')).write_bytes(encoded);(output/(case+'.expected')).write_bytes(data)
  results.append({'case':case,'name':name,'input':len(data),'archive':len(encoded),'method':method,'sha256':hashlib.sha256(data).hexdigest(),
   'seconds':time.monotonic()-start,'blocks':stats,'mode':0o644,'mtime_seconds':1700000000,'mtime_nanos':123456789})
 assert all(row['archive']<row['input']//2 for row in results if row['case'] in ('zeros','text','alphabet','periodic'))
 (output/'results.json').write_text(json.dumps({'production_backend_qualified':False,'cases':results},indent=2));print(json.dumps(results),flush=True)

if __name__=='__main__':
 if len(sys.argv)!=2:raise SystemExit('Usage: prototype.py FRESH_OWNED_OUTPUT')
 run(Path(sys.argv[1]))
