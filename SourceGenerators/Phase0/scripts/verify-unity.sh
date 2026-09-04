#!/usr/bin/env bash
set -euo pipefail

unity_version="${1:-6000.3.2f1}"
phase0_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repo_root="$(cd "${phase0_root}/../.." && pwd)"
unity="/Applications/Unity/Hub/Editor/${unity_version}/Unity.app/Contents/MacOS/Unity"
results="${TMPDIR:-/tmp}/UnityOpenApiCodeGen-Phase0-${unity_version}-results.xml"
log="${TMPDIR:-/tmp}/UnityOpenApiCodeGen-Phase0-${unity_version}.log"
no_change_log="${TMPDIR:-/tmp}/UnityOpenApiCodeGen-Phase0-${unity_version}-no-change.log"
changed_log="${TMPDIR:-/tmp}/UnityOpenApiCodeGen-Phase0-${unity_version}-changed.log"

if [[ ! -x "${unity}" ]]; then
  echo "Unity ${unity_version} is not installed at ${unity}." >&2
  exit 2
fi
if ! command -v xmllint >/dev/null 2>&1; then
  echo "xmllint is required to verify Unity test results." >&2
  exit 2
fi

case "${unity_version}" in
  6000.0.23f1)
    revision="1c4764c07fb4"
    ;;
  6000.3.2f1)
    revision="a9779f353c9b"
    ;;
  *)
    echo "Add the installed editor revision for ${unity_version} before using this script." >&2
    exit 2
    ;;
esac

"${phase0_root}/scripts/test.sh"

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/UnityOpenApiCodeGen-Phase0.XXXXXX")"
project_path="${temporary_root}/UnityProject"
trap 'rm -rf "${temporary_root}"' EXIT

mkdir -p "${project_path}"
rsync -a \
  --exclude '.git' \
  --exclude 'Library' \
  --exclude 'Temp' \
  --exclude 'Logs' \
  --exclude 'obj' \
  --exclude 'bin' \
  "${repo_root}/" "${project_path}/"

printf 'm_EditorVersion: %s\nm_EditorVersionWithRevision: %s (%s)\n' \
  "${unity_version}" "${unity_version}" "${revision}" \
  > "${project_path}/ProjectSettings/ProjectVersion.txt"

if [[ "${unity_version}" == "6000.0.23f1" ]]; then
  printf '%s\n' \
    '{' \
    '  "dependencies": {' \
    '    "com.unity.test-framework": "1.4.5"' \
    '  }' \
    '}' \
    > "${project_path}/Packages/manifest.json"
  rm -f "${project_path}/Packages/packages-lock.json"
fi

rm -f "${results}" "${log}" "${no_change_log}" "${changed_log}"
"${unity}" \
  -batchmode \
  -nographics \
  -projectPath "${project_path}" \
  -runTests \
  -testPlatform EditMode \
  -testResults "${results}" \
  -logFile "${log}"

if [[ "$(xmllint --xpath 'string(/test-run/@result)' "${results}")" != "Passed" ]]; then
  echo "Unity test run did not pass." >&2
  exit 1
fi

for test_name in \
  'Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.Tests.SourceGeneratorPhase0CompilationPipelineTests.AnalyzerAndAdditionalFilesFollowAsmdefReferenceScope' \
  'Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.Tests.SourceGeneratorPhase0Tests.GeneratedCodeContainsFixedAndAdditionalFileValues'
do
  result="$(xmllint --xpath "string(//test-case[@fullname='${test_name}']/@result)" "${results}")"
  if [[ "${result}" != "Passed" ]]; then
    echo "Unity test did not pass: ${test_name} (${result:-missing})" >&2
    exit 1
  fi
done

"${unity}" \
  -batchmode \
  -nographics \
  -projectPath "${project_path}" \
  -quit \
  -logFile "${no_change_log}"
if grep -q '] Csc ' "${no_change_log}"; then
  echo "An unchanged Unity reopen unexpectedly ran a C# compiler action." >&2
  grep '] Csc ' "${no_change_log}" >&2
  exit 1
fi

printf 'changed-root-value\n' \
  > "${project_path}/Assets/SourceGeneratorPhase0/Root.Rhycol.OpenApiCodeGen.SourceGenerator.Phase0.additionalfile"
"${unity}" \
  -batchmode \
  -nographics \
  -projectPath "${project_path}" \
  -quit \
  -logFile "${changed_log}"

for assembly in \
  'Unity.OpenApiCodeGen.SourceGenerator.Phase0.dll' \
  'Unity.OpenApiCodeGen.SourceGenerator.Phase0.Tests.dll'
do
  if ! grep -q "] Csc .*${assembly}" "${changed_log}"; then
    echo "Changed AdditionalFile did not recompile expected assembly: ${assembly}" >&2
    exit 1
  fi
done

for assembly in \
  'Unity.OpenApiCodeGen.SourceGenerator.Phase0.Unrelated.dll' \
  'Assembly-CSharp-Editor.dll'
do
  if grep -q "] Csc .*${assembly}" "${changed_log}"; then
    echo "Changed AdditionalFile recompiled excluded assembly: ${assembly}" >&2
    exit 1
  fi
done

echo "Unity ${unity_version} Phase 0 EditMode tests passed."
echo "Results: ${results}"
echo "Log: ${log}"
echo "No-change log: ${no_change_log}"
echo "Changed-input log: ${changed_log}"
