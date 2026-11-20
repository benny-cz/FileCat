#!/usr/bin/env python3
"""Compare owned managed vectors with independently compiled pinned public-domain SDK output."""
from pathlib import Path
import hashlib,json,subprocess,sys

root=Path(sys.argv[1]);oracle=Path(sys.argv[2]);verified=[]
inputs=sorted(root.glob('*.input'));assert len(inputs)==19
for path in inputs:
 parts=path.stem.split('-');output=path.with_suffix('.sdk')
 assert not output.exists() and path.stat().st_size<=4<<20
 args=([str(oracle),'ppmd','model',str(path),str(output),parts[1],parts[2]] if parts[0]=='Ppmd7' else
       [str(oracle),'filter',parts[0],str(path),str(output),parts[1],'0'])
 subprocess.run(args,check=True,timeout=120)
 expected=path.with_suffix('.encoded').read_bytes();actual=output.read_bytes()
 assert actual==expected,(path.name,len(actual),len(expected),next((i for i,(a,b) in enumerate(zip(actual,expected)) if a!=b),None))
 verified.append({'name':path.stem,'input_bytes':path.stat().st_size,'encoded_bytes':len(actual),'sha256':hashlib.sha256(actual).hexdigest()})
(root/'sdk-reader.json').write_text(json.dumps({'sdk_commit':'b88c6bc09339de32f7d18bbab900686b244f42ba',
 'oracle_sha256':hashlib.sha256(oracle.read_bytes()).hexdigest(),'verified':verified,'production_dependency':False},indent=2))
print('PASS: 19 exact independent SDK vectors, including bounded PPMd models and all filter directions.')
