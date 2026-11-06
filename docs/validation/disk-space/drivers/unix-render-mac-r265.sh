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
[ "$actual" = 'fb1345be3d09406c4f6ec987e730796a64714c323eb565918edb57be293ba337' ] || exit 74
umask 077
mkdir -p render
tar -xzf tests.tgz -C render
cd render
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; then printf 'BUSY\n' > ../state.txt; exit 75; fi
set +e
"$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -method '*HandledRowReleaseRetiresTheTableFreezeAfterItsCapturedAction*' -noLogo -noColor -maxThreads 1 -xml ../render-r265.xml > ../render-r265.log 2>&1
result=$?
printf 'FINISHED exit=%s\n' "$result" > ../state.txt
cat ../render-r265.log
exit "$result"
