import argparse, datetime, hashlib, json, pathlib, re, shutil, subprocess, xml.etree.ElementTree as ET

p = argparse.ArgumentParser()
for name, default in [('candidate', 'r805'), ('render', 'r806'), ('build', 'r804'), ('manifest', 'r807')]:
    p.add_argument('--' + name, default=default)
p.add_argument('--failed', action='append', default=[])
a = p.parse_args()
c, render, build, manifest = a.candidate, a.render, a.build, a.manifest
assert all(re.fullmatch(r'r\d+[a-z]?', v) for v in (c, render, build, manifest))
r = pathlib.Path.cwd(); t = r / '.tmp'; out = r / 'docs/validation/disk-space'
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
sources = read(t / f'source-candidate-{c}.json')
assert all(sha(r / s['path']) == s['sha256'] for s in sources)
files = [f'build-{rev}.txt' for rev in dict.fromkeys(('r802', 'r803', build))]
files.extend(('windows-pre-r805-busy-initial.json','windows-pre-r809-busy-initial.json','windows-busy-diagnostic-r809.json'))
drivers = ['preflight-indexed-r732.ps1', 'deploy-batch.ps1', 'collect-batch.ps1', 'finalize-raw-names.py']

def collect(candidate, build_revision, qualified):
    files.append(f'build-{build_revision}.txt'); matrix = {}; controls = {}; inventories = {}; failures = {}
    for platform in ('mac', 'ubuntu', 'windows'):
        controls[platform] = {}; failures[platform] = []
        if not (t / f'{platform}-batch-{candidate}-core-{candidate}.xml').exists():
            assert not qualified and platform == 'windows' and candidate == 'r805'
            busy = read(t / 'windows-pre-r805-busy-initial.json')
            assert busy['exitCode'] == 0 and busy['filecatOrDotnet']
            inventories[platform] = {'notStarted': True, 'reason': 'Foreign test instances; superseded candidate was never run here', 'processes': busy['filecatOrDotnet']}
            continue
        for phase in (('core', 'native', 'render') if platform == 'windows' else ('core', 'render')):
            stem = f'{platform}-batch-{candidate}-{phase}-{candidate}'
            files.extend(stem + '.' + ext for ext in ('xml', 'log'))
            assembly = ET.parse(t / (stem + '.xml')).getroot().find('assembly')
            matrix[stem + '.xml'] = {key: assembly.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
            failures[platform].extend(test.get('name') for test in assembly.findall('.//test') if test.get('result') == 'Fail')
            if qualified:
                assert assembly.get('failed') == assembly.get('errors') == '0', (stem, matrix[stem + '.xml'])
                if phase in ('core', 'render'):
                    prefixes = ('FileCat.Core.Tests.AnalysisDatasetSummaryTests.RawName', 'FileCat.Core.Tests.AnalysisHierarchyNameKeyTests.RawNameKeys') if phase == 'core' else ('FileCat.Rendering.Tests.AnalysisRenderingTests.RawNameSavedOtherNativePrivateWindow',)
                    selected = [test for test in assembly.findall('.//test') if test.get('name', '').startswith(prefixes)]
                    assert len(selected) == (80 if phase == 'core' else 12), (stem, len(selected))
                    assert all(test.get('result') == 'Pass' for test in selected), stem
                    controls[platform][phase] = [test.get('name') for test in selected]
        files.extend((f'{platform}-batch-{candidate}-state.txt', f'{platform}-batch-{candidate}-post.json', f'{platform}-pre-{candidate}.json'))
        state = (t / f'{platform}-batch-{candidate}-state.txt').read_text(encoding='utf-8-sig').strip()
        assert state == ('FINISHED exit=1' if failures[platform] else 'FINISHED exit=0'), (platform, state)
        pre = read(t / f'{platform}-pre-{candidate}.json'); post = read(t / f'{platform}-batch-{candidate}-post.json')
        assert pre['exitCode'] == 0 and not pre['filecatOrDotnet'], pre
        assert post['exitCode'] == 0, post
        inventories[platform] = post['filecatOrDotnet']
        for row in inventories[platform]:
            assert 'dsa-batch-' not in row and f'batch-{candidate}' not in row, (platform, row)
            assert 'archive-writing-42829297/' in row or platform == 'windows' and ('owner=DESKTOP-A60F1NE\\fcarchive42829297,' in row or 'owner=DESKTOP-A60F1NE\\Admin,' in row), (platform, row)
    for platform in ('mac', 'linux', 'windows'):
        drivers.append(f'batch-{platform}-{candidate}.{"ps1" if platform == "windows" else "sh"}')
    if not qualified: assert any(failures.values())
    return {'candidate': candidate, 'sources': read(t / f'source-candidate-{candidate}.json'), 'matrix': matrix, 'targetedControls': controls, 'postCollectionProcesses': inventories, 'failures': failures, 'qualified': qualified, 'allRunnersAndCollectorsTerminal': True}

success = collect(c, build, True); failed = []
for pair in a.failed:
    prior, prior_build = pair.split(':'); failed.append(collect(prior, prior_build, False))
assert all(s in (t / f'build-{build}.txt').read_text(encoding='utf-8-sig') for s in ('Build succeeded.', '0 Warning(s)', '0 Error(s)'))
stages = (f'core-{c}', f'native-{c}', f'render-{render}-mac', f'render-{render}-linux', f'render-{render}-windows')
core = {stage: sha(t / stage / 'FileCat.Core.dll') for stage in stages}; assert len(set(core.values())) == 1
app = {stage: sha(t / stage / 'FileCat.dll') for stage in stages if stage.startswith('render-')}; assert len(set(app.values())) == 1
windows = {stage: sha(t / stage / 'FileCat.Platform.Windows.dll') for stage in stages if (t / stage / 'FileCat.Platform.Windows.dll').exists()}; assert len(set(windows.values())) == 1
files = list(dict.fromkeys(files)); drivers = list(dict.fromkeys(drivers))
for name in files: shutil.copy2(t / name, out / name)
for name in drivers: shutil.copy2(t / name, out / 'drivers' / name)
qualification = {'capturedNameOrderingAndBoundedRawEvidenceImplemented': True, 'indexedGeometryAccountsDecodedNamesAndEvidence': True,
    'groupSubsetUsesSingleDecodedGeometryObservation': True, 'folderAndGroupedRawNameSuffixControlsPassedAllThree': True,
    'nativePrivateSavedWindowRawEvidenceControlsPassedAllThree': True, 'newControlsPassedAllThree': True,
    'indexedOtherExactSubsetPersistenceParityQualified': False, 'fullMixedBasisUnknownAndUnverifiedSubsetQualificationComplete': False,
    'fullNativeDesktopOtherQualified': False, 'fullResourceGateQualified': False, 'fullMillionTenMillionQualificationComplete': False,
    'productCommandRegistered': False, 'publicIndexedSpillEnabled': False, 'persistenceGateQualified': False,
    'actionsHelperContentEnabled': False, 'finalIndependentReviewPerformed': False}
summary = f'''Indexed folder and grouped chart items now retain bounded raw-name evidence, and both charts show it through keyboard/focus tooltips. The memory table orders real entries by captured names with the indexed ordinal-ignore-case/name-direction ID ties, preserving a separate escaped display. It retains references to names already owned by its bounded snapshot; indexed chart items retain no full ordering-name strings. Raw identities, geometry membership and inventory interchange are unchanged.

Geometry pages now charge the captured records decoded to build shortened labels, together with raw evidence. Grouped Other construction reuses the same decoded observation for its amount instead of reading it twice. Chart retention counts evidence strings in its existing byte budget. This closes a long-name batching gap without widening resource limits.

80 new Core cases and 12 native-private-store/headless saved-window cases pass on Windows, macOS and Ubuntu with the applicable suites. Folder controls cover four metrics, three encodings and filtering, exact source names/bytes, both name directions and case-fold ties, paged names, scoped ranges/matches and source-owner closure. Group controls cover four supported dimensions and all metrics/encodings, retained canonical geometry, name order and Other captures. Maximum 64-KiB record controls page folder/group geometry and subsets without losing rows. Cases distinguish invalid Unix byte names with the same decoded display, unpaired UTF-16, control/bidi characters, literal backslashes, combining forms, trailing dots/spaces and Unicode case pairs. Saved Other windows retain names through folder/group restoration, inspection and keyboard tips in both charts, preserve private inventory bytes, never bind the absent source and leave empty owned scratch after close. A preliminary compile invoked a test-data property as a method; the fixture generator was corrected. Superseded r805 retained 12 ordering failures on Mac and Ubuntu while all rendering checks passed: the private key ranked a valid supplementary character by its first UTF-16 unit, unlike the runtime ordinal-ignore-case comparer. A compact escaped scalar encoding now preserves the comparer order within the existing key bound; dedicated mixed BMP/supplementary/unpaired and maximum-size controls pass. Windows was occupied by foreign test instances and the superseded candidate was never run there; the corrected candidate was tested on all three. No foreign process was stopped. Every original runner and collector is terminal; post inventories contain no owned instances. No host application/input testing occurred. [Provenance](source-manifest-{manifest}.json).

These controls do not qualify all native source-name creation/traversal, full Other/resource/persistence or the full W0–W8 product. Physical desktop/a11y/theme/DPI/package, remaining accounting/interaction/source-boundary and 1M/10M resource matrices and the final independent UX/UI/functional/security review remain open. Public registration, spill, persistence, actions, helper and content gates remain off.'''
def append(path, value):
    old = path.read_text(encoding='utf-8')
    if value.strip() not in old: path.write_text(old.rstrip() + '\n\n' + value.strip() + '\n', encoding='utf-8', newline='\n')
append(out / 'README.md', summary)
append(r / 'docs/design/FILECAT_DISK_SPACE_EXPLORATION.md', summary.replace(f'(source-manifest-{manifest}.json)', f'(../validation/disk-space/source-manifest-{manifest}.json)'))
progress_path = out / 'progress.json'; progress = read(progress_path)
behavior = 'Captured-name memory ordering and bounded raw-name evidence now agree with indexed folder/group views, with keyboard chart tips, bounded scalar-aware ordinal keys and decoded-record geometry work accounting. 80 Core and 12 native-private saved Other window controls pass all three platforms; full native name traversal, Other/product/resource qualification remain open.'
if behavior not in progress['completedBehaviors']: progress['completedBehaviors'].append(behavior)
progress['validation']['rawNameExploration'] = {'newCoreControls': 80, 'newRenderingControls': 12, 'targetedControls': success['targetedControls'], 'matrix': success['matrix'], 'manifest': f'source-manifest-{manifest}.json', **qualification}
progress['validation']['testingWorkflow']['batchEvidence'] = f'source-manifest-{manifest}.json'
progress_path.write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
record = {'purpose': 'Captured-name ordering, raw evidence and bounded geometry reads for folder/group Other views; full W0-W8 remains open',
    'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'gitBase': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
    'testedWorkingTreeChanges': True, 'sources': sources,
    'artifacts': [{'path': (out / name).relative_to(r).as_posix(), 'sha256': sha(out / name)} for name in files],
    'drivers': [{'path': (out / 'drivers' / name).relative_to(r).as_posix(), 'sha256': sha(out / 'drivers' / name)} for name in drivers],
    'archives': [{'name': f'dsa-batch-{platform}-{c}.tgz', 'sha256': sha(t / f'dsa-batch-{platform}-{c}.tgz')} for platform in ('mac', 'linux', 'windows')],
    'productionCoreHashes': core, 'productionAppHashes': app, 'productionWindowsPlatformHashes': windows,
    'matrix': success['matrix'], 'targetedControls': success['targetedControls'], 'postCollectionProcesses': success['postCollectionProcesses'],
    'newCoreControls': 80, 'newRenderingControls': 12, 'supersededFailedBatches': failed, 'finalBuild': f'build-{build}.txt: full solution zero warnings/errors',
    'testScope': 'Actual ordinary guest runtimes, native private saved stores and headless window controls, managed reference/ownership controls and applicable suites. Full physical desktop, complete accounting/interaction and 1M/10M/resource qualification remain pending.',
    'batchWorkflow': 'One immutable bundle/platform, sequential applicable suites, integrity and per-phase foreign-instance gates, one collection/completed machine.',
    'ownedProcessExitEvidence': 'Every original runner and collector terminal; post inventories contain no owned candidate instances. Identifiable foreign processes were preserved. No host application/input tests.',
    'qualification': qualification}
(out / f'source-manifest-{manifest}.json').write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
print(json.dumps({'sources': len(sources), 'artifacts': len(files), 'drivers': len(drivers), 'matrix': success['matrix'], 'newControlsPassedAllThree': True}, indent=2))
