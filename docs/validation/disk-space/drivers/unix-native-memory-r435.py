import hashlib, json, os, pathlib, platform, re, shlex, shutil, subprocess, sys, time

kind='mac' if sys.platform=='darwin' else 'linux'
base=pathlib.Path.home()/'FileCatValidation'/('dsa-20261106-r435-memory-'+kind)
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
    child=subprocess.Popen(['./FileCat','--memory-fallback-probe'],cwd=app,env=env,stdin=subprocess.DEVNULL,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
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
try:
    until=time.monotonic()+210
    while time.monotonic()<until:
        gate();runs=list((app/'native-validation').glob('dsa-*'))
        if len(runs)==1 and (runs[0]/'report.json').exists():
            run=runs[0];data=report()
            if data.get('indexedProbeError'):raise RuntimeError(data['indexedProbeError'])
            if data.get('indexedProbeFinished'):break
        time.sleep(.1)
    else:raise TimeoutError('The native memory workflow did not finish')
    data=report();probe=data['indexedProbe'];result['finished']=data
    assert data['memoryFallbackProbeRequested'] and data['sessions']==1 and probe['kind']=='memory-fallback' and probe['view']=='Memory'
    assert probe['matchingPaths']==data['fixtureRegularPaths'] and probe['queryMatches']==1 and probe['unavailableRoots']==1
    assert probe['acquisitionOwnerClosed'] and probe['capturedRevisionRetained'] and probe['sourceCloseFailures']==0
    assert probe['stateUnchanged'] and probe['neighborUnchanged'] and not probe['scratchCreated'] and not probe['privateNamesObserved']
    assert 0<probe['pixelWidth']<=8192 and 0<probe['pixelHeight']<=8192 and probe['pixelWidth']*probe['pixelHeight']<=16_000_000
    gate()
    # The image was rendered inside the actual FileCat window from its own visual tree.
    # No system/desktop capture, unrelated-window pixels, crop or privacy-helper change occurs.
    frame=run/'native-memory-window-render.png'
    assert frame.is_file() and frame.stat().st_size>1024
    shutil.copy2(frame,base/'native-memory-window-render.png')
    result['captureSha256']=hashlib.sha256(frame.read_bytes()).hexdigest();result['captureScope']=probe['captureScope']
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
        assert data['state']=='closed' and data['sourceUnchanged'] is True and data['deadlineClosed'] is False
        if data.get('indexedProbe'):
            probe=data['indexedProbe']
            result['stateHashAfterClose']=hashlib.sha256(pathlib.Path(probe['privateStateFile']).read_bytes()).hexdigest()
            result['neighborHashAfterClose']=hashlib.sha256(pathlib.Path(probe['neighborFile']).read_bytes()).hexdigest()
            assert result['stateHashAfterClose']==probe['privateStateSha256'] and result['neighborHashAfterClose']==probe['neighborSha256']
            assert not pathlib.Path(probe['scratch']).exists()
    apps,tests=observed();result['finalProcessObservation']={'apps':apps,'tests':tests}
    (base/'native-memory-result.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({'passed':result.get('passed'),'error':result.get('error'),'exitCode':child.returncode,'pid':child.pid,'post':result['finalProcessObservation']}))
sys.exit(0 if result.get('passed') and child.returncode==0 else 1)
