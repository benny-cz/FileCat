"""Independent guest-only checks of public worker/native 7z pipeline vectors; never executes FileCat decoding."""
from pathlib import Path
import hashlib,io,json,struct,subprocess,sys,zlib

class Packet:
 def __init__(self,data):self.data=bytes.fromhex(data);self.offset=0
 def take(self,count):
  assert 0<=count<=len(self.data)-self.offset;value=self.data[self.offset:self.offset+count];self.offset+=count;return value
 def number(self,kind):return struct.unpack('<'+kind,self.take(struct.calcsize(kind)))[0]
 def flag(self):value=self.number('B');assert value in (0,1);return bool(value)
 def time(self):return self.number('Q') if self.flag() else None
 def graph(self):
  count=self.number('i');assert 1<=count<=32;coders=[]
  for _ in range(count):
   method,inputs,size=self.number('Q'),self.number('i'),self.number('i');assert 0<=size<=64
   coders.append({'method':method,'inputs':inputs,'properties':self.take(size)})
  bindings=[(self.number('i'),self.number('i')) for _ in range(count-1)]
  count=self.number('i');assert 1<=count<=128;packed=[self.number('i') for _ in range(count)];crcs=[self.flag() for _ in range(count)]
  return {'coders':coders,'bindings':bindings,'packed':packed,'packed_crcs':crcs,'final':self.number('i'),'folder_crc':self.flag(),'member_crc':self.flag()}
 def end(self):assert self.offset==len(self.data)

def metadata(value):
 p=Packet(value);assert p.number('H')==1;kind=p.number('i');attrs=p.number('I') if p.flag() else None
 created,accessed,modified=p.time(),p.time(),p.time();graph=p.graph() if p.flag() else None;p.end()
 return {'type':kind,'attributes':attrs,'created':created,'accessed':accessed,'modified':modified,'graph':graph}

def public_coder(coder):
 method=int.from_bytes(coder['method'],'big');properties=coder.get('properties') or b''
 if method==0x06f10701:
  first=properties[0];salt=((first>>7)+(properties[1]>>4)) if first&0xc0 else 0
  iv=(((first>>6)&1)+(properties[1]&15)) if first&0xc0 else 0;properties=bytes((first&63,salt,iv))
 return {'method':method,'inputs':coder['numinstreams'],'properties':properties}

def observe_headers(correct_partial_crc):
 # The pinned reader assumes every folder CRC is defined and encoded-header coders are in execution order.
 # Retain its failures; this explicit test-only adapter handles the two standard record layouts used here.
 import py7zr.archiveinfo as info
 observed=[];files_observed=[];retrieve=info.HeaderStreamsInfo.retrieve
 if correct_partial_crc:
  def unpack_sizes(self,stream):
   assert stream.read(1)==b'\x0c'
   for folder in self.folders:
    for coder in folder.coders:
     for _ in range(coder['numoutstreams']):folder.unpacksizes.append(info.read_uint64(stream))
   tag=stream.read(1)
   if tag==b'\x0a':
    defined=info.read_boolean(stream,len(self.folders),checkall=True);values=iter(info.read_crcs(stream,sum(defined)))
    for folder,present in zip(self.folders,defined):folder.digestdefined=present;folder.crc=next(values) if present else None
    tag=stream.read(1)
   assert tag==b'\x00'
  info.UnpackInfo._retrieve_coders_info=unpack_sizes
  read_files=info.FilesInfo._read
  def files(self,stream):
   count=info.read_uint64(stream);assert count<1000000;filtered=io.BytesIO();info.write_uint64(filtered,count);mask=0;dummy=0;empty=0
   while True:
    tag=stream.read(1);assert len(tag)==1
    if tag==b'\x00':filtered.write(tag);break
    size=info.read_uint64(stream);assert size<16<<20;payload=stream.read(size);assert len(payload)==size;mask|=1<<tag[0]
    if tag==b'\x0e':empty=sum(bool(b&(128>>i)) for j,b in enumerate(payload) for i in range(8) if j*8+i<count)
    if tag==b'\x10':assert size==(empty+7)//8 and not any(payload);continue
    if tag==b'\x19':dummy+=size;assert not any(payload)
    filtered.write(tag);info.write_uint64(filtered,size);filtered.write(payload)
   files_observed.append({'mask':mask,'dummy':dummy});filtered.seek(0);return read_files(self,filtered)
  info.FilesInfo._read=files
 def headers(cls,stream):
  value=retrieve(stream);assert len(value.unpackinfo.folders)==1
  folder=value.unpackinfo.folders[0];consumed={b.outcoder for b in folder.bindpairs};final=[i for i in range(len(folder.coders)) if i not in consumed];assert len(final)==1
  flags=value.packinfo.digestdefined or [False]*value.packinfo.numstreams
  member_crc=bool(folder.digestdefined) or value.substreamsinfo is not None and value.substreamsinfo.digestsdefined[0]
  observed.append({'coders':[public_coder(c) for c in folder.coders],'bindings':[(b.incoder,b.outcoder) for b in folder.bindpairs],
   'packed':list(folder.packed_indices),'packed_crcs':list(flags),'final':final[0],'folder_crc':bool(folder.digestdefined),'member_crc':bool(member_crc)})
  if correct_partial_crc and len(folder.coders)>1:
   assert len(folder.coders)==2 and len(folder.packed_indices)==1 and all(c['numinstreams']==c['numoutstreams']==1 for c in folder.coders)
   order=[folder.packed_indices[0]]
   while len(order)<len(folder.coders):
    following=[b.incoder for b in folder.bindpairs if b.outcoder==order[-1]];assert len(following)==1 and following[0] not in order;order+=following
   folder.coders=[folder.coders[i] for i in order];folder.unpacksizes=[folder.unpacksizes[i] for i in order]
  return value
 info.HeaderStreamsInfo.retrieve=classmethod(headers)
 return observed,files_observed

def header_graph(value):
 p=Packet(value);assert p.number('H')==1;p.number('B');p.flag();mask=p.number('I');dummy=p.number('q')
 for _ in range(4):p.flag()
 graph=p.graph() if p.flag() else None
 for _ in range(4):p.flag()
 p.end();return graph,{'mask':mask,'dummy':dummy}

def run(root,tool,modules,bcj2,adapter=None):
 root,tool,modules,bcj2=map(Path,(root,tool,modules,bcj2));sys.path.insert(0,str(modules));import py7zr
 from py7zr.compressor import AESDecompressor,ZstdDecompressor
 assert adapter in (None,'--partial-folder-crc');observed_headers,observed_files=observe_headers(adapter is not None)
 archives=sorted(root.rglob('*.7z'));assert archives;rows=[];unsupported=[]
 for archive in archives:
  facts=json.loads(archive.with_suffix('.json').read_text());assert facts['synthetic_public_vector']
  password=facts['password'];assert password in ('Public-7z-worker-暗号-42829324','Public-7z-Native-暗号-42829324','  Public creation é e\u0301 🔐  ')
  assert archive.stat().st_size<16<<20;members=facts['members'];want=[metadata(m['metadata']) for m in members]
  result=subprocess.run([str(tool),'t','-bd','-y','-p'+password,str(archive)],capture_output=True,timeout=120)
  archive.with_suffix('.official.log').write_bytes(result.stdout+result.stderr);observed_headers.clear();observed_files.clear()
  with py7zr.SevenZipFile(archive,'r',password=password) as reader:
   expected_header,expected_properties=header_graph(facts['features']);assert observed_headers==([] if expected_header is None else [expected_header]),archive
   if adapter is not None:assert observed_files==[expected_properties],archive
   assert reader.getnames()==[m['name'] for m in members]
   nonempty=[];streams=reader.header.main_streams;folder_index=0;first_pack=0
   pack_crc_flags=streams.packinfo.digestdefined or [False]*streams.packinfo.numstreams
   assert len(pack_crc_flags)==streams.packinfo.numstreams
   packed_crc_values=iter(streams.packinfo.crcs);pack_crcs=[next(packed_crc_values) if defined else None for defined in pack_crc_flags];assert next(packed_crc_values,None) is None
   for index,(actual,raw,member,meta) in enumerate(zip(reader.files,reader.header.files_info.files,members,want)):
    assert meta['type']==member['type'] and actual.is_directory==(member['type']==1) and actual.uncompressed==member['length']
    assert raw.get('attributes')==meta['attributes']
    for tag,key in (('creationtime','created'),('lastaccesstime','accessed'),('lastwritetime','modified')):
     value=raw.get(tag);assert (None if value is None else int(value))==meta[key],(archive.name,member['name'],key)
    graph=meta['graph']
    if graph is None:assert member['length']==0;continue
    folder=streams.unpackinfo.folders[folder_index];folder_index+=1;pack_count=len(folder.packed_indices)
    assert [public_coder(c) for c in folder.coders]==graph['coders']
    assert [(b.incoder,b.outcoder) for b in folder.bindpairs]==graph['bindings'] and folder.packed_indices==graph['packed']
    assert bool(folder.digestdefined)==graph['folder_crc'] and all(c['numoutstreams']==1 for c in folder.coders)
    assert list(pack_crc_flags[first_pack:first_pack+pack_count])==graph['packed_crcs']
    nonempty.append((index,member,folder,first_pack));first_pack+=pack_count
   assert folder_index==len(streams.unpackinfo.folders) and first_pack==streams.packinfo.numstreams
   has_zstd=any(c['method']==b'\x04\xf7\x11\x01' for _,_,folder,_ in nonempty for c in folder.coders)
   if has_zstd:
    assert result.returncode==2 and b'Unsupported Method' in result.stderr;unsupported.append(archive.relative_to(root).as_posix())
    for index,member,folder,first_pack in nonempty:
     encrypted=folder.coders[0]['method']==b'\x06\xf1\x07\x01';split=folder.coders[-1]['method']==b'\x03\x03\x01\x1b'
     chains=([[0,1],[2,3],[4,5],[6]] if encrypted else [[0],[1],[2],[]]) if split else ([[0,1]] if encrypted else [[0]])
     assert len(chains)==len(folder.packed_indices);parts=[]
     offset=reader.afterheader+streams.packinfo.packpos+sum(streams.packinfo.packsizes[:first_pack])
     for branch,chain in enumerate(chains):
      length=streams.packinfo.packsizes[first_pack+branch];assert length<4<<20;reader.fp.seek(offset);packed=reader.fp.read(length);offset+=length;assert len(packed)==length
      if pack_crcs[first_pack+branch] is not None:assert zlib.crc32(packed)==pack_crcs[first_pack+branch]
      coders=[folder.coders[i] for i in chain];sizes=[folder.unpacksizes[i] for i in chain]
      if encrypted:
       decoder=AESDecompressor(coders[0]['properties'],password,65536);plain=decoder.decompress(packed)+decoder.decompress(b'')
       assert len(plain)==len(packed) and 0<=len(plain)-sizes[0]<=15 and not any(plain[sizes[0]:]);packed=plain[:sizes[0]];coders=coders[1:];sizes=sizes[1:]
      if coders:
       assert len(coders)==1 and coders[0]['method']==b'\x04\xf7\x11\x01'
       decoder=ZstdDecompressor(coders[0]['properties'],65536).decompressor;data=decoder.decompress(packed,4<<20)
       assert len(data)==sizes[0] and decoder.eof and not decoder.unused_data
      else:data=packed
      parts.append(bytes(data))
     if split:
      packet=archive.with_suffix('.member-'+str(index)+'.streams');output=archive.with_suffix('.member-'+str(index)+'.decoded')
      packet.write_bytes(struct.pack('<4I',*(len(p) for p in parts))+b''.join(parts));subprocess.run([str(bcj2),str(packet),str(output),str(member['length'])],check=True,timeout=30,capture_output=True);data=output.read_bytes()
     else:data=parts[0]
     assert len(data)==member['length'] and hashlib.sha256(data).hexdigest().upper()==member['sha256']
     if folder.digestdefined:assert zlib.crc32(data)==folder.crc
   else:
    assert result.returncode==0,(archive,result.stderr)
    for member in members:
     if member['type']==1:continue
     value=subprocess.run([str(tool),'x','-so','-bd','-y','-ssc','-p'+password,str(archive),'--',member['name']],capture_output=True,timeout=120)
     assert value.returncode==0 and len(value.stdout)==member['length'] and hashlib.sha256(value.stdout).hexdigest().upper()==member['sha256'],(archive,member['name'])
   for member in members:
    if member['length']==0:assert member['sha256']==hashlib.sha256(b'').hexdigest().upper()
   rows.append({'archive':archive.relative_to(root).as_posix(),'sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'metadata':len(members),'payloads':sum(m['type']==0 for m in members),'composed_zstandard':has_zstd})
 result={'archives':len(rows),'metadata':sum(r['metadata'] for r in rows),'payloads':sum(r['payloads'] for r in rows),'official_archives':len(rows)-len(unsupported),'composed_zstandard_archives':len(unsupported),
  'official_unsupported_zstandard':unsupported,'tool_sha256':hashlib.sha256(tool.read_bytes()).hexdigest(),'bcj2_sha256':hashlib.sha256(bcj2.read_bytes()).hexdigest(),'py7zr':py7zr.__version__,
  'explicit_partial_crc_header_order_adapter':adapter is not None,'encoded_header_graphs_observed':True,'rows':rows}
 (root/'independent-pipeline.json').write_text(json.dumps(result,indent=2));print(json.dumps({k:v for k,v in result.items() if k not in ('rows','official_unsupported_zstandard')}),flush=True)

if __name__=='__main__':run(*sys.argv[1:])
