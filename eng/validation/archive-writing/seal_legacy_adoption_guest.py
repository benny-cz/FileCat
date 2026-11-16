#!/usr/bin/env python3
"""Seal source, symbols and raw guest tests for the staged legacy adoption candidate."""

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import xml.etree.ElementTree as ET


CANDIDATE = '28e24a97'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def inside(task, relative):
    path = (task / relative).resolve(strict=True)
    if path == task or task not in path.parents or not path.is_file() or path.is_symlink():
        raise ValueError('Evidence input escapes the named guest task: ' + relative)
    return path


def test_counts(path):
    xml = ET.parse(path)
    rows = xml.findall('.//t:UnitTestResult', NS)
    counts = xml.find('.//t:Counters', NS).attrib
    if len(rows) != int(counts['total']) or int(counts['failed']) != 0 or int(counts['passed']) <= 0 or \
            sum(row.attrib['outcome'] == 'Passed' for row in rows) != int(counts['passed']) or \
            any(row.attrib['outcome'] not in ('Passed', 'NotExecuted') for row in rows):
        raise ValueError('The raw guest TRX does not describe a passing complete test run: ' + str(path))
    return {key: int(counts[key]) for key in ('total', 'passed', 'notExecuted', 'failed')}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--task', type=Path, required=True)
    parser.add_argument('--platform', choices=('windows', 'mac', 'ubuntu'), required=True)
    parser.add_argument('--folder', required=True)
    parser.add_argument('--packet-sha256', required=True)
    parser.add_argument('--test-project', choices=('FileCat.Core.Tests', 'FileCat.Platform.Windows.Tests'), required=True)
    parser.add_argument('--test', action='append', required=True, help='label=path relative to the task')
    parser.add_argument('--log', action='append', default=[], help='path relative to the task')
    args = parser.parse_args()
    platform = {'win32': 'windows', 'darwin': 'mac', 'linux': 'ubuntu'}.get(sys.platform)
    if platform != args.platform or not args.folder.replace('-', '').isalnum():
        raise ValueError('Platform or evidence folder differs from this guest')
    task = args.task.resolve(strict=True)
    packet = inside(task, 'adoption-28e24a97.tar.gz')
    if sha(packet) != args.packet_sha256:
        raise ValueError('The adoption source packet changed')
    root = (task / 'archive-workflow-source-v1').resolve(strict=True)
    out = task / args.folder
    out.mkdir(exist_ok=False)
    (out / 'source').mkdir()
    shutil.copyfile(packet, out / 'source-packet.tar.gz')
    with tarfile.open(packet) as archive:
        members = archive.getmembers()
        if len(members) != 18 or any(not row.isfile() or row.name.startswith('/') or '..' in Path(row.name).parts
                                     for row in members):
            raise ValueError('The committed source packet inventory changed')
        source = {}
        for row in members:
            data = archive.extractfile(row).read()
            current = root / row.name
            if not current.is_file() or current.is_symlink() or current.read_bytes() != data:
                raise ValueError('Guest source differs from the committed packet: ' + row.name)
            target = out / 'source' / row.name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            source[row.name] = hashlib.sha256(data).hexdigest()

    tests = {}
    (out / 'tests').mkdir()
    for value in args.test:
        label, separator, relative = value.partition('=')
        if not separator or not label.replace('-', '').isalnum() or label in tests:
            raise ValueError('A test needs a unique simple label')
        path = inside(task, relative)
        if path.stat().st_size > 60 * 1024 * 1024:
            raise ValueError('A raw test result exceeds its evidence budget')
        target = out / 'tests' / (label + '.trx')
        shutil.copyfile(path, target)
        tests[label] = test_counts(target)
    (out / 'logs').mkdir()
    for index, relative in enumerate(args.log):
        path = inside(task, relative)
        if path.stat().st_size > 8 * 1024 * 1024:
            raise ValueError('A build/test log exceeds its evidence budget')
        shutil.copyfile(path, out / 'logs' / f'{index:02d}-{path.name}')

    project = root / 'tests' / args.test_project / 'bin' / 'Release' / 'net10.0'
    binaries = out / 'binaries'
    binaries.mkdir()
    for path in sorted(project.iterdir()):
        if path.suffix not in ('.dll', '.pdb') or not path.name.startswith('FileCat.'):
            continue
        if path.is_symlink() or path.stat().st_size > 80 * 1024 * 1024:
            raise ValueError('A guest binary is not a bounded ordinary file: ' + path.name)
        shutil.copyfile(path, binaries / path.name)
    if not (binaries / 'FileCat.Core.dll').is_file() or not (binaries / 'FileCat.Core.pdb').is_file():
        raise ValueError('The guest Core assembly and compiler symbols are missing')
    if platform == 'windows':
        # The focused Windows cases live in Platform.Windows.Tests, but full-core uses
        # Core.Tests. Bind both executed test assemblies to the same product binaries.
        core_tests = root / 'tests' / 'FileCat.Core.Tests' / 'bin' / 'Release' / 'net10.0'
        for name in ('FileCat.Core.dll', 'FileCat.Core.pdb'):
            if sha(core_tests / name) != sha(binaries / name):
                raise ValueError('Windows test projects used different Core binaries: ' + name)
        for name in ('FileCat.Core.Tests.dll', 'FileCat.Core.Tests.pdb'):
            path = core_tests / name
            if not path.is_file() or path.is_symlink() or path.stat().st_size > 80 * 1024 * 1024:
                raise ValueError('The full Core test binary or symbols are unavailable: ' + name)
            shutil.copyfile(path, binaries / name)
    dotnet = task / 'dotnet' / ('dotnet.exe' if platform == 'windows' else 'dotnet')
    if platform == 'mac':
        dotnet = Path.home() / '.dotnet' / 'dotnet'
    nuget = Path('C:/Users/Admin/.nuget/packages') if platform == 'windows' else Path.home() / '.nuget' / 'packages'
    auditor = root / 'eng' / 'validation' / 'archive-writing' / 'PdbSourceAudit' / 'bin' / 'Release' / 'net10.0' / 'PdbSourceAudit.dll'
    with (out / 'source-audit.log').open('w') as log:
        subprocess.run([str(dotnet), str(auditor), str(root), str(binaries), str(out / 'compiler-sources'), str(nuget)],
                       stdout=log, stderr=subprocess.STDOUT, check=True, timeout=180)
    facts = json.loads((out / 'compiler-sources' / 'facts.json').read_text())
    if not facts['compilerSourceChecksumsMatched'] or not all(row['pdbBoundToAssembly'] for row in facts['binaries']):
        raise ValueError('Guest compiler-source audit failed')
    compiled = {row['document'].replace('\\', '/').rsplit('/', 1)[-1]: row for row in facts['sources']}
    required = {'ILegacyEditSource.cs', 'EditSessions.LegacyAdoption.cs', 'EditSessions.cs',
                'EditSessions.Storage.cs', 'UnixLegacyEditSource.cs'}
    if platform == 'windows':
        required |= {'ArchiveEditLegacyAdoptionCases.cs', 'WindowsLegacyEditSource.cs',
                     'WindowsLegacyEditAdoptionTests.cs'}
    else:
        required |= {'ArchiveEditLegacyAdoptionCases.cs', 'UnixLegacyEditAdoptionTests.cs',
                     'UnixLegacyEditSourceTests.cs'}
    if not required.issubset(compiled):
        raise ValueError('A changed adoption source is absent from the compiler audit: ' + str(required - compiled.keys()))
    for name in required:
        relative = next(path for path in source if path.endswith('/' + name))
        if compiled[name]['sha256'].lower() != source[relative]:
            raise ValueError('The compiled adoption source differs from the committed packet: ' + name)
    shutil.copyfile(Path(__file__), out / 'executed-sealer.py')
    summary = {'candidate': CANDIDATE, 'platform': platform, 'packet_sha256': args.packet_sha256,
               'source_files': source, 'tests': tests, 'compiler_audit_sha256': sha(out / 'compiler-sources' / 'facts.json'),
               'production_store_enabled': False}
    (out / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
    rows = [{'path': file.relative_to(out).as_posix(), 'bytes': file.stat().st_size, 'sha256': sha(file)}
            for file in sorted(out.rglob('*')) if file.is_file()]
    (out / 'files.json').write_text(json.dumps({'files': rows}, indent=2) + '\n')
    packet_out = task / (args.folder + '-evidence.tar.gz')
    with tarfile.open(packet_out, 'x:gz', compresslevel=1) as archive:
        archive.add(out, arcname=out.name)
    packet_out.with_suffix('.json').write_text(json.dumps({'bytes': packet_out.stat().st_size,
                                                            'sha256': sha(packet_out)}, indent=2) + '\n')
    print(json.dumps({'platform': platform, 'tests': tests, 'archive_sha256': sha(packet_out),
                      'archive_bytes': packet_out.stat().st_size}))


if __name__ == '__main__':
    main()
