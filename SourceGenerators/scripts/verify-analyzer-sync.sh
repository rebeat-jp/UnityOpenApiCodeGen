#!/usr/bin/env bash

set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${scripts_directory}/common.sh"

case "$#:${1:-}" in
  0:)
    "${scripts_directory}/build.sh"
    ;;
  1:--no-build)
    ;;
  *)
    echo "Usage: $0 [--no-build]" >&2
    exit 2
    ;;
esac

require_file "${analyzer_output}"
require_file "${package_analyzer}"

if ! cmp -s "${analyzer_output}" "${package_analyzer}"; then
  echo "The packaged analyzer is stale. Run SourceGenerators/scripts/sync-analyzer.sh." >&2
  shasum -a 256 "${analyzer_output}" "${package_analyzer}" >&2
  exit 1
fi

echo "Verified that the packaged analyzer matches the deterministic Release build."
shasum -a 256 "${package_analyzer}"
