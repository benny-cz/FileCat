"""Verify and retain actual native engine/export receipts; no host test execution."""
import csv
import datetime
import hashlib
import importlib.util
import io
import itertools
import json
import pathlib
import struct
import subprocess
import tarfile
import xml.etree.ElementTree as ET

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space'
destination = out / 'source-manifest-r1157-indexed-comparison-bundle.json'
assert not destination.exists(), 'Completed evidence is immutable'
spec = importlib.util.spec_from_file_location('evidence', out / 'drivers/native_batch_evidence.py')
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)
files, runners = set(), set()
sources, latest, deltas = {}, {}, {}
revisions = ('r1148', 'r1149', 'r1150', 'r1151', 'r1152', 'r1153', 'r1154', 'r1155', 'r1156', 'r1157')
for revision in reversed(revisions):
    manifest = temp / f'source-candidate-{revision}.json'
    rows = e.read(manifest)
    expected = {row['path']: row['sha256'] for row in rows}
    assert len(expected) == len(rows)
    suffix = 'outputs' if int(revision[1:]) <= 1153 else 'bundle'
    with tarfile.open(temp / f'source-{revision}-indexed-comparison-{suffix}.tgz') as archive:
        members = archive.getmembers()
        assert len(members) == len(rows) and sum(m.size for m in members) <= 64 * 1024 * 1024
        assert {m.name for m in members} == set(expected)
        content = {}
        for m in members:
            name = pathlib.PurePosixPath(m.name)
            assert m.isfile() and not name.is_absolute() and '..' not in name.parts and ':' not in m.name
            data = archive.extractfile(m).read()
            assert hashlib.sha256(data).hexdigest() == expected[m.name]
            if revision == 'r1157': latest[m.name] = data
            elif latest.get(m.name) != data: content[m.name] = data
        if content: deltas[revision] = content
    sources[revision] = expected
    files.add(manifest)
assert {p for p in sources['r1155'] if sources['r1155'][p] != sources['r1156'][p]} == {
    'tests/FileCat.Core.Tests/AnalysisDatasetSummaryTests.IndexedComparisonBundles.cs'}
assert {p for p in sources['r1156'] if sources['r1156'][p] != sources['r1157'][p]} == {
    'tests/FileCat.Rendering.Tests/AnalysisRenderingTests.InventoryDeath.cs'}

inputs = {}
for revision, allowed in [('r1156', {'tests/FileCat.Core.Tests/bin/Debug/net10.0/FileCat.Core.Tests.' + ext for ext in ('dll', 'pdb')}),
                          ('r1157', {'render/FileCat.Rendering.Tests.' + ext for ext in ('dll', 'pdb')})]:
    baseline = 'r1155' if revision == 'r1156' else 'r1156'
    proof = e.read(temp / f'test-fix-proof-{revision}.json')
    assert proof['baseline'] == baseline and proof['scope'] == 'TestOnly'
    for platform in ('windows', 'mac', 'linux'):
        stage = temp / f'batch-{revision}-{platform}'
        before = temp / f'batch-{baseline}-{platform}'
        now = {p.relative_to(stage).as_posix(): e.sha(p) for p in stage.rglob('*') if p.is_file()}
        old = {p.relative_to(before).as_posix(): e.sha(p) for p in before.rglob('*') if p.is_file()}
        assert now.keys() == old.keys() and {p for p in now if now[p] != old[p]} == allowed
        row = next(r for r in proof['platforms'] if r['platform'] == platform)
        assert set(row['changed']) == allowed and row['verifiedInputs'] == len(now)
        assert e.sha(temp / f'dsa-batch-{platform}-{revision}.tgz') == row['sha256']
        inputs[f'{platform}/{revision}'] = now
    files.add(temp / f'test-fix-proof-{revision}.json')

matrix, failures, receipts = {}, {}, {}
def batch(platform, revision, state='FINISHED exit=0'):
    key = platform + '/' + revision
    if key not in receipts:
        path = temp / f'{platform}-batch-{revision}-results.tgz'
        result = e.reports(path)
        assert result['state.txt'].decode('utf-8-sig').strip() == state
        receipts[key] = result; files.add(path)
        tag = 'linux' if platform == 'ubuntu' else platform
        runner = temp / f'batch-{tag}-{revision}.{"ps1" if platform == "windows" else "sh"}'
        if runner.exists(): runners.add(runner)
    return receipts[key]

def phase(platform, revision, name, total, skipped=0, state='FINISHED exit=0'):
    a = e.assembly(batch(platform, revision, state), name, revision)
    assert tuple(int(a.get(k)) for k in ('total', 'passed', 'failed', 'errors', 'skipped')) == (total, total-skipped, 0, 0, skipped)
    skip_names = sorted(t.get('name') for t in a.findall('.//test') if t.get('result') == 'Skip')
    if name in ('core', 'native', 'render', 'appsettings') and total > 36:
        old = ET.parse(out / f'{platform}-batch-r1036-{name}-r1036.xml').getroot().find('assembly')
        expected = sorted(t.get('name') for t in old.findall('.//test') if t.get('result') == 'Skip')
        if name == 'core' and platform != 'windows': expected.append('FileCat.Core.Tests.ZipListingRevisionTests.Rows_appear_without_reading_the_whole_archive_and_a_failed_read_is_tried_again')
        # Focused render25 has no skipped cases; its broad source is checked separately.
        assert skip_names == sorted(expected)
    matrix[f'{platform}/{revision}/{name}'] = {k: a.get(k) for k in ('total','passed','failed','errors','skipped','time')}
    matrix[f'{platform}/{revision}/{name}']['skippedNames'] = skip_names
    return a

for platform in ('windows', 'mac', 'ubuntu'):
    revision = 'r1157' if platform == 'windows' else 'r1156'
    state = 'BUSY phase=appsettings' if platform == 'ubuntu' else 'FINISHED exit=0'
    phase(platform, revision, 'smoke', 17, state=state)
    phase(platform, revision, 'preview', 28 if platform == 'windows' else 3, state=state)
    a = phase(platform, revision, 'core', 1963 if platform == 'windows' else 1945, 48 if platform == 'windows' else 5, state)
    core_new = [t for t in a.findall('.//test') if t.get('method','').startswith(('IndexedComparisonReport', 'IndexedComparisonBundle'))]
    assert len(core_new) == 48 and all(t.get('result') == 'Pass' for t in core_new)
    a = phase(platform, revision, 'render', 754, 1 if platform == 'windows' else 0, state)
    view_new = [t for t in a.findall('.//test') if t.get('method','').startswith(('NativeIndexedComparisonOutputsPreflight','NativeIndexedComparisonReport','NativeIndexedComparisonImages','NativeIndexedComparisonImageGuard','NativeIndexedComparisonBundles'))]
    assert len(view_new) == 41 and all(t.get('result') == 'Pass' for t in view_new)
    if platform == 'windows': phase(platform, revision, 'native', 75)
    else: phase(platform, 'r1157', 'render', 25)
    phase(platform, 'r1156' if platform == 'mac' else 'r1157', 'appsettings', 36, 1 if platform == 'windows' else 0)
    for rev in ({revision, 'r1157'}):
        post = temp / f'{platform}-batch-{rev}-post.json'; assert not e.read(post)['filecatOrDotnet']; files.add(post)
        original = temp / f'{platform}-batch-{rev}-original-run.json'; data = e.read(original)
        assert data['terminalExit'] == (75 if platform == 'ubuntu' and rev == 'r1156' else 0)
        assert data['sessionId']; files.add(original)

artifacts = {}
expected_names = {f'report-{fmt}-{redact}-{entire}.{ext}' for fmt, ext in (('Markdown','md'),('Html','html'),('Csv','csv'))
                  for redact, entire in itertools.product(('False','True'), repeat=2)}
expected_names |= {f'image-{png}-{previous}-{sunburst}-{redact}.{ext}' for png, ext in (('False','svg'),('True','png'))
                   for previous, sunburst, redact in itertools.product(('False','True'), repeat=3)}
expected_names |= {f'bundle-{kind}-{structural}.fcdsc' for kind in ('named','exported') for structural in ('False','True')}
for platform in ('windows','mac','ubuntu'):
    rev = 'r1157' if platform == 'windows' else 'r1156'
    exported = {}
    with tarfile.open(temp / f'{platform}-batch-{rev}-results.tgz') as a:
        for m in a:
            if m.isfile() and 'indexed-comparison-output-artifacts/' in m.name:
                assert m.size <= 8*1024*1024
                name = pathlib.PurePosixPath(m.name).name; assert name not in exported
                exported[name] = a.extractfile(m).read()
    assert exported.keys() == expected_names
    artifacts[platform] = []
    for name,data in sorted(exported.items()):
        if name.endswith('.fcdsc'):
            end = data.index(b'\n')+1; header = json.loads(data[:end]); assert header['kind'] == 'comparison-manifest' and header['schema'] == '1'
            offset = end + int(header['previousBytes']) + int(header['currentBytes'])
            trailer = json.loads(data[offset:]); assert trailer['kind'] == 'comparison-trailer' and trailer['sha256'] == hashlib.sha256(data[:offset]).hexdigest()
        elif name.endswith('.png'): assert data[:8] == b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',data[16:24]) == (960,720)
        else:
            text = data.decode('utf-8-sig'); assert '\u202e' not in text
            if name.endswith('.csv'):
                rows = list(csv.DictReader(io.StringIO(text))); assert rows[0]['row_kind'] == 'metadata'
                assert len(rows)-1 == int(rows[0]['declared_item_rows']) and all(r['spreadsheet_safe']=='true' for r in rows)
            elif name.endswith('.svg'):
                doc = ET.fromstring(text)
                assert all(n.tag.rsplit('}',1)[-1] in {'svg','rect','path','defs','clipPath','text'} for n in doc.iter())
                assert not any(k.lower().startswith('on') or k.lower().endswith('href') for n in doc.iter() for k in n.attrib)
                assert 'Previous UTC' in text and 'Current UTC' in text
            elif name.endswith('.html'): assert '<script' not in text.lower() and ' href=' not in text.lower() and ' src=' not in text.lower()
            redact = name.split('-')[2]=='True' if name.startswith('report-') else name.rsplit('-',1)[1].split('.')[0]=='True'
            if redact:
                plain = text.replace('\\-','-').replace('\\.','.').replace('\\_','_')
                assert 'match-target' not in plain and 'alias.jpg' not in plain and 'indexed-window-' not in plain
        artifacts[platform].append({'name':name,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()})

for platform in ('windows','mac','ubuntu'):
    for revision,state in [('r1148','FINISHED exit=1 phase=smoke'),('r1149','FINISHED exit=1 phase=preview'),('r1150','FINISHED exit=1'),('r1155','FINISHED exit=1 phase=smoke')]:
        if platform == 'windows' and revision == 'r1155': continue  # Never deployed there.
        result = batch(platform,revision,state)
        failed = []
        for name,data in result.items():
            if name.endswith('.xml'):
                a = ET.fromstring(data).find('assembly'); assert a.get('errors') == '0'
                failed.extend({'case':t.get('name'),'message':t.findtext('failure/message','')[:2000]} for t in a.findall('.//test') if t.get('result')=='Fail')
        assert len(failed) == {'r1148':6,'r1149':1,'r1150':2,'r1155':8}[revision]
        failures[platform+'/'+revision] = {'state':state,'failures':failed}
    phase(platform,'r1151','render',12)
for revision,state in [('r1153','RUNNING phase=verify runnerPid=4456'),('r1153c','FINISHED exit=1')]:
    result = e.reports(temp / f'windows-batch-{revision}-results.tgz')
    failures['windows/'+revision] = {'state':result['state.txt'].decode('utf-8-sig').strip()}
    if revision == 'r1153': assert not any(n.endswith('.xml') for n in result)
    else:
        a = e.assembly(result,'render',revision); assert a.get('failed') == '12'
        failed = [t for t in a.findall('.//test') if t.get('result')=='Fail']
        assert all(t.get('method')=='AbruptOwnedInventoryDeathPreservesNamedBytesAndAllowsFreshExplicitRecovery' for t in failed)
        failures['windows/'+revision]['failedCases'] = [t.get('name') for t in failed]
    files.add(temp / f'windows-batch-{revision}-results.tgz')
    files.add(temp / f'windows-batch-{revision}-original-run.json')
    files.add(temp / f'windows-batch-{revision}-post.json')
    runners.add(temp / f'batch-windows-{revision}.ps1')

windows = receipts['windows/r1157']
task = json.loads(windows['task-outcome.json'].decode('utf-8-sig')); environment = json.loads(windows['environment.json'].decode('utf-8-sig'))
assert task['terminalExit']==0 and task['logonType']=='Interactive' and task['runLevel']=='Limited'
assert environment['session']>0 and environment['user'].lower().endswith('\\filecatdsa_r2')
builds = {'indexed-comparison-outputs-build-r1148.txt':4,'indexed-comparison-outputs-fixture-build-r1149.txt':1,
          'indexed-comparison-outputs-view-build-r1150.txt':1,'indexed-comparison-outputs-view-build-r1151.txt':1,
          'indexed-comparison-bundle-build-r1155.txt':4,'indexed-comparison-bundle-fixture-build-r1156.txt':1,
          'indexed-comparison-bundle-recovery-fixture-build-r1157.txt':1}
for name,count in builds.items(): e.assert_clean_build(temp/name,count); files.add(temp/name)
files.update({temp/'source-r1157-indexed-comparison-bundle.tgz',temp/'runner-scopes-r1157.json',temp/'pack-timing-r1155.json',
              temp/'windows-batch-r1150-derived-provenance.json',temp/'windows-runtime-restore-r1151.json',
              temp/'windows-account-restore-r1151.json',temp/'r1150-cleanup-only.json'})
assert all(p.exists() for p in files | runners)

# All checks precede publication. One complete source tree plus changed older
# bytes keeps failed-source reconstruction possible without duplicate trees.
delta_path = temp/'earlier-source-deltas-r1157.tgz'
assert not delta_path.exists()
with tarfile.open(delta_path,'w:gz') as a:
    for revision,changed in sorted(deltas.items()):
        for name,data in sorted(changed.items()):
            m=tarfile.TarInfo(revision+'/'+name);m.size=len(data);a.addfile(m,io.BytesIO(data))
files.add(delta_path)
record = {'revision':'r1157','observedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
          'gitBase':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
          'sources':e.read(temp/'source-candidate-r1157.json'),'inputs':inputs,'matrix':matrix,'outputArtifacts':artifacts,
          'developmentFailures':failures,'sourceReconstruction':{'base':'source-r1157-indexed-comparison-bundle.tgz',
              'deltas':delta_path.name,'earlierChangedPaths':{r:sorted(v) for r,v in deltas.items()}},
          'qualification':{'newCoreCases':48,'newNativeFilesystemHeadlessViewCases':41,'retainedOutputsPerPlatform':32,
              'boundedReportsChartsAndStreamingBundleEngine':True,'taskOwnedProcessesExited':True,
              'comparisonPersistenceUiQualified':False,'automaticCrossPageChartToTableLocationQualified':False,
              'fullLargeResourcePhysicalA11yPackageMatrixQualified':False,'publicFeatureRegistered':False,'finalIndependentReviewPerformed':False}}
helpers = ('native_batch_evidence.py','prepare-test-fix-batch.ps1','deploy-batch.ps1','preflight-indexed-r732.ps1','finalize-indexed-comparison-bundle-r1157.py')
runners.add(temp/'collect-comparison-outputs-r1150.ps1')
e.retain(root,record,files,runners,helpers)
snapshot = out/'exact-receipt-text-r1157-indexed-comparison-bundle.tgz'
assert not snapshot.exists()
with tarfile.open(snapshot,'w:gz') as a:
    for row in record['artifacts']+record['drivers']:
        if not row['path'].endswith('.tgz'):
            p=root/row['path'];assert e.sha(p)==row['sha256'];a.add(p,arcname=row['path'],recursive=False)
record['artifacts'].append({'path':snapshot.relative_to(root).as_posix(),'sha256':e.sha(snapshot)})
destination.write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
print('Verified and retained all-three engine/export evidence:',destination)
