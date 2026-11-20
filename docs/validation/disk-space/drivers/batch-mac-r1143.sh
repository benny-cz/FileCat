set -eu
cd "$(dirname "$0")"
base=$(pwd)
busy() { pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1; }
if busy; then printf 'BUSY phase=verify\n' > state.txt; exit 75; fi
printf 'RUNNING phase=verify runnerPid=%s\n' "$$" > state.txt
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
[ "$actual" = '66d202d3d38b5374336e9e422f5ec8ef391fcb10657808291f53f97aa722bc7e' ] || exit 74
umask 077
mkdir payload
tar -xzf bundle.tgz -C payload
overall=0
for phase in smoke preview core render appsettings; do
 if busy; then printf 'BUSY phase=%s\n' "$phase" > "$base/state.txt"; exit 75; fi
 printf 'RUNNING phase=%s runnerPid=%s\n' "$phase" "$$" > "$base/state.txt"
 if [ "$phase" = core ] || [ "$phase" = smoke ]; then cd "$base/payload/tests/FileCat.Core.Tests/bin/Debug/net10.0"; else cd "$base/payload/render"; fi
 set +e
 if [ "$phase" = smoke ]; then
  "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -method '*IndexedComparison*' -method '*ExactKeyCompaction*' -noLogo -noColor -maxThreads 2 -xml "$base/smoke-r1143.xml" > "$base/smoke-r1143.log" 2>&1
 elif [ "$phase" = preview ]; then
  "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -method '*NativeIndexedComparison*' -method '*NativeComparisonEntitiesLimitedBasis*' -noLogo -noColor -maxThreads 2 -xml "$base/preview-r1143.xml" > "$base/preview-r1143.log" 2>&1
 elif [ "$phase" = core ] || [ "$phase" = smoke ]; then
  "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -class 'FileCat.Core.Tests.Analysis*' -class '*CommandTests' -class '*DeviceIoSchedulerTests' -class '*ZipListingRevisionTests' -noLogo -noColor -maxThreads 2 -xml "$base/core-r1143.xml" > "$base/core-r1143.log" 2>&1
 elif [ "$phase" = appsettings ]; then
  "$HOME/.dotnet/dotnet" FileCat.App.Tests.dll -class '*SettingsDialogTests' -class '*SettingsPreservationTests' -noLogo -noColor -maxThreads 2 -xml "$base/appsettings-r1143.xml" > "$base/appsettings-r1143.log" 2>&1
 else
  "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 2 -xml "$base/render-r1143.xml" > "$base/render-r1143.log" 2>&1
 fi
 result=$?
 set -e
 if [ "$phase" = preview ]; then
  if [ "$result" != 0 ] || ! python3 -c 'import sys,xml.etree.ElementTree as E;a=E.parse(sys.argv[1]).getroot().find("assembly");assert a.get("total")==a.get("passed")=="29" and a.get("failed")==a.get("errors")=="0"' "$base/preview-r1143.xml"; then
   printf 'FINISHED exit=1 phase=preview\n' > "$base/state.txt"; tail -n 60 "$base/preview-r1143.log"; exit 1
  fi
 fi
 if [ "$phase" = smoke ] && { [ "$result" != 0 ] || ! python3 -c 'import sys,xml.etree.ElementTree as E;a=E.parse(sys.argv[1]).getroot().find("assembly");assert a.get("total")==a.get("passed")=="248" and a.get("failed")==a.get("errors")=="0"' "$base/smoke-r1143.xml"; }; then
  printf 'FINISHED exit=1 phase=smoke\n' > "$base/state.txt"; tail -n 60 "$base/smoke-r1143.log"; exit 1
 fi
 if [ "$result" != 0 ]; then overall=1; tail -n 45 "$base/$phase-r1143.log"; fi
done
printf 'FINISHED exit=%s\n' "$overall" > "$base/state.txt"
exit "$overall"
