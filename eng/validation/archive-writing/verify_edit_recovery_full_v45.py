#!/usr/bin/env python3
"""Verify the sealed v45 complete recovery-byte regressions after UI and test recorder fixes."""

from collections import Counter
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET


ROOT=Path(__file__).resolve().parents[3]
FOLDER='awe-storage-v45'
CANDIDATE='bf6690d4cc6775218b7b3777d7618af023b0a1e5'
CHANGED={'eng/validation/archive-writing/run_edit_storage_batch.py',
         'src/FileCat.App/ViewModels/MainViewModel.EditSessions.cs',
         'tests/FileCat.Core.Tests/ContentAndToolTests.cs'}
spec=importlib.util.spec_from_file_location('transport',Path(__file__).with_name('verify_password_browsing_checkpoint.py'))
transport=importlib.util.module_from_spec(spec);spec.loader.exec_module(transport)


def git(name):
    return subprocess.check_output(['git','show',CANDIDATE+':'+name],cwd=ROOT)


def committed_checkout(name,expected):
    blob=git(name)
    for value in (blob,blob.replace(b'\n',b'\r\n')):
        if {'bytes':len(value),'sha256':hashlib.sha256(value).hexdigest()}==expected:
            return value
    raise AssertionError('Sealed source differs from the committed Git blob: '+name)


def verify(platform):
    _,index,data=transport.transport(ROOT/'.tmp/storage-evidence'/platform/(FOLDER+'-evidence.tar.gz'),FOLDER,
                                     retain=('resource-preflight.jsonl',))
    js=lambda name:json.loads(data[name].decode('utf-8-sig'))
    delta=js('delta.json')
    assert delta['candidate_commit']==CANDIDATE
    assert delta['separate_fuzz_checkpoint'].startswith('eng/validation/archive-writing/ArchiveFuzzHarness')
    rows={row['path']:row for row in delta['files']}
    changed=CHANGED-({'src/FileCat.App/ViewModels/MainViewModel.EditSessions.cs'} if platform=='mac' else set())
    assert set(rows)==changed
    prior={'mac':'awe-storage-v44','windows':'awe-storage-v43','ubuntu':'awe-storage-v40'}[platform]
    _,previous_index,previous_data=transport.transport(ROOT/'.tmp/storage-evidence'/platform/(prior+'-evidence.tar.gz'),prior)
    previous_ledger={row['path']:row for row in json.loads(previous_data['source-files.json'])['files']}
    predecessor=js('previous-evidence.json')
    assert predecessor['folder']==prior and predecessor['manifest_sha256']==previous_index['files.json']['sha256']
    for name,row in rows.items():
        assert row['previous_sha256']==previous_ledger[name]['sha256']
    ledger={row['path']:row for row in js('source-files.json')['files']}
    uncompiled={row['path']:row for row in js('uncompiled-platform-sources.json')}
    audit=js('compiler-sources/facts.json')
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for row in audit['binaries']:
        for extension,key in (('dll','dllSha256'),('pdb','pdbSha256')):
            assert index['binaries/'+row['assembly']+'.'+extension]['sha256']==row[key].lower()
    for name,row in rows.items():
        blob=committed_checkout(name,{key:row[key] for key in ('bytes','sha256')})
        assert ledger[name]['sha256']==row['sha256']
        if name.endswith('.cs'):
            matches=[item for item in audit['sources'] if item['document'].replace('\\','/').endswith('/'+name)]
            if platform!='windows' and name.startswith('tests/FileCat.Platform.Windows.Tests/'):
                assert not matches and uncompiled[name]['sha256']==row['sha256']
                continue
            assert matches,name
            for item in matches:
                snapshot='compiler-sources/'+item['snapshot']
                assert index[snapshot]['sha256']==item['sha256'].lower()==row['sha256']
                assert data[snapshot].replace(b'\r\n',b'\n')==blob.replace(b'\r\n',b'\n')
    runner=committed_checkout('eng/validation/archive-writing/run_edit_storage_batch.py',index['executed-storage-batch.py'])
    scope=js('validation-scope.json')
    assert scope['scope']=='full' and scope['expected_new_core_cases']==335 and not scope['production_backend_qualified']
    assert all(value=='required' for value in scope['full_regressions'].values())
    assert all('results/'+name+'.trx' in data for name in scope['full_regressions'])
    resource=[json.loads(line) for line in data['resource-preflight.jsonl'].decode().splitlines()]
    assert [row['phase'] for row in resource]==['build','focused','complete-core','complete-remote','complete-app']
    assert all(item['free']>=item['minimum_free']==8*1024**3 for row in resource for item in row['filesystems'])
    complete=js('tests-complete.json')
    assert set(complete)==set(js('passed-gates.json'))
    tests={}
    for name,content in data.items():
        if not(name.startswith('results/') and name.endswith('.trx')):continue
        counts=dict(Counter(row.get('outcome') for row in transport.cases(content)))
        counters=ET.fromstring(content).find('.//t:Counters',transport.NS).attrib
        assert sum(counts.values())==int(counters['total'])
        assert counts.get('Passed',0)==int(counters['passed']) and counts.get('Failed',0)==int(counters['failed'])
        assert counters==complete[Path(name).stem]
        assert int(counters['failed'])==0 and int(counters['passed'])>0
        tests[Path(name).stem]=counts
    assert set(tests)==set(complete)
    assert tests['retained-copy-ui']=={'Passed':2}
    assert tests['capture-recovery']=={'Passed':33}
    assert tests['save-copy-session']=={'Passed':26}
    assert tests['recovered-save-copy']=={'Passed':9}
    assert tests['native-discard']=={'Passed':40 if platform=='windows' else 3}
    assert tests['storage-admission']=={'Passed':45}
    assert tests['discard-ui']==({'Passed':6} if platform=='windows' else {'Passed':3,'NotExecuted':3})
    assert all(tests[name].get('Failed',0)==0 and tests[name].get('Passed',0)>0 for name in scope['full_regressions'])
    attempts=js('full-regression-attempts.json')
    assert [row['name'] for row in attempts]==['complete-core','complete-remote','complete-app']
    assert all(row['status']=='passed' for row in attempts)
    inventory=js('full-case-inventory.json')
    assert inventory['expected_new_cases']==335 and inventory['current']==inventory['previous']+335
    assert not inventory['missing_prior_cases'] and not inventory['lost_prior_passes']
    census=js('final-census.json')
    assert census['owned_filecat_or_test_processes']==0 and not census['production_backend_qualified'] and not census['storage_integrated']
    if platform=='mac':assert census['preserved_wake_helper'].startswith('36765 ')
    if platform=='windows':
        account=js('account.json')
        assert not account['AdministratorsMember'] and account['HiveLoaded']
    return {'platform':platform,'candidate':CANDIDATE,'gates':len(tests),
            'retained_copy_ui':tests['retained-copy-ui'],'native_discard':tests['native-discard'],
            'storage_admission':tests['storage-admission'],
            'transport_bytes':(ROOT/'.tmp/storage-evidence'/platform/(FOLDER+'-evidence.tar.gz')).stat().st_size}


if __name__=='__main__':
    print(json.dumps([verify(platform) for platform in sys.argv[1:]],indent=2))
