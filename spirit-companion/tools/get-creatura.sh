#!/usr/bin/env bash
# Fetch Creatura at a pinned commit into external/creatura (its packages are not on nuget.org yet).
# Creatura is PolyForm Noncommercial: commercial use needs the owner's written licence (docs/plan.md, Phase 0).
set -euo pipefail
REV="${CREATURA_REV:-b7df228}"
DIR="$(cd "$(dirname "$0")/.." && pwd)/external/creatura"
if [ ! -d "$DIR/.git" ]; then
  git clone --filter=blob:none https://github.com/egarim/creatura "$DIR"
fi
git -C "$DIR" fetch --quiet origin
git -C "$DIR" checkout --quiet "$REV"
echo "creatura at $(git -C "$DIR" rev-parse --short HEAD) in $DIR"
