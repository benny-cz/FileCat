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
[ "$actual" = '4db8cfbdaf90cab696ffb9b3bf65f0bb2f0b04a2bcb001afd872a9e9e321d7b1' ]; umask 077; mkdir payload; tar -xzf bundle.tgz -C payload
export FILECAT_TEST_MAC_RUSAGE_CONTROL="$base/payload/render/native-counter-layout.json"
for count in 1000000; do
 for shape in wide deep; do
  phase="$count-$shape";idle "$phase";printf 'RUNNING phase=%s runnerPid=%s\n' "$phase" "$$" > "$base/state.txt"
  case="$base/case-$phase";mkdir "$case"
  export FILECAT_TEST_NATIVE_SCALE_COUNT="$count" FILECAT_TEST_NATIVE_SCALE_SHAPE="$shape" FILECAT_TEST_NATIVE_SCALE_ROOT="$case"
  export FILECAT_TEST_NATIVE_SCALE_KEEP_SOURCE=1
  unset FILECAT_TEST_NATIVE_SCALE_SOURCE
  if [ "$count-$shape" = 1000000-wide ]; then export FILECAT_TEST_NATIVE_SCALE_SOURCE='/Users/benny/FileCatValidation/dsa-batch-20261107-r1182/retained-fixtures/native-20737c85917441a4ac76f67a59c7596e/source'; fi
  set +e
  (cd "$base/payload/render"; "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class '*AnalysisAcquisitionScaleTests' -noLogo -noColor -maxThreads 1 -xml "$case/tests.xml") > "$case/test.log" 2>&1
  result=$?;set -e
  if [ "$result" != 0 ] || ! python3 "$base/payload/render/validate-native-optimization.py" scale "$case"; then
   printf 'FINISHED exit=1 phase=%s nativeExit=%s\n' "$phase" "$result" > "$base/state.txt";tail -n 30 "$case/test.log";exit 1
  fi
 done
done
printf 'FINISHED exit=0\n' > "$base/state.txt"
