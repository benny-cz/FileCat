set -eu
base=/home/benny/FileCatValidation/dsa-20261105-r95-links
deadline=$(($(date +%s) + 600))
state="$base/qualification-state.txt"
clear_slot() {
  ! pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 && ! pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1
}
wait_slot() {
  printf 'WAITING_FOR_OTHER_INSTANCES\n' > "$state"
  while ! clear_slot; do
    if [ "$(date +%s)" -ge "$deadline" ]; then printf 'WAIT_TIMEOUT\n' > "$state"; return 75; fi
    sleep 0.5
  done
}
while :; do
  wait_slot
  printf 'RUNNING_RENDER\n' > "$state"
  set +e
  sh /home/benny/FileCatValidation/dsa-20261105-r94-render/run.sh > "$base/render-driver.log" 2>&1
  result=$?
  set -e
  if [ "$result" -eq 0 ]; then break; fi
  if [ "$result" -ne 75 ]; then printf 'RENDER_FAILED\n' > "$state"; exit "$result"; fi
done
while :; do
  wait_slot
  printf 'RUNNING_NATIVE\n' > "$state"
  set +e
  python3 "$base/run.py" > "$base/native-driver.log" 2>&1
  result=$?
  set -e
  if [ -f "$base/app/owned.pid" ]; then break; fi
  if [ "$result" -eq 0 ]; then break; fi
  if [ "$(date +%s)" -ge "$deadline" ]; then printf 'WAIT_TIMEOUT\n' > "$state"; exit 75; fi
  sleep 0.5
done
python3 - "$base" <<'PY'
import json,pathlib,sys
base=pathlib.Path(sys.argv[1]);result=json.loads((base/'native-control-result.json').read_text())
assert result['state']=='passed-native-navigation-subset' and result['nativeCapturedRangeVerified'] and result['ownedProcessExited']
assert result['harnessReport']['sourceUnchanged'] and not result['harnessReport']['deadlineClosed']
assert result['harnessReport']['fixtureRegularPaths']==509
print('PASS: renderer and native captured range; owned FileCat exited',flush=True)
PY
printf 'FINISHED\n' > "$state"
