"""Read sealed guest evidence only; no application or archive runtime executes on the host."""
from collections import Counter
from pathlib import Path
import json, runpy, subprocess, sys, xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
BASE = runpy.run_path(str(Path(__file__).with_name('verify_native_cleanup_checkpoint.py')))
NS = BASE['NS']


def verify(platform, version='v5'):
    folder = 'archive-creation-ui-' + version
    facts, index, data = BASE['transport'](ROOT / '.tmp' / (platform + '-creation-ui-' + version + '-evidence'), folder)
    js = lambda name: json.loads(data[name].decode('utf-8-sig'))
    tests = {Path(name).name: dict(Counter(row.get('outcome') for row in ET.fromstring(content).findall('.//t:UnitTestResult', NS)))
        for name, content in data.items() if name.startswith('results/') and name.endswith('.trx')}
    expected = {'mac': {'Passed': 8164, 'NotExecuted': 207}, 'ubuntu': {'Passed': 8186, 'NotExecuted': 185},
        'windows': {'Passed': 7915, 'NotExecuted': 461}}[platform]
    assert tests['archive-creation-core.trx'] == expected
    for name, count in [('password-preflight', 21), ('creation-smoke', 2), ('creation-ui', 20)]:
        assert tests[name + '.trx'] == {'Passed': count}
    assert tests['application-regressions.trx'] == ({'Passed': 114} if platform == 'windows' else {'Passed': 113, 'NotExecuted': 1})
    inventory = js('full-case-inventory.json')
    assert inventory['current'] == inventory['previous'] + 13 and inventory['prior_cases_present'] and inventory['prior_passes_preserved']
    old = ET.fromstring(data['previous-full-core.trx']).findall('.//t:UnitTestResult', NS)
    current = ET.fromstring(data['results/archive-creation-core.trx']).findall('.//t:UnitTestResult', NS)
    names = Counter(row.get('testName') for row in current)
    assert all(names[name] == count for name, count in Counter(row.get('testName') for row in old).items())
    passed = Counter(row.get('testName') for row in current if row.get('outcome') == 'Passed')
    assert all(passed[name] >= count for name, count in Counter(row.get('testName') for row in old if row.get('outcome') == 'Passed').items())
    if platform == 'windows':
        for name, count in [('creation-native-ui', 10), ('native', 217), ('creation-cleanup-crashes', 18), ('app-entry', 14)]:
            assert tests[name + '.trx'] == {'Passed': count}
        assert not js('account.json')['AdministratorsMember'] and js('account.json')['HiveLoaded']
        assert js('standard-launch.json')['ExitCode'] == 0
    audit = js('compiler-sources/facts.json')
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for binary in audit['binaries']:
        for suffix, key in [('dll', 'dllSha256'), ('pdb', 'pdbSha256')]:
            assert index['binaries/' + binary['assembly'] + '.' + suffix]['sha256'] == binary[key].lower()
    paths = subprocess.check_output(['git', 'diff', '--name-only', '98a404e6', '-z'], cwd=ROOT).decode().split('\0')
    paths += subprocess.check_output(['git', 'ls-files', '--others', '--exclude-standard', '-z'], cwd=ROOT).decode().split('\0')
    changed = sorted({name for name in paths if name.endswith('.cs')})
    for name in changed:
        rows = [row for row in audit['sources'] if row['document'].replace('\\', '/').endswith('/' + name)]
        assert rows and all(row['sha256'].lower() == BASE['sha'](ROOT / name) for row in rows), name
    for path, key, count in [('catalog/independent-catalog.json', 'cases', 273), ('workers/independent-pipeline.json', 'archives', 74),
        ('native-vectors/independent-pipeline.json', 'archives', 33 if platform == 'windows' else 21),
        ('workflows/independent-pipeline.json', 'archives', 6), ('workflows/independent-workflow-rar.json', 'official_archives', 6),
        ('workflows/independent-workflow-tar.json', 'archives', 8), ('jobs/independent-pipeline.json', 'archives', 6 if platform == 'windows' else 3),
        ('jobs/independent-workflow-rar.json', 'official_archives', 6 if platform == 'windows' else 4),
        ('jobs/independent-workflow-tar.json', 'archives', 8 if platform == 'windows' else 4)]:
        assert js(path)[key] == count, path
    if platform == 'windows':
        for folder, seven, tar in [('moves', (6, 33, 21), (8, 44, 28)), ('cleanup', (6, 9, 9), (8, 12, 12)), ('creation-ui', (3, 3, 3), (4, 4, 4))]:
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
    return dict(platform=platform, **facts, tests=tests, current_CSharp_sources_bound=len(changed),
        DLL_PDB_pairs_bound=len(audit['binaries']), census=census, production_backend_qualified=False)


if __name__ == '__main__':
    for platform in sys.argv[1:] or ('mac', 'ubuntu', 'windows'):
        print(json.dumps(verify(platform)), flush=True)
