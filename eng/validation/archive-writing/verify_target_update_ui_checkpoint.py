"""Read sealed guest evidence only; no application or archive runtime executes on the host."""
from collections import Counter
from pathlib import Path
import argparse, hashlib, json, runpy, subprocess, xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
BASE = runpy.run_path(str(Path(__file__).with_name('verify_native_cleanup_checkpoint.py')))
NS = BASE['NS']


def verify(platform, version='v6', candidate='HEAD'):
    candidate = subprocess.check_output(['git', 'rev-parse', '--verify', candidate + '^{commit}'], cwd=ROOT).decode().strip()
    folder = 'awt-' + version
    facts, index, data = BASE['transport'](ROOT / '.tmp' / (platform + '-target-update-ui-' + version + '-evidence'), folder, capture_sources=True)
    js = lambda name: json.loads(data[name].decode('utf-8-sig'))
    tests = {Path(name).name: dict(Counter(row.get('outcome') for row in ET.fromstring(content).findall('.//t:UnitTestResult', NS)))
        for name, content in data.items() if name.startswith('results/') and name.endswith('.trx')}
    expected = {'mac': {'Passed': 8190, 'NotExecuted': 207}, 'ubuntu': {'Passed': 8212, 'NotExecuted': 185},
        'windows': {'Passed': 7941, 'NotExecuted': 461}}[platform]
    assert tests['archive-creation-core.trx'] == expected
    for name, count in [('target-preflight', 28), ('update-smoke', 1), ('update-ui', 16), ('password-preflight', 21), ('creation-smoke', 2), ('creation-ui', 20), ('mutation-preflight', 8)]:
        assert tests[name + '.trx'] == {'Passed': count}
    assert tests['application-regressions.trx'] == ({'Passed': 114} if platform == 'windows' else {'Passed': 113, 'NotExecuted': 1})
    inventory = js('full-case-inventory.json')
    assert inventory['current'] == inventory['previous'] + 26 and inventory['prior_cases_present'] and inventory['prior_passes_preserved']
    old = ET.fromstring(data['previous-full-core.trx']).findall('.//t:UnitTestResult', NS)
    current = ET.fromstring(data['results/archive-creation-core.trx']).findall('.//t:UnitTestResult', NS)
    names = Counter(row.get('testName') for row in current)
    assert all(names[name] == count for name, count in Counter(row.get('testName') for row in old).items())
    passed = Counter(row.get('testName') for row in current if row.get('outcome') == 'Passed')
    assert all(passed[name] >= count for name, count in Counter(row.get('testName') for row in old if row.get('outcome') == 'Passed').items())
    if platform == 'windows':
        for name, count in [('creation-native-ui', 10), ('native', 217), ('creation-cleanup-crashes', 18), ('app-entry', 14)]:
            assert tests[name + '.trx'] == {'Passed': count}
        if version == 'v7': assert tests['native-review-preflight.trx'] == {'Passed': 10}
        assert not js('account.json')['AdministratorsMember'] and js('account.json')['HiveLoaded']
        assert js('standard-launch.json')['ExitCode'] == 0
    if platform != 'windows':
        assert tests['target-native-preflight.trx'] == {'Passed': 10}
    audit = js('compiler-sources/facts.json')
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for binary in audit['binaries']:
        for suffix, key in [('dll', 'dllSha256'), ('pdb', 'pdbSha256')]:
            assert index['binaries/' + binary['assembly'] + '.' + suffix]['sha256'] == binary[key].lower()
    paths = subprocess.check_output(['git', 'diff', '--name-only', 'e7f98320', candidate, '-z'], cwd=ROOT).decode().split('\0')
    changed = sorted({name for name in paths if name.endswith('.cs')})
    assert changed, 'The candidate has no changed C# inputs.'
    not_applicable = []
    source = {row['path']: row for row in js('source-files.json')['files']}
    for name in changed:
        if platform != 'windows' and name.startswith('tests/FileCat.Platform.Windows.Tests/'):
            not_applicable.append(name); continue
        rows = [row for row in audit['sources'] if row['document'].replace('\\', '/').endswith('/' + name)]
        assert rows, name
        blob = subprocess.check_output(['git', 'show', candidate + ':' + name], cwd=ROOT)
        # Git normalizes C# line endings. Verify exact compiler bytes through the sealed manifest,
        # then bind their normalized text to the immutable candidate rather than the moving worktree.
        for row in rows:
            snapshot = 'compiler-sources/' + row['snapshot']
            assert row['sha256'].lower() == index[snapshot]['sha256'] == source[name]['sha256'], name
            assert hashlib.sha256(data[snapshot].replace(b'\r\n', b'\n')).digest() == hashlib.sha256(blob.replace(b'\r\n', b'\n')).digest(), name
    for path, key, count in [('catalog/independent-catalog.json', 'cases', 273), ('workers/independent-pipeline.json', 'archives', 74),
        ('native-vectors/independent-pipeline.json', 'archives', 33 if platform == 'windows' else 21),
        ('workflows/independent-pipeline.json', 'archives', 6), ('workflows/independent-workflow-rar.json', 'official_archives', 6),
        ('workflows/independent-workflow-tar.json', 'archives', 8), ('jobs/independent-pipeline.json', 'archives', 6 if platform == 'windows' else 3),
        ('jobs/independent-workflow-rar.json', 'official_archives', 6 if platform == 'windows' else 4),
        ('jobs/independent-workflow-tar.json', 'archives', 8 if platform == 'windows' else 4)]:
        assert js(path)[key] == count, path
    if platform == 'windows':
        for folder, seven, tar in [('moves', (6, 33, 21), (8, 44, 28)), ('cleanup', (6, 9, 9), (8, 12, 12)), ('creation-ui', (3, 3, 3), (4, 4, 4)), ('update-ui', (3, 6, 6), (4, 8, 8))]:
            for leaf, keys, values in [('independent-pipeline.json', ('archives', 'metadata', 'payloads'), seven),
                ('independent-workflow-rar.json', ('official_archives', 'independent_metadata_records', 'exact_payloads'), seven),
                ('independent-workflow-tar.json', ('archives', 'metadata', 'payloads'), tar)]:
                result = js(folder + '/' + leaf)
                assert tuple(result[key] for key in keys) == values, (folder, leaf)
                for row in result['rows']:
                    assert index[folder + '/' + row['archive']]['sha256'] == row['sha256'].lower()
    assert not js('writing-closure.json')['Rar15_or_DiscUtils_present'] and not js('machine.json')['new_profile_registered']
    census = js('final-census.json')
    assert not census['busy'] and census['owned_filecat_or_test_processes'] == 0
    if platform == 'mac': assert census['preserved_wake_helper'].startswith('36765 ')
    if platform == 'ubuntu': assert js('private-keyring.json')['pid'] == js('private-keyring-stopped.json')['pid']
    reused = []
    if platform == 'windows' and version == 'v7':
        _, prior_index, _ = BASE['transport'](ROOT / '.tmp' / 'windows-target-update-ui-v6-failed-evidence', 'awt-v6')
        reuse = js('reused-regressions.json'); reused = reuse['lanes']
        assert reuse['previous'] == 'awt-v6' and reuse['previous_manifest_sha256'] == prior_index['files.json']['sha256']
        assert len(reused) == 1 and reused[0]['lane'] == 'tests'
        lane = reused[0]
        assert lane['executed_in_previous_batch'] and not lane['rerun_in_this_batch']
        assert lane['sha256'] == index[lane['result']]['sha256'] == prior_index[lane['result']]['sha256']
        assert lane['log_sha256'] == index['tests.log']['sha256'] == prior_index['tests.log']['sha256']
        before = js('runtime-before.json'); after = js('unchanged-runtime.json')
        assert after['byte_identical'] and after['files'] == before
        for name in js('previous-binary-binding.json')['files']:
            assert before[name] == prior_index['failed-binaries/' + Path(name).name]['sha256']
        for binary in audit['binaries']:
            if binary['assembly'] == 'FileCat.Platform.Windows.Tests': continue
            for suffix in ('dll', 'pdb'):
                leaf = binary['assembly'] + '.' + suffix
                assert index['binaries/' + leaf]['sha256'] == prior_index['failed-binaries/' + leaf]['sha256']
    return dict(platform=platform, candidate_source_commit=candidate, **facts, tests=tests, current_CSharp_sources_bound=len(changed)-len(not_applicable),
        source_audit_not_applicable=not_applicable,
        evidence_version=version, reused_regressions=reused,
        DLL_PDB_pairs_bound=len(audit['binaries']), census=census, production_backend_qualified=False)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--candidate', default='HEAD')
    parser.add_argument('platforms', nargs='*', choices=('mac', 'ubuntu', 'windows'))
    args = parser.parse_args()
    for platform in args.platforms or ('mac', 'ubuntu', 'windows'):
        print(json.dumps(verify(platform, candidate=args.candidate)), flush=True)
