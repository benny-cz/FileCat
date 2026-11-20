#!/bin/bash
# Replay one fuzz image's collected regression corpus on this RID (test tooling, §13.6): every target's folder runs once
# in one process through `replay-corpus`, JOBS targets at a time (default 1), the folders with most files first. Reuses a
# structured-lane build of the same candidate.
# Usage: run_corpus_replay_unix.sh WORK HARNESS_DLL CORPORA_TGZ CORPORA_SHA256 [JOBS]
set -uo pipefail
W=$1; H=$2; C=$3; CS=$4; JOBS=${5:-1}
case "$(uname -s)" in Darwin) export DOTNET_ROOT=$HOME/.dotnet;; *) export DOTNET_ROOT=$HOME/FileCatValidation/archive-writing-42829297/dotnet;; esac
export PATH=$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
[ "$(shasum -a 256 "$C" | cut -d' ' -f1)" = "$CS" ] || { echo "corpora hash mismatch"; exit 2; }
rm -rf "$W"; mkdir -p "$W/reports"; tar -xzf "$C" -C "$W"
order=$(for d in "$W"/corpora/*; do echo "$(find "$d" -type f | wc -l | tr -d ' ') ${d##*/}"; done | sort -rn | cut -d' ' -f2)
for t in $order; do
  while [ "$(jobs -rp | wc -l)" -ge "$JOBS" ]; do sleep 2; done
  ( DOTNET_GCHeapHardLimit=20000000 dotnet "$H" replay-corpus "$t" "$W/corpora/$t" "$W/reports/$t.json" > "$W/reports/$t.out" 2>&1
    echo "$t exit=$?" >> "$W/replay.log" ) &
done
wait
date -u +%FT%TZ > "$W/done.txt"
