#!/usr/bin/env python3
"""Exercise the production Windows instance protocol with profile aliases and distinct state roots."""
import argparse
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import time
import uuid


def wait_file(path, process, timeout=5):
    until = time.monotonic() + timeout
    while time.monotonic() < until:
        if path.exists():
            try:
                return json.loads(path.read_text())
            except json.JSONDecodeError:
                pass
        if process.poll() is not None:
            raise RuntimeError(f'Process exited {process.returncode} before {path.name}')
        time.sleep(.02)
    raise TimeoutError(f'Missing {path.name}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--smoke-dll', required=True, type=pathlib.Path)
    parser.add_argument('--evidence-dir', required=True, type=pathlib.Path)
    args = parser.parse_args()
    if os.name != 'nt' or args.evidence_dir.exists():
        parser.error('Requires Windows and a new evidence directory')
    dll = args.smoke_dll.resolve(strict=True)
    args.evidence_dir.mkdir()
    scratch = (args.evidence_dir / 'fixtures').resolve(); scratch.mkdir()
    (scratch / '.filecat-owned').write_text(uuid.uuid4().hex)
    portable = scratch / 'portable-payload'
    shutil.copytree(dll.parent, portable)
    (portable / 'FileCat.portable').write_text('owned instance fixture')
    commands = [[shutil.which('dotnet') or 'dotnet', str(dll)],
                [shutil.which('dotnet') or 'dotnet', str(portable / dll.name)]]
    manifest = {'smoke_dll': str(dll), 'sha256': hashlib.sha256(dll.read_bytes()).hexdigest(),
                'app_sha256': hashlib.sha256((dll.parent / 'FileCat.dll').read_bytes()).hexdigest(),
                'scratch': str(scratch), 'results': []}
    cases = ['usual-profile-case-alias', 'data-profile-case-alias',
             'distinct-default-state-roots', 'usual-and-portable-state-roots']
    for label in cases:
        root = scratch / label; root.mkdir()
        profile = 'ipc-win-' + uuid.uuid4().hex[:24]
        options = ['--profile', profile]
        with_data = label in ['data-profile-case-alias', 'distinct-default-state-roots']
        if with_data:
            options += ['--data', str(root / 'state')]
        distinct = label in ['distinct-default-state-roots', 'usual-and-portable-state-roots']
        servers = []; result = {'case': label, 'passed': False}
        try:
            for index in range(2 if distinct else 1):
                control = root / str(index); control.mkdir()
                (control / '.filecat-owned').write_text(uuid.uuid4().hex)
                command = commands[index] if label == 'usual-and-portable-state-roots' else commands[0]
                server_options = options if label != 'distinct-default-state-roots' else \
                    ['--profile', 'default' if index == 0 else 'DEFAULT', '--data', str(root / 'state')]
                with (control / 'server.log').open('wb') as log:
                    server = subprocess.Popen(command + ['serve', str(control)] + server_options,
                                              stdout=log, stderr=subprocess.STDOUT)
                servers.append((server, control, command, server_options))
                ready = wait_file(control / 'ready.json', server)
                result[f'owner_{index}_local_directory'] = ready['LocalDirectory']
            if label == 'usual-profile-case-alias':
                probe = subprocess.run(commands[0] + ['probe', str(servers[0][1]), '--profile', profile.upper()],
                                       capture_output=True, timeout=5)
                (root / 'probe.log').write_bytes(probe.stdout + probe.stderr)
                result['case_alias_probe'] = probe.returncode == 0 and json.loads(probe.stdout)['running']
            for index in range(2 if distinct else 1):
                server, control, command, forward_options = servers[index]
                if not distinct:
                    forward_options = ['--profile', profile.upper()] + (['--data', str(root / 'state')] if with_data else [])
                path = str(root / f'žluťoučký & 100% {index}')
                run = subprocess.run(command + ['forward', str(control)] + forward_options + [path],
                                     capture_output=True, timeout=5)
                (root / f'forward-{index}.log').write_bytes(run.stdout + run.stderr)
                result[f'forwarder_{index}_exit'] = run.returncode
                if run.returncode or wait_file(control / 'received.json', server) != [path]:
                    raise RuntimeError('Forwarding did not reach the owner of the matching state folder')
            if label == 'usual-profile-case-alias' and not result['case_alias_probe']:
                raise RuntimeError('Case alias missed the running usual instance')
            if any(server.poll() is not None for server, *_ in servers):
                raise RuntimeError('An owner stopped during the test')
            result['passed'] = True
        except Exception as error:
            result['error'] = str(error)
        finally:
            for server, control, *_ in servers:
                (control / 'stop').touch()
                try:
                    server.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    server.kill(); server.wait()
                if server.returncode:
                    result['passed'] = False
            for index, (_, control, *_) in enumerate(servers):
                if (control / 'ready.json').exists():
                    ready = json.loads((control / 'ready.json').read_text())
                    for key in ['LocalDirectory', 'SettingsDirectory']:
                        shutil.copytree(ready[key], root / f'owner-{index}-{key}', dirs_exist_ok=True)
        manifest['results'].append(result)
        print(json.dumps(result, ensure_ascii=False), flush=True)
    (args.evidence_dir / 'results.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2))
    return 0 if all(result['passed'] for result in manifest['results']) else 1


if __name__ == '__main__':
    raise SystemExit(main())
