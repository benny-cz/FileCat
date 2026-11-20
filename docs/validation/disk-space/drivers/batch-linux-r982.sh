set -eu
cd "$(dirname "$0")"
base=$(pwd)
busy() {
  pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1
}
if busy; then printf 'BUSY phase=verify\n' > state.txt; exit 75; fi
printf 'RUNNING phase=verify runnerPid=%s\n' "$$" > state.txt
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
if [ "$actual" != 'b063f0832f9a11b592301341272110aa667ab8e2c63c84fc8488f4cf13fc498b' ]; then printf 'FAILED phase=integrity\n' > state.txt; exit 74; fi
umask 077
mkdir payload
tar -xzf bundle.tgz -C payload
overall=0
for check in smoke preview; do
  if busy; then printf 'BUSY phase=%s\n' "$check" > "$base/state.txt"; exit 75; fi
  printf 'RUNNING phase=%s runnerPid=%s\n' "$check" "$$" > "$base/state.txt"
  set +e
  if [ "$check" = smoke ]; then
    cd "$base/payload/core"
    "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -method 'FileCat.Core.Tests.AnalysisHistoryTests.*' -noLogo -noColor -maxThreads 2 -xml "$base/smoke-r982.xml" > "$base/smoke-r982.log" 2>&1
    code=$?; expected=25
  else
    cd "$base/payload/render"
    "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -method 'FileCat.Rendering.Tests.AnalysisRenderingTests.ActualDiagnosticsZipIncludesOnlyFiniteAnalysisPreferencesAndPreservesPrivateSentinels' -noLogo -noColor -maxThreads 2 -xml "$base/preview-r982.xml" > "$base/preview-r982.log" 2>&1
    code=$?; expected=1
  fi
  set -e
  if [ "$code" -ne 0 ]; then printf 'FINISHED exit=1 phase=%s\n' "$check" > "$base/state.txt"; exit 1; fi
  if ! python3 -c 'import sys,xml.etree.ElementTree as E;a=E.parse(sys.argv[1]).getroot().find("assembly");n=int(sys.argv[2]);assert int(a.get("total"))==int(a.get("passed"))==n and a.get("failed")==a.get("errors")=="0"' "$base/$check-r982.xml" "$expected"; then
    printf 'FINISHED exit=1 phase=%s\n' "$check" > "$base/state.txt"; exit 1
  fi
done
for phase in core render; do
  if busy; then printf 'BUSY phase=%s\n' "$phase" > "$base/state.txt"; exit 75; fi
  printf 'RUNNING phase=%s runnerPid=%s\n' "$phase" "$$" > "$base/state.txt"
  cd "$base/payload/$phase"
  set +e
  if [ "$phase" = core ]; then
    "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -class 'FileCat.Core.Tests.Analysis*' -class '*CommandTests' -class '*DeviceIoSchedulerTests' -noLogo -noColor -maxThreads 2 -xml "$base/core-r982.xml" > "$base/core-r982.log" 2>&1
  else
    "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 2 -xml "$base/render-r982.xml" > "$base/render-r982.log" 2>&1
  fi
  result=$?
  set -e
  cd "$base"
  if [ "$result" != 0 ]; then tail -n 45 "$phase-r982.log"; overall=1; fi
done
printf 'FINISHED exit=%s\n' "$overall" > state.txt
printf 'Core and rendering completed; overall exit=%s.\n' "$overall"
exit "$overall"
