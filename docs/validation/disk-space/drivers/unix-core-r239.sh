set -eu
cd "$(dirname "$0")"
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; then
  printf 'BUSY\n' > state.txt
  exit 75
fi
if [ "$(uname -s)" = Darwin ]; then
  actual=$(shasum -a 256 tests.tgz | cut -d ' ' -f 1)
else
  actual=$(sha256sum tests.tgz | cut -d ' ' -f 1)
fi
[ "$actual" = 'd9529e9302c31073844953735228bf17080d7d294ab68ecbd4853220e446025b' ] || exit 74
umask 077
mkdir -p core
tar -xzf tests.tgz -C core
cd core
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; then printf 'BUSY\n' > ../state.txt; exit 75; fi
set +e
"$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -class 'FileCat.Core.Tests.Analysis*' -class '*CommandTests' -noLogo -noColor -maxThreads 2 -xml ../core-r239.xml > ../core-r239.log 2>&1
result=$?
printf 'FINISHED exit=%s\n' "$result" > ../state.txt
cat ../core-r239.log
exit "$result"
