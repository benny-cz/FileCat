"""Static transport/TRX/source-binding checks for this milestone checkout; guest evidence belongs under .tmp."""
from collections import Counter
from pathlib import Path, PurePosixPath
import hashlib, json, subprocess, sys, tarfile, xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[3]
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
names = subprocess.check_output(['git', 'diff', '--name-only', '03107264', '-z'], cwd=root).decode().split('\0')
names += subprocess.check_output(['git', 'ls-files', '--others', '--exclude-standard', '-z'], cwd=root).decode().split('\0')
changed = sorted(set(name for name in names if name.endswith('.cs')))
assert len(changed) == 21, changed
result = {'date': '2026-11-08', 'source_checkpoint': '03107264', 'production_profile_registered': False,
    'production_backend_qualified': False, 'native_manager_execution_implemented': True, 'production_UI_integration_complete': False,
    'new_core_cases': 81, 'new_Windows_native_cases': 14, 'changed_csharp': changed,
    'delta_packets': {version: json.loads((root / ('.tmp/archive-native-jobs-delta-' + version + '.json')).read_text()) for version in ('v1', 'v2', 'v3', 'v4', 'v5')},
    'limitations': ['No candidate is composed into the application; all production qualification gates remain open.',
        'Native source removal/F6, safe new-file cleanup, complete recovery and creation/member/edit/password UI remain.',
        'Unix update/confinement and resource/crash/fuzz/RID/package and the final independent review remain.'], 'platforms': {}}
result['static_verifier_sha256'] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
result['current_validation_driver_sha256'] = hashlib.sha256((root / 'eng/validation/archive-writing/run_native_job_batch.py').read_bytes()).hexdigest()

def verify(platform):
    batch = 'archive-native-jobs-' + ('v5' if platform == 'windows' else 'v4')
    stem = root / '.tmp' / (platform + '-native-jobs-' + ('v5' if platform == 'windows' else 'v4') + '-evidence')
    archive = stem.with_name(stem.name + '.tar.gz')
    facts = json.loads(stem.with_suffix('.json').read_text())
    with archive.open('rb') as stream: digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    assert archive.stat().st_size == facts['bytes'] and digest == facts['sha256']
    observed, retained = {}, {}
    with tarfile.open(archive, 'r|gz') as tar:
        for member in tar:
            name = PurePosixPath(member.name)
            assert not name.is_absolute() and '..' not in name.parts and name.parts[0] == batch
            assert member.isdir() or member.isfile()
            if member.isdir(): continue
            leaf = name.relative_to(batch).as_posix(); assert leaf not in observed
            stream = tar.extractfile(member)
            if leaf.endswith(('.json', '.trx')):
                data = stream.read(); retained[leaf] = data; checksum = hashlib.sha256(data).hexdigest()
            else: checksum = hashlib.file_digest(stream, 'sha256').hexdigest()
            observed[leaf] = {'bytes': member.size, 'sha256': checksum}
    def js(name): return json.loads(retained[name].decode('utf-8-sig'))
    ledger = js('files.json')['files']; assert len(ledger) == facts['rows'] and len(observed) == len(ledger) + 1
    for row in ledger: assert observed[row['path']] == {key: row[key] for key in ('bytes', 'sha256')}, row['path']
    tests, cases = {}, []
    for name, data in retained.items():
        if name.startswith('results/') and name.endswith('.trx'):
            rows = ET.fromstring(data).findall('.//t:UnitTestResult', ns)
            counts = dict(Counter(case.get('outcome') for case in rows))
            assert counts.get('Passed', 0) and not counts.get('Failed', 0), (name, counts)
            tests[PurePosixPath(name).name] = counts
            if name.endswith('/archive-native-jobs-core.trx'): cases = rows
    assert tests['job-preflight.trx'] == {'Passed': 114}
    assert tests['native-job-preflight.trx'] == {'Passed': 14 if platform == 'windows' else 12}
    assert len(cases) == (8325 if platform == 'windows' else 8320)
    expected_core = {'mac': (8113, 207), 'ubuntu': (8135, 185), 'windows': (7868, 457)}[platform]
    assert tests['archive-native-jobs-core.trx'] == {'Passed': expected_core[0], 'NotExecuted': expected_core[1]}
    for component in ('JobEngineTests', 'JobRetirementOwnershipTests', 'UndoLifecycleTests', 'SyncGroupTests'):
        assert any('.' + component + '.' in case.get('testName', '') and case.get('outcome') == 'Passed' for case in cases), component
    if platform == 'windows':
        assert tests['native.trx'] == {'Passed': 177} and tests['app-entry.trx'] == {'Passed': 10}
        assert not js('account.json')['AdministratorsMember'] and js('account.json')['HiveLoaded']
    else:
        assert sum(case.get('outcome') == 'Passed' and '.UnixArchiveStorageTests.Native_jobs_' in case.get('testName', '') for case in cases) == 10
    independent = {}
    for folder, name in (('catalog', 'independent-catalog.json'), ('workers', 'independent-pipeline.json'),
        ('native-vectors', 'independent-pipeline.json'), ('workflows', 'independent-pipeline.json'),
        ('workflows', 'independent-workflow-rar.json'), ('workflows', 'independent-workflow-tar.json'),
        ('jobs', 'independent-pipeline.json'), ('jobs', 'independent-workflow-rar.json'), ('jobs', 'independent-workflow-tar.json')):
        data = js(folder + '/' + name)
        independent[folder + '/' + name] = {key: value for key, value in data.items() if key not in ('rows', 'official_unsupported_zstandard')}
    assert independent['catalog/independent-catalog.json']['cases'] == 273
    for name, values in [('workers/independent-pipeline.json', {'archives': 74, 'metadata': 357, 'payloads': 256, 'official_archives': 62, 'composed_zstandard_archives': 12}),
        ('native-vectors/independent-pipeline.json', {'archives': 33 if platform == 'windows' else 21}),
        ('workflows/independent-pipeline.json', {'archives': 6, 'metadata': 45, 'payloads': 39}),
        ('workflows/independent-workflow-rar.json', {'official_archives': 6, 'independent_metadata_records': 45, 'exact_payloads': 39}),
        ('workflows/independent-workflow-tar.json', {'archives': 8, 'metadata': 60, 'payloads': 52}),
        ('jobs/independent-pipeline.json', {'archives': 6 if platform == 'windows' else 3, 'metadata': 57 if platform == 'windows' else 24, 'payloads': 33 if platform == 'windows' else 15}),
        ('jobs/independent-workflow-rar.json', {'official_archives': 6 if platform == 'windows' else 4, 'independent_metadata_records': 57 if platform == 'windows' else 32, 'exact_payloads': 33 if platform == 'windows' else 20}),
        ('jobs/independent-workflow-tar.json', {'archives': 8 if platform == 'windows' else 4, 'metadata': 76 if platform == 'windows' else 32, 'payloads': 44 if platform == 'windows' else 20})]:
        assert all(independent[name][key] == value for key, value in values.items()), (platform, name, independent[name])
    audit = js('compiler-sources/facts.json')
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for binary in audit['binaries']:
        for suffix, key in (('dll', 'dllSha256'), ('pdb', 'pdbSha256')):
            assert observed['binaries/' + binary['assembly'] + '.' + suffix]['sha256'] == binary[key].lower()
    applicable = [name for name in changed if platform == 'windows' or not name.startswith(('src/FileCat.Platform.Windows/', 'tests/FileCat.Platform.Windows.Tests/'))]
    for name in applicable:
        docs = [row for row in audit['sources'] if row['document'].replace('\\', '/').endswith('/' + name)]
        assert docs and all(row['sha256'].lower() == hashlib.sha256((root / name).read_bytes()).hexdigest() for row in docs), (platform, name)
    assert not js('writing-closure.json')['Rar15_or_DiscUtils_present']
    repair = js('harness-oracle-repair/facts.json')
    assert repair['archive_bytes_unchanged'] and len(repair['archives']) == (20 if platform == 'windows' else 11)
    for name, source in [('verify-native-job-rar-v1.py', 'eng/validation/archive-writing/rar5-independent-probe/verify_native_publication.py'),
                         ('verify-native-job-tar-v1.py', 'eng/validation/archive-writing/verify-workflow-tar.py')]:
        pin = hashlib.sha256((root / source).read_bytes()).hexdigest()
        assert observed['harness-oracle-repair/' + name]['sha256'] == pin
        assert next(row for row in repair['repaired_helpers'] if row['path'] == name)['sha256'] == pin
    for row in repair['archives']: assert observed[row['path']] == {key: row[key] for key in ('bytes', 'sha256')}
    census = js('final-census.json'); assert not census['busy'] and census['owned_filecat_or_test_processes'] == 0
    if platform == 'mac': assert census['preserved_wake_helper'].startswith('36765 ')
    if platform == 'ubuntu':
        environment = js('private-keyring.json')
        assert environment['service_owner_pid_matches'] and environment['isolated_bus']
        assert js('private-keyring-stopped.json')['pid'] == environment['pid']
    else:
        optional = ET.fromstring(retained['environment-fixture/environment-preflight.trx']).findall('.//t:UnitTestResult', ns)
        assert len(optional) == 1 and optional[0].get('outcome') == 'NotExecuted'
        assert optional[0].get('testName').endswith('SecretStoreTests.The_system_store_keeps_replaces_and_removes_a_secret')
    assert 'job-oracles-complete.txt' in observed and 'complete.txt' in observed
    return dict(facts, batch=batch, tests=tests, independent=independent, changed_csharp_matched=applicable,
        compiler_source_documents=audit['documents'], assembly_pdb_pairs_bound=len(audit['binaries']), final_census=census,
        executed_native_job_driver_sha256=observed['executed-native-job-batch.py']['sha256'],
        source_ledger_sha256=observed['source-files.json']['sha256'],
        oracle_repair={'archive_bytes_unchanged': True, 'archives': len(repair['archives']), 'repaired_helpers': repair['repaired_helpers']})

for platform in sys.argv[1:] or ('mac', 'ubuntu', 'windows'):
    result['platforms'][platform] = verify(platform)
    value = result['platforms'][platform]
    print(json.dumps({platform: {'tests': value['tests'], 'verified_transport_rows': value['rows'],
        'changed_csharp_bound': len(value['changed_csharp_matched']), 'assembly_pdb_pairs_bound': value['assembly_pdb_pairs_bound'],
        'independent_job_archives': value['oracle_repair']['archives'], 'owned_instances_left': value['final_census']['owned_filecat_or_test_processes']}}), flush=True)
if len(result['platforms']) == 3:
    result['retained_original_failures'] = json.loads((root / '.tmp/native-job-failure-checks.json').read_text())
    (root / 'docs/validation/archive-writing/native-archive-job-checkpoint.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
