"""Static transport/TRX/compiler-source verification for the source-case milestone checkout."""
from pathlib import Path, PurePosixPath
from collections import Counter
import hashlib, json, subprocess, sys, tarfile, xml.etree.ElementTree as ET

root=Path(__file__).resolve().parents[3]
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
batch='archive-source-case-v1'
names=subprocess.check_output(['git','diff','--name-only','c5d62947','-z'],cwd=root).decode().split('\0')
names+=subprocess.check_output(['git','ls-files','--others','--exclude-standard','-z'],cwd=root).decode().split('\0')
changed=sorted({name for name in names if name.endswith('.cs')});assert len(changed)==5,changed
assert {name for name in changed if name.startswith('src/')}=={'src/FileCat.Core/Archives/ArchiveJobPlanning.cs','src/FileCat.Core/Archives/ArchiveSelectionForest.cs'}

def sha(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()

def transport(stem,folder):
    archive=stem.with_name(stem.name+'.tar.gz');facts=json.loads(stem.with_suffix('.json').read_text())
    assert archive.stat().st_size==facts['bytes'] and sha(archive)==facts['sha256']
    observed={};retained={}
    with tarfile.open(archive,'r|gz') as tar:
        for member in tar:
            name=PurePosixPath(member.name)
            assert not name.is_absolute() and '..' not in name.parts and name.parts[0]==folder
            assert member.isdir() or member.isfile()
            if member.isdir():continue
            leaf=name.relative_to(folder).as_posix();assert leaf not in observed
            stream=tar.extractfile(member)
            if leaf.endswith(('.json','.trx')):
                data=stream.read();retained[leaf]=data;digest=hashlib.sha256(data).hexdigest()
            else:digest=hashlib.file_digest(stream,'sha256').hexdigest()
            observed[leaf]={'bytes':member.size,'sha256':digest}
    ledger=json.loads(retained['files.json'])['files']
    assert len(ledger)==facts['rows'] and len(observed)==len(ledger)+1
    for row in ledger:assert observed[row['path']]=={key:row[key] for key in ('bytes','sha256')},row['path']
    return facts,observed,retained

def verify(platform):
    facts,index,data=transport(root/'.tmp'/(platform+'-source-case-v1-evidence'),batch)
    def js(name):return json.loads(data[name].decode('utf-8-sig'))
    cases=ET.fromstring(data['results/archive-source-case-core.trx']).findall('.//t:UnitTestResult',ns)
    tests={}
    for name,content in data.items():
        if name.startswith('results/') and name.endswith('.trx'):
            counts=dict(Counter(row.get('outcome') for row in ET.fromstring(content).findall('.//t:UnitTestResult',ns)))
            assert counts.get('Passed',0) and not counts.get('Failed',0);tests[PurePosixPath(name).name]=counts
    assert tests['job-preflight.trx']=={'Passed':116}
    passed,skipped={'mac':(3811,55),'ubuntu':(3824,42),'windows':(3595,271)}[platform]
    assert tests['archive-source-case-core.trx']=={'Passed':passed,'NotExecuted':skipped}
    inventory=js('affected-case-inventory.json')
    assert inventory['previous_cases']==3864 and inventory['current_cases']==3866 and inventory['all_previous_passes_still_pass']
    prior_all=ET.fromstring(data['reused-full-core.trx']).findall('.//t:UnitTestResult',ns)
    assert len(prior_all)==(8325 if platform=='windows' else 8320) and all(row.get('outcome') in ('Passed','NotExecuted') for row in prior_all)
    prefixes=tuple('FileCat.Core.Tests.'+name for name in ('Archive','Strict','SevenZip','Rar','EmptySevenZip','UnixArchive','Zip'))
    prior=[row for row in prior_all if row.get('testName','').startswith(prefixes)]
    expected=Counter(row.get('testName') for row in prior);expected.update(inventory['added_cases'])
    assert Counter(row.get('testName') for row in cases)==expected
    previous_passes=Counter(row.get('testName') for row in prior if row.get('outcome')=='Passed')
    current_passes=Counter(row.get('testName') for row in cases if row.get('outcome')=='Passed')
    assert all(current_passes[name]>=count for name,count in previous_passes.items())
    version='v5' if platform=='windows' else 'v4'
    prior_archive=root/'.tmp'/(platform+'-native-jobs-'+version+'-evidence.tar.gz')
    baseline=json.loads((root/'docs/validation/archive-writing/native-archive-job-checkpoint.json').read_text())['platforms'][platform]
    assert sha(prior_archive)==baseline['sha256']
    with tarfile.open(prior_archive) as tar:
        prior_ledger_bytes=tar.extractfile('archive-native-jobs-'+version+'/files.json').read()
    previous=js('previous-evidence.json');assert previous['manifest_sha256']==hashlib.sha256(prior_ledger_bytes).hexdigest()
    old_index={row['path']:row for row in json.loads(prior_ledger_bytes)['files']}
    assert index['reused-full-core.trx']['sha256']==old_index['results/archive-native-jobs-core.trx']['sha256']
    audit=js('compiler-sources/facts.json')
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for binary in audit['binaries']:
        for suffix,key in (('dll','dllSha256'),('pdb','pdbSha256')):assert index['binaries/'+binary['assembly']+'.'+suffix]['sha256']==binary[key].lower()
    applicable=[name for name in changed if platform=='windows' or not name.startswith('tests/FileCat.Platform.Windows.Tests/')]
    for name in applicable:
        documents=[row for row in audit['sources'] if row['document'].replace('\\','/').endswith('/'+name)]
        assert documents and all(row['sha256'].lower()==sha(root/name) for row in documents),name
    assert index['harness-inventory-repair/corrected-driver.py']['sha256']==sha(root/'eng/validation/archive-writing/run_source_case_batch.py')
    assert js('harness-inventory-repair/original-failure.json')['CSharp_binaries_unchanged']
    assert not js('writing-closure.json')['Rar15_or_DiscUtils_present']
    for path,key,value in [('catalog/independent-catalog.json','cases',273),('workers/independent-pipeline.json','archives',74),
        ('native-vectors/independent-pipeline.json','archives',33 if platform=='windows' else 21),
        ('workflows/independent-pipeline.json','archives',6),('workflows/independent-workflow-rar.json','official_archives',6),
        ('workflows/independent-workflow-tar.json','archives',8),('jobs/independent-pipeline.json','archives',6 if platform=='windows' else 3),
        ('jobs/independent-workflow-rar.json','official_archives',6 if platform=='windows' else 4),('jobs/independent-workflow-tar.json','archives',8 if platform=='windows' else 4)]:
        assert js(path)[key]==value,(platform,path)
    if platform=='windows':
        assert tests['source-case-preflight.trx']=={'Passed':20} and tests['native.trx']=={'Passed':197} and tests['app-entry.trx']=={'Passed':10}
        assert not js('account.json')['AdministratorsMember'] and js('resume-standard-v2.json')['ExitCode']==0
        for path,keys in [('source-case/independent-pipeline.json',{'archives':6,'metadata':12,'payloads':12}),
            ('source-case/independent-workflow-rar.json',{'official_archives':6,'independent_metadata_records':12,'exact_payloads':12}),
            ('source-case/independent-workflow-tar.json',{'archives':8,'metadata':16,'payloads':16})]:
            assert all(js(path)[key]==value for key,value in keys.items()),path
    census=js('final-census.json');assert not census['busy'] and census['owned_filecat_or_test_processes']==0
    if platform=='mac':assert census['preserved_wake_helper'].startswith('36765 ')
    if platform=='ubuntu':assert js('private-keyring.json')['pid']==js('private-keyring-stopped.json')['pid']
    return dict(facts,tests=tests,reused_full_core_cases=len(prior_all),affected_inventory=inventory,changed_csharp_matched=applicable,
        assembly_pdb_pairs=len(audit['binaries']),compiler_source_documents=audit['documents'],final_census=census,
        source_case_independent_archives=20 if platform=='windows' else 0)

result={'date':'2026-11-08','baseline_commit':'c5d62947','production_profile_registered':False,'production_backend_qualified':False,
    'fix':'Preserve case-distinct native source leaves and parent directories before native capture.',
    'new_core_cases':2,'new_Windows_native_cases':20,'changed_csharp':changed,'static_verifier_sha256':sha(Path(__file__)),
    'limitations':['Native cleanup/F6, shared UI, Unix update/confinement, full resource/crash/fuzz/RID/package qualification and independent final review remain.'], 'platforms':{}}
for platform in sys.argv[1:] or ('mac','ubuntu','windows'):
    result['platforms'][platform]=verify(platform);value=result['platforms'][platform]
    print(json.dumps({platform:{'tests':value['tests'],'verified_rows':value['rows'],'changed_csharp_bound':len(value['changed_csharp_matched']),'owned_instances_left':0}}),flush=True)
if len(result['platforms'])==3:
    facts,index,data=transport(root/'.tmp/windows-native-source-case-reproduction-v1','native-source-case-reproduction-v1')
    proof=json.loads(data['facts.json']);assert proof['source_collapse_reproduced'] and proof['actual_sources']==1 and proof['expected_sources']==2
    with tarfile.open(root/'.tmp/windows-native-jobs-v5-evidence.tar.gz') as tar:
        old={row['path']:row for row in json.load(tar.extractfile('archive-native-jobs-v5/files.json'))['files']}
    assert proof['CoreSha256']==old['binaries/FileCat.Core.dll']['sha256']
    result['original_reproduction']=dict(facts,proof=proof)
    (root/'docs/validation/archive-writing/native-source-case-checkpoint.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
