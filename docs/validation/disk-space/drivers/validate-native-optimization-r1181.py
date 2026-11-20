import json
import pathlib
import sys
import xml.etree.ElementTree as ET

mode, path = sys.argv[1], pathlib.Path(sys.argv[2])
assembly = ET.parse(path / 'tests.xml' if mode in ('cancel', 'scale') else path).getroot().find('assembly')
assert assembly is not None
counts = tuple(int(assembly.get(k, '-1')) for k in ('total', 'passed', 'failed', 'errors', 'skipped'))
if mode == 'old-control':
    assert counts == (5, 3, 2, 0, 0), counts
    failed = [t for t in assembly.findall('.//test') if t.get('result') == 'Fail']
    expected = {'NativeDeepSiblingEnumerationHasBoundedDescriptorChurn',
                'PermutedNativeIdentitiesProduceOrderedCanonicalObjectsWithExactAliasesAndBoundedReads'}
    assert {t.get('name').split('.')[-1] for t in failed} == expected
    # This small fixture fits the 32 MiB cache. Its negative control establishes
    # canonical object ordering; actual cache-pressure timing comes from native 1M.
    assert any('Assert.Equal()' in ''.join(t.itertext()) for t in failed)
    assert any('Assert.InRange()' in ''.join(t.itertext()) for t in failed)
elif mode == 'smoke':
    assert counts == (6, 6, 0, 0, 0), counts
elif mode == 'native-control':
    assert counts == ((4, 0, 0, 0, 4) if sys.platform == 'win32' else (4, 4, 0, 0, 0)), counts
elif mode in ('cancel', 'scale'):
    r = json.loads((path / 'receipt.json').read_text(encoding='utf-8-sig'))
    assert r['cleanup'] and r['phase'] == 'closed' and r['remainingSharedQuotaBytes'] == 0
    if mode == 'cancel':
        assert counts == (1, 0, 1, 0, 0), counts
        assert not r['completed'] and 'OperationCanceledException' in r['failure']
    else:
        assert counts == (1, 1, 0, 0, 0), counts
        assert r['completed'] and r['failure'] is None and r['sourceCloseFailures'] == 0
        assert r['paths'] == r['count'] + (33 if r['shape'] == 'deep' else 1)
        assert r['progressReports'] > 0 and r['firstObservationMilliseconds'] is not None
        if sys.platform == 'darwin':
            physical = r['nativePhysicalMemory']
            assert physical['available'] and physical['complete'] and physical['withinPhysicalReferenceCeiling']
            assert not physical['committedPrivateByteBudgetQualified']
        else:
            assert r['privateMemoryBudgetVerified']
else:
    assert mode == 'broad' and counts[0] > 0 and counts[2:4] == (0, 0), counts
print(json.dumps(dict(mode=mode, counts=counts)))
