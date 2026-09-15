#!/usr/bin/env bash

set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

node --test "${scripts_directory}/tests/"*.test.js
"${scripts_directory}/test.sh"
"${scripts_directory}/verify-reproducible-build.sh"
"${scripts_directory}/verify-analyzer-sync.sh" --no-build
"${scripts_directory}/verify-package.sh"
git -C "${scripts_directory}/../.." diff --check

echo "Source generator build, tests, reproducibility, sync, package, and diff checks passed."
