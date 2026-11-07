"""Guest-only independent metadata and official-reader checks of complete RAR transform outputs."""
from pathlib import Path
import hashlib,json,struct,subprocess,sys,zlib

def verify(root,tool,modules):
 root,tool,modules=map(Path,(root,tool,modules));sys.path.insert(0,str(modules))
 from Cryptodome.Cipher import AES
 folders=sorted(p.parent for p in root.glob('*/rebuilt.json'));assert len(folders)==18
 rows=[];password='Public-RAR-Transform-暗号-42829316'
 def vint(data,pos):
  value=0
  for shift in range(0,70,7):
   assert pos<len(data);byte=data[pos];pos+=1;assert shift<63 or byte<2;value|=(byte&127)<<shift
   if byte<128:return value,pos
  raise AssertionError('Oversized integer')
 class Reader:
  def __init__(self,data):self.data=data;self.pos=0
  def take(self,n):assert 0<=n<=len(self.data)-self.pos;v=self.data[self.pos:self.pos+n];self.pos+=n;return v
  def number(self):v,self.pos=vint(self.data,self.pos);return v
  def u32(self):return struct.unpack('<I',self.take(4))[0]
  def u64(self):return struct.unpack('<Q',self.take(8))[0]
  def end(self):assert self.pos==len(self.data)
 def parse(data):
  assert data[:8]==b'Rar!\x1a\x07\x01\x00';pos=8;key=None;header_info=None;members=[];main=None;end=None;enc_flags=0
  while pos<len(data):
   if key is None:
    size,body=vint(data,pos+4);total=body-pos+size;header=data[pos:pos+total];finish=pos+total
   else:
    iv=data[pos:pos+16];first=AES.new(key,AES.MODE_CBC,iv).decrypt(data[pos+16:pos+32]);size,body=vint(first,4)
    total=body+size;packed=(total+15)&~15;header=AES.new(key,AES.MODE_CBC,iv).decrypt(data[pos+16:pos+16+packed])[:total];finish=pos+16+packed
   assert len(header)==total and 2<=size<=1<<20
   assert zlib.crc32(header[4:])==struct.unpack('<I',header[:4])[0]
   size,start=vint(header,4);h=Reader(header[start:]);kind=h.number();flags=h.number();assert flags&~7==0
   extra=h.number() if flags&1 else 0;packed=h.number() if flags&2 else 0
   assert extra<=len(h.data)-h.pos and packed<=len(data)-finish
   body=Reader(h.take(len(h.data)-h.pos-extra));extras=Reader(h.take(extra));h.end()
   if kind==4:
    assert key is None and main is None and packed==0 and extra==0;assert body.number()==0
    crypt=body.number();assert crypt in (0,1);power=body.take(1)[0];salt=body.take(16)
    if crypt:body.take(12)
    body.end();key=hashlib.pbkdf2_hmac('sha256',password.encode(),salt,1<<power,32);header_info=(crypt,power,salt);enc_flags=flags
   elif kind==1:assert main is None and body.number()==0;body.end();main=flags
   elif kind==5:assert body.number()==0 and packed==0;body.end();end=flags;assert finish==len(data)
   elif kind==2:
    fileflags=body.number();assert fileflags&~7==0;length=body.number();attrs=body.number()
    basic=body.u32() if fileflags&2 else None;crc=body.u32() if fileflags&4 else None
    compression=body.number();host=body.number();name=body.take(body.number()).decode('utf-8');body.end()
    times=None;owner=None;crypto=None;raw_crypto=None;blake=None
    while extras.pos<len(extras.data):
     r=Reader(extras.take(extras.number()));tag=r.number()
     if tag==1:
      raw_crypto=r.data[r.pos:];assert r.number()==0;c=r.number();power=r.take(1)[0];r.take(16);r.take(16)
      if c&1:r.take(12)
      crypto=(c,power)
     elif tag==2:assert r.number()==0;blake=r.take(32)
     elif tag==3:
      f=r.number();values=[]
      for bit in (2,4,8):
       if f&bit:
        value=r.u32() if f&1 else r.u64();values.append((value,0) if f&1 else (value//10000000-11644473600,value%10000000*100))
       else:values.append(None)
      if f&16:
       for i,value in enumerate(values):
        if value is not None:values[i]=(value[0],r.u32())
      times=(f,values)
     elif tag==6:
      f=r.number();user=r.take(r.number()) if f&1 else None;group=r.take(r.number()) if f&2 else None
      uid=r.number() if f&4 else None;gid=r.number() if f&8 else None;owner=(f,user,group,uid,gid)
     else:raise AssertionError(('Unknown extra',tag))
     r.end()
    extras.end()
    # Core's independently specified semantic metadata, with compressed-filter observations zero for these new encodings.
    meta=struct.pack('<HiQIiQi',1,1 if fileflags&1 else 0,flags,attrs,host,compression,0)+bytes([basic is not None])
    if basic is not None:meta+=struct.pack('<I',basic)
    meta+=bytes([crc is not None,blake is not None,times is not None])
    if times:
     meta+=struct.pack('<Q',times[0])
     for value in times[1]:meta+=bytes([value is not None])+(struct.pack('<qi',*value) if value is not None else b'')
    meta+=bytes([owner is not None])
    if owner:
     f,user,group,uid,gid=owner;meta+=struct.pack('<Q',f)
     for value in (user,group):
      if value is not None:meta+=struct.pack('<i',len(value))+value
     for value in (uid,gid):
      if value is not None:meta+=struct.pack('<Q',value)
    meta+=bytes([crypto is not None])+(struct.pack('<ii',*crypto) if crypto is not None else b'')
    members.append({'name':name,'length':length,'metadata':meta,'crc':crc,'blake':blake,'crypto':raw_crypto,
     'packed_sha256':hashlib.sha256(data[finish:finish+packed]).hexdigest()})
   else:raise AssertionError(('Unknown header',kind))
   pos=finish+packed
  assert main is not None and end is not None
  feature=bytes([1,0,main,end,enc_flags,header_info is not None,header_info[0] if header_info else 0,header_info[1] if header_info else 0,0,0])
  return members,feature,header_info
 tool_info=subprocess.run([str(tool),'i'],capture_output=True,timeout=60,check=True);(root/'tool-info.txt').write_bytes(tool_info.stdout+tool_info.stderr)
 payloads=0;metadata=0
 for folder in folders:
  parsed=[]
  for which in ('source','rebuilt'):
   expected=json.loads((folder/(which+'.json')).read_text());assert expected['synthetic_public_vector'] and expected['password']==password
   archive=folder/(which+'.rar');members,features,header=parse(archive.read_bytes());parsed.append(members)
   assert features.hex().upper()==expected['features'] and len(members)==len(expected['members'])
   test=subprocess.run([str(tool),'t','-bd','-y','-sccUTF-8','-p'+password,str(archive)],capture_output=True,timeout=120)
   (folder/(which+'-test.log')).write_bytes(test.stdout+test.stderr);assert test.returncode==0,(folder.name,which,test.stderr)
   for index,(actual,wanted) in enumerate(zip(members,expected['members'])):
    assert actual['name']==wanted['name'] and actual['length']==wanted['length'] and actual['metadata'].hex().upper()==wanted['metadata'],(folder.name,which,index)
    metadata+=1
    if wanted['type']==1:assert actual['length']==0;continue
    extraction=subprocess.run([str(tool),'x','-so','-bd','-y','-p'+password,str(archive),'--',actual['name']],capture_output=True,timeout=120)
    (folder/(which+'-extract-'+str(index)+'.log')).write_bytes(extraction.stderr)
    assert extraction.returncode==0 and len(extraction.stdout)==wanted['length'] and hashlib.sha256(extraction.stdout).hexdigest().upper()==wanted['sha256'],(folder.name,which,index)
    payloads+=1
  before,after=parsed[0][0],parsed[1][0]
  for field in ('packed_sha256','crc','blake','crypto'):assert before[field]==after[field],(folder.name,field)
  rows.append({'case':folder.name,'retained_packed_sha256':before['packed_sha256'],'exact_metadata':True,
   'source_sha256':hashlib.sha256((folder/'source.rar').read_bytes()).hexdigest(),'rebuilt_sha256':hashlib.sha256((folder/'rebuilt.rar').read_bytes()).hexdigest()})
 facts={'cases':len(rows),'official_archives':len(rows)*2,'exact_payloads':payloads,'independent_metadata_records':metadata,
  'exact_retained_packed_streams':len(rows),'tool_sha256':hashlib.sha256(tool.read_bytes()).hexdigest(),'rows':rows}
 assert payloads==108 and metadata==162
 (root/'independent-transform.json').write_text(json.dumps(facts,indent=2));print(json.dumps({key:value for key,value in facts.items() if key!='rows'}),flush=True)

if __name__=='__main__':verify(*sys.argv[1:])
