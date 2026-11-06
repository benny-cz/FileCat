"""Independently check native/guest cancellation outcomes and refuse bad lifetime descriptors before VM setup."""
import argparse
import json
import os
from pathlib import Path
import socket
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--root', type=Path, required=True)
args = parser.parse_args()
root = args.root.resolve(strict=True)
facts = json.loads((root / 'cancellation/facts.json').read_text())
assert facts['experimentPassed'] is True and facts['productionBackendQualified'] is False
cases = ['actual-assessment-session', 'blocked-input', 'blocked-output', 'uncooperative-spin']
assert [row['kind'] for row in facts['cases']] == cases and len({row['Id'] for row in facts['cases']}) == 4
outcomes = []
for row in facts['cases']:
    assert row['Reaped'] is True and row['ExitCode'] == 0 and 0 <= row['seconds'] < 2
    name = 'core-assessment' if row['kind'] == 'actual-assessment-session' else row['kind']
    log = root / 'cancellation' / (name + '.log')
    assert log.stat().st_size <= 1048576
    text = log.read_text()
    host_rows = [json.loads(line.removeprefix('FILECAT_HOST_FACTS ')) for line in text.splitlines() if line.startswith('FILECAT_HOST_FACTS ')]
    assert len(host_rows) == 1
    host = host_rows[0]
    assert host['pid'] == row['Id'] and host['uid'] == host['euid'] == os.getuid() and host['state'] == 0 and host['serialPorts'] == 3
    assert host['valid'] is True and host['started'] is True and host['parentExited'] is False and host['parentMonitorFailed'] is False
    assert host['cancellationRequested'] is True and host['cancellationSent'] is True and host['cancellationMalformed'] is False and host['stopReason'] == 'cancel'
    assert all(host[key] == '' for key in ('guestError', 'stopError', 'startError', 'validationError'))
    marker = 'FILECAT_GUEST_CANCELED' in text
    if host['forcedStop']:
        assert host['stopRequested'] is True and host['stopCompleted'] is True
    else:
        assert host['guestStopped'] is True and host['stopRequested'] is False and host['stopCompleted'] is False
        assert marker and 'FILECAT_WAIT_STATUS=0000000000000000' in text and 'FILECAT_GUEST_FINISHED' in text
    if row['kind'] in ('actual-assessment-session', 'blocked-input'): assert host['forcedStop'] is False and marker
    if row['kind'] == 'uncooperative-spin': assert host['forcedStop'] is True and not marker
    result = subprocess.run(['/bin/ps', '-p', str(row['Id']), '-o', 'pid='], stdout=subprocess.PIPE)
    assert result.returncode == 1 and not result.stdout
    outcomes.append(dict(kind=row['kind'], pid=row['Id'], seconds=row['seconds'], forced=host['forcedStop'], guest_canceled_marker=marker))

native = root / 'FileCat.ArchiveLinuxVmWorker'
invalid = []
reader, writer = os.pipe()
regular = os.open('/dev/null', os.O_RDONLY)
left, right = socket.socketpair()
try:
    for value, passed, expected in [('0', (), 64), ('2', (), 64), ('-3', (), 64), ('+3', (), 64), ('03', (), 64),
        ('2147483648', (), 64), ('999999', (), 65), (str(writer), (writer,), 65), (str(regular), (regular,), 65), (str(left.fileno()), (left.fileno(),), 65)]:
        result = subprocess.run([str(native), str(root / 'input/kernel.Image'), str(root / 'input/initrd-worker.cpio'), '1024', str(os.getpid()), value],
            pass_fds=passed, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=5)
        assert result.returncode == expected and not result.stdout and not result.stderr
        invalid.append(dict(argument=value, exit=expected))
finally:
    os.close(reader); os.close(writer); os.close(regular); left.close(); right.close()
result = dict(experiment_passed=True, production_backend_qualified=False, outcomes=outcomes, invalid_descriptor_cases=invalid)
with (root / 'cancellation/verified.json').open('x') as output:
    json.dump(result, output, indent=2); output.write('\n')
print(json.dumps(result))
