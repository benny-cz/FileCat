#!/usr/bin/env bash
# Runs the required WebKitGTK tests under the same named user-namespace grant
# that the Debian package installs. Ubuntu 24.04 otherwise sends bubblewrap into
# its generic unprivileged_userns profile, which denies sandbox construction.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DOTNET="$(readlink -f "$(command -v dotnet)")"
PROFILE="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/filecat-ci-webkit-$$.apparmor"
NAME="filecat-ci-webkit-$$"

# AppArmor attachment syntax gives whitespace and metacharacters meaning. The
# hosted tool path uses only this deliberately small set; refuse anything else.
case "$DOTNET" in
  *[!A-Za-z0-9._/-]*)
    echo "The .NET host path cannot be represented safely in the temporary AppArmor profile: $DOTNET" >&2
    exit 2
    ;;
esac
sed "s|^profile filecat /opt/filecat/FileCat flags=(unconfined) {|profile $NAME $DOTNET flags=(unconfined) {|" \
  "$ROOT/eng/linux/filecat.apparmor" > "$PROFILE"
grep -Fqx "profile $NAME $DOTNET flags=(unconfined) {" "$PROFILE" || {
  echo "The FileCat AppArmor template did not contain its expected attachment declaration." >&2
  exit 2
}

loaded=0
cleanup() {
  if [ "$loaded" = 1 ]; then sudo -n apparmor_parser -R "$PROFILE" || true; fi
  rm -f "$PROFILE"
}
trap cleanup EXIT

if [ -r /proc/sys/kernel/apparmor_restrict_unprivileged_userns ] &&
   [ "$(cat /proc/sys/kernel/apparmor_restrict_unprivileged_userns)" = 1 ]; then
  command -v apparmor_parser >/dev/null || { echo "AppArmor parser is required while user namespaces are restricted." >&2; exit 2; }
  sudo -n apparmor_parser -r "$PROFILE"
  loaded=1
fi

cd "$ROOT"
FILECAT_REQUIRE_WEBKIT=1 xvfb-run -a dotnet test tests/FileCat.App.Tests -c Release --no-build \
  --filter "FullyQualifiedName~LinuxPageEngineTests" --blame-hang-timeout 3m --blame-hang-dump-type mini
