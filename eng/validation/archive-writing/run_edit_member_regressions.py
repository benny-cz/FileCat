"""Fixture-only Windows member continuation; preserve all already-passed product/native checks."""
from pathlib import Path
import importlib.util, json, shutil, sys, xml.etree.ElementTree as ET

FOLDER='awe-v3-member-v1'
PREVIOUS='awe-v3-native-v2'
PACKET='archive-edit-member-fixture-delta-v1.tar.gz'
def helper(task):
    path=task/'run_edit_native_regressions.py'
    spec=importlib.util.spec_from_file_location('edit_native_parent',path); value=importlib.util.module_from_spec(spec);spec.loader.exec_module(value)
    value.FOLDER=FOLDER;return value
def batch_for(task):return helper(task).batch_for(task)
def prepare(task,expected):
    assert sys.platform=='win32'; value=helper(task); batch=batch_for(task); batch.idle()
    previous=task/PREVIOUS; proof=json.loads((previous/'files.json').read_text())['files']
    for name in ('source-files.json','passed-gates.json'):
        row=next(row for row in proof if row['path']==name); assert value.sha(previous/name)==row['sha256']
    gates=json.loads((previous/'passed-gates.json').read_text())
    assert all(gates[name]['passed']==str(count) for name,count in [('native',217),('cleanup-crashes',18),('app-entry',14)])
    spec=importlib.util.spec_from_file_location('edit_member_delta',batch.root/'eng/validation/archive-writing/run_job_backend_batch.py')
    base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
    base.BATCH_FOLDER=FOLDER;base.PREVIOUS_FOLDER=PREVIOUS;base.PACKET_NAME=PACKET;base.prepare(task,expected)
    shutil.copyfile(Path(__file__),batch.out/'executed-member-continuation.py')
    (batch.out/'inherited-gates.json').write_text(json.dumps({'folder':PREVIOUS,'manifest_sha256':value.sha(previous/'files.json'),'gates':gates},indent=2))
def build(task):
    batch=batch_for(task);value=helper(task)
    delta=json.loads((batch.out/'delta.json').read_text())['files']
    assert all(row['path'].startswith(('tests/','eng/validation/')) for row in delta)
    batch.execute('FileCat.App.Tests-build',[batch.dotnet,'build',str(batch.root/'tests/FileCat.App.Tests/FileCat.App.Tests.csproj'),
        '-c','Release','--disable-build-servers','-p:NuGetAudit=false','-m:1','-v','minimal'])
    parent=task/'awe-v3';proof=json.loads((parent/'files.json').read_text())['files'];checked=[]
    for row in proof:
        if not row['path'].startswith(('app/','probe/')):continue
        path=parent/row['path'];assert path.stat().st_size==row['bytes'] and value.sha(path)==row['sha256'];checked.append(row)
    for name,project in [('app','src/FileCat.App'),('probe','tests/FileCat.WorkerProbe')]:
        for path in (batch.root/project/'bin/Release/net10.0').iterdir():
            if path.suffix not in ('.dll','.pdb') or not(path.name.startswith('FileCat.') or path.name in ('FileCat.dll','FileCat.pdb')):continue
            assert value.sha(path)==value.sha(parent/name/path.name),str(path)
    (batch.out/'reused-parent.json').write_text(json.dumps({'folder':'awe-v3','manifest_sha256':value.sha(parent/'files.json'),'checked':checked},indent=2))
def tests(task):
    batch=batch_for(task);batch.idle()
    batch.execute('member-native-ui',batch.test_args('FileCat.App.Tests','FullyQualifiedName~ArchiveMemberNativeUiTests','member-native-ui'))
    ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    counters=ET.parse(batch.out/'results/member-native-ui.trx').find('.//t:Counters',ns).attrib
    assert counters['passed']=='10' and counters['total']=='10' and counters['failed']=='0',counters
    batch.idle();(batch.out/'tests-complete.json').write_text(json.dumps({'member-native-ui':counters},indent=2))
if __name__=='__main__':
    phase,task,*rest=sys.argv[1:];task=Path(task).resolve()
    if phase=='prepare':prepare(task,rest[0])
    elif phase=='build':build(task)
    elif phase=='tests':tests(task)
    elif phase=='seal':helper(task).seal(task)
    else:raise SystemExit('Unknown phase')
