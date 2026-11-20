import hashlib, json, os, pathlib, re, selectors, subprocess, time

base = pathlib.Path.home() / 'FileCatValidation' / 'dsa-20261106-r382-ipc'
expected = 'ad43f7e531f72430e1916f8779489501d95dafe6254bfc3f85b243f62ca999a4'
capacity = 8 * 1024**3
owned = []
known_pids = set()
result = {'purpose': 'Actual Ubuntu IPC namespace quota counterexample and private-file coordination; no UI/resource/package qualification', 'passed': False, 'events': []}

def observed():
    apps = subprocess.run(['pgrep', '-x', 'FileCat([.].*)?'], capture_output=True, text=True).stdout.split()
    tests = subprocess.run(['pgrep', '-f', r'^[^ ]*dotnet +.*FileCat'], capture_output=True, text=True).stdout.split()
    return [int(pid) for pid in apps + tests]

def gate():
    foreign = [pid for pid in observed() if pid not in known_pids]
    if foreign: raise RuntimeError('BUSY: preserve other testers and stop only owned children')

gate()
assert hashlib.sha256((base / 'tests.tgz').read_bytes()).hexdigest() == expected
app = base / 'app'; app.mkdir(mode=0o700)
subprocess.run(['tar', '-xzf', str(base / 'tests.tgz'), '-C', str(app)], check=True)
profile_parent = base / 'profile'; profile_parent.mkdir(mode=0o700)
canary = profile_parent / 'neighbor.txt'; canary.write_text('untouched neighbor\n')
dotnet = str(pathlib.Path.home() / '.dotnet' / 'dotnet')
harness = str(app / 'FileCat.CoreCrashHarness.dll')
initialized = subprocess.run([dotnet, harness, '--analysis-file-quota-init', str(profile_parent)], capture_output=True, text=True, check=True, timeout=30)
profile = initialized.stdout.strip(); assert re.fullmatch(r'[0-9a-f]{32}', profile)
state = profile_parent / 'capacity' / 'private-state.json'
state_hash = hashlib.sha256(state.read_bytes()).hexdigest()

class Child:
    def __init__(self, label, file_mode, namespace, amount):
        gate()
        args = [dotnet, harness, '--analysis-file-quota-hold' if file_mode else '--analysis-quota-hold', profile, str(amount), 'same']
        if file_mode: args.append(str(profile_parent))
        self.namespace = namespace
        if namespace:
            # Metadata precedes exec, which preserves this PID. The actual FileCat helper runs
            # only after setpriv has dropped every UID/GID/capability to the ordinary account.
            script = 'printf \'{"pid":%s,"ipc":"%s"}\\n\' "$$" "$(/usr/bin/readlink /proc/self/ns/ipc)"; exec "$@"'
            args = ['/usr/bin/sudo', '-k', '-S', '-p', '', '--', '/usr/bin/unshare', '--ipc', '--',
                    '/usr/bin/setpriv', '--reuid=' + str(os.getuid()), '--regid=' + str(os.getgid()), '--clear-groups',
                    '--bounding-set=-all', '--inh-caps=-all', '--ambient-caps=-all', '--',
                    '/bin/sh', '-c', script, 'filecat-quota-namespace'] + args
        self.error = (base / (label + '.err')).open('wb')
        env = dict(os.environ); env.pop('FILECAT_TEST_SUDO_PASSWORD', None)
        self.process = subprocess.Popen(args, cwd=app, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=self.error, bufsize=0, start_new_session=True)
        owned.append(self); self.pending = b''; self.pid = None
        if namespace:
            self.send(os.environ['FILECAT_TEST_SUDO_PASSWORD'])
            self.info = json.loads(self.line())
            assert isinstance(self.info['pid'], int) and self.info['pid'] > 1
            assert re.fullmatch(r'ipc:\[[0-9]+\]', self.info['ipc'])
            self.pid = self.info['pid']; known_pids.add(self.pid)
        else:
            self.pid = self.process.pid; known_pids.add(self.pid)
            self.info = None
        gate()
    def send(self, command):
        self.process.stdin.write((command + '\n').encode()); self.process.stdin.flush()
    def line(self, seconds=25):
        until = time.monotonic() + seconds
        with selectors.DefaultSelector() as selector:
            selector.register(self.process.stdout, selectors.EVENT_READ)
            while b'\n' not in self.pending:
                remaining = until - time.monotonic()
                if remaining <= 0 or not selector.select(remaining): raise TimeoutError('Owned child output deadline')
                data = os.read(self.process.stdout.fileno(), 4096)
                if not data:
                    if self.pending: break
                    return None
                self.pending += data
        first, separator, rest = self.pending.partition(b'\n'); self.pending = rest
        return first.decode()
    def ready(self):
        assert self.line() == 'QUOTA_READY', 'Owned ordinary helper failed to acquire capacity'
        if self.info is None:
            self.send('INFO'); self.info = json.loads(self.line()); assert self.info['pid'] == self.pid
        return self.info
    def stop(self, crash=False):
        if self.process.poll() is None:
            if crash:
                assert self.pid is not None and self.pid in known_pids
                os.kill(self.pid, 9)
            else: self.send('EXIT')
        code = self.process.wait(timeout=20); self.error.close(); return code

try:
    legacy = Child('legacy-parent', False, False, capacity); parent_info = legacy.ready()
    split = Child('legacy-separate-ipc', False, True, 1); split_info = split.ready()
    assert split_info['ipc'] != parent_info['ipc']
    result['events'].append({'case': 'rejected-IPC-only-boundary', 'parent': parent_info, 'child': split_info, 'parentBytes': capacity, 'additionalChildBytes': 1, 'secondCeilingObserved': True})
    assert split.stop() == 0; assert legacy.stop() == 0; gate()

    protected = Child('files-parent', True, False, capacity); protected_info = protected.ready()
    token_files = sorted((profile_parent / 'capacity').glob('capacity-*.state')); assert len(token_files) == 32
    hashes = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in token_files}
    denied = Child('files-separate-ipc-refused', True, True, 1)
    assert denied.info['ipc'] != protected_info['ipc']
    assert denied.line() is None; assert denied.stop() == 73
    protected.send('PING'); assert protected.line() == 'QUOTA_HELD'
    result['events'].append({'case': 'shared-file-boundary', 'parent': protected_info, 'child': denied.info, 'refusedExitCode': 73, 'parentStillHoldsFullCapacity': True})
    assert {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in token_files} == hashes
    assert protected.stop(crash=True) != 0; gate()
    recovered = Child('files-new-ipc-after-crash', True, True, capacity); recovered_info = recovered.ready()
    result['events'].append({'case': 'actual-crash-release', 'child': recovered_info, 'reservedBytes': capacity})
    assert recovered.stop() == 0; gate()
    assert hashlib.sha256(state.read_bytes()).hexdigest() == state_hash
    assert {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in token_files} == hashes
    assert canary.read_text() == 'untouched neighbor\n'
    result['profileStateUnchanged'] = True; result['immutableTokensUnchanged'] = True; result['neighborUnchanged'] = True
    result['passed'] = True
except BaseException as error:
    result['error'] = type(error).__name__ + ': ' + str(error)
finally:
    exits = []
    for child in reversed(owned):
        try:
            if child.process.poll() is None: child.stop()
            exits.append({'pid': child.pid, 'wrapperPid': child.process.pid, 'exitCode': child.process.returncode})
        except BaseException as error:
            result['passed'] = False; result.setdefault('cleanupErrors', []).append(type(error).__name__ + ': ' + str(error))
    result['ownedExits'] = exits; result['finalProcesses'] = observed()
    if result['finalProcesses']: result['passed'] = False
    result['binaries'] = {name: hashlib.sha256((app / name).read_bytes()).hexdigest() for name in ['FileCat.Core.dll', 'FileCat.CoreCrashHarness.dll']}
    (base / 'result.json').write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps(result))
raise SystemExit(0 if result['passed'] else 1)
