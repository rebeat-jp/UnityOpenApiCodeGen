#!/usr/bin/env bash

set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${scripts_directory}/common.sh"

require_file "${tests_project}"

cd "${source_generators_root}"
dotnet test "${tests_project}" \
  --configuration Release \
  --nologo
