set -eu
cd "$(dirname "$0")";base=$(pwd)
idle() {
 for attempt in 1 2 3; do
  if pgrep -x 'FileCat([.].*)?' >/dev/null 2>&1 || pgrep -x dotnet >/dev/null 2>&1; then
   printf 'BUSY\n' > state.txt;ps -axo pid=,args= > busy.txt;exit 75
  fi
  sleep 1
 done
}
idle
printf 'RUNNING phase=verify\n' > state.txt
if [ "$(uname -s)" = Darwin ];then actual=$(shasum -a 256 bundle.tgz | cut -d ' ' -f 1);else actual=$(sha256sum bundle.tgz | cut -d ' ' -f 1);fi
[ "$actual" = 'ae77584669572d20eb2eeae1931e2a17951a5e0703100fd28e3f5167cc4e7a0e' ];umask 077;mkdir payload;tar -xzf bundle.tgz -C payload
bin="$base/payload/render"
for phase in app-rescan render settings;do
 idle;printf 'RUNNING phase=%s\n' "$phase" > "$base/state.txt"
 assembly=FileCat.Rendering.Tests.dll
 case "$phase" in
  app-rescan) set -- -method '*NativeRescanPreparation*';;
  render) set -- -class '*AnalysisRenderingTests';;
  settings) assembly=FileCat.App.Tests.dll;set -- -class '*SettingsDialogTests' -class '*SettingsPreservationTests';;
 esac
 set +e
 (cd "$bin";"$HOME/.dotnet/dotnet" "$assembly" "$@" -noLogo -noColor -maxThreads 2 -xml "$base/$phase.xml") > "$base/$phase.log" 2>&1
 result=$?;set -e
 if [ "$result" != 0 ] || ! python3 "$base/payload/validate.py" "$phase" "$base/$phase.xml";then printf 'FINISHED exit=1 phase=%s\n' "$phase" > "$base/state.txt";tail -n 30 "$base/$phase.log";exit 1;fi
done
printf 'FINISHED exit=0\n' > "$base/state.txt"
