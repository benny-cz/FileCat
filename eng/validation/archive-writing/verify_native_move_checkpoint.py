"""Static transport, actual TRX, source/PDB and independent-reader verification. No host runtime tests."""
from pathlib import Path, PurePosixPath
from collections import Counter
import hashlib, json, subprocess, sys, tarfile, xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[3]
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
names = subprocess.check_output(['git', 'diff', '--name-only', '4bd8b8ab', '-z'], cwd=root).decode().split('\0')
names += subprocess.check_output(['git', 'ls-files', '--others', '--exclude-standard', '-z'], cwd=root).decode().split('\0')
changed = sorted({name for name in names if name.endswith('.cs')}); assert len(changed) == 18, changed

def sha(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()

def transport(stem, folder):
    archive = stem.with_name(stem.name + '.tar.gz'); facts = json.loads(stem.with_suffix('.json').read_text())
    assert archive.stat().st_size == facts['bytes'] and sha(archive) == facts['sha256']
    observed, retained = {}, {}
    with tarfile.open(archive, 'r|gz') as tar:
        for member in tar:
            name = PurePosixPath(member.name)
            assert not name.is_absolute() and '..' not in name.parts and name.parts[0] == folder
            assert member.isdir() or member.isfile()
            if member.isdir(): continue
            leaf = name.relative_to(folder).as_posix(); assert leaf not in observed
            stream = tar.extractfile(member)
            if leaf.endswith(('.json', '.trx')):
                data = stream.read(); retained[leaf] = data; digest = hashlib.sha256(data).hexdigest()
            else: digest = hashlib.file_digest(stream, 'sha256').hexdigest()
            observed[leaf] = {'bytes': member.size, 'sha256': digest}
    ledger = json.loads(retained['files.json'])['files']
    assert len(ledger) == facts['rows'] and len(observed) == len(ledger) + 1
    for row in ledger: assert observed[row['path']] == {key: row[key] for key in ('bytes', 'sha256')}, row['path']
    return facts, observed, retained

def verify(platform):
    batch = 'archive-native-moves-' + ('v4' if platform == 'windows' else 'v1')
    facts, index, data = transport(root / '.tmp' / (platform + '-native-moves-' + ('v4' if platform == 'windows' else 'v1') + '-evidence'), batch)
    def js(name): return json.loads(data[name].decode('utf-8-sig'))
    def cases(name): return ET.fromstring(data[name]).findall('.//t:UnitTestResult', ns)
    tests = {}
    for name in data:
        if name.startswith('results/') and name.endswith('.trx'):
            counts = dict(Counter(row.get('outcome') for row in cases(name)))
            assert counts.get('Passed', 0) and not counts.get('Failed', 0), (name, counts)
            tests[PurePosixPath(name).name] = counts
    if platform == 'mac':
        original = cases('case-sensitive-fixture-repair/original-full-core.trx')
        retried = cases('results/case-sensitive-move-fixture.trx')
        failed = [row for row in original if row.get('outcome') == 'Failed']
        assert len(failed) == len(retried) == 4 and all('Case_distinct_sources_' in row.get('testName') for row in failed)
        assert Counter(row.get('testName') for row in failed) == Counter(row.get('testName') for row in retried)
        replacements = {row.get('testName'): row for row in retried}; core = [replacements.get(row.get('testName'), row) for row in original]
        repair = js('case-sensitive-fixture-repair/facts.json')
        assert repair['CSharp_binaries_unchanged'] and repair['combined_results_not_a_single_clean_full_run']
        assert js('case-sensitive-fixture-repair/case-sensitive-preflight.json')['case_distinct_native_identities']
    else: core = cases('results/archive-native-moves-core.trx')
    wanted = {'mac': {'Passed': 8125, 'NotExecuted': 207}, 'ubuntu': {'Passed': 8147, 'NotExecuted': 185},
        'windows': {'Passed': 7876, 'NotExecuted': 461}}[platform]
    assert Counter(row.get('outcome') for row in core) == wanted
    inventory = js('full-case-inventory.json')
    assert inventory['previous_cases'] == (8325 if platform == 'windows' else 8320)
    assert inventory['current_cases'] == len(core) and inventory['all_previous_passes_still_pass'] and len(inventory['added']) == 12
    if platform != 'windows': assert tests['move-preflight.trx'] == {'Passed': 6}
    else:
        assert tests['source-removal.trx'] == {'Passed': 23} and tests['move-jobs.trx'] == {'Passed': 10}
        assert tests['native.trx'] == {'Passed': 197} and tests['app-entry.trx'] == {'Passed': 10}
        assert tests['legacy-moves.trx'] == {'Passed': 23, 'NotExecuted': 1}
        assert not js('account.json')['AdministratorsMember']
        if 'resume-standard-v4.json' in data:
            assert js('standard-launch.json')['ExitCode'] == -1073741502
            assert js('resume-standard-v4.json')['ExitCode'] == 0
            assert js('resume-standard-v4.json')['NoCSharpTestsBeforeInitialStartupFailure']
            assert 'runner-startup-repair/original-standard-launch.json' in data
        else: assert js('standard-launch.json')['ExitCode'] == 0
        assert js('reused-evidence.json')['complete_Core_regression_reused_not_rerun']
        assert js('unchanged-core-binaries.json')['files']
        tail = js('evidence-finish-repair/facts.json')
        assert tail['CSharp_tests_and_archive_bytes_unchanged']
        for name, digest in tail['reviewed_inventory'].items(): assert index[name]['sha256'] == digest, name
    audit = js('compiler-sources/facts.json')
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for binary in audit['binaries']:
        for suffix, key in (('dll', 'dllSha256'), ('pdb', 'pdbSha256')):
            assert index['binaries/' + binary['assembly'] + '.' + suffix]['sha256'] == binary[key].lower()
    applicable = [name for name in changed if platform == 'windows' or not name.startswith(('src/FileCat.Platform.Windows/', 'tests/FileCat.Platform.Windows.Tests/'))]
    for name in applicable:
        documents = [row for row in audit['sources'] if row['document'].replace('\\', '/').endswith('/' + name)]
        assert documents and all(row['sha256'].lower() == sha(root / name) for row in documents), name
    assert not js('writing-closure.json')['Rar15_or_DiscUtils_present']
    for path, key, count in [('catalog/independent-catalog.json','cases',273), ('workers/independent-pipeline.json','archives',74),
            ('native-vectors/independent-pipeline.json','archives',33 if platform == 'windows' else 21),
            ('workflows/independent-pipeline.json','archives',6), ('workflows/independent-workflow-rar.json','official_archives',6),
            ('workflows/independent-workflow-tar.json','archives',8), ('jobs/independent-pipeline.json','archives',6 if platform == 'windows' else 3),
            ('jobs/independent-workflow-rar.json','official_archives',6 if platform == 'windows' else 4),
            ('jobs/independent-workflow-tar.json','archives',8 if platform == 'windows' else 4)]:
        assert js(path)[key] == count, path
    if platform == 'windows':
        for path, counts in [('moves/independent-pipeline.json', {'archives':6,'metadata':33,'payloads':21}),
                ('moves/independent-workflow-rar.json', {'official_archives':6,'independent_metadata_records':33,'exact_payloads':21}),
                ('moves/independent-workflow-tar.json', {'archives':8,'metadata':44,'payloads':28})]:
            assert all(js(path)[key] == value for key, value in counts.items()), path
        for folder, leaf in [('moves','independent-pipeline.json'), ('moves','independent-workflow-rar.json'),
                ('moves','independent-workflow-tar.json')]:
            for row in js(folder + '/' + leaf)['rows']:
                assert index[folder + '/' + row['archive']]['sha256'] == row['sha256'].lower(), row['archive']
    census = js('final-census.json'); assert not census['busy'] and census['owned_filecat_or_test_processes'] == 0
    if platform == 'mac': assert census['preserved_wake_helper'].startswith('36765 ')
    if platform == 'ubuntu': assert js('private-keyring.json')['pid'] == js('private-keyring-stopped.json')['pid']
    return {'platform': platform, **facts, 'verified_current_CSharp_sources': len(applicable), 'bound_DLL_PDB_pairs': len(audit['binaries']),
        'core': wanted, 'core_evidence': 'original full run plus four corrected APFS-fixture cases' if platform == 'mac' else
            'hash-verified complete Core run retained from v1' if platform == 'windows' else 'complete Core run', 'tests': tests, 'census': census}

if __name__ == '__main__':
    for platform in sys.argv[1:] or ('mac', 'ubuntu', 'windows'): print(json.dumps(verify(platform)), flush=True)
