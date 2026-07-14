#!/usr/bin/env bash

set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${scripts_directory}/common.sh"

if [[ $# -gt 1 ]]; then
  echo "Usage: $0 [unity-version]" >&2
  exit 2
fi

unity_version="${1:-2021.3.19f1}"
unity_executable="${UNITY_EXECUTABLE:-/Applications/Unity/Hub/Editor/${unity_version}/Unity.app/Contents/MacOS/Unity}"

case "${unity_version}" in
  2021.3.19f1)
    unity_revision="c9714fde33b6"
    test_framework_version="1.1.33"
    ;;
  *)
    echo "Add the editor revision and test-framework version for ${unity_version} before using this script." >&2
    exit 2
    ;;
esac

if [[ ! -x "${unity_executable}" ]]; then
  echo "Unity ${unity_version} is not installed at ${unity_executable}." >&2
  exit 2
fi
if ! command -v xmllint >/dev/null 2>&1; then
  echo "xmllint is required to verify Unity test results." >&2
  exit 2
fi

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/UnityOpenApiCodeGen-Base-${unity_version}.XXXXXX")"
project_path="${temporary_root}/UnityProject"
results="${temporary_root}/base-results.xml"
log="${temporary_root}/base.log"
trap 'rm -rf "${temporary_root}"' EXIT

mkdir -p \
  "${project_path}/Assets" \
  "${project_path}/Packages/OpenApiCodeGen" \
  "${project_path}/ProjectSettings"

# Install only the base package. The optional Unity 6 add-on must not mask
# compatibility regressions against the base package's declared minimum Unity.
rsync -a \
  "${repository_root}/Packages/OpenApiCodeGen/" \
  "${project_path}/Packages/OpenApiCodeGen/"

printf '%s\n' \
  '{' \
  '  "dependencies": {' \
  "    \"com.unity.test-framework\": \"${test_framework_version}\"" \
  '  }' \
  '}' \
  > "${project_path}/Packages/manifest.json"

printf 'm_EditorVersion: %s\nm_EditorVersionWithRevision: %s (%s)\n' \
  "${unity_version}" "${unity_version}" "${unity_revision}" \
  > "${project_path}/ProjectSettings/ProjectVersion.txt"

# ApplicationConstant resolves the settings directory from the process cwd.
cd "${project_path}"

"${unity_executable}" \
  -batchmode \
  -nographics \
  -projectPath "${project_path}" \
  -runTests \
  -testPlatform EditMode \
  -testResults "${results}" \
  -logFile "${log}"

require_file "${results}"
if [[ "$(xmllint --xpath 'string(/test-run/@result)' "${results}")" != "Passed" ]]; then
  echo "Unity ${unity_version} base-package EditMode tests did not pass." >&2
  xmllint \
    --xpath '//test-case[@result="Failed"]/@fullname | //test-case[@result="Failed"]/failure/message' \
    "${results}" >&2 || true
  exit 1
fi

echo "Unity ${unity_version} base-package verification passed."
echo "$(xmllint --xpath 'concat("total=",/test-run/@total," passed=",/test-run/@passed," failed=",/test-run/@failed)' "${results}")"
echo "Results: ${results}"
echo "Log: ${log}"
