import argparse,hashlib,json,os,pathlib,platform,subprocess,time
p=argparse.ArgumentParser();p.add_argument('base');p.add_argument('sha256');a=p.parse_args();base=pathlib.Path(a.base);app=base/'app'
def gate():
    if subprocess.run(['pgrep','-x','FileCat([.].*)?'],stdout=subprocess.DEVNULL).returncode==0 or subprocess.run(['pgrep','-f',r'^[^ ]*dotnet +.*FileCat'],stdout=subprocess.DEVNULL).returncode==0:
        raise SystemExit('BUSY: postpone this machine')
gate();assert hashlib.sha256((base/'app.tgz').read_bytes()).hexdigest()==a.sha256
assert not app.exists(),'Use a fresh scale stage'
app.mkdir(mode=0o700);subprocess.run(['tar','-xzf',str(base/'app.tgz'),'-C',str(app)],check=True)
result={'purpose':'Native typed path and lineage storage; not full analysis scale qualification','archiveSha256':a.sha256,'platform':platform.platform(),'architecture':platform.machine(),'runs':[]}
result['hashes']={n:hashlib.sha256((app/n).read_bytes()).hexdigest() for n in ['FileCat.Core.dll','FileCat.CoreCrashHarness.dll','FileCat.Platform.Windows.dll']}
for count in [1_000_000,10_000_000]:
    gate();parent=base/('records-'+str(count));parent.mkdir(mode=0o700);record={'records':count};process=None
    try:
        with (base/(str(count)+'.out')).open('wb') as out,(base/(str(count)+'.err')).open('wb') as err:
            process=subprocess.Popen([str(pathlib.Path.home()/'.dotnet/dotnet'),str(app/'FileCat.CoreCrashHarness.dll'),'--analysis-scratch-paths',str(count),str(parent)],cwd=app,stdin=subprocess.DEVNULL,stdout=out,stderr=err)
            record['processId']=process.pid
            (base/'state.json').write_text(json.dumps({'state':'running','records':count,'ownedPid':process.pid})+'\n')
            try:process.wait(timeout=900)
            except subprocess.TimeoutExpired:
                process.terminate();process.wait(timeout=15);record['deadlineClosed']=True
        record.update(exitCode=process.returncode,ownedProcessExited=process.poll() is not None)
        lines=(base/(str(count)+'.out')).read_text().splitlines()
        if lines:record['measurement']=json.loads(lines[-1])
        assert record['exitCode']==0 and record.get('measurement',{}).get('passed') is True,'Native storage scale failed: '+(base/(str(count)+'.err')).read_text()[:2000]
        assert record['measurement']['records']==count and record['measurement']['verifiedEveryRecord'] and record['measurement']['remainingQuotaBytes']==0
        print('PASS '+str(count)+' owned process exited; write='+str(record['measurement']['writeSeconds'])+' read='+str(record['measurement']['readSeconds']),flush=True)
    except BaseException as e:
        record['error']=str(e);raise
    finally:
        if process is not None and process.poll() is None:
            process.terminate();process.wait(timeout=15);record['forcedTermination']=True
        result['runs'].append(record);(base/'result.json').write_text(json.dumps(result,indent=2)+'\n')
    gate()
result['passed']=True;(base/'result.json').write_text(json.dumps(result,indent=2)+'\n');(base/'state.json').write_text('{"state":"finished","passed":true}\n')
