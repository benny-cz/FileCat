#!/usr/bin/env python3
"""Verify an adoption guest transport against Git, compiler symbols and raw TRX outcomes."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tarfile
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[3]
CANDIDATE = '28e24a97'
PACKET_SHA = 'a644af3dee2aa97994aee24f90ec0cf7f382621d0c6bb0d9dd762c699aad0e9a'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def git(path):
    return subprocess.check_output(['git', 'show', CANDIDATE + ':' + path], cwd=ROOT)


def verify(path):
    path = path.resolve(strict=True)
    sidecar = json.loads(path.with_suffix('.json').read_text(encoding='utf-8-sig'))
    if path.stat().st_size != sidecar['bytes'] or sha(path.read_bytes()) != sidecar['sha256']:
        raise ValueError('Adoption transport differs from its sidecar')
    retained = {}
    observed = {}
    with tarfile.open(path) as archive:
        for member in archive:
            parts = Path(member.name).parts
            if not parts or parts[0] != path.name.removesuffix('-evidence.tar.gz') or '..' in parts:
                raise ValueError('Unexpected evidence member: ' + member.name)
            if member.isdir():
                continue
            if not member.isfile() or len(parts) < 2:
                raise ValueError('Unexpected evidence member: ' + member.name)
            name = Path(*parts[1:]).as_posix()
            if name in observed:
                raise ValueError('Duplicate evidence member: ' + name)
            data = archive.extractfile(member).read()
            observed[name] = {'bytes': len(data), 'sha256': sha(data)}
            if name in ('files.json', 'summary.json', 'compiler-sources/facts.json', 'executed-sealer.py',
                        'source-packet.tar.gz') or name.startswith(('source/', 'binaries/', 'tests/')):
                retained[name] = data
    index = json.loads(retained['files.json'])
    expected = {row['path']: {'bytes': row['bytes'], 'sha256': row['sha256']} for row in index['files']}
    assert len(expected) == len(index['files']) and expected == {k: v for k, v in observed.items() if k != 'files.json'}
    summary = json.loads(retained['summary.json'])
    assert summary['candidate'] == CANDIDATE and summary['packet_sha256'] == PACKET_SHA
    assert summary['production_store_enabled'] is False
    assert sha(retained['source-packet.tar.gz']) == PACKET_SHA
    sealer_path = 'eng/validation/archive-writing/seal_legacy_adoption_guest.py'
    platform = summary['platform']
    sealer = (ROOT / sealer_path).read_bytes() if platform == 'windows' else \
        subprocess.check_output(['git', 'show', '70ba680b:' + sealer_path], cwd=ROOT)
    assert retained['executed-sealer.py'].replace(b'\r\n', b'\n') == sealer.replace(b'\r\n', b'\n')
    paths = subprocess.check_output(['git', 'diff-tree', '--no-commit-id', '--name-only', '-r', CANDIDATE], cwd=ROOT).decode().splitlines()
    assert len(paths) == 18 and set(summary['source_files']) == set(paths)
    for source in paths:
        data = retained['source/' + source]
        assert sha(data) == summary['source_files'][source]
        assert data.replace(b'\r\n', b'\n') == git(source).replace(b'\r\n', b'\n')
    facts = json.loads(retained['compiler-sources/facts.json'])
    assert facts['compilerSourceChecksumsMatched'] and all(row['pdbBoundToAssembly'] for row in facts['binaries'])
    assert sha(retained['compiler-sources/facts.json']) == summary['compiler_audit_sha256']
    for row in facts['binaries']:
        for suffix, key in (('.dll', 'dllSha256'), ('.pdb', 'pdbSha256')):
            assert sha(retained['binaries/' + row['assembly'] + suffix]) == row[key].lower()
    sources = facts['sources']
    required = {'src/FileCat.Core/Edit/ILegacyEditSource.cs',
                'src/FileCat.Core/Edit/EditSessions.LegacyAdoption.cs',
                'src/FileCat.Core/Edit/EditSessions.cs',
                'src/FileCat.Core/Edit/EditSessions.Storage.cs',
                'src/FileCat.Core/Edit/UnixLegacyEditSource.cs',
                'tests/ArchiveEditLegacyAdoptionCases.cs'}
    if platform == 'windows':
        assert any(row['assembly'] == 'FileCat.Core.Tests' for row in facts['binaries'])
        required |= {'src/FileCat.Platform.Windows/Archives/WindowsLegacyEditSource.cs',
                     'tests/FileCat.Platform.Windows.Tests/WindowsLegacyEditAdoptionTests.cs'}
    else:
        required |= {'tests/FileCat.Core.Tests/UnixLegacyEditSourceTests.cs',
                     'tests/FileCat.Core.Tests/UnixLegacyEditAdoptionTests.cs'}
    for source in required:
        matches = [row for row in sources if row['document'].replace('\\', '/').endswith('/' + source)]
        assert matches and all(row['sha256'].lower() == summary['source_files'][source] for row in matches), source
        for row in matches:
            snapshot = 'compiler-sources/' + row['snapshot']
            assert observed[snapshot]['sha256'] == row['sha256'].lower()
    tests = {}
    for label, counts in summary['tests'].items():
        data = retained['tests/' + label + '.trx']
        xml = ET.fromstring(data)
        rows = xml.findall('.//t:UnitTestResult', NS)
        counter = xml.find('.//t:Counters', NS).attrib
        actual = {'total': len(rows), 'passed': sum(row.attrib['outcome'] == 'Passed' for row in rows),
                  'failed': sum(row.attrib['outcome'] == 'Failed' for row in rows)}
        skipped = sum(row.attrib['outcome'] == 'NotExecuted' for row in rows)
        # VSTest's Counters.notExecuted is zero for xUnit skips. The raw result rows
        # still retain them; verify both representations without relabeling skips as passes.
        assert actual == {key: counts[key] for key in actual}
        assert all(int(counter[key]) == value for key, value in counts.items())
        assert actual['total'] == actual['passed'] + skipped and actual['failed'] == 0 and actual['passed'] > 0
        tests[label] = {**counts, 'skipped_rows': skipped}
    assert {'windows': {'legacy-adoption', 'legacy-source', 'native-storage', 'native-discard',
                        'full-core'},
            'mac': {'focused', 'full-core'},
            'ubuntu': {'focused'}}[platform] == set(tests)
    if platform == 'windows':
        assert tests['legacy-adoption'] == {'total': 6, 'passed': 6, 'notExecuted': 0, 'failed': 0, 'skipped_rows': 0}
        assert tests['legacy-source'] == {'total': 10, 'passed': 10, 'notExecuted': 0, 'failed': 0, 'skipped_rows': 0}
    else:
        assert tests['focused'] == {'total': 38, 'passed': 38, 'notExecuted': 0, 'failed': 0, 'skipped_rows': 0}
    return {'platform': platform, 'candidate': CANDIDATE, 'tests': tests,
            'transport_bytes': path.stat().st_size, 'transport_sha256': sidecar['sha256']}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('path', type=Path)
    print(json.dumps(verify(parser.parse_args().path), indent=2))
