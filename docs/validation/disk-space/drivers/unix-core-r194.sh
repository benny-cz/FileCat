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
[ "$actual" = 'e7f2dc029c451894cf5ecdbde8ae9a9851a6f99f56086fe749bea6c710f8e4e7' ] || exit 74
umask 077
mkdir -p core
tar -xzf tests.tgz -C core
cd core
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; then printf 'BUSY\n' > ../state.txt; exit 75; fi
set +e
"$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -class 'FileCat.Core.Tests.AnalysisPrivateRootExclusionTests' -noLogo -noColor -maxThreads 2 -xml ../core-r194.xml > ../core-r194.log 2>&1
result=$?
printf 'FINISHED exit=%s\n' "$result" > ../state.txt
cat ../core-r194.log
exit "$result"
