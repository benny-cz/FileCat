#!/usr/bin/env python3
"""Supporting native-reader survey on owned public BCJ2 fixtures. Failed/unsupported observations never count as passes."""
from pathlib import Path
import ctypes,hashlib,json,sys,unicodedata

def run(root,library):
 root=Path(root).resolve();lib=ctypes.CDLL(library);ptr=ctypes.c_void_p;integer=ctypes.c_int
 signatures={'archive_read_new':([],ptr),'archive_read_support_format_7zip':([ptr],integer),'archive_read_add_passphrase':([ptr,ctypes.c_char_p],integer),
  'archive_read_open_filename':([ptr,ctypes.c_char_p,ctypes.c_size_t],integer),'archive_read_next_header':([ptr,ctypes.POINTER(ptr)],integer),
  'archive_entry_pathname_utf8':([ptr],ctypes.c_char_p),'archive_entry_size':([ptr],ctypes.c_int64),'archive_entry_filetype':([ptr],ctypes.c_uint),
  'archive_entry_perm':([ptr],ctypes.c_uint),'archive_entry_mtime':([ptr],ctypes.c_int64),'archive_entry_mtime_nsec':([ptr],ctypes.c_long),
  'archive_read_data':([ptr,ptr,ctypes.c_size_t],ctypes.c_ssize_t),'archive_error_string':([ptr],ctypes.c_char_p),
  'archive_read_free':([ptr],integer),'archive_version_details':([],ctypes.c_char_p)}
 for name,(args,result) in signatures.items():getattr(lib,name).argtypes=args;getattr(lib,name).restype=result
 folders=sorted(root.glob('archive-*'));assert len(folders)==42;verified=[];failed=[];buffer=ctypes.create_string_buffer(65536)
 for folder in folders:
  archive=lib.archive_read_new();assert archive;rows=json.loads((folder/'members.json').read_text());profile=json.loads((folder/'profile.json').read_text());observed=[]
  def check(status):
   if status!=0:raise RuntimeError(str(status)+': '+str(lib.archive_error_string(archive)))
  try:
   check(lib.archive_read_support_format_7zip(archive))
   if profile['encrypted']:check(lib.archive_read_add_passphrase(archive,profile['public_synthetic_password'].encode('utf-8')))
   check(lib.archive_read_open_filename(archive,str(folder/'members.7z').encode('utf-8'),65536))
   for row in rows:
    entry=ptr();check(lib.archive_read_next_header(archive,ctypes.byref(entry)))
    name=lib.archive_entry_pathname_utf8(entry).decode('utf-8');allowed=[row['name']]+([row['name']+'/'] if row['directory'] else [])
    if sys.platform=='darwin':allowed+=[unicodedata.normalize('NFD',n) for n in allowed]
    assert name in allowed,(name,row)
    assert lib.archive_entry_size(entry)==row['length'] and lib.archive_entry_perm(entry)==row['mode'],row
    assert lib.archive_entry_filetype(entry)&0xf000==(0x4000 if row['directory'] else 0x8000),row
    assert (lib.archive_entry_mtime(entry),lib.archive_entry_mtime_nsec(entry))==(row['mtime_seconds'],row['mtime_nanos']),row
    total=0;digest=hashlib.sha256()
    while not row['directory']:
     got=lib.archive_read_data(archive,buffer,len(buffer))
     if got<0:check(got)
     if got==0:break
     assert got<=len(buffer) and total+got<=row['length']<=1<<20
     total+=got;digest.update(buffer.raw[:got])
    assert total==row['length'] and digest.hexdigest().upper()==row['sha256'],row
    observed.append({'fixture':folder.name,'name':row['name'],'bytes':total,'sha256':digest.hexdigest(),'mode':row['mode']})
   assert lib.archive_read_next_header(archive,ctypes.byref(ptr()))==1
   verified.extend(observed)
  except (RuntimeError,AssertionError,UnicodeError) as error:
   failed.append({'fixture':folder.name,'error':str(error)[:2048],'complete_payload_verification':False})
  finally:lib.archive_read_free(archive)
 result={'library':library,'library_sha256':hashlib.sha256(Path(library).read_bytes()).hexdigest(),'details':lib.archive_version_details().decode(),
  'verified':verified,'not_qualified':failed,'production_dependency':False,'production_profile_qualified':False}
 (root/'native-bcj2.json').write_text(json.dumps(result,indent=2));print(json.dumps({'native_records':len(verified),'unqualified_fixtures':len(failed),'details':result['details']}),flush=True)

if __name__=='__main__':run(*sys.argv[1:])
