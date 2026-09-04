#!/usr/bin/env bash

set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${scripts_directory}/common.sh"

require_file "${package_root}/package.json"
require_file "${package_analyzer}"
require_file "${package_analyzer}.meta"

if ! grep -F -x -q -- '- RoslynAnalyzer' "${package_analyzer}.meta"; then
  echo "Packaged analyzer metadata is missing the RoslynAnalyzer label." >&2
  exit 1
fi

# Bash 3 (the default on macOS) has neither mapfile nor readarray, so collect
# the package's controlled paths with a simple loop instead.
package_dlls=()
while IFS= read -r dll; do
  package_dlls+=("${dll}")
done < <(find "${package_root}" -type f -iname '*.dll' -print | LC_ALL=C sort)

if [[ ${#package_dlls[@]} -ne 1 ]]; then
  echo "The UPM package must contain exactly one DLL: ${package_analyzer}" >&2
  if [[ ${#package_dlls[@]} -gt 0 ]]; then
    printf 'Found: %s\n' "${package_dlls[@]}" >&2
  fi
  exit 1
fi

if [[ "${package_dlls[0]}" != "${package_analyzer}" ]]; then
  echo "The UPM package must contain exactly one DLL: ${package_analyzer}" >&2
  printf 'Found: %s\n' "${package_dlls[0]}" >&2
  exit 1
fi

for forbidden_name in \
  'Microsoft.CodeAnalysis.dll' \
  'Microsoft.CodeAnalysis.CSharp.dll' \
  'Newtonsoft.Json.dll' \
  'System.Text.Json.dll' \
  'YamlDotNet.dll'; do
  if find "${package_root}" -type f -iname "${forbidden_name}" -print -quit | grep -q .; then
    echo "Forbidden dependency DLL is bundled in the UPM package: ${forbidden_name}" >&2
    exit 1
  fi
done

inspect_analyzer "${package_analyzer}"

echo "Verified UPM analyzer contents and assembly references."
