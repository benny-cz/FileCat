#!/usr/bin/env python3
"""Independent bounded native record/metadata/content check of owned 7z encoder fixtures; no extraction."""
from pathlib import Path
import ctypes,hashlib,json,sys,unicodedata
from raw_names import observe

def run(root,library):
 lib=ctypes.CDLL(library);ptr=ctypes.c_void_p;integer=ctypes.c_int
 signatures={'archive_read_new':([],ptr),'archive_read_support_format_7zip':([ptr],integer),
 'archive_read_open_filename':([ptr,ctypes.c_char_p,ctypes.c_size_t],integer),
 'archive_read_next_header':([ptr,ctypes.POINTER(ptr)],integer),'archive_entry_pathname_utf8':([ptr],ctypes.c_char_p),
 'archive_entry_size':([ptr],ctypes.c_int64),'archive_entry_filetype':([ptr],ctypes.c_uint),
 'archive_entry_perm':([ptr],ctypes.c_uint),'archive_entry_mtime':([ptr],ctypes.c_int64),
 'archive_entry_mtime_nsec':([ptr],ctypes.c_long),'archive_entry_ctime':([ptr],ctypes.c_int64),
 'archive_entry_ctime_nsec':([ptr],ctypes.c_long),'archive_entry_ctime_is_set':([ptr],integer),
 'archive_entry_atime':([ptr],ctypes.c_int64),'archive_entry_atime_nsec':([ptr],ctypes.c_long),
 'archive_entry_atime_is_set':([ptr],integer),'archive_read_data':([ptr,ptr,ctypes.c_size_t],ctypes.c_ssize_t),
 'archive_error_string':([ptr],ctypes.c_char_p),'archive_read_free':([ptr],integer),
 'archive_version_string':([],ctypes.c_char_p),'archive_version_details':([],ctypes.c_char_p)}
 for name,(args,result) in signatures.items():getattr(lib,name).argtypes=args;getattr(lib,name).restype=result
 verified=[];buffer=ctypes.create_string_buffer(65536);archives={};raw_records={}
 for codec in range(6):
  folder=root/('codec-'+str(codec));index=folder/'members.json';path=folder/'members.7z'
  if not index.is_file() or index.stat().st_size>65536 or path.stat().st_size>16<<20:raise ValueError('Fixture scope exceeded')
  rows=json.loads(index.read_text());assert len(rows)==7
  raw=observe(path);assert raw['names']==[row['name'] for row in rows];raw_records[str(codec)]=raw
  archives[str(codec)]=hashlib.sha256(path.read_bytes()).hexdigest()
  archive=lib.archive_read_new();assert archive
  def check(status):
   if status!=0:raise AssertionError((codec,status,lib.archive_error_string(archive)))
  try:
   check(lib.archive_read_support_format_7zip(archive));check(lib.archive_read_open_filename(archive,str(path).encode(),65536))
   for row in rows:
    entry=ptr();check(lib.archive_read_next_header(archive,ctypes.byref(entry)))
    name=lib.archive_entry_pathname_utf8(entry).decode('utf-8')
    allowed=[row['name'],row['name']+'/'] if row['directory'] else [row['name']]
    # Apple's UTF-16 conversion can present decomposed UTF-8. Raw Name property observation above must still
    # match every original code point exactly; normalization is never applied to stored names or input fixtures.
    if sys.platform=='darwin':allowed+=[unicodedata.normalize('NFD',item) for item in allowed]
    assert name in allowed,(row,name)
    assert lib.archive_entry_size(entry)==row['length']
    assert lib.archive_entry_filetype(entry)&0xf000==(0x4000 if row['directory'] else 0x8000)
    metadata=(lib.archive_entry_perm(entry),lib.archive_entry_mtime(entry),lib.archive_entry_mtime_nsec(entry))
    assert metadata==(row['mode'],row['mtime_seconds'],row['mtime_nanos']),(codec,row['name'],metadata,row)
    for field,prefix in [('ctime','ctime'),('atime','atime')]:
     present=getattr(lib,'archive_entry_'+prefix+'_is_set')(entry)!=0
     assert present==(row[field+'_seconds'] is not None),(codec,row['name'],field,present)
     if present:
      actual=(getattr(lib,'archive_entry_'+prefix)(entry),getattr(lib,'archive_entry_'+prefix+'_nsec')(entry))
      assert actual==(row[field+'_seconds'],row[field+'_nanos']),(codec,row['name'],field,actual,row)
    count=0;digest=hashlib.sha256()
    while not row['directory']:
     got=lib.archive_read_data(archive,buffer,len(buffer))
     if got<0:check(got)
     if got==0:break
     assert got<=len(buffer) and count+got<=row['length']<=3<<20
     digest.update(buffer.raw[:got]);count+=got
    assert count==row['length'] and digest.hexdigest().upper()==row['sha256']
    verified.append({'codec':codec,**row,'native_name':name})
   assert lib.archive_read_next_header(archive,ctypes.byref(ptr()))==1
  finally:assert lib.archive_read_free(archive)==0
 result={'reader':lib.archive_version_string().decode(),'details':lib.archive_version_details().decode(),'library':library,
 'library_sha256':hashlib.sha256(Path(library).read_bytes()).hexdigest(),'archive_sha256':archives,'raw_header_records':raw_records,
 'verified':verified,'production_backend_qualified':False}
 (root/'native-reader.json').write_text(json.dumps(result,indent=2));print('PASS:',len(verified),'independent exact 7z records;',result['details'],flush=True)

if __name__=='__main__':
 if len(sys.argv)!=3:raise SystemExit('Usage: verify_native.py OWNED_OUTPUT EXACT_LIBARCHIVE')
 run(Path(sys.argv[1]),sys.argv[2])
