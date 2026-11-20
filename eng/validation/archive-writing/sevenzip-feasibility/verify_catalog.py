"""Guest-only independent metadata, packed-witness and official payload checks of public 7z catalog vectors."""
from pathlib import Path
import hashlib,io,json,struct,subprocess,sys

def run(root,tool,modules,oracle=None,bcj2=None):
 root,tool,modules=map(Path,(root,tool,modules));sys.path.insert(0,str(modules));import py7zr
 fixtures=sorted(root.glob('codec-*-filter-*-protection-*'));assert len(fixtures)==273
 rows=[];unsupported=[]
 for folder in fixtures:
  facts=json.loads((folder/'members.json').read_text());assert facts['synthetic_public_vector'] and facts['password']=='Public-7z-catalog-暗号-42829323'
  archive=folder/'members.7z';data=archive.read_bytes();assert hashlib.sha256(data).hexdigest().upper()==facts['revision'] and len(data)<1<<20
  with py7zr.SevenZipFile(archive,'r',password=facts['password'] if facts['encrypted'] else None) as reader:
   assert reader.getnames()==[m['name'] for m in facts['members']]
   for actual,raw,wanted in zip(reader.files,reader.header.files_info.files,facts['members']):
    assert actual.is_directory==(wanted['type']==1) and actual.uncompressed==wanted['length']
    assert raw.get('attributes')==wanted['attributes']
    value=raw.get('lastwritetime');assert (None if value is None else int(value))==wanted['modified']
  result=subprocess.run([str(tool),'t','-bd','-y','-p'+facts['password'],str(archive)],capture_output=True,timeout=120)
  (folder/'official-test.log').write_bytes(result.stdout+result.stderr)
  zstandard=folder.name.startswith('codec-5-')
  if zstandard:
   assert result.returncode==2 and b'Unsupported Method' in result.stderr,(folder.name,result.stderr);unsupported.append(folder.name)
   verify_zstandard(folder,facts,archive,oracle,bcj2,py7zr)
  else:assert result.returncode==0,(folder.name,result.stderr)
  for index,wanted in enumerate(facts['members']):
   for witness in wanted['packed']:
    p=witness['Physical'];block=data[p['Offset']:p['Offset']+p['Length']];assert len(block)==p['Length'] and hashlib.sha256(block).hexdigest().upper()==witness['Sha256']
   if wanted['type']==1 or zstandard:continue
   result=subprocess.run([str(tool),'x','-so','-bd','-y','-p'+facts['password'],str(archive),'--',wanted['name']],capture_output=True,timeout=120)
   (folder/('extract-'+str(index)+'.log')).write_bytes(result.stderr)
   assert result.returncode==0 and len(result.stdout)==wanted['length'] and hashlib.sha256(result.stdout).hexdigest().upper()==wanted['sha256'],(folder.name,index)
  rows.append({'case':folder.name,'revision':facts['revision'],'metadata':len(facts['members']),'payloads':sum(m['type']==0 for m in facts['members']),
   'packed_witnesses':sum(len(m['packed']) for m in facts['members'])})
 result={'cases':len(rows),'metadata_records':sum(r['metadata'] for r in rows),'payloads':sum(r['payloads'] for r in rows),
  'packed_witnesses':sum(r['packed_witnesses'] for r in rows),'tool_sha256':hashlib.sha256(tool.read_bytes()).hexdigest(),'py7zr':py7zr.__version__,'rows':rows,
  'official_archives':len(rows)-len(unsupported),'official_payloads':(len(rows)-len(unsupported))*2,'zstandard_composed_archives':len(unsupported),'zstandard_composed_payloads':len(unsupported)*2,'official_unsupported_zstandard':unsupported}
 assert len(unsupported)==39
 if oracle:
  offsets=[]
  for folder in sorted((root/'branch-offsets').iterdir()):
   kind,offset=folder.name.split('-');output=folder/'sdk-encoded.bin'
   subprocess.run([str(oracle),'filter',kind,str(folder/'input.bin'),str(output),'1',offset],check=True,timeout=30,capture_output=True)
   assert output.read_bytes()==(folder/'encoded.bin').read_bytes(),folder.name
   offsets.append(folder.name)
  assert len(offsets)==6;result.update(branch_offsets=offsets,sdk_sha256=hashlib.sha256(Path(oracle).read_bytes()).hexdigest())
 (root/'independent-catalog.json').write_text(json.dumps(result,indent=2));print(json.dumps({k:v for k,v in result.items() if k!='rows'}),flush=True)

def verify_zstandard(fixture,facts,archive,oracle,bcj2,py7zr):
 from py7zr.compressor import AESDecompressor,ZstdDecompressor
 assert oracle and bcj2
 password=facts['password'] if facts['encrypted'] else None
 with py7zr.SevenZipFile(archive,'r',password=password) as reader:
  streams=reader.header.main_streams;assert len(streams.unpackinfo.folders)==1;folder=streams.unpackinfo.folders[0];pack=streams.packinfo
  encrypted=facts['encrypted'];is_bcj2=fixture.name.split('-')[3]=='12';parts=[];position=reader.afterheader+pack.packpos
  chains=([[0,1],[2,3],[4,5],[6]] if encrypted else [[0],[1],[2],[]]) if is_bcj2 else ([[0,1]] if encrypted else [[0]])
  assert len(chains)==pack.numstreams
  for branch,chain in enumerate(chains):
   count=pack.packsizes[branch];reader.fp.seek(position);packed=reader.fp.read(count);position+=count;assert len(packed)==count<1<<20
   coders=[folder.coders[c] for c in chain];sizes=[folder.unpacksizes[c] for c in chain]
   if encrypted:
    assert coders[0]['method']==b'\x06\xf1\x07\x01';decoder=AESDecompressor(coders[0]['properties'],password,65536)
    plain=decoder.decompress(packed)+decoder.decompress(b'');assert len(plain)==count and 0<=len(plain)-sizes[0]<=15 and not any(plain[sizes[0]:])
    packed=plain[:sizes[0]];coders=coders[1:];sizes=sizes[1:]
   if not coders:decoded=packed
   else:
    assert len(coders)==1 and coders[0]['method']==b'\x04\xf7\x11\x01'
    # The high-level py7zr chain stops at the declared symbol count, so it cannot consume a valid zero-output frame.
    # Its pinned independent Zstd implementation exposes actual frame completion and a bounded-output primitive.
    decoder=ZstdDecompressor(coders[0]['properties'],65536).decompressor;decoded=decoder.decompress(packed,1<<20)
    assert len(decoded)==sizes[-1] and decoder.eof and not decoder.unused_data
   parts.append(bytes(decoded))
  wanted=facts['members'][0];assert wanted['length']>0
  if is_bcj2:
   packet=fixture/'zstandard-bcj2.streams';output=fixture/'zstandard-bcj2.decoded';packet.write_bytes(struct.pack('<4I',*(len(p) for p in parts))+b''.join(parts))
   subprocess.run([str(bcj2),str(packet),str(output),str(wanted['length'])],check=True,timeout=30,capture_output=True);data=output.read_bytes()
   assert len(data)==wanted['length'] and hashlib.sha256(data).hexdigest().upper()==wanted['sha256']
  else:
   filter_id=int(fixture.name.split('-')[3]);source=fixture.parent/'branch-offsets/Arm64-4/input.bin';original=source.read_bytes()
   assert len(original)==wanted['length'] and hashlib.sha256(original).hexdigest().upper()==wanted['sha256']
   if filter_id==0:expected=original
   else:
    kind=['None','Delta','X86','Arm','ArmThumb','Arm64','Ppc','Ia64','Sparc','RiscV','Swap2','Swap4'][filter_id];output=fixture/'sdk-filtered.bin'
    subprocess.run([str(oracle),'filter',kind,str(source),str(output),'7' if filter_id==1 else '1','0'],check=True,timeout=30,capture_output=True);expected=output.read_bytes()
   assert parts[0]==expected,fixture.name
  assert facts['members'][2]['length']==0 and facts['members'][2]['sha256']==hashlib.sha256(b'').hexdigest().upper()

if __name__=='__main__':run(*sys.argv[1:])
