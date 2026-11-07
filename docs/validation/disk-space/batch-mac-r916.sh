set -eu
cd "$(dirname "$0")"
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; then printf 'BUSY phase=verify\n' > state.txt; exit 75; fi
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
if [ "$actual" != '88ce054d1c58c900023eb346d77d0133c72b4f92ac246f4df4b70d4d20c0f716' ]; then printf 'FAILED phase=integrity\n' > state.txt; exit 74; fi
umask 077
mkdir driver
tar -xzf bundle.tgz -C driver
python3 driver/run.py