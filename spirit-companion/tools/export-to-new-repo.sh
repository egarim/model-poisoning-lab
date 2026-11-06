#!/usr/bin/env bash
# Move this folder into its own repository, with its history.
# Run from the root of model-poisoning-lab:  spirit-companion/tools/export-to-new-repo.sh https://github.com/egarim/little-souls
set -euo pipefail
URL="${1:?usage: export-to-new-repo.sh <empty repository URL>}"
git subtree split --prefix=spirit-companion -b little-souls-main
git push "$URL" little-souls-main:main
echo "pushed spirit-companion/ as the root of $URL (branch main)"
