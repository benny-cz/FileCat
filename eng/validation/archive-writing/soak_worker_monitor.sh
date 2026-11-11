#!/bin/bash
# Soak for the two intermittent Ubuntu worker-monitor refusals of the TAR.GZ checkpoint (WorkerUnavailable in ordinary
# TAR preparation and assessment cases, cause never captured): replays the TAR pipelines, the worker lifecycle classes
# and the native Unix archive storage cases of an already built run of run_unix_test_packet.sh, ITERATIONS times, three
# shards at once for load, and records every result that did not pass with its message, so a recurrence keeps its facts.
# Usage: soak_worker_monitor.sh RUN_DIR ITERATIONS
#   RUN_DIR is a finished run (its src was built in Release by the runner); writes RUN_DIR-soak/{N/*.trx,summary.txt}.
# Refuses (exit 3) while another FileCat/.NET test process runs, as the runner does.
set -euo pipefail
RUN=$(cd "$1" && pwd); N=$2
[[ "$N" =~ ^[1-9][0-9]{0,2}$ ]] || { echo "bad iteration count" >&2; exit 2; }
CORE="$RUN/src/tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj"
[ -f "$CORE" ] && [ -d "$RUN/src/tests/FileCat.Core.Tests/bin/Release" ] || { echo "no built Core tests in $RUN" >&2; exit 2; }
OUT="$RUN-soak"; test ! -e "$OUT" || { echo "$OUT exists" >&2; exit 2; }
case "$(uname -s)" in
  Darwin) DOTNET_ROOT="$HOME/.dotnet" ;;
  Linux) DOTNET_ROOT="$HOME/FileCatValidation/archive-writing-42829297/dotnet" ;;
  *) exit 2 ;;
esac
others() {
  # As the runner does: a shell that runs this script or the other runners names test projects in its own arguments.
  for pid in $(pgrep -f 'FileCat\.(App|Core\.Tests|Remote\.Tests|App\.Tests|Rendering\.Tests)|testhost|vstest\.console|ArchiveFuzzHarness' || true); do
    args=$(ps -o args= -p "$pid" 2>/dev/null || true)
    case "$args" in *run_unix_test_packet.sh*|*run_fz10_smoke.sh*|*soak_worker_monitor.sh*|"") continue ;; esac
    echo "$pid $args"
  done
}
if [ -n "$(others)" ]; then echo "another tester owns this machine:" >&2; others >&2; exit 3; fi
export DOTNET_ROOT PATH="$DOTNET_ROOT:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1 DOTNET_NOLOGO=1
mkdir -m 700 "$OUT"
SHARDS=("pipelines|FullyQualifiedName~ArchiveTarGzipPipelineTests|FullyQualifiedName~ArchiveTarBzip2PipelineTests|FullyQualifiedName~ArchiveTarZstdPipelineTests|FullyQualifiedName~ArchiveTarRewriteTests"
        "workers|FullyQualifiedName~ArchiveAssessmentClientTests|FullyQualifiedName~ArchiveWorkerCancellationTests|FullyQualifiedName~ArchiveWorkerSessionDiagnosticsTests|FullyQualifiedName~ArchiveStrictTarTests"
        "storage|FullyQualifiedName~UnixArchiveStorageTests")
date -u +%Y-%m-%dT%H:%M:%SZ >"$OUT/started.txt"
for i in $(seq 1 "$N"); do
  mkdir "$OUT/$i"; pids=""
  for s in "${SHARDS[@]}"; do
    name=${s%%|*}; filter=${s#*|}
    nice -n 5 "$DOTNET_ROOT/dotnet" test "$CORE" -c Release --no-build --no-restore --disable-build-servers -p:NuGetAudit=false \
      --filter "$filter" --logger "trx;LogFileName=$name.trx" --results-directory "$OUT/$i" --blame-hang-timeout 20m \
      </dev/null >"$OUT/$i/$name.log" 2>&1 &
    pids="$pids $!"
  done
  for p in $pids; do wait "$p" || true; done
done
date -u +%Y-%m-%dT%H:%M:%SZ >"$OUT/finished.txt"
python3 - "$OUT" "$N" <<'EOF'
import glob, os, re, sys
out, n = sys.argv[1], int(sys.argv[2])
row = re.compile(r'<UnitTestResult [^>]*?testName="([^"]+)"[^>]*?outcome="(\w+)"[^>]*?(?:/>|>(.*?)</UnitTestResult>)', re.S)
totals, bad = {}, []
for i in range(1, n + 1):
    for trx in sorted(glob.glob(os.path.join(out, str(i), "*.trx"))):
        for name, outcome, body in row.findall(open(trx, encoding="utf-8", errors="replace").read()):
            totals[outcome] = totals.get(outcome, 0) + 1
            if outcome not in ("Passed", "NotExecuted"):
                message = re.search(r"<Message>(.*?)</Message>", body or "", re.S)
                bad.append(f"{i} {name} {outcome}: {(message.group(1) if message else '').strip()[:600]}")
with open(os.path.join(out, "summary.txt"), "w", encoding="utf-8") as summary:
    summary.write(f"iterations={n} outcomes={totals}\n")
    summary.write(f"worker-unavailable={sum('WorkerUnavailable' in line or 'archive worker' in line for line in bad)}\n")
    for line in bad:
        summary.write(line + "\n")
print(open(os.path.join(out, "summary.txt"), encoding="utf-8").read()[:4000])
EOF
