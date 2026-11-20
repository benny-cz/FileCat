import hashlib, os, pathlib, subprocess
base=pathlib.Path('/home/benny/FileCatValidation/dsa-20261105-r60-chart')
def gate():
    if subprocess.run(['pgrep','-x','FileCat([.].*)?'],stdout=subprocess.DEVNULL).returncode==0 or subprocess.run(['pgrep','-f',r'^[^ ]*dotnet +.*FileCat'],stdout=subprocess.DEVNULL).returncode==0:
        raise SystemExit('BUSY: postpone desktop test')
gate()
archive=base/'app.tgz'
if hashlib.sha256(archive.read_bytes()).hexdigest()!='d3ae3fe454e2d9bdc540b045132a280c16bc98ca8c33d3f0ab49642e7bcc2633':
    raise SystemExit('Desktop archive hash mismatch')
app=base/'app'; app.mkdir(mode=0o700,exist_ok=True)
subprocess.run(['tar','-xzf',str(archive),'-C',str(app)],check=True)
(app/'FileCat').chmod(0o700)
auth='/run/user/1000/.mutter-Xwaylandauth.2HB3X3'
if not pathlib.Path(auth).is_file(): raise SystemExit('X11 authentication path changed; inspect current session')
env=dict(os.environ,DISPLAY=':0',XAUTHORITY=auth,XDG_RUNTIME_DIR='/run/user/1000',DBUS_SESSION_BUS_ADDRESS='unix:path=/run/user/1000/bus',DOTNET_ROOT='/home/benny/.dotnet')
gate()
with (base/'desktop.log').open('wb') as log:
    process=subprocess.Popen(['./FileCat'],cwd=app,env=env,stdin=subprocess.DEVNULL,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
(app/'owned.pid').write_text(str(process.pid)+'\n')
print('OWNED_PID='+str(process.pid))
for name in ['FileCat.dll','DiskSpaceHarness.dll']:
    print(name+' SHA256='+hashlib.sha256((app/name).read_bytes()).hexdigest())

import time,json,gi,runpy
os.environ.update(env)
base=app;pid=process.pid;run=None
result={'purpose':'Native Ubuntu table ordering/breadcrumb/pointer/keyboard subset; no full qualification','processId':pid,'events':[]}
gi.require_version('Atspi','2.0')
from gi.repository import Atspi,GLib

def gate():
    observed=subprocess.run(['pgrep','-x','FileCat([.].*)?'],text=True,capture_output=True)
    assert observed.returncode==0 and observed.stdout.strip()==str(pid),'Owned app exited or another tester appeared'
    assert subprocess.run(['pgrep','-f',r'^[^ ]*dotnet +.*FileCat'],stdout=subprocess.DEVNULL).returncode!=0,'Another test runner appeared'
def report():return json.loads((run/'report.json').read_text())
def nodes():
    desktop=Atspi.get_desktop(0);stack=[desktop.get_child_at_index(i) for i in range(desktop.get_child_count()) if desktop.get_child_at_index(i).get_process_id()==pid];output=[];seen=set()
    while stack and len(output)<2000:
        node=stack.pop()
        if node in seen:continue
        seen.add(node);output.append(node)
        stack.extend(node.get_child_at_index(i) for i in range(min(node.get_child_count(),200)))
    return output
def matching(name,roles):return [n for n in nodes() if n.get_name()==name and n.get_role_name() in roles and n.get_state_set().contains(Atspi.StateType.SHOWING)]
def wait_for(check,seconds=8):
    until=time.monotonic()+seconds
    while time.monotonic()<until:
        gate()
        try:
            value=check()
            if value:return value
        except (FileNotFoundError,json.JSONDecodeError,GLib.Error):pass
        time.sleep(.05)
    raise RuntimeError('Native tab observation timed out')
def invoke(name):
    gate();found=matching(name,('push button',));assert len(found)==1,(name,len(found))
    assert found[0].get_action_iface().do_action(0)
    result['events'].append('Invoked '+name)
def one_header(name):
    values=matching(name,('page tab','radio button'));assert len(values)==1,(name,[(n.get_name(),n.get_role_name()) for n in nodes() if name==n.get_name()]);return values[0]
def rectangle(node):
    value=node.get_component_iface().get_extents(Atspi.CoordType.SCREEN)
    return dict(x=value.x,y=value.y,width=value.width,height=value.height)
def active():
    gate();wid=subprocess.check_output(['xdotool','getactivewindow'],text=True).strip()
    assert subprocess.check_output(['xdotool','getwindowpid',wid],text=True).strip()==str(pid)
def click(node):
    active();r=rectangle(node);assert r['width']>8 and r['height']>8
    subprocess.run(['xdotool','mousemove','--sync',str(r['x']+4),str(r['y']+4),'click','1'],check=True)
def snapshot(label):
    gate();subprocess.run(['import','-window',str(wid),str(base/('native-'+label+'.png'))],check=True,timeout=10)
    data=[dict(name=n.get_name(),role=n.get_role_name(),selected=n.get_state_set().contains(Atspi.StateType.SELECTED),showing=n.get_state_set().contains(Atspi.StateType.SHOWING)) for n in nodes()]
    (base/('a11y-'+label+'.json')).write_text(json.dumps(data,indent=2)+'\n')
def observation():return report().get('lastObservation') or {}
def rows():return observation().get('tableRows',[])
def row_nodes():return matching_rows()
def matching_rows():return [n for n in nodes() if n.get_role_name()=='list item' and n.get_state_set().contains(Atspi.StateType.SHOWING)]
def key(value):
    active();subprocess.run(['xdotool','key','--clearmodifiers',value],check=True)
def center_click(node):
    active();r=rectangle(node);assert r['width']>10 and r['height']>10,r
    subprocess.run(['xdotool','mousemove','--sync',str(r['x']+min(80,r['width']//2)),str(r['y']+r['height']//2),'click','1'],check=True)
def choose(name,value):
    found=matching(name,('combo box',));assert len(found)==1,(name,len(found))
    center_click(found[0]);wait_for(lambda:len(matching(value,('list item',)))==1)
    center_click(matching(value,('list item',))[0]);result['events'].append('Selected '+name+' = '+value)
try:
    until=time.monotonic()+15
    while time.monotonic()<until:
        runs=list((base/'native-validation').glob('dsa-*'))
        if len(runs)==1 and (runs[0]/'report.json').exists():run=runs[0];break
        if process.poll() is not None:raise RuntimeError('App exited before native startup')
        time.sleep(.05)
    assert run is not None
    ids=subprocess.check_output(['xdotool','search','--onlyvisible','--pid',str(pid)],text=True).split()
    windows=[value for value in ids if subprocess.check_output(['xdotool','getwindowname',value],text=True).strip()=='Disk space — FileCat'];assert len(windows)==1;wid=windows[0]
    subprocess.run(['xdotool','windowactivate','--sync',wid],check=True,timeout=5)
    invoke('Start analysis');wait_for(lambda:observation().get('Phase')==5 and bool(rows()),30);time.sleep(.5)
    snapshot('finished-overview')
    wait_for(lambda:len(row_nodes())==1)
    center_click(row_nodes()[0]);key('Return')
    wait_for(lambda:len(observation().get('breadcrumbs',[]))==2 and len(rows())>3)
    result['nativePointerAndEnterDrilledRoot']=True
    choose('Analysis table sort','Name');choose('Analysis table sort direction','Ascending')
    wait_for(lambda:observation().get('tableSort')==1 and observation().get('tableDescending') is False and rows()[0]['Name']=='.hidden.bin')
    result['ascendingNames']=[row['Name'] for row in rows()]
    candidates=row_nodes();candidates.sort(key=lambda n:rectangle(n)['y']);assert candidates
    center_click(candidates[0]);key('space');wait_for(lambda:observation().get('marked')==1)
    marked=observation()['focus'];result['markedKey']=marked
    choose('Analysis table sort direction','Descending')
    wait_for(lambda:observation().get('tableDescending') is True and rows()[0]['Name']=='zero.bin')
    assert observation()['marked']==1 and observation()['focus']==marked
    result['descendingNames']=[row['Name'] for row in rows()]
    result['nativeSortPreservedFocusAndMark']=True;snapshot('sorted-table')
    invoke('Go to captured location: All locations');wait_for(lambda:len(observation().get('breadcrumbs',[]))==1 and len(rows())==1)
    assert observation()['marked']==1 and observation()['focus']==marked
    invoke('Back');wait_for(lambda:len(observation().get('breadcrumbs',[]))==2 and len(rows())>3)
    assert observation()['marked']==1 and observation()['focus']==marked
    result['nativeBreadcrumbAndBackPreservedMark']=True;snapshot('breadcrumb-back')
    result['state']='passed-native-navigation-subset'
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
    (base.parent/'native-control-result.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result))
