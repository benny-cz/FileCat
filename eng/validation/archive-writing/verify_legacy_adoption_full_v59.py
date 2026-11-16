#!/usr/bin/env python3
"""Independently reconcile the Windows adoption Core full TRX with the sealed AW baseline."""

from collections import Counter
import hashlib
import importlib.util
import json
from pathlib import Path
import tarfile
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[3]
BASE = ROOT / '.tmp/edit-evidence/windows/awe-v3-evidence.tar.gz'
BEFORE = ROOT / '.tmp/win-v59-previous-full-core.trx'
CURRENT = ROOT / '.tmp/win-v59-complete-core.trx'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
ENVIRONMENT_CASE = 'InspectorCrossCheckTests.PE_headers_directories_and_sections_are_what_the_runtime_reads(path:'


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    sidecar = json.loads(BASE.with_suffix('.json').read_text(encoding='utf-8-sig'))
    assert BASE.stat().st_size == sidecar['bytes'] and sha(BASE) == sidecar['sha256']
    with tarfile.open(BASE) as archive:
        index = json.load(archive.extractfile('awe-v3/files.json'))
        original = archive.extractfile('awe-v3/results/core-regressions.trx').read()
    row = next(row for row in index['files'] if row['path'] == 'results/core-regressions.trx')
    assert len(original) == row['bytes'] and hashlib.sha256(original).hexdigest() == row['sha256']
    assert sha(BEFORE) == row['sha256']

    spec = importlib.util.spec_from_file_location('inventory', Path(__file__).with_name('edit_core_inventory.py'))
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    migrations = helper.migrations()
    renamed = {row['previous']: row['current'] for row in migrations}

    old = ET.parse(BEFORE)
    new = ET.parse(CURRENT)
    prior = old.findall('.//t:UnitTestResult', NS)
    present = new.findall('.//t:UnitTestResult', NS)

    def normalized(name):
        # Windows paths are case-insensitive; the same fixture path was rendered once
        # with Admin and once with admin between the sealed baseline and current run.
        return name.casefold() if ENVIRONMENT_CASE in name else name

    before = Counter(normalized(renamed.get(row.attrib['testName'], row.attrib['testName'])) for row in prior)
    after = Counter(normalized(row.attrib['testName']) for row in present)
    passed_before = Counter(normalized(renamed.get(row.attrib['testName'], row.attrib['testName']))
                            for row in prior if row.attrib['outcome'] == 'Passed')
    passed_after = Counter(normalized(row.attrib['testName'])
                           for row in present if row.attrib['outcome'] == 'Passed')
    assert all(sum(row.attrib['testName'] == migration['previous'] for row in prior) == 1 and
               sum(row.attrib['testName'] == migration['current'] for row in present) == 1
               for migration in migrations)
    assert not before - after and not passed_before - passed_after
    assert len(prior) == 8530 and len(present) == 8866 and sum((after - before).values()) == 336
    counters = new.find('.//t:Counters', NS).attrib
    outcomes = Counter(row.attrib['outcome'] for row in present)
    assert outcomes == {'Passed': 8139, 'NotExecuted': 727}
    assert int(counters['total']) == len(present) and int(counters['passed']) == outcomes['Passed']
    assert int(counters['failed']) == 0
    report = {'baseline_transport_sha256': sidecar['sha256'],
              'baseline_trx_sha256': row['sha256'], 'current_trx_sha256': sha(CURRENT),
              'baseline_cases': len(prior), 'current_cases': len(present),
              'added_cases': 336, 'retained_prior_passes': sum(passed_before.values()),
              'outcomes': outcomes, 'declared_name_migrations': len(migrations),
              'environment_case_normalization': ENVIRONMENT_CASE}
    output = ROOT / '.tmp/storage-evidence/windows/legacy-adoption-full-v59-inventory.json'
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
