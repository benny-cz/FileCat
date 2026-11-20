#!/usr/bin/env python3
"""Verify the sealed, focused three-platform v38 historical-capture cohort."""

from collections import Counter
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET


ROOT=Path(__file__).resolve().parents[3]
FOLDER='awe-storage-v38'
CANDIDATE='a59c7d2aa5515cb81f6c59a2af8a0e138a2437b8'
CHANGED={'src/FileCat.Core/Edit/EditSessions.Storage.cs',
         'src/FileCat.Core/Edit/EditSessions.CaptureRecovery.cs',
         'tests/ArchiveEditCaptureRecoveryCases.cs',
         'tests/ArchiveEditSaveCopySessionCases.cs',
         'eng/validation/archive-writing/run_edit_storage_batch.py'}
spec=importlib.util.spec_from_file_location('transport',Path(__file__).with_name('verify_password_browsing_checkpoint.py'))
transport=importlib.util.module_from_spec(spec);spec.loader.exec_module(transport)


def git(name):
    return subprocess.check_output(['git','show',CANDIDATE+':'+name],cwd=ROOT)


def verify(platform):
    _,index,data=transport.transport(ROOT/'.tmp/storage-evidence'/platform/(FOLDER+'-evidence.tar.gz'),FOLDER,
                                     retain=('resource-preflight.jsonl',))
    js=lambda name:json.loads(data[name].decode('utf-8-sig'))
    delta=js('delta.json')
    assert delta['candidate_commit']==CANDIDATE
    assert delta['separate_fuzz_checkpoint'].startswith('eng/validation/archive-writing/ArchiveFuzzHarness')
    rows={row['path']:row for row in delta['files']}
    changed=CHANGED|({'src/FileCat.App/ViewModels/MainViewModel.EditSessions.cs',
                     'tests/FileCat.App.Tests/EditRetainedCopyUiTests.cs'} if platform=='ubuntu' else set())
    assert set(rows)==changed
    ledger={row['path']:row for row in js('source-files.json')['files']}
    audit=js('compiler-sources/facts.json')
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for row in audit['binaries']:
        for extension,key in (('dll','dllSha256'),('pdb','pdbSha256')):
            assert index['binaries/'+row['assembly']+'.'+extension]['sha256']==row[key].lower()
    for name,row in rows.items():
        blob=git(name)
        assert row['bytes']==len(blob) and row['sha256']==hashlib.sha256(blob).hexdigest()
        assert ledger[name]['sha256']==row['sha256']
        if name.endswith('.cs'):
            matches=[item for item in audit['sources'] if item['document'].replace('\\','/').endswith('/'+name)]
            assert matches,name
            for item in matches:
                snapshot='compiler-sources/'+item['snapshot']
                assert index[snapshot]['sha256']==item['sha256'].lower()==row['sha256']
                assert data[snapshot].replace(b'\r\n',b'\n')==blob.replace(b'\r\n',b'\n')
    runner=git('eng/validation/archive-writing/run_edit_storage_batch.py')
    assert index['executed-storage-batch.py']=={'bytes':len(runner),'sha256':hashlib.sha256(runner).hexdigest()}
    scope=js('validation-scope.json')
    assert scope['scope']=='focused' and scope['expected_new_core_cases']==326 and not scope['production_backend_qualified']
    assert all(value=='not-run' for value in scope['full_regressions'].values())
    assert not any('results/'+name+'.trx' in data for name in scope['full_regressions'])
    resource=[json.loads(line) for line in data['resource-preflight.jsonl'].decode().splitlines()]
    assert [row['phase'] for row in resource]==['build','focused']
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
    assert tests['retained-copy-ui']=={'Passed':1}
    assert tests['capture-recovery']=={'Passed':33}
    assert tests['save-copy-session']=={'Passed':26}
    assert tests['native-discard']=={'Passed':40 if platform=='windows' else 3}
    assert tests['storage-admission']=={'Passed':45}
    assert tests['discard-ui']==({'Passed':6} if platform=='windows' else {'Passed':3,'NotExecuted':3})
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
