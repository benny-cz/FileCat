"""Compare every exported exact-extent accept/refuse decision with SDK LZMA_FINISH_END and exact byte counts."""
from pathlib import Path
import hashlib,json,subprocess,sys

def run(vectors,oracle,out):
 vectors,oracle,out=Path(vectors),Path(oracle),Path(out);out.mkdir()
 rows=[];failures=[]
 for directory in sorted(vectors.iterdir()):
  expected=json.loads((directory/'expected.json').read_text());decoded=out/(directory.name+'.bin')
  result=subprocess.run([str(oracle),str(directory/'vector.bin'),str(decoded)],capture_output=True,text=True,timeout=15)
  assert result.returncode==0,(directory.name,result.returncode,result.stdout,result.stderr)
  actual=json.loads(result.stdout);good=actual['accepted']==expected['accepted']
  if expected['accepted']:
   good=good and hashlib.sha256(decoded.read_bytes()).hexdigest()==expected['sha256'].lower()
   good=good and actual['status']==(1 if expected['mode'] in (1,2) else 4)
  rows.append({'case':directory.name,'expected':expected,'sdk':actual,'matches':good,
   'vector_sha256':hashlib.sha256((directory/'vector.bin').read_bytes()).hexdigest(),'decoded_sha256':hashlib.sha256(decoded.read_bytes()).hexdigest()})
  if not good:failures.append(directory.name)
 assert rows,'No vectors exported'
 (out/'comparison.json').write_text(json.dumps({'oracle_sha256':hashlib.sha256(oracle.read_bytes()).hexdigest(),'rows':rows,'failures':failures},indent=2))
 print(json.dumps({'vectors':len(rows),'accepted':sum(row['expected']['accepted'] for row in rows),'failures':failures}),flush=True)
 assert not failures,failures

if __name__=='__main__':run(*sys.argv[1:])
