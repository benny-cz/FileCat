set -eu
cd "$(dirname "$0")"
base=$(pwd)
busy() { pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; }
if busy; then printf 'BUSY phase=verify\n' > state.txt; exit 75; fi
printf 'RUNNING phase=verify runnerPid=%s\n' "$$" > state.txt
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
[ "$actual" = '95329866ad7e2e286a1f1fd2a09d30a0678b1081d1ee4c5746a6396d6787eb0f' ] || exit 74
umask 077
mkdir patch-input
tar -xzf bundle.tgz -C patch-input
python3 patch-input/reuse.py '/Users/benny/FileCatValidation/dsa-batch-20261107-r1118/payload'
overall=0
for phase in smoke preview core render appsettings; do
 if busy; then printf 'BUSY phase=%s\n' "$phase" > "$base/state.txt"; exit 75; fi
 printf 'RUNNING phase=%s runnerPid=%s\n' "$phase" "$$" > "$base/state.txt"
 if [ "$phase" = core ] || [ "$phase" = smoke ]; then cd "$base/payload/tests/FileCat.Core.Tests/bin/Debug/net10.0"; else cd "$base/payload/render"; fi
 set +e
 if [ "$phase" = smoke ]; then
  "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -method '*IndexedComparisonPaths*' -noLogo -noColor -maxThreads 2 -xml "$base/smoke-r1121.xml" > "$base/smoke-r1121.log" 2>&1
 elif [ "$phase" = preview ]; then
  "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -method '*NativeIndexedComparisonPaths*' -noLogo -noColor -maxThreads 2 -xml "$base/preview-r1121.xml" > "$base/preview-r1121.log" 2>&1
 elif [ "$phase" = core ] || [ "$phase" = smoke ]; then
  "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -class 'FileCat.Core.Tests.Analysis*' -class '*CommandTests' -class '*DeviceIoSchedulerTests' -class '*ZipListingRevisionTests' -noLogo -noColor -maxThreads 2 -xml "$base/core-r1121.xml" > "$base/core-r1121.log" 2>&1
 elif [ "$phase" = appsettings ]; then
  "$HOME/.dotnet/dotnet" FileCat.App.Tests.dll -class '*SettingsDialogTests' -class '*SettingsPreservationTests' -noLogo -noColor -maxThreads 2 -xml "$base/appsettings-r1121.xml" > "$base/appsettings-r1121.log" 2>&1
 else
  "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 2 -xml "$base/render-r1121.xml" > "$base/render-r1121.log" 2>&1
 fi
 result=$?
 set -e
 if [ "$phase" = preview ]; then
  if [ "$result" != 0 ] || ! python3 -c 'import sys,xml.etree.ElementTree as E;a=E.parse(sys.argv[1]).getroot().find("assembly");assert a.get("total")==a.get("passed")=="1" and a.get("failed")==a.get("errors")=="0"' "$base/preview-r1121.xml"; then
   printf 'FINISHED exit=1 phase=preview\n' > "$base/state.txt"; tail -n 60 "$base/preview-r1121.log"; exit 1
  fi
 fi
 if [ "$phase" = smoke ] && { [ "$result" != 0 ] || ! python3 -c 'import sys,xml.etree.ElementTree as E;a=E.parse(sys.argv[1]).getroot().find("assembly");assert a.get("total")==a.get("passed")=="14" and a.get("failed")==a.get("errors")=="0"' "$base/smoke-r1121.xml"; }; then
  printf 'FINISHED exit=1 phase=smoke\n' > "$base/state.txt"; tail -n 60 "$base/smoke-r1121.log"; exit 1
 fi
 if [ "$result" != 0 ]; then overall=1; tail -n 45 "$base/$phase-r1121.log"; fi
done
printf 'FINISHED exit=%s\n' "$overall" > "$base/state.txt"
exit "$overall"
