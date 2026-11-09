#!/usr/bin/env python3
"""Seal a connected adoption iteration's raw results, sources, symbols and inputs."""

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import xml.etree.ElementTree as ET


NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def inside(task, name):
    path = (task / name).resolve(strict=True)
    if task not in path.parents or not path.is_file() or path.is_symlink():
        raise ValueError('The evidence input escapes the named guest task: ' + name)
    return path


def count(path):
    tree = ET.parse(path)
    rows = tree.findall('.//t:UnitTestResult', NS)
    counters = tree.find('.//t:Counters', NS).attrib
    outcomes = [row.attrib['outcome'] for row in rows]
    if len(rows) != int(counters['total']) or any(value not in ('Passed', 'NotExecuted') for value in outcomes) or \
            outcomes.count('Passed') != int(counters['passed']) or int(counters['failed']) != 0:
        raise ValueError('The raw test result is not a complete passing run: ' + str(path))
    return dict(total=len(rows), passed=outcomes.count('Passed'), skipped=outcomes.count('NotExecuted'), failed=0)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--task', type=Path, required=True)
    parser.add_argument('--folder', required=True)
    parser.add_argument('--platform', choices=('windows', 'mac', 'ubuntu'), required=True)
    parser.add_argument('--commit', required=True)
    parser.add_argument('--packet', action='append', required=True, help='name:sha256, in applied order')
    parser.add_argument('--project', action='append', required=True)
    parser.add_argument('--test', action='append', required=True, help='label=path relative to task')
    args = parser.parse_args()
    current_platform = {'win32': 'windows', 'darwin': 'mac', 'linux': 'ubuntu'}.get(sys.platform)
    if current_platform != args.platform or not args.folder.replace('-', '').isalnum():
        raise ValueError('The evidence folder or platform is invalid')
    if len(args.commit) < 8 or any(c not in '0123456789abcdef' for c in args.commit.lower()):
        raise ValueError('A pinned commit is required')
    task = args.task.resolve(strict=True)
    root = task / 'archive-workflow-source-v1'
    run = task / args.folder
    if not run.is_dir() or (run / 'status.txt').read_text().strip() != 'PASS':
        raise ValueError('The connected guest batch did not finish cleanly')
    out = task / (args.folder + '-seal')
    out.mkdir(exist_ok=False)
    (out / 'packets').mkdir()
    (out / 'source').mkdir()
    (out / 'tests').mkdir()
    (out / 'binaries').mkdir()
    source = {}
    packets = []
    for value in args.packet:
        name, separator, expected = value.partition(':')
        if not separator or Path(name).name != name or len(expected) != 64 or \
                any(c not in '0123456789abcdef' for c in expected.lower()):
            raise ValueError('A packet needs its exact SHA-256')
        packet = inside(task, name)
        if sha(packet) != expected.lower():
            raise ValueError('A staged source packet changed: ' + name)
        shutil.copyfile(packet, out / 'packets' / name)
        packets.append(dict(name=name, sha256=expected.lower()))
        with tarfile.open(packet) as archive:
            for member in archive:
                relative = Path(member.name)
                if relative.is_absolute() or '..' in relative.parts or not (member.isfile() or member.isdir()):
                    raise ValueError('An unexpected source packet member was found')
                if member.isdir():
                    continue
                data = archive.extractfile(member).read()
                source[relative.as_posix()] = hashlib.sha256(data).hexdigest()
                target = out / 'source' / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
    for relative, expected in source.items():
        current = root / relative
        if not current.is_file() or current.is_symlink() or sha(current) != expected:
            raise ValueError('Guest source differs from the final staged packet: ' + relative)
    tests = {}
    for value in args.test:
        label, separator, relative = value.partition('=')
        if not separator or not label.replace('-', '').isalnum() or label in tests:
            raise ValueError('A raw result needs a unique simple label')
        path = inside(task, relative)
        target = out / 'tests' / (label + '.trx')
        shutil.copyfile(path, target)
        tests[label] = count(target)
    binaries = out / 'binaries'
    for name in args.project:
        if name not in ('FileCat.Core.Tests', 'FileCat.App.Tests', 'FileCat.Remote.Tests',
                        'FileCat.Platform.Windows.Tests'):
            raise ValueError('Unexpected test project')
        directory = root / 'tests' / name / 'bin' / 'Release' / 'net10.0'
        for path in directory.iterdir():
            if path.suffix not in ('.dll', '.pdb') or not (path.name.startswith('FileCat.') or
                    path.name in ('FileCat.dll', 'FileCat.pdb')):
                continue
            target = binaries / path.name
            if target.exists():
                if sha(target) != sha(path):
                    raise ValueError('Connected projects used different product or test bytes: ' + path.name)
            else:
                shutil.copyfile(path, target)
    if not (binaries / 'FileCat.Core.dll').is_file() or not (binaries / 'FileCat.Core.pdb').is_file():
        raise ValueError('Core binaries and symbols are missing')
    dotnet = task / 'dotnet' / ('dotnet.exe' if args.platform == 'windows' else 'dotnet')
    if args.platform == 'mac':
        dotnet = Path.home() / '.dotnet' / 'dotnet'
    nuget = Path('C:/Users/Admin/.nuget/packages') if args.platform == 'windows' else Path.home() / '.nuget' / 'packages'
    auditor = root / 'eng/validation/archive-writing/PdbSourceAudit/bin/Release/net10.0/PdbSourceAudit.dll'
    with (out / 'compiler-audit.log').open('x') as log:
        subprocess.run([str(dotnet), str(auditor), str(root), str(binaries),
                        str(out / 'compiler-sources'), str(nuget)],
                       stdout=log, stderr=subprocess.STDOUT, check=True, timeout=300)
    facts = json.loads((out / 'compiler-sources/facts.json').read_text())
    if not facts['compilerSourceChecksumsMatched'] or not all(row['pdbBoundToAssembly'] for row in facts['binaries']):
        raise ValueError('The compiler-source audit failed')
    uncompiled = []
    for relative, expected in source.items():
        if not relative.endswith('.cs'):
            continue
        matches = [row for row in facts['sources'] if row['document'].replace('\\', '/').endswith('/' + relative)]
        if not matches and args.platform != 'windows' and relative.startswith('tests/FileCat.Platform.Windows.Tests/'):
            uncompiled.append(relative)
            continue
        if not matches or any(row['sha256'].lower() != expected for row in matches):
            raise ValueError('Changed source is absent from the compiled candidate: ' + relative)
    (out / 'executed-sealer.py').write_bytes(Path(__file__).read_bytes())
    (out / 'summary.json').write_text(json.dumps(dict(platform=args.platform, folder=args.folder,
        commit=args.commit.lower(), packets=packets, source=source, tests=tests,
        uncompiled_platform_sources=uncompiled, production_store_enabled=False), indent=2) + '\n')
    rows = [dict(path=file.relative_to(out).as_posix(), bytes=file.stat().st_size, sha256=sha(file))
            for file in sorted(out.rglob('*')) if file.is_file()]
    (out / 'files.json').write_text(json.dumps(dict(files=rows), indent=2) + '\n')
    archive = task / (args.folder + '-evidence.tar.gz')
    with tarfile.open(archive, 'x:gz', compresslevel=1) as writer:
        writer.add(out, arcname=out.name)
    archive.with_suffix('.json').write_text(json.dumps(dict(bytes=archive.stat().st_size,
        sha256=sha(archive)), indent=2) + '\n')
    print(json.dumps(dict(platform=args.platform, tests=tests, bytes=archive.stat().st_size,
                          sha256=sha(archive))))


if __name__ == '__main__':
    main()
