"""Verify preserved edit-storage evidence statically; never run product code or tests on the host."""
from pathlib import Path
from collections import Counter
import argparse,hashlib,importlib.util,json,subprocess,xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[3]
spec=importlib.util.spec_from_file_location('storage_transport',Path(__file__).with_name('verify_password_browsing_checkpoint.py'))
base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)

def verify(platform,folder):
    facts,index,data=base.transport(ROOT/'.tmp/storage-evidence'/platform/(folder+'-evidence.tar.gz'),folder)
    js=lambda name:json.loads(data[name].decode('utf-8-sig'))
    candidate=js('delta.json')['candidate_commit']
    subprocess.check_call(['git','cat-file','-e',candidate+'^{commit}'],cwd=ROOT)
    audit=js('compiler-sources/facts.json')
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for row in audit['binaries']:
        for extension,key in [('dll','dllSha256'),('pdb','pdbSha256')]:
            assert index['binaries/'+row['assembly']+'.'+extension]['sha256']==row[key].lower()
    ledger={row['path']:row for row in js('source-files.json')['files']}
    if 'uncompiled-platform-sources.json' not in data:assert platform=='windows'
    excluded={row['path']:row for row in js('uncompiled-platform-sources.json')} if 'uncompiled-platform-sources.json' in data else {}
    changed=subprocess.check_output(['git','diff','--name-only','-z','e23c1b7f',candidate,'--','src','tests'],cwd=ROOT).decode().split('\0')
    bound=[]
    for name in changed:
        if not name.endswith('.cs'):continue
        blob=subprocess.check_output(['git','show',candidate+':'+name],cwd=ROOT)
        if platform!='windows' and name.startswith('tests/FileCat.Platform.Windows.Tests/'):
            normalized=blob.replace(b'\r\n',b'\n')
            assert ledger[name]['sha256'] in {hashlib.sha256(normalized).hexdigest(),hashlib.sha256(normalized.replace(b'\n',b'\r\n')).hexdigest()}
            if name in excluded:assert excluded[name]['sha256']==ledger[name]['sha256']
            else:excluded[name]=dict(path=name,sha256=ledger[name]['sha256'],reason='Inherited unchanged Windows-only source; not compiled on Unix.')
            continue
        matches=[row for row in audit['sources'] if row['document'].replace('\\','/').endswith('/'+name)]
        assert matches,name
        for row in matches:
            key='compiler-sources/'+row['snapshot']
            assert index[key]['sha256']==row['sha256'].lower()==ledger[name]['sha256'],name
            assert data[key].replace(b'\r\n',b'\n')==blob.replace(b'\r\n',b'\n'),name
        bound.append(name)
    tests={}
    for name,content in data.items():
        if not(name.startswith('results/') and name.endswith('.trx')):continue
        counts=dict(Counter(row.get('outcome') for row in base.cases(content)))
        counters=ET.fromstring(content).find('.//t:Counters',base.NS).attrib
        assert sum(counts.values())==int(counters['total'])
        assert counts.get('Passed',0)==int(counters['passed']) and counts.get('Failed',0)==int(counters['failed'])
        tests[Path(name).stem]=counts
    census=js('final-census.json');assert census['owned_filecat_or_test_processes']==0 and not census['production_backend_qualified']
    if platform=='mac':assert census['preserved_wake_helper'].startswith('36765 ')
    if platform=='windows':assert not js('account.json')['AdministratorsMember'] and js('account.json')['HiveLoaded']
    complete='tests-complete.json' in data
    if complete:
        assert tests['record-recovery']=={'Passed':19} and tests['native-directory']=={'Passed':16}
        expected=(14 if platform=='windows' else 19) if folder=='awe-storage-v5' else (13 if platform=='windows' else 14)
        assert tests['native-storage']=={'Passed':expected}
        assert set(tests)=={'record-recovery','native-directory','native-storage','edit-core','edit-ui'}
        assert all('Failed' not in counts for counts in tests.values())
        assert tests['edit-core'].get('Passed',0)>90 and tests['edit-ui'].get('Passed',0)>0
    return dict(platform=platform,folder=folder,candidate=candidate,**facts,manifest_sha256=index['files.json']['sha256'],
        changed_CSharp_sources_bound=len(bound),DLL_PDB_pairs_bound=len(audit['binaries']),uncompiled_platform_sources=sorted(excluded),
        tests=tests,focused_gates_complete=complete,final_census=census,production_backend_qualified=False)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--platform',choices=('windows','mac','ubuntu'),required=True);parser.add_argument('--folder',required=True)
    args=parser.parse_args();print(json.dumps(verify(args.platform,args.folder),indent=2))
