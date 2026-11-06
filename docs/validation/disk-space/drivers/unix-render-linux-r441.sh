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
[ "$actual" = 'ae8136be73511949788af73435177d9b68745cdb7e10153cefa998071bd77859' ] || exit 74
umask 077
mkdir -p render
tar -xzf tests.tgz -C render
cd render
set +e
"$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 1 -xml ../render-r441.xml > ../render-r441.log 2>&1
result=$?
printf 'FINISHED exit=%s\n' "$result" > ../state.txt
cat ../render-r441.log
exit "$result"
