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
[ "$actual" = '4efd5f0125f9e53a2dd509d2d743d7463cdb2615a2f6a1b82884554c7d2bbe9f' ] || exit 74
umask 077
mkdir -p core
tar -xzf tests.tgz -C core
cd core
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; then printf 'BUSY\n' > ../state.txt; exit 75; fi
set +e
"$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -class 'FileCat.Core.Tests.Analysis*' -class '*CommandTests' -noLogo -noColor -maxThreads 2 -xml ../core-r248.xml > ../core-r248.log 2>&1
result=$?
printf 'FINISHED exit=%s\n' "$result" > ../state.txt
cat ../core-r248.log
exit "$result"
