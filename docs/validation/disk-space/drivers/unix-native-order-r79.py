import argparse, hashlib, json, os, pathlib, re, shutil, stat, subprocess, time

parser=argparse.ArgumentParser()
parser.add_argument('platform',choices=['mac','linux']);parser.add_argument('base');parser.add_argument('sha256')
args=parser.parse_args();base=pathlib.Path(args.base);app=base/'app'
def pids():
    p=subprocess.run(['pgrep','-x','FileCat([.].*)?'],capture_output=True,text=True)
    return set(map(int,p.stdout.split()))
def gate(owned=None):
    assert pids()==({owned} if owned else set()),'BUSY: another FileCat instance or owned process exited'
    assert subprocess.run(['pgrep','-f',r'^[^ ]*dotnet +.*FileCat'],stdout=subprocess.DEVNULL).returncode!=0,'BUSY: another test runner'
gate();assert hashlib.sha256((base/'app.tgz').read_bytes()).hexdigest()==args.sha256
assert not app.exists(),'Use a fresh native profile stage'
app.mkdir(mode=0o700);subprocess.run(['tar','-xzf',str(base/'app.tgz'),'-C',str(app)],check=True);(app/'FileCat').chmod(0o700)
env=dict(os.environ,DOTNET_ROOT=str(pathlib.Path.home()/'.dotnet'))
if args.platform=='linux':
    auth='/run/user/1000/.mutter-Xwaylandauth.2HB3X3'
    assert pathlib.Path(auth).is_file(),'Inspect the current guest X11 session'
    env.update(DISPLAY=':0',XAUTHORITY=auth,XDG_RUNTIME_DIR='/run/user/1000',DBUS_SESSION_BUS_ADDRESS='unix:path=/run/user/1000/bus')
result={'purpose':'Native saved table order/process restart subset; no physical input or complete qualification','archiveSha256':args.sha256,'runs':[]}
profile=None;identity=None;state_bytes=None;corrupt=b'{"schema":"future-profile","test-canary":"preserve exact bytes"}'
def snapshot(process,number):
    gate(process.pid)
    target=base/('native-profile-'+str(number)+'.png')
    if args.platform=='linux':
        until=time.monotonic()+10
        while time.monotonic()<until:
            gate(process.pid)
            x=subprocess.run(['xdotool','search','--onlyvisible','--pid',str(process.pid)],env=env,capture_output=True,text=True)
            ids=[wid for wid in x.stdout.split() if subprocess.check_output(['xdotool','getwindowname',wid],env=env,text=True).strip()=='Disk space — FileCat']
            if len(ids)==1:break
            time.sleep(.1)
        assert len(ids)==1,'Expected one mapped owned native window'
        assert subprocess.check_output(['xdotool','getwindowpid',ids[0]],env=env,text=True).strip()==str(process.pid)
        subprocess.run(['import','-window',ids[0],str(target)],env=env,check=True,timeout=15)
    else:
        script='tell application "System Events"\nset p to first application process whose unix id is '+str(process.pid)+'\nset frontmost of p to true\nreturn name of first window of p whose name is "Disk space — FileCat"\nend tell'
        assert subprocess.check_output(['osascript','-e',script],text=True,timeout=15).strip()=='Disk space — FileCat'
        time.sleep(.3);gate(process.pid)
        out=base/('capture-'+str(number)+'.txt');err=base/('capture-'+str(number)+'.err')
        subprocess.run(['open','-g','-n','-W','--stdout',str(out),'--stderr',str(err),'-a','/Users/benny/FileCatValidation/FileCatCapture.app'],check=True,timeout=20)
        m=re.search(r'capture=(/Users/benny/FileCatValidation/native-screen-[0-9A-Fa-f-]+[.]png) width=([0-9]+) height=([0-9]+)',out.read_text())
        assert m,'Missing authorized capture receipt';shutil.copy2(m.group(1),target)
    assert target.stat().st_size>1000
for number in range(1,3):
    gate()
    if number==3:
        assert profile and profile.is_relative_to(app) and not profile.is_symlink()
        st=profile.stat();assert stat.S_ISREG(st.st_mode) and st.st_nlink==1 and profile.read_bytes()==state_bytes
        profile.write_bytes(corrupt)
    before=set((app/'native-validation').glob('dsa-*')) if (app/'native-validation').exists() else set()
    with (base/('desktop-'+str(number)+'.log')).open('wb') as log:
        process=subprocess.Popen(['./FileCat','--shared-profile','--saved-ordering-probe'],cwd=app,env=env,stdin=subprocess.DEVNULL,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
    run=None;record={'pass':number,'processId':process.pid}
    try:
        until=time.monotonic()+150
        while time.monotonic()<until:
            gate(process.pid)
            new=set((app/'native-validation').glob('dsa-*'))-before
            if len(new)==1:
                run=new.pop()
                try:
                    report=json.loads((run/'report.json').read_text())
                    assert report['processId']==process.pid
                    if report['profileProbeError']:raise RuntimeError(report['profileProbeError'])
                    if report['profileProbeFinished']:break
                except (FileNotFoundError,json.JSONDecodeError):pass
            time.sleep(.1)
        else:raise RuntimeError('Native profile probe timed out')
        observation=report['lastObservation'];assert observation['Phase']==5
        current=observation['hostProfile'];notice=observation['ContinuityNotice']
        profiles=list((app/'native-validation-shared-profile').rglob('private-state.json'));assert len(profiles)==1
        profile=profiles[0]
        if number==1:
            assert current.startswith('hp1:') and notice is None;identity=current;state_bytes=profile.read_bytes()
        elif number==2:
            assert current==identity and notice is None and profile.read_bytes()==state_bytes
        else:
            assert current.startswith('ephemeral:') and notice and profile.read_bytes()==corrupt
        order=report['savedOrderingProbe']
        assert order and order['created']==(number==1) and order['reopenedAfterProcessRestart']==(number==2) and order['filenameOnlyUsesDefaultView']
        assert len(order['results'])==2
        for item in order['results']:
            assert item['Sort']==1 and item['SortDescending']==(item['Name']=='descending.fcdsa') and item['offline'] and item['emptyMarksAndBasket']
        if number==1:first_order_rows=[item['rows'] for item in order['results']]
        else:assert first_order_rows==[item['rows'] for item in order['results']]
        saved_files={str(p.relative_to(app)):hashlib.sha256(p.read_bytes()).hexdigest() for p in (app/'native-validation-shared-profile').rglob('*') if p.is_file() and (p.suffix=='.fcdsa' or p.name.startswith('catalog'))}
        assert len(saved_files)==5
        if number==1:first_saved_files=saved_files
        else:assert first_saved_files==saved_files
        record['savedPayloadBytesPreserved']=True
        record['savedOrderingProbe']=order
        snapshot(process,number)
        record.update(finished=True,hasQualifiedHostProfile=current.startswith('hp1:'),sameIdentityAsFirst=current==identity,continuityNotice=notice,privateBytesPreserved=profile.read_bytes()==(corrupt if number==3 else state_bytes),fixtureRegularPaths=report['fixtureRegularPaths'],appSha256=report['appSha256'],harnessSha256=report['harnessSha256'])
    finally:
        if run is not None:(run/'close.request').touch()
        try:process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            # Only this Popen child, after the harness close request failed.
            process.terminate();process.wait(timeout=10);record['forcedTermination']=True
        if run is not None and (run/'report.json').exists():
            closed=json.loads((run/'report.json').read_text());record.update(state=closed['state'],sourceUnchanged=closed['sourceUnchanged'],deadlineClosed=closed['deadlineClosed'])
        record['exitCode']=process.returncode;record['ownedProcessExited']=process.poll() is not None
        result['runs'].append(record);(base/'result.json').write_text(json.dumps(result,indent=2)+'\n')
    assert record['state']=='closed' and record['sourceUnchanged'] and not record['deadlineClosed'] and not record.get('forcedTermination') and record['exitCode']==0
    gate();print('PASS '+str(number)+' owned process exited',flush=True)
result['passed']=True;(base/'result.json').write_text(json.dumps(result,indent=2)+'\n')
