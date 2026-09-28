#!/usr/bin/env bash

set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${scripts_directory}/common.sh"

require_file "${analyzer_project}"

cd "${source_generators_root}"
build_analyzer_project "${analyzer_project}"
require_file "${analyzer_output}"
inspect_analyzer "${analyzer_output}"

echo "Built deterministic analyzer: ${analyzer_output}"
