#!/usr/bin/env bash
# Fetch Singular Framework (private) at a pinned commit into external/singular-framework, for the app module
# (dotnet/src/SingularFramework.Apps.LittleSouls). Needs read access to egarim/singular-framework: locally your git
# credentials; in CI a token in SINGULAR_TOKEN. Nothing in Singular is modified.
set -euo pipefail
REV="${SINGULAR_REV:-0c34f9c}"
DIR="$(cd "$(dirname "$0")/.." && pwd)/external/singular-framework"
URL="https://github.com/egarim/singular-framework"
if [ -n "${SINGULAR_TOKEN:-}" ]; then URL="https://x-access-token:${SINGULAR_TOKEN}@github.com/egarim/singular-framework"; fi
if [ ! -d "$DIR/.git" ]; then
  git clone --filter=blob:none "$URL" "$DIR"
fi
git -C "$DIR" fetch --quiet origin "$REV" 2>/dev/null || git -C "$DIR" fetch --quiet origin
git -C "$DIR" checkout --quiet "$REV"
echo "singular-framework at $(git -C "$DIR" rev-parse --short HEAD) in $DIR"
