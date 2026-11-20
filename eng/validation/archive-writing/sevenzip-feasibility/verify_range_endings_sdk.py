"""Guest-only exact PPMd7 and four-branch BCJ2 decisions compared with the pinned public-domain SDK."""
from pathlib import Path
import hashlib,json,subprocess,sys

vectors,ppmd,bcj2,out=map(Path,sys.argv[1:]);out.mkdir();rows=[];failures=[];counts={}
for kind,tool in [('ppmd',ppmd),('bcj2',bcj2)]:
 counts[kind]={'accepted':0,'refused':0}
 for directory in sorted((vectors/kind).iterdir()):
  expected=json.loads((directory/'expected.json').read_text());assert expected['synthetic_public_vector'];decoded=out/(kind+'-'+directory.name+'.bin')
  args=[str(tool),str(directory/'packet.bin'),str(decoded)]+([str(expected['length'])] if kind=='bcj2' else [])
  result=subprocess.run(args,capture_output=True,text=True,timeout=30)
  if kind=='ppmd':
   assert result.returncode==0,(directory.name,result.returncode,result.stdout,result.stderr)
   actual=json.loads(result.stdout);accepted=actual['accepted']
  else:
   assert result.returncode in (0,1),(directory.name,result.returncode,result.stdout,result.stderr)
   accepted=result.returncode==0;actual={'accepted':accepted,'exit_code':result.returncode}
  match=accepted==expected['accepted'];digest=hashlib.sha256(decoded.read_bytes()).hexdigest() if decoded.exists() else None
  if expected['accepted']:match=match and decoded.stat().st_size==expected['length'] and digest.upper()==expected['sha256']
  counts[kind]['accepted' if accepted else 'refused']+=1
  rows.append({'kind':kind,'case':directory.name,'expected':expected,'sdk':actual,'matches':match,
   'packet_sha256':hashlib.sha256((directory/'packet.bin').read_bytes()).hexdigest(),'decoded_sha256':digest})
  if not match:failures.append(kind+'/'+directory.name)
facts={'ppmd_oracle_sha256':hashlib.sha256(ppmd.read_bytes()).hexdigest(),'bcj2_oracle_sha256':hashlib.sha256(bcj2.read_bytes()).hexdigest(),
 'counts':counts,'rows':rows,'failures':failures}
(out/'comparison.json').write_text(json.dumps(facts,indent=2));print(json.dumps({'vectors':len(rows),'counts':counts,'failures':failures}),flush=True)
assert counts['ppmd']['accepted']==56 and counts['ppmd']['refused']>=7 and counts['bcj2']=={'accepted':18,'refused':9},counts
assert not failures,failures
