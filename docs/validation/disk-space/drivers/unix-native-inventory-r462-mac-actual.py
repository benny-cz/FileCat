import hashlib, json, os, pathlib, platform, re, shlex, shutil, subprocess, sys, time

kind='mac' if sys.platform=='darwin' else 'linux'
base=pathlib.Path.home()/'FileCatValidation'/('dsa-20261106-r462-inventory-'+kind)
def observed():
    apps=subprocess.run(['pgrep','-x','FileCat([.].*)?'],capture_output=True,text=True)
    tests=subprocess.run(['pgrep','-f',r'^[^ ]*dotnet +.*FileCat'],capture_output=True,text=True)
    return apps.stdout.split(),tests.stdout.split()
def clear():
    apps,tests=observed()
    if apps or tests: raise RuntimeError('BUSY: native memory test postponed')
clear()
expected=sys.argv[1]
assert re.fullmatch('[0-9a-f]{64}',expected)
assert hashlib.sha256((base/'app.tgz').read_bytes()).hexdigest()==expected,'Archive mismatch'
app=base/'app';app.mkdir(mode=0o700,exist_ok=False)
subprocess.run(['tar','-xzf',str(base/'app.tgz'),'-C',str(app)],check=True)
(app/'FileCat').chmod(0o700)
env=dict(os.environ,DOTNET_ROOT=str(pathlib.Path.home()/'.dotnet'))
if kind=='linux':
    rows=subprocess.check_output(['ps','-u',str(os.getuid()),'-o','args='],text=True).splitlines()
    servers=[shlex.split(row) for row in rows if re.search(r'(^|/)Xwayland\s',row)]
    assert len(servers)==1,'Expected one observed Xwayland session'
    server=servers[0];auth=server[server.index('-auth')+1]
    displays=[arg for arg in server if re.fullmatch(r':[0-9]+',arg)]
    assert len(displays)==1 and pathlib.Path(auth).is_file()
    env.update(DISPLAY=displays[0],XAUTHORITY=auth,XDG_RUNTIME_DIR='/run/user/'+str(os.getuid()),DBUS_SESSION_BUS_ADDRESS='unix:path=/run/user/'+str(os.getuid())+'/bus')
clear()
with (base/'desktop.log').open('wb') as log:
    child=subprocess.Popen(['./FileCat','--indexed-inventory-probe'],cwd=app,env=env,stdin=subprocess.DEVNULL,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
(base/'owned.pid').write_text(str(child.pid)+'\n')
result={'purpose':'Native memory preparation, disjoint scope, retained view and query subset; no full qualification','platform':platform.platform(),'pid':child.pid,'bundleSha256':expected,
        'binaries':{n:hashlib.sha256((app/n).read_bytes()).hexdigest() for n in ['FileCat.dll','FileCat.Core.dll','DiskSpaceHarness.dll','FileCat']}}
run=None
def gate():
    apps,tests=observed()
    assert apps==[str(child.pid)] and not tests,'Owned process ended or another tester appeared'
def report():
    for attempt in range(20):
        try:return json.loads((run/'report.json').read_text())
        except (json.JSONDecodeError,FileNotFoundError):
            if child.poll() is not None:raise
            time.sleep(.025)
    raise RuntimeError('The same live owned report remained unreadable across bounded observations')
def verify_inventory(inv):
    path=pathlib.Path(inv['file']);assert path.resolve().is_relative_to((app/'native-validation').resolve())
    info=path.stat();assert info.st_uid==os.getuid() and info.st_mode & 0o077==0 and info.st_nlink==1
    assert info.st_size==inv['Bytes'] and info.st_size<32*1024*1024
    kinds=['manifest','object','path-node','storage-fact','issue','coverage'];counts={k:0 for k in kinds};digest=hashlib.sha256()
    objects=set();paths={};roots=set();stage=0;trailer=False;files=0
    def number(s):
        assert isinstance(s,str) and re.fullmatch(r'0|[1-9][0-9]{0,19}',s)
        n=int(s);assert n<=2**64-1;return n
    with path.open('rb') as stream:
        while True:
            line=stream.readline(1024*1024+2)
            if not line:break
            assert not trailer and line.endswith(b'\n') and len(line)<=1024*1024+1
            row=json.loads(line);kind=row['kind']
            if kind=='trailer':
                assert row['sha256']==digest.hexdigest() and row['counts']=={k:str(v) for k,v in counts.items()};trailer=True;continue
            digest.update(line);at=kinds.index(kind);assert at>=stage;stage=at;counts[kind]+=1
            if kind=='manifest':
                assert counts[kind]==1 and row['schema']=='1' and row['inventory']==inv['inventoryId']
                assert row['session']==inv['Identity']['SessionId'] and number(row['historicalTaint'])==inv['Taint']
                roots={r[0] for r in row['roots']}
            elif kind=='object':
                assert number(row['id'])==counts[kind];objects.add(row['id'])
            elif kind=='path-node':
                n=number(row['id']);parent=number(row['parent']);assert n==counts[kind] and row['root'] in roots
                assert parent==0 or parent in paths and paths[parent]=='Directory'
                assert row['object'] is None or row['object'] in objects
                paths[n]=row['entryKind'];files+=int(row['entryKind']=='File' and row['included'])
            elif kind=='storage-fact':
                assert number(row['id'])==counts[kind]
                assert row['object'] in objects if row['object'] is not None else paths[number(row['directory'])]=='Directory'
            elif kind=='issue':assert row['node'] is None or number(row['node']) in paths
            else:
                assert counts[kind]==1 and row['qualification'][0] is inv['CoverageDetails']['EnumerationKnown']
                assert number(row['qualification'][1])==inv['CoverageDetails']['EligibleDirectories']
                assert row['qualification'][2:]==[inv['CoverageDetails']['StaleScopesComplete'],inv['CoverageDetails']['HasUnlocatedStaleScopes']]
    assert trailer and counts['path-node']==inv['Paths'] and counts['object']==inv['Objects'] and counts['storage-fact']==inv['Facts'] and counts['issue']==inv['SampledIssues']
    assert files==result.get('finished',{}).get('fixtureRegularPaths',files)
    assert hashlib.sha256(path.read_bytes()).hexdigest()==inv['sha256']
    return {'counts':counts,'regularPaths':files,'sha256':inv['sha256'],'originalDensePathIds':True,'relationshipsAndTrailerVerified':True,'privateModeAndOwnerVerified':True}
try:
    until=time.monotonic()+300
    while time.monotonic()<until:
        gate();runs=list((app/'native-validation').glob('dsa-*'))
        if len(runs)==1 and (runs[0]/'report.json').exists():
            run=runs[0];data=report()
            if data.get('indexedProbeError'):raise RuntimeError(data['indexedProbeError'])
            if data.get('indexedProbeFinished'):break
        time.sleep(.1)
    else:raise TimeoutError('The native memory workflow did not finish')
    data=report();probe=data['indexedProbe'];inv=data['inventoryProbe'];result['finished']=data
    assert data['indexedInventoryProbeRequested'] and data['sessions']==1 and probe['view']=='Table'
    assert probe['matchingPaths']==data['fixtureRegularPaths'] and inv['Paths']>4096
    assert inv['fullDatasetNotPreview'] and inv['conflictPreserved'] and inv['acquisitionOwnerClosedBeforeSave']
    result['wireVerification']=verify_inventory(inv)
    gate()
    # The image was rendered inside the actual FileCat window from its own visual tree.
    # No system/desktop capture, unrelated-window pixels, crop or privacy-helper change occurs.
    frame=run/'native-inventory-window-render.png'
    assert frame.is_file() and frame.stat().st_size>1024
    shutil.copy2(frame,base/'native-inventory-window-render.png')
    result['captureSha256']=hashlib.sha256(frame.read_bytes()).hexdigest();result['captureScope']=inv['captureScope']
    result['passed']=True
except BaseException as error:
    result['passed']=False;result['error']=type(error).__name__+': '+str(error)
finally:
    if run is not None and child.poll() is None:(run/'close.request').write_text('close only this owned memory fixture\n')
    try:child.wait(timeout=30)
    except subprocess.TimeoutExpired:
        result['forcedOwnedTermination']=True;child.kill();child.wait(timeout=10);result['passed']=False
    result['exitCode']=child.returncode
    if run is not None:
        data=report();result['closed']=data
        assert data['state']=='closed' and data['sourceUnchanged'] is True and data['deadlineClosed'] is False and not data.get('indexedProbeError')
        if data.get('inventoryProbe'):
            inv=data['inventoryProbe'];result['wireAfterClose']=verify_inventory(inv)
            result['privateStateUnchangedAfterClose']=hashlib.sha256(pathlib.Path(inv['privateStateFile']).read_bytes()).hexdigest()==inv['privateStateSha256']
            assert result['privateStateUnchangedAfterClose']
            assert not list(pathlib.Path(data['indexedProbe']['scratch']).iterdir())
    apps,tests=observed();result['finalProcessObservation']={'apps':apps,'tests':tests}
    (base/'native-inventory-result.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({'passed':result.get('passed'),'error':result.get('error'),'exitCode':child.returncode,'pid':child.pid,'post':result['finalProcessObservation']}))
sys.exit(0 if result.get('passed') and child.returncode==0 else 1)
