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
[ "$actual" = '3ea6980c210d86a80f72db2c1f3d50198a66501714c04bd3b05e0df6d317c4fb' ] || exit 74
umask 077
mkdir -p render
tar -xzf tests.tgz -C render
cd render
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; then printf 'BUSY\n' > ../state.txt; exit 75; fi
set +e
"$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 1 -xml ../render-r501.xml > ../render-r501.log 2>&1
result=$?
printf 'FINISHED exit=%s\n' "$result" > ../state.txt
cat ../render-r501.log
exit "$result"
