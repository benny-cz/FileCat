#!/usr/bin/env bash
set -euo pipefail
case "${1:-}" in
  positive) port=22; limit=12; output=/tmp/filecat-ac43-positive.txt ;;
  positive445) port=445; limit=12; output=/tmp/filecat-ac43-positive445.txt ;;
  icon) port=445; limit=45; output=/tmp/filecat-ac43-icon.txt ;;
  *) exit 2 ;;
esac
sudo_password="${2:?supply the disposable guest sudo password at runtime}"
printf '%s\n' "$sudo_password" | sudo -S -p '' timeout "${limit}s" tcpdump -i any -nn -l -q -tttt \
  "src host 192.168.58.128 and dst host 192.168.58.129 and tcp dst port ${port}" \
  > "$output" 2>&1 || code=$?
if [ "${code:-0}" -ne 0 ] && [ "${code:-0}" -ne 124 ]; then exit "$code"; fi
