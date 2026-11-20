"""Public test vectors only; independent pinned official reader and private AES implementation."""
from pathlib import Path
import hashlib,json,subprocess,sys

out,tool,modules=map(Path,sys.argv[1:]);sys.path.insert(0,str(modules))
from Cryptodome.Cipher import AES
fixtures=out/'logical-fixtures';rows=[];unsupported=[]
folders=sorted(fixtures.glob('codec-*-header-*-protection-*'));assert len(folders)==42
for folder in folders:
 facts=json.loads((folder/'observed.json').read_text());assert facts['synthetic_public_vector'];password=facts['public_synthetic_password']
 archive=folder/'members.7z';test=subprocess.run([str(tool),'t','-bd','-y','-sccUTF-8','-p'+password,str(archive)],capture_output=True,timeout=120)
 (folder/'official-test.log').write_bytes(test.stdout+test.stderr)
 if folder.name.startswith('codec-5-'):
  assert test.returncode==2 and b'Unsupported Method' in test.stderr;unsupported.append(folder.name);continue
 assert test.returncode==0,(folder.name,test.stdout[-2048:],test.stderr)
 for member in facts['members']:
  if member['directory']:continue
  result=subprocess.run([str(tool),'x','-so','-bd','-y','-p'+password,str(archive),'--',member['name']],capture_output=True,timeout=120)
  assert result.returncode==0 and len(result.stdout)==member['length'] and hashlib.sha256(result.stdout).hexdigest().upper()==member['sha256']
  rows.append({'fixture':folder.name,'name':member['name'],'bytes':len(result.stdout),'sha256':member['sha256']})
assert len(unsupported)==6 and len(rows)==72
aes=[]
folders=sorted((out/'layers').glob('[0-9][0-9]'));assert len(folders)==34
for folder in folders:
 facts=json.loads((folder/'expected.json').read_text());assert facts['synthetic_public_vector'] and facts['password']=='Public-7z-exact-header-暗号-42829313'
 p=(folder/'properties.bin').read_bytes();assert p[:2]==b'\xff\x3f' and len(p)==22;salt,iv=p[2:6],p[6:]
 key=(salt+facts['password'].encode('utf-16le'))[:32].ljust(32,b'\0');cipher=(folder/'cipher.bin').read_bytes();assert len(cipher)%16==0
 plain=AES.new(key,AES.MODE_CBC,iv).decrypt(cipher);length=facts['length'];assert 0<=len(plain)-length<=15
 assert plain[:length]==(folder/'plain.bin').read_bytes() and hashlib.sha256(plain[:length]).hexdigest().upper()==facts['sha256']
 assert plain[length:]==bytes([facts['padding_byte']])*(len(plain)-length)
 aes.append({'case':folder.name,'length':length,'padding':len(plain)-length,'cipher_sha256':hashlib.sha256(cipher).hexdigest(),'plain_sha256':facts['sha256']})
result={'official_archives':36,'official_exact_payloads':72,'official_unsupported_zstandard':unsupported,
 'official_tool_sha256':hashlib.sha256(tool.read_bytes()).hexdigest(),'native_rows':rows,'independent_aes_layers':34,'aes_layers':aes}
(out/'independent-header-endings.json').write_text(json.dumps(result,indent=2));print('PASS: 36 official archives / 72 exact payloads, 6 known Zstandard-method gaps and 34 independent AES layers.',flush=True)
