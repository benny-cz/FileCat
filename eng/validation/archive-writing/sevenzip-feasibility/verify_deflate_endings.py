"""Guest-only independent native zlib checks of exact raw-deflate accept/refuse vectors and preset history."""
from pathlib import Path
import hashlib,json,platform,sys,zlib

root=Path(sys.argv[1]);rows=[];failures=[]
for directory in sorted(p.parent for p in root.glob('*/expected.json')):
 expected=json.loads((directory/'expected.json').read_text());packed=(directory/'compressed.bin').read_bytes();assert len(packed)<4<<20 and 0<=expected['length']<=2<<20
 reader=zlib.decompressobj(-15);output=b'';error=None
 try:
  output=reader.decompress(packed,expected['length']+1)
  accepted=reader.eof and not reader.unused_data and not reader.unconsumed_tail and len(output)==expected['length']
 except zlib.error as ex:accepted=False;error=str(ex)
 match=accepted==expected['accepted']
 if accepted:match=match and hashlib.sha256(output).hexdigest().upper()==expected['sha256']
 rows.append({'case':directory.name,'expected':expected,'native_accepted':accepted,'matches':match,'error':error,
  'compressed_sha256':hashlib.sha256(packed).hexdigest(),'decoded_sha256':hashlib.sha256(output).hexdigest() if accepted else None})
 if not match:failures.append(directory.name)
preset=root/'preset';dictionary=(preset/'dictionary.bin').read_bytes();packed=(preset/'compressed.bin').read_bytes();expected=(preset/'plain.bin').read_bytes()
reader=zlib.decompressobj(15,zdict=dictionary);output=reader.decompress(packed,len(expected)+1)
assert output==expected and reader.eof and not reader.unused_data and not reader.unconsumed_tail
facts={'python':sys.version,'platform':platform.platform(),'zlib_compiled_version':zlib.ZLIB_VERSION,'zlib_runtime_version':zlib.ZLIB_RUNTIME_VERSION,
 'vectors':len(rows),'accepted':sum(row['native_accepted'] for row in rows),'refused':sum(not row['native_accepted'] for row in rows),
 'preset_dictionary_verified':True,'preset_sha256':hashlib.sha256(packed).hexdigest(),'rows':rows,'failures':failures}
(root/'independent-deflate.json').write_text(json.dumps(facts,indent=2));print(json.dumps({key:value for key,value in facts.items() if key!='rows'}),flush=True)
assert facts['accepted']==55 and not failures,failures
