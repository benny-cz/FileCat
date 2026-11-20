import hashlib, json, os, pathlib, subprocess, time

base=pathlib.Path('/Users/benny/FileCatValidation/dsa-20261105-r95-links')
def clear():
    return subprocess.run(['pgrep','-x','FileCat([.].*)?'],stdout=subprocess.DEVNULL).returncode!=0 and subprocess.run(['pgrep','-f',r'^[^ ]*dotnet +.*FileCat'],stdout=subprocess.DEVNULL).returncode!=0
if not clear():raise SystemExit('BUSY: postpone native Mac test')
archive=base/'app.tgz'
assert hashlib.sha256(archive.read_bytes()).hexdigest()=='49d59029b7d8f6b22a57c311f2ee6b7d8424984696cc540074f3075fabd6b454'
app=base/'app';app.mkdir(mode=0o700,exist_ok=True)
assert not (app/'native-validation').exists(),'Use a fresh native fixture run'
subprocess.run(['tar','-xzf',str(archive),'-C',str(app)],check=True);(app/'FileCat').chmod(0o700)
if not clear():raise SystemExit('BUSY: another tester started')
with (base/'desktop.log').open('wb') as log:
    process=subprocess.Popen(['./FileCat'],cwd=app,env=dict(os.environ,DOTNET_ROOT=str(pathlib.Path.home()/'.dotnet')),stdin=subprocess.DEVNULL,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
(app/'owned.pid').write_text(str(process.pid)+'\n')
result={'purpose':'Native Mac table ordering/breadcrumb/AX selection/keyboard subset; no full qualification','processId':process.pid,'events':[]}
run=None
def gate():
    p=subprocess.run(['pgrep','-x','FileCat([.].*)?'],capture_output=True,text=True)
    assert p.returncode==0 and p.stdout.strip()==str(process.pid),'Owned process exited or another tester appeared'
    assert subprocess.run(['pgrep','-f',r'^[^ ]*dotnet +.*FileCat'],stdout=subprocess.DEVNULL).returncode!=0,'Another test runner appeared'
def script(body):
    gate()
    code='tell application "System Events"\nset p to first application process whose unix id is '+str(process.pid)+'\n'+body+'\nend tell'
    response=subprocess.run(['/usr/bin/osascript','-e',code],capture_output=True,text=True,timeout=20)
    if response.returncode:raise RuntimeError(response.stderr.strip())
    return response.stdout
def literal(value):return '"'+value.replace('\\','\\\\').replace('"','\\"')+'"'
def find(role,name):
    return '''set observedElements to entire contents of (first window of p whose name is "Disk space — FileCat")
set matches to 0
set rawMatches to 0
set observedTargetFrames to {}
set candidate to missing value
repeat with i from 1 to (count observedElements)
set e to item i of observedElements
try
set elementRole to role of e as text
set elementName to name of e as text
if elementRole is '''+literal(role)+''' and elementName is '''+literal(name)+''' then
set rawMatches to rawMatches + 1
set observedTargetFrame to {position of e, size of e}
if observedTargetFrame is not in observedTargetFrames then
set end of observedTargetFrames to observedTargetFrame
set matches to matches + 1
set candidate to e
exit repeat
end if
end if
end try
end repeat
if matches is not 1 then error ("Expected one target; found " & matches & "; frames=" & (observedTargetFrames as text))
'''
def invoke(name):
    until=time.monotonic()+5
    while True:
        try:
            script(find('AXButton',name)+'perform action "AXPress" of candidate')
            break
        except RuntimeError as error:
            if 'Expected one target; found 0' not in str(error) or time.monotonic()>=until:raise
            time.sleep(.1)
    result['events'].append('Invoked '+name)
def set_text(name,value):
    script(find('AXTextField',name)+'set value of candidate to '+literal(value));result['events'].append('Entered '+name)
def tree():
    return script('''set observedElements to entire contents of (first window of p whose name is "Disk space — FileCat")
set treeText to ""
repeat with i from 1 to (count observedElements)
if i > 400 then exit repeat
set e to item i of observedElements
set r to ""
set n to ""
set v to ""
try
set r to role of e as text
end try
try
set n to name of e as text
end try
try
set v to value of e as text
end try
set treeText to treeText & r & tab & n & tab & v & linefeed
end repeat
return treeText''')
def wait(check,seconds=15):
    until=time.monotonic()+seconds
    while time.monotonic()<until:
        gate()
        try:
            value=check()
            if value:return value
        except (FileNotFoundError,json.JSONDecodeError):pass
        time.sleep(.05)
    raise RuntimeError('Native observation timed out')
def snapshot(label):
    (base/('observation-'+label+'.json')).write_text(json.dumps(observation(),indent=2))
    output=subprocess.run(['/Users/benny/FileCatValidation/mac-owned-window-r34',str(process.pid)],capture_output=True,text=True,timeout=10)
    if output.returncode:raise RuntimeError(output.stderr.strip())
    windows=json.loads(output.stdout);(base/('windows-'+label+'.json')).write_text(json.dumps(windows,indent=2))
    windows=[w for w in windows if w.get('kCGWindowIsOnscreen') is True and w.get('kCGWindowLayer')==0 and w.get('kCGWindowBounds',{}).get('Width',0)>300 and w.get('kCGWindowBounds',{}).get('Height',0)>300]
    assert len(windows)==1,'Expected one owned native main window'
    script('set frontmost of p to true')
    captureOut=base/('helper-'+label+'.txt');captureErr=base/('helper-'+label+'.err')
    launched=subprocess.run(['/usr/bin/open','-g','-n','-W','--stdout',str(captureOut),'--stderr',str(captureErr),'-a','/Users/benny/FileCatValidation/FileCatCapture.app'],capture_output=True,text=True,timeout=15)
    capture=subprocess.CompletedProcess(launched.args,launched.returncode,captureOut.read_text() if captureOut.exists() else launched.stdout,captureErr.read_text() if captureErr.exists() else launched.stderr)
    (base/('capture-'+label+'.txt')).write_text(capture.stdout+capture.stderr)
    if capture.returncode!=0:
        result.setdefault('captureFailures',{})[label]=capture.stdout.strip()+capture.stderr.strip()
        return
    import re,shutil
    match=re.search(r'capture=(/Users/benny/FileCatValidation/native-screen-[0-9A-Fa-f-]+[.]png) width=([0-9]+) height=([0-9]+)',capture.stdout)
    assert match,'Capture helper returned an unexpected receipt'
    source=pathlib.Path(match.group(1));assert source.is_file()
    shutil.copy2(source,base/('native-'+label+'.png'))
    result.setdefault('captures',{})[label]={'width':int(match.group(2)),'height':int(match.group(3)),'helper':'com.filecat.validation.capture','scope':'Main display while owned app is frontmost'}
def enter():
    script('''set frontmost of p to true
set foregroundProcess to first application process whose frontmost is true
if (unix id of foregroundProcess as integer) is not '''+str(process.pid)+''' then error "Foreground process mismatch"
key code 36''');result['events'].append('Pressed native Return')
def report():return json.loads((run/'report.json').read_text())
def radio(name):
    script(find('AXRadioButton',name)+'perform action "AXPress" of candidate')
    result['events'].append('Selected '+name)
def frame(name):
    value=script(find('AXRadioButton',name)+'''set origin to position of candidate
set extent to size of candidate
return (item 1 of origin as text) & tab & (item 2 of origin as text) & tab & (item 1 of extent as text) & tab & (item 2 of extent as text)''')
    x,y,width,height=[float(part.replace(',','.')) for part in value.strip().split('\t')]
    return dict(x=x,y=y,width=width,height=height)
def observation():return report().get('lastObservation') or {}
def rows():return observation().get('tableRows',[])
def front():return 'set frontmost of p to true\nset fp to first application process whose frontmost is true\nif (unix id of fp as integer) is not '+str(process.pid)+' then error "Foreground mismatch"\n'
def click_candidate(prefix):
    # Coordinates come from the freshly enumerated owned accessibility object.
    observed=script(prefix+'''set origin to position of candidate
set extent to size of candidate
return (item 1 of origin as text) & tab & (item 2 of origin as text) & tab & (item 1 of extent as text) & tab & (item 2 of extent as text)''')
    result.setdefault('pointerObservations',[]).append(observed.strip())
    x,y,w,h=[float(v.replace(',','.')) for v in observed.strip().split('\t')];assert w>8 and h>8,observed
    script(front()+'click at {'+str(int(x+min(80,w/2)))+', '+str(int(y+h/2))+'}')
def first_row():
    return find('AXList','Ranked analysis entries')+'''set listElements to entire contents of candidate
set candidate to missing value
repeat with e in listElements
if role of e as text is "AXRow" then
set candidate to e
exit repeat
end if
end repeat
if candidate is missing value then error "No realized table row"
'''
def choose(name,value):
    script(find('AXPopUpButton',name)+'perform action "AXPress" of candidate')
    menu=find('AXPopUpButton',name)+find('AXMenuItem',value).replace('set observedElements to entire contents of (first window of p whose name is "Disk space — FileCat")','set observedElements to entire contents of candidate')
    receipt=script(menu+'set selected of candidate to true'+chr(10)+'return (rawMatches as text) & tab & (matches as text)');result.setdefault('menuTargetReceipts',[]).append(receipt.strip());script(find('AXPopUpButton',name)+'if value of attribute '+literal('AXExpanded')+' of candidate is true then perform action '+literal('AXPress')+' of candidate');result['events'].append('Selected '+name+' = '+value)
try:
    until=time.monotonic()+15
    while time.monotonic()<until:
        if process.poll() is not None:raise RuntimeError('App exited before native startup')
        runs=list((app/'native-validation').glob('dsa-*'))
        if len(runs)==1 and (runs[0]/'report.json').exists():run=runs[0];break
        time.sleep(.05)
    assert run is not None
    script(front())
    invoke('Start analysis');wait(lambda:observation().get('Phase')==5 and bool(rows()),30)
    choose('Chart view','Table')
    script(first_row()+'set selected of candidate to true'+chr(10)+'set focused of candidate to true')
    wait(lambda:observation().get('focus')=='n:1');enter()
    wait(lambda:len(observation().get('breadcrumbs',[]))==2 and len(rows())>3)
    choose('Analysis table sort','Name');choose('Analysis table sort direction','Ascending')
    wait(lambda:observation().get('tableDescending') is False and rows()[0]['Name']=='.hidden.bin')
    script(first_row()+'set selected of candidate to true'+chr(10)+'set focused of candidate to true')
    marked=rows()[0]['Key'];wait(lambda:observation().get('focus')==marked)
    script(front()+'key code 49');wait(lambda:observation().get('marked')==1)
    result['markedKey']=marked
    script(front()+'key code 119 using {shift down}')
    expected=sum(row['Key'].startswith('n:') for row in rows())
    wait(lambda:observation().get('focus')==rows()[-1]['Key'] and observation().get('marked')==expected)
    assert observation()['basket']==0
    result['capturedRangeRealRows']=expected;snapshot('range-all')
    script(front()+'key code 115 using {shift down}')
    wait(lambda:observation().get('focus')==marked and observation().get('marked')==1)
    assert observation()['basket']==0
    result['nativeCapturedRangeVerified']=True;snapshot('range-shrunk')
    result['state']='passed-native-range-subset'
except Exception as error:
    import traceback
    result['state']='native-navigation-failed';result['error']=str(error);result['traceback']=traceback.format_exc()
    try:snapshot('failure')
    except Exception as captureError:result['failureCaptureError']=str(captureError)
finally:
    if run is not None:(run/'close.request').touch()
    try:process.wait(timeout=8)
    except subprocess.TimeoutExpired:result['cleanupTimeout']=True
    result['ownedProcessExited']=process.poll() is not None
    if run is not None and (run/'report.json').exists():
        try:result['harnessReport']=json.loads((run/'report.json').read_text())
        except json.JSONDecodeError:pass
    (base/'native-control-result.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result))
