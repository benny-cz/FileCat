"""Finalize the actual Core epoch and native App preparation cohorts, once all originals end."""
import datetime
import importlib.util
import json
import pathlib
import tarfile
import xml.etree.ElementTree as ET

r = pathlib.Path.cwd()
t = r / '.tmp'
out = r / 'docs/validation/disk-space'
target = out / 'source-manifest-r1197-runtime-epochs.json'
assert not target.exists(), 'Completed qualification is immutable'
spec = importlib.util.spec_from_file_location('evidence', out / 'drivers/native_batch_evidence.py')
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)
core = e.read(t / 'epoch-proof-r1192.json')
app = e.read(t / 'epoch-app-proof-r1194.json')
windows = e.read(t / 'epoch-app-windows-preflight-proof-r1195.json')
boundary = e.read(t / 'boundary-fixture-proof-r1199.json')
assert app['coreUnchanged'] and app['coreSha256'] == core['coreSha256']
assert boundary['productAndAppUnchanged'] and boundary['changedSourcePaths'] == ['tests/FileCat.Rendering.Tests/AnalysisRenderingTests.Rescan.cs']
assert all(e.sha(r / row['path']) == row['sha256'] for row in boundary['sources'])
assert e.sha(t / 'source-r1199-boundary-fixture.tgz') == boundary['sourceSnapshotSha256']
assert e.sha(t / 'source-r1192-epoch.tgz') == core['sourceSnapshotSha256']
assert e.sha(t / 'source-r1194-epoch-app.tgz') == app['sourceSnapshotSha256']
files = {t / name for name in ('epoch-proof-r1192.json', 'epoch-app-proof-r1194.json',
    'epoch-app-windows-preflight-proof-r1195.json', 'source-r1192-epoch.tgz', 'source-r1194-epoch-app.tgz',
    'windows-r1195-rejected-prelaunch.json', 'boundary-fixture-proof-r1199.json',
    'source-r1199-boundary-fixture.tgz', 'focused-delta-proof-r1199.json',
    'focused-delta-proof-r1200.json', 'unix-r1199-rejected-shell-driver.json')}
e.assert_clean_build(r / '.tmp-epoch-boundary-fixture-build-r1198.txt', 1)
files.add(r / '.tmp-epoch-boundary-fixture-build-r1198.txt')
files.add(r / '.tmp-epoch-boundary-fixture-build-r1198-warnings.txt')
for name in ('.tmp-epoch-build-final-r1192.txt', '.tmp-epoch-build-windows-native-r1192.txt',
             '.tmp-epoch-app-build-final-r1194.txt', '.tmp-epoch-settings-build-r1194.txt'):
    e.assert_clean_build(r / name, 1)
    files.add(r / name)
files.add(r / '.tmp-epoch-build-windows-native-r1192-rejected.txt')
runners = set()
results = {}
matrix = {}


def text(data):
    return data.decode('utf-16' if data.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig')


def result(platform, revision):
    key = platform + '/' + revision
    if key in results:
        return results[key]
    original = t / f'{platform}-batch-{revision}-original-run.json'
    controller = e.read(original)
    assert controller['terminal'] and controller['exitCode'] == 0, controller
    archive = t / f'{platform}-batch-{revision}-results.tgz'
    values = e.reports(archive)
    assert text(values['state.txt']).strip() == 'FINISHED exit=0'
    post = t / f'{platform}-batch-{revision}-post.json'
    assert not e.read(post)['filecatOrDotnet']
    files.update((original, archive, post))
    tag = 'linux' if platform == 'ubuntu' else platform
    runners.add(t / f'batch-{tag}-{revision}.{"ps1" if platform == "windows" else "sh"}')
    results[key] = values
    return values


def check(platform, revision, phase, total, skip):
    values = result(platform, revision)
    a = ET.fromstring(values[phase + '.xml']).find('assembly')
    counts = tuple(int(a.get(k, '-1')) for k in ('total', 'passed', 'failed', 'errors', 'skipped'))
    assert counts == (total, total - skip, 0, 0, skip), (platform, phase, counts)
    matrix[platform + '/' + phase] = dict(revision=revision, counts=counts,
        skippedNames=[node.get('name') for node in a.findall('.//test') if node.get('result') == 'Skip'])


for platform in ('windows', 'mac', 'ubuntu'):
    win = platform == 'windows'
    check(platform, 'r1195' if win else 'r1192', 'epoch', 23, 2 if win else 0)
    check(platform, 'r1195' if win else 'r1192', 'core', 2015, 50 if win else 6)
    if win:
        check(platform, 'r1195', 'native-epoch', 2, 0)
        check(platform, 'r1195', 'native', 77, 0)
    for phase, total, skip in [('app-rescan', 7, 0), ('render', 786, 1 if win else 0), ('settings', 36, 1 if win else 0)]:
        check(platform, 'r1195' if win else 'r1194', phase, total, skip)
    focused_revision = 'r1199' if win else 'r1200'
    check(platform, focused_revision, 'render-' + focused_revision, 3, 0)
    focused = result(platform, focused_revision)
    if 'cache-proof.txt' in focused:
        cache = text(focused['cache-proof.txt'])
    else:
        # The original Unix collector omitted this small flat receipt. Retain the
        # separately copied original bytes without rewriting the collected archive.
        cache_path = t / f'{platform}-batch-{focused_revision}-cache-proof.txt'
        files.add(cache_path); cache = cache_path.read_text(encoding='utf-8-sig')
    assert 'VERIFIED cachedFiles=' in cache and 'changedFiles=2' in cache
    tag = 'linux' if platform == 'ubuntu' else platform
    for proof, revision in ((core, 'r1192'), (app, 'r1194')):
        candidate = next(row for row in proof['platforms'] if row['platform'] == tag)
        stage = t / f'batch-{revision}-{tag}'
        actual = {p.relative_to(stage).as_posix(): e.sha(p) for p in stage.rglob('*') if p.is_file()}
        assert actual == {row['path']: row['sha256'] for row in candidate['inputs']}
        assert e.sha(t / f'dsa-batch-{tag}-{revision}.tgz') == candidate['archiveSha256']
        assert e.sha(t / f'batch-{tag}-{revision}.{"ps1" if win else "sh"}') == candidate['runnerSha256']
    if win:
        candidate = next(row for row in app['platforms'] if row['platform'] == tag)
        assert windows['archiveSha256'] == candidate['archiveSha256'] == e.sha(t / 'dsa-batch-windows-r1195.tgz')
        assert e.sha(t / 'batch-windows-r1195.ps1') == windows['runnerSha256']
        values = results['windows/r1195']
        task = json.loads(text(values['task-outcome.json']))
        assert task['terminalExit'] == 0 and task['logonType'] == 'Interactive' and task['runLevel'] == 'Limited'
        assert task['principal'].lower().endswith('\\filecatdsa_r2')

record = dict(revision='r1197', observedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
    baseCommit='378fab83', independentOfPerformanceAndNewMainIntegration=True, sources=boundary['sources'],
    coreSha256=core['coreSha256'], inputs=app['platforms'], matrix=matrix,
    unixCoreAndNativeEpochResultsReusedWithExactCoreBytes=True,
    windowsFocusedPreflightsFirstWithUnchangedArchive=True, extraPhysicalBoundaryControlsReuseUnchangedProduct=True,
    qualification=dict(runtimeCoreEpochAndAppPreparation=True, publicRescanUiQualified=False,
        completeW1Lifecycle=False, publicFeatureRegistered=False, fullGoalComplete=False, finalIndependentReviewPerformed=False))
helpers = ('native_batch_evidence.py', 'prepare-epoch-r1192.py', 'prepare-epoch-app-r1194.py',
    'epoch-unix-template-r1192.sh', 'epoch-windows-template-r1192.ps1',
    'epoch-app-unix-template-r1194.sh', 'epoch-app-windows-template-r1194.ps1',
    'epoch-app-windows-template-r1195.ps1', 'collect-epoch-r1192.ps1',
    'collect-epoch-app-r1194.ps1', 'collect-epoch-boundary-r1199.ps1',
    'prepare-focused-test-delta.ps1', 'run-focused-test-delta.ps1', 'run-focused-test-delta.py', 'finalize-epoch-r1197.py')
e.retain(r, record, files, runners, helpers)
e.freeze_text(r, record, 'exact-receipt-text-r1197-runtime-epochs.tgz')
target.write_text(json.dumps(record, indent=2) + '\n')
print('Verified runtime epoch and native preparation cohorts:', target)
