"""Guest-only official-reader and independent semantic checks of native RAR publication/restoration."""
from pathlib import Path
import hashlib,json,struct,subprocess,sys,zlib

def verify(root,tool,modules,creations,updates,workflow=None):
 root,tool,modules=map(Path,(root,tool,modules));creations,updates=int(creations),int(updates);sys.path.insert(0,str(modules))
 from Cryptodome.Cipher import AES
 assert workflow in (None,'--workflow','--jobs','--source-case','--moves','--cleanup','--creation-ui','--update-ui','--member-ui')
 password='Public-RAR-Transform-暗号-42829316' if workflow is None else 'Public-7z-worker-暗号-42829324'
 if workflow in ('--creation-ui','--update-ui'):password='  Public creation é e\u0301 🔐  '
 if workflow=='--member-ui':password=' Public existing archive é 🔐 '
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
 documents=sorted(root.glob('format-Rar5-mode-*/*.json' if workflow is not None else '*/*.json'))
 if workflow is None:
  assert len(documents)==creations+updates*3 and sum(p.stem=='created' for p in documents)==creations
  assert {p.stem for p in documents}<={'created','source','updated','restored'}
 elif workflow=='--workflow':assert len(documents)==6 and {p.stem for p in documents}=={'source','rebuilt'}
 elif workflow=='--jobs':
  assert (creations,updates) in ((4,0),(3,3))
  expected={(mode,'created') for mode in range(3)}
  expected|={(mode,'updated') for mode in range(3)} if updates else {(2,'cancelled-after-commit')}
  assert {(int(p.parent.name.rsplit('-',1)[1]),p.stem) for p in documents}==expected and len(documents)==len(expected)
 elif workflow=='--source-case':
  assert creations==updates==0
  expected={(mode,name) for mode in range(3) for name in ('source-leaves','source-parents')}
  assert {(int(p.parent.name.rsplit('-',1)[1]),p.stem) for p in documents}==expected and len(documents)==6
 elif workflow=='--moves':
  assert creations==updates==0
  expected={(mode,name) for mode in range(3) for name in ('moved-created','moved-updated')}
  assert {(int(p.parent.name.rsplit('-',1)[1]),p.stem) for p in documents}==expected and len(documents)==6
 elif workflow=='--cleanup':
  assert creations==updates==0
  expected={(mode,name) for mode in range(3) for name in ('clean-created','clean-updated')}
  assert {(int(p.parent.name.rsplit('-',1)[1]),p.stem) for p in documents}==expected and len(documents)==6
 else:
  assert creations==updates==0
  expected={(mode,'ui-updated' if workflow in ('--update-ui','--member-ui') else 'ui-created') for mode in range(3)}
  assert {(int(p.parent.name.rsplit('-',1)[1]),p.stem) for p in documents}==expected and len(documents)==3
 rows=[];payloads=0;metadata=0
 for document in documents:
  expected=json.loads(document.read_text());assert expected['synthetic_public_vector'] and expected['password']==password
  archive=document.with_suffix('.rar');members,features,header=parse(archive.read_bytes())
  assert features.hex().upper()==expected['features'] and len(members)==len(expected['members'])
  test=subprocess.run([str(tool),'t','-bd','-y','-sccUTF-8','-p'+password,str(archive)],capture_output=True,timeout=120)
  document.with_suffix('.test.log').write_bytes(test.stdout+test.stderr);assert test.returncode==0,(str(archive),test.stderr)
  for index,(actual,wanted) in enumerate(zip(members,expected['members'])):
   assert actual['name']==wanted['name'] and actual['length']==wanted['length'] and actual['metadata'].hex().upper()==wanted['metadata'],(str(archive),index)
   metadata+=1
   if wanted['type']==1:assert actual['length']==0;continue
   extraction=subprocess.run([str(tool),'x','-so','-bd','-y','-ssc','-p'+password,str(archive),'--',actual['name']],capture_output=True,timeout=120)
   (archive.parent/(archive.stem+'-extract-'+str(index)+'.log')).write_bytes(extraction.stderr)
   assert extraction.returncode==0 and len(extraction.stdout)==wanted['length'] and hashlib.sha256(extraction.stdout).hexdigest().upper()==wanted['sha256'],(str(archive),index)
   payloads+=1
  rows.append({'archive':archive.relative_to(root).as_posix(),'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'exact_metadata':True})
 for document in root.glob('*/restored.json'):
  assert document.with_suffix('.rar').read_bytes()==(document.parent/'source.rar').read_bytes()
 if workflow is None:assert metadata==creations*2+updates*5 and payloads==creations+updates*4
 elif workflow=='--workflow':assert metadata==45 and payloads==39
 elif workflow=='--jobs':assert metadata==creations*8+updates*11 and payloads==creations*5+updates*6
 elif workflow=='--source-case':assert metadata==payloads==12
 elif workflow=='--moves':assert metadata==33 and payloads==21
 elif workflow=='--cleanup':assert metadata==payloads==9
 else:assert metadata==payloads==(6 if workflow in ('--update-ui','--member-ui') else 3)
 facts={'creations':creations,'updates_and_restorations':updates,'official_archives':len(documents),'exact_payloads':payloads,
  'independent_metadata_records':metadata,'tool_sha256':hashlib.sha256(tool.read_bytes()).hexdigest(),'rows':rows}
 (root/('independent-native.json' if workflow is None else 'independent-workflow-rar.json')).write_text(json.dumps(facts,indent=2));print(json.dumps({key:value for key,value in facts.items() if key!='rows'}),flush=True)

if __name__=='__main__':verify(*sys.argv[1:])
