import datetime,json,subprocess
pids=set()
for args in [['pgrep','-x','FileCat([.].*)?'],['pgrep','-f','^[^ ]*dotnet +.*FileCat']]:
 p=subprocess.run(args,text=True,capture_output=True)
 if p.returncode not in (0,1):raise RuntimeError(p.stderr)
 pids.update(p.stdout.split())
rows=[]
for pid in sorted(pids,key=int):
 p=subprocess.run(['ps','-p',pid,'-o','pid=,ppid=,args='],text=True,capture_output=True)
 if p.returncode not in (0,1):raise RuntimeError(p.stderr)
 rows.extend(p.stdout.splitlines())
owned=[row for row in rows if 'dsa-20261105-r479-core' in row or 'dsa-20261105-r480-render' in row or 'core-all-r479' in row or 'render-r480' in row]
print(json.dumps({'observedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'exitCode':0,'processCommands':rows,'ownedCoreOrRenderCommands':owned,'otherProcessesPreserved':True},indent=2))
