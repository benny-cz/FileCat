#!/bin/bash
# Build and run filtered tests from one hash-pinned source packet on a disposable Unix test machine.
# Usage: run_unix_test_packet.sh RUN_ID PACKET SHA256 FILTER PROJECT [PROJECT...]
#   PROJECT paths are relative to the packet root (for example tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj).
#   Prefix build: to only build a project, or publish: to publish it self-contained for this machine's RID into
#   RUN_ID/publish/NAME (group/other write removed). On Linux a published FileCat.App or FileCat.WorkerProbe enables the
#   confined native lane (FILECAT_ARCHIVE_LINUX_NATIVE/_APP/_PROBE) and test projects then run in a transient
#   systemd --user service, because each worker's delegated cgroup must share the user manager's subtree.
#   An env:NAME=VALUE entry exports a test variable (also into the systemd --user service), for example
#   env:FILECAT_ARCHIVE_SMALL_VOLUME=/home/benny/FileCatValidation/small-volume for an owned small tmpfs.
#   FILTER is a dotnet test --filter expression; "*" runs the complete project inventory.
# Writes $ROOT/RUN_ID/{status.txt,summary.json,machine.txt,*.log,results/*.trx} and RUN_ID-evidence.tar.gz.
# Refuses (exit 3) while another FileCat/.NET test process from a different owner directory is running.
set -euo pipefail
RUN_ID=$1; PACKET=$2; EXPECTED=$3; FILTER=$4; shift 4
PROJECTS=("$@")
[[ "$RUN_ID" =~ ^[A-Za-z0-9._-]{1,80}$ ]] || { echo "bad run id" >&2; exit 2; }
[ "${#PROJECTS[@]}" -ge 1 ] || { echo "no project" >&2; exit 2; }
ROOT="$HOME/FileCatValidation/aw-claude"
mkdir -p "$ROOT"; chmod 700 "$ROOT"
D="$ROOT/$RUN_ID"
test ! -e "$D" && test ! -e "$D-evidence.tar.gz" || { echo "run directory exists" >&2; exit 2; }
case "$(uname -s)" in
  Darwin) DOTNET_ROOT="$HOME/.dotnet"; LABEL=mac; JOBS=6; RID=osx-arm64 ;;
  Linux) DOTNET_ROOT="$HOME/FileCatValidation/archive-writing-42829297/dotnet"; LABEL=ubuntu; JOBS=4; RID=linux-x64 ;;
  *) exit 2 ;;
esac
others() {
  # Exclude this runner and its own subshells by script name (macOS bash 3.2 has no BASHPID).
  for pid in $(pgrep -f 'FileCat\.(App|Core\.Tests|Remote\.Tests|App\.Tests|Rendering\.Tests)|testhost|vstest\.console|ArchiveFuzzHarness|FileCat\.ArchiveWorker' || true); do
    args=$(ps -o args= -p "$pid" 2>/dev/null || true)
    case "$args" in *run_unix_test_packet.sh*|"") continue ;; esac
    echo "$pid $args" | grep -v -- "$D" || true
  done
}
if [ -n "$(others)" ]; then echo "another tester owns this machine:" >&2; others >&2; exit 3; fi
ACTUAL=$(python3 -c 'import hashlib,sys;print(hashlib.sha256(open(sys.argv[1],"rb").read()).hexdigest())' "$PACKET")
[ "$ACTUAL" = "$EXPECTED" ] || { echo "packet hash mismatch" >&2; exit 2; }
mkdir -m 700 "$D"; mkdir "$D/src" "$D/results"
finish() {
  local code=$?
  [ -f "$D/status.txt" ] || echo FAIL >"$D/status.txt"
  if [ -n "$(pgrep -f "$D" || true)" ]; then echo "leftover processes" >>"$D/status.txt"; pkill -f "$D" || true; fi
  tar -czf "$D-evidence.tar.gz" -C "$ROOT" --exclude="$RUN_ID/src" "$RUN_ID" || true
  exit $code
}
trap finish EXIT
date -u +%Y-%m-%dT%H:%M:%SZ >"$D/started.txt"
tar -xzf "$PACKET" -C "$D/src"
export DOTNET_ROOT PATH="$DOTNET_ROOT:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1 DOTNET_NOLOGO=1
{ uname -a; if [ $LABEL = mac ]; then sw_vers; sysctl -n hw.logicalcpu; else lsb_release -d; nproc; fi
  "$DOTNET_ROOT/dotnet" --version; df -h "$D" | tail -1; echo "packet=$EXPECTED"; echo "filter=$FILTER"; } >"$D/machine.txt" 2>&1
if [ $LABEL = ubuntu ]; then NICE=(nice -n 5); else NICE=(env); fi
EXTRA_ENVS=()
for P in "${PROJECTS[@]}"; do
  case "$P" in
    env:*) KV=${P#env:}; [[ "$KV" =~ ^[A-Z][A-Z0-9_]*=[^[:space:]]*$ ]] || { echo "bad env entry" >&2; exit 2; }
      export "$KV"; EXTRA_ENVS+=("$KV"); echo "env=$KV" >>"$D/machine.txt" ;;
  esac
done
RUNNER=("${NICE[@]}")
i=0
for P in "${PROJECTS[@]}"; do
  i=$((i+1))
  case "$P" in
    env:*) continue ;;
    build:*) P=${P#build:}; name=$(basename "$P" .csproj)
      "${NICE[@]}" "$DOTNET_ROOT/dotnet" build "$D/src/$P" -c Release --disable-build-servers -p:NuGetAudit=false -m:$JOBS -v:minimal >"$D/build-$name.log" 2>&1
      continue ;;
    publish:*) P=${P#publish:}; name=$(basename "$P" .csproj)
      "${NICE[@]}" "$DOTNET_ROOT/dotnet" publish "$D/src/$P" -c Release -r $RID --self-contained true -o "$D/publish/$name"         --disable-build-servers -p:NuGetAudit=false -m:$JOBS -v:minimal >"$D/publish-$name.log" 2>&1
      chmod -R go-w "$D/publish/$name"
      { du -sb "$D/publish/$name" 2>/dev/null || du -sk "$D/publish/$name"; find "$D/publish/$name" -type f | wc -l; } >"$D/publish-$name.size"
      if [ $LABEL = ubuntu ]; then
        ENVS=(DOTNET_ROOT="$DOTNET_ROOT" PATH="$DOTNET_ROOT:/usr/bin:/bin" DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1
              DOTNET_NOLOGO=1 FILECAT_ARCHIVE_LINUX_NATIVE=1 "${EXTRA_ENVS[@]}")
        if [ -x "$D/publish/FileCat.App/FileCat" ]; then ENVS+=(FILECAT_ARCHIVE_LINUX_APP="$D/publish/FileCat.App/FileCat"); fi
        if [ -x "$D/publish/FileCat.WorkerProbe/FileCat.WorkerProbe" ]; then
          ENVS+=(FILECAT_ARCHIVE_LINUX_PROBE="$D/publish/FileCat.WorkerProbe/FileCat.WorkerProbe"); fi
        export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
        export DBUS_SESSION_BUS_ADDRESS="${DBUS_SESSION_BUS_ADDRESS:-unix:path=$XDG_RUNTIME_DIR/bus}"
        RUNNER=(systemd-run --user --wait --pipe --collect --quiet -p Nice=5 --unit="filecat-aw-claude-$RUN_ID-pub$i" /usr/bin/env "${ENVS[@]}")
      fi
      continue ;;
  esac
  name=$(basename "$P" .csproj)
  "${NICE[@]}" "$DOTNET_ROOT/dotnet" build "$D/src/$P" -c Release --disable-build-servers -p:NuGetAudit=false -m:$JOBS -v:minimal >"$D/build-$name.log" 2>&1
  args=(test "$D/src/$P" -c Release --no-build --no-restore --disable-build-servers -p:NuGetAudit=false
        --logger "trx;LogFileName=$name.trx" --results-directory "$D/results" --blame-hang-timeout 20m)
  [ "$FILTER" = "*" ] || args+=(--filter "$FILTER")
  set +e; "${RUNNER[@]}" "$DOTNET_ROOT/dotnet" "${args[@]}" </dev/null >"$D/test-$name.log" 2>&1; echo $? >"$D/exit-$name.txt"; set -e
done
date -u +%Y-%m-%dT%H:%M:%SZ >"$D/finished.txt"
python3 - "$D" <<'PY'
import json, sys, xml.etree.ElementTree as ET
from pathlib import Path
d = Path(sys.argv[1]); ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
out = {'suites': {}, 'failed': [], 'ok': True}
for trx in sorted((d / 'results').glob('*.trx')):
    root = ET.parse(trx).getroot(); c = root.find('.//t:Counters', ns).attrib
    rows = [r.attrib for r in root.findall('.//t:UnitTestResult', ns)]
    failed = [r['testName'] for r in rows if r['outcome'] not in ('Passed', 'NotExecuted')]
    out['suites'][trx.stem] = {'total': len(rows), 'passed': sum(r['outcome'] == 'Passed' for r in rows),
                               'skipped': sum(r['outcome'] == 'NotExecuted' for r in rows), 'failed': len(failed),
                               'counters_total': int(c['total'])}
    out['failed'] += failed[:200]
    if failed or len(rows) != int(c['total']) or not rows: out['ok'] = False
exits = {p.stem[5:]: int(p.read_text().strip()) for p in d.glob('exit-*.txt')}
out['exit_codes'] = exits
if any(v != 0 for v in exits.values()) or not out['suites']: out['ok'] = False
(d / 'summary.json').write_text(json.dumps(out, indent=2))
(d / 'status.txt').write_text('PASS\n' if out['ok'] else 'FAIL\n')
print(json.dumps(out))
PY
