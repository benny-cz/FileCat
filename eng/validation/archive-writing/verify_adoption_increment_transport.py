#!/usr/bin/env python3
"""Independently verify a connected adoption iteration transport against Git and raw results."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tarfile
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[3]
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def raw_counts(data):
    tree = ET.fromstring(data)
    rows = tree.findall('.//t:UnitTestResult', NS)
    counts = tree.find('.//t:Counters', NS).attrib
    outcomes = [row.attrib['outcome'] for row in rows]
    result = dict(total=len(rows), passed=outcomes.count('Passed'),
                  skipped=outcomes.count('NotExecuted'), failed=outcomes.count('Failed'))
    if result['total'] != int(counts['total']) or result['passed'] != int(counts['passed']) or \
            result['failed'] != int(counts['failed']) or result['failed'] or \
            result['total'] != result['passed'] + result['skipped'] or not result['passed']:
        raise ValueError('A raw guest result is incomplete or failed')
    return result


def verify(path, profile='adoption'):
    path = path.resolve(strict=True)
    sidecar = json.loads(path.with_suffix('.json').read_text(encoding='utf-8-sig'))
    if path.stat().st_size != sidecar['bytes'] or sha(path.read_bytes()) != sidecar['sha256']:
        raise ValueError('The transport differs from its sidecar')
    prefix = path.name.removesuffix('-evidence.tar.gz') + '-seal'
    data = {}
    with tarfile.open(path) as archive:
        for member in archive:
            parts = Path(member.name).parts
            if not parts or parts[0] != prefix or '..' in parts or Path(member.name).is_absolute():
                raise ValueError('Unexpected evidence member')
            if member.isdir():
                continue
            if not member.isfile() or len(parts) < 2:
                raise ValueError('Unexpected non-file evidence member')
            relative = Path(*parts[1:]).as_posix()
            if relative in data:
                raise ValueError('Duplicate evidence member')
            data[relative] = archive.extractfile(member).read()
    index = json.loads(data['files.json'])
    expected = {row['path']: {'bytes': row['bytes'], 'sha256': row['sha256']}
                for row in index['files']}
    actual = {name: {'bytes': len(value), 'sha256': sha(value)}
              for name, value in data.items() if name != 'files.json'}
    if len(expected) != len(index['files']) or expected != actual:
        raise ValueError('The internal evidence inventory differs from the archive')
    summary = json.loads(data['summary.json'])
    if summary['folder'] != path.name.removesuffix('-evidence.tar.gz') or \
            summary['production_store_enabled'] is not False:
        raise ValueError('Wrong candidate or production status')
    sealer = (ROOT / 'eng/validation/archive-writing/seal_adoption_increment_guest.py').read_bytes()
    if data['executed-sealer.py'].replace(b'\r\n', b'\n') != sealer.replace(b'\r\n', b'\n'):
        raise ValueError('An unexpected sealer executed on the guest')
    source = {}
    for row in summary['packets']:
        packet = data['packets/' + row['name']]
        if sha(packet) != row['sha256']:
            raise ValueError('A source packet differs from its declared hash')
        import io
        with tarfile.open(fileobj=io.BytesIO(packet)) as archive:
            for member in archive:
                if member.isdir():
                    continue
                if not member.isfile() or '..' in Path(member.name).parts or Path(member.name).is_absolute():
                    raise ValueError('Unexpected source packet member')
                source[member.name] = archive.extractfile(member).read()
    if {name: sha(value) for name, value in source.items()} != summary['source']:
        raise ValueError('The final source map differs from the applied packets')
    for name, value in source.items():
        if data['source/' + name] != value:
            raise ValueError('The sealed guest source differs from its packet')
        committed = subprocess.check_output(['git', 'show', summary['commit'] + ':' + name], cwd=ROOT)
        if value.replace(b'\r\n', b'\n') != committed.replace(b'\r\n', b'\n'):
            raise ValueError('The guest source differs from the pinned Git commit: ' + name)
    facts = json.loads(data['compiler-sources/facts.json'])
    if not facts['compilerSourceChecksumsMatched'] or not all(row['pdbBoundToAssembly'] for row in facts['binaries']):
        raise ValueError('Compiler checksum or PDB identity failure')
    for row in facts['binaries']:
        for suffix, key in (('.dll', 'dllSha256'), ('.pdb', 'pdbSha256')):
            if sha(data['binaries/' + row['assembly'] + suffix]) != row[key].lower():
                raise ValueError('A sealed binary differs from the compiler audit')
    for row in facts['sources']:
        if sha(data['compiler-sources/' + row['snapshot']]) != row['sha256'].lower():
            raise ValueError('A PDB source snapshot differs from the compiler audit')
    for name, expected_hash in summary['source'].items():
        if not name.endswith('.cs') or name in summary['uncompiled_platform_sources']:
            continue
        matches = [row for row in facts['sources'] if row['document'].replace('\\', '/').endswith('/' + name)]
        if not matches or any(row['sha256'].lower() != expected_hash for row in matches):
            raise ValueError('A changed source file is not bound to compiled symbols: ' + name)
    if profile == 'adoption':
        required = {'focused-core', 'focused-ui', 'full-core', 'full-remote', 'full-app'}
        if set(summary['tests']) != required:
            raise ValueError('The connected test inventory is incomplete')
    elif profile == 'native-ui-v96':
        expected_ui = {
            'creation-native-ui': ('ArchiveCreationWorkflowTests.Actual_confined_application_workers_create_', 10),
            'member-native-ui': ('ArchiveMemberNativeUiTests.Actual_native_member_and_transfer_commands_', 10),
            'edit-native-ui': ('ArchiveEditNativeUiTests.Actual_native_F4_commit_', 10),
            'cleanup-native-ui': ('ArchiveRetainedCleanupNativeUiTests.Actual_NTFS_archive_cleanup_', 4),
        }
        if summary['platform'] != 'windows' or summary['folder'] != 'archive-native-ui-v96' or \
                set(summary['tests']) != set(expected_ui):
            raise ValueError('The native UI candidate or suite inventory is wrong')
        for label, (case_prefix, count) in expected_ui.items():
            result = summary['tests'][label]
            rows = ET.fromstring(data['tests/' + label + '.trx']).findall('.//t:UnitTestResult', NS)
            if result != dict(total=count, passed=count, skipped=0, failed=0) or \
                    len(rows) != count or any(not row.attrib['testName'].startswith('FileCat.App.Tests.' + case_prefix)
                                             for row in rows):
                raise ValueError('A native UI suite omitted or substituted a required case')
    else:
        raise ValueError('Unknown connected evidence profile')
    for label, counts in summary['tests'].items():
        if raw_counts(data['tests/' + label + '.trx']) != counts:
            raise ValueError('A test summary differs from raw guest results')
    return dict(platform=summary['platform'], commit=summary['commit'],
                tests=summary['tests'], transport_bytes=path.stat().st_size,
                transport_sha256=sidecar['sha256'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('path', type=Path)
    parser.add_argument('--profile', choices=('adoption', 'native-ui-v96'), default='adoption')
    args = parser.parse_args()
    print(json.dumps(verify(args.path, args.profile), indent=2))
