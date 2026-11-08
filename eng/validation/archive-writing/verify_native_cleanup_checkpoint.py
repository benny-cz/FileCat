"""Static guest-evidence verification: transport, tests, compiler inputs and independent readers; no host tests."""
from pathlib import Path, PurePosixPath
from collections import Counter
import hashlib, json, subprocess, sys, tarfile, xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[3]
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def sha(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()

def transport(stem,folder,capture_sources=False):
    archive=stem.with_name(stem.name+'.tar.gz');facts=json.loads(stem.with_suffix('.json').read_text())
    assert archive.stat().st_size==facts['bytes'] and sha(archive)==facts['sha256']
    observed={};data={}
    with tarfile.open(archive,'r|gz') as tar:
        for member in tar:
            path=PurePosixPath(member.name)
            assert not path.is_absolute() and '..' not in path.parts and path.parts[0]==folder
            assert member.isdir() or member.isfile()
            if member.isdir():continue
            name=path.relative_to(folder).as_posix();assert name not in observed
            stream=tar.extractfile(member)
            if name.endswith(('.json','.trx')) or capture_sources and name.endswith('.source'):
                content=stream.read();data[name]=content;digest=hashlib.sha256(content).hexdigest()
            else:digest=hashlib.file_digest(stream,'sha256').hexdigest()
            observed[name]={'bytes':member.size,'sha256':digest}
    rows=json.loads(data['files.json'])['files']
    assert len(rows)==facts['rows'] and len(observed)==len(rows)+1
    for row in rows:assert observed[row['path']]=={key:row[key] for key in ('bytes','sha256')},row['path']
    return facts,observed,data

def verify(platform):
    facts,index,data=transport(ROOT/'.tmp'/(platform+'-native-cleanup-v7-evidence'),'archive-native-cleanup-v7')
    def js(path):return json.loads(data[path].decode('utf-8-sig'))
    def cases(path):return ET.fromstring(data[path]).findall('.//t:UnitTestResult',NS)
    tests={}
    for name in data:
        if name.startswith('results/') and name.endswith('.trx'):
            counts=dict(Counter(row.get('outcome') for row in cases(name)))
            assert counts.get('Passed',0) and not counts.get('Failed',0),(name,counts)
            tests[PurePosixPath(name).name]=counts
    assert tests['cleanup-preflight.trx']=={'Passed':26} and tests['cleanup-ui.trx']=={'Passed':27}
    wanted={'mac':{'Passed':8151,'NotExecuted':207},'ubuntu':{'Passed':8173,'NotExecuted':185},
        'windows':{'Passed':7902,'NotExecuted':461}}[platform]
    assert tests['archive-native-cleanup-core.trx']==wanted
    inventory=js('full-case-inventory.json')
    assert inventory['prior_cases_present'] and inventory['prior_passes_preserved']
    if platform=='windows':
        for name,count in [('cleanup-identity',13),('cleanup-native-preflight',10),('cleanup-native-crashes',18),('native',207),('app-entry',14)]:
            assert tests[name+'.trx']=={'Passed':count},(name,tests.get(name+'.trx'))
        assert not js('account.json')['AdministratorsMember'] and js('account.json')['HiveLoaded']
        assert js('standard-launch.json')['ExitCode']==0
        assert inventory['previous']==8337 and inventory['current']==8363
    else:
        assert inventory['previous']==inventory['current']==8358
        primary,old_index,old_data=transport(ROOT/'.tmp'/(platform+'-native-cleanup-v4-evidence'),'archive-native-cleanup-v4')
        reuse=js('reused-core-evidence.json')
        assert reuse['full_Core_reused_not_rerun'] and reuse['previous_manifest_sha256']==old_index['files.json']['sha256']
        assert index['results/archive-native-cleanup-core.trx']==old_index['results/archive-native-cleanup-core.trx']
        assert reuse['unchanged_binaries']
        actual={row['path'].replace('\\','/'):row['sha256'] for row in js('actual-binaries.json')['files']}
        for row in reuse['unchanged_binaries']:
            matches=[digest for name,digest in actual.items() if name.endswith('/'+row['path'].replace('\\','/'))]
            assert matches and all(digest==row['sha256'] for digest in matches),row['path']
        for row in reuse['files']:
            assert old_index[row['path']]=={key:row[key] for key in ('bytes','sha256')}
            # Reader reports/logs are regenerated and may contain the new batch's absolute archive path.
            # The archives and expected metadata documents must retain their exact bytes.
            if not PurePosixPath(row['path']).name.startswith('independent-') and not row['path'].endswith('.log'):
                assert index[row['path']]==old_index[row['path']],row['path']
    audit=js('compiler-sources/facts.json')
    assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for binary in audit['binaries']:
        for suffix,key in [('dll','dllSha256'),('pdb','pdbSha256')]:
            assert index['binaries/'+binary['assembly']+'.'+suffix]['sha256']==binary[key].lower()
    paths=subprocess.check_output(['git','diff','--name-only','f268b89a','-z'],cwd=ROOT).decode().split('\0')
    paths+=subprocess.check_output(['git','ls-files','--others','--exclude-standard','-z'],cwd=ROOT).decode().split('\0')
    changed=sorted({name for name in paths if name.endswith('.cs') and (platform=='windows' or not name.startswith('tests/FileCat.Platform.Windows.Tests/'))})
    for name in changed:
        rows=[row for row in audit['sources'] if row['document'].replace('\\','/').endswith('/'+name)]
        assert rows and all(row['sha256'].lower()==sha(ROOT/name) for row in rows),name
    for path,key,count in [('catalog/independent-catalog.json','cases',273),('workers/independent-pipeline.json','archives',74),
        ('native-vectors/independent-pipeline.json','archives',33 if platform=='windows' else 21),
        ('workflows/independent-pipeline.json','archives',6),('workflows/independent-workflow-rar.json','official_archives',6),
        ('workflows/independent-workflow-tar.json','archives',8),('jobs/independent-pipeline.json','archives',6 if platform=='windows' else 3),
        ('jobs/independent-workflow-rar.json','official_archives',6 if platform=='windows' else 4),
        ('jobs/independent-workflow-tar.json','archives',8 if platform=='windows' else 4)]:assert js(path)[key]==count,path
    if platform=='windows':
        for folder,counts in [('moves',(33,21)),('cleanup',(9,9))]:
            for leaf,expected in [('independent-pipeline.json',{'archives':6,'metadata':counts[0],'payloads':counts[1]}),
                ('independent-workflow-rar.json',{'official_archives':6,'independent_metadata_records':counts[0],'exact_payloads':counts[1]}),
                ('independent-workflow-tar.json',{'archives':8,'metadata':44 if folder=='moves' else 12,'payloads':28 if folder=='moves' else 12})]:
                result=js(folder+'/'+leaf);assert all(result[key]==value for key,value in expected.items()),(folder,leaf)
                for row in result['rows']:assert index[folder+'/'+row['archive']]['sha256']==row['sha256'].lower()
    assert not js('writing-closure.json')['Rar15_or_DiscUtils_present'] and not js('machine.json')['new_profile_registered']
    census=js('final-census.json');assert not census['busy'] and census['owned_filecat_or_test_processes']==0
    if platform=='mac':assert census['preserved_wake_helper'].startswith('36765 ')
    if platform=='ubuntu':assert js('private-keyring.json')['pid']==js('private-keyring-stopped.json')['pid']
    return {'platform':platform,**facts,'core':wanted,'tests':tests,'current_CSharp_sources_bound':len(changed),
        'DLL_PDB_pairs_bound':len(audit['binaries']),'core_reused_with_focused_fixture_followup':platform!='windows',
        'census':census,'production_backend_qualified':False}

if __name__=='__main__':
    for platform in sys.argv[1:] or ('mac','ubuntu','windows'):print(json.dumps(verify(platform)),flush=True)
