"""Validate notice identity admission with pinned publish metadata; no package is executed."""
from pathlib import Path
import argparse,hashlib,json,os,subprocess
from datetime import datetime,timezone

parser=argparse.ArgumentParser()
parser.add_argument('--evidence-dir',required=True,type=Path)
parser.add_argument('--repo',type=Path,default=Path(__file__).resolve().parents[2])
args=parser.parse_args();repo=args.repo.resolve();root=args.evidence_dir.resolve();root.mkdir(parents=True)
def sha(p):
    with p.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def capture(command,folder,timeout):
    with (folder/'stdout.txt').open('xb') as so,(folder/'stderr.txt').open('xb') as se:
        return subprocess.run(command,cwd=repo,stdout=so,stderr=se,timeout=timeout,creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0).returncode
fixtureRoot=repo/'eng/validation/fixtures/dependency-notices';fixtureIndex=json.loads((fixtureRoot/'index.json').read_text())
assert fixtureIndex['SchemaVersion']==1 and len(fixtureIndex['Manifests'])==7
inputs=[repo/'eng/DependencyNotices/Program.cs',repo/'eng/DependencyNotices/DependencyNotices.csproj',repo/'eng/DependencyNotices/packages.lock.json',repo/'src/FileCat.App/packages.lock.json',repo/'Directory.Build.props',repo/'Directory.Build.targets',repo/'Directory.Packages.props',repo/'global.json',fixtureRoot/'index.json']
inputs += sorted(p for p in (repo/'licenses/dependencies').rglob('*') if p.is_file())
inputs += [fixtureRoot/v['Path'] for v in fixtureIndex['Manifests']]
inputPins=[dict(Path=p.relative_to(repo).as_posix(),Bytes=p.stat().st_size,SHA256=sha(p)) for p in inputs]
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip()
dirty=bool(subprocess.check_output(['git','status','--porcelain'],cwd=repo,text=True).strip())
build=root/'build';build.mkdir()
command=['dotnet','build','eng/DependencyNotices/DependencyNotices.csproj','-c','Release','--artifacts-path',str(root/'artifacts'),'-p:RestoreLockedMode=true','-p:SourceRevisionId='+head,'--nologo']
buildCommand=command.copy();code=capture(command,build,120)
if code:raise SystemExit('Notice validation tool build failed; raw output retained.')
tool=root/'artifacts/bin/DependencyNotices/release/DependencyNotices.dll';assert tool.is_file()
snapshot={p.relative_to(repo/'licenses/dependencies').as_posix():sha(p) for p in (repo/'licenses/dependencies').rglob('*') if p.is_file()}
cases=[]
behaviors=['unchanged','unknown-package','wrong-package-version','wrong-package-hash','missing-package-hash','case-duplicate-package','missing-package-library','unknown-runtime-target','changed-package-kind']
for fixture in fixtureIndex['Manifests']:
    original=fixtureRoot/fixture['Path'];assert sha(original)==fixture['SHA256']
    data=json.loads(original.read_text());key='Avalonia/12.1.1';assert data['libraries'][key]['type']=='package'
    for behavior in behaviors:
        d=json.loads(json.dumps(data));libs=d['libraries'];target=d['targets'][d['runtimeTarget']['name']]
        if behavior=='unknown-package':libs['Owned.Unknown.Package/0.0.1']=dict(type='package',sha512='sha512-owned invalid hash');target['Owned.Unknown.Package/0.0.1']={}
        elif behavior=='wrong-package-version':libs['Avalonia/0.0.1']=libs.pop(key);target['Avalonia/0.0.1']=target.pop(key)
        elif behavior=='wrong-package-hash':libs[key]['sha512']='sha512-owned invalid hash'
        elif behavior=='missing-package-hash':del libs[key]['sha512']
        elif behavior=='changed-package-kind':libs[key]['type']='reference'
        elif behavior=='case-duplicate-package':libs['avalonia/12.1.1']=libs[key];target['avalonia/12.1.1']=target[key]
        elif behavior=='missing-package-library':del libs[key]
        elif behavior=='unknown-runtime-target':d['runtimeTarget']['name']='owned nonexistent runtime target'
        name=fixture['Name']+'-'+behavior;folder=root/'cases'/name;folder.mkdir(parents=True);payload=folder/'payload';payload.mkdir();manifest=payload/'FileCat.deps.json'
        with manifest.open('x',encoding='utf-8',newline='\n') as f:json.dump(d,f,indent=2)
        output=folder/'output';command=['dotnet',str(tool),str(repo/'licenses/dependencies'),str(repo/'src/FileCat.App/packages.lock.json'),str(payload),str(output)]
        code=capture(command,folder,30)
        outputs={p.relative_to(output).as_posix():sha(p) for p in output.rglob('*') if p.is_file()} if output.exists() else {}
        expected=behavior=='unchanged';passed=(code==0 and output.exists() and outputs==snapshot) if expected else (code!=0 and not output.exists())
        cases.append(dict(Name=name,Fixture=fixture['Name'],Behavior=behavior,ExpectedAllowed=expected,ControlPassed=passed,ExitCode=code,DestinationCreated=output.exists(),ActualOutputPins=outputs,ExpectedOutputPins=snapshot,ManifestSHA256=sha(manifest),OriginalManifestSHA256=fixture['SHA256'],Command=command,StdoutSHA256=sha(folder/'stdout.txt'),StderrSHA256=sha(folder/'stderr.txt')))
assert len(cases)==63 and all(sha(repo/p['Path'])==p['SHA256'] for p in inputPins)
payloadPins=[dict(Path=p.relative_to(root).as_posix(),Bytes=p.stat().st_size,SHA256=sha(p)) for p in sorted((root/'artifacts/bin/DependencyNotices/release').rglob('*')) if p.is_file()]
proof=dict(UTC=datetime.now(timezone.utc).isoformat(),SourceCommit=head,SourceTreeDirty=dirty,InputPinsBeforeAfterVerified=inputPins,ActualPayloadPins=payloadPins,BuildCommand=buildCommand,BuildExitCode=0,Cases=cases,ActualManifestOnlyControls=True,NoOriginalSnapshotOrPublishManifestChanged=True,ActualBinaryCompositionLicenseEligibilityNativeDesktopOrCandidateQualified=False)
with (root/'dependency-notice-manifest-controls.json').open('x',encoding='utf-8',newline='\n') as f:json.dump(proof,f,indent=2)
print(json.dumps(dict(Controls=63,Allowed=7,Refused=56,Passed=sum(c['ControlPassed'] for c in cases),SourceCommit=head,SourceTreeDirty=dirty)))
if not all(c['ControlPassed'] for c in cases):raise SystemExit(1)
