#!/bin/bash
# Development qualification only. Prepare/build an owned self-contained bundle and Core tests beforehand.
# This runner neither installs helpers nor changes global/user-manager controller configuration.
set -euo pipefail

if [ "${1:-}" = --inside ]; then
    shift
    [ "$#" -eq 5 ] || exit 64
    repo=$1; dotnet=$2; probe=$3; results=$4; tag=$5
    export FILECAT_ARCHIVE_LINUX_NATIVE=1 FILECAT_ARCHIVE_LINUX_PROBE="$probe"
    export DOTNET_ROOT="$(dirname "$dotnet")" PATH="$(dirname "$dotnet"):/usr/bin:/bin"
    export DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1
    cd "$repo"
    uname -a
    id
    cat /proc/self/cgroup
    "$dotnet" --info
    "$dotnet" test tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj -c Release --no-build --no-restore \
        --filter FullyQualifiedName~LinuxArchive --logger "trx;LogFileName=$tag.trx" --results-directory "$results" -v normal
    exit $?
fi

if [ "$#" -ne 4 ]; then
    echo 'usage: run-linux-containment.sh ABS_REPO ABS_DOTNET ABS_PROBE ABS_RESULTS' >&2
    exit 64
fi
[ "$(uname -s)" = Linux ] && [ "$(id -u)" -ne 0 ] || { echo 'An ordinary native Linux user is required.' >&2; exit 65; }
repo=$1; dotnet=$2; probe=$3; results=$4
for path in "$repo" "$dotnet" "$probe" "$results"; do
    case "$path" in /*) ;; *) echo 'All qualification paths must be absolute.' >&2; exit 64 ;; esac
done
[ -f "$repo/tests/FileCat.Core.Tests/bin/Release/net10.0/FileCat.Core.Tests.dll" ] && [ -x "$dotnet" ] && [ -x "$probe" ] || exit 65
python3 - <<'PY'
import os, subprocess
active=[]
for row in subprocess.check_output(['ps','-A','-o','pid=','-o','comm='],text=True).splitlines():
    fields=row.strip().split(None,1)
    if len(fields)!=2:
        continue
    name=os.path.basename(fields[1])
    if name.startswith('FileCat') or name in ('dotnet','testhost','vstest.console'):
        active.append(row)
if active:
    raise SystemExit('Another FileCat/test instance is active; postpone.\n'+'\n'.join(active))
PY
mkdir -p "$results"
script=$(realpath "$0")
tag="linux-containment-$(date -u +%Y%m%dT%H%M%SZ)-$(cat /proc/sys/kernel/random/uuid)"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
export DBUS_SESSION_BUS_ADDRESS="${DBUS_SESSION_BUS_ADDRESS:-unix:path=$XDG_RUNTIME_DIR/bus}"
# The existing user manager places the driver beneath its delegated subtree; SSH sessions can be outside it.
# Each worker uses its own native cgroup, and the runner creates no persistent systemd unit.
systemd-run --user --wait --pipe --collect --unit="filecat-$tag" \
    /bin/bash "$script" --inside "$repo" "$dotnet" "$probe" "$results" "$tag"
