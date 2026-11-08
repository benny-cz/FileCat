"""Bounded, reusable verification of actual guest batch receipts; never runs tests on the host."""
import datetime
import hashlib
import json
import pathlib
import re
import shutil
import subprocess
import tarfile
import xml.etree.ElementTree as ET


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def reports(path):
    result = {}
    with tarfile.open(path) as archive:
        members = archive.getmembers()
        assert len(members) <= 1000 and sum(member.size for member in members) <= 256 * 1024 * 1024
        for member in members:
            name = pathlib.PurePosixPath(member.name)
            assert not name.is_absolute() and '..' not in name.parts and ':' not in member.name
            assert member.isfile() or member.isdir()
            if member.isdir() or len(name.parts) != 1:
                continue
            assert name.name not in result and member.size <= 32 * 1024 * 1024
            result[name.name] = archive.extractfile(member).read()
    return result


def assembly(receipts, phase, revision):
    return ET.fromstring(receipts[f'{phase}-{revision}.xml']).find('assembly')


def assert_clean_build(path, projects):
    text = path.read_text(encoding='utf-8-sig')
    assert 'Build FAILED.' not in text
    for pattern in (r'^Build succeeded\.$', r'^    0 Warning\(s\)$', r'^    0 Error\(s\)$'):
        assert len(re.findall(pattern, text, re.M)) == projects


def qualify(root, revision, expected, prefixes, new_cases, build_projects=4, phase_revisions=None, expected_states=None, source_snapshot=None, foreign_post_processes=None):
    assert re.fullmatch(r'r[0-9]+[a-z]?', revision)
    temp = root / '.tmp'
    out = root / 'docs/validation/disk-space'
    packing_path = temp / f'pack-timing-{revision}.json'
    packing = read(packing_path)
    sources_path = root / packing['sourceManifest']
    sources = read(sources_path)
    assert sources
    snapshot_path = None
    if source_snapshot is None:
        assert all(sha(root / source['path']) == source['sha256'] for source in sources)
    else:
        # A helper-only rerun may update a test after its original broad cohort was
        # frozen. Verify the exact original sources without restoring/replacing the
        # current checkout. The caller separately proves every helper input delta.
        snapshot_path = root / source_snapshot
        assert snapshot_path.resolve().is_relative_to(root.resolve())
        with tarfile.open(snapshot_path) as archive:
            members = archive.getmembers()
            assert len(members) <= 1000 and sum(member.size for member in members) <= 64 * 1024 * 1024
            expected_sources = {source['path']: source['sha256'] for source in sources}
            assert len(expected_sources) == len(sources) == len(members)
            assert {member.name for member in members} == set(expected_sources)
            for member in members:
                name = pathlib.PurePosixPath(member.name)
                assert member.isfile() and not name.is_absolute() and '..' not in name.parts and ':' not in member.name
                assert hashlib.sha256(archive.extractfile(member).read()).hexdigest() == expected_sources[member.name]
    build_path = root / packing['buildLog']
    assert_clean_build(build_path, build_projects)
    assert packing['archiveCount'] == 3 and {row['platform'] for row in packing['archives']} == {'windows', 'mac', 'linux'}
    matrix, inputs, processes, receipts = {}, {}, {}, {}
    files = {packing_path, sources_path, build_path, temp / f'app-settings-inputs-{revision}.json'}
    if snapshot_path is not None:
        files.add(snapshot_path)
    runners = set()
    phase_revisions = phase_revisions or {}
    expected_states = expected_states or {}
    foreign_post_processes = foreign_post_processes or {}
    def check_processes(inventory, key):
        # A later foreign tester need not delay receipt verification for a
        # completed cohort. Any exception is an exact previously inspected
        # process list, retained by the caller with explicit foreign provenance.
        actual = inventory['filecatOrDotnet']
        assert not actual or actual == foreign_post_processes.get(key)
    for platform in ('windows', 'mac', 'ubuntu'):
        tag = 'linux' if platform == 'ubuntu' else platform
        bundle = next(row for row in packing['archives'] if row['platform'] == tag)
        assert sha(pathlib.Path(bundle['path'])) == bundle['sha256']
        stage = temp / f'batch-{revision}-{tag}'
        inputs[platform] = [{'path': path.relative_to(stage).as_posix(), 'sha256': sha(path)}
                            for path in sorted(stage.rglob('*')) if path.is_file()]
        assert inputs[platform]
        archive_path = temp / f'{platform}-batch-{revision}-results.tgz'
        result = receipts[platform] = reports(archive_path)
        assert result['state.txt'].decode('utf-8-sig').strip() == expected_states.get(platform, 'FINISHED exit=0')
        inventory = read(temp / f'{platform}-batch-{revision}-post.json')
        check_processes(inventory, platform)
        processes[platform] = inventory
        for phase, (total, skip) in expected[platform].items():
            actual_revision = phase_revisions.get(f'{platform}/{phase}', revision)
            actual_result = result
            if actual_revision != revision:
                extra_path = temp / f'{platform}-batch-{actual_revision}-results.tgz'
                actual_result = reports(extra_path)
                assert actual_result['state.txt'].decode('utf-8-sig').strip() == 'FINISHED exit=0'
                extra_inventory = read(temp / f'{platform}-batch-{actual_revision}-post.json')
                check_processes(extra_inventory, f'{platform}/{actual_revision}')
                processes[f'{platform}/{actual_revision}'] = extra_inventory
                files.add(extra_path)
                runners.add(temp / f'batch-{tag}-{actual_revision}.{"ps1" if platform == "windows" else "sh"}')
            a = assembly(actual_result, phase, actual_revision)
            assert a.get('failed') == a.get('errors') == '0'
            assert (int(a.get('total')), int(a.get('passed')), int(a.get('skipped'))) == (total, total - skip, skip)
            skipped = lambda node: sorted(test.get('name') for test in node.findall('.//test') if test.get('result') == 'Skip')
            old = ET.parse(out / f'{platform}-batch-r1036-{phase}-r1036.xml').getroot().find('assembly')
            assert skipped(a) == skipped(old)
            row = {'revision': actual_revision, **{key: a.get(key) for key in ('total', 'passed', 'failed', 'skipped', 'errors', 'time')}, 'skippedNames': skipped(a)}
            if phase in ('preview', 'render'):
                cases = [test for test in a.findall('.//test') if (test.get('method') or '').startswith(prefixes)]
                assert len(cases) == new_cases and all(case.get('result') == 'Pass' for case in cases)
                row['newCases'] = sorted(case.get('name') for case in cases)
                assert len(set(row['newCases'])) == new_cases
            matrix[f'{platform}/{phase}'] = row
        files.add(archive_path)
        runners.add(temp / f'batch-{tag}-{revision}.{"ps1" if platform == "windows" else "sh"}')
    record = {'revision': revision, 'observedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'gitBase': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
              'sources': sources, 'inputs': inputs, 'matrix': matrix, 'postRunProcesses': processes}
    if snapshot_path is not None:
        record['productSourceSnapshot'] = {'path': (out / snapshot_path.name).relative_to(root).as_posix(), 'sha256': sha(snapshot_path)}
    return record, files, runners, receipts


def retain(root, record, files, runners, helpers):
    out = root / 'docs/validation/disk-space'
    for source in files | runners:
        destination = out / ('drivers' if source in runners else '') / source.name
        if destination.exists():
            assert sha(source) == sha(destination), 'Existing evidence cannot be overwritten'
        else:
            shutil.copy2(source, destination)
    record['artifacts'] = [{'path': (out / path.name).relative_to(root).as_posix(), 'sha256': sha(out / path.name)} for path in sorted(files)]
    paths = {out / 'drivers' / path.name for path in runners} | {out / 'drivers' / name for name in helpers}
    record['drivers'] = [{'path': path.relative_to(root).as_posix(), 'sha256': sha(path)} for path in sorted(paths)]


def freeze_text(root, record, filename):
    """Preserve tested source/receipt bytes independently of checkout newline conversion."""
    target = root / 'docs/validation/disk-space' / filename
    assert not target.exists()
    entries = {item['path']: item['sha256'] for group in ('sources', 'artifacts', 'drivers') for item in record[group]
               if not item['path'].endswith('.tgz')}
    with tarfile.open(target, 'w:gz') as archive:
        for name, digest in sorted(entries.items()):
            path = root / name
            assert path.resolve().is_relative_to(root.resolve()) and path.is_file() and not path.is_symlink()
            assert sha(path) == digest
            archive.add(path, arcname=name, recursive=False)
    record['artifacts'].append({'path': target.relative_to(root).as_posix(), 'sha256': sha(target)})
    record['exactTextSnapshot'] = {'path': target.relative_to(root).as_posix(), 'entries': sorted(entries),
                                   'purpose': 'Exact compiled source and producer receipt bytes; use these when Git checkout newline conversion differs.'}
