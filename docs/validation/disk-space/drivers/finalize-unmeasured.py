import argparse, datetime, hashlib, json, pathlib, re, shutil, subprocess, xml.etree.ElementTree as ET

p = argparse.ArgumentParser()
for name, default in [('candidate', 'r789'), ('render', 'r790'), ('build', 'r788'), ('manifest', 'r791')]:
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
files = [f'build-{rev}.txt' for rev in dict.fromkeys(('r784', 'r786', 'r787', build))]
drivers = ['preflight-indexed-r732.ps1', 'deploy-batch.ps1', 'collect-batch.ps1', 'finalize-unmeasured.py']

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
                assert assembly.get('failed') == assembly.get('errors') == '0', (stem, matrix[stem + '.xml'])
                if phase in ('core', 'render'):
                    prefixes = tuple('FileCat.Core.Tests.AnalysisDatasetSummaryTests.' + name for name in ('UnmeasuredHierarchyGeometryAndEveryOtherSuffix', 'IncompatibleCombinedGeometryAndOther')) if phase == 'core' else ('FileCat.Rendering.Tests.AnalysisRenderingTests.UnmeasuredOtherNativeSavedWindow',)
                    selected = [test for test in assembly.findall('.//test') if test.get('name', '').startswith(prefixes)]
                    assert len(selected) == (121 if phase == 'core' else 8), (stem, len(selected))
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
qualification = {'folderUnmeasuredEvidenceAndExactSuffixControlsPassedAllThree': True, 'boundedCanonicalMissingCohortAndRawFactEvidenceImplemented': True,
    'rootAndSubsetUnknownHeadersRetained': True, 'incompatibleCombinedGeometryAndSiblingScopesExplicitlyUnavailable': True,
    'nativePrivateSavedWindowControlsPassedAllThree': True, 'newControlsPassedAllThree': True,
    'indexedOtherExactSubsetPersistenceParityQualified': False, 'fullMixedBasisUnknownAndUnverifiedSubsetQualificationComplete': False,
    'fullNativeDesktopOtherQualified': False, 'fullResourceGateQualified': False, 'fullMillionTenMillionQualificationComplete': False,
    'productCommandRegistered': False, 'publicIndexedSpillEnabled': False, 'persistenceGateQualified': False,
    'actionsHelperContentEnabled': False, 'finalIndependentReviewPerformed': False}
summary = f'''Folder geometry retains unmeasured evidence from canonical missing charge cohorts, per-object raw facts, node issues and metadata. Ancestors, independently retained scopes and synthetic scope headers preserve metric/logical unknown flags, including measured-zero and identity-unverified-zero distinctions. Missing raw component evidence remains visible when an inclusive component supplies chart area. Unverified missing measurements remain path-specific. The temporary evidence index and issue traversal use bounded captured-device work; unknown evidence never relocates a shared charge or reopens a source. The private hierarchy row codec is versioned; inventory interchange is unchanged.

Combined On disk geometry and Other scopes refuse incompatible allocation components with the prescribed explanation, including sibling navigation from an existing subset. Individual root amounts and geometry remain available. This follows A61 and section 27.3; it does not qualify combined ranking of incompatible bases.

121 new Core cases and eight native-private-store/headless saved-window cases pass on Windows, macOS and Ubuntu, alongside the applicable suites. Controls cover all four metrics, filtered/unfiltered paths, every suffix, zero, five missing states, unverified paths, conflicting versions, absent per-object facts in a verified cohort, covered missing components, issues, partial coverage, unknown namespace kinds, directory self allocation, source-owner closure, both chart modes, exact saved membership and preserving native private-file cleanup. Known-zero files remain outside the saved unmeasured-alias Other scopes. Every original runner and collector is terminal; post inventories contain no owned instances. No host application/input testing occurred. [Provenance](source-manifest-{manifest}.json).

The full W0–W8 implementation remains open. Folder shared-reference statistics, the complete accounting/raw-name/interaction matrix, physical desktop/a11y/theme/DPI/package qualification, large-inventory resource gates and the final independent UX/UI/functional/security review remain required. Public registration, spill, persistence, actions, helper and content gates remain off.'''
def append(path, value):
    old = path.read_text(encoding='utf-8')
    if value.strip() not in old: path.write_text(old.rstrip() + '\n\n' + value.strip() + '\n', encoding='utf-8', newline='\n')
append(out / 'README.md', summary)
append(r / 'docs/design/FILECAT_DISK_SPACE_EXPLORATION.md', summary.replace(f'(source-manifest-{manifest}.json)', f'(../validation/disk-space/source-manifest-{manifest}.json)'))
progress_path = out / 'progress.json'; progress = read(progress_path)
behavior = 'Folder hierarchy geometry and synthetic scope headers retain bounded canonical missing-cohort, raw-fact, issue and metadata evidence through every exact Other suffix. Incompatible combined geometry and sibling scopes explicitly require a root-separated view. 121 Core and eight native-private-store/headless saved-window controls pass all three platforms; complete Other/product/resource qualification remains open.'
if behavior not in progress['completedBehaviors']: progress['completedBehaviors'].append(behavior)
progress['validation']['folderUnmeasuredGeometry'] = {'newCoreControls': 121, 'newRenderingControls': 8, 'targetedControls': success['targetedControls'], 'matrix': success['matrix'], 'manifest': f'source-manifest-{manifest}.json', **qualification}
progress['validation']['testingWorkflow']['batchEvidence'] = f'source-manifest-{manifest}.json'
progress_path.write_text(json.dumps(progress, indent=2, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
record = {'purpose': 'Bounded folder unmeasured evidence, exact suffix/header retention and prescribed incompatible-root refusal; full W0-W8 remains open',
    'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'gitBase': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
    'testedWorkingTreeChanges': True, 'sources': sources,
    'artifacts': [{'path': (out / name).relative_to(r).as_posix(), 'sha256': sha(out / name)} for name in files],
    'drivers': [{'path': (out / 'drivers' / name).relative_to(r).as_posix(), 'sha256': sha(out / 'drivers' / name)} for name in drivers],
    'archives': [{'name': f'dsa-batch-{platform}-{c}.tgz', 'sha256': sha(t / f'dsa-batch-{platform}-{c}.tgz')} for platform in ('mac', 'linux', 'windows')],
    'productionCoreHashes': core, 'productionAppHashes': app, 'productionWindowsPlatformHashes': windows,
    'matrix': success['matrix'], 'targetedControls': success['targetedControls'], 'postCollectionProcesses': success['postCollectionProcesses'],
    'newCoreControls': 121, 'newRenderingControls': 8, 'supersededFailedBatches': failed, 'finalBuild': f'build-{build}.txt: full solution zero warnings/errors',
    'testScope': 'Actual ordinary guest runtimes, native private saved stores and headless window controls, managed reference/ownership controls and applicable suites. Full physical desktop, complete accounting/interaction and 1M/10M/resource qualification remain pending.',
    'batchWorkflow': 'One immutable bundle/platform, sequential applicable suites, integrity and per-phase foreign-instance gates, one collection/completed machine.',
    'ownedProcessExitEvidence': 'Every original runner and collector terminal; post inventories contain no owned candidate instances. Identifiable foreign processes were preserved. No host application/input tests.',
    'qualification': qualification}
(out / f'source-manifest-{manifest}.json').write_text(json.dumps(record, indent=2, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
print(json.dumps({'sources': len(sources), 'artifacts': len(files), 'drivers': len(drivers), 'matrix': success['matrix'], 'newControlsPassedAllThree': True}, indent=2))
