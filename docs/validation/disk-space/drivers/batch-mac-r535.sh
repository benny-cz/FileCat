set -eu
cd "$(dirname "$0")"
base=$(pwd)
busy() {
  pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -f '^[^ ]*dotnet +.*FileCat' >/dev/null 2>&1
}
if busy; then printf 'BUSY phase=verify\n' > state.txt; exit 75; fi
printf 'RUNNING phase=verify runnerPid=%s\n' "$$" > state.txt
if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1); else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1); fi
if [ "$actual" != '861c84178161b60dd8fca7edf3e3d822dfaf8837fa847c462b419716e24ce8c1' ]; then printf 'FAILED phase=integrity\n' > state.txt; exit 74; fi
umask 077
mkdir payload
tar -xzf bundle.tgz -C payload
for phase in core render; do
  if busy; then printf 'BUSY phase=%s\n' "$phase" > "$base/state.txt"; exit 75; fi
  printf 'RUNNING phase=%s runnerPid=%s\n' "$phase" "$$" > "$base/state.txt"
  cd "$base/payload/$phase"
  set +e
  if [ "$phase" = core ]; then
    "$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll -class 'FileCat.Core.Tests.Analysis*' -class '*CommandTests' -class '*DeviceIoSchedulerTests' -noLogo -noColor -maxThreads 2 -xml "$base/core-r535.xml" > "$base/core-r535.log" 2>&1
  else
    "$HOME/.dotnet/dotnet" FileCat.Rendering.Tests.dll -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 2 -xml "$base/render-r535.xml" > "$base/render-r535.log" 2>&1
  fi
  result=$?
  set -e
  cd "$base"
  if [ "$result" != 0 ]; then printf 'FINISHED phase=%s exit=%s\n' "$phase" "$result" > state.txt; tail -n 45 "$phase-r535.log"; exit "$result"; fi
done
printf 'FINISHED exit=0\n' > state.txt
printf 'Core and rendering completed successfully.\n'
