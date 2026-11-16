"""Record the exact merged build and native receipts without rewriting earlier evidence."""
import datetime
import hashlib
import json
import pathlib
import shutil
import subprocess
import tarfile
import xml.etree.ElementTree as ET

root = pathlib.Path.cwd()
temp = root / '.tmp'
out = root / 'docs/validation/disk-space/main-sync-20261116'
if out.exists():
    raise RuntimeError('The completed checkpoint is immutable')

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def assembly(platform, revision, phase):
    path = temp / f'{platform}-batch-{revision}-{phase}-{revision}.xml'
    a = ET.parse(path).getroot().find('assembly')
    assert a is not None and a.get('errors') == '0'
    tests = a.findall('.//test')
    assert len(tests) == int(a.get('total'))
    return path, a, tests

def previous_assembly(platform, phase):
    with tarfile.open(root / f'docs/validation/disk-space/{platform}-batch-r1104-results.tgz') as archive:
        member = archive.getmember(f'{phase}-r1104.xml')
        assert member.isfile() and member.size <= 32 * 1024 * 1024
        return ET.fromstring(archive.extractfile(member).read()).find('assembly')

sources = read(temp / 'source-candidate-r1105.json')
assert sources and all(sha(root / row['path']) == row['sha256'] for row in sources)
build = temp / 'main-sync-solution-build-20261116.log'
text = build.read_text(encoding='utf-8-sig')
assert 'Build succeeded.' in text and 'Build FAILED.' not in text
assert '0 Warning(s)' in text and '0 Error(s)' in text
packing = read(temp / 'pack-timing-r1105.json')
assert len(packing['archives']) == 3
for row in packing['archives']:
    assert sha(pathlib.Path(row['path'])) == row['sha256']

matrix = {}
files = {build, temp / 'source-candidate-r1105.json', temp / 'pack-timing-r1105.json',
         temp / 'main-sync-probe-r1106.json', temp / 'prepare-main-sync-probe-r1106.ps1'}
for platform in ('windows', 'mac', 'ubuntu'):
    tag = 'linux' if platform == 'ubuntu' else platform
    for revision, terminal in (('r1105', 1), ('r1106', 0)):
        original = temp / f'{platform}-batch-{revision}-original-run.json'
        assert read(original)['terminalExit'] == terminal
        post = temp / f'{platform}-batch-{revision}-post.json'
        assert read(post)['filecatOrDotnet'] == []
        files.update((original, post, temp / f'{platform}-batch-{revision}-results.tgz'))
        files.add(temp / f'batch-{tag}-{revision}.{"ps1" if platform == "windows" else "sh"}')
    path, a, tests = assembly(platform, 'r1105', 'core')
    failed = [test for test in tests if test.get('result') == 'Fail']
    assert len(failed) == int(a.get('failed')) == 4
    assert all(test.get('type') == 'FileCat.Core.Tests.ArchivePasswordCommandTests' for test in failed)
    rerun_path, rerun, rerun_tests = assembly(platform, 'r1106', 'core')
    assert int(rerun.get('total')) == int(rerun.get('passed')) == 4
    assert rerun.get('failed') == rerun.get('skipped') == '0'
    assert {test.get('name') for test in failed} == {test.get('name') for test in rerun_tests}
    assert all(test.get('result') == 'Pass' for test in rerun_tests)
    old = previous_assembly(platform, 'core')
    skipped = lambda node: sorted(test.get('name') for test in node.findall('.//test') if test.get('result') == 'Skip')
    assert skipped(a) == skipped(old)
    matrix[platform] = {'coreOriginal': dict(a.attrib), 'coreFocusedRerun': dict(rerun.attrib),
                        'effectiveCorePassed': int(a.get('passed')) + 4,
                        'coreSkippedNames': skipped(a), 'packagingFailureCases': sorted(test.get('name') for test in failed)}
    files.update((path, rerun_path))
    for phase in (('render', 'native') if platform == 'windows' else ('render',)):
        path, a, tests = assembly(platform, 'r1105', phase)
        if phase == 'native':
            failed_native = [test for test in tests if test.get('result') == 'Fail']
            assert int(a.get('failed')) == len(failed_native) == 1
            assert failed_native[0].get('method') == 'Recycle_reports_outcome_and_restore_uses_the_bin_item'
            focused_path, focused, focused_tests = assembly(platform, 'r1107', 'native')
            assert focused.get('total') == focused.get('failed') == '1'
            assert focused_tests[0].get('name') == failed_native[0].get('name')
            assert read(temp / 'windows-batch-r1107-original-run.json')['terminalExit'] == 1
            assert read(temp / 'windows-batch-r1107-post.json')['filecatOrDotnet'] == []
            desktop_path, desktop, desktop_tests = assembly(platform, 'r1109', 'native')
            assert desktop.get('total') == desktop.get('passed') == '1'
            assert desktop.get('failed') == desktop.get('skipped') == '0'
            assert desktop_tests[0].get('name') == failed_native[0].get('name')
            assert read(temp / 'windows-batch-r1109-original-run.json')['terminalExit'] == 0
            assert read(temp / 'windows-batch-r1109-post.json')['filecatOrDotnet'] == []
            environment = read(temp / 'windows-recycle-environment-r1109.json')
            assert environment['user'].lower().endswith('\\filecatdsa_r2') and environment['session'] == 2
            matrix[platform]['recycleInteractiveQualificationPending'] = False
            matrix[platform]['recycleFocusedFailure'] = dict(focused.attrib)
            matrix[platform]['recycleDesktopPass'] = dict(desktop.attrib)
            matrix[platform]['effectiveNativePassed'] = int(a.get('passed')) + 1
            files.update((focused_path, temp / 'windows-batch-r1107-native-r1107.log',
                          temp / 'windows-batch-r1107-original-run.json', temp / 'windows-batch-r1107-post.json',
                          temp / 'windows-batch-r1107-results.tgz', temp / 'batch-windows-r1107.ps1',
                          temp / 'windows-recycle-environment-20261116.json', temp / 'windows-interactive-owner-20261116.json'))
            files.update((desktop_path, temp / 'windows-batch-r1109-native-r1109.log',
                          temp / 'windows-batch-r1109-original-run.json', temp / 'windows-batch-r1109-post.json',
                          temp / 'windows-batch-r1109-results.tgz', temp / 'batch-windows-r1109.ps1',
                          temp / 'launch-windows-r1109.ps1', temp / 'windows-interactive-owner-r1108.json',
                          temp / 'windows-recycle-environment-r1109.json', temp / 'windows-batch-r1108-original-run.json'))
        else:
            assert a.get('failed') == '0'
        old = previous_assembly(platform, phase)
        assert skipped(a) == skipped(old)
        assert {test.get('name') for test in old.findall('.//test')} <= {test.get('name') for test in tests}
        matrix[platform][phase] = dict(a.attrib)
        files.add(path)
    for revision in ('r1105', 'r1106'):
        files.update(temp.glob(f'{platform}-batch-{revision}-*-{revision}.log'))

assert subprocess.check_output(['git', 'diff', '--name-only', '--diff-filter=U'], text=True).strip() == ''
upstream = subprocess.check_output(['git', 'rev-parse', 'MERGE_HEAD'], text=True).strip()
feature = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
record = {'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'featureParent': feature,
          'upstreamMain': upstream, 'fullSolutionBuild': 'passed; locked dependencies; zero warnings/errors',
          'sourceFiles': len(sources), 'matrix': matrix,
          'scope': 'Post-merge disk-space and shared-command/scheduler regressions, plus the four newly selected archive command cases.',
          'recycleControl': 'The existing Windows recycle/restore case reports preserving Aborted in R2 session 0, reproduced alone. The unchanged binaries pass in the confirmed ordinary R2 desktop session 2. Original failures and the desktop pass are retained separately.',
          'fullPlanComplete': False, 'finalIndependentReviewPerformed': False}
out.mkdir()
for file in sorted(files):
    assert file.is_file(), file
    shutil.copy2(file, out / file.name)
record['evidence'] = [{'path': file.name, 'sha256': sha(file)} for file in sorted(out.iterdir())]
(out / 'qualification.json').write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
print(json.dumps({'upstream': upstream, 'matrix': {p: {'corePassed': r['effectiveCorePassed'],
    'coreSkipped': r['coreOriginal']['skipped'], 'renderPassed': r['render']['passed'],
    'renderSkipped': r['render']['skipped'], 'native': r.get('native', {}).get('passed')} for p, r in matrix.items()}}, indent=2))
