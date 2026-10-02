#!/bin/sh
# Adds FileCat to this user's menu, using Desktop Entry escaping (not shell quoting).
set -eu
case "$0" in
  */*) script_dir=${0%/*} ;;
  *) script_dir=. ;;
esac
case "$script_dir" in /*) ;; *) script_dir=./$script_dir ;; esac
# Preserve newlines in directory names; remove only pwd's terminating newline.
HERE=$(cd "$script_dir" && pwd -P && printf '.')
HERE=${HERE%.}
HERE=${HERE%?}
if [ "${1-}" = --launch ]; then
  shift
  exec "$HERE/FileCat" "$@"
fi
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"

escape_entry() {
  # Exec is parsed twice: Desktop Entry string escapes, then argument quoting.
  # Icon uses only the string layer. Read the path on stdin so awk -v cannot
  # reinterpret literal backslashes. UTF-8 bytes pass through unchanged.
  LC_ALL=C awk -v mode="$1" '
    BEGIN { ORS = "" }
    NR > 1 { printf "%s", "\\n" }
    {
      for (i = 1; i <= length($0); i++) {
        c = substr($0, i, 1)
        if (c == "\\") printf "%s", mode == "exec" ? "\\\\\\\\" : "\\\\"
        else if (c == "\t") printf "%s", "\\t"
        else if (c == "\r") printf "%s", "\\r"
        else if (mode == "exec" && c == "%") printf "%s", "%%"
        else if (mode == "exec" && (c == "\"" || c == "$" || c == "`")) printf "%s", "\\\\" c
        else printf "%s", c
      }
    }'
}
# GLib checks the executable before expanding %%: a literal percent in its
# pathname otherwise makes a valid entry fail to load. A fixed /bin/sh command
# invokes this script by a quoted argument, then --launch execs FileCat directly.
launcher=$(printf '%s' "$HERE/install-desktop-entry.sh" | escape_entry exec)
icon=$(printf '%s' "$HERE/filecat.png" | escape_entry icon)
mkdir -p "$APPS"
# Write values as data, without embedding them in sed replacement expressions.
while IFS= read -r line || [ -n "$line" ]; do
  case "$line" in
    Exec=*) printf 'Exec=/bin/sh "%s" --launch %%F\n' "$launcher" ;;
    Icon=*) printf 'Icon=%s\n' "$icon" ;;
    *) printf '%s\n' "$line" ;;
  esac
done < "$HERE/filecat.desktop" > "$APPS/filecat.desktop"
printf 'Added %s\n' "$APPS/filecat.desktop"
