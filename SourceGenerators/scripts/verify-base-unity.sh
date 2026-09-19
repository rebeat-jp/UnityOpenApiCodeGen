#!/usr/bin/env bash

# Run the base-package manual Unity gate against the release candidate tarball.
# This script intentionally does not use the repository's embedded package.
set -euo pipefail

scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${scripts_directory}/common.sh"

unity_version="2021.3.19f1"
pack_release_args=()
for argument in "$@"; do
  case "${argument}" in
    --allow-dirty)
      if [[ ${#pack_release_args[@]} -ne 0 ]]; then
        echo "Usage: $0 [unity-version] [--allow-dirty]" >&2
        exit 2
      fi
      pack_release_args=("--allow-dirty")
      ;;
    2021.3.19f1)
      unity_version="${argument}"
      ;;
    *)
      echo "Usage: $0 [unity-version] [--allow-dirty]" >&2
      exit 2
      ;;
  esac
done

unity_executable="${UNITY_EXECUTABLE:-/Applications/Unity/Hub/Editor/${unity_version}/Unity.app/Contents/MacOS/Unity}"
release_version="$(node -p "require('${repository_root}/Packages/OpenApiCodeGen/package.json').version")"
release_output="${SOURCE_GENERATOR_RELEASE_OUTPUT:-${repository_root}/artifacts/upm/${release_version}}"
base_archive="${release_output}/jp.rhycol.openapicodegen-${release_version}.tgz"
evidence_root="${SOURCE_GENERATOR_EVIDENCE_ROOT:-${release_output}/unity-gate/${unity_version}}"
unity_timeout_seconds="${SOURCE_GENERATOR_UNITY_TIMEOUT_SECONDS:-300}"

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

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/UnityOpenApiCodeGen-Base-${unity_version}.XXXXXX")"
project_path="${temporary_root}/UnityProject"
local_packages="${temporary_root}/LocalPackages"
results="${temporary_root}/base-results.xml"
log="${temporary_root}/base.log"
consumer_log="${temporary_root}/base-consumer.log"
gate_status="not-run"
gate_reason="base Unity gate did not start"

copy_evidence_file() {
  local source_path="$1"
  local evidence_name="$2"

  if [[ -f "${source_path}" ]]; then
    if [[ "${SOURCE_GENERATOR_CI:-0}" == "1" && ( "${evidence_name}" == *.log || "${evidence_name}" == *.xml ) ]]; then
      node "${scripts_directory}/ci-evidence.js" sanitize-file "${source_path}" "${evidence_root}/${evidence_name}"
    else
      cp "${source_path}" "${evidence_root}/${evidence_name}"
    fi
  fi
}

write_gate_evidence() {
  local exit_code="$1"

  mkdir -p "${evidence_root}"
  copy_evidence_file "${results}" "base-results.xml"
  copy_evidence_file "${log}" "base.log"
  copy_evidence_file "${consumer_log}" "base-consumer.log"

  if command -v node >/dev/null 2>&1; then
    node "${scripts_directory}/ci-evidence.js" write-gate \
      "${evidence_root}/gate.json" "${gate_status}" "${exit_code}" \
      "${unity_version}" "${gate_reason}" "${evidence_root}" "${release_output}"
  else
    printf '{\n  "schemaVersion": 1,\n  "status": "%s",\n  "version": "%s",\n  "exitCode": %s,\n  "reason": "manual evidence writer unavailable"\n}\n' \
      "${gate_status}" "${unity_version}" "${exit_code}" > "${evidence_root}/gate.json"
  fi
}

cleanup() {
  local exit_code=$?
  trap - EXIT
  write_gate_evidence "${exit_code}"
  if [[ "${SOURCE_GENERATOR_KEEP_TEMP:-0}" == "1" ]]; then
    echo "Preserved verification workspace: ${temporary_root}"
  else
    rm -rf "${temporary_root}"
  fi
  exit "${exit_code}"
}
trap cleanup EXIT

fail_not_run() {
  gate_status="not-run"
  gate_reason="$1"
  exit 2
}

fail_gate() {
  gate_status="failed"
  gate_reason="$1"
  exit 1
}

if [[ ! -x "${unity_executable}" ]]; then
  fail_not_run "Unity editor is not installed at ${unity_executable}"
fi
if ! command -v xmllint >/dev/null 2>&1; then
  fail_not_run "xmllint is unavailable"
fi
if ! command -v node >/dev/null 2>&1; then
  fail_not_run "node is unavailable for gate evidence"
fi
if [[ ! -f "${base_archive}" ]]; then
  if [[ "${SOURCE_GENERATOR_CI:-0}" == "1" ]]; then
    fail_not_run "the CI candidate tarball is missing; repacking is forbidden"
  fi
  if ! "${scripts_directory}/pack-release.sh" "${pack_release_args[@]}"; then
    fail_not_run "release-candidate packing failed before the Unity gate"
  fi
fi

rm -rf "${evidence_root}"
if [[ ! -f "${base_archive}" ]]; then
  fail_not_run "base release-candidate tarball is missing: ${base_archive}"
fi

mkdir -p \
  "${project_path}/Assets" \
  "${project_path}/Packages" \
  "${project_path}/ProjectSettings" \
  "${local_packages}"
base_archive_name="$(basename "${base_archive}")"
cp "${base_archive}" "${local_packages}/${base_archive_name}"

printf '%s\n' \
  '{' \
  '  "dependencies": {' \
  "    \"jp.rhycol.openapicodegen\": \"file:../../LocalPackages/${base_archive_name}\"" \
  '  }' \
  '}' \
  > "${project_path}/Packages/manifest.json"

printf 'm_EditorVersion: %s\nm_EditorVersionWithRevision: %s (%s)\n' \
  "${unity_version}" "${unity_version}" "${unity_revision}" \
  > "${project_path}/ProjectSettings/ProjectVersion.txt"

# ApplicationConstant resolves the settings directory from the process cwd.
cd "${project_path}"

run_editor() {
  local output_log="$1"
  shift

  rm -f "${output_log}"
  if command -v perl >/dev/null 2>&1; then
    perl -e 'alarm shift; exec @ARGV' "${unity_timeout_seconds}" "$@" \
      -logFile "${output_log}"
  else
    "$@" -logFile "${output_log}"
  fi
}

# A normal package consumer does not need the Unity Test Framework. Compile the
# exact candidate in that configuration before explicitly enabling package tests.
if run_editor "${consumer_log}" \
    "${unity_executable}" \
    -batchmode \
    -nographics \
    -projectPath "${project_path}" \
    -quit; then
  :
else
  unity_exit_code=$?
  if [[ "${unity_exit_code}" == "142" || "${unity_exit_code}" == "124" || ! -f "${consumer_log}" ]]; then
    fail_not_run "Unity consumer compilation timed out or produced no log (exit ${unity_exit_code})"
  fi
  fail_gate "base package failed to compile without the Unity Test Framework"
fi
if grep -Eq 'error CS[0-9]{4}|Scripts have compiler errors' "${consumer_log}"; then
  fail_gate "base package reported compiler errors without the Unity Test Framework"
fi
for assembly in Unity.OpenApiCodeGen.Editor.Contracts.dll Unity.OpenApiCodeGen.Editor.dll; do
  if [[ ! -f "${project_path}/Library/ScriptAssemblies/${assembly}" ]]; then
    fail_gate "base package consumer compilation did not produce ${assembly}"
  fi
done

printf '%s\n' \
  '{' \
  '  "dependencies": {' \
  "    \"com.unity.test-framework\": \"${test_framework_version}\"," \
  "    \"jp.rhycol.openapicodegen\": \"file:../../LocalPackages/${base_archive_name}\"" \
  '  },' \
  '  "testables": ["jp.rhycol.openapicodegen"]' \
  '}' \
  > "${project_path}/Packages/manifest.json"

if run_editor "${log}" \
    "${unity_executable}" \
    -batchmode \
    -nographics \
    -projectPath "${project_path}" \
    -runTests \
    -testPlatform EditMode \
    -testResults "${results}"; then
  :
else
  unity_exit_code=$?
  if [[ "${unity_exit_code}" == "142" || "${unity_exit_code}" == "124" || ! -f "${results}" ]]; then
    fail_not_run "Unity editor timed out or did not produce test XML (exit ${unity_exit_code})"
  fi
  test_result="$(xmllint --xpath 'string(/test-run/@result)' "${results}" 2>/dev/null || true)"
  if [[ "${test_result}" == Failed* ]]; then
    fail_gate "base Unity EditMode assertion failure"
  fi
  fail_not_run "Unity editor exited ${unity_exit_code} despite producing non-failed test XML"
fi

if [[ ! -f "${results}" ]]; then
  fail_not_run "Unity editor did not produce test XML"
fi
test_result="$(xmllint --xpath 'string(/test-run/@result)' "${results}" 2>/dev/null || true)"
case "${test_result}" in
  Passed)
    ;;
  Failed*)
    fail_gate "base Unity EditMode assertion failure"
    ;;
  *)
    fail_not_run "Unity test XML did not contain a usable result"
    ;;
esac

if grep -q 'CS8785' "${log}"; then
  fail_gate "Unity reported CS8785 while loading the base package"
fi

gate_status="passed"
gate_reason="base consumer compilation and Unity EditMode gate passed"
echo "Unity ${unity_version} base-package verification passed."
xmllint --xpath 'concat("total=",/test-run/@total," passed=",/test-run/@passed," failed=",/test-run/@failed)' "${results}"
printf '\n'
echo "Results: ${results}"
echo "Log: ${log}"
echo "Consumer log: ${consumer_log}"
