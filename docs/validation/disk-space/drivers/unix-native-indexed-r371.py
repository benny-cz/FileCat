import hashlib, json, os, pathlib, platform, re, shlex, shutil, subprocess, sys, time

kind='mac' if sys.platform=='darwin' else 'linux'
base=pathlib.Path.home()/'FileCatValidation'/('dsa-20261106-r371-indexed-'+kind)
def observed():
    apps=subprocess.run(['pgrep','-x','FileCat([.].*)?'],capture_output=True,text=True)
    tests=subprocess.run(['pgrep','-f',r'^[^ ]*dotnet +.*FileCat'],capture_output=True,text=True)
    return apps.stdout.split(),tests.stdout.split()
def clear():
    apps,tests=observed()
    if apps or tests:raise RuntimeError('BUSY: native test postponed')
clear()
expected={'mac':'1fa5a0edf157c1bacfa58fccf974e94d2fb0bb582e1456a59a0267a82056a9d4','linux':'99060260e8330b50e4a5367bcbb909a74309a516149129cef8dff76e5a0d1375'}[kind]
assert hashlib.sha256((base/'app.tgz').read_bytes()).hexdigest()==expected,'Archive hash mismatch'
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
    assert len(displays)==1 and pathlib.Path(auth).is_file(),'X11 session authentication is unavailable'
    env.update(DISPLAY=displays[0],XAUTHORITY=auth,XDG_RUNTIME_DIR='/run/user/'+str(os.getuid()),DBUS_SESSION_BUS_ADDRESS='unix:path=/run/user/'+str(os.getuid())+'/bus')
clear()
with (base/'desktop.log').open('wb') as log:
    child=subprocess.Popen(['./FileCat','--indexed-probe'],cwd=app,env=env,stdin=subprocess.DEVNULL,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
(base/'owned.pid').write_text(str(child.pid)+'\n')
result={'purpose':'Native indexed acquisition/private-profile/quota/window/order/lifetime subset; no full qualification','platform':platform.platform(),'pid':child.pid,'events':[],'binaries':{name:hashlib.sha256((app/name).read_bytes()).hexdigest() for name in ['FileCat.dll','FileCat.Core.dll','DiskSpaceHarness.dll','FileCat']}}
run=None
def gate():
    apps,tests=observed()
    assert apps==[str(child.pid)] and not tests,'Owned process ended or another tester appeared; stop this owned run'
def report():
    # The UI can update this generated report while it is observed. A partial read is
    # not process completion/failure; re-observe the same confirmed live owned handle.
    for attempt in range(20):
        try:return json.loads((run/'report.json').read_text())
        except (json.JSONDecodeError,FileNotFoundError):
            if child.poll() is not None:raise
            time.sleep(.025)
    raise RuntimeError('The live owned report remained unreadable across bounded observations')
try:
    until=time.monotonic()+210
    while time.monotonic()<until:
        gate()
        runs=list((app/'native-validation').glob('dsa-*'))
        if len(runs)==1 and (runs[0]/'report.json').exists():
            run=runs[0];data=report()
            if data.get('indexedProbeError'):raise RuntimeError(data['indexedProbeError'])
            if data.get('indexedProbeFinished'):break
        time.sleep(.1)
    else:raise TimeoutError('Native indexed workflow did not finish')
    result['finished']=report()
    assert result['finished']['indexedProbe']['matchingPaths']==result['finished']['fixtureRegularPaths']
    assert result['finished']['indexedProbe']['acquisitionOwnerClosed'] is True
    assert result['finished']['indexedProbe']['sourceCloseFailures']==0
    assert result['finished']['indexedProbe']['order']=='Name' and result['finished']['indexedProbe']['Descending'] is False
    assert result['finished']['sessions']==1
    gate()
    if kind=='mac':
        owned=subprocess.run(['/Users/benny/FileCatValidation/mac-owned-window-r34',str(child.pid)],capture_output=True,text=True,timeout=10)
        assert owned.returncode==0,owned.stderr
        windows=[item for item in json.loads(owned.stdout) if item.get('kCGWindowIsOnscreen') and item.get('kCGWindowLayer')==0 and item.get('kCGWindowBounds',{}).get('Width',0)>300]
        assert len(windows)==1,'Expected one owned native main window'
        result['ownedWindows']=windows
        subprocess.run(['/usr/bin/osascript','-e','tell application "System Events" to set frontmost of first application process whose unix id is '+str(child.pid)+' to true'],check=True,timeout=15)
        out=base/'capture-helper.txt';err=base/'capture-helper.err'
        captured=subprocess.run(['/usr/bin/open','-g','-n','-W','--stdout',str(out),'--stderr',str(err),'-a','/Users/benny/FileCatValidation/FileCatCapture.app'],capture_output=True,text=True,timeout=20)
        text=out.read_text() if out.exists() else captured.stdout
        match=re.search(r'capture=(/Users/benny/FileCatValidation/native-screen-[0-9A-Fa-f-]+[.]png) width=([0-9]+) height=([0-9]+)',text)
        assert captured.returncode==0 and match,'Authorized screen capture helper failed: '+text
        shutil.copy2(match.group(1),base/'native-indexed.png');result['captureScope']='Main display with owned app frontmost; not physical-input qualification'
    else:
        found=subprocess.check_output(['xdotool','search','--onlyvisible','--pid',str(child.pid)],env=env,text=True).split()
        windows=[wid for wid in found if subprocess.check_output(['xdotool','getwindowname',wid],env=env,text=True).strip()=='Disk space — FileCat']
        assert len(windows)==1,'Expected one owned native main window'
        subprocess.run(['import','-window',windows[0],str(base/'native-indexed.png')],env=env,check=True,timeout=15)
        result['ownedWindow']=windows[0];result['captureScope']='Owned X11 window; not physical-input qualification'
    result['passed']=True
except BaseException as error:
    result['passed']=False;result['error']=type(error).__name__+': '+str(error)
finally:
    if run is not None and child.poll() is None:
        (run/'close.request').write_text('close only this owned indexed harness\n')
    try:child.wait(timeout=30)
    except subprocess.TimeoutExpired:
        result['forcedOwnedTermination']=True;child.kill();child.wait(timeout=10);result['passed']=False
    result['exitCode']=child.returncode
    if run is not None:
        data=report();result['closed']=data
        assert data['state']=='closed' and data['sourceUnchanged'] is True and data['deadlineClosed'] is False
        scratch=pathlib.Path(data['indexedProbe']['scratch']) if data.get('indexedProbe') else None
        if scratch is not None:assert not list(scratch.iterdir()),'Owned scratch remains after actual close'
    apps,tests=observed();result['finalProcessObservation']={'apps':apps,'tests':tests}
    (base/'native-indexed-result.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({'passed':result.get('passed'),'error':result.get('error'),'exitCode':result['exitCode'],'pid':child.pid,'post':result['finalProcessObservation']}))
sys.exit(0 if result.get('passed') and child.returncode==0 else 1)
