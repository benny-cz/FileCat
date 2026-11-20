set -eu
cd "$(dirname "$0")"
base=$(pwd)
busy() { pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -x dotnet >/dev/null 2>&1; }
await_idle() {
 phase=$1; idle=0; attempt=0
 while [ "$attempt" -le 5 ]; do
  if busy; then idle=0; else idle=$((idle+1)); fi
  if [ "$idle" -ge 3 ]; then return 0; fi
  attempt=$((attempt+1)); if [ "$attempt" -le 5 ]; then sleep 1; fi
 done
 { date -u; for name in 'FileCat([.].*)?' dotnet; do
   pids=$(pgrep -x "$name" || true)
   for pid in $pids; do ps -p "$pid" -o pid=,ppid=,stat=,args= || true; done
  done; } > "$base/busy-@REV@-$phase.txt"
 printf 'BUSY phase=%s\n' "$phase" > "$base/state.txt"
 exit 75
}
await_idle verify
printf 'RUNNING phase=verify runnerPid=%s\n' "$$" > state.txt
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
[ "$actual" = '@HASH@' ] || exit 74
umask 077
mkdir payload
tar -xzf bundle.tgz -C payload
export FILECAT_COLUMN_BASELINE_OUT="$base/column-baseline-@REV@.json"
export FILECAT_OWNER_PERFORMANCE_OUT="$base/owner-performance-@REV@.json"
export FILECAT_OWNER_ANALYSIS_OUT="$base/ownership-analysis-@REV@.json"
overall=0
for phase in smoke preview core metacore render ownerrender appsettings ownerapp; do
 await_idle "$phase"
 printf 'RUNNING phase=%s runnerPid=%s\n' "$phase" "$$" > "$base/state.txt"
 case "$phase" in smoke|core|metacore) cd "$base/payload/@CORE@";; *) cd "$base/payload/render";; esac
 set +e
 case "$phase" in
  smoke) "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -method '*IndexedComparisonLocate*' -noLogo -noColor -maxThreads 2 -xml "$base/$phase-@REV@.xml" > "$base/$phase-@REV@.log" 2>&1;;
  preview) "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -method '*NativeIndexedComparisonMaintenance*' -noLogo -noColor -maxThreads 2 -xml "$base/$phase-@REV@.xml" > "$base/$phase-@REV@.log" 2>&1;;
  core) "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -class 'FileCat.Core.Tests.Analysis*' -class '*CommandTests' -class '*DeviceIoSchedulerTests' -class '*ZipListingRevisionTests' -noLogo -noColor -maxThreads 2 -xml "$base/$phase-@REV@.xml" > "$base/$phase-@REV@.log" 2>&1;;
  metacore) "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -class '*Metadata*Tests' -class '*OwnershipFieldTests' -class '*PrincipalNamesTests' -class '*UnixOwnershipTests' -class '*UnixPermissionTests' -noLogo -noColor -maxThreads 1 -xml "$base/$phase-@REV@.xml" > "$base/$phase-@REV@.log" 2>&1;;
  render) "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 2 -xml "$base/$phase-@REV@.xml" > "$base/$phase-@REV@.log" 2>&1;;
  ownerrender) "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class '*ColumnOwnershipBaselineTests' -class '*OwnerColumnPerformanceTests' -class '*OwnerColumnWidthTests' -class '*OwnershipAnalysisPerformanceTests' -noLogo -noColor -maxThreads 1 -xml "$base/$phase-@REV@.xml" > "$base/$phase-@REV@.log" 2>&1;;
  appsettings) "$HOME/.dotnet/dotnet" FileCat.App.Tests.dll -class '*SettingsDialogTests' -class '*SettingsPreservationTests' -noLogo -noColor -maxThreads 2 -xml "$base/$phase-@REV@.xml" > "$base/$phase-@REV@.log" 2>&1;;
  ownerapp) "$HOME/.dotnet/dotnet" FileCat.App.Tests.dll -class '*OwnerColumn*Tests' -class '*AnalysisCurrentDemandTests' -class '*ClippedCellInspectionTests' -class '*MetadataOutcomeUiTests' -noLogo -noColor -maxThreads 1 -xml "$base/$phase-@REV@.xml" > "$base/$phase-@REV@.log" 2>&1;;
 esac
 result=$?
 set -e
 if [ "$phase" = smoke ] || [ "$phase" = preview ]; then
  count=17; if [ "$phase" = preview ]; then count=5; fi
  if [ "$result" != 0 ] || ! python3 -c 'import sys,xml.etree.ElementTree as E;a=E.parse(sys.argv[1]).getroot().find("assembly");assert a.get("total")==a.get("passed")==sys.argv[2] and a.get("failed")==a.get("errors")=="0"' "$base/$phase-@REV@.xml" "$count"; then
   printf 'FINISHED exit=1 phase=%s\n' "$phase" > "$base/state.txt"; tail -n 45 "$base/$phase-@REV@.log"; exit 1
  fi
 fi
 if [ "$result" != 0 ]; then overall=1; tail -n 45 "$base/$phase-@REV@.log"; fi
done
printf 'FINISHED exit=%s\n' "$overall" > "$base/state.txt"
exit "$overall"
