"""Check every role lifetime/kernel result and bounded independent extraction of all four owned outputs."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import resource
import subprocess
import sys
import tempfile

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from zstd_oracle_common import NativeZstd

parser = argparse.ArgumentParser()
parser.add_argument('--root', type=Path, required=True)
args = parser.parse_args()
root = args.root.resolve(strict=True)
facts = json.loads((root / 'results/facts.json').read_text())
assert facts['experimentPassed'] is True and facts['productionBackendQualified'] is False
formats = ['Tar', 'TarGzip', 'TarBzip2', 'TarZstd']
assert [row['format'] for row in facts['outcomes']] == formats
lifetimes = facts['lifetimes']
assert len(lifetimes) == 16 and len({row['pid'] for row in lifetimes}) == 16
assert all(row['reaped'] is True and row['exit'] == 0 for row in lifetimes)
logs = sorted((root / 'results').glob('*.log'))
assert len(logs) == 16
seen, peaks = set(), []
for log in logs:
    assert log.stat().st_size < 1048576
    text = log.read_text()
    assert text.count('FILECAT_HOST_FACTS ') == 1 and text.count('"guestWorker":true') == 1
    host = json.loads(next(line.removeprefix('FILECAT_HOST_FACTS ') for line in text.splitlines() if line.startswith('FILECAT_HOST_FACTS ')))
    assert host['uid'] == host['euid'] and host['uid'] > 0 and host['cpus'] == 1 and host['memoryBytes'] == 1536 << 20
    assert all(host[key] is True for key in ('valid', 'started', 'guestStopped'))
    assert host['forcedStop'] is False and host['state'] == 0
    assert all(host[key] == '' for key in ('guestError', 'startError', 'validationError'))
    assert all(host[key] == 0 for key in ('balloonDevices', 'networkDevices', 'storageDevices'))
    assert host['pid'] not in seen
    seen.add(host['pid'])
    guest = json.loads(next(line for line in text.splitlines() if line.startswith('{"guestWorker":')))
    assert guest == dict(guestWorker=True, uid=1000, runtime='10.0.12', memoryLimit=1073741824, limitRaiseDenied=True)
    assert text.count('FILECAT_GUEST_READY module_status=0000000000000000') == 1
    assert text.count('FILECAT_LIMIT=1073741824') == 1 and 'FILECAT_ERROR=' not in text
    assert text.count('FILECAT_LIMIT_RAISE_DENIED uid=1000') == 1
    assert text.count('FILECAT_WAIT_STATUS=0000000000000000') == 1 and text.count('FILECAT_GUEST_FINISHED') == 1
    events = re.findall(r'^FILECAT_MEMORY_EVENTS\n((?:[a-z_]+ \d+\n){6})', text, re.M)
    assert len(events) == 1
    assert dict(line.split() for line in events[0].splitlines()) == dict.fromkeys(['low', 'high', 'max', 'oom', 'oom_kill', 'oom_group_kill'], '0')
    peak = re.findall(r'^FILECAT_MEMORY_PEAK=(\d+)$', text, re.M)
    assert len(peak) == 1 and 0 < int(peak[0]) <= 1 << 30
    peaks.append(int(peak[0]))
    trace = log.with_name(log.name + '.protocol.bin').read_bytes()
    assert len(trace) == 512 and trace.startswith(b'FCVMRDY1FCAW')
assert seen == {row['pid'] for row in lifetimes}

def tar(arguments, data):
    def limits():
        resource.setrlimit(resource.RLIMIT_CPU, (10, 10))
        resource.setrlimit(resource.RLIMIT_FSIZE, (2 << 20, 2 << 20))
    with tempfile.TemporaryFile(dir=root) as output, tempfile.TemporaryFile(dir=root) as errors:
        result = subprocess.run(['/usr/bin/tar'] + arguments, input=data, stdout=output, stderr=errors, timeout=15, preexec_fn=limits,
                                env=dict(os.environ, LC_ALL='en_US.UTF-8'))
        assert result.returncode == 0
        output.seek(0); errors.seek(0)
        content, error = output.read((2 << 20) + 1), errors.read((2 << 20) + 1)
        assert len(content) <= 2 << 20 and not error, error
        return content

oracle = NativeZstd()
checked = []
for row in facts['outcomes']:
    path = root / 'results' / (row['format'] + '.archive')
    expected = root / 'results' / (row['format'] + '.expected')
    assert path.stat().st_size == row['stageBytes'] < 1 << 20 and expected.stat().st_size == 196608
    data = path.read_bytes()
    assert hashlib.sha256(data).hexdigest().upper() == row['sha256']
    if row['format'] == 'TarZstd': data = oracle.decompress(data)
    assert tar(['-tf', '-'], data).decode('utf-8').splitlines() == ['documents/界.txt']
    extracted = tar(['-xOf', '-', 'documents/界.txt'], data)
    assert extracted == expected.read_bytes()
    checked.append(dict(format=row['format'], payloadBytes=len(extracted), payloadSha256=hashlib.sha256(extracted).hexdigest()))
result = dict(experiment_passed=True, production_backend_qualified=False, roles_checked=16, peak_min=min(peaks), peak_max=max(peaks),
    independent_tar_version=subprocess.check_output(['/usr/bin/tar', '--version'], text=True).strip(),
    independent_tar_sha256=hashlib.sha256(Path('/usr/bin/tar').read_bytes()).hexdigest(), zstd=oracle.facts(), checked=checked)
with (root / 'verified.json').open('x') as output:
    json.dump(result, output, indent=2); output.write('\n')
print(json.dumps(result, sort_keys=True))
