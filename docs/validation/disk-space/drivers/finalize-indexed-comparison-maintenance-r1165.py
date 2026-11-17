"""Retain the App UI delta after the three original guest runs have ended."""
import datetime
import hashlib
import importlib.util
import json
import pathlib
import tarfile

root=pathlib.Path.cwd();temp=root/'.tmp';out=root/'docs/validation/disk-space'
destination=out/'source-manifest-r1165-indexed-comparison-maintenance.json'
assert not destination.exists(), 'Completed evidence is immutable'
spec=importlib.util.spec_from_file_location('evidence',out/'drivers/native_batch_evidence.py')
e=importlib.util.module_from_spec(spec);spec.loader.exec_module(e)
proof=e.read(temp/'test-fix-proof-r1165.json')
assert proof['baseline']=='r1164' and proof['scope']=='AppUi' and proof['reuseFrozenDependencies']
assert proof['producerSha256']==e.sha(out/'drivers/prepare-test-fix-batch.ps1')
previous={r['path']:r['sha256'] for r in e.read(temp/'source-candidate-r1164.json')}
current={r['path']:r['sha256'] for r in e.read(temp/'source-candidate-r1165.json')}
assert previous.keys()<=current.keys()
changed={p for p in current if previous.get(p)!=current[p]}
assert changed and all(p.endswith('.cs') and p.startswith(('src/FileCat.App/','tests/FileCat.Rendering.Tests/')) for p in changed)
allowed={'render/FileCat.dll','render/FileCat.pdb','render/FileCat.Rendering.Tests.dll','render/FileCat.Rendering.Tests.pdb'}
archives=[]
for tag in ('windows','mac','linux'):
    old=temp/f'batch-r1164-{tag}';new=temp/f'batch-r1165-{tag}'
    before={p.relative_to(old).as_posix():e.sha(p) for p in old.rglob('*') if p.is_file()}
    after={p.relative_to(new).as_posix():e.sha(p) for p in new.rglob('*') if p.is_file()}
    assert before.keys()==after.keys() and {p for p in before if before[p]!=after[p]}==allowed
    p=next(p for p in proof['platforms'] if p['platform']==tag)
    assert set(p['changed'])==allowed and p['verifiedInputs']==len(after)
    archive=temp/f'dsa-batch-{tag}-r1165.tgz';assert e.sha(archive)==p['sha256']
    archives.append({'platform':tag,'path':str(archive),'sha256':p['sha256'],'bytes':archive.stat().st_size})
packing={'revision':'r1165','mode':'verified-app-ui-with-frozen-dependencies','fullFreshBuild':False,
 'baseline':'r1164','buildLog':'.tmp/indexed-comparison-maintenance-build-r1165.txt',
 'sourceManifest':'.tmp/source-candidate-r1165.json','archiveCount':3,'archives':archives}
(temp/'pack-timing-r1165.json').write_text(json.dumps(packing,indent=2)+'\n')
app={tag:[{'path':p.relative_to(temp/f'batch-r1165-{tag}').as_posix(),'sha256':e.sha(p)}
          for p in sorted((temp/f'batch-r1165-{tag}/render').glob('FileCat.App.Tests.*'))] for tag in ('windows','mac','linux')}
assert all(app.values());(temp/'app-settings-inputs-r1165.json').write_text(json.dumps(app,indent=2)+'\n')
expected={
 'windows':{'preview':(5,0),'core':(1980,48),'native':(75,0),'render':(773,1),'appsettings':(36,1)},
 'mac':{'preview':(5,0),'core':(1962,5),'render':(773,0),'appsettings':(36,0)},
 'ubuntu':{'preview':(5,0),'core':(1962,5),'render':(773,0),'appsettings':(36,0)}}
overlays={platform+'/core':'r1164' for platform in expected};overlays['windows/native']='r1164'
skip='FileCat.Core.Tests.ZipListingRevisionTests.Rows_appear_without_reading_the_whole_archive_and_a_failed_read_is_tried_again'
post=e.read(temp/'windows-batch-r1164-post.json');idle=e.read(temp/'windows-pre-r1165launch.json')
assert post['filecatOrDotnet']==['pid=2704, owner=, cmd=dotnet.exe'] and not idle['filecatOrDotnet']
assert idle['observedUtc']>post['observedUtc']
record,files,runners,receipts=e.qualify(root,'r1165',expected,('NativeIndexedComparisonMaintenance',),5,build_projects=1,
 phase_revisions=overlays,source_snapshot='.tmp/source-r1165-indexed-comparison-maintenance.tgz',
 additional_skips={p+'/core':[skip] for p in ('mac','ubuntu')},foreign_post_processes={'windows/r1164':post['filecatOrDotnet']})
record['frozenDependencyReuse']={'proof':proof,'changedSourcePaths':sorted(changed),
 'meaning':'Only App/rendering-test DLL/PDB pairs changed; Core, native, settings and other dependency bytes are exact r1164 inputs.',
 'reusedCoreNativeQualification':'docs/validation/disk-space/source-manifest-r1164-indexed-comparison-persistence.json'}
negative=e.read(temp/'frozen-dependency-guard-negative-r1165.json')
assert len(negative['controls'])==2 and all(c['refusedBeforeStaging'] for c in negative['controls'])
record['dependencyGuardNegativeControls']=negative
record['unidentifiedEarlierPostProcess']={'originalObservation':post,'laterEmptyInventory':idle,
 'ownership':'unidentified; never stopped by this task; the original observation is preserved.'}
for platform in expected:
    original=temp/f'{platform}-batch-r1165-original-run.json';data=e.read(original)
    assert data['terminalExit']==0 and data['sessionId'];files.add(original)
    with tarfile.open(temp/f'{platform}-batch-r1165-results.tgz') as archive:
        outputs={pathlib.PurePosixPath(m.name).name:hashlib.sha256(archive.extractfile(m).read()).hexdigest()
                 for m in archive if m.isfile() and 'indexed-comparison-output-artifacts/' in m.name}
    assert len(outputs)==32;record.setdefault('retainedOutputHashes',{})[platform]=outputs
task=json.loads(receipts['windows']['task-outcome.json'].decode('utf-8-sig'))
environment=json.loads(receipts['windows']['environment.json'].decode('utf-8-sig'))
assert task['terminalExit']==0 and task['logonType']=='Interactive' and task['runLevel']=='Limited'
assert environment['session']>0 and environment['user'].lower().endswith('\\filecatdsa_r2')
files.update({temp/'test-fix-proof-r1165.json',temp/'source-candidate-r1164.json',
 temp/'frozen-dependency-guard-negative-r1165.json',temp/'windows-pre-r1165launch.json'})
runners.add(temp/'collect-comparison-outputs-r1150.ps1')
record['qualification']={'newNativeFilesystemHeadlessUiCases':5,'indexedPrivateComparisonMaintenanceWired':True,
 'preservingDefaultDangerFocusAndStaleChoiceChecks':True,'maintenanceAvailableWhenSessionCapacityFull':True,
 'canceledReviewActuallyClosesNativeOwner':True,'savedReferenceInvalidationAndSourceBytesPreserved':True,
 'taskOwnedProcessesExited':True,'fullLargeResourcePhysicalInputA11yPackageMatrixQualified':False,
 'publicFeatureRegistered':False,'finalIndependentReviewPerformed':False}
helpers=('native_batch_evidence.py','prepare-test-fix-batch.ps1','deploy-batch.ps1','preflight-indexed-r732.ps1','finalize-indexed-comparison-maintenance-r1165.py')
e.retain(root,record,files,runners,helpers)
snapshot=out/'exact-receipt-text-r1165-indexed-comparison-maintenance.tgz';assert not snapshot.exists()
with tarfile.open(snapshot,'w:gz') as a:
    for row in record['artifacts']+record['drivers']:
        if not row['path'].endswith('.tgz'):
            p=root/row['path'];assert e.sha(p)==row['sha256'];a.add(p,arcname=row['path'],recursive=False)
record['artifacts'].append({'path':snapshot.relative_to(root).as_posix(),'sha256':e.sha(snapshot)})
destination.write_text(json.dumps(record,indent=2)+'\n')
print('Verified immutable all-three indexed comparison maintenance:',destination)
