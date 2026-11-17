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
[ "$actual" = '@HASH@' ];umask 077;mkdir payload;tar -xzf bundle.tgz -C payload
bin="$base/payload/tests/FileCat.Core.Tests/bin/Debug/net10.0"
for phase in epoch core;do
 idle;printf 'RUNNING phase=%s\n' "$phase" > "$base/state.txt"
 if [ "$phase" = epoch ];then set -- -method '*RescanEpoch*';else set -- -class 'FileCat.Core.Tests.Analysis*' -class '*CommandTests' -class '*DeviceIoSchedulerTests' -class '*PathAndStateTests' -class '*ZipListingRevisionTests';fi
 set +e
 (cd "$bin";"$HOME/.dotnet/dotnet" FileCat.Core.Tests.dll "$@" -noLogo -noColor -maxThreads 2 -xml "$base/$phase.xml") > "$base/$phase.log" 2>&1
 result=$?;set -e
 if [ "$result" != 0 ] || ! python3 "$base/payload/validate.py" "$phase" "$base/$phase.xml";then printf 'FINISHED exit=1 phase=%s\n' "$phase" > "$base/state.txt";tail -n 30 "$base/$phase.log";exit 1;fi
done
printf 'FINISHED exit=0\n' > "$base/state.txt"
