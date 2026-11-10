#!/bin/bash
# Create a reproducible tar.gz source packet of the current worktree (committed + uncommitted, non-ignored
# files) without moving HEAD, the index or the shared stash. A throwaway commit object pins the exact tree.
# Usage: make_source_packet.sh OUTPUT.tar.gz   -> prints "packet sha256 tree commit"
set -euo pipefail
OUT=$1
cd "$(git rev-parse --show-toplevel)"
INDEX=$(mktemp "${TMPDIR:-/tmp}/fc-packet-index.XXXXXX")
trap 'rm -f "$INDEX"' EXIT
GIT_INDEX_FILE="$INDEX" git read-tree HEAD
GIT_INDEX_FILE="$INDEX" git add -A -- .
TREE=$(GIT_INDEX_FILE="$INDEX" git write-tree)
COMMIT=$(git commit-tree "$TREE" -p HEAD -m "source packet for validation (not a branch commit)")
git archive --format=tar.gz -o "$OUT" "$COMMIT"
SHA=$(sha256sum "$OUT" | cut -d' ' -f1)
echo "$OUT $SHA $TREE $COMMIT"
