"""Read sealed guest artifacts; never execute FileCat, a test or an archive decoder on the host."""
from collections import Counter
from pathlib import Path, PurePosixPath
import argparse,hashlib,json,runpy,subprocess,tarfile,xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[3]
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
OLD=runpy.run_path(str(Path(__file__).with_name('verify_native_cleanup_checkpoint.py')))

def transport(platform,folder):
    packet=ROOT/'.tmp/member-evidence'/platform/(folder+'-evidence.tar.gz')
    facts=json.loads(packet.with_suffix('.json').read_text(encoding='utf-8-sig'))
    assert packet.stat().st_size==facts['bytes'] and OLD['sha'](packet)==facts['sha256']
    index={};data={}
    with tarfile.open(packet,'r|gz') as tar:
        for item in tar:
            path=PurePosixPath(item.name);assert not path.is_absolute() and '..' not in path.parts and path.parts[0]==folder
            assert item.isdir() or item.isfile()
            if item.isdir():continue
            key=path.relative_to(folder).as_posix();assert key not in index
            stream=tar.extractfile(item)
            if key.endswith(('.json','.trx','.source','.py')):
                content=stream.read();data[key]=content;digest=hashlib.sha256(content).hexdigest()
            else:digest=hashlib.file_digest(stream,'sha256').hexdigest()
            index[key]={'bytes':item.size,'sha256':digest}
    rows=json.loads(data['files.json'])['files'];assert len(index)==len(rows)+1
    for row in rows:assert index[row['path']]=={key:row[key] for key in ('bytes','sha256')},row['path']
    return {**facts,'rows':len(rows)},index,data

def cases(content):return ET.fromstring(content).findall('.//t:UnitTestResult',NS)

def verify(platform,folder,candidate,reader_candidate='HEAD'):
    candidate=subprocess.check_output(['git','rev-parse',candidate+'^{commit}'],cwd=ROOT).decode().strip()
    facts,index,data=transport(platform,folder);js=lambda key:json.loads(data[key].decode('utf-8-sig'))
    tests={Path(key).name:dict(Counter(row.get('outcome') for row in cases(value))) for key,value in data.items() if key.startswith('results/') and key.endswith('.trx')}
    for name,count in [('member-core',116),('member-ui',53),('target-preflight',41),('update-smoke',1),('update-ui',16),('password-preflight',21),('creation-smoke',2),('creation-ui',20),('mutation-preflight',8)]:
        assert tests[name+'.trx']=={'Passed':count},(name,tests.get(name+'.trx'))
    expected={'mac':{'Passed':8246,'NotExecuted':207},'ubuntu':{'Passed':8268,'NotExecuted':185},'windows':{'Passed':7997,'NotExecuted':461}}[platform]
    assert tests['archive-creation-core.trx']==expected,tests['archive-creation-core.trx']
    assert tests['application-regressions.trx']==({'Passed':114} if platform=='windows' else {'Passed':113,'NotExecuted':1})
    if platform=='windows':
        for name,count in [('member-native-ui',10),('creation-native-ui',10),('native',217),('creation-cleanup-crashes',18),('app-entry',14)]:
            assert tests[name+'.trx']=={'Passed':count},(name,tests.get(name+'.trx'))
        assert not js('account.json')['AdministratorsMember'] and js('account.json')['HiveLoaded']
        assert js('standard-launch.json')['ExitCode']==0
    else:assert tests['target-native-preflight.trx']=={'Passed':10}
    prior_folder='awt-v7' if platform=='windows' else 'awt-v6'
    _,_,prior=OLD['transport'](ROOT/'.tmp'/(platform+'-target-update-ui-'+prior_folder[4:]+'-evidence'),prior_folder)
    previous=cases(prior['results/archive-creation-core.trx']);current=cases(data['results/archive-creation-core.trx'])
    names=Counter(row.get('testName') for row in current);passed=Counter(row.get('testName') for row in current if row.get('outcome')=='Passed')
    assert len(current)==len(previous)+56
    assert all(names[name]==count for name,count in Counter(row.get('testName') for row in previous).items())
    assert all(passed[name]>=count for name,count in Counter(row.get('testName') for row in previous if row.get('outcome')=='Passed').items())
    audit=js('compiler-sources/facts.json');assert audit['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in audit['binaries'])
    for row in audit['binaries']:
        for extension,key in [('dll','dllSha256'),('pdb','pdbSha256')]:assert index['binaries/'+row['assembly']+'.'+extension]['sha256']==row[key].lower()
    changed=[name for name in subprocess.check_output(['git','diff','--name-only','-z','1e3ea8de',candidate],cwd=ROOT).decode().split('\0') if name.endswith('.cs')]
    source={row['path']:row for row in js('source-files.json')['files']};checked=[]
    for name in changed:
        if platform!='windows' and name.startswith('tests/FileCat.Platform.Windows.Tests/'):continue
        rows=[row for row in audit['sources'] if row['document'].replace('\\','/').endswith('/'+name)];assert rows,name
        blob=subprocess.check_output(['git','show',candidate+':'+name],cwd=ROOT).replace(b'\r\n',b'\n')
        for row in rows:
            key='compiler-sources/'+row['snapshot'];assert index[key]['sha256']==row['sha256'].lower()==source[name]['sha256'],name
            assert data[key].replace(b'\r\n',b'\n')==blob,name
        checked.append(name)
    for path,key,count in [('catalog/independent-catalog.json','cases',273),('workers/independent-pipeline.json','archives',74),
        ('native-vectors/independent-pipeline.json','archives',33 if platform=='windows' else 21),
        ('workflows/independent-pipeline.json','archives',6),('workflows/independent-workflow-rar.json','official_archives',6),('workflows/independent-workflow-tar.json','archives',8),
        ('jobs/independent-pipeline.json','archives',6 if platform=='windows' else 3),('jobs/independent-workflow-rar.json','official_archives',6 if platform=='windows' else 4),('jobs/independent-workflow-tar.json','archives',8 if platform=='windows' else 4)]:
        assert js(path)[key]==count,path
    reader_facts=None;reader_index=None;reader_data=None
    if platform=='windows':
        reader_facts,reader_index,reader_data=transport(platform,'awm-v4r')
        lineage=json.loads(reader_data['parent.json'])
        assert lineage['parent_manifest_sha256']==index['files.json']['sha256'] and not lineage['tests_rerun'] and lineage['archive_bytes_unchanged']
        assert json.loads(reader_data['status.json'])['readers_passed']
        for row in lineage['vectors']:
            if row['path'].endswith('.log'):continue
            assert reader_index[row['path']]==index[row['path']]=={key:row[key] for key in ('bytes','sha256')},row['path']
        for row in lineage['reader_sources']['files']:
            snapshot=reader_data['reader-helpers/'+row['path']]
            assert hashlib.sha256(snapshot).hexdigest()==row['sha256']
            blob=subprocess.check_output(['git','show',reader_candidate+':'+row['source']],cwd=ROOT)
            assert snapshot.replace(b'\r\n',b'\n')==blob.replace(b'\r\n',b'\n'),row['source']
        for folder,seven,tar in [('moves',(6,33,21),(8,44,28)),('cleanup',(6,9,9),(8,12,12)),('creation-ui',(3,3,3),(4,4,4)),('update-ui',(3,6,6),(4,8,8)),('member-ui-vectors',(3,6,6),(4,8,8))]:
            for leaf,keys,wanted in [('independent-pipeline.json',('archives','metadata','payloads'),seven),('independent-workflow-rar.json',('official_archives','independent_metadata_records','exact_payloads'),seven),('independent-workflow-tar.json',('archives','metadata','payloads'),tar)]:
                observed=reader_data if folder=='member-ui-vectors' else data;observed_index=reader_index if folder=='member-ui-vectors' else index
                result=json.loads(observed[folder+'/'+leaf]);assert tuple(result[key] for key in keys)==wanted,(folder,leaf)
                for row in result['rows']:assert observed_index[folder+'/'+row['archive']]['sha256']==row['sha256'].lower()
    census=js('final-census.json');assert census['owned_filecat_or_test_processes']==0 and not census['production_backend_qualified']
    if platform=='mac':assert census['preserved_wake_helper'].startswith('36765 ')
    if platform=='ubuntu':assert js('private-keyring.json')['pid']==js('private-keyring-stopped.json')['pid']
    mf,mi,md=transport(platform,folder+'-metadata-v1');meta=lambda key:json.loads(md[key].decode('utf-8-sig'))
    assert meta('parent.json')['parent_manifest_sha256']==index['files.json']['sha256']
    assert meta('parent.json')['writing_DLL_sha256']==index['binaries/FileCat.Archives.Writing.dll']['sha256']
    assert not meta('writing-closure.json')['Rar15_or_DiscUtils_present'] and not meta('machine.json')['new_profile_registered']
    return {'platform':platform,'candidate_source_commit':candidate,**facts,'core':expected,'tests':tests,'CSharp_sources_bound':len(checked),'DLL_PDB_pairs_bound':len(audit['binaries']),'census':census,'metadata':mf,'reader_followup':reader_facts,'production_backend_qualified':False}

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--platform',choices=('mac','ubuntu','windows'),required=True);parser.add_argument('--folder',default='awm-v4');parser.add_argument('--candidate',default='630d349154e765b3562236eb0c28ca3e959861a7');parser.add_argument('--reader-candidate',default='HEAD');args=parser.parse_args()
    print(json.dumps(verify(args.platform,args.folder,args.candidate,args.reader_candidate),indent=2))
