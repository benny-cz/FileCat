"""Guest-only independent raw TAR metadata and official payload checks of the shared workflow vectors."""
from pathlib import Path
import bz2,decimal,gzip,hashlib,io,json,runpy,struct,subprocess,sys,tarfile

def run(root,tool,modules,packet_helper,scope=None):
 assert scope in (None,'--jobs','--source-case','--moves')
 root,tool,modules=map(Path,(root,tool,modules));sys.path.insert(0,str(modules))
 packet=runpy.run_path(packet_helper)['Packet'];rows=[];payloads=0;metadata=0
 for document in sorted(root.glob('format-Tar*-mode-*/*.json')):
  facts=json.loads(document.read_text());assert facts['synthetic_public_vector'] and facts['format'] in (1,2,3,4)
  extension={1:'.tar',2:'.tar.gz',3:'.tar.bz2',4:'.tar.zst'}[facts['format']];archive=document.with_name(document.stem+extension)
  data=archive.read_bytes();assert len(data)<16<<20 and hashlib.sha256(data).hexdigest().upper()==facts['revision']
  if facts['format']==2:
   with gzip.GzipFile(fileobj=io.BytesIO(data)) as reader:raw=reader.read((16<<20)+1);assert reader.read(1)==b''
  elif facts['format']==3:
   decoder=bz2.BZ2Decompressor();raw=decoder.decompress(data,(16<<20)+1);assert decoder.eof and not decoder.unused_data
  elif facts['format']==4:
   from py7zr.compressor import zstd
   decoder=zstd.ZstdDecompressor();raw=decoder.decompress(data,(16<<20)+1);assert decoder.eof and not decoder.unused_data
  else:raw=data
  assert len(raw)<16<<20;expected=facts['members'];assert 1<=len(expected)<=100
  # The independent official reader sees a raw TAR view so wrapper support does not affect the extraction check.
  view=archive.with_name(document.stem+'.oracle.tar');view.write_bytes(raw)
  test=subprocess.run([str(tool),'t','-bd','-y',str(view)],capture_output=True,timeout=60)
  document.with_suffix('.official.log').write_bytes(test.stdout+test.stderr);assert test.returncode==0,(archive,test.stderr)
  with tarfile.open(fileobj=io.BytesIO(raw),mode='r:') as reader:
   actual=reader.getmembers();assert len(actual)==len(expected)
   for member,wanted in zip(actual,expected):
    assert member.isdir()==(wanted['type']==1) and member.size==wanted['length']
    header=raw[member.offset_data-512:member.offset_data];assert len(header)==512
    spelling=header[:100].split(b'\0')[0].decode('utf-8');prefix=header[345:500].split(b'\0')[0].decode('utf-8')
    if prefix:spelling=prefix+'/'+spelling
    assert member.pax_headers.get('path',spelling)==wanted['name']
    p=packet(wanted['metadata']);schema=p.number('H');assert schema in (1,2)
    assert (member.mode,member.uid,member.gid)==(p.number('i'),p.number('i'),p.number('i'))
    for text in (member.uname,member.gname):size=p.number('i');assert 0<=size<=65536 and p.take(size).decode('utf-8')==text
    def instant():
     value=p.number('q')
     if schema==1:return decimal.Decimal(value-621355968000000000)/10000000
     return decimal.Decimal(value)+decimal.Decimal(p.number('i'))/1000000000
    assert decimal.Decimal(member.pax_headers.get('mtime',str(member.mtime)))==instant()
    for name in ('atime','ctime'):
     if p.flag():assert name in member.pax_headers and decimal.Decimal(member.pax_headers[name])==instant()
     else:assert name not in member.pax_headers
    p.end();metadata+=1
    if wanted['type']==1:continue
    stream=reader.extractfile(member);assert stream is not None;content=stream.read((16<<20)+1)
    assert len(content)==wanted['length'] and hashlib.sha256(content).hexdigest().upper()==wanted['sha256']
    extracted=subprocess.run([str(tool),'x','-so','-bd','-y','-ssc',str(view),'--',wanted['name']],capture_output=True,timeout=60)
    assert extracted.returncode==0 and extracted.stdout==content,(archive,wanted['name']);payloads+=1
  rows.append({'archive':archive.relative_to(root).as_posix(),'sha256':hashlib.sha256(data).hexdigest()})
 if scope is None:assert len(rows)==8 and metadata==60 and payloads==52
 elif scope=='--jobs':
  documents=list(root.glob('format-Tar*-mode-*/*.json'))
  variants={p.stem for p in documents};assert variants in ({'created'},{'created','updated'})
  formats={'Tar','TarGzip','TarBzip2','TarZstd'}
  assert {(p.parent.name,p.stem) for p in documents}=={('format-'+format+'-mode-0',name) for format in formats for name in variants}
  assert len(rows)==len(variants)*4 and metadata==(32 if len(variants)==1 else 76) and payloads==(20 if len(variants)==1 else 44)
 elif scope=='--source-case':
  documents=list(root.glob('format-Tar*-mode-*/*.json'));formats={'Tar','TarGzip','TarBzip2','TarZstd'}
  assert {(p.parent.name,p.stem) for p in documents}=={('format-'+format+'-mode-0',name) for format in formats for name in ('source-leaves','source-parents')}
  assert len(rows)==8 and metadata==payloads==16
 else:
  documents=list(root.glob('format-Tar*-mode-*/*.json'));formats={'Tar','TarGzip','TarBzip2','TarZstd'}
  assert {(p.parent.name,p.stem) for p in documents}=={('format-'+format+'-mode-0',name) for format in formats for name in ('moved-created','moved-updated')}
  assert len(rows)==8 and metadata==44 and payloads==28
 result={'archives':len(rows),'metadata':metadata,'payloads':payloads,'tool_sha256':hashlib.sha256(tool.read_bytes()).hexdigest(),'rows':rows}
 (root/'independent-workflow-tar.json').write_text(json.dumps(result,indent=2));print(json.dumps({k:v for k,v in result.items() if k!='rows'}),flush=True)

if __name__=='__main__':run(*sys.argv[1:])
