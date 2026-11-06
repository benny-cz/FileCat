"""Recheck exact resource observations and refuse every managed-guest RAM setting except the fixed 1 GiB profile."""
import argparse
import json
import os
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--root', type=Path, required=True)
args = parser.parse_args()
root = args.root.resolve(strict=True)
facts = json.loads((root / 'memory-envelope/facts.json').read_text())
assert facts['experiment_passed'] is True and facts['production_backend_qualified'] is False
expected = ['control-256m', 'overrun-1280m', 'entropy-draining-10s', 'entropy-blocked-10s']
assert [row['case'] for row in facts['cases']] == expected
for row in facts['cases']:
    detail = json.loads((root / 'memory-envelope' / (row['case'] + '.json')).read_text())
    assert row == {key:value for key,value in detail.items() if key != 'samples'}
    assert row['guest_ram_bytes'] == 1 << 30 and row['helper_reaped'] is True and row['provider_absent'] is True
    assert row['native']['memoryBytes'] == 1 << 30 and row['native']['state'] == 0
    assert row['native']['networkDevices'] == row['native']['storageDevices'] == row['native']['balloonDevices'] == 0
    assert len(detail['samples']) >= 6
    for kind in ['helper', 'provider', 'observer']:
        samples = [sample for sample in detail['samples'] if sample['kind'] == kind]
        assert len({sample['start_abstime'] for sample in samples}) == 1
        for key in ['resident', 'footprint', 'lifetime_max_footprint']:
            assert row['maxima'][kind][key] == max(sample[key] for sample in samples) > 0
    for pid in [row['helper'], row['provider']]:
        found = subprocess.run(['/bin/ps', '-p', str(pid), '-o', 'pid='], stdout=subprocess.PIPE)
        assert found.returncode == 1 and not found.stdout
assert facts['cases'][0]['allocation_complete'] is True and facts['cases'][0]['touched_bytes'] == 256 << 20
assert facts['cases'][1]['allocation_complete'] is False and 512 << 20 <= facts['cases'][1]['touched_bytes'] < 1 << 30
assert facts['cases'][2]['received_bytes'] >= 1 << 20 and facts['cases'][3]['received_bytes'] == 0
assert min(sample['seconds'] for sample in json.loads((root / 'memory-envelope/entropy-blocked-10s.json').read_text())['samples']) < 0.1
assert max(sample['seconds'] for sample in json.loads((root / 'memory-envelope/entropy-blocked-10s.json').read_text())['samples']) >= 10

bad = []
reader, writer = os.pipe()
try:
    for value in ['512', '1536', '0', '-1', '+1024', '01024', '1024.0', '2147483647']:
        result = subprocess.run([str(root / 'FileCat.ArchiveLinuxVmWorker'), str(root / 'input/kernel.Image'), str(root / 'input/initrd-worker.cpio'),
            value, str(os.getpid()), str(reader)], pass_fds=(reader,), stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=5)
        assert result.returncode == 64 and not result.stdout and not result.stderr
        bad.append(value)
finally:
    os.close(reader); os.close(writer)
result = dict(experiment_passed=True, production_backend_qualified=False, cases_checked=4, refused_ram_arguments=bad,
    whole_host_memory_boundary_qualified=False, maximum_provider_resident=max(row['maxima']['provider']['resident'] for row in facts['cases']),
    maximum_provider_footprint=max(row['maxima']['provider']['lifetime_max_footprint'] for row in facts['cases']))
with (root / 'memory-envelope/verified.json').open('x') as output:
    json.dump(result, output, indent=2); output.write('\n')
print(json.dumps(result))
