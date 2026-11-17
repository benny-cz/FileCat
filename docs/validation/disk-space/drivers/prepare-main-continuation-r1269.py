import hashlib
import json
import pathlib
import shutil
import subprocess

r = pathlib.Path.cwd(); t = r / '.tmp'; d = r / 'docs/validation/disk-space/drivers'; revision = 'r1269'
prior = json.loads((t / 'main-integration-proof-r1255.json').read_text())


def sha(p):
    with p.open('rb') as f: return hashlib.file_digest(f, 'sha256').hexdigest()


platform = 'linux'; old = next(p for p in prior['platforms'] if p['platform'] == platform)
assert all(sha(t / 'batch-r1255-linux' / row['path']) == row['sha256'] for row in old['inputs'])
stage = t / 'main-continuation-r1269-linux'; assert not stage.exists(); stage.mkdir()
plan = dict(baseline='/home/benny/FileCatValidation/dsa-batch-20261107-r1255/payload', inputs=old['inputs'],
    methods=['*NativeHistoryViewProtectsItsRollingCopyAndPinnedCopySurvivesLaterRetention',
             '*GroupOtherNativeWindowRetainsExactMembersThroughChartFocusEveryOrderHistoryAndCatalogRestore',
             '*GroupOtherNativeOverviewKeepsCategoriesBoundedAndFocusedCaptureInsideItsSavedScope'])
(stage / 'plan.json').write_text(json.dumps(plan, indent=2)); shutil.copy2(d / 'cached-main-continuation-r1268.py', stage / 'run.py')
archive = t / 'dsa-batch-linux-r1269.tgz'; assert not archive.exists(); subprocess.run(['tar.exe', '-czf', str(archive), '-C', str(stage), '.'], check=True)
template = '''set -eu
cd "$(dirname "$0")"
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -x dotnet >/dev/null 2>&1;then printf 'BUSY phase=verify\n' > state.txt;exit 75;fi
actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1);[ "$actual" = '@HASH@' ];umask 077;mkdir driver;tar -xzf bundle.tgz -C driver
python3 driver/run.py
'''
runner = t / 'batch-linux-r1269.sh'; assert not runner.exists(); runner.write_text(template.replace('@HASH@', sha(archive)), encoding='utf-8', newline='\n')
proof = dict(revision=revision, platform=platform, productRebuilt=False, priorRevision='r1255', priorProofSha256=sha(t / 'main-integration-proof-r1255.json'),
    verifiedInputCount=len(old['inputs']), changedProductFiles=0, shallowFixtureWorkingDirectory=True, flatRenderingAssembly=True, archiveSha256=sha(archive), archiveBytes=archive.stat().st_size, runnerSha256=sha(runner))
out = t / 'main-continuation-proof-r1269.json'; assert not out.exists(); out.write_text(json.dumps(proof, indent=2)); print(json.dumps(proof))
