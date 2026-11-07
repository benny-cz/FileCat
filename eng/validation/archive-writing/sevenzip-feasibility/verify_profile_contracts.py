"""Independent fixed public-options, request, manifest-digest/sidecar and provenance encoding vectors. Guest-only."""
from pathlib import Path
import hashlib,json,struct,sys

def run(root):
 root=Path(root);folders=sorted(p.parent for p in root.glob('*/profile.bin'));assert len(folders)==60
 fields=['format','version','level','dictionary','codec','filter','modelOrder','modelMemory','deltaDistance','header','encryption','countPower','algorithm']
 nonce=bytes.fromhex('23af5afc440749e2a838428293110001');rows=[]
 def integer(n):return struct.pack('<i',n)
 def blob(b):return integer(len(b))+b
 for folder in folders:
  index=int(folder.name)
  if index<42:
   mode=index//14;codec=(index%14)//2;header=index%2
   values=[5,2,0,8388608,codec,0,0,0,1,header,mode,19 if mode else 0,0]
  else:
   mode=(index-42)//6;method=(index-42)%6;values=[9,2,method,131072,0,0,0,0,0,0,mode,19 if mode else 0,0]
  profile=struct.pack('<13i',*values);assert (folder/'profile.bin').read_bytes()==profile
  actual=json.loads((folder/'profile.json').read_text());assert actual==dict(zip(fields,values))
  request=struct.pack('<H',1)+profile+struct.pack('<q',1)+hashlib.sha256(b'').digest()
  assert (folder/'request.bin').read_bytes()==request
  name='é.bin'.encode();payload=hashlib.sha256(bytes([1,2,3])).digest();metadata=bytes([1,2]);features=b'\x01'
  digests=[]
  for domain in range(3):
   data=b'FCAM'+integer(1)+integer(domain)+profile+integer(1)
   if domain!=1:data+=blob(features)
   data+=blob(name)+integer(0)+integer(0)
   if domain!=2:data+=struct.pack('<q',3)+blob(payload)
   if domain!=1:data+=blob(metadata)
   digests.append(hashlib.sha256(data).digest())
  assert json.loads((folder/'digests.json').read_text())==dict(zip(['Digest','ContentDigest','MetadataDigest'],[x.hex().upper() for x in digests]))
  member=struct.pack('<H',1)+blob(name)+integer(0)+integer(0)+struct.pack('<q',3)+payload+blob(metadata)
  sidecar=b'FCAMSC01'+struct.pack('<HBB',1,2,0)+nonce+profile[:16]+integer(1)+struct.pack('<q',3)+integer(1)+profile[16:]+features+b''.join(digests)+blob(member)+b'FCME'
  assert (folder/'manifest.bin').read_bytes()==sidecar
  origin=b'FCOEVID1'+struct.pack('<HBB',1,0,0)+nonce+profile[:16]+integer(0)+profile[16:]+b'FCOE'
  assert (folder/'origin.bin').read_bytes()==origin
  rows.append({'case':index,'profile_sha256':hashlib.sha256(profile).hexdigest(),'request_sha256':hashlib.sha256(request).hexdigest(),
   'manifest_sha256':hashlib.sha256(sidecar).hexdigest(),'origin_sha256':hashlib.sha256(origin).hexdigest(),'manifest_digests':[d.hex() for d in digests]})
 (root/'independent-profile-contracts.json').write_text(json.dumps({'profiles':len(rows),'rows':rows,'public_options_only':True},indent=2))
 print('PASS: 60 independently encoded profiles, requests, manifests/all three digests and origin sidecars.',flush=True)

if __name__=='__main__':run(sys.argv[1])
