"""Exercise the actual dependency launcher with owned commands; no package manager or privilege changes."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--bash', default=shutil.which('bash'))
    parser.add_argument('--evidence-dir', type=Path, required=True)
    args = parser.parse_args()
    assert args.bash, 'A Bash runtime is required'
    script = Path(__file__).with_name('prepare-linux-ci-dependencies.sh').resolve()
    args.evidence_dir.mkdir(parents=True, exist_ok=False)

    def posix(path):
        if os.name != 'nt':
            return str(path)
        return subprocess.check_output([args.bash, '--noprofile', '--norc', '-c', 'cygpath -u "$1"', 'path', str(path)], text=True).strip()

    with tempfile.TemporaryDirectory(prefix='filecat-owned-ci-dependencies-') as folder:
        root = Path(folder)
        common = 'printf "%s\\0" "$0" "$@" >> "$FC_OWNED_LOG"; printf "\\n" >> "$FC_OWNED_LOG"\n'
        scripts = {
            'sudo': common + '[[ $1 == -n ]]; shift; exec "$@"\n',
            'timeout': common + '[[ $1 == --signal=TERM && $2 == --kill-after=15s && ( $3 == 120s || $3 == 300s ) ]]; shift 3\nexec /usr/bin/timeout --signal=TERM --kill-after=0.2s 0.4s "$@"\n',
            'apt-get': common + '''
operation=install
for arg in "$@"; do if [[ $arg == update ]]; then operation=update; fi; done
if [[ $FC_OWNED_CASE == update-retry && $operation == update && ! -f $FC_OWNED_MARK ]]; then touch "$FC_OWNED_MARK"; exit 42; fi
if [[ $FC_OWNED_CASE == update-fail && $operation == update || $FC_OWNED_CASE == install-fail && $operation == install ]]; then exit 42; fi
if [[ $FC_OWNED_CASE == update-hang && $operation == update ]]; then exec sleep 3; fi
''',
        }
        for name, body in scripts.items():
            p = root / name
            p.write_text('#!/usr/bin/env bash\nset -euo pipefail\n' + body, encoding='utf-8', newline='\n')
            p.chmod(0o700)
        controls = []
        for case, expected, updates, installs in [('responsive', 0, 1, 1), ('update-retry', 0, 2, 1), ('update-fail', 42, 2, 0), ('install-fail', 42, 1, 1), ('update-hang', 124, 2, 0)]:
            log = root / (case + '.calls')
            env = os.environ.copy()
            env.update(FC_OWNED_LOG=posix(log), FC_OWNED_MARK=posix(root / (case + '.mark')), FC_OWNED_CASE=case)
            before = time.monotonic()
            # Positional arguments carry paths. The launcher runs unchanged; only the owned PATH supplies commands.
            command = [args.bash, '--noprofile', '--norc', '-c', 'export PATH="$1:$PATH"; exec bash "$2"', 'control', posix(root), posix(script)]
            result = subprocess.run(command, env=env, capture_output=True, timeout=15)
            raw = log.read_bytes()
            calls = [line.rstrip(b'\0').decode('utf-8').split('\0') for line in raw.splitlines()]
            apt = [row[1:] for row in calls if row[0].endswith('/apt-get')]
            assert result.returncode == expected, (case, result.returncode, result.stderr)
            assert sum('update' in row for row in apt) == updates
            assert sum('install' in row for row in apt) == installs
            assert all('Acquire::ForceIPv4=true' in row and 'Acquire::Retries=2' in row and 'DPkg::Lock::Timeout=30' in row for row in apt)
            durations = [row[3] for row in calls if row[0].endswith('/timeout')]
            assert durations == ['120s'] * updates + ['300s'] * installs
            if installs:
                install = next(row for row in apt if 'install' in row)
                assert install[install.index('--no-install-recommends') + 1:] == ['gnome-keyring', 'dbus', 'libsecret-1-0', 'samba', 'smbclient', 'gvfs-backends', 'gvfs-fuse', 'dosfstools', 'exfatprogs', 'exfat-fuse', 'openssh-server', 'libwebkit2gtk-4.1-0', 'xvfb', 'python3-gi', 'desktop-file-utils']
            (args.evidence_dir / (case + '.stdout')).write_bytes(result.stdout)
            (args.evidence_dir / (case + '.stderr')).write_bytes(result.stderr)
            (args.evidence_dir / (case + '.calls')).write_bytes(raw)
            controls.append(dict(Case=case, ExitCode=result.returncode, Seconds=time.monotonic() - before, Arguments=command, Calls=calls))
    assert not root.exists()
    proof = dict(ActualLauncherExercised=True, OwnedCommandsOnly=True, RealTimeoutControl=True, OwnedTemporaryDirectoryRemoved=True, NativePackageInstallQualified=False, Controls=controls)
    (args.evidence_dir / 'controls.json').write_text(json.dumps(proof, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(Passed=len(controls), NativePackageInstallQualified=False)))


if __name__ == '__main__':
    main()
