#!/bin/sh
# A bug fix needs a reproduction that fails on the base and passes on the head.
set -eu
script=".harness/repro/$HARNESS_PLAN/$HARNESS_CHECKPOINT.sh"
if [ ! -f "$script" ]; then
  echo "write the reproduction to $script; it must fail on base and pass on head" >&2
  exit 1
fi
# The repro's own output goes to stderr, which the gate log captures.
if ! sh "$script" >&2; then
  echo "repro fails on head" >&2
  exit 1
fi

tmp=$(mktemp -d)
trap 'git worktree remove --force "$tmp/base" >/dev/null 2>&1 || true; rm -rf "$tmp"' EXIT
git worktree add --detach "$tmp/base" "$HARNESS_BASE_REF" >&2

# New tests the fix added are part of the reproduction, so they run against the base too.
while IFS= read -r f; do
  case "$f" in
    test/* | tests/*)
      [ -f "$f" ] || continue
      git cat-file -e "$HARNESS_BASE_REF:$f" 2>/dev/null && continue
      mkdir -p "$tmp/base/$(dirname "$f")"
      cp "$f" "$tmp/base/$f"
      ;;
  esac
done <"$HARNESS_CHANGED"
mkdir -p "$tmp/base/$(dirname "$script")"
cp "$script" "$tmp/base/$script"

if (cd "$tmp/base" && sh "$script" >&2); then
  echo "repro passes on base $HARNESS_BASE_REF; expected it to fail there" >&2
  exit 1
fi
printf '{"summary":"fails on base, passes on head"}\n' >"${HARNESS_VERDICT:-/dev/null}"
