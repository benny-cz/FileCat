"""Validate a completed owned harness log, never run the product on the host."""
import json
import pathlib
import platform
import sys

phase, filename = sys.argv[1:]
path = pathlib.Path(filename)
assert path.stat().st_size <= 1024 * 1024
last = path.read_text(encoding='utf-8-sig').splitlines()[-1]
row = json.loads(last)
if phase == 'negative':
    assert row['passed'] is False and row['allocationAttempted'] is False and row['nativeReads'] == 0
    sys.exit(0)
assert row['passed'] is True
if phase == 'counter':
    assert row['allocationAttempted'] and row['touchedBytes'] == 67108864
    if platform.system() == 'Darwin':
        assert row['nativeReads'] >= 4 and row['nativeLayoutControlSha256']
    sys.exit(0)
count = 1000 if phase.startswith('small-') else 10000000 if phase.startswith('huge-') else 1000000
shape = phase.removeprefix('small-').removeprefix('huge-')
assert row['records'] == count and row['shape'] == shape
assert row['remainingQuotaBytes'] == row['remainingProfileQuotaBytes'] == 0
assert row['ownedArtifactsRemoved'] and row['foreignNeighborPreserved'] and row['observedSourceWasNeverCreated']
if row.get('limitRefusal'):
    assert count == 10000000 and row['fullCsvExportQualified'] is False
    assert row['destinationWasNeverOpened'] and row['retainedSourceUnchanged']
else:
    assert len(row['reports']) == 2 and {r['redact'] for r in row['reports']} == {True, False}
    assert all(r['verifiedEveryDeclaredRow'] and r['lines'] == count + 2 for r in row['reports'])
if platform.system() == 'Darwin':
    memory = row['nativePhysicalMemory']
    assert memory['available'] and memory['complete'] and memory['withinPhysicalReferenceCeiling']
    # This is an additional measured physical footprint, not committed private bytes.
    assert memory['committedPrivateByteBudgetQualified'] is False
else:
    assert row['privateMemoryBudgetVerified']
