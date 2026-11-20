import json
import pathlib
import sys
import xml.etree.ElementTree as ET

case = pathlib.Path(sys.argv[1])
assembly = ET.parse(case / 'tests.xml').getroot().find('assembly')
assert assembly is not None
assert tuple(int(assembly.get(k, '-1')) for k in ('total', 'passed', 'failed', 'errors', 'skipped')) == (1, 1, 0, 0, 0)
receipt = json.loads((case / 'receipt.json').read_text(encoding='utf-8-sig'))
assert receipt['completed'] and receipt['cleanup'] and receipt['phase'] == 'closed' and receipt['failure'] is None
assert receipt['sourceCloseFailures'] == 0 and receipt['remainingSharedQuotaBytes'] == 0
assert receipt['paths'] == receipt['count'] + (33 if receipt['shape'] == 'deep' else 1)
assert receipt['progressReports'] > 0 and receipt['firstObservationMilliseconds'] is not None
if sys.platform == 'darwin':
    physical = receipt['nativePhysicalMemory']
    assert physical['available'] and physical['complete'] and physical['withinPhysicalReferenceCeiling']
    assert not physical['committedPrivateByteBudgetQualified']
else:
    assert receipt['privateMemoryBudgetVerified']
print(json.dumps(dict(count=receipt['count'], shape=receipt['shape'], paths=receipt['paths'], completed=True, cleanup=True)))
