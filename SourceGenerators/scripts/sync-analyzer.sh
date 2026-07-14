#!/usr/bin/env bash

set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${scripts_directory}/common.sh"

"${scripts_directory}/build.sh"

if [[ ! -d "${package_root}" ]]; then
  echo "Production source-generator package does not exist: ${package_root}" >&2
  exit 1
fi

mkdir -p "${package_analyzers}"
if [[ -f "${package_analyzer}" ]] && cmp -s "${analyzer_output}" "${package_analyzer}"; then
  echo "Packaged analyzer is already synchronized: ${package_analyzer}"
  exit 0
fi

temporary_assembly="$(mktemp "${package_analyzers}/.${analyzer_assembly}.XXXXXX")"
trap 'rm -f "${temporary_assembly}"' EXIT
cp "${analyzer_output}" "${temporary_assembly}"
chmod 0644 "${temporary_assembly}"
mv -f "${temporary_assembly}" "${package_analyzer}"

echo "Synchronized analyzer into the embedded package: ${package_analyzer}"
