from pathlib import Path
import bz2,ctypes,gzip,io,json,subprocess,sys,tarfile
root=Path(sys.argv[1]); rows=[]
for spec in sorted(root.glob('*.json')):
 if spec.name in ('independent-oracles.json','facts.json'):continue
 data=json.loads(spec.read_text()); archive=spec.with_suffix('.archive'); encoded=archive.read_bytes(); assert len(encoded)<1<<20
 fmt=data['format']
 if fmt=='Tar':raw=encoded
 elif fmt=='TarGzip':raw=gzip.decompress(encoded)
 elif fmt=='TarBzip2':raw=bz2.decompress(encoded)
 elif fmt=='TarZstd':
  lib=ctypes.CDLL('/opt/homebrew/lib/libzstd.dylib' if sys.platform=='darwin' else 'libzstd.so.1')
  lib.ZSTD_decompress.argtypes=[ctypes.c_void_p,ctypes.c_size_t,ctypes.c_void_p,ctypes.c_size_t];lib.ZSTD_decompress.restype=ctypes.c_size_t
  lib.ZSTD_isError.argtypes=[ctypes.c_size_t];lib.ZSTD_isError.restype=ctypes.c_uint
  out=ctypes.create_string_buffer(1<<20);size=lib.ZSTD_decompress(out,len(out),encoded,len(encoded));assert not lib.ZSTD_isError(size);raw=out.raw[:size]
 else:raise AssertionError(fmt)
 assert len(raw)<1<<20
 with tarfile.open(fileobj=io.BytesIO(raw),mode='r:') as tar:
  members=tar.getmembers();names=[m.name.rstrip('/') for m in members]
  assert names==data['names']==['loose','tree','tree/nested','tree/nested/empty','tree/nested/界.bin'],names
  assert [m.isdir() for m in members]==[False,True,True,True,False]
  assert tar.extractfile(members[0]).read()==bytes([2,3]);assert tar.extractfile(members[4]).read()==bytes([7,0,255,9])
 decoded=root/(spec.stem+'.tar');decoded.write_bytes(raw);dest=root/(spec.stem+'-native');dest.mkdir()
 subprocess.run(['/usr/bin/tar','-xf',str(decoded),'-C',str(dest)],check=True)
 assert (dest/'loose').read_bytes()==bytes([2,3]);assert (dest/'tree/nested/界.bin').read_bytes()==bytes([7,0,255,9]);assert (dest/'tree/nested/empty').is_dir()
 assert sorted(p.relative_to(dest).as_posix() for p in dest.rglob('*'))==sorted(data['names'])
 rows.append({'format':fmt,'archive':archive.name,'members':names,'python_tar':True,'native_extract':True})
assert len(rows)==4,len(rows)
(root/'independent-oracles.json').write_text(json.dumps(rows,indent=2));print('PASS: Python TAR and native extraction for all four recursive outputs')
