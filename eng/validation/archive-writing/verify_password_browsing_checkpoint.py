"""Static evidence verification only: never run FileCat, tests or archive decoders on the host."""
from pathlib import Path, PurePosixPath
from collections import Counter
import argparse, hashlib, json, subprocess, tarfile, xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
NS = {'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def transport(packet, folder, retain=()):
    facts = json.loads(packet.with_suffix('.json').read_text(encoding='utf-8-sig'))
    with packet.open('rb') as stream: assert hashlib.file_digest(stream, 'sha256').hexdigest() == facts['sha256']
    assert packet.stat().st_size == facts['bytes']; index = {}; data = {}
    with tarfile.open(packet, 'r|gz') as tar:
        for item in tar:
            path = PurePosixPath(item.name)
            assert not path.is_absolute() and '..' not in path.parts and path.parts[0] == folder
            assert item.isdir() or item.isfile()
            if item.isdir(): continue
            key = path.relative_to(folder).as_posix(); assert key not in index
            stream = tar.extractfile(item)
            if key.endswith(('.json', '.trx', '.source')) or key in retain:
                content = stream.read(); data[key] = content; digest = hashlib.sha256(content).hexdigest()
            else: digest = hashlib.file_digest(stream, 'sha256').hexdigest()
            index[key] = {'bytes':item.size, 'sha256':digest}
    rows = json.loads(data['files.json'])['files']; assert len(index) == len(rows)+1
    for row in rows: assert index[row['path']] == {key:row[key] for key in ('bytes', 'sha256')}, row['path']
    return facts, index, data

def cases(content): return ET.fromstring(content).findall('.//t:UnitTestResult', NS)

def verify(platform, folder='awp-v3'):
    facts, index, data = transport(ROOT/'.tmp/password-evidence'/platform/(folder+'-evidence.tar.gz'), folder)
    js = lambda key: json.loads(data[key].decode('utf-8-sig'))
    candidate = js('delta.json')['candidate_commit']
    subprocess.check_call(['git', 'cat-file', '-e', candidate+'^{commit}'], cwd=ROOT)
    audit = js('compiler-sources/facts.json')
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for row in audit['binaries']:
        for ext, key in [('dll','dllSha256'), ('pdb','pdbSha256')]:
            assert index['binaries/'+row['assembly']+'.'+ext]['sha256'] == row[key].lower()
    source = {row['path']:row for row in js('source-files.json')['files']}; checked = []
    names = subprocess.check_output(['git','diff','--name-only','-z','630d3491',candidate,'--','src','tests'], cwd=ROOT).decode().split('\0')
    for name in names:
        if not name.endswith('.cs'): continue
        matches = [row for row in audit['sources'] if row['document'].replace('\\','/').endswith('/'+name)]
        assert matches, name
        blob = subprocess.check_output(['git','show',candidate+':'+name], cwd=ROOT).replace(b'\r\n',b'\n')
        for row in matches:
            key = 'compiler-sources/'+row['snapshot']
            assert index[key]['sha256'] == row['sha256'].lower() == source[name]['sha256'], name
            assert data[key].replace(b'\r\n',b'\n') == blob, name
        checked.append(name)
    tests = {Path(key).name:dict(Counter(row.get('outcome') for row in cases(value))) for key,value in data.items()
        if key.startswith('results/') and key.endswith('.trx')}
    assert tests['password-read-core.trx'] == {'Passed':20}
    assert tests['password-read-ui.trx'] == {'Passed':27}
    assert tests['recovery-container-preflight.trx'] == ({'Passed':44,'NotExecuted':8} if platform == 'windows' else {'Passed':52})
    assert tests['navigation-member-regressions.trx'] == ({'Passed':235,'NotExecuted':2} if platform == 'mac' else {'Passed':236,'NotExecuted':1})
    expected = {'mac':{'Passed':8266,'NotExecuted':207}, 'ubuntu':{'Passed':8288,'NotExecuted':185},
        'windows':{'Passed':8017,'NotExecuted':461}}[platform]
    assert tests['core-regressions.trx'] == expected, tests['core-regressions.trx']
    assert all('Failed' not in result for result in tests.values())
    old = cases(data['previous-full-core.trx']); current = cases(data['results/core-regressions.trx'])
    assert len(current) == len(old)+20
    assert not (Counter(row.get('testName') for row in old)-Counter(row.get('testName') for row in current))
    assert not (Counter(row.get('testName') for row in old if row.get('outcome')=='Passed')-
        Counter(row.get('testName') for row in current if row.get('outcome')=='Passed'))
    baseline = js('core-baseline-proof.json'); assert index['previous-full-core.trx'] == {key:baseline[key] for key in ('bytes','sha256')}
    cohort = 'awm-v5' if platform == 'mac' else 'awm-v4'
    _, old_index, _ = transport(ROOT/'.tmp/member-evidence'/platform/(cohort+'-evidence.tar.gz'), cohort)
    assert baseline['manifest_sha256'] == old_index['files.json']['sha256']
    assert index['previous-full-core.trx'] == old_index['results/archive-creation-core.trx']
    census = js('final-census.json'); assert census['owned_filecat_or_test_processes'] == 0 and not census['production_backend_qualified']
    if platform == 'windows':
        assert not js('account.json')['AdministratorsMember'] and js('account.json')['HiveLoaded']
        assert js('standard-launch.json')['ExitCode'] == 0
    elif platform == 'mac':
        assert census['preserved_wake_helper'].startswith('36765 ')
        assert js('case-fixture.json')['source'].endswith('/'+folder+'-cases')
        assert index['case-image-detach.stdout']['bytes'] > 0
    else:
        assert js('private-keyring.json')['pid'] == js('private-keyring-stopped.json')['pid']
    return {'platform':platform, 'folder':folder, 'candidate_source_commit':candidate, **facts, 'manifest_rows':len(index)-1,
        'tests':tests, 'CSharp_sources_bound':len(checked), 'DLL_PDB_pairs_bound':len(audit['binaries']),
        'all_prior_cases_and_passes_preserved':True, 'census':census, 'production_backend_qualified':False}

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--platform', choices=('mac','ubuntu','windows'), required=True)
    parser.add_argument('--folder', default='awp-v3'); args = parser.parse_args()
    print(json.dumps(verify(args.platform, args.folder), indent=2))
