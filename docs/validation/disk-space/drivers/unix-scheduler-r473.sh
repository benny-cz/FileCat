set -eu
cd "$(dirname "$0")"
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; then printf 'BUSY\n' > scheduler-state-r473.txt; exit 75; fi
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 core/FileCat.Core.Tests.dll | cut -d ' ' -f 1); else actual=$(sha256sum core/FileCat.Core.Tests.dll | cut -d ' ' -f 1); fi
[ "$actual" = 'f8fa73019ff14ad8d5a8b2b63cb36e4bf82326b5154d9210a9bb4a9d04c00797' ] || exit 74
cd core
if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; then printf 'BUSY\n' > ../scheduler-state-r473.txt; exit 75; fi
set +e
"$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -class '*DeviceIoSchedulerTests' -noLogo -noColor -maxThreads 1 -xml ../scheduler-r473.xml > ../scheduler-r473.log 2>&1
result=$?
printf 'FINISHED exit=%s\n' "$result" > ../scheduler-state-r473.txt
cat ../scheduler-r473.log
exit "$result"
