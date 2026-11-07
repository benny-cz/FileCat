import argparse, datetime, hashlib, json, pathlib, re, shutil, subprocess, xml.etree.ElementTree as ET

p = argparse.ArgumentParser()
for name, default in [('candidate', 'r818'), ('render', 'r819'), ('build', 'r817'), ('manifest', 'r820')]:
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
files = [f'build-{rev}.txt' for rev in dict.fromkeys(('r812', 'r813', build))]
files.extend(('pack-timing-r821.json','ubuntu-focused-r823-results.tgz','ubuntu-pre-r823.json','ubuntu-pre-r823post.json'))
drivers = ['preflight-indexed-r732.ps1', 'deploy-batch.ps1', 'collect-batch.ps1', 'finalize-native-source-names.py']

def collect(candidate, build_revision, qualified):
    files.append(f'build-{build_revision}.txt'); matrix = {}; controls = {}; inventories = {}; failures = {}
    for platform in ('mac', 'ubuntu', 'windows'):
        controls[platform] = {}; failures[platform] = []
        for phase in (('core', 'native', 'render') if platform == 'windows' else ('core', 'render')):
            stem = f'{platform}-batch-{candidate}-{phase}-{candidate}'
            files.extend(stem + '.' + ext for ext in ('xml', 'log'))
            assembly = ET.parse(t / (stem + '.xml')).getroot().find('assembly')
            matrix[stem + '.xml'] = {key: assembly.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}
            failures[platform].extend(test.get('name') for test in assembly.findall('.//test') if test.get('result') == 'Fail')
            if qualified:
                assert assembly.get('errors') == '0'
                known_timeout = platform == 'ubuntu' and phase == 'render'
                expected_failures = ['FileCat.Rendering.Tests.AnalysisRenderingTests.OtherExplorerNativeWindowKeepsCanonicalMembershipThroughFocusSortHistoryAndCatalogRestore(mode: "Treemap")'] if known_timeout else []
                actual_failures = [test.get('name') for test in assembly.findall('.//test') if test.get('result') == 'Fail']
                assert [name.replace('\\', '') for name in actual_failures] == expected_failures, (stem, actual_failures)
                assert int(assembly.get('failed')) == len(expected_failures)
                if phase == 'render':
                    prefixes = ('FileCat.Rendering.Tests.AnalysisRenderingTests.NativeNameSource',)
                    selected = [test for test in assembly.findall('.//test') if test.get('name', '').startswith(prefixes)]
                    assert len(selected) == 12, (stem, len(selected))
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
drivers.extend(['pack-batch-direct.ps1', 'prepare-focused-unix.ps1', 'run-focused-unix.py'])
focused = ET.parse(t/'ubuntu-focused-r823-results/results.xml').getroot().find('assembly')
assert focused.get('total') == focused.get('passed') == '2' and focused.get('failed') == focused.get('errors') == '0'
focused_state = read(t/'ubuntu-focused-r823-results/state.json')
assert focused_state['status'] == 'FINISHED' and focused_state['exitCode'] == 0 and focused_state['verifiedFiles'] == 104
assert focused_state['baseRevision'] == c
assert (t/'ubuntu-pre-r823post.json').exists() and not read(t/'ubuntu-pre-r823post.json')['filecatOrDotnet']
native_reports = {}
for platform in ('mac','ubuntu','windows'):
    log_bytes=(t/f'{platform}-batch-{c}-render-{c}.log').read_bytes()
    log=log_bytes.decode('utf-16' if log_bytes.startswith(b'\xff\xfe') else 'utf-8-sig')
    rows=re.findall(r'Native name creation: platform=([^;]+); files=(\d+); caseDistinct=(True|False); invalidUnixCreationErrors=([0-9,]*)',log)
    assert len(rows) == 12,(platform,len(rows))
    native_reports[platform]=[{'platform':value[0],'createdFiles':int(value[1]),'caseDistinct':value[2]=='True','invalidUnixCreationErrors':[int(n) for n in value[3].split(',') if n]} for value in rows]
qualification = {'nativeSourceNamesMemoryAndIndexedControlsPassedAllThree': True, 'nativeSourceNamesSavedOtherControlsPassedAllThree': True,
    'nativeHardlinkAndCaseCollisionEvidenceCaptured': True, 'macInvalidUnixByteCreationRefusalEvidenced': True,
    'targetedControlsPassedAllThree': True, 'allApplicableSuitesPassedInThisBatch': False,
    'ubuntuSavedViewTimeoutRetainedAndFocusedUnchangedBuildPassed': True,
    'fullNativeDesktopOtherQualified': False, 'fullResourceGateQualified': False, 'fullMillionTenMillionQualificationComplete': False,
    'productCommandRegistered': False, 'publicIndexedSpillEnabled': False, 'persistenceGateQualified': False,
    'actionsHelperContentEnabled': False, 'finalIndependentReviewPerformed': False}
summary = f'''Actual native filename creation and anchored scanning now have twelve cross-platform controls: eight memory/indexed captures across all four metrics and four saved-Other window cases. Generated entries include hard links, case-collision probes, bidi/combining/supplementary/formula/trailing names, Windows reserved and unpaired UTF-16 names, and Unix backslash/invalid-byte names alongside a real U+FFFD filename. Independent raw/native payload reads, captured metadata/identities/bytes, manual logical/unique/count totals, all supported groups, uncertain name filters, saved scope, keyboard tips, mark counts and preserving cleanup are checked. Native source closure and actual private-store close are awaited.

All twelve controls pass on Windows, macOS and Ubuntu. The tested NTFS volume created fourteen files and was case insensitive; ext4 created fifteen files and was case sensitive, preserving distinct invalid FF/FE names and the real replacement-character entry. APFS created twelve files and was case insensitive; both invalid-byte creation attempts were refused with native error92. That refusal is filesystem-specific evidence, not an invented successful capture. A superseded Mac fixture used the wrong fixed P/Invoke signature for variadic open mode on ARM64 and had two intermittent pre-scan read failures. The fixture now uses fixed-arity creat; production private storage already had its explicit ARM64 mode handling. Superseded evidence is retained.

The r818 broad Mac and Windows suites pass. Ubuntu's twelve new controls and all Core cases pass, but one pre-existing saved-view control exceeded its five-second publication wait. That full-suite failure is retained. Both selected saved-view cases then passed in 15.383 seconds using the exact unchanged cached build after all 104 input files were SHA-verified; only a 6.9 KiB driver was transferred and one results archive collected. This focused result does not erase or relabel the broad-suite timeout. [Provenance](source-manifest-{manifest}.json).

Workflow inspection found avoidable packaging and rerun work. The direct packer creates exactly three deployable archives with source/build checks, avoiding five unused standalone archives; its measured run took 26.49 seconds. Helper-only fixes will use focused cached reruns, short Windows smoke checks will precede broad suites, related implementation will be batched into larger feature checkpoints, and full Windows/Mac/Ubuntu qualification remains required at those checkpoints and before completion.

This is native source-name evidence for the tested filesystems and cases, not full source/action/package/resource qualification. Full W0–W8 implementation, physical desktop/a11y/theme/DPI, 1M/10M/resource limits, reliable loaded-machine wait qualification and the final independent UX/UI/functional/security review remain open. Public registration, spill, persistence, actions, helper and content gates remain off.'''
def append(path, value):
    old = path.read_text(encoding='utf-8')
    if value.strip() not in old: path.write_text(old.rstrip() + '\n\n' + value.strip() + '\n', encoding='utf-8', newline='\n')
append(out / 'README.md', summary)
append(r / 'docs/design/FILECAT_DISK_SPACE_EXPLORATION.md', summary.replace(f'(source-manifest-{manifest}.json)', f'(../validation/disk-space/source-manifest-{manifest}.json)'))
progress_path = out / 'progress.json'; progress = read(progress_path)
behavior = 'Twelve actual native-source filename/capture/saved-Other controls pass all three platforms with hardlink, case, raw-byte/unpaired and preserving payload evidence. APFS invalid-byte creation refusals are recorded. A broad Ubuntu old saved-view timeout is retained with a focused unchanged-build passing check; no broad qualification is claimed.'
if behavior not in progress['completedBehaviors']: progress['completedBehaviors'].append(behavior)
progress['validation']['nativeSourceNames'] = {'newCoreControls': 0, 'newRenderingControls': 12, 'targetedControls': success['targetedControls'], 'matrix': success['matrix'], 'manifest': f'source-manifest-{manifest}.json', **qualification}
progress['validation']['testingWorkflow']['optimizationPolicy'] = {'fullSuites':'At coherent feature checkpoints and final qualification', 'helperOnlyChanges':'Focused affected methods using SHA-verified unchanged guest inputs', 'firstPlatform':'Short Windows smoke before broad suites', 'batching':'Larger related implementation chunks; one transfer and one collection per completed batch', 'packing':'Direct three deployable archives; no unused standalone archives', 'unchangedCacheProof':'r823 verified104files;2tests passed15.383s;6990-byte driver;single result archive'}
progress['validation']['testingWorkflow']['batchEvidence'] = f'source-manifest-{manifest}.json'
progress_path.write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
record = {'purpose': 'Actual native source-name and saved-Other qualification plus measured workflow improvements; full W0-W8 remains open',
    'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'gitBase': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
    'testedWorkingTreeChanges': True, 'sources': sources,
    'artifacts': [{'path': (out / name).relative_to(r).as_posix(), 'sha256': sha(out / name)} for name in files],
    'drivers': [{'path': (out / 'drivers' / name).relative_to(r).as_posix(), 'sha256': sha(out / 'drivers' / name)} for name in drivers],
    'archives': [{'name': f'dsa-batch-{platform}-{c}.tgz', 'sha256': sha(t / f'dsa-batch-{platform}-{c}.tgz')} for platform in ('mac', 'linux', 'windows')],
    'productionCoreHashes': core, 'productionAppHashes': app, 'productionWindowsPlatformHashes': windows,
    'nativeCreationReports': native_reports, 'focusedRegression': {'state':focused_state, 'total':focused.get('total'),'passed':focused.get('passed'),'time':focused.get('time')},
    'matrix': success['matrix'], 'targetedControls': success['targetedControls'], 'postCollectionProcesses': success['postCollectionProcesses'],
    'newCoreControls': 0, 'newRenderingControls': 12, 'supersededFailedBatches': failed, 'finalBuild': f'build-{build}.txt: full solution zero warnings/errors',
    'testScope': 'Actual ordinary guest runtimes, native private saved stores and headless window controls, managed reference/ownership controls and applicable suites. Full physical desktop, complete accounting/interaction and 1M/10M/resource qualification remain pending.',
    'batchWorkflow': 'One immutable bundle/platform, sequential applicable suites, integrity and per-phase foreign-instance gates, one collection/completed machine.',
    'ownedProcessExitEvidence': 'Every original runner and collector terminal; post inventories contain no owned candidate instances. Identifiable foreign processes were preserved. No host application/input tests.',
    'qualification': qualification}
(out / f'source-manifest-{manifest}.json').write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
print(json.dumps({'sources': len(sources), 'artifacts': len(files), 'drivers': len(drivers), 'matrix': success['matrix'], 'newControlsPassedAllThree': True}, indent=2))
