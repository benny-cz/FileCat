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
#   A hang:MINUTES entry (1-600) replaces the default 20-minute per-test hang guard, for opt-in scale lanes such as AT18.
#   A test PROJECT may name a shard after "#": PATH.csproj#ClassA+ClassB runs only tests whose name contains one of them,
#   PATH.csproj#!ClassA+ClassB (or #~ClassA+ClassB, as the Windows runner takes it) runs every other test; each shard is
#   its own test process with its own trx and exit code.
#   FileCat's archive-worker gate is per process, so shards let worker-heavy test classes use more than one CPU.
#   A parallel:on entry runs all test processes at the same time after every build has finished (default: one by one).
#   FILTER is a dotnet test --filter expression; "*" runs the complete project inventory.
# Writes $ROOT/RUN_ID/{status.txt,summary.json,machine.txt,*.log,results/*.trx} and RUN_ID-evidence.tar.gz (without src/ and
# publish/; publish-sha256.txt lists the published files).
# Refuses (exit 3) while another FileCat/.NET test process from a different owner directory is running.
set -euo pipefail
RUN_ID=$1; PACKET=$2; EXPECTED=$3; FILTER=$4; shift 4
PROJECTS=("$@")
[[ "$RUN_ID" =~ ^[A-Za-z0-9._-]{1,80}$ ]] || { echo "bad run id" >&2; exit 2; }
[ "${#PROJECTS[@]}" -ge 1 ] || { echo "no project" >&2; exit 2; }
# FILTER and each PROJECT entry are written into machine.txt and handed to the test processes: printable ASCII without
# `"` or `$`, judged in the C locale. A line break in the filter wrote a line of its own into the record - an env= line
# this runner never exported - while each test process took the filter's first line alone; systemd-run expands ${...}
# in the test processes' arguments; and dotnet hands the filter to MSBuild inside quotes, so a `"` ended it and what
# followed a `;` set other properties - the gate among them, through VSTestCLIRunSettings. Nor do they hold `@` (a
# response file where the filter begins, an item list inside it) or `%` (MSBuild decodes %XX), as a project entry's
# name reaches MSBuild too, in the logger's property; and the filter does not begin with `-`. (The patterns sit in
# variables: a space written into [[ =~ ]] itself would have to be escaped, which bash versions pass on differently.)
PRINTABLE='^[ !#&-?A-~]+$'
FILTERABLE='^[ !#&-,.-?A-~][ !#&-?A-~]*$'
( LC_ALL=C; [[ "$FILTER" =~ $FILTERABLE ]] ) || { echo "bad filter" >&2; exit 2; }
for P in "${PROJECTS[@]}"; do ( LC_ALL=C; [[ "$P" =~ $PRINTABLE ]] ) || { echo "bad project entry" >&2; exit 2; }; done
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
# A run extracts, builds and publishes 3+ GiB beside earlier evidence: refuse rather than fill the machine's disk.
FREE_KIB=$(df -Pk "$ROOT" | awk 'NR == 2 {print $4}')
[ "${FREE_KIB:-0}" -ge $((10 * 1024 * 1024)) ] || { echo "less than 10 GiB free under $ROOT" >&2; exit 4; }
ACTUAL=$(python3 -c 'import hashlib,sys;print(hashlib.sha256(open(sys.argv[1],"rb").read()).hexdigest())' "$PACKET")
[ "$ACTUAL" = "$EXPECTED" ] || { echo "packet hash mismatch" >&2; exit 2; }
mkdir -m 700 "$D"; mkdir "$D/src" "$D/results"
finish() {
  local code=$?
  [ -f "$D/status.txt" ] || echo FAIL >"$D/status.txt"
  # Only processes running from inside the run folder: a sibling path such as RUN_ID.log in a caller's arguments is not ours.
  if [ -n "$(pgrep -f "$D/" || true)" ]; then echo "leftover processes" >>"$D/status.txt"; pkill -f "$D/" || true; fi
  # Published apps are rebuilt from the pinned packet, so the evidence keeps each published file's SHA-256 instead of
  # 200+ MiB of binaries per run (the Windows runner also leaves src and publish out).
  if [ -d "$D/publish" ]; then (cd "$D/publish" && find . -type f -exec shasum -a 256 {} + | LC_ALL=C sort -k2) >"$D/publish-sha256.txt" || true; fi
  tar -czf "$D-evidence.tar.gz" -C "$ROOT" --exclude="$RUN_ID/src" --exclude="$RUN_ID/publish" "$RUN_ID" || true
  exit $code
}
trap finish EXIT
date -u +%Y-%m-%dT%H:%M:%SZ >"$D/started.txt"
tar -xzf "$PACKET" -C "$D/src"
export DOTNET_ROOT PATH="$DOTNET_ROOT:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1 DOTNET_NOLOGO=1
{ uname -a; if [ $LABEL = mac ]; then sw_vers; sysctl -n hw.logicalcpu; else lsb_release -d; nproc; fi
  "$DOTNET_ROOT/dotnet" --version; df -h "$D" | tail -1; printf 'packet=%s\nfilter=%s\n' "$EXPECTED" "$FILTER"; } >"$D/machine.txt" 2>&1
# A record line that carries an argument's text is written with printf: echo under sh (xpg_echo) turns a \n inside
# a value into a line of its own.
if [ $LABEL = ubuntu ]; then NICE=(nice -n 5); else NICE=(env); fi
EXTRA_ENVS=()
HANG=20m
PARALLEL=0
for P in "${PROJECTS[@]}"; do
  case "$P" in
    # FILECAT_ names in capitals, values of printable ASCII without a space or `$`, judged in the C locale (elsewhere
    # [A-Z] may take a lowercase letter): a value could hold \x1c or \x85, which Python's splitlines() takes for a line
    # end (the evidence check once read the record that way);
    # the test platform reads some other names (VSTESTCLIRUNSETTINGS can set a variable inside the test host); and
    # systemd-run expands ${...} in a value on its way to the tests. This refuses them before a run starts.
    env:*) KV=${P#env:}; ( LC_ALL=C; [[ "$KV" =~ ^FILECAT_[A-Z0-9_]+=[!-#%-~]*$ ]] ) || { echo "bad env entry" >&2; exit 2; }
      export "$KV"; EXTRA_ENVS+=("$KV"); printf 'env=%s\n' "$KV" >>"$D/machine.txt" ;;
    hang:*) M=${P#hang:}; [[ "$M" =~ ^[1-9][0-9]{0,2}$ ]] && [ "$M" -le 600 ] || { echo "bad hang entry" >&2; exit 2; }
      HANG="${M}m"; printf 'hang=%s\n' "$HANG" >>"$D/machine.txt" ;;
    parallel:*) case "${P#parallel:}" in on) PARALLEL=1 ;; off) PARALLEL=0 ;; *) echo "bad parallel entry" >&2; exit 2 ;; esac
      printf 'parallel=%s\n' "${P#parallel:}" >>"$D/machine.txt" ;;
  esac
done
USE_SYSTEMD=0
BUILT="|"
TAB=$'\t'
TESTS=()
i=0
for P in "${PROJECTS[@]}"; do
  i=$((i+1))
  case "$P" in
    env:*|hang:*|parallel:*) continue ;;
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
        USE_SYSTEMD=1
      else
        # The macOS sandboxed lane (route decision of 12 November 2026): each publish carries the sandbox launcher.
        if [ -x "$D/publish/FileCat.App/FileCat" ] && [ -x "$D/publish/FileCat.App/FileCatArchiveWorker" ]; then
          export FILECAT_ARCHIVE_MAC_NATIVE=1 FILECAT_ARCHIVE_MAC_APP="$D/publish/FileCat.App/FileCat"; fi
        if [ -x "$D/publish/FileCat.WorkerProbe/FileCat.WorkerProbe" ] && [ -x "$D/publish/FileCat.WorkerProbe/FileCatArchiveWorker" ]; then
          export FILECAT_ARCHIVE_MAC_NATIVE=1 FILECAT_ARCHIVE_MAC_PROBE="$D/publish/FileCat.WorkerProbe/FileCat.WorkerProbe"; fi
      fi
      continue ;;
  esac
  # A test project, optionally one shard of it; each project is built once, before any test process starts.
  csproj=${P%%#*}; spec=""; [ "$csproj" = "$P" ] || spec=${P#*#}
  name=$(basename "$csproj" .csproj); shard=""
  if [ -n "$spec" ]; then
    neg=0; case "$spec" in '!'*|'~'*) neg=1; spec=${spec:1} ;; esac
    [[ "$spec" =~ ^[A-Za-z0-9_.]+(\+[A-Za-z0-9_.]+)*$ ]] || { echo "bad shard entry" >&2; exit 2; }
    IFS='+' read -r -a parts <<<"$spec"
    if [ $neg = 1 ]; then shard=$(printf 'FullyQualifiedName!~%s&' "${parts[@]}"); shard="(${shard%&})"; name="$name-not-${spec//+/-}"
    else shard=$(printf 'FullyQualifiedName~%s|' "${parts[@]}"); shard="(${shard%|})"; name="$name-${spec//+/-}"; fi
  fi
  case "$BUILT" in *"|$csproj|"*) ;; *)
    "${NICE[@]}" "$DOTNET_ROOT/dotnet" build "$D/src/$csproj" -c Release --disable-build-servers -p:NuGetAudit=false -m:$JOBS -v:minimal \
      >"$D/build-$(basename "$csproj" .csproj).log" 2>&1
    BUILT="$BUILT$csproj|" ;;
  esac
  f="$FILTER"
  if [ -n "$shard" ]; then if [ "$FILTER" = "*" ]; then f="$shard"; else f="($FILTER)&$shard"; fi; fi
  TESTS+=("$csproj$TAB$name$TAB$f")
done
run_test() {
  local csproj=$1 name=$2 f=$3 idx=$4
  local args=(test "$D/src/$csproj" -c Release --no-build --no-restore --disable-build-servers -p:NuGetAudit=false
        --logger "trx;LogFileName=$name.trx" --results-directory "$D/results" --blame-hang-timeout "$HANG" --blame-hang-dump-type mini)
  [ "$f" = "*" ] || args+=(--filter "$f")
  local runner=("${NICE[@]}")
  if [ $USE_SYSTEMD = 1 ]; then
    runner=(systemd-run --user --wait --pipe --collect --quiet -p Nice=5 --unit="filecat-aw-claude-$RUN_ID-test$idx" /usr/bin/env "${ENVS[@]}")
  fi
  printf 'test %s: %s\n' "$name" "$f" >>"$D/machine.txt"
  "${runner[@]}" "$DOTNET_ROOT/dotnet" "${args[@]}" </dev/null >"$D/test-$name.log" 2>&1; echo $? >"$D/exit-$name.txt"
}
set +e
idx=0; pids=""
for t in ${TESTS[@]+"${TESTS[@]}"}; do
  idx=$((idx+1)); IFS="$TAB" read -r c n f <<<"$t"
  if [ $PARALLEL = 1 ]; then run_test "$c" "$n" "$f" $idx & pids="$pids $!"; else run_test "$c" "$n" "$f" $idx; fi
done
for p in $pids; do wait "$p"; done
set -e
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
