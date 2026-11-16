"""Verify the two owned experiments, without claiming production backend qualification."""
import argparse
import json
from pathlib import Path
import re

def verify(root):
    results = []
    for case, limit, ram, status in (('control', 64 << 20, 512 << 20, 0), ('budget1g', 1 << 30, 1536 << 20, 9)):
        with (root / (case + '.log')).open('rb') as stream:
            data = stream.read((1 << 20) + 1)
        if len(data) > 1 << 20:
            raise ValueError('The owned diagnostic log exceeds its bound.')
        text = data.decode('utf-8', 'strict').replace('\r\n', '\n')
        hosts = re.findall(r'^FILECAT_HOST_FACTS (\{[^\n]+\})$', text, re.M)
        if len(hosts) != 1:
            raise ValueError('Exactly one final host observation is required.')
        host = json.loads(hosts[0])
        if not (host['uid'] == host['euid'] > 0 and host['valid'] and host['started'] and host['guestStopped'] and
                not host['forcedStop'] and host['state'] == 0 and host['memoryBytes'] == ram and host['cpus'] == 1 and
                host['networkDevices'] == host['storageDevices'] == host['balloonDevices'] == 0 and
                host['startError'] == host['validationError'] == host['guestError'] == ''):
            raise ValueError('The ordinary-user guest configuration/lifetime observation is incomplete.')
        required = (f'FILECAT_LIMIT={limit}\n', 'FILECAT_LIMIT_RAISE_DENIED uid=1000\n',
                    f'FILECAT_WAIT_STATUS={status:016x}\n', 'FILECAT_GUEST_FINISHED\n')
        if any(text.count(marker) != 1 for marker in required) or 'FILECAT_ERROR=' in text or 'FILECAT_LIMIT_NOT_ENFORCED' in text:
            raise ValueError('The guest did not report the exact expected enforced/control outcome.')
        blocks = re.findall(r'^FILECAT_MEMORY_EVENTS\n(.*?)FILECAT_MEMORY_PEAK=([0-9]+)\n', text, re.M | re.S)
        if len(blocks) != 1:
            raise ValueError('Exactly one kernel memory-event observation is required.')
        pairs = re.findall(r'^(low|high|max|oom|oom_kill|oom_group_kill) ([0-9]+)$', blocks[0][0], re.M)
        events = dict((name, int(value)) for name, value in pairs)
        peak = int(blocks[0][1])
        if len(pairs) != 6 or len(events) != 6 or not 0 < peak <= limit:
            raise ValueError('The kernel memory counters are incomplete or exceed the requested limit.')
        if case == 'control':
            if text.count('FILECAT_CONTROL_COMPLETED\n') != 1 or events['oom'] or events['oom_kill'] or peak < 32 << 20:
                raise ValueError('The healthy control did not complete below the kernel limit.')
        elif events['oom_kill'] != 1 or events['oom'] < 1 or peak != limit:
            raise ValueError('The large experiment lacks exact kernel OOM/peak evidence.')
        results.append({'case': case, 'host': host, 'limit_bytes': limit, 'peak_bytes': peak, 'wait_status': status, 'events': events})
    return {'experiment_passed': True, 'production_backend_qualified': False, 'runs': results}

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(verify(args.root)))
