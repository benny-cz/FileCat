"""Publish once, after all original r1164 guest controllers finish successfully."""
import datetime
import hashlib
import importlib.util
import io
import json
import pathlib
import tarfile
import xml.etree.ElementTree as ET

root = pathlib.Path.cwd(); temp = root / '.tmp'; out = root / 'docs/validation/disk-space'
destination = out / 'source-manifest-r1164-indexed-comparison-persistence.json'
assert not destination.exists(), 'Completed evidence is immutable'
spec = importlib.util.spec_from_file_location('evidence', out/'drivers/native_batch_evidence.py')
e = importlib.util.module_from_spec(spec); spec.loader.exec_module(e)
proof = e.read(temp/'test-fix-proof-r1164.json')
assert proof['baseline'] == proof['productSourceBaseline'] == 'r1163' and proof['scope'] == 'OldProductControl'
allowed = {'render/FileCat.Rendering.Tests.dll', 'render/FileCat.Rendering.Tests.pdb'}
source_sets = {}; content = {}
for revision in ('r1158','r1160','r1161','r1163','r1164'):
    suffix = 'saved-state' if int(revision[1:]) >= 1163 else 'persistence'
    rows = e.read(temp/f'source-candidate-{revision}.json'); expected = {r['path']:r['sha256'] for r in rows}
    with tarfile.open(temp/f'source-{revision}-indexed-comparison-{suffix}.tgz') as a:
        members = a.getmembers(); assert len(members) == len(expected) and sum(m.size for m in members) <= 64*1024*1024
        assert {m.name for m in members} == set(expected)
        data = {}
        for m in members:
            p=pathlib.PurePosixPath(m.name); assert m.isfile() and not p.is_absolute() and '..' not in p.parts and ':' not in m.name
            b=a.extractfile(m).read(); assert hashlib.sha256(b).hexdigest()==expected[m.name];data[m.name]=b
    source_sets[revision]=expected;content[revision]=data
assert source_sets['r1163'].keys()==source_sets['r1164'].keys()
assert {p for p in source_sets['r1163'] if source_sets['r1163'][p]!=source_sets['r1164'][p]} == {'tests/FileCat.Rendering.Tests/AnalysisRenderingTests.IndexedComparisonSavedState.cs'}
archives=[]
for tag in ('windows','mac','linux'):
    old=temp/f'batch-r1163-{tag}';new=temp/f'batch-r1164-{tag}'
    before={p.relative_to(old).as_posix():e.sha(p) for p in old.rglob('*') if p.is_file()}
    after={p.relative_to(new).as_posix():e.sha(p) for p in new.rglob('*') if p.is_file()}
    assert before.keys()==after.keys() and {p for p in before if before[p]!=after[p]}==allowed
    p=next(p for p in proof['platforms'] if p['platform']==tag);assert set(p['changed'])==allowed
    archive=temp/f'dsa-batch-{tag}-r1164.tgz';assert e.sha(archive)==p['sha256']
    archives.append({'platform':tag,'path':str(archive),'sha256':p['sha256'],'bytes':archive.stat().st_size})

# This is a derived packing receipt for exact frozen-product replay. It does not
# claim the helper build rebuilt the product or repacked every guest dependency.
packing={'revision':'r1164','mode':'verified-frozen-product-replay','productSourceBaseline':'r1163',
         'fullFreshBuild':False,'buildLog':'.tmp/indexed-comparison-saved-state-fixture-build-r1164.txt',
         'sourceManifest':'.tmp/source-candidate-r1164.json','archiveCount':3,'archives':archives}
(temp/'pack-timing-r1164.json').write_text(json.dumps(packing,indent=2)+'\n')
app={tag:[{'path':p.relative_to(temp/f'batch-r1164-{tag}').as_posix(),'sha256':e.sha(p)}
          for p in sorted((temp/f'batch-r1164-{tag}/render').glob('FileCat.App.Tests.*'))] for tag in ('windows','mac','linux')}
assert all(app.values());(temp/'app-settings-inputs-r1164.json').write_text(json.dumps(app,indent=2)+'\n')
expected={
 'windows':{'preview':(15,0),'core':(1980,48),'native':(75,0),'render':(768,1),'appsettings':(36,1)},
 'mac':{'preview':(15,0),'core':(1962,5),'render':(768,0),'appsettings':(36,0)},
 'ubuntu':{'preview':(15,0),'core':(1962,5),'render':(768,0),'appsettings':(36,0)}}
prefixes=('NativeIndexedComparisonPersistenceControls','NativeIndexedComparisonExternalOpenRefuses','NativeIndexedComparisonChartKeyboard',
          'NativeIndexedComparisonSavedState','NativeIndexedComparisonUnpublishedRestore')
skip='FileCat.Core.Tests.ZipListingRevisionTests.Rows_appear_without_reading_the_whole_archive_and_a_failed_read_is_tried_again'
windows_post=e.read(temp/'windows-batch-r1164-post.json')
windows_idle=e.read(temp/'windows-pre-r1165launch.json')
assert windows_post['filecatOrDotnet']==['pid=2704, owner=, cmd=dotnet.exe']
assert not windows_idle['filecatOrDotnet'] and windows_idle['observedUtc']>windows_post['observedUtc']
# This exact exception preserves an unidentified process, not an ownership
# assertion. It was left alone and a later global inventory was empty before
# the next admission. Never rewrite the original collection observation.
record,files,runners,receipts=e.qualify(root,'r1164',expected,prefixes,{'preview':14,'render':14},build_projects=1,
 source_snapshot='.tmp/source-r1164-indexed-comparison-saved-state.tgz',additional_skips={p+'/core':[skip] for p in ('mac','ubuntu')},
 foreign_post_processes={'windows':windows_post['filecatOrDotnet']})
record['unidentifiedPostCollectionProcess']={'originalObservation':windows_post,'laterEmptyInventory':windows_idle,
 'ownership':'unidentified; never stopped by this task','meaning':'All FileCat/dotnet instances were gone before the next batch admission.'}
files.add(temp/'windows-pre-r1165launch.json')
task=json.loads(receipts['windows']['task-outcome.json'].decode('utf-8-sig'))
environment=json.loads(receipts['windows']['environment.json'].decode('utf-8-sig'))
assert task['terminalExit']==0 and task['logonType']=='Interactive' and task['runLevel']=='Limited'
assert environment['session']>0 and environment['user'].lower().endswith('\\filecatdsa_r2')
e.assert_clean_build(temp/'indexed-comparison-saved-state-build-r1163.txt',4)
record['compiledProductReplay']=proof
record['earlierCohorts']={};record['developmentFailures']={}
for platform in ('windows','mac','ubuntu'):
    original=temp/f'{platform}-batch-r1164-original-run.json';assert e.read(original)['terminalExit']==0;files.add(original)
    a=e.assembly(receipts[platform],'smoke','r1164');assert tuple(a.get(k) for k in ('total','passed','failed','errors'))==('17','17','0','0')
    a=e.assembly(receipts[platform],'core','r1164');new=[t for t in a.findall('.//test') if t.get('method','').startswith('IndexedComparisonLocate')]
    assert len(new)==17 and all(t.get('result')=='Pass' for t in new)
    for revision in ('r1158','r1160','r1161'):
        path=temp/f'{platform}-batch-{revision}-results.tgz';result=e.reports(path)
        state=result['state.txt'].decode('utf-8-sig').strip()
        failures=[]
        for name,data in result.items():
            if name.endswith('.xml'):
                a=ET.fromstring(data).find('assembly');assert a.get('errors')=='0'
                failures.extend(t.get('name') for t in a.findall('.//test') if t.get('result')=='Fail')
        if revision=='r1161': assert state=='FINISHED exit=0' and not failures
        else: assert state=='FINISHED exit=1 phase=preview' and failures
        record['earlierCohorts'][platform+'/'+revision]={'state':state,'failedCases':failures}
        files.update({path,temp/f'{platform}-batch-{revision}-original-run.json',temp/f'{platform}-batch-{revision}-post.json'})
        assert not e.read(temp/f'{platform}-batch-{revision}-post.json')['filecatOrDotnet']
        tag='linux' if platform=='ubuntu' else platform;runners.add(temp/f'batch-{tag}-{revision}.{"ps1" if platform=="windows" else "sh"}')
    with tarfile.open(temp/f'{platform}-batch-r1164-results.tgz') as archive:
        exported={pathlib.PurePosixPath(m.name).name:hashlib.sha256(archive.extractfile(m).read()).hexdigest()
                  for m in archive if m.isfile() and 'indexed-comparison-output-artifacts/' in m.name}
    assert len(exported)==32;record.setdefault('retainedOutputHashes',{})[platform]=exported
failed=e.reports(temp/'mac-batch-r1163-results.tgz');a=e.assembly(failed,'preview','r1163')
assert a.get('total')=='15' and a.get('failed')=='1' and a.get('errors')=='0'
record['developmentFailures']['mac/r1163']={'state':failed['state.txt'].decode('utf-8-sig').strip(),
 'failedCases':[t.get('name') for t in a.findall('.//test') if t.get('result')=='Fail'],
 'reason':'Fixture opened two unnecessary graphs to read a receipt ID and correctly hit the native-handle cap; it now reads only the saved header.'}
files.update({temp/'mac-batch-r1163-results.tgz',temp/'mac-batch-r1163-original-run.json',temp/'mac-batch-r1163-post.json',
              temp/'indexed-comparison-saved-state-build-r1163.txt',temp/'test-fix-proof-r1164.json',temp/'pack-timing-r1163.json'})
runners.add(temp/'batch-mac-r1163.sh');runners.add(temp/'collect-comparison-outputs-r1150.ps1')
runners.add(temp/'prepare-test-fix-batch-r1164-original.ps1')
delta=temp/'earlier-source-deltas-r1164.tgz';assert not delta.exists()
with tarfile.open(delta,'w:gz') as a:
    for revision,data in content.items():
        if revision=='r1164':continue
        for name,b in data.items():
            if content['r1164'].get(name)==b:continue
            m=tarfile.TarInfo(revision+'/'+name);m.size=len(b);a.addfile(m,io.BytesIO(b))
files.add(delta)
for revision in source_sets: files.add(temp/f'source-candidate-{revision}.json')
record['qualification']={'newCoreNavigationCases':17,'newNativeFilesystemHeadlessUiCases':14,
 'indexedSaveOpenExternalImportAndBundleExportWired':True,'exactAppliedMetricQueryCohortOrderChartSideScopeFocus':True,
 'boundedCrossPageChartAndAliasNavigation':True,'namedSavedStateAndRemovalInvalidation':True,
 'unpublishedImportCancellationAndFailureCleanup':True,'taskOwnedProcessesExited':True,
 'fullLargeResourcePhysicalInputA11yPackageMatrixQualified':False,'publicFeatureRegistered':False,'finalIndependentReviewPerformed':False}
record['earlierSourceReconstruction']={'base':'source-r1164-indexed-comparison-saved-state.tgz','deltas':delta.name}
record['originalPreparationDriver']='docs/validation/disk-space/drivers/prepare-test-fix-batch-r1164-original.ps1'
helpers=('native_batch_evidence.py','prepare-test-fix-batch.ps1','deploy-batch.ps1','preflight-indexed-r732.ps1','finalize-indexed-comparison-persistence-r1164.py')
e.retain(root,record,files,runners,helpers)
snapshot=out/'exact-receipt-text-r1164-indexed-comparison-persistence.tgz';assert not snapshot.exists()
with tarfile.open(snapshot,'w:gz') as a:
    for row in record['artifacts']+record['drivers']:
        if not row['path'].endswith('.tgz'):
            p=root/row['path'];assert e.sha(p)==row['sha256'];a.add(p,arcname=row['path'],recursive=False)
record['artifacts'].append({'path':snapshot.relative_to(root).as_posix(),'sha256':e.sha(snapshot)})
destination.write_text(json.dumps(record,indent=2)+'\n')
print('Verified immutable all-three indexed comparison persistence/navigation:',destination)
