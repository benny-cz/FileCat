#!/usr/bin/env bash
set -euo pipefail
mode="${1:?positive or icon}"
sudo_password="${2:?supply the disposable guest sudo password at runtime}"
printf '%s\n' "$sudo_password" | sudo -S -p '' /usr/bin/python3 /tmp/filecat-ac43-tcp445-listener.py "$mode"
