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
appLock=json.loads((repo/'src/FileCat.App/packages.lock.json').read_text())
avaloniaVersion=appLock['dependencies']['net10.0']['Avalonia']['resolved']
targetsText=(repo/'Directory.Build.targets').read_text()
runtimeMarker='<FileCatServicedRuntimeVersion>'
runtimeVersion=targetsText.split(runtimeMarker,1)[1].split('</FileCatServicedRuntimeVersion>',1)[0]
assert runtimeVersion and runtimeVersion.count('.')==2
publicNotices=repo/'THIRD-PARTY-NOTICES.md'
publicNoticeText=publicNotices.read_text()
noticeIndex=json.loads((repo/'licenses/dependencies/index.json').read_text())
def publicRuntimeMatches(text):
    return (text.count(f'pinned .NET {runtimeVersion} runtime pack texts')==1 and
            text.count(f'| .NET runtime and libraries | {runtimeVersion} | MIT |')==1)
assert publicRuntimeMatches(publicNoticeText)
assert not publicRuntimeMatches(publicNoticeText.replace(runtimeVersion,'0.0.0'))
def publicSbomLayoutMatches(text):
    required=(
        '`eng/publish.ps1` writes the Windows',
        '`eng/package-linux.sh` writes the Linux',
        '`eng/package-macos.sh` writes the macOS',
        '`artifacts/sbom-<version>-<rid>-<artifact-kind>.spdx.json`',
    )
    return all(text.count(value)==1 for value in required) and 'sbom-<version>-<rid>.json' not in text
assert publicSbomLayoutMatches(publicNoticeText)
assert not publicSbomLayoutMatches(publicNoticeText.replace('`eng/package-macos.sh` writes the macOS','`eng/package-macos.sh` omits the macOS'))
def ltrFullTextResolved(index):
    matches=[p for p in index['Packages'] if p['ID']=='LTRData.Extensions' and p['Version']=='1.0.23']
    if len(matches)!=1:return False
    package=matches[0];reference=package.get('LicenseReference',{})
    return (package.get('NuGetLicenseDeclaration',{}).get('Text')=='MIT' and
            package.get('NuGetCopyrightDeclaration')=='2011 - 2026, Olof Lagerkvist, LTR Data' and
            package.get('PackageSignatureAuthenticityQualified') is True and
            package.get('FullTextRetained') is True and
            package.get('RetainedNoticePaths')==['spdx/MIT/f715b3660bb164c3d0b81cba9ce94e3c558c4048/MIT.txt'] and
            reference.get('SHA256')=='b05785f9f18e6716bab63424b11454513b9943a222595b70411009202fc592b5' and
            not any(gap.get('ID')=='LTRData.Extensions' for gap in index['KnownFullTextGaps']))
assert ltrFullTextResolved(noticeIndex)
changedIndex=json.loads(json.dumps(noticeIndex));changedIndex['Packages'][next(i for i,p in enumerate(changedIndex['Packages']) if p['ID']=='LTRData.Extensions')]['LicenseReference']['SHA256']='0'*64
assert not ltrFullTextResolved(changedIndex)
inputs=[repo/'eng/DependencyNotices/Program.cs',repo/'eng/DependencyNotices/DependencyNotices.csproj',repo/'eng/DependencyNotices/packages.lock.json',repo/'src/FileCat.App/packages.lock.json',repo/'Directory.Build.props',repo/'Directory.Build.targets',repo/'Directory.Packages.props',repo/'global.json',repo/'THIRD-PARTY-NOTICES.md',fixtureRoot/'index.json']
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
    data=json.loads(original.read_text())
    # The retained publish manifests are historical 12.1.1/runtime-10.0.12 fixtures.
    # Adapt only their dependency identities in memory to test the current frozen
    # notice snapshot; the original fixture bytes and SHA pins remain intact.
    if avaloniaVersion!='12.1.1':
        def currentAvalonia(value):
            if isinstance(value,dict):
                return {(name.replace('/12.1.1','/'+avaloniaVersion) if name.startswith('Avalonia') else name):currentAvalonia(item)
                        for name,item in value.items()}
            if isinstance(value,list):return [currentAvalonia(item) for item in value]
            return avaloniaVersion if value=='12.1.1' else value
        data=currentAvalonia(data)
        for package,entry in appLock['dependencies']['net10.0'].items():
            key=package+'/'+avaloniaVersion
            if package.startswith('Avalonia') and key in data['libraries']:
                data['libraries'][key]['sha512']='sha512-'+entry['contentHash']
    if runtimeVersion!='10.0.12':
        def currentRuntime(value):
            if isinstance(value,dict):
                return {(name.replace('/10.0.12','/'+runtimeVersion) if name.startswith('runtimepack.') else name):currentRuntime(item)
                        for name,item in value.items()}
            if isinstance(value,list):return [currentRuntime(item) for item in value]
            return runtimeVersion if value=='10.0.12' else value
        data=currentRuntime(data)
    key='Avalonia/'+avaloniaVersion;assert data['libraries'][key]['type']=='package'
    for behavior in behaviors:
        d=json.loads(json.dumps(data));libs=d['libraries'];target=d['targets'][d['runtimeTarget']['name']]
        if behavior=='unknown-package':libs['Owned.Unknown.Package/0.0.1']=dict(type='package',sha512='sha512-owned invalid hash');target['Owned.Unknown.Package/0.0.1']={}
        elif behavior=='wrong-package-version':libs['Avalonia/0.0.1']=libs.pop(key);target['Avalonia/0.0.1']=target.pop(key)
        elif behavior=='wrong-package-hash':libs[key]['sha512']='sha512-owned invalid hash'
        elif behavior=='missing-package-hash':del libs[key]['sha512']
        elif behavior=='changed-package-kind':libs[key]['type']='reference'
        elif behavior=='case-duplicate-package':libs['avalonia/'+avaloniaVersion]=libs[key];target['avalonia/'+avaloniaVersion]=target[key]
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
proof=dict(UTC=datetime.now(timezone.utc).isoformat(),SourceCommit=head,SourceTreeDirty=dirty,InputPinsBeforeAfterVerified=inputPins,ActualPayloadPins=payloadPins,BuildCommand=buildCommand,BuildExitCode=0,Cases=cases,PublicNoticeRuntimeVersion=runtimeVersion,PublicNoticeRuntimeMatched=True,ChangedPublicNoticeRefused=True,PublicNoticeSbomLayoutMatched=True,ChangedPublicNoticeSbomLayoutRefused=True,LTRDataExtensionsFullTextResolved=True,ChangedLTRDataExtensionsIdentityRefused=True,ActualManifestOnlyControls=True,NoOriginalSnapshotOrPublishManifestChanged=True,ActualBinaryCompositionLicenseEligibilityNativeDesktopOrCandidateQualified=False)
with (root/'dependency-notice-manifest-controls.json').open('x',encoding='utf-8',newline='\n') as f:json.dump(proof,f,indent=2)
print(json.dumps(dict(Controls=69,Allowed=10,Refused=59,Passed=sum(c['ControlPassed'] for c in cases)+6,SourceCommit=head,SourceTreeDirty=dirty)))
if not all(c['ControlPassed'] for c in cases):raise SystemExit(1)
