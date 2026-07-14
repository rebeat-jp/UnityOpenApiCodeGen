#!/usr/bin/env bash

set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

"${scripts_directory}/test.sh"
"${scripts_directory}/verify-reproducible-build.sh"
"${scripts_directory}/verify-analyzer-sync.sh" --no-build
"${scripts_directory}/verify-package.sh"

echo "Source generator build, tests, reproducibility, sync, and package checks passed."
