#!/usr/bin/env python3
"""Independent system-libarchive readback of owned feasibility outputs; no extraction paths granted."""
from pathlib import Path
import ctypes,ctypes.util,hashlib,json,sys

def run(root,library=None):
 library=library or ('/usr/lib/libarchive.2.dylib' if sys.platform=='darwin' else ctypes.util.find_library('archive'))
 lib=ctypes.CDLL(library)
 ptr=ctypes.c_void_p;integer=ctypes.c_int;size=ctypes.c_size_t
 signatures={'archive_read_new':([],ptr),'archive_read_support_format_rar5':([ptr],integer),
  'archive_read_open_filename':([ptr,ctypes.c_char_p,size],integer),'archive_read_next_header':([ptr,ctypes.POINTER(ptr)],integer),
  'archive_entry_pathname_utf8':([ptr],ctypes.c_char_p),'archive_entry_size':([ptr],ctypes.c_int64),
  'archive_entry_filetype':([ptr],ctypes.c_uint),'archive_read_data':([ptr,ptr,size],ctypes.c_ssize_t),
  'archive_entry_perm':([ptr],ctypes.c_uint),'archive_entry_mtime':([ptr],ctypes.c_int64),'archive_entry_mtime_nsec':([ptr],ctypes.c_long),
  'archive_error_string':([ptr],ctypes.c_char_p),'archive_read_free':([ptr],integer),'archive_version_string':([],ctypes.c_char_p)}
 for name,(arguments,result) in signatures.items():getattr(lib,name).argtypes=arguments;getattr(lib,name).restype=result
 rows=json.loads((root/'results.json').read_text())['cases'];verified=[]
 assert len(rows)==8
 for row in rows:
  path=root/(row['case']+'.rar');expected=root/(row['case']+'.expected');archive=lib.archive_read_new();assert archive
  def check(status):
   if status!=0:raise AssertionError((row['case'],status,lib.archive_error_string(archive)))
  try:
   check(lib.archive_read_support_format_rar5(archive));check(lib.archive_read_open_filename(archive,str(path).encode(),65536))
   entry=ptr();check(lib.archive_read_next_header(archive,ctypes.byref(entry)))
   assert lib.archive_entry_pathname_utf8(entry).decode()==row['name'];assert lib.archive_entry_size(entry)==row['input']
   assert lib.archive_entry_filetype(entry)&0xf000==0x8000
   metadata=(lib.archive_entry_perm(entry),lib.archive_entry_mtime(entry),lib.archive_entry_mtime_nsec(entry))
   assert metadata==(row['mode'],row['mtime_seconds'],row['mtime_nanos']),(row['case'],'metadata differs',metadata)
   output=bytearray();buffer=ctypes.create_string_buffer(65536)
   while True:
    count=lib.archive_read_data(archive,buffer,len(buffer))
    if count<0:check(count)
    if count==0:break
    assert count<=65536 and len(output)+count<=row['input']<=1<<20;output.extend(buffer.raw[:count])
   assert bytes(output)==expected.read_bytes();assert hashlib.sha256(output).hexdigest()==row['sha256']
   assert lib.archive_read_next_header(archive,ctypes.byref(entry))==1
   verified.append({'case':row['case'],'bytes':len(output),'method':row['method'],'sha256':row['sha256'],'mode':metadata[0],'mtime_seconds':metadata[1],'mtime_nanos':metadata[2]})
  finally:assert lib.archive_read_free(archive)==0
 result={'reader':lib.archive_version_string().decode(),'library':library,'verified':verified,'production_backend_qualified':False}
 (root/'native-reader.json').write_text(json.dumps(result,indent=2));print(json.dumps(result),flush=True)

if __name__=='__main__':run(Path(sys.argv[1]),sys.argv[2] if len(sys.argv)>2 else None)
