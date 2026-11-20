import argparse, hashlib, json, os, pathlib, re, shutil, subprocess, time

p=argparse.ArgumentParser();p.add_argument('platform',choices=['mac','linux']);p.add_argument('base');p.add_argument('sha256');a=p.parse_args()
base=pathlib.Path(a.base);app=base/'app';process=None;run=None
def gate(owned=None):
    ids=set(map(int,subprocess.run(['pgrep','-x','FileCat([.].*)?'],capture_output=True,text=True).stdout.split()))
    assert ids==({owned} if owned else set()),'BUSY: another FileCat instance or owned process exited'
    assert subprocess.run(['pgrep','-f',r'^[^ ]*dotnet +.*FileCat'],stdout=subprocess.DEVNULL).returncode!=0,'BUSY: another FileCat test runner'
gate();assert hashlib.sha256((base/'app.tgz').read_bytes()).hexdigest()==a.sha256
assert not app.exists(),'Use a fresh stage'
app.mkdir(mode=0o700);subprocess.run(['tar','-xzf',str(base/'app.tgz'),'-C',str(app)],check=True);(app/'FileCat').chmod(0o700)
env=dict(os.environ,DOTNET_ROOT=str(pathlib.Path.home()/'.dotnet'))
if a.platform=='linux':
    auth='/run/user/1000/.mutter-Xwaylandauth.2HB3X3';assert pathlib.Path(auth).is_file()
    env.update(DISPLAY=':0',XAUTHORITY=auth,XDG_RUNTIME_DIR='/run/user/1000',DBUS_SESSION_BUS_ADDRESS='unix:path=/run/user/1000/bus')
result={'purpose':'Native acquisition lifetime scan/save/import/compare subset; no physical-input qualification','archiveSha256':a.sha256}
try:
    gate()
    with (base/'desktop.log').open('wb') as log:
        process=subprocess.Popen(['./FileCat','--acquisition-probe'],cwd=app,env=env,stdin=subprocess.DEVNULL,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
    result['processId']=process.pid;until=time.monotonic()+150
    while time.monotonic()<until:
        gate(process.pid);runs=list((app/'native-validation').glob('dsa-*'))
        if len(runs)==1:
            run=runs[0]
            try:
                report=json.loads((run/'report.json').read_text());assert report['processId']==process.pid
                if report['profileProbeError']:raise RuntimeError(report['profileProbeError'])
                if report['profileProbeFinished']:break
            except (FileNotFoundError,json.JSONDecodeError):pass
        time.sleep(.1)
    else:raise RuntimeError('Native acquisition probe timed out')
    probe=report['acquisitionProbe'];obs=report['lastObservation']
    assert all(probe[k] is True for k in ['repeatedStartPreservedIdentity','marksAndBasketPreserved','importedStayedInert','comparisonStayedInert'])
    assert obs['Phase']==5 and obs['marked']==1 and obs['basket']==1 and obs['hostProfile'].startswith('hp1:')
    assert 'New analysis' in obs['Status'] and probe['sourcePaths']==518 and report['fixtureRegularPaths']==509
    target=base/'native-acquisition.png';gate(process.pid)
    if a.platform=='linux':
        until=time.monotonic()+10;ids=[]
        while time.monotonic()<until:
            gate(process.pid)
            ids=[w for w in subprocess.run(['xdotool','search','--onlyvisible','--pid',str(process.pid)],env=env,capture_output=True,text=True).stdout.split() if subprocess.check_output(['xdotool','getwindowname',w],env=env,text=True).strip()=='Disk space — FileCat']
            if len(ids)==1:break
            time.sleep(.1)
        assert len(ids)==1
        assert subprocess.check_output(['xdotool','getwindowpid',ids[0]],env=env,text=True).strip()==str(process.pid)
        subprocess.run(['import','-window',ids[0],str(target)],env=env,check=True,timeout=15)
    else:
        script='tell application "System Events"\nset p to first application process whose unix id is '+str(process.pid)+'\nset frontmost of p to true\nreturn name of first window of p whose name is "Disk space — FileCat"\nend tell'
        assert subprocess.check_output(['osascript','-e',script],text=True,timeout=20).strip()=='Disk space — FileCat'
        time.sleep(.3);gate(process.pid)
        out=base/'capture.txt';err=base/'capture.err'
        subprocess.run(['open','-g','-n','-W','--stdout',str(out),'--stderr',str(err),'-a','/Users/benny/FileCatValidation/FileCatCapture.app'],check=True,timeout=20)
        match=re.search(r'capture=(/Users/benny/FileCatValidation/native-screen-[0-9A-Fa-f-]+[.]png) width=([0-9]+) height=([0-9]+)',out.read_text());assert match
        shutil.copy2(match.group(1),target)
    assert target.stat().st_size>1000
    result.update(finished=True,acquisitionProbe=probe,fixtureRegularPaths=report['fixtureRegularPaths'],appSha256=report['appSha256'],harnessSha256=report['harnessSha256'])
except BaseException as e:
    result['error']=str(e);raise
finally:
    if run is not None:(run/'close.request').touch()
    if process is not None:
        try:process.wait(timeout=20)
        except subprocess.TimeoutExpired:
            process.terminate();process.wait(timeout=10);result['forcedTermination']=True
        result.update(exitCode=process.returncode,ownedProcessExited=process.poll() is not None)
    if run is not None and (run/'report.json').exists():
        closed=json.loads((run/'report.json').read_text());result.update(state=closed['state'],sourceUnchanged=closed['sourceUnchanged'],deadlineClosed=closed['deadlineClosed'])
    (base/'result.json').write_text(json.dumps(result,indent=2)+'\n')
assert result['state']=='closed' and result['sourceUnchanged'] and not result['deadlineClosed'] and not result.get('forcedTermination') and result['exitCode']==0
gate();result['passed']=True;(base/'result.json').write_text(json.dumps(result,indent=2)+'\n');print('PASS: acquisition boundary; owned process exited',flush=True)
