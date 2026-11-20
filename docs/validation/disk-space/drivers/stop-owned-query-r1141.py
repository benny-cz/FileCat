import datetime,json,os,pathlib,signal,subprocess,sys
base=pathlib.Path(sys.argv[1]); pid=int(sys.argv[2])
assert base.name=='dsa-batch-20261107-r1141' and base.is_absolute()
command=subprocess.check_output(['ps','-p',str(pid),'-o','args='],text=True).strip()
assert 'dotnet FileCat.Core.Tests.dll ' in command and str(base/'smoke-r1141.xml') in command
(base/'owned-stop-r1141.json').write_text(json.dumps(dict(observedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),pid=pid,command=command,reason='Diagnosed retained unexpected query after assertion; exact owned preflight child terminated',signal='SIGTERM'),indent=2)+'\n')
os.kill(pid,signal.SIGTERM)
