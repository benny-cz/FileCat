#!/usr/bin/env python3
"""Independent libarchive check of the bounded C# writer's owned multi-member fixtures; no extraction."""
from pathlib import Path
import ctypes,ctypes.util,hashlib,json,sys

def run(root,library):
 lib=ctypes.CDLL(library);ptr=ctypes.c_void_p;integer=ctypes.c_int
 signatures={'archive_read_new':([],ptr),'archive_read_support_format_rar5':([ptr],integer),
 'archive_read_open_filename':([ptr,ctypes.c_char_p,ctypes.c_size_t],integer),
 'archive_read_next_header':([ptr,ctypes.POINTER(ptr)],integer),'archive_entry_pathname_utf8':([ptr],ctypes.c_char_p),
 'archive_entry_size':([ptr],ctypes.c_int64),'archive_entry_filetype':([ptr],ctypes.c_uint),
 'archive_entry_perm':([ptr],ctypes.c_uint),'archive_entry_mtime':([ptr],ctypes.c_int64),
 'archive_entry_mtime_nsec':([ptr],ctypes.c_long),'archive_read_data':([ptr,ptr,ctypes.c_size_t],ctypes.c_ssize_t),
 'archive_error_string':([ptr],ctypes.c_char_p),'archive_read_free':([ptr],integer),'archive_version_string':([],ctypes.c_char_p)}
 for name,(args,result) in signatures.items():getattr(lib,name).argtypes=args;getattr(lib,name).restype=result
 verified=[];buffer=ctypes.create_string_buffer(65536)
 for method in range(6):
  folder=root/('method-'+str(method));index=folder/'members.json';archive_path=folder/'members.rar'
  if not index.is_file() or index.stat().st_size>65536 or archive_path.stat().st_size>16<<20:raise ValueError('Fixture bound exceeded')
  rows=json.loads(index.read_text());assert len(rows)==14
  archive=lib.archive_read_new();assert archive
  def check(status):
   if status!=0:raise AssertionError((method,status,lib.archive_error_string(archive)))
  try:
   check(lib.archive_read_support_format_rar5(archive));check(lib.archive_read_open_filename(archive,str(archive_path).encode(),65536))
   for row in rows:
    entry=ptr();check(lib.archive_read_next_header(archive,ctypes.byref(entry)))
    name=lib.archive_entry_pathname_utf8(entry).decode('utf-8')
    # libarchive can present a directory with an appended slash; file names have no presentation adjustment.
    assert name in ([row['name'],row['name']+'/'] if row['directory'] else [row['name']]),(row,name)
    assert lib.archive_entry_size(entry)==row['length']
    assert lib.archive_entry_filetype(entry)&0xf000==(0x4000 if row['directory'] else 0x8000)
    metadata=(lib.archive_entry_perm(entry),lib.archive_entry_mtime(entry),lib.archive_entry_mtime_nsec(entry))
    assert metadata==(row['mode'],row['mtime_seconds'],row['mtime_nanos']),(row['name'],metadata,row)
    count=0;digest=hashlib.sha256()
    while True:
     # Directories have no payload. libarchive correctly refuses archive_read_data on a directory.
     if row['directory']:break
     got=lib.archive_read_data(archive,buffer,len(buffer))
     if got<0:check(got)
     if got==0:break
     assert got<=len(buffer) and count+got<=row['length']<=1<<20
     digest.update(buffer.raw[:got]);count+=got
    assert count==row['length'] and digest.hexdigest().upper()==row['sha256']
    verified.append({'preset':method,'name':row['name'],'bytes':count,'sha256':digest.hexdigest(),
     'mode':metadata[0],'mtime_seconds':metadata[1],'mtime_nanos':metadata[2]})
   assert lib.archive_read_next_header(archive,ctypes.byref(ptr()))==1
  finally:assert lib.archive_read_free(archive)==0
 result={'reader':lib.archive_version_string().decode(),'library':library,'verified':verified,'production_backend_qualified':False}
 (root/'native-reader.json').write_text(json.dumps(result,indent=2));print('PASS:',len(verified),'independent exact member readbacks;',result['reader'],flush=True)

if __name__=='__main__':
 if len(sys.argv)!=3:raise SystemExit('Usage: verify_csharp_native.py OWNED_OUTPUT LIBARCHIVE')
 run(Path(sys.argv[1]),sys.argv[2])
