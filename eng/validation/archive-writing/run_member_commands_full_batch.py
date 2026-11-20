"""Connected member/transfer gate followed by complete regressions and independent readers; guests only."""
from pathlib import Path
import importlib.util,json,os,shutil,subprocess,sys

FOLDER=os.environ.get('FILECAT_MEMBER_BATCH','awm-v4')
PACKET=os.environ.get('FILECAT_MEMBER_PACKET','archive-member-commands-delta-v4.tar.gz')

def read_module(name,path):
    spec=importlib.util.spec_from_file_location(name,path);value=importlib.util.module_from_spec(spec);spec.loader.exec_module(value);return value

def member(task):
    value=read_module('member_gate',task/'archive-workflow-source-v1/eng/validation/archive-writing/run_member_commands_batch.py')
    value.FOLDER=FOLDER;return value

def native(task):
    value=member(task).module(task);value.BATCH_FOLDER=FOLDER;value.PACKET_NAME=PACKET
    value.PREVIOUS_FOLDER=os.environ.get('FILECAT_MEMBER_PREVIOUS','awm-v3g' if sys.platform=='win32' else 'awm-v3')
    value.tests=tests;return value

def batch_for(task):return native(task).batch_for(task)

def regressions(task):
    value=read_module('member_regressions',task/'archive-workflow-source-v1/eng/validation/archive-writing/run_target_update_ui_batch.py')
    value.BATCH_FOLDER=FOLDER;value.TARGET_PREFLIGHT_CASES=41;value.CORE_CASE_DELTA=82
    value.native=native;return value

def prepare(task,expected):
    if shutil.disk_usage(task).free < 2 << 30:raise RuntimeError('Guest validation needs 2 GiB free.')
    native(task).prepare(task,expected)
    out=batch_for(task).out
    shutil.copyfile(task/'archive-creation-ui-v5/results/archive-creation-core.trx',out/'previous-full-core.trx')
    shutil.copyfile(Path(__file__),out/'executed-member-full-batch.py')

def build(task):regressions(task).build(task)

def tests(batch):
    gate=member(batch.task);gate.tests(batch.task)
    facts=json.loads((batch.out/'focused-complete.json').read_text())
    assert facts['member-core']['passed']=='116' and facts['member-ui']['passed']=='53',facts
    if sys.platform=='win32':assert facts['member-native-ui']['passed']=='10',facts
    regressions(batch.task).tests(batch)

def run_tests(task):
    value=regressions(task)
    value.tests=tests
    # Reentrant private-bus runs return to this driver, retaining the connected gate and native factory.
    value.__file__=str(Path(__file__).resolve())
    value.run_tests(task)

def oracles(task):
    regressions(task).oracles(task)
    if sys.platform!='win32':return
    batch=batch_for(task);helpers=batch.root/'eng/validation/archive-writing/sevenzip-feasibility'
    tool=task/'password-oracle-tools-v1/7zip/7z.exe';modules=task/'password-oracle-tools-v1/modules';folder=batch.out/'member-ui-vectors'
    batch.execute('member-ui-sevenzip',[sys.executable,'-B',str(helpers/'verify_pipeline.py'),str(folder),str(tool),str(modules),str(tool)])
    batch.execute('member-ui-rar',[sys.executable,'-B',str(batch.root/'eng/validation/archive-writing/rar5-independent-probe/verify_native_publication.py'),str(folder),str(tool),str(modules),'0','0','--member-ui'])
    batch.execute('member-ui-tar',[sys.executable,'-B',str(batch.root/'eng/validation/archive-writing/verify-workflow-tar.py'),str(folder),str(tool),str(modules),str(helpers/'verify_pipeline.py'),'--update-ui'])
    facts=json.loads((folder/'independent-pipeline.json').read_text());assert (facts['archives'],facts['metadata'],facts['payloads'])==(3,6,6),facts

def seal(task):
    gate=member(task);gate.seal(task)

if __name__=='__main__':
    phase,task,*rest=sys.argv[1:];task=Path(task).resolve()
    if phase=='prepare':prepare(task,rest[0])
    elif phase=='build':build(task)
    elif phase=='tests':run_tests(task)
    elif phase=='inside-private':native(task).private_tests(task)
    elif phase=='oracle':oracles(task)
    elif phase=='seal':seal(task)
    elif phase=='run':
        prepare(task,rest[0]);build(task)
        try:run_tests(task);oracles(task)
        finally:seal(task)
    else:raise SystemExit('Unknown phase')
