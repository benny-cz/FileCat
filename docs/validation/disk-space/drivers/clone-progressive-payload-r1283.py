"""Guest-only cache clone. The shell runner owns test execution after verification."""
import hashlib
import json
import pathlib
import shutil
import stat
import subprocess
import sys

driver = pathlib.Path(__file__).resolve().parent; root = driver.parent
plan = json.loads((driver / 'plan.json').read_text()); baseline = pathlib.Path(plan['baseline']).resolve(); payload = root / 'payload'
def sha(p):
    with p.open('rb') as f: return hashlib.file_digest(f, 'sha256').hexdigest()

for pattern in ('FileCat([.].*)?', 'dotnet'):
    result = subprocess.run(['pgrep', '-x', pattern], capture_output=True)
    if result.returncode == 0:
        (root / 'state.txt').write_text('BUSY phase=verify\n'); sys.exit(75)
    assert result.returncode == 1
assert baseline.is_relative_to(pathlib.Path.home() / 'FileCatValidation') and not payload.exists()
disk = shutil.disk_usage(root); reserve = min(2 * 1024**3, max(256 * 1024**2, (disk.total + 99) // 100))
assert disk.free >= reserve + plan['clonedBytes'] + 256 * 1024**2, 'Insufficient clone space'
for row in plan['inputs']:
    name = pathlib.PurePosixPath(row['path']); assert not name.is_absolute() and '..' not in name.parts and ':' not in str(name)
    source = baseline / name; assert source.resolve().is_relative_to(baseline) and stat.S_ISREG(source.lstat().st_mode)
    assert sha(source) == row['sha256'], row['path']
for row in plan['patches']:
    assert sha(driver / 'patch' / row['path']) == row['sha256']
for row in plan['inputs']:
    target = payload / row['path']; target.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(baseline / row['path'], target)
    assert sha(target) == row['sha256']
for row in plan['patches']:
    target = payload / row['path']; shutil.copy2(driver / 'patch' / row['path'], target); assert sha(target) == row['sha256']
(root / 'cache-proof.json').write_text(json.dumps(dict(baseline=str(baseline), verifiedFiles=len(plan['inputs']), patches=plan['patches'])))
