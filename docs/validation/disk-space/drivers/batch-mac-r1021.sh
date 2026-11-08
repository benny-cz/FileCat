set -eu
cd "$(dirname "$0")"
base=$(pwd)
busy() {
  pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1
}
if busy; then printf 'BUSY phase=verify\n' > state.txt; exit 75; fi
printf 'RUNNING phase=verify runnerPid=%s\n' "$$" > state.txt
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
if [ "$actual" != 'ad8c521dfb307f3425807dd58e7e12328538cc12288e1030023e3220605cad52' ]; then printf 'FAILED phase=integrity\n' > state.txt; exit 74; fi
umask 077
mkdir patch-input
tar -xzf bundle.tgz -C patch-input
python3 patch-input/reuse.py '/Users/benny/FileCatValidation/dsa-batch-20261107-r1015/payload'
overall=0
for check in smoke preview; do
  if busy; then printf 'BUSY phase=%s\n' "$check" > "$base/state.txt"; exit 75; fi
  printf 'RUNNING phase=%s runnerPid=%s\n' "$check" "$$" > "$base/state.txt"
  set +e
  if [ "$check" = smoke ]; then
    cd "$base/payload/core"
    "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -method '*AnalysisHistoryTests.*' -method '*AnalysisCatalogTests.CatalogMemoryRemainsReservedUntilResultOwnersClose' -noLogo -noColor -maxThreads 2 -xml "$base/smoke-r1021.xml" > "$base/smoke-r1021.log" 2>&1
    code=$?; expected=27
  else
    cd "$base/payload/render"
    "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -method '*AnalysisRenderingTests.NativeRolling*' -method '*AnalysisRenderingTests.NativeNamedOwnerCannotBecomeAutomaticHistoryAndNonHistoryFilesArePreserved' -method '*AnalysisRenderingTests.RollingHistoryPolicyReviewPreservesTheCurrentChoiceAndNamesOnlyItsPrivateScope' -method '*AnalysisRenderingTests.ActualDiagnosticsZipIncludesOnlyFiniteAnalysisPreferencesAndPreservesPrivateSentinels' -method '*AnalysisRenderingTests.NativeHistoryCompletion*' -method '*AnalysisRenderingTests.NativeHistoryOff*' -method '*AnalysisRenderingTests.StoppedAndUnconfirmedSourceCloseSessions*' -method '*AnalysisRenderingTests.SettingsHistoryPolicyRequires*' -method '*AnalysisRenderingTests.HistorySettingsKeepExact*' -method '*AnalysisRenderingTests.HistoryPolicyChangedDuringReview*' -noLogo -noColor -maxThreads 2 -xml "$base/preview-r1021.xml" > "$base/preview-r1021.log" 2>&1
    code=$?; expected=26
  fi
  set -e
  if [ "$code" -ne 0 ]; then printf 'FINISHED exit=1 phase=%s\n' "$check" > "$base/state.txt"; exit 1; fi
  if ! python3 -c 'import sys,xml.etree.ElementTree as E;a=E.parse(sys.argv[1]).getroot().find("assembly");n=int(sys.argv[2]);assert int(a.get("total"))==int(a.get("passed"))==n and a.get("failed")==a.get("errors")=="0"' "$base/$check-r1021.xml" "$expected"; then
    printf 'FINISHED exit=1 phase=%s\n' "$check" > "$base/state.txt"; exit 1
  fi
done
for phase in core render appsettings; do
  if busy; then printf 'BUSY phase=%s\n' "$phase" > "$base/state.txt"; exit 75; fi
  printf 'RUNNING phase=%s runnerPid=%s\n' "$phase" "$$" > "$base/state.txt"
  if [ "$phase" = appsettings ]; then cd "$base/payload/render"; else cd "$base/payload/$phase"; fi
  set +e
  if [ "$phase" = core ]; then
    "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -class 'FileCat.Core.Tests.Analysis*' -class '*CommandTests' -class '*DeviceIoSchedulerTests' -noLogo -noColor -maxThreads 2 -xml "$base/core-r1021.xml" > "$base/core-r1021.log" 2>&1
  elif [ "$phase" = appsettings ]; then
    cd "$base/payload/render"
    "$HOME/.dotnet/dotnet" FileCat.App.Tests.dll -class '*SettingsDialogTests' -class '*SettingsPreservationTests' -noLogo -noColor -maxThreads 2 -xml "$base/appsettings-r1021.xml" > "$base/appsettings-r1021.log" 2>&1
  else
    "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 2 -xml "$base/render-r1021.xml" > "$base/render-r1021.log" 2>&1
  fi
  result=$?
  set -e
  cd "$base"
  if [ "$result" != 0 ]; then tail -n 45 "$phase-r1021.log"; overall=1; fi
done
printf 'FINISHED exit=%s\n' "$overall" > state.txt
printf 'Core and rendering completed; overall exit=%s.\n' "$overall"
exit "$overall"
