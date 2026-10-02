#!/usr/bin/env python3
"""Cross-process oracle for FileCat's production Unix instance election, forwarding and read-only usual-instance probe."""
import argparse
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import tempfile
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
    if os.name != 'posix' or args.evidence_dir.exists():
        parser.error('Requires Unix and a new evidence directory')
    dll = args.smoke_dll.resolve(strict=True)
    args.evidence_dir.mkdir(mode=0o700)
    scratch = pathlib.Path(tempfile.mkdtemp(prefix='filecat-process-', dir='/tmp'))
    (scratch / '.filecat-owned').write_text(uuid.uuid4().hex)
    short = scratch / 'short'; long = scratch / ('long-' + 'a' * 120); unicode = scratch / ('unicode-' + 'ž' * 60)
    for temp in [short, long, unicode]:
        temp.mkdir(mode=0o700)
    command = [shutil.which('dotnet') or 'dotnet', str(dll)]
    manifest = {'smoke_dll': str(dll), 'sha256': hashlib.sha256(dll.read_bytes()).hexdigest(),
                'app_sha256': hashlib.sha256((dll.parent / 'FileCat.dll').read_bytes()).hexdigest(),
                'scratch': str(scratch), 'results': []}
    cases = [('same-session-control', short, short, False, True),
             ('same-temp-separate-session', short, short, True, True),
             ('different-temp-separate-session', short, long, True, True),
             ('long-temp-separate-session', long, unicode, True, True),
             ('usual-instance-from-other-temp', short, long, True, False)]
    for label, server_temp, client_temp, separate, with_data in cases:
        root = scratch / label; root.mkdir(mode=0o700)
        (root / '.filecat-owned').write_text(uuid.uuid4().hex)
        profile = 'ipc-smoke-' + uuid.uuid4().hex[:24]
        options = ['--profile', profile] + (['--data', str(root / 'state')] if with_data else [])
        result = {'case': label, 'profile': profile, 'server_temp': str(server_temp),
                  'client_temp': str(client_temp), 'passed': False}
        env = {**os.environ, 'DOTNET_EnableDiagnostics': '0'}
        server_env = {**env, 'TMPDIR': str(server_temp)}; client_env = {**env, 'TMPDIR': str(client_temp)}
        server = None
        try:
            if not with_data:
                negative = subprocess.run(command + ['probe', str(root)] + options, env=client_env,
                                          capture_output=True, timeout=5, start_new_session=separate)
                (root / 'probe-before.log').write_bytes(negative.stdout + negative.stderr)
                if negative.returncode or json.loads(negative.stdout)['running']:
                    raise RuntimeError('Missing usual instance was not reported absent')
            with (root / 'server.log').open('wb') as output:
                server = subprocess.Popen(command + ['serve', str(root)] + options, env=server_env,
                                          stdout=output, stderr=subprocess.STDOUT, start_new_session=separate)
            ready = wait_file(root / 'ready.json', server)
            result['server_pid'] = ready['pid']; result['server_session'] = os.getsid(ready['pid'])
            # The production listener is started on its worker; give it a bounded opportunity to bind before forwarding.
            time.sleep(.1)
            if not with_data:
                before = sorted((str(p), p.stat().st_size, p.stat().st_mtime_ns)
                                for d in [ready['LocalDirectory'], ready['SettingsDirectory']]
                                for p in pathlib.Path(d).rglob('*') if p.is_file())
                probe = subprocess.run(command + ['probe', str(root)] + options, env=client_env,
                                       capture_output=True, timeout=5, start_new_session=separate)
                (root / 'probe-running.log').write_bytes(probe.stdout + probe.stderr)
                if probe.returncode or not json.loads(probe.stdout)['running']:
                    raise RuntimeError('Usual instance was missed from the other TMPDIR/session')
                after = sorted((str(p), p.stat().st_size, p.stat().st_mtime_ns)
                               for d in [ready['LocalDirectory'], ready['SettingsDirectory']]
                               for p in pathlib.Path(d).rglob('*') if p.is_file())
                if before != after:
                    raise RuntimeError('Usual-instance probe changed profile files')
                result['probe_profile_unchanged'] = True
            paths = [str(root / 'žluťoučký & 100%'), '--left', str(root / 'another folder')]
            client = subprocess.Popen(command + ['forward', str(root)] + options + paths, env=client_env,
                                      stdout=subprocess.PIPE, stderr=subprocess.STDOUT, start_new_session=separate)
            result['client_session'] = os.getsid(client.pid)
            try:
                output, _ = client.communicate(timeout=5)
            except subprocess.TimeoutExpired:
                client.kill(); client.communicate(); raise
            (root / 'forward.log').write_bytes(output)
            result['forwarder_exit'] = client.returncode
            if (separate and result['server_session'] == result['client_session']) or \
                    (not separate and result['server_session'] != result['client_session']):
                raise RuntimeError('Requested session isolation was not established')
            if client.returncode or not json.loads(output)['forwarded']:
                raise RuntimeError('Second process elected itself instead of forwarding')
            received = wait_file(root / 'received.json', server)
            expected = ['--left', paths[2], paths[0]]
            if received != expected:
                raise RuntimeError(f'Forwarded arguments changed: {received!r}')
            result['received'] = received
            if server.poll() is not None:
                raise RuntimeError('Original server stopped during forwarding')
            result['passed'] = True
        except Exception as error:
            result['error'] = str(error)
        finally:
            if server is not None:
                (root / 'stop').touch()
                try:
                    server.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    server.kill(); server.wait()
                result['server_exit'] = server.returncode
                if server.returncode:
                    result['passed'] = False
            if not with_data and (root / 'ready.json').exists():
                ready = json.loads((root / 'ready.json').read_text())
                for key in ['LocalDirectory', 'SettingsDirectory']:
                    shutil.copytree(ready[key], root / key, dirs_exist_ok=True)
        manifest['results'].append(result)
        print(json.dumps(result, ensure_ascii=False), flush=True)
    # Two different state folders with one profile must keep their own live endpoints, including on case-sensitive Unix.
    root = scratch / 'different-data-folders'; root.mkdir(mode=0o700)
    profile = 'ipc-smoke-' + uuid.uuid4().hex[:24]
    data_roots = [root / 'state', root / 'STATE']
    for data in data_roots:
        data.mkdir(exist_ok=True)
    same_folder = os.path.samefile(*data_roots)
    servers = []; result = {'case': 'different-data-folders', 'same_physical_folder': same_folder, 'passed': False}
    env = {**os.environ, 'TMPDIR': str(short), 'DOTNET_EnableDiagnostics': '0'}
    try:
        count = 1 if same_folder else 2
        for index in range(count):
            control = root / str(index); control.mkdir(mode=0o700)
            (control / '.filecat-owned').write_text(uuid.uuid4().hex)
            with (control / 'server.log').open('wb') as log:
                server = subprocess.Popen(command + ['serve', str(control), '--profile', profile, '--data', str(data_roots[index])],
                                          env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            servers.append((server, control)); wait_file(control / 'ready.json', server)
        time.sleep(.1)
        for index in range(2):
            server, control = servers[0 if same_folder else index]
            received = control / 'received.json'
            if received.exists():
                received.rename(control / f'received-{index - 1}.json')
            forwarded = [str(root / f'folder-{index}')]
            run = subprocess.run(command + ['forward', str(control), '--profile', profile, '--data', str(data_roots[index])] + forwarded,
                                 env=env, capture_output=True, timeout=5, start_new_session=True)
            (control / f'forward-{index}.log').write_bytes(run.stdout + run.stderr)
            if run.returncode or wait_file(received, server) != forwarded:
                raise RuntimeError('Data-folder launch reached the wrong instance')
        if any(server.poll() is not None for server, _ in servers):
            raise RuntimeError('One data-folder instance displaced the other')
        result['passed'] = True
    except Exception as error:
        result['error'] = str(error)
    finally:
        for server, control in servers:
            (control / 'stop').touch()
            try:
                server.wait(timeout=5)
            except subprocess.TimeoutExpired:
                server.kill(); server.wait()
            if server.returncode:
                result['passed'] = False
    manifest['results'].append(result)
    print(json.dumps(result, ensure_ascii=False), flush=True)
    (args.evidence_dir / 'results.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2))
    shutil.make_archive(str(args.evidence_dir / 'fixtures'), 'gztar', scratch.parent, scratch.name)
    return 0 if all(r['passed'] for r in manifest['results']) else 1


if __name__ == '__main__':
    raise SystemExit(main())
