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
[ "$actual" = '413d2c61a6d761810f5b56c813dd4d18567a2fbbbde46e3e85df300f05571fe2' ] || exit 74
umask 077
mkdir -p render
tar -xzf tests.tgz -C render
cd render
set +e
"$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -method '*IndexedQueryPublicationWaitsForTheCapturedChartGestureToEnd*' -noLogo -noColor -maxThreads 1 -xml ../render-r293.xml > ../render-r293.log 2>&1
result=$?
printf 'FINISHED exit=%s\n' "$result" > ../state.txt
cat ../render-r293.log
exit "$result"
