#!/bin/bash
# Cross-RID structured mutation lanes (test tooling): build product + harness at the candidate from a source packet, then
# run every target's structured mutation for SECONDS each, LANES targets at a time, against the image's seed folders.
# Usage: structured-lanes.sh WORK PACKET PACKET_SHA256 SEEDS_TGZ SECONDS LANES
set -uo pipefail
W=$1; P=$2; PS=$3; SEEDS=$4; SECONDS_EACH=$5; LANES=$6
case "$(uname -s)" in Darwin) export DOTNET_ROOT=$HOME/.dotnet;; *) export DOTNET_ROOT=$HOME/FileCatValidation/archive-writing-42829297/dotnet;; esac
export PATH=$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 MSBUILDDISABLENODEREUSE=1
[ "$(shasum -a 256 "$P" 2>/dev/null | cut -d' ' -f1 || sha256sum "$P" | cut -d' ' -f1)" = "$PS" ] || { echo "packet hash mismatch"; exit 2; }
rm -rf "$W"; mkdir -p "$W/src" "$W/seeds" "$W/reports"; tar -xzf "$P" -C "$W/src"; tar -xzf "$SEEDS" -C "$W/seeds"
cd "$W/src"
dotnet build src/FileCat.Archives/FileCat.Archives.csproj -c Release -p:NuGetAudit=false -p:CopyLocalLockFileAssemblies=true --nologo -v q > "$W/build.log" 2>&1 || { echo build failed; exit 3; }
dotnet build eng/validation/archive-writing/ArchiveFuzzHarness/ArchiveFuzzHarness.csproj -c Release -p:NuGetAudit=false --nologo -v q >> "$W/build.log" 2>&1 || { echo harness build failed; exit 3; }
H=$W/src/eng/validation/archive-writing/ArchiveFuzzHarness/bin/Release/net10.0/FileCat.ArchiveFuzzHarness.dll
dotnet "$H" make-worker-seeds "$W/worker-seeds" > "$W/worker-seeds.log" 2>&1
seed=42830001
for d in "$W"/seeds/seeds-*; do
  t=${d##*/seeds-}
  [ "$t" = publication-races ] && [ "$(uname -s)" != Linux ] && [ "$(uname -s)" != Darwin ] && continue
  while [ "$(jobs -rp | wc -l)" -ge "$LANES" ]; do sleep 5; done
  DOTNET_GCHeapHardLimit=20000000 dotnet "$H" structured-mutation "$t" "$d" "$SECONDS_EACH" "$seed" "$W/reports/$t.json" > "$W/reports/$t.log" 2>&1 &
  seed=$((seed+1))
done
wait
date -u +%FT%TZ > "$W/done.txt"
