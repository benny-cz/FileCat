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
    diagnostic_exclusions=js('delta.json').get('excluded_diagnostic_sources',[])
    if diagnostic_exclusions:
        assert {row['path'] for row in diagnostic_exclusions}=={
            'eng/validation/archive-writing/EditCopyReceiptProbe/Probe.csproj','eng/validation/archive-writing/EditCopyReceiptProbe/Program.cs'}
        for row in diagnostic_exclusions:
            blob=subprocess.check_output(['git','show',candidate+':'+row['path']],cwd=ROOT)
            assert len(blob)==row['bytes'] and hashlib.sha256(blob).hexdigest()==row['sha256']
            assert row['reason']=='Standalone read-only diagnostic tool, outside this batch compiled product/test graph.'
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
    scope='full'
    if folder in ('awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28'):
        coverage=js('validation-scope.json');scope=coverage['scope']
        assert scope==('full' if folder=='awe-storage-v28' else 'focused') and coverage['expected_new_core_cases']=={'awe-storage-v24':219,'awe-storage-v25':220,'awe-storage-v26':225,'awe-storage-v27':235,'awe-storage-v28':235}[folder] and not coverage['production_backend_qualified']
        assert coverage['full_regressions']=={name:'required' if scope=='full' else 'not-run' for name in ('complete-core','complete-remote','complete-app')}
        if scope=='focused':assert not any('results/'+name+'.trx' in data for name in coverage['full_regressions'])
    complete='tests-complete.json' in data
    if complete:
        cleanup=folder in ('awe-storage-v8','awe-storage-v9','awe-storage-v10','awe-storage-v11','awe-storage-v12','awe-storage-v13','awe-storage-v14','awe-storage-v15','awe-storage-v16','awe-storage-v17','awe-storage-v18','awe-storage-v19','awe-storage-v20','awe-storage-v21','awe-storage-v22','awe-storage-v23','awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28')
        assert tests['record-recovery']=={'Passed':25 if folder in ('awe-storage-v27','awe-storage-v28') else 19} and tests['native-directory']=={'Passed':21 if folder in ('awe-storage-v12','awe-storage-v13','awe-storage-v14','awe-storage-v15','awe-storage-v16','awe-storage-v17','awe-storage-v18','awe-storage-v19','awe-storage-v20','awe-storage-v21','awe-storage-v22','awe-storage-v23','awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28') else 20 if cleanup else 16}
        expected=(14 if platform=='windows' else 19) if folder in ('awe-storage-v5','awe-storage-v6','awe-storage-v7','awe-storage-v8','awe-storage-v9','awe-storage-v10','awe-storage-v11','awe-storage-v12','awe-storage-v13','awe-storage-v14','awe-storage-v15','awe-storage-v16','awe-storage-v17','awe-storage-v18','awe-storage-v19','awe-storage-v20','awe-storage-v21','awe-storage-v22','awe-storage-v23','awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28') else (13 if platform=='windows' else 14)
        assert tests['native-storage']=={'Passed':expected}
        gates={'record-recovery','native-directory','native-storage','edit-core','edit-ui'}
        if folder in ('awe-storage-v6','awe-storage-v7','awe-storage-v8','awe-storage-v9','awe-storage-v10','awe-storage-v11','awe-storage-v12','awe-storage-v13','awe-storage-v14','awe-storage-v15','awe-storage-v16','awe-storage-v17','awe-storage-v18','awe-storage-v19','awe-storage-v20','awe-storage-v21','awe-storage-v22','awe-storage-v23','awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28'):
            snapshots=35 if folder in ('awe-storage-v11','awe-storage-v12','awe-storage-v13','awe-storage-v14','awe-storage-v15','awe-storage-v16','awe-storage-v17','awe-storage-v18','awe-storage-v19','awe-storage-v20','awe-storage-v21','awe-storage-v22','awe-storage-v23','awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28') and platform=='windows' else 34 if cleanup else 26
            gates.update(('native-snapshot','commit-copy'));assert tests['native-snapshot']=={'Passed':snapshots} and tests['commit-copy']=={'Passed':10}
        if folder in ('awe-storage-v11','awe-storage-v12','awe-storage-v13','awe-storage-v14','awe-storage-v15','awe-storage-v16','awe-storage-v17','awe-storage-v18','awe-storage-v19','awe-storage-v20','awe-storage-v21','awe-storage-v22','awe-storage-v23','awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28'):
            gates.add('capture-recovery');assert tests['capture-recovery']=={'Passed':31 if folder in ('awe-storage-v27','awe-storage-v28') else 27}
            origins=['origin-policy']+(['native-origin-regressions'] if platform=='windows' else [])
            for name in origins:
                gates.add(name);assert set(tests[name])=={'Passed'} and tests[name]['Passed']>0
        if folder in ('awe-storage-v13','awe-storage-v14','awe-storage-v15','awe-storage-v16','awe-storage-v17','awe-storage-v18','awe-storage-v19','awe-storage-v20','awe-storage-v21','awe-storage-v22','awe-storage-v23','awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28'):
            if scope=='full':
                gates.update(('complete-core','complete-app'))
                assert tests['complete-core'].get('Passed',0)>8000 and tests['complete-app'].get('Passed',0)>190
        if folder in ('awe-storage-v15','awe-storage-v16','awe-storage-v17','awe-storage-v18','awe-storage-v19','awe-storage-v20','awe-storage-v21','awe-storage-v22','awe-storage-v23','awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28'):
            gates.update(('native-legacy-zip','history-retirement-core','history-retirement-ui'))
            assert tests['native-legacy-zip']=={'Passed':18} and tests['history-retirement-ui']=={'Passed':18}
            assert set(tests['history-retirement-core'])=={'Passed'} and tests['history-retirement-core']['Passed']>0
            if scope=='full':
                baseline=js('baseline-core.json')
                _,prior_index,prior_data=base.transport(ROOT/'.tmp/edit-evidence'/platform/'awe-v3-evidence.tar.gz','awe-v3')
                assert baseline['folder']=='awe-v3' and baseline['manifest_sha256']==prior_index['files.json']['sha256']
                assert baseline['trx_sha256']==hashlib.sha256(data['previous-full-core.trx']).hexdigest()==prior_index['results/core-regressions.trx']['sha256']
                old=base.cases(prior_data['results/core-regressions.trx']);current=base.cases(data['results/complete-core.trx'])
                if folder in ('awe-storage-v16','awe-storage-v17','awe-storage-v18','awe-storage-v19','awe-storage-v20','awe-storage-v21','awe-storage-v22','awe-storage-v23','awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28'):
                    spec=importlib.util.spec_from_file_location('edit_inventory',Path(__file__).with_name('edit_core_inventory.py'))
                    helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)
                    inventory=helper.review(old,current,235 if folder in ('awe-storage-v27','awe-storage-v28') else 225 if folder=='awe-storage-v26' else 220 if folder=='awe-storage-v25' else 219 if folder=='awe-storage-v24' else 174 if folder=='awe-storage-v23' else 170 if folder=='awe-storage-v22' else 153);assert js('full-case-inventory.json')==inventory;helper.require_complete(inventory)
                    if platform=='ubuntu':
                        gates.add('picture-boundary');assert set(tests['picture-boundary'])=={'Passed'} and tests['picture-boundary']['Passed']>0
                else:
                    names=lambda cases,passed=False:Counter(row.get('testName') for row in cases if not passed or row.get('outcome')=='Passed')
                    missing=sorted((names(old)-names(current)).elements());lost=sorted((names(old,True)-names(current,True)).elements())
                    assert js('full-case-inventory.json')==dict(previous=len(old),current=len(current),missing_prior_cases=missing,lost_prior_passes=lost,expected_new_cases=153)
                    assert not missing and not lost and len(current)==len(old)+153
        if folder in ('awe-storage-v17','awe-storage-v18','awe-storage-v19','awe-storage-v20','awe-storage-v21','awe-storage-v22','awe-storage-v23','awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28'):
            gates.add('native-sftp');assert tests['native-sftp']=={'Passed':23}
            if scope=='full':
                gates.add('complete-remote');assert tests['complete-remote'].get('Passed',0)>0
        if folder in ('awe-storage-v18','awe-storage-v19','awe-storage-v20','awe-storage-v21','awe-storage-v22','awe-storage-v23','awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28'):
            gates.add('verification-ui');assert tests['verification-ui']=={'Passed':12 if folder in ('awe-storage-v20','awe-storage-v21','awe-storage-v22','awe-storage-v23','awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28') else 11,'NotExecuted':1}
        if folder in ('awe-storage-v22','awe-storage-v23','awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28'):
            gates.add('save-copy-preparation');assert tests['save-copy-preparation']=={'Passed':17}
        if folder in ('awe-storage-v23','awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28'):
            gates.add('comparison-retirement');assert tests['comparison-retirement']=={'Passed':108}
        if folder in ('awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28'):
            gates.update(('save-copy-publication','save-copy-record'))
            assert tests['save-copy-publication']=={'Passed':26 if folder in ('awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28') and platform!='windows' else 25} and tests['save-copy-record']=={'Passed':25 if folder in ('awe-storage-v26','awe-storage-v27','awe-storage-v28') else 20}
            if platform=='ubuntu':
                gates.add('picture-boundary');assert set(tests['picture-boundary'])=={'Passed'} and tests['picture-boundary']['Passed']>0
        if cleanup:
            gates.add('job-input-regressions');assert tests['job-input-regressions'].get('Passed',0)>0
        assert set(tests)==gates
        assert all('Failed' not in counts for counts in tests.values())
        assert tests['edit-core'].get('Passed',0)>90 and tests['edit-ui'].get('Passed',0)>0
    return dict(platform=platform,folder=folder,candidate=candidate,**facts,manifest_sha256=index['files.json']['sha256'],
        changed_CSharp_sources_bound=len(bound),DLL_PDB_pairs_bound=len(audit['binaries']),uncompiled_platform_sources=sorted(excluded),
        tests=tests,focused_gates_complete=complete,final_census=census,production_backend_qualified=False,
        **(dict(validation_scope=scope,full_regressions_not_run=sorted(name for name in coverage['full_regressions'] if 'results/'+name+'.trx' not in data)) if folder in ('awe-storage-v24','awe-storage-v25','awe-storage-v26','awe-storage-v27','awe-storage-v28') else {}),
        **(dict(excluded_diagnostic_sources=diagnostic_exclusions) if diagnostic_exclusions else {}))

def unchanged_product_binaries(platform,previous,current):
    _,old,_=base.transport(ROOT/'.tmp/storage-evidence'/platform/(previous+'-evidence.tar.gz'),previous)
    _,new,_=base.transport(ROOT/'.tmp/storage-evidence'/platform/(current+'-evidence.tar.gz'),current)
    names=sorted(name for name in new if name.startswith('binaries/') and '.Tests.' not in name and name.endswith(('.dll','.pdb')))
    assert names and all(new[name]==old[name] for name in names),platform
    return dict(platform=platform,previous=previous,current=current,unchanged_product_binaries=names)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--platform',choices=('windows','mac','ubuntu'));parser.add_argument('--folder')
    parser.add_argument('--checkpoint',type=Path,help='Verify another preserved storage checkpoint with the same evidence schema.')
    args=parser.parse_args()
    if bool(args.platform)!=bool(args.folder):parser.error('Specify both --platform and --folder, or neither to verify the checkpoint.')
    if args.platform and args.checkpoint:parser.error('Specify either one platform/folder or a checkpoint file.')
    if args.platform:print(json.dumps(verify(args.platform,args.folder),indent=2))
    else:
        checkpoint=json.loads((args.checkpoint or ROOT/'docs/validation/archive-writing/edit-storage-checkpoint.json').read_text())
        assert checkpoint['schema']==1 and not checkpoint['production_backend_qualified'] and not checkpoint['application_composition_uses_native_store']
        rows=checkpoint['history']+checkpoint['current']
        for row in rows:assert verify(row['platform'],row['folder'])==row,(row['platform'],row['folder'])
        for proof in checkpoint.get('fixture_retry_product_binaries',[]):
            assert unchanged_product_binaries(proof['platform'],proof['previous'],proof['current'])==proof
        for refusal in checkpoint.get('postponed',[])+checkpoint.get('unapplied_startup_refusals',[]):
            trace=refusal['trace'];content=(ROOT/trace['path']).read_bytes()
            assert len(content)==trace['bytes'] and hashlib.sha256(content).hexdigest()==trace['sha256']
            assert not refusal['source_delta_applied'] and not refusal['tests_started'] and b'Another tester is active; postpone.' in content
        for probe in checkpoint.get('read_only_parser_probes',[]):
            artifacts={}
            for trace in probe['traces']:
                path=(ROOT/trace['path']).resolve();assert path.is_relative_to(ROOT.resolve())
                content=path.read_bytes();assert len(content)==trace['bytes'] and hashlib.sha256(content).hexdigest()==trace['sha256']
                artifacts[path.name]=content
            result=json.loads(artifacts[probe.get('result_file_name','result.json')])
            assert result['accepted']==probe['accepted'] and result['rejected']==probe['controlled_refusals']
            assert len(result['unexpected'])==probe['unexpected_exceptions'] and result['sha256'].lower()==probe['assembly_sha256']
            folder=probe.get('folder','awe-storage-v25')
            _,index,data=base.transport(ROOT/'.tmp/storage-evidence'/probe['platform']/(folder+'-evidence.tar.gz'),folder)
            assert json.loads(data['delta.json'])['candidate_commit']==probe['candidate']
            assert index['binaries/FileCat.Core.dll']['sha256']==probe['assembly_sha256']
        print(json.dumps(dict(bundles_verified=len(rows),bytes_verified=sum(row['bytes'] for row in rows),production_backend_qualified=False)))
