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
[ "$actual" = '@HASH@' ]
umask 077
mkdir payload
tar -xzf bundle.tgz -C payload
export FILECAT_TEST_MAC_RUSAGE_CONTROL="$base/payload/native-counter-layout.json"
for count in 1000 100000 1000000; do
 for shape in wide deep; do
  phase="$count-$shape"; idle "$phase"
  printf 'RUNNING phase=%s runnerPid=%s\n' "$phase" "$$" > "$base/state.txt"
  case="$base/case-$phase"; mkdir "$case"
  export FILECAT_TEST_NATIVE_SCALE_COUNT="$count" FILECAT_TEST_NATIVE_SCALE_SHAPE="$shape" FILECAT_TEST_NATIVE_SCALE_ROOT="$case"
  set +e
  (cd "$base/payload/render"; "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class '*AnalysisAcquisitionScaleTests' -noLogo -noColor -maxThreads 1 -xml "$case/tests.xml") > "$case/test.log" 2>&1
  result=$?
  set -e
  if [ "$result" != 0 ]; then printf 'FINISHED exit=1 phase=%s nativeExit=%s\n' "$phase" "$result" > "$base/state.txt"; tail -n 30 "$case/test.log"; exit 1; fi
  if ! python3 "$base/payload/validate.py" "$case"; then printf 'FINISHED exit=1 phase=%s validation=failed\n' "$phase" > "$base/state.txt"; exit 1; fi
 done
done
printf 'FINISHED exit=0\n' > "$base/state.txt"
