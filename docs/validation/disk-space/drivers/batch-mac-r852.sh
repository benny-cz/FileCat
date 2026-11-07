set -eu
cd "$(dirname "$0")"
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; then printf 'BUSY phase=verify\n' > state.txt; exit 75; fi
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
if [ "$actual" != '460ba48cf08ce7e47e5f90e5060b8bbc16b217e574fd7906915532fe155f0746' ]; then printf 'FAILED phase=integrity\n' > state.txt; exit 74; fi
umask 077
mkdir driver
tar -xzf bundle.tgz -C driver
python3 driver/run.py