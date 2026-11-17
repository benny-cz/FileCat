set -eu
cd "$(dirname "$0")"
base=$(pwd)
busy() { pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -x dotnet >/dev/null 2>&1; }
idle() {
 phase=$1; clear=0; attempt=0
 while [ "$attempt" -le 5 ]; do
  if busy; then clear=0; else clear=$((clear+1)); fi
  if [ "$clear" -ge 3 ]; then return; fi
  attempt=$((attempt+1)); if [ "$attempt" -le 5 ]; then sleep 1; fi
 done
 { date -u; for name in 'FileCat([.].*)?' dotnet; do
   ids=$(pgrep -x "$name" || true); for pid in $ids; do ps -p "$pid" -o pid=,ppid=,stat=,args= || true; done
  done; } > "$base/busy-$phase.txt"
 printf 'BUSY phase=%s\n' "$phase" > "$base/state.txt"; exit 75
}
idle verify
printf 'RUNNING phase=verify runnerPid=%s\n' "$$" > state.txt
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
[ "$actual" = '8a19a2b5dd0495b34f53fa4f14c9a5f69d590313db81193b3f9116a87cd549ac' ]
umask 077
mkdir payload
tar -xzf bundle.tgz -C payload
export FILECAT_TEST_MAC_RUSAGE_CONTROL="$base/payload/native-counter-layout.json"
for phase in counter negative small-wide small-deep huge-wide huge-deep; do
 if [ "$phase" = negative ] && [ "$(uname -s)" != Darwin ]; then continue; fi
 idle "$phase"
 printf 'RUNNING phase=%s runnerPid=%s\n' "$phase" "$$" > "$base/state.txt"
 set +e
 case "$phase" in
  counter) "$HOME/.dotnet/dotnet" "$base/payload/FileCat.CoreCrashHarness.dll" --analysis-memory-counter-control > "$base/$phase.log" 2>&1;;
  negative) FILECAT_TEST_MAC_RUSAGE_CONTROL="$base/payload/invalid-counter-layout.json" "$HOME/.dotnet/dotnet" "$base/payload/FileCat.CoreCrashHarness.dll" --analysis-memory-counter-control > "$base/$phase.log" 2>&1;;
  *)
   count=1000000; shape=$phase
   case "$phase" in small-*) count=1000;shape=${phase#small-};; huge-*) count=10000000;shape=${phase#huge-};; esac
   mkdir "$base/case-$phase"
   "$HOME/.dotnet/dotnet" "$base/payload/FileCat.CoreCrashHarness.dll" --analysis-csv-scale "$count" "$shape" "$base/case-$phase" > "$base/$phase.log" 2>&1;;
 esac
 result=$?
 set -e
 expected=0; if [ "$phase" = negative ]; then expected=31; fi
 if [ "$result" != "$expected" ]; then printf 'FINISHED exit=1 phase=%s nativeExit=%s\n' "$phase" "$result" > "$base/state.txt"; tail -n 30 "$base/$phase.log"; exit 1; fi
 if ! python3 "$base/payload/validate.py" "$phase" "$base/$phase.log"; then printf 'FINISHED exit=1 phase=%s validation=failed\n' "$phase" > "$base/state.txt"; exit 1; fi
done
printf 'FINISHED exit=0\n' > "$base/state.txt"
