"""Run the actual source configuration CLI against owned files, preserving all unrelated bytes."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--evidence-dir', type=Path, required=True)
    args = parser.parse_args()
    args.evidence_dir.mkdir(parents=True, exist_ok=False)
    script = Path(__file__).with_name('configure-linux-ci-mirror.py')
    controls = []
    fixtures = [
        ('deb822', 'sources.list.d/ubuntu.sources', b'Types: deb\r\nURIs: http://azure.archive.ubuntu.com/ubuntu/\r\nSuites: noble noble-updates\r\nComponents: main universe\r\nSigned-By: /usr/share/keyrings/ubuntu-archive-keyring.gpg\r\n', 1),
        ('legacy', 'sources.list', b'deb http://azure.archive.ubuntu.com/ubuntu noble main\ndeb https://azure.archive.ubuntu.com/ubuntu/ noble-updates main\ndeb http://security.ubuntu.com/ubuntu noble-security main\n', 2),
        ('already-canonical', 'sources.list.d/ubuntu.sources', b'URIs: https://archive.ubuntu.com/ubuntu/\nSuites: noble\nSigned-By: /usr/share/keyrings/ubuntu-archive-keyring.gpg\n', 0),
        ('unrelated', 'sources.list', b'deb https://azure.archive.ubuntu.com.evil/ubuntu noble main\ndeb https://azure.archive.ubuntu.com/ubuntu-other noble main\ndeb https://packages.microsoft.com/repos/code stable main\n', 0),
        ('mirror-list', 'apt-mirrors.txt', b'# Hosted mirror priorities\r\nhttp://azure.archive.ubuntu.com/ubuntu/\tpriority:1\r\nhttps://archive.ubuntu.com/ubuntu/\tpriority:2\r\n', 1),
        ('mirror-list-already-canonical', 'apt-mirrors.txt', b'https://archive.ubuntu.com/ubuntu/\tpriority:1\n', 0),
        ('mirror-list-unrelated', 'apt-mirrors.txt', b'https://azure.archive.ubuntu.com.evil/ubuntu/\nhttps://azure.archive.ubuntu.com/ubuntu-other/\nhttps://security.ubuntu.com/ubuntu/\n', 0),
    ]
    with tempfile.TemporaryDirectory(prefix='filecat-owned-ci-mirror-') as folder:
        root = Path(folder)
        for name, rel, before, count in fixtures:
            source = root / name
            target = source / rel
            target.parent.mkdir(parents=True)
            target.write_bytes(before)
            ignored = source / 'sources.list.d/third-party.list'
            ignored.parent.mkdir(exist_ok=True)
            ignored.write_bytes(b'deb https://azure.archive.ubuntu.com/ubuntu noble main\n')
            before_time = target.stat().st_mtime_ns
            command = [sys.executable, str(script), '--sources-root', str(source), '--evidence-file', str(args.evidence_dir / (name + '.json'))]
            result = subprocess.run(command, capture_output=True, timeout=10)
            assert result.returncode == 0, (name, result.stderr)
            report = json.loads((args.evidence_dir / (name + '.json')).read_text())
            after = target.read_bytes()
            expected = before.replace(b'http://azure.archive.ubuntu.com/ubuntu', b'https://archive.ubuntu.com/ubuntu').replace(b'https://azure.archive.ubuntu.com/ubuntu', b'https://archive.ubuntu.com/ubuntu') if count else before
            assert after == expected and sum(r['Replacements'] for r in report['Records']) == count
            assert ignored.read_bytes() == b'deb https://azure.archive.ubuntu.com/ubuntu noble main\n'
            assert not list(source.rglob('.filecat-ci-*'))
            if count == 0:
                assert before_time == target.stat().st_mtime_ns
            (args.evidence_dir / (name + '.stdout')).write_bytes(result.stdout)
            (args.evidence_dir / (name + '.stderr')).write_bytes(result.stderr)
            controls.append(dict(Case=name, Arguments=command, ExitCode=result.returncode, Replacements=count,
                                 BeforeBase64=base64.b64encode(before).decode(), AfterBase64=base64.b64encode(after).decode()))
        source = root / 'linked'
        source.mkdir()
        outside = root / 'owned-outside-file'
        outside.write_bytes(b'owned outside sentinel')
        link = source / 'sources.list'
        try:
            os.symlink(outside, link)
        except OSError as error:
            controls.append(dict(Case='linked', Skipped=True, Reason=str(error)))
        else:
            result = subprocess.run([sys.executable, str(script), '--sources-root', str(source), '--evidence-file', str(args.evidence_dir / 'linked.json')], capture_output=True, timeout=10)
            assert result.returncode != 0 and outside.read_bytes() == b'owned outside sentinel' and link.is_symlink()
            (args.evidence_dir / 'linked.stderr').write_bytes(result.stderr)
            controls.append(dict(Case='linked', ExitCode=result.returncode, RejectedBeforeWrite=True, OutsideSHA256=hashlib.sha256(outside.read_bytes()).hexdigest()))
        # The mirror list is last in the target list: reject it before changing an earlier valid source file.
        source = root / 'linked-mirror-list'
        source.mkdir()
        before = b'deb http://azure.archive.ubuntu.com/ubuntu noble main\n'
        earlier = source / 'sources.list'
        earlier.write_bytes(before)
        link = source / 'apt-mirrors.txt'
        try:
            os.symlink(outside, link)
        except OSError as error:
            controls.append(dict(Case='linked-mirror-list', Skipped=True, Reason=str(error)))
        else:
            result = subprocess.run([sys.executable, str(script), '--sources-root', str(source), '--evidence-file', str(args.evidence_dir / 'linked-mirror-list.json')], capture_output=True, timeout=10)
            assert result.returncode != 0 and earlier.read_bytes() == before and outside.read_bytes() == b'owned outside sentinel' and link.is_symlink()
            (args.evidence_dir / 'linked-mirror-list.stderr').write_bytes(result.stderr)
            controls.append(dict(Case='linked-mirror-list', ExitCode=result.returncode, RejectedBeforeWrite=True,
                                 EarlierSourceSHA256=hashlib.sha256(earlier.read_bytes()).hexdigest(), OutsideSHA256=hashlib.sha256(outside.read_bytes()).hexdigest()))
    assert not root.exists()
    proof = dict(ActualConfigurationCLIExercised=True, OwnedSourceFilesOnly=True, TemporaryDirectoryRemoved=True,
                 NativeRunnerMirrorOrPackageInstallQualified=False, Controls=controls)
    (args.evidence_dir / 'controls.json').write_text(json.dumps(proof, indent=2), encoding='utf-8')
    print(json.dumps(dict(Passed=sum(not c.get('Skipped', False) for c in controls), Skipped=sum(c.get('Skipped', False) for c in controls))))


if __name__ == '__main__':
    main()
