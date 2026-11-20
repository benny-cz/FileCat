set -eu
cd "$(dirname "$0")"
base=$(pwd)
busy() { pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; }
if busy; then printf 'BUSY phase=verify\n' > state.txt; exit 75; fi
printf 'RUNNING phase=verify runnerPid=%s\n' "$$" > state.txt
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
[ "$actual" = 'd150e55c5d5d62edaa14273a2557c0d53bb3db0e8b5e5976bfaff6cf8b602f31' ] || exit 74
umask 077
mkdir payload
tar -xzf bundle.tgz -C payload
if busy; then printf 'BUSY phase=core\n' > state.txt; exit 75; fi
printf 'RUNNING phase=core runnerPid=%s\n' "$$" > state.txt
cd payload/tests/FileCat.Core.Tests/bin/Debug/net10.0
set +e
"$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -class 'FileCat.Core.Tests.ArchivePasswordCommandTests' -noLogo -noColor -maxThreads 2 -xml "$base/core-r1106.xml" > "$base/core-r1106.log" 2>&1
code=$?
set -e
printf 'FINISHED exit=%s\n' "$code" > "$base/state.txt"
tail -n 8 "$base/core-r1106.log"
exit "$code"