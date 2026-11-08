set -eu
cd "$(dirname "$0")"
base=$(pwd)
busy() {
  pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1
}
if busy; then printf 'BUSY phase=verify\n' > state.txt; exit 75; fi
printf 'RUNNING phase=verify runnerPid=%s\n' "$$" > state.txt
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
if [ "$actual" != '71345725c63893f0b5feebce587a20a9545b1286140e5ab1dae5143f7aa4905b' ]; then printf 'FAILED phase=integrity\n' > state.txt; exit 74; fi
umask 077
mkdir patch-input
tar -xzf bundle.tgz -C patch-input
python3 patch-input/reuse.py '/home/benny/FileCatValidation/dsa-batch-20261107-r1063/payload'
overall=0
for check in smoke preview; do
  if busy; then printf 'BUSY phase=%s\n' "$check" > "$base/state.txt"; exit 75; fi
  printf 'RUNNING phase=%s runnerPid=%s\n' "$check" "$$" > "$base/state.txt"
  set +e
  if [ "$check" = smoke ]; then
    cd "$base/payload/core"
    "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -method '*AnalysisCatalogTests.CatalogMemoryRemainsReservedUntilResultOwnersClose' -noLogo -noColor -maxThreads 2 -xml "$base/smoke-r1068.xml" > "$base/smoke-r1068.log" 2>&1
    code=$?; expected=1
  else
    cd "$base/payload/render"
    "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -method '*AnalysisRenderingTests.AbruptOwnedInventoryDeathPreservesNamedBytesAndAllowsFreshExplicitRecovery' -noLogo -noColor -maxThreads 2 -xml "$base/preview-r1068.xml" > "$base/preview-r1068.log" 2>&1
    code=$?; expected=25
  fi
  set -e
  if [ "$code" -ne 0 ]; then printf 'FINISHED exit=1 phase=%s\n' "$check" > "$base/state.txt"; exit 1; fi
  if ! python3 -c 'import sys,xml.etree.ElementTree as E;a=E.parse(sys.argv[1]).getroot().find("assembly");n=int(sys.argv[2]);assert int(a.get("total"))==int(a.get("passed"))==n and a.get("failed")==a.get("errors")=="0"' "$base/$check-r1068.xml" "$expected"; then
    printf 'FINISHED exit=1 phase=%s\n' "$check" > "$base/state.txt"; exit 1
  fi
done
printf 'FINISHED exit=0\n' > "$base/state.txt"
exit 0

for phase in core render; do
  if busy; then printf 'BUSY phase=%s\n' "$phase" > "$base/state.txt"; exit 75; fi
  printf 'RUNNING phase=%s runnerPid=%s\n' "$phase" "$$" > "$base/state.txt"
  cd "$base/payload/$phase"
  set +e
  if [ "$phase" = core ]; then
    "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -class 'FileCat.Core.Tests.Analysis*' -class '*CommandTests' -class '*DeviceIoSchedulerTests' -noLogo -noColor -maxThreads 2 -xml "$base/core-r1068.xml" > "$base/core-r1068.log" 2>&1
  else
    "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 2 -xml "$base/render-r1068.xml" > "$base/render-r1068.log" 2>&1
  fi
  result=$?
  set -e
  cd "$base"
  if [ "$result" != 0 ]; then tail -n 45 "$phase-r1068.log"; overall=1; fi
done
printf 'FINISHED exit=%s\n' "$overall" > state.txt
printf 'Core and rendering completed; overall exit=%s.\n' "$overall"
exit "$overall"
