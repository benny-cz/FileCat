"""Static evidence checks only; no application, test or decoder execution on the host."""
from pathlib import Path
from collections import Counter
import importlib.util, json, subprocess

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('password_evidence', Path(__file__).with_name('verify_password_browsing_checkpoint.py'))
base = importlib.util.module_from_spec(spec); spec.loader.exec_module(base)

def verify(platform, folder):
    packet = ROOT/'.tmp/edit-evidence'/platform/(folder+'-evidence.tar.gz')
    facts, index, data = base.transport(packet, folder)
    js = lambda key: json.loads(data[key].decode('utf-8-sig'))
    candidate = js('delta.json')['candidate_commit']
    subprocess.check_call(['git', 'cat-file', '-e', candidate+'^{commit}'], cwd=ROOT)
    audit = js('compiler-sources/facts.json')
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for row in audit['binaries']:
        for ext, key in [('dll', 'dllSha256'), ('pdb', 'pdbSha256')]:
            assert index['binaries/'+row['assembly']+'.'+ext]['sha256'] == row[key].lower()
    ledger = {row['path']:row for row in js('source-files.json')['files']}; checked = []
    names = subprocess.check_output(['git', 'diff', '--name-only', '-z', 'ff8cc93e', candidate, '--', 'src', 'tests'], cwd=ROOT).decode().split('\0')
    for name in names:
        if not name.endswith('.cs'): continue
        matches = [row for row in audit['sources'] if row['document'].replace('\\', '/').endswith('/'+name)]
        assert matches, name
        blob = subprocess.check_output(['git', 'show', candidate+':'+name], cwd=ROOT).replace(b'\r\n', b'\n')
        for row in matches:
            key = 'compiler-sources/'+row['snapshot']
            assert index[key]['sha256'] == row['sha256'].lower() == ledger[name]['sha256']
            assert data[key].replace(b'\r\n', b'\n') == blob
        checked.append(name)
    tests = {Path(key).name:dict(Counter(row.get('outcome') for row in base.cases(value)))
        for key,value in data.items() if key.startswith('results/') and key.endswith('.trx')}
    extra = 4 if folder == 'awe-v3' else 0
    expected_focused = {'windows':{'Passed':134+extra,'NotExecuted':21},
        'mac':{'Passed':152+extra,'NotExecuted':3},'ubuntu':{'Passed':150+extra,'NotExecuted':5}}[platform]
    assert tests['edit-core.trx'] == expected_focused
    assert tests['edit-ui.trx'] == {'Passed':259}
    final = 'core-regressions.trx' in tests
    if final:
        assert tests['core-regressions.trx'] == {'windows':{'Passed':8061+extra,'NotExecuted':465},
            'mac':{'Passed':8314+extra,'NotExecuted':207},'ubuntu':{'Passed':8336+extra,'NotExecuted':185}}[platform]
        old = base.cases(data['previous-full-core.trx']); current = base.cases(data['results/core-regressions.trx'])
        assert len(current) == len(old)+(52 if platform == 'windows' else 4) if folder == 'awe-v3' else len(current) == len(old)+48
        assert not (Counter(row.get('testName') for row in old)-Counter(row.get('testName') for row in current))
        assert not (Counter(row.get('testName') for row in old if row.get('outcome')=='Passed')-
            Counter(row.get('testName') for row in current if row.get('outcome')=='Passed'))
        assert js('full-case-inventory.json')['missing_prior_cases'] == []
        assert js('full-case-inventory.json')['lost_prior_passes'] == []
    census = js('final-census.json'); assert census['owned_filecat_or_test_processes'] == 0
    if platform == 'mac': assert '36765' in census['preserved_wake_helper']
    if platform == 'windows':
        account = js('account.json'); assert account['AdministratorsMember'] is False and account['HiveLoaded'] is True
        if folder == 'awe-v1': assert not final and tests['native-edit-ui.trx'].get('Failed') == 1
        else: assert tests['native-edit-ui.trx'] == {'Passed':10}
    result = dict(platform=platform, folder=folder, candidate=candidate, **facts,
        manifest_sha256=index['files.json']['sha256'],
        changed_compiler_inputs=len(checked), binaries=len(audit['binaries']), tests=tests,
        full_core_inventory_verified=final, production_backend_qualified=False, final_census=census)
    return result

def verify_native(folder='awe-v3-native-v2', parent=None):
    parent = parent or verify('windows', 'awe-v3')
    facts,index,data = base.transport(ROOT/'.tmp/edit-evidence/windows'/(folder+'-evidence.tar.gz'),folder)
    js = lambda key: json.loads(data[key].decode('utf-8-sig'))
    reused = js('reused-parent.json')
    assert reused['folder'] == parent['folder'] and reused['manifest_sha256'] == parent['manifest_sha256']
    candidate = js('delta.json')['candidate_commit']; assert candidate == parent['candidate']
    audit = js('compiler-sources/facts.json')
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for row in audit['binaries']:
        for ext,key in [('dll','dllSha256'),('pdb','pdbSha256')]:
            assert index['binaries/'+row['assembly']+'.'+ext]['sha256'] == row[key].lower()
    ledger = {row['path']:row for row in js('source-files.json')['files']}; checked = []
    names = subprocess.check_output(['git','diff','--name-only','-z','ff8cc93e',candidate,'--','src','tests'],cwd=ROOT).decode().split('\0')
    for name in names:
        if not name.endswith('.cs'): continue
        matches = [row for row in audit['sources'] if row['document'].replace('\\','/').endswith('/'+name)]
        assert matches,name
        blob = subprocess.check_output(['git','show',candidate+':'+name],cwd=ROOT).replace(b'\r\n',b'\n')
        for row in matches:
            key = 'compiler-sources/'+row['snapshot']
            assert index[key]['sha256'] == row['sha256'].lower() == ledger[name]['sha256']
            assert data[key].replace(b'\r\n',b'\n') == blob
        checked.append(name)
    tests = {Path(key).name:dict(Counter(row.get('outcome') for row in base.cases(value)))
        for key,value in data.items() if key.startswith('results/') and key.endswith('.trx')}
    assert tests == {name+'.trx':{'Passed':count} for name,count in [('native',217),('cleanup-crashes',18),('app-entry',14),('member-native-ui',10)]}
    census = js('final-census.json'); assert census['owned_filecat_or_test_processes'] == 0
    account = js('account.json'); assert account['AdministratorsMember'] is False and account['HiveLoaded'] is True
    return dict(platform='windows',folder=folder,candidate=candidate,**facts,manifest_sha256=index['files.json']['sha256'],
        parent_manifest_sha256=reused['manifest_sha256'],changed_compiler_inputs=len(checked),binaries=len(audit['binaries']),
        tests=tests,final_census=census,production_backend_qualified=False)

if __name__ == '__main__':
    import sys
    result = verify_native(*sys.argv[2:]) if sys.argv[1] == 'native' else verify(*sys.argv[1:])
    print(json.dumps(result, indent=2))
