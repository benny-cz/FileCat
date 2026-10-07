#!/usr/bin/env bash
set -euo pipefail

# Keep repository and lock waits within this step, with visible progress and no package-install retry after interruption.
apt_options=(-o Acquire::Retries=2 -o Acquire::http::Timeout=20 -o Acquire::https::Timeout=20
             -o Acquire::ForceIPv4=true -o DPkg::Lock::Timeout=30)
updated=false
update_status=0
for attempt in 1 2; do
    echo "Refreshing Linux package indexes (attempt $attempt of 2; at most 120 seconds)."
    if sudo -n timeout --signal=TERM --kill-after=15s 120s apt-get "${apt_options[@]}" update; then
        updated=true
        break
    else
        update_status=$?
        echo "Package index refresh exited $update_status." >&2
    fi
    if [[ $attempt == 1 ]]; then sleep 2; fi
done
if [[ $updated != true ]]; then exit "$update_status"; fi

echo "Installing Linux native test dependencies (at most 300 seconds)."
sudo -n env DEBIAN_FRONTEND=noninteractive timeout --signal=TERM --kill-after=15s 300s \
    apt-get "${apt_options[@]}" install -y --no-install-recommends \
    gnome-keyring dbus libsecret-1-0 samba smbclient gvfs-backends gvfs-fuse \
    dosfstools exfatprogs exfat-fuse openssh-server libwebkit2gtk-4.1-0 xvfb python3-gi desktop-file-utils
