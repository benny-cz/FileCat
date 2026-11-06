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
[ "$actual" = '6faf7439ed2e5ad9520ded4728ac6b86cbe5085db0ddc44b2760de9b973e8f3c' ] || exit 74
umask 077
mkdir -p render
tar -xzf tests.tgz -C render
cd render
set +e
"$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 1 -xml ../render-r432.xml > ../render-r432.log 2>&1
result=$?
printf 'FINISHED exit=%s\n' "$result" > ../state.txt
cat ../render-r432.log
exit "$result"
